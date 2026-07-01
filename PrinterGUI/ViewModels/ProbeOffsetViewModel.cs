using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;

namespace PrinterGUI.ViewModels
{
    public class ProbeOffsetViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        readonly string _serialPort;

        string _status = string.Empty;
        public string Status { get => _status; set { _status = value; Notify(nameof(Status)); } }

        string _currentZOffset = "Not set";
        public string CurrentZOffset { get => _currentZOffset; set { _currentZOffset = value; Notify(nameof(CurrentZOffset)); } }

        bool _canHome = false;
        public bool CanHome { get => _canHome; set { _canHome = value; Notify(nameof(CanHome)); } }

        bool _canAdjust = false;
        public bool CanAdjust 
        { 
            get => _canAdjust; 
            set 
            { 
                _canAdjust = value; 
                Notify(nameof(CanAdjust));
                Notify(nameof(CanSaveToEeprom));
            } 
        }

        bool _hasUnsavedChanges = false;
        public bool HasUnsavedChanges 
        { 
            get => _hasUnsavedChanges; 
            set 
            { 
                _hasUnsavedChanges = value; 
                Notify(nameof(HasUnsavedChanges));
                Notify(nameof(CanSaveToEeprom));
            } 
        }

        public bool CanSaveToEeprom => CanAdjust && HasUnsavedChanges;

        double _currentZPosition = 0.0; // Track cumulative Z position changes
        const double BaseOffset = -4.0; // The initial offset we start with

        public ICommand ResetOffsetCommand { get; }
        public ICommand HomeCommand { get; }
        public ICommand AdjustZCommand { get; }
        public ICommand SaveToEepromCommand { get; }

        public ProbeOffsetViewModel(string serialPort = "/dev/ttyACM0")
        {
            _serialPort = serialPort;
            ResetOffsetCommand = new RelayCommand(async _ => await ResetOffsetAsync());
            HomeCommand = new RelayCommand(async _ => await HomeAsync());
            AdjustZCommand = new RelayCommand(async p => await AdjustZAsync(p));
            SaveToEepromCommand = new RelayCommand(async _ => await SaveToEepromAsync());
        }

        async Task ResetOffsetAsync()
        {
            Status = string.Empty;
            var response = await SendGcodeAsync("M851 Z-4");

            if (!string.IsNullOrEmpty(response))
            {
                CurrentZOffset = $"{BaseOffset:F2} mm";
                CanHome = true;
                CanAdjust = false;
                HasUnsavedChanges = false;
            }
        }

        async Task HomeAsync()
        {
            Status = string.Empty;

            var response = await SendGcodeAsync("T1\nG1 E-17 F800\nG28", timeoutSeconds: 30);

            if (!string.IsNullOrEmpty(response))
            {
                // Query current Z position after homing
                var positionResponse = await SendGcodeAsync("M114");
                _currentZPosition = ParseZPosition(positionResponse);

                CanAdjust = true;
                UpdateCalculatedOffset();
            }
            else
            {
                Status = "Homing failed or timed out. Try again.";
            }
        }

        double ParseZPosition(string response)
        {
            // Parse M114 response: "X:0.00 Y:0.00 Z:150.00 E:0.00"
            var match = Regex.Match(response, @"Z:(-?\d+\.?\d*)", RegexOptions.IgnoreCase);
            if (match.Success && double.TryParse(match.Groups[1].Value, out var z))
            {
                return z;
            }
            return 0.0; // Fallback if parsing fails
        }

        async Task AdjustZAsync(object? adjustmentObj)
        {
            if (adjustmentObj is not string adjustStr || !double.TryParse(adjustStr, out var adjustment))
                return;

            Status = string.Empty;

            // Move Z axis physically
            var moveCmd = $"G91\nG1 Z{adjustment:F2} F400\nG90";
            var response = await SendGcodeAsync(moveCmd);

            if (!string.IsNullOrEmpty(response))
            {
                _currentZPosition += adjustment;
                HasUnsavedChanges = true;
                UpdateCalculatedOffset();
            }
        }

        void UpdateCalculatedOffset()
        {
            // Calculate new offset: base offset (-4) + current Z position
            double calculatedOffset = BaseOffset + _currentZPosition;
            CurrentZOffset = $"{calculatedOffset:F2} mm";
        }

        async Task SaveToEepromAsync()
        {
            if (!CanSaveToEeprom)
                return;

            // Calculate final offset
            double finalOffset = BaseOffset + _currentZPosition;

            Status = string.Empty;
            
            // Set the calculated offset
            var setOffsetCmd = $"M851 Z{finalOffset:F2}";
            var response = await SendGcodeAsync(setOffsetCmd);

            if (!string.IsNullOrEmpty(response))
            {
                // Save to EEPROM
                var saveResponse = await SendGcodeAsync("M500");
                
                if (!string.IsNullOrEmpty(saveResponse))
                {
                    HasUnsavedChanges = false;
                    Status = "New offset saved";
                }
            }
        }

        async Task<string> SendGcodeAsync(string gcode, int timeoutSeconds = 5)
        {
            try
            {
                using var port = new SerialPort(_serialPort, 115200)
                {
                    NewLine = "\n",
                    ReadTimeout = 1000,
                    WriteTimeout = 2000,
                    DtrEnable = true,
                    RtsEnable = true
                };

                port.Open();
                await Task.Delay(500);

                // Handle multi-line commands
                foreach (var line in gcode.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    port.WriteLine(line.Trim());
                    await Task.Delay(50);
                }

                var responseBuilder = new System.Text.StringBuilder();
                var startTime = DateTime.Now;
                var maxWait = TimeSpan.FromSeconds(timeoutSeconds);
                bool gotOk = false;

                while ((DateTime.Now - startTime) < maxWait)
                {
                    if (port.BytesToRead > 0)
                    {
                        var line = port.ReadLine().Trim();
                        Debug.WriteLine(line);
                        if (!string.IsNullOrEmpty(line))
                        {
                            responseBuilder.AppendLine(line);
                            if (line.StartsWith("ok", StringComparison.OrdinalIgnoreCase))
                            {
                                gotOk = true;
                                break;
                            }
                        }
                    }
                    else
                    {
                        await Task.Delay(50);
                    }
                }

                port.Close();
                return gotOk ? responseBuilder.ToString().Trim() : string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Serial error: {ex.Message}");
                return string.Empty;
            }
        }

        class RelayCommand : ICommand
        {
            readonly Func<object?, Task> _execute;
            public RelayCommand(Func<object?, Task> execute) => _execute = execute;
            public bool CanExecute(object? parameter) => true;
            public event EventHandler? CanExecuteChanged;
            public async void Execute(object? parameter) => await _execute(parameter);
        }
    }
}
