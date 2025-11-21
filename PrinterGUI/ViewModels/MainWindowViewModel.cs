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

namespace PrinterGUI.ViewModels
{
    public class MainWindowViewModel : INotifyPropertyChanged, IDisposable
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public ObservableCollection<PrintObject> Objects { get; } = new ObservableCollection<PrintObject>();

        PrintObject? _selectedObject;
        public PrintObject? SelectedObject { get => _selectedObject; set { _selectedObject = value; Notify(nameof(SelectedObject)); UpdateCanSend(); } }

        public string SerialPortPath { get; set; } = "/dev/ttyUSB0";

        // parameters
        public string ExtruderTemp { get; set; } = "0";
        public string DryingTemp { get; set; } = "0";
        public string LayerHeight { get; set; } = "0.3";
        public string Infill { get; set; } = "90";
        public string PrintSpeed { get; set; } = "11.5";

        // new: drying time (minutes) stored as string for binding
        public string DryingTime { get; set; } = "0";

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
            var progressText = new Progress<string>(s => Status = s);

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

                var result = await GcodeGenerator.SliceWithPrusaAsync(
                    stlPath,
                    outPath,
                    layerHeight,
                    infill,
                    extruderTemp,
                    dryingTemp,
                    dryingTime,
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

        // Generate ODFX.gcode, send to printer, then delete the generated gcode file
        async Task SendToPrinterAsync()
        {
            if (SelectedObject == null) { Status = "Select an object first"; return; }

            if (!TryParseInputs(out int extruderTemp, out int dryingTemp, out double layerHeight, out int infill, out double printSpeed))
                return;

            // create temp gcode via slicer
            string tempPath = Path.Combine(Path.GetTempPath(), $"print_{Guid.NewGuid():N}.gcode");
            IsSending = true;
            _cts = new CancellationTokenSource();
            Progress = 0;
            Status = "Slicing (PrusaSlicer) before send...";

            var slicerProgress = new Progress<string>(s => Status = s);
            try
            {
                string stlPath = SelectedObject.FileName;
                if (!Path.IsPathRooted(stlPath))
                    stlPath = Path.Combine(_modelsFolder, stlPath);

                // ensure dryingTime is passed in the correct position (7th parameter) and printSpeed stays double
                int dryingTime = 0;
                if (!int.TryParse(DryingTime, out dryingTime))
                {
                    Status = "Invalid drying time";
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
                    printSpeed,    // 8th param (double)
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
            var progText = new Progress<string>(s => Status = s);
            var progPercent = new Progress<int>(p => Progress = p);

            try
            {
                await _serial.SendFileAsync(tempPath, SerialPortPath, 115200, progText, progPercent, _cts.Token);
                Status = "Send complete";
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