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
                    Thread.Sleep(50); // Let printer process command
                    
                    var response = string.Empty;
                    var startTime = DateTime.UtcNow;

                    while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
                    {
                        if (_port?.BytesToRead > 0)
                        {
                            var chunk = _port.ReadExisting();
                            response += chunk;
                            
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

        public void WriteLine(string line)
        {
            lock (_portLock)
            {
                if (_disposed)
                    throw new ObjectDisposedException("SharedSerialPortService");

                if (_port?.IsOpen != true)
                    OpenPort();

                try
                {
                    _port?.WriteLine(line);
                    Thread.Sleep(50); // Ensure line is sent
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error writing line: {ex.Message}");
                    ClosePort();
                    throw;
                }
            }
        }

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

        public string ReadLine()
        {
            lock (_portLock)
            {
                if (_disposed || _port?.IsOpen != true)
                    throw new InvalidOperationException("Port not open");

                try
                {
                    return _port?.ReadLine() ?? string.Empty;
                }
                catch (TimeoutException)
                {
                    return string.Empty;
                }
            }
        }

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
                    ReadTimeout = 500,
                    WriteTimeout = 500,
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

        /// <summary>
        /// Temporarily close the port to allow other processes to use it.
        /// Must be paired with Resume().
        /// </summary>
        public void Pause()
        {
            lock (_portLock)
            {
                ClosePort();
            }
        }

        /// <summary>
        /// Reopen the port after it was paused.
        /// </summary>
        public void Resume()
        {
            lock (_portLock)
            {
                if (_port?.IsOpen != true)
                {
                    try
                    {
                        OpenPort();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error resuming port: {ex.Message}");
                    }
                }
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