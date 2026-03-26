using System;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace PrinterGUI.Services
{
    public class SerialPrinterService
    {
        /// <summary>
        /// Send a G-code file to a printer over a serial port line-by-line.
        /// Reads printer responses and reports progress.
        /// </summary>
        public async Task SendFileAsync(string filePath, string portName, int baudRate, IProgress<string>? statusProgress = null, IProgress<int>? percentProgress = null, CancellationToken ct = default)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("G-code file not found", filePath);

            string[] lines = await File.ReadAllLinesAsync(filePath, ct).ConfigureAwait(false);
            int total = lines.Length;

            using SerialPort port = new SerialPort(portName, baudRate)
            {
                NewLine = "\n",
                ReadTimeout = 5000,
                WriteTimeout = 15000,
                DtrEnable = true,
                RtsEnable = true
            };

            try
            {
                port.Open();
            }
            catch (Exception ex)
            {
                statusProgress?.Report($"Failed to open serial port {portName}: {ex.Message}");
                throw;
            }

            await Task.Delay(300, ct).ConfigureAwait(false);

            try
            {
                while (port.BytesToRead > 0)
                {
                    var s = port.ReadLine();
                    statusProgress?.Report(s ?? string.Empty);
                }
            }
            catch { }

            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();

                string line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";"))
                {
                    percentProgress?.Report((i * 100) / total);
                    continue;
                }

                port.WriteLine(line);
                statusProgress?.Report($"> {line}");

                // Dynamic ACK timeout: longer for dwell commands (G4 Sxxx)
                int ackTimeoutMs = 15000;
                if (line.StartsWith("G4", StringComparison.OrdinalIgnoreCase))
                {
                    var sIdx = line.IndexOf('S');
                    if (sIdx >= 0 && int.TryParse(line[(sIdx + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var secs) && secs > 0)
                        ackTimeoutMs = (secs + 10) * 1000;
                }

                bool okReceived = false;
                var sw = System.Diagnostics.Stopwatch.StartNew();

                while (sw.ElapsedMilliseconds < ackTimeoutMs)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        if (port.BytesToRead > 0)
                        {
                            string resp = port.ReadLine();
                            statusProgress?.Report(resp);

                            var r = resp.ToLowerInvariant();
                            if (r.Contains("ok") || r.Contains("start"))
                            {
                                okReceived = true;
                                break;
                            }

                            // keep waiting on "busy" messages
                            if (r.Contains("busy"))
                                continue;
                        }
                    }
                    catch (TimeoutException)
                    {
                        // keep polling
                    }

                    await Task.Delay(50, ct).ConfigureAwait(false);
                }

                if (!okReceived)
                    throw new TimeoutException($"Timeout waiting for printer ACK after line {i + 1}: {line}");

                percentProgress?.Report(((i + 1) * 100) / total);
            }

            await Task.Delay(300, ct).ConfigureAwait(false);
            try { port.Close(); } catch { }
        }
    }
}