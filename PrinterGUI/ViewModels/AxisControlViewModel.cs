using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PrinterGUI.Services;

namespace PrinterGUI.ViewModels
{
    public class AxisControlViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        readonly SharedSerialPortService _sharedPort;
        CancellationTokenSource? _tempReadingCts;

        string _status = "Ready";
        public string Status { get => _status; set { _status = value; Notify(nameof(Status)); } }

        string _customGcode = string.Empty;
        public string CustomGcode { get => _customGcode; set { _customGcode = value; Notify(nameof(CustomGcode)); } }

        string _extruderTempControl = "0";
        public string ExtruderTempControl
        {
            get => _extruderTempControl;
            set { _extruderTempControl = value; Notify(nameof(ExtruderTempControl)); }
        }

        string _currentExtruderTemp = "0";
        public string CurrentExtruderTemp
        {
            get => _currentExtruderTemp;
            set { _currentExtruderTemp = value; Notify(nameof(CurrentExtruderTemp)); }
        }

        string _response = string.Empty;
        public string Response { get => _response; set { _response = value; Notify(nameof(Response)); } }

        public ICommand MoveXCommand { get; }
        public ICommand MoveYCommand { get; }
        public ICommand MoveZCommand { get; }
        public ICommand MoveE0Command { get; }
        public ICommand MoveE1Command { get; }
        public ICommand HomeAllCommand { get; }
        public ICommand DisableMotorsCommand { get; }
        public ICommand SendCustomGcodeCommand { get; }
        public ICommand StartTempControlCommand { get; }
        public ICommand StopTempControlCommand { get; }

        public AxisControlViewModel(SharedSerialPortService sharedPort)
        {
            _sharedPort = sharedPort;

            MoveXCommand = new RelayCommand(async p => await MoveAxisAsync("X", p));
            MoveYCommand = new RelayCommand(async p => await MoveAxisAsync("Y", p));
            MoveZCommand = new RelayCommand(async p => await MoveAxisAsync("Z", p));
            MoveE0Command = new RelayCommand(async p => await MoveExtruderAsync("E", p));
            MoveE1Command = new RelayCommand(async p => await MoveExtruderAsync("E", p, extruderIndex: 1));
            HomeAllCommand = new RelayCommand(async _ => await SendGcodeAsync("G28"));
            DisableMotorsCommand = new RelayCommand(async _ => await SendGcodeAsync("M84"));
            SendCustomGcodeCommand = new RelayCommand(async _ => await SendCustomGcodeAsync());

            StartTempControlCommand = new RelayCommand(async _ => await StartTempControlAsync());
            StopTempControlCommand = new RelayCommand(async _ => await StopTempControlAsync());

            // Start reading temperature
            StartTemperatureReading();
        }

        void StartTemperatureReading()
        {
            _tempReadingCts = new CancellationTokenSource();
            _ = ReadTemperaturePeriodicAsync(_tempReadingCts.Token);
        }

        async Task ReadTemperaturePeriodicAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1000, cancellationToken);
                    await ReadCurrentTemperatureAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error reading temperature: {ex.Message}");
                }
            }
        }

        async Task ReadCurrentTemperatureAsync()
        {
            try
            {
                var response = await _sharedPort.SendCommandAsync("M105", 2000);

                // Parse temperature from response (e.g., "T:25.5 /0.0 B:25.0 /0.0")
                if (!string.IsNullOrEmpty(response))
                {
                    var tempMatch = System.Text.RegularExpressions.Regex.Match(response, @"T:([\d.]+)");
                    if (tempMatch.Success && double.TryParse(tempMatch.Groups[1].Value, out var temp))
                    {
                        CurrentExtruderTemp = temp.ToString("F1");
                    }
                    else
                    {
                        Debug.WriteLine($"Could not parse temp from: {response}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception reading temperature: {ex.Message}");
            }
        }

        async Task StartTempControlAsync()
        {
            if (!int.TryParse(ExtruderTempControl, NumberStyles.Integer, CultureInfo.InvariantCulture, out var temp) || temp < 0)
            {
                Status = "Invalid extruder temp";
                return;
            }

            await SendGcodeAsync($"M104 S{temp}");
        }

        async Task StopTempControlAsync()
        {
            await SendGcodeAsync("M104 S0");
        }

        async Task MoveAxisAsync(string axis, object? distanceObj)
        {
            if (distanceObj is not string distStr || !double.TryParse(distStr, out var distance))
            {
                Status = "Invalid distance";
                return;
            }

            var feedrate = axis switch
            {
                "X" or "Y" => "F3000",
                "Z" => "F400",
                _ => "F3000"
            };

            var gcode = $"G91\nG1 {axis}{distance:F2} {feedrate}\nG90";
            await SendGcodeAsync(gcode);
        }

        async Task MoveExtruderAsync(string axis, object? distanceObj, int extruderIndex = 0)
        {
            if (distanceObj is not string distStr || !double.TryParse(distStr, out var distance))
            {
                Status = "Invalid distance";
                return;
            }

            var toolCmd = extruderIndex == 0 ? "T0" : "T1";
            var gcode = $"{toolCmd}\nG91\nG1 E{distance:F2} F400\nG1 F3000\nG90";
            await SendGcodeAsync(gcode);
        }

        async Task SendCustomGcodeAsync()
        {
            if (string.IsNullOrWhiteSpace(CustomGcode))
            {
                Status = "Enter a G-code command first";
                return;
            }

            var command = CustomGcode.Trim();
            await SendGcodeAsync(command);
            CustomGcode = string.Empty;
        }

        async Task SendGcodeAsync(string gcode)
        {
            Status = $"Sending: {gcode.Replace("\n", " | ")}";
            Response = string.Empty;

            try
            {
                var responseBuilder = new System.Text.StringBuilder();

                foreach (var line in gcode.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    
                    var response = await _sharedPort.SendCommandAsync(line.Trim(), 5000);
                    if (!string.IsNullOrEmpty(response))
                    {
                        responseBuilder.AppendLine(response);
                        Debug.WriteLine($"Response: {response}");
                    }
                    
                    await Task.Delay(50);
                }

                Response = responseBuilder.Length > 0
                    ? responseBuilder.ToString().Trim()
                    : "(no response - check serial port connection)";

                Status = "Command sent successfully";
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
                Response = $"Error: {ex.Message}";
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
