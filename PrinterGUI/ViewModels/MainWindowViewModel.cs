using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PrinterGUI.Models;
using PrinterGUI.Services;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace PrinterGUI.ViewModels
{
    public class MainWindowViewModel : INotifyPropertyChanged, IDisposable
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public ObservableCollection<PrintObject> Objects { get; } = new ObservableCollection<PrintObject>();

        // NEW: print type list on the left
        public ObservableCollection<string> PrintTypes { get; } = new() { "ODF", "Gummies" };

        string? _selectedPrintType = "ODF";
        public string? SelectedPrintType
        {
            get => _selectedPrintType;
            set
            {
                _selectedPrintType = value;
                Notify(nameof(SelectedPrintType));
                Notify(nameof(IsOdf));
                Notify(nameof(IsGummies));
                ApplyDefaultModelForType();
                UpdateCanSend();
            }
        }

        public bool IsOdf => SelectedPrintType == "ODF";
        public bool IsGummies => SelectedPrintType == "Gummies";

        PrintObject? _selectedObject;
        public PrintObject? SelectedObject { get => _selectedObject; set { _selectedObject = value; Notify(nameof(SelectedObject)); UpdateCanSend(); } }

        public string SerialPortPath { get; set; } = "/dev/ttyACM0";

        // ODF parameters
        public string ExtruderTemp { get; set; } = "0";
        public string DryingTemp { get; set; } = "0";
        public string LayerHeight { get; set; } = "0.3";
        public string Infill { get; set; } = "90";
        public string PrintSpeed { get; set; } = "11.5";
        public string DryingTime { get; set; } = "0";
        public string DryingTimeRT { get; set; } = "0";

        // NEW: ODF rectangle size (mm)
        public string OdfWidthMm { get; set; } = "20";
        public string OdfLengthMm { get; set; } = "30";

        public string OdfFilmCount { get; set; } = "1";

        // NEW: Gummies-specific fields
        public string GummiesMlPerGummy { get; set; } = "1";
        public string GummiesMmPerMl { get; set; } = "5";
        public string GummiesWaitBetweenSeconds { get; set; } = "5";
        public string GummiesExtrusionSpeed { get; set; } = "600";

        public string PrusaSlicerPath { get; set; } = "prusa-slicer";

        string _status = "Idle";
        public string Status { get => _status; set { _status = value; Notify(nameof(Status)); } }

        int _progress;
        public int Progress { get => _progress; set { _progress = value; Notify(nameof(Progress)); } }

        bool _isSending;
        public bool IsSending { get => _isSending; set { _isSending = value; Notify(nameof(IsSending)); UpdateCanSend(); } }

        public bool CanSend => (IsOdf || IsGummies) ? !IsSending : SelectedObject != null && !IsSending;

        public bool CanPrintCustomGcode => !IsSending;

        void UpdateCanSend()
        {
            Notify(nameof(CanSend));
            Notify(nameof(CanPrintCustomGcode));
            Notify(nameof(CanStop));

            if (SendToPrinterCommand is RelayCommand sendCmd)
                sendCmd.RaiseCanExecuteChanged();

            if (StopPrintCommand is RelayCommand stopCmd)
                stopCmd.RaiseCanExecuteChanged();
        }

        public ICommand SendToPrinterCommand { get; }

        readonly SerialPrinterService _serial = new SerialPrinterService();

        CancellationTokenSource? _cts;

        readonly string _modelsFolder = "/home/raspberrypie/stl";
        FileSystemWatcher? _watcher;
        readonly SynchronizationContext? _uiContext;
        Timer? _debounceTimer;

        public MainWindowViewModel()
        {
            _uiContext = SynchronizationContext.Current;

            LoadModels();
            StartWatchingModelsFolder();

            SendToPrinterCommand = new RelayCommand(async _ => await SendToPrinterAsync(), _ => CanSend);
            StopPrintCommand = new RelayCommand(async _ => await StopPrintAsync(), _ => CanStop);
        }

        void ApplyDefaultModelForType()
        {
            if (Objects.Count == 0 || string.IsNullOrWhiteSpace(SelectedPrintType))
                return;

            var match = Objects.FirstOrDefault(o =>
                o.Name.Contains(SelectedPrintType, StringComparison.OrdinalIgnoreCase));

            SelectedObject = match ?? Objects.FirstOrDefault();
        }

        void StartWatchingModelsFolder()
        {
            try
            {
                if (!Directory.Exists(_modelsFolder))
                    return;

                _watcher = new FileSystemWatcher(_modelsFolder, "*.stl")
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
                };

                FileSystemEventHandler handler = (s, e) => DebounceLoad();
                RenamedEventHandler renHandler = (s, e) => DebounceLoad();

                _watcher.Created += handler;
                _watcher.Deleted += handler;
                _watcher.Changed += handler;
                _watcher.Renamed += renHandler;
                _watcher.EnableRaisingEvents = true;

                _debounceTimer = new Timer(_ => LoadModels(), null, Timeout.Infinite, Timeout.Infinite);
            }
            catch { }
        }

        void DebounceLoad()
        {
            try { _debounceTimer?.Change(300, Timeout.Infinite); } catch { }
        }

        void LoadModels()
        {
            try
            {
                string[] files = Array.Empty<string>();
                if (Directory.Exists(_modelsFolder))
                    files = Directory.GetFiles(_modelsFolder, "*.stl", SearchOption.TopDirectoryOnly);

                void Action()
                {
                    Objects.Clear();
                    foreach (var f in files.OrderBy(Path.GetFileName))
                    {
                        Objects.Add(new PrintObject
                        {
                            Name = Path.GetFileNameWithoutExtension(f),
                            FileName = f,
                            WidthMm = 0,
                            DepthMm = 0,
                            HeightMm = 0
                        });
                    }

                    ApplyDefaultModelForType();
                }

                if (_uiContext != null) _uiContext.Post(_ => Action(), null);
                else Action();
            }
            catch { }
        }

        // Generate temp gcode, send to printer, then delete the temp file and any older generated files for the selected model
        async Task SendToPrinterAsync()
        {
            if (!IsOdf && !IsGummies && SelectedObject == null)
            {
                Status = "Select an object first";
                return;
            }

            if (!TryParseInputs(out int extruderTemp, out int dryingTemp, out double layerHeight, out int infill, out double printSpeed))
                return;

            string stlPath = string.Empty;
            bool cleanupByModel = !IsOdf && !IsGummies;

            if (IsOdf)
            {
                if (!double.TryParse(OdfWidthMm, NumberStyles.Float, CultureInfo.InvariantCulture, out var widthMm) || widthMm <= 0)
                {
                    Status = "Invalid ODF width";
                    return;
                }

                if (!double.TryParse(OdfLengthMm, NumberStyles.Float, CultureInfo.InvariantCulture, out var lengthMm) || lengthMm <= 0)
                {
                    Status = "Invalid ODF length";
                    return;
                }

                if (!double.TryParse(LayerHeight, NumberStyles.Float, CultureInfo.InvariantCulture, out var thicknessMm) || thicknessMm <= 0)
                {
                    Status = "Invalid layer height";
                    return;
                }

                if (!int.TryParse(OdfFilmCount, out var filmCount) || filmCount < 1 || filmCount > OdfMaxFilms)
                {
                    Status = $"Invalid ODF film count (1-{OdfMaxFilms})";
                    return;
                }

                if (!TryBuildOdfFilmOrigins(widthMm, lengthMm, filmCount, out var filmOrigins, out var layoutError))
                {
                    Status = layoutError;
                    return;
                }

                stlPath = Path.Combine(Path.GetTempPath(), $"odf_rect_{Guid.NewGuid():N}.stl");
                WriteOdfLayoutStl(stlPath, widthMm, lengthMm, thicknessMm, filmOrigins);
            }
            else if (!IsGummies)
            {
                stlPath = SelectedObject!.FileName;
                if (!Path.IsPathRooted(stlPath))
                    stlPath = Path.Combine(_modelsFolder, stlPath);
            }

            // create temp gcode via slicer
            string tempPath = Path.Combine(Path.GetTempPath(), $"print_{Guid.NewGuid():N}.gcode");
            IsSending = true;
            _cts = new CancellationTokenSource();
            Progress = 0;
            Status = IsGummies ? "Generating Gummies G-code..." : "Slicing (PrusaSlicer) before send...";

            var slicerProgress = new Progress<string>(s =>
            {
                if (!s.StartsWith("G") && !s.StartsWith("M") && !s.StartsWith(">") && !s.StartsWith(";"))
                    Status = s;
            });

            try
            {
                if (IsGummies)
                {
                    if (!double.TryParse(GummiesMlPerGummy, NumberStyles.Float, CultureInfo.InvariantCulture, out var mlPerGummy) || mlPerGummy <= 0)
                    {
                        Status = "Invalid ml/gummy (ml)";
                        IsSending = false;
                        return;
                    }

                    if (!double.TryParse(GummiesMmPerMl, NumberStyles.Float, CultureInfo.InvariantCulture, out var mmPerMl) || mmPerMl <= 0)
                    {
                        Status = "Invalid mm/ml";
                        IsSending = false;
                        return;
                    }

                    if (!int.TryParse(GummiesWaitBetweenSeconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out var waitSeconds) || waitSeconds < 0)
                    {
                        Status = "Invalid wait between gummies (s)";
                        IsSending = false;
                        return;
                    }

                    if (!int.TryParse(GummiesExtrusionSpeed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var extrusionSpeedPercent) || extrusionSpeedPercent <= 0)
                    {
                        Status = "Invalid extrusion speed";
                        IsSending = false;
                        return;
                    }

                    var extrusionAmount = mlPerGummy * mmPerMl;

                    Dictionary<int, (double X, double Y)[]> blisterPointsMap;
                    try
                    {
                        blisterPointsMap = await LoadGummiesPointsAsync(GummiesPointsJsonPath, _cts.Token);
                    }
                    catch (Exception ex)
                    {
                        Status = "Gummies points file error: " + ex.Message;
                        IsSending = false;
                        return;
                    }

                    var selectedBlisters = GetSelectedBlisters();
                    if (selectedBlisters.Count == 0)
                    {
                        Status = "Select at least one blister";
                        IsSending = false;
                        return;
                    }

                    var points = new List<(double X, double Y)>();
                    foreach (var blisterIndex in selectedBlisters)
                    {
                        if (!blisterPointsMap.TryGetValue(blisterIndex, out var blisterPoints))
                        {
                            Status = $"Blister {blisterIndex} is selected but not defined in JSON";
                            IsSending = false;
                            return;
                        }

                        points.AddRange(blisterPoints);
                    }

                    var gummiesGcode = BuildGummiesGcode(extrusionAmount, waitSeconds, extrusionSpeedPercent, points);
                    await File.WriteAllTextAsync(tempPath, gummiesGcode, _cts.Token);

                    try
                    {
                        string gcodeFolder = "/home/raspberrypie/gcode";
                        if (!Directory.Exists(gcodeFolder))
                            Directory.CreateDirectory(gcodeFolder);

                        string copyPath = Path.Combine(gcodeFolder, $"gummies_{DateTime.Now:yyyyMMdd_HHmmss}.gcode");
                        File.Copy(tempPath, copyPath, overwrite: true);

                        _uiContext?.Post(_ =>
                        {
                            Status = $"G-code saved: {Path.GetFileName(copyPath)}. Starting send...";
                        }, null);
                    }
                    catch (Exception ex)
                    {
                        _uiContext?.Post(_ =>
                        {
                            Status = $"Warning: G-code copy failed ({ex.Message}). Continuing with send...";
                        }, null);
                        await Task.Delay(1000);
                    }
                }
                else
                {
                    int dryingTime = 0;
                    if (!int.TryParse(DryingTime, out dryingTime))
                    {
                        Status = "Invalid drying time";
                        IsSending = false;
                        return;
                    }

                    int dryingTimeRT = 0;
                    if (!int.TryParse(DryingTimeRT, out dryingTimeRT))
                    {
                        Status = "Invalid drying time RT";
                        IsSending = false;
                        return;
                    }

                    var resultPath = await GcodeGenerator.SliceWithPrusaAsync(
                        stlPath,
                        tempPath,
                        layerHeight,
                        infill,
                        extruderTemp,
                        dryingTemp,
                        dryingTime,
                        dryingTimeRT,
                        printSpeed,
                        prusaSlicerPath: PrusaSlicerPath,
                        profilePath: "/home/raspberrypie/config.ini",
                        extraArgs: null,
                        timeout: TimeSpan.FromMinutes(12),
                        outputProgress: slicerProgress,
                        cancellationToken: _cts.Token);

                    if (!resultPath.Success)
                    {
                        Status = "Slicing failed: " + (string.IsNullOrWhiteSpace(resultPath.Error) ? resultPath.Output : resultPath.Error);
                        IsSending = false;
                        return;
                    }

                    try
                    {
                        string gcodeFolder = "/home/raspberrypie/gcode";
                        if (!Directory.Exists(gcodeFolder))
                            Directory.CreateDirectory(gcodeFolder);

                        string modelName = Path.GetFileNameWithoutExtension(stlPath);
                        string copyPath = Path.Combine(gcodeFolder, $"{modelName}_{DateTime.Now:yyyyMMdd_HHmmss}.gcode");

                        File.Copy(tempPath, copyPath, overwrite: true);

                        _uiContext?.Post(_ =>
                        {
                            Status = $"G-code saved: {Path.GetFileName(copyPath)}. Starting send...";
                        }, null);
                    }
                    catch (Exception ex)
                    {
                        _uiContext?.Post(_ =>
                        {
                            Status = $"Warning: G-code copy failed ({ex.Message}). Continuing with send...";
                        }, null);
                        await Task.Delay(1000);
                    }
                }
            }
            catch (Exception ex)
            {
                Status = (IsGummies ? "G-code generation failed: " : "Slicing failed: ") + ex.Message;
                IsSending = false;
                _cts?.Dispose();
                _cts = null;
                return;
            }

            Status = "Opening serial port...";
            var progText = new Progress<string>(s =>
            {
                if (!s.StartsWith("G") && !s.StartsWith("M") && !s.StartsWith(">") && !s.StartsWith(";"))
                    Status = s;
            });
            var progPercent = new Progress<int>(p => Progress = p);

            bool sendSucceeded = false;
            try
            {
                await _serial.SendFileAsync(tempPath, SerialPortPath, 115200, progText, progPercent, _cts.Token);
                Status = "Send complete";
                sendSucceeded = true;
            }
            catch (OperationCanceledException)
            {
                Status = "Send stopped";
            }
            catch (Exception ex)
            {
                Status = "Send failed: " + ex.Message;
            }
            finally
            {
                IsSending = false;
                _cts?.Dispose();
                _cts = null;

                try { File.Delete(tempPath); } catch { }

                if (IsOdf && !string.IsNullOrWhiteSpace(stlPath))
                {
                    try { File.Delete(stlPath); } catch { }
                }

                if (sendSucceeded && cleanupByModel && !string.IsNullOrWhiteSpace(stlPath))
                {
                    _ = Task.Run(async () =>
                    {
                        await DeleteOldGeneratedFilesAsync(stlPath).ConfigureAwait(false);
                    });
                }
            }
        }

        async Task DeleteOldGeneratedFilesAsync(string stlPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(stlPath) ?? _modelsFolder;
                string baseName = Path.GetFileNameWithoutExtension(stlPath);

                if (!Directory.Exists(dir))
                    return;

                // Generated-file pattern: BaseName_yyyyMMdd_HHmmss.gcode
                var pattern = new Regex("^" + Regex.Escape(baseName) + @"_(\d{8}_\d{6})\.gcode$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

                var candidates = Directory.GetFiles(dir, baseName + "_*.gcode", SearchOption.TopDirectoryOnly);
                var parsed = new List<(string Path, DateTime TimeUtc)>(candidates.Length);

                foreach (var f in candidates)
                {
                    var fn = Path.GetFileName(f);
                    var m = pattern.Match(fn);
                    if (m.Success)
                    {
                        if (DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
                        {
                            parsed.Add((f, dt));
                            continue;
                        }
                    }

                    // fallback to file last write time (UTC)
                    try
                    {
                        var t = File.GetLastWriteTimeUtc(f);
                        parsed.Add((f, t));
                    }
                    catch
                    {
                        // if we can't read time, assign MinValue so it will be deleted first
                        parsed.Add((f, DateTime.MinValue));
                    }
                }

                // Keep the most recent N entries, delete older ones
                const int KeepCount = 75;
                var toDelete = parsed
                    .OrderByDescending(x => x.TimeUtc)
                    .Skip(KeepCount)
                    .Select(x => x.Path)
                    .ToList();

                var deleted = new List<string>();
                foreach (var f in toDelete)
                {
                    try
                    {
                        if (File.Exists(f))
                        {
                            File.Delete(f);
                            deleted.Add(f);
                        }
                    }
                    catch { /* best-effort */ }

                    // delete common sidecars if present (.cmdline.txt, .postproc.json, .meta.json)
                    foreach (var side in new[] { ".cmdline.txt", ".postproc.json", ".meta.json" })
                    {
                        try
                        {
                            var sf = f + "." + side.TrimStart('.'); // f + ".cmdline.txt" etc.
                            if (File.Exists(sf))
                            {
                                File.Delete(sf);
                                deleted.Add(sf);
                            }
                        }
                        catch { /* best-effort */ }
                    }

                    // handle backup name variants:
                    // 1) <file>.gcode.orig.gcode  (created by some backups)
                    // 2) <file>.orig.gcode
                    // 3) <file without .gcode>.orig.gcode
                    try
                    {
                        var candidate1 = f + ".orig.gcode"; // produces file.gcode.orig.gcode
                        if (File.Exists(candidate1))
                        {
                            File.Delete(candidate1);
                            deleted.Add(candidate1);
                        }
                    }
                    catch { /* best-effort */ }

                    try
                    {
                        var candidate2 = Path.ChangeExtension(f, ".orig.gcode"); // produces file.orig.gcode
                        if (candidate2 != null && File.Exists(candidate2))
                        {
                            File.Delete(candidate2);
                            deleted.Add(candidate2);
                        }
                    }
                    catch { /* best-effort */ }
                }

                if (deleted.Count > 0)
                {
                    _uiContext?.Post(_ =>
                    {
                        Status = $"Deleted {deleted.Count} generated file(s)";
                        Progress = 0;
                    }, null);
                }
            }
            catch
            {
                // ignore
            }
        }

        async Task CleanupGcodeArchiveAsync()
        {
            try
            {
                string gcodeFolder = "/home/raspberrypie/gcode";
                
                if (!Directory.Exists(gcodeFolder))
                    return;

                // Get all .gcode files
                var files = Directory.GetFiles(gcodeFolder, "*.gcode", SearchOption.TopDirectoryOnly);
                
                if (files.Length <= 100)
                    return; // Nothing to delete

                // Sort by last write time (most recent first)
                var sorted = files
                    .Select(f => new { Path = f, Time = File.GetLastWriteTimeUtc(f) })
                    .OrderByDescending(x => x.Time)
                    .ToList();

                // Keep the 50 most recent, delete the rest
                var toDelete = sorted.Skip(100).Select(x => x.Path).ToList();

                int deletedCount = 0;
                foreach (var file in toDelete)
                {
                    try
                    {
                        File.Delete(file);
                        deletedCount++;
                    }
                    catch { /* best-effort */ }
                }

                if (deletedCount > 0)
                {
                    _uiContext?.Post(_ =>
                    {
                        Status = $"Archived G-code. Cleaned up {deletedCount} old file(s)";
                    }, null);
                }
            }
            catch
            {
                // ignore errors
            }
        }

        public bool CanStop => IsSending;
        public ICommand StopPrintCommand { get; }

        bool TryParseInputs(out int extruderTemp, out int dryingTemp, out double layerHeight, out int infill, out double printSpeed)
        {
            extruderTemp = 0; dryingTemp = 0; layerHeight = 0; infill = 0; printSpeed = 0;

            if (IsGummies)
            {
                // Gummies fields mapped to existing pipeline variables
                if (!double.TryParse(GummiesMlPerGummy, NumberStyles.Float, CultureInfo.InvariantCulture, out layerHeight))
                {
                    Status = "Invalid ml/gummy (ml)";
                    return false;
                }

                if (!int.TryParse(GummiesWaitBetweenSeconds, out dryingTemp))
                {
                    Status = "Invalid wait between gummies (s)";
                    return false;
                }

                if (!int.TryParse(GummiesExtrusionSpeed, out infill))
                {
                    Status = "Invalid extrusion speed";
                    return false;
                }

                // not used by gummies flow, but required downstream
                printSpeed = 0;
                return true;
            }

            // ODF (existing behavior)
            if (!int.TryParse(ExtruderTemp, out extruderTemp)) { Status = "Invalid extruder temp"; return false; }
            if (!int.TryParse(DryingTemp, out dryingTemp)) { Status = "Invalid drying temp"; return false; }
            if (!double.TryParse(LayerHeight, NumberStyles.Float, CultureInfo.InvariantCulture, out layerHeight)) { Status = "Invalid layer height"; return false; }
            if (!int.TryParse(Infill, out infill)) { Status = "Invalid infill"; return false; }
            if (!double.TryParse(PrintSpeed, NumberStyles.Float, CultureInfo.InvariantCulture, out printSpeed)) { Status = "Invalid print speed"; return false; }

            return true;
        }

        // Simple ICommand implementation
        class RelayCommand : ICommand
        {
            readonly Func<object?, Task>? _executeAsync;
            readonly Predicate<object?>? _canExecute;
            public RelayCommand(Func<object?, Task> executeAsync, Predicate<object?>? canExecute = null)
            {
                _executeAsync = executeAsync;
                _canExecute = canExecute;
            }

            public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
            public event EventHandler? CanExecuteChanged;
            public async void Execute(object? parameter)
            {
                try
                {
                    await (_executeAsync?.Invoke(parameter) ?? Task.CompletedTask);
                }
                catch (OperationCanceledException)
                {
                    // Expected when STOP cancels the current print.
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Command execution error: {ex}");
                    // Prevent app shutdown from async-void unhandled exceptions.
                }
            }
            public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            try
            {
                _watcher?.Dispose();
                _debounceTimer?.Dispose();
            }
            catch { }
        }

        public async Task SendCustomGcodeFileAsync(string gcodePath)
        {
            if (string.IsNullOrWhiteSpace(gcodePath) || !File.Exists(gcodePath))
            {
                Status = "Selected G-code file not found";
                return;
            }

            if (IsSending)
            {
                Status = "Already sending a file";
                return;
            }

            IsSending = true;
            _cts = new CancellationTokenSource();
            Progress = 0;
            Status = $"Sending custom G-code: {Path.GetFileName(gcodePath)}";

            var progText = new Progress<string>(s =>
            {
                if (!s.StartsWith("G") && !s.StartsWith("M") && !s.StartsWith(">") && !s.StartsWith(";"))
                    Status = s;
            });
            var progPercent = new Progress<int>(p => Progress = p);

            try
            {
                await _serial.SendFileAsync(gcodePath, SerialPortPath, 115200, progText, progPercent, _cts.Token);
                Status = "Custom G-code send complete";
            }
            catch (OperationCanceledException)
            {
                Status = "Custom G-code send stopped";
            }
            catch (Exception ex)
            {
                Status = "Custom G-code send failed: " + ex.Message;
            }
            finally
            {
                IsSending = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private static bool TryBuildOdfFilmOrigins(
            double widthMm,
            double lengthMm,
            int filmCount,
            out List<(double x, double y)> origins,
            out string error)
        {
            origins = new List<(double x, double y)>(filmCount);
            error = string.Empty;

            if (filmCount < 1 || filmCount > OdfMaxFilms)
            {
                error = $"ODF film count must be between 1 and {OdfMaxFilms}.";
                return false;
            }

            // 1st film: top-left corner
            var firstY = OdfBedYMaxMm - lengthMm;
            if (firstY < 0)
            {
                error = $"Film length ({lengthMm} mm) exceeds bed Y ({OdfBedYMaxMm} mm).";
                return false;
            }
            origins.Add((0.0, firstY));

            if (filmCount >= 2)
            {
                // 2nd film: bottom-left corner
                origins.Add((0.0, 0.0));
            }

            // 3rd+ films: same Y as second, moving +X with 10 mm spacing
            for (int i = 3; i <= filmCount; i++)
            {
                var x = (i - 2) * (widthMm + OdfGapMm);
                origins.Add((x, 0.0));
            }

            // bounds check
            foreach (var (x, y) in origins)
            {
                if (x < 0 || y < 0 || (x + widthMm) > OdfBedXMaxMm || (y + lengthMm) > OdfBedYMaxMm)
                {
                    error = $"ODF layout does not fit bed ({OdfBedXMaxMm} x {OdfBedYMaxMm} mm) with {filmCount} film(s).";
                    return false;
                }
            }

            return true;
        }

        private static void WriteOdfLayoutStl(
            string path,
            double width,
            double length,
            double height,
            IReadOnlyList<(double x, double y)> origins)
        {
            using var w = new StreamWriter(path, false);
            w.WriteLine("solid odf_layout");

            foreach (var (ox, oy) in origins)
            {
                WriteBoxAt(w, ox, oy, width, length, height);
            }

            w.WriteLine("endsolid odf_layout");
        }

        private static void WriteBoxAt(StreamWriter w, double ox, double oy, double width, double length, double height)
        {
            var x0 = ox;              var x1 = ox + width;
            var y0 = oy;              var y1 = oy + length;
            var z0 = 0.0;             var z1 = height;

            void Tri((double x, double y, double z) a, (double x, double y, double z) b, (double x, double y, double z) c)
            {
                w.WriteLine("facet normal 0 0 0");
                w.WriteLine("  outer loop");
                w.WriteLine($"    vertex {a.x.ToString(CultureInfo.InvariantCulture)} {a.y.ToString(CultureInfo.InvariantCulture)} {a.z.ToString(CultureInfo.InvariantCulture)}");
                w.WriteLine($"    vertex {b.x.ToString(CultureInfo.InvariantCulture)} {b.y.ToString(CultureInfo.InvariantCulture)} {b.z.ToString(CultureInfo.InvariantCulture)}");
                w.WriteLine($"    vertex {c.x.ToString(CultureInfo.InvariantCulture)} {c.y.ToString(CultureInfo.InvariantCulture)} {c.z.ToString(CultureInfo.InvariantCulture)}");
                w.WriteLine("  endloop");
                w.WriteLine("endfacet");
            }

            // bottom
            Tri((x0, y0, z0), (x1, y0, z0), (x1, y1, z0));
            Tri((x0, y0, z0), (x1, y1, z0), (x0, y1, z0));

            // top
            Tri((x0, y0, z1), (x1, y1, z1), (x1, y0, z1));
            Tri((x0, y0, z1), (x0, y1, z1), (x1, y1, z1));

            // front
            Tri((x0, y0, z0), (x1, y0, z1), (x1, y0, z0));
            Tri((x0, y0, z0), (x0, y0, z1), (x1, y0, z1));

            // back
            Tri((x0, y1, z0), (x1, y1, z0), (x1, y1, z1));
            Tri((x0, y1, z0), (x1, y1, z1), (x0, y1, z1));

            // left
            Tri((x0, y0, z0), (x0, y1, z0), (x0, y1, z1));
            Tri((x0, y0, z0), (x0, y1, z1), (x0, y0, z1));

            // right
            Tri((x1, y0, z0), (x1, y1, z1), (x1, y1, z0));
            Tri((x1, y0, z0), (x1, y0, z1), (x1, y1, z1));
        }

        private static string BuildGummiesGcode(
            double extrusionAmount,
            int waitSeconds,
            int extrusionSpeedPercent,
            IReadOnlyList<(double X, double Y)> points)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("T1\t\t; select E1 (oven door)");
            sb.AppendLine("G92 E0");
            sb.AppendLine("G1 E-17 F1000\t; open oven door");
            sb.AppendLine("T0\t\t; select E0 (extruder)");
            sb.AppendLine("G28\t\t; home all axes");
            sb.AppendLine();
            sb.AppendLine("; Filament gcode");
            sb.AppendLine();
            sb.AppendLine($"M221 S100\t; velocità estrusione");
            sb.AppendLine("G21 \t\t; set units to millimeters");
            sb.AppendLine("G90 \t\t; use absolute coordinates");
            sb.AppendLine("M82 \t\t; use absolute distances for extrusion");
            sb.AppendLine("G92 E0");
            sb.AppendLine();
            sb.AppendLine("; layer change");
            sb.AppendLine();

            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                sb.AppendLine("G92 E0");
                sb.AppendLine("G1 E-1 F1000");
                sb.AppendLine($"G1 X{p.X.ToString("0.###", ci)} Y{p.Y.ToString("0.###", ci)} F4998.000\t; {i + 1}");
                sb.AppendLine("G1 Z0 F800");
                sb.AppendLine($"G1 E{extrusionAmount.ToString("0.###", ci)} F{extrusionSpeedPercent}");
                sb.AppendLine("G92 E0");
                sb.AppendLine("G1 E-1 F1000");
                sb.AppendLine($"G4 S{waitSeconds}");
                sb.AppendLine("G1 Z3 F800");
            }

            sb.AppendLine();
            sb.AppendLine("; final steps");
            sb.AppendLine();
            sb.AppendLine("G28 X0 Y0");
            sb.AppendLine("T1\t\t; select E1 (oven door)");
            sb.AppendLine("G92 E0");
            sb.AppendLine("G1 E17 F1000\t; close oven door");
            sb.AppendLine("M84\t\t; disable motors");

            return sb.ToString();
        }

        private const double OdfBedXMaxMm = 261.0;
        private const double OdfBedYMaxMm = 132.0; // if your Y max is really 261, set this to 261.0
        private const double OdfGapMm = 10.0;
        private const int OdfMaxFilms = 12;
        private const string GummiesPointsJsonPath = "/home/raspberrypie/json/gummies_points.json";

        private sealed class GummiesPointsConfig
        {
            public List<GummiesBlisterConfig>? Blisters { get; set; }
        }

        private sealed class GummiesBlisterConfig
        {
            public int Index { get; set; } // 1..6
            public List<GummiesPoint>? Points { get; set; }
        }

        private sealed class GummiesPoint
        {
            public double X { get; set; }
            public double Y { get; set; }
        }

        private static async Task<Dictionary<int, (double X, double Y)[]>> LoadGummiesPointsAsync(string jsonPath, CancellationToken ct)
        {
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException("Gummies points JSON not found", jsonPath);

            var json = await File.ReadAllTextAsync(jsonPath, ct).ConfigureAwait(false);

            var cfg = JsonSerializer.Deserialize<GummiesPointsConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (cfg?.Blisters == null || cfg.Blisters.Count == 0)
                throw new InvalidDataException("JSON must contain a non-empty 'blisters' array.");

            var result = new Dictionary<int, (double X, double Y)[]>();

            foreach (var b in cfg.Blisters)
            {
                if (b.Index < 1 || b.Index > 6)
                    throw new InvalidDataException($"Blister index {b.Index} is invalid. Allowed values: 1..6.");

                if (b.Points == null || b.Points.Count == 0)
                    throw new InvalidDataException($"Blister {b.Index} has no points.");

                var points = b.Points.Select(p => (p.X, p.Y)).ToArray();

                if (points.Any(p => double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsInfinity(p.X) || double.IsInfinity(p.Y)))
                    throw new InvalidDataException($"Blister {b.Index} contains invalid numeric values.");

                result[b.Index] = points;
            }

            return result;
        }

        async Task StopPrintAsync()
        {
            if (!IsSending)
                return;

            Status = "Stopping...";
            try { _cts?.Cancel(); } catch { }

            await Task.CompletedTask;
        }

        // Blister presence (2x3 UI checkboxes)
        // UI labels are remapped as:
        // Blister1Present -> B6
        // Blister2Present -> B4
        // Blister3Present -> B2
        // Blister4Present -> B5
        // Blister5Present -> B3
        // Blister6Present -> B1

        // Default: B1 enabled (therefore Blister6Present = true)
        bool _blister1Present = true;
        public bool Blister1Present { get => _blister1Present; set { _blister1Present = value; Notify(nameof(Blister1Present)); } }

        bool _blister2Present;
        public bool Blister2Present { get => _blister2Present; set { _blister2Present = value; Notify(nameof(Blister2Present)); } }

        bool _blister3Present;
        public bool Blister3Present { get => _blister3Present; set { _blister3Present = value; Notify(nameof(Blister3Present)); } }

        bool _blister4Present;
        public bool Blister4Present { get => _blister4Present; set { _blister4Present = value; Notify(nameof(Blister4Present)); } }

        bool _blister5Present;
        public bool Blister5Present { get => _blister5Present; set { _blister5Present = value; Notify(nameof(Blister5Present)); } }

        bool _blister6Present;
        public bool Blister6Present { get => _blister6Present; set { _blister6Present = value; Notify(nameof(Blister6Present)); } }

        private List<int> GetSelectedBlisters()
        {
            var selected = new List<int>(6);

            if (Blister1Present) selected.Add(1);
            if (Blister2Present) selected.Add(2);
            if (Blister3Present) selected.Add(3);
            if (Blister4Present) selected.Add(4);
            if (Blister5Present) selected.Add(5);
            if (Blister6Present) selected.Add(6);

            return selected;
        }
    }
}