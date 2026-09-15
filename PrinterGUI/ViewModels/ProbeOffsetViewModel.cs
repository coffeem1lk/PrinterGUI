using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using PrinterGUI.Services;

namespace PrinterGUI.ViewModels
{
    public class ProbeOffsetViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        readonly SharedSerialPortService _sharedPort;

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

        double _homeZPosition = 0.0;
        double _currentZPosition = 0.0;
        const double BaseOffset = -4.0;

        public ICommand ResetOffsetCommand { get; }
        public ICommand HomeCommand { get; }
        public ICommand AdjustZCommand { get; }
        public ICommand SaveToEepromCommand { get; }

        public ProbeOffsetViewModel(SharedSerialPortService sharedPort)
        {
            _sharedPort = sharedPort;
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

        async Task AdjustZAsync(object? adjustmentObj)
        {
            if (adjustmentObj is not string adjustStr || !double.TryParse(adjustStr, out var adjustment))
                return;

            Status = string.Empty;

            var response = await SendGcodeAsync($"G91\nG1 Z{adjustment:F2} F400\nG90");

            if (!string.IsNullOrEmpty(response))
            {
                var positionResponse = await SendGcodeAsync("M114");
                _currentZPosition = ParseZPosition(positionResponse);

                HasUnsavedChanges = true;
                UpdateCalculatedOffset();
            }
        }

        async Task SaveToEepromAsync()
        {
            if (!CanSaveToEeprom)
                return;

            double finalOffset = BaseOffset + _homeZPosition + _currentZPosition;

            Status = string.Empty;

            var response = await SendGcodeAsync($"M851 Z{finalOffset:F2}");

            if (!string.IsNullOrEmpty(response))
            {
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
            var response = await _sharedPort.SendCommandAsync(gcode, timeoutSeconds * 1000);
            return response ?? string.Empty;
        }

        double ParseZPosition(string response)
        {
            var match = Regex.Match(response, @"Z:(-?\d+\.?\d*)", RegexOptions.IgnoreCase);
            if (match.Success && double.TryParse(match.Groups[1].Value, out var z))
                return z;

            return 0.0;
        }

        void UpdateCalculatedOffset()
        {
            double calculatedOffset = BaseOffset + _homeZPosition + _currentZPosition;
            CurrentZOffset = $"{calculatedOffset:F2} mm";
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
