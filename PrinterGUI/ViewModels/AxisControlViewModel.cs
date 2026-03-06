using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading.Tasks;
using System.Windows.Input;

namespace PrinterGUI.ViewModels
{
    public class AxisControlViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        readonly string _serialPort;

        string _status = "Ready";
        public string Status { get => _status; set { _status = value; Notify(nameof(Status)); } }

        public ICommand MoveXCommand { get; }
        public ICommand MoveYCommand { get; }
        public ICommand MoveZCommand { get; }
        public ICommand MoveE0Command { get; }
        public ICommand MoveE1Command { get; }
        public ICommand HomeAllCommand { get; }
        public ICommand DisableMotorsCommand { get; }

        public AxisControlViewModel(string serialPort = "/dev/ttyACM0")
        {
            _serialPort = serialPort;

            MoveXCommand = new RelayCommand(async p => await MoveAxisAsync("X", p));
            MoveYCommand = new RelayCommand(async p => await MoveAxisAsync("Y", p));
            MoveZCommand = new RelayCommand(async p => await MoveAxisAsync("Z", p));
            MoveE0Command = new RelayCommand(async p => await MoveExtruderAsync("E", p));
            MoveE1Command = new RelayCommand(async p => await MoveExtruderAsync("E", p, extruderIndex: 1));
            HomeAllCommand = new RelayCommand(async _ => await SendGcodeAsync("G28"));
            DisableMotorsCommand = new RelayCommand(async _ => await SendGcodeAsync("M84"));
        }

        async Task MoveAxisAsync(string axis, object? distanceObj)
        {
            if (distanceObj is not string distStr || !double.TryParse(distStr, out var distance))
            {
                Status = "Invalid distance";
                return;
            }

            // Use appropriate feedrates for each axis
            var feedrate = axis switch
            {
                "X" or "Y" => "F3000", // 3000 mm/min for XY (50 mm/s)
                "Z" => "F400",          // 400 mm/min for Z (~6.7 mm/s)
                _ => "F3000"
            };

            // Use G1 (controlled move) with explicit feedrate
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

            // Select tool, move E with F400 feedrate, then reset to normal speed
            var toolCmd = extruderIndex == 0 ? "T0" : "T1";
            var gcode = $"{toolCmd}\nG91\nG1 E{distance:F2} F400\nG1 F3000\nG90";
            await SendGcodeAsync(gcode);
        }

        async Task SendGcodeAsync(string gcode)
        {
            Status = $"Sending: {gcode.Replace("\n", " | ")}";
            try
            {
                using var port = new SerialPort(_serialPort, 115200)
                {
                    NewLine = "\n",
                    ReadTimeout = 2000,
                    WriteTimeout = 2000,
                    DtrEnable = true,
                    RtsEnable = true
                };

                port.Open();
                await Task.Delay(200); // allow init

                foreach (var line in gcode.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    port.WriteLine(line.Trim());
                    await Task.Delay(100); // small delay between commands
                }

                // Wait for response (best-effort)
                await Task.Delay(300);
                while (port.BytesToRead > 0)
                {
                    try { var resp = port.ReadLine(); Debug.WriteLine(resp); } catch { }
                }

                port.Close();
                Status = "Command sent successfully";
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
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
