using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Ports;
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
                Notify(nameof(CanSaveToEeprom)); // Update save button when homing completes
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
                Notify(nameof(CanSaveToEeprom)); // Update save button when changes occur
            } 
        }

        // Save button only enabled after homing AND when there are changes
        public bool CanSaveToEeprom => CanAdjust && HasUnsavedChanges;

        double _zOffsetValue = -4.0;

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
                _zOffsetValue = -4.0;
                CurrentZOffset = $"{_zOffsetValue:F2} mm";
                CanHome = true;
                CanAdjust = false;
                HasUnsavedChanges = true;
            }
        }

        async Task HomeAsync()
        {
            Status = string.Empty;
            var response = await SendGcodeAsync("G28", timeoutSeconds: 30); // G28 can take 20-30 seconds

            if (!string.IsNullOrEmpty(response))
            {
                CanAdjust = true;
            }
            else
            {
                Status = "Homing failed or timed out. Try again.";
            }
        }

        async Task AdjustZAsync(object? adjustmentObj)
        {
            if (adjustmentObj is not string adjustStr || !double.TryParse(adjustStr, out var adjustment))
                return;

            Status = string.Empty;
            _zOffsetValue += adjustment;

            var cmd = $"M851 Z{_zOffsetValue:F2}";
            var response = await SendGcodeAsync(cmd);

            if (!string.IsNullOrEmpty(response))
            {
                CurrentZOffset = $"{_zOffsetValue:F2} mm";
                HasUnsavedChanges = true;
            }
            else
            {
                _zOffsetValue -= adjustment;
            }
        }

        async Task SaveToEepromAsync()
        {
            if (!CanSaveToEeprom)
                return;

            var response = await SendGcodeAsync("M500");

            if (!string.IsNullOrEmpty(response))
            {
                HasUnsavedChanges = false;
                Status = "New offset saved";
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

                port.WriteLine(gcode.Trim());
                await Task.Delay(100);

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
