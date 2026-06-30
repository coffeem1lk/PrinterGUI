using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace PrinterGUI.Services
{
    public class SharedSerialPortService : IDisposable
    {
        private SerialPort? _port;
        private readonly object _portLock = new object();
        private readonly string _portName;
        private const int BaudRate = 115200;
        private bool _disposed = false;

        public SharedSerialPortService(string portName)
        {
            _portName = portName;
        }

        public bool IsOpen
        {
            get
            {
                lock (_portLock)
                {
                    return _port?.IsOpen == true;
                }
            }
        }

        public async Task<string?> SendCommandAsync(string command, int timeoutMs = 500)
        {
            lock (_portLock)
            {
                if (_disposed)
                    throw new ObjectDisposedException("SharedSerialPortService");

                if (_port?.IsOpen != true)
                    OpenPort();

                try
                {
                    _port?.WriteLine(command);
                    var response = string.Empty;
                    var sw = System.Diagnostics.Stopwatch.StartNew();

                    while (sw.ElapsedMilliseconds < timeoutMs)
                    {
                        if (_port?.BytesToRead > 0)
                        {
                            response += _port.ReadExisting();
                            if (response.Contains("ok") || response.Contains("\n"))
                                break;
                        }
                        Thread.Sleep(10);
                    }

                    return response;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error sending command: {ex.Message}");
                    ClosePort();
                    return null;
                }
            }
        }

        /// <summary>
        /// Write a line to the serial port (used by SendFileAsync).
        /// </summary>
        public void WriteLine(string line)
        {
            lock (_portLock)
            {
                if (_disposed)
                    throw new ObjectDisposedException("SharedSerialPortService");

                if (_port?.IsOpen != true)
                    OpenPort();

                _port?.WriteLine(line);
            }
        }

        /// <summary>
        /// Read existing data from the port buffer.
        /// </summary>
        public string ReadExisting()
        {
            lock (_portLock)
            {
                if (_disposed || _port?.IsOpen != true)
                    return string.Empty;

                try
                {
                    return _port.ReadExisting();
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        /// <summary>
        /// Check how many bytes are available to read.
        /// </summary>
        public int BytesToRead
        {
            get
            {
                lock (_portLock)
                {
                    if (_disposed || _port?.IsOpen != true)
                        return 0;
                    return _port.BytesToRead;
                }
            }
        }

        /// <summary>
        /// Read a line from the serial port.
        /// </summary>
        public string ReadLine()
        {
            lock (_portLock)
            {
                if (_disposed || _port?.IsOpen != true)
                    throw new InvalidOperationException("Port not open");

                return _port?.ReadLine() ?? string.Empty;
            }
        }

        /// <summary>
        /// Ensure the port is open. Called before sending files.
        /// </summary>
        public void EnsureOpen()
        {
            lock (_portLock)
            {
                if (_disposed)
                    throw new ObjectDisposedException("SharedSerialPortService");

                if (_port?.IsOpen != true)
                    OpenPort();
            }
        }

        /// <summary>
        /// Clear the read buffer.
        /// </summary>
        public void DiscardInBuffer()
        {
            lock (_portLock)
            {
                if (_disposed || _port?.IsOpen != true)
                    return;

                try
                {
                    _port?.DiscardInBuffer();
                }
                catch { }
            }
        }

        private void OpenPort()
        {
            try
            {
                _port = new SerialPort(_portName, BaudRate)
                {
                    NewLine = "\n",
                    ReadTimeout = 5000,
                    WriteTimeout = 15000,
                    DtrEnable = true,
                    RtsEnable = true
                };
                _port.Open();
                Thread.Sleep(300);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open serial port {_portName}: {ex.Message}");
                ClosePort();
                throw;
            }
        }

        private void ClosePort()
        {
            try { _port?.Close(); } catch { }
            try { _port?.Dispose(); } catch { }
            _port = null;
        }

        public void Close()
        {
            lock (_portLock)
            {
                ClosePort();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            lock (_portLock)
            {
                ClosePort();
                _disposed = true;
            }
        }
    }
}