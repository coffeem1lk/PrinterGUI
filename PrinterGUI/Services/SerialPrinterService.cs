using System;
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
                ReadTimeout = 3000,
                WriteTimeout = 3000,
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

            // drain initial messages
            await Task.Delay(200).ConfigureAwait(false);
            try
            {
                while (port.BytesToRead > 0)
                {
                    string? s = port.ReadLine();
                    statusProgress?.Report(s ?? string.Empty);
                }
            }
            catch { /* ignore */ }

            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                string line = lines[i].TrimEnd('\r', '\n');
                if (string.IsNullOrWhiteSpace(line)) 
                {
                    percentProgress?.Report((i * 100) / total);
                    continue;
                }

                try
                {
                    port.WriteLine(line);
                    statusProgress?.Report($"> {line}");
                }
                catch (Exception ex)
                {
                    statusProgress?.Report($"Write error: {ex.Message}");
                    throw;
                }

                // Wait for an "ok" or short delay
                bool okReceived = false;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 4000)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        if (port.BytesToRead > 0)
                        {
                            string resp = port.ReadLine();
                            statusProgress?.Report(resp);
                            if (resp.ToLowerInvariant().Contains("ok") || resp.ToLowerInvariant().Contains("start"))
                            {
                                okReceived = true;
                                break;
                            }
                        }
                    }
                    catch (TimeoutException) { /* loop */ }
                    catch (Exception ex) 
                    {
                        statusProgress?.Report($"Serial read error: {ex.Message}");
                        break;
                    }

                    await Task.Delay(50, ct).ConfigureAwait(false);
                }

                // if not ok, proceed anyway (some firmwares are quiet) after a short pause
                await Task.Delay(20, ct).ConfigureAwait(false);

                percentProgress?.Report(((i + 1) * 100) / total);
            }

            // finalization pause
            await Task.Delay(300).ConfigureAwait(false);
            try { port.Close(); } catch { }
        }
    }
}