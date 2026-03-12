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

namespace PrinterGUI.ViewModels
{
    public class MainWindowViewModel : INotifyPropertyChanged, IDisposable
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public ObservableCollection<PrintObject> Objects { get; } = new ObservableCollection<PrintObject>();

        PrintObject? _selectedObject;
        public PrintObject? SelectedObject { get => _selectedObject; set { _selectedObject = value; Notify(nameof(SelectedObject)); UpdateCanSend(); } }

        public string SerialPortPath { get; set; } = "/dev/ttyACM0";

        // parameters
        public string ExtruderTemp { get; set; } = "0";
        public string DryingTemp { get; set; } = "0";
        public string LayerHeight { get; set; } = "0.3";
        public string Infill { get; set; } = "90";
        public string PrintSpeed { get; set; } = "11.5";

        // new: drying time (minutes) stored as string for binding
        public string DryingTime { get; set; } = "0";

        // new: Drying time RT (minutes) stored for post-processing only
        public string DryingTimeRT { get; set; } = "0";

        // CLI binary (can remain default command if on PATH)
        public string PrusaSlicerPath { get; set; } = "prusa-slicer";

        string _status = "Idle";
        public string Status { get => _status; set { _status = value; Notify(nameof(Status)); } }

        int _progress;
        public int Progress { get => _progress; set { _progress = value; Notify(nameof(Progress)); } }

        bool _isSending;
        public bool IsSending { get => _isSending; set { _isSending = value; Notify(nameof(IsSending)); UpdateCanSend(); } }

        public bool CanSend => SelectedObject != null && !IsSending;

        void UpdateCanSend() { Notify(nameof(CanSend)); }

        public ICommand GenerateGcodeCommand { get; }
        public ICommand SendToPrinterCommand { get; }

        readonly SerialPrinterService _serial = new SerialPrinterService();

        CancellationTokenSource? _cts;

        // Dynamic discovery fields
        readonly string _modelsFolder = "/home/raspberrypie/stl"; // change to your folder
        FileSystemWatcher? _watcher;
        readonly SynchronizationContext? _uiContext;
        Timer? _debounceTimer;

        public MainWindowViewModel()
        {
            _uiContext = SynchronizationContext.Current;

            // Populate objects from disk
            LoadModels();

            // Start watching the models folder to update the list live
            StartWatchingModelsFolder();

            GenerateGcodeCommand = new RelayCommand(async _ => await GenerateGcodeAsync());
            SendToPrinterCommand = new RelayCommand(async _ => await SendToPrinterAsync(), _ => CanSend);
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

                // create debounce timer but don't start it yet; 300ms debounce
                _debounceTimer = new Timer(_ => LoadModels(), null, Timeout.Infinite, Timeout.Infinite);
            }
            catch
            {
                // best-effort: don't crash VM if watcher can't be created
            }
        }

        void DebounceLoad()
        {
            try
            {
                // reset timer to trigger once after 300ms of no changes
                _debounceTimer?.Change(300, Timeout.Infinite);
            }
            catch { }
        }

        void LoadModels()
        {
            try
            {
                string[] files = Array.Empty<string>();
                if (Directory.Exists(_modelsFolder))
                    files = Directory.GetFiles(_modelsFolder, "*.stl", SearchOption.TopDirectoryOnly);

                // Ensure collection modifications happen on UI thread
                void Action()
                {
                    Objects.Clear();
                    foreach (var f in files.OrderBy(Path.GetFileName))
                    {
                        var po = new PrintObject
                        {
                            Name = Path.GetFileNameWithoutExtension(f),
                            FileName = f,
                            WidthMm = 0,
                            DepthMm = 0,
                            HeightMm = 0
                        };

                        Objects.Add(po);
                    }
                    // keep selection valid
                    if (SelectedObject != null && !Objects.Contains(SelectedObject))
                        SelectedObject = Objects.FirstOrDefault();
                }

                if (_uiContext != null)
                    _uiContext.Post(_ => Action(), null);
                else
                    Action();
            }
            catch
            {
                // ignore errors during scanning
            }
        }

        // Generate G-code file next to the STL (same folder) and leave it there
        async Task GenerateGcodeAsync()
        {
            if (SelectedObject == null) { Status = "Select an object first"; return; }

            if (!TryParseInputs(out int extruderTemp, out int dryingTemp, out double layerHeight, out int infill, out double printSpeed))
                return;

            Status = "Slicing (PrusaSlicer)...";
            Progress = 0;

            using var cts = new CancellationTokenSource();
            var progressText = new Progress<string>(s => 
            {
                // Only show important messages, not every G-code line or comments
                if (!s.StartsWith("G") && !s.StartsWith("M") && !s.StartsWith(">") && !s.StartsWith(";"))
                    Status = s;
            });

            try
            {
                string stlPath = SelectedObject.FileName;
                if (!Path.IsPathRooted(stlPath))
                    stlPath = Path.Combine(_modelsFolder, stlPath);

                string outPath = Path.Combine(Path.GetDirectoryName(stlPath) ?? _modelsFolder,
                    $"{Path.GetFileNameWithoutExtension(stlPath)}_{DateTime.Now:yyyyMMdd_HHmmss}.gcode");

                // ensure dryingTime is passed in the correct position (7th parameter) and printSpeed stays double
                int dryingTime = 0;
                if (!int.TryParse(DryingTime, out dryingTime))
                {
                    Status = "Invalid drying time";
                    return;
                }

                int dryingTimeRT = 0;
                if (!int.TryParse(DryingTimeRT, out dryingTimeRT))
                {
                    Status = "Invalid drying time RT";
                    return;
                }

                var result = await GcodeGenerator.SliceWithPrusaAsync(
                    stlPath,
                    outPath,
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
                    outputProgress: progressText,
                    cancellationToken: cts.Token);

                if (!result.Success)
                {
                    Status = $"Slicing failed: {result.Error}";
                    return;
                }

                Status = $"G-code saved: {outPath}";
                Progress = 100;
            }
            catch (Exception ex)
            {
                Status = $"Slicing failed: {ex.Message}";
            }
        }

        // Generate temp gcode, send to printer, then delete the temp file and any older generated files for the selected model
        async Task SendToPrinterAsync()
        {
            if (SelectedObject == null) { Status = "Select an object first"; return; }

            if (!TryParseInputs(out int extruderTemp, out int dryingTemp, out double layerHeight, out int infill, out double printSpeed))
                return;

            // Prepare model path early so deletion can target the model's folder after send
            string stlPath = SelectedObject.FileName;
            if (!Path.IsPathRooted(stlPath))
                stlPath = Path.Combine(_modelsFolder, stlPath);

            // create temp gcode via slicer
            string tempPath = Path.Combine(Path.GetTempPath(), $"print_{Guid.NewGuid():N}.gcode");
            IsSending = true;
            _cts = new CancellationTokenSource();
            Progress = 0;
            Status = "Slicing (PrusaSlicer) before send...";

            var slicerProgress = new Progress<string>(s => 
            {
                // Only show important messages, not every G-code line or comments
                if (!s.StartsWith("G") && !s.StartsWith("M") && !s.StartsWith(">") && !s.StartsWith(";"))
                    Status = s;
            });
            try
            {
                // ensure dryingTime is passed in the correct position (7th parameter) and printSpeed stays double
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
                    dryingTemp,    // 6th param
                    dryingTime,    // 7th param (int)
                    dryingTimeRT,  // new param (RT)
                    printSpeed,    // 9th param (double)
                    prusaSlicerPath: PrusaSlicerPath,
                    profilePath: "/home/raspberrypie/config.ini",
                    extraArgs: null,
                    timeout: TimeSpan.FromMinutes(12),
                    outputProgress: slicerProgress,
                    cancellationToken: _cts.Token).ConfigureAwait(false);

                if (!resultPath.Success)
                {
                    Status = "Slicing failed: " + (string.IsNullOrWhiteSpace(resultPath.Error) ? resultPath.Output : resultPath.Error);
                    IsSending = false;
                    return;
                }
            }
            catch (Exception ex)
            {
                Status = "Slicing failed: " + ex.Message;
                IsSending = false;
                _cts?.Dispose();
                _cts = null;
                return;
            }

            Status = "Opening serial port...";
            var progText = new Progress<string>(s => 
            {
                // Only show important messages during send
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
            catch (Exception ex)
            {
                Status = "Send failed: " + ex.Message;
            }
            finally
            {
                IsSending = false;
                _cts?.Dispose();
                _cts = null;

                // Save copy to /home/raspberrypie/gcode/ before deleting temp file
                if (sendSucceeded && File.Exists(tempPath))
                {
                    try
                    {
                        string gcodeFolder = "/home/raspberrypie/gcode";
                        Directory.CreateDirectory(gcodeFolder); // Ensure folder exists
                        
                        string modelName = Path.GetFileNameWithoutExtension(stlPath);
                        string copyPath = Path.Combine(gcodeFolder, $"{modelName}_{DateTime.Now:yyyyMMdd_HHmmss}.gcode");
                        
                        File.Copy(tempPath, copyPath, overwrite: true);
                        Status = $"Send complete. G-code saved to {copyPath}";
                    }
                    catch (Exception ex)
                    {
                        // Don't fail the whole operation if copy fails
                        Status = $"Send complete (copy failed: {ex.Message})";
                    }
                }

                try { File.Delete(tempPath); } catch { }

                if (sendSucceeded)
                {
                    // Clean up older generated files for the same model (created by previous "Generate Gcode" runs).
                    // Runs off the UI thread and reports a short status update when done.
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

                // Pattern used by GenerateGcodeAsync: BaseName_yyyyMMdd_HHmmss.gcode
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

        bool TryParseInputs(out int extruderTemp, out int dryingTemp, out double layerHeight, out int infill, out double printSpeed)
        {
            extruderTemp = 0; dryingTemp = 0; layerHeight = 0; infill = 0; printSpeed = 0;
            if (!int.TryParse(ExtruderTemp, out extruderTemp)) { Status = "Invalid extruder temp"; return false; }
            if (!int.TryParse(DryingTemp, out dryingTemp)) { Status = "Invalid drying temp"; return false; }
            if (!double.TryParse(LayerHeight, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out layerHeight)) { Status = "Invalid layer height"; return false; }
            if (!int.TryParse(Infill, out infill)) { Status = "Invalid infill"; return false; }
            if (!double.TryParse(PrintSpeed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out printSpeed)) { Status = "Invalid print speed"; return false; }
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
            public async void Execute(object? parameter) => await (_executeAsync?.Invoke(parameter) ?? Task.CompletedTask);
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
    }
}