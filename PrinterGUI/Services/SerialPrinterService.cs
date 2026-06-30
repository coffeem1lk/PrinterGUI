using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PrinterGUI.Services
{
    public class SerialPrinterService
    {
        /// <summary>
        /// Send a G-code file to a printer using a shared serial port.
        /// Reads printer responses and reports progress.
        /// </summary>
        public async Task SendFileAsync(string filePath, SharedSerialPortService sharedPort, IProgress<string>? statusProgress = null, IProgress<int>? percentProgress = null, CancellationToken ct = default)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("G-code file not found", filePath);

            string[] lines = await File.ReadAllLinesAsync(filePath, ct).ConfigureAwait(false);
            int total = lines.Length;

            try
            {
                sharedPort.EnsureOpen();
                sharedPort.DiscardInBuffer();
                await Task.Delay(300, ct).ConfigureAwait(false);

                for (int i = 0; i < total; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    string line = lines[i].Trim();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";"))
                    {
                        percentProgress?.Report((i * 100) / total);
                        continue;
                    }

                    // Remove inline comments for command parsing/sending
                    string command = line.Split(';', 2)[0].Trim();
                    if (string.IsNullOrWhiteSpace(command))
                    {
                        percentProgress?.Report((i * 100) / total);
                        continue;
                    }

                    sharedPort.WriteLine(command);
                    statusProgress?.Report($"> {line}");

                    // Dynamic ACK timeout
                    int ackTimeoutMs = 15000;

                    // long-running commands
                    if (command.StartsWith("G28", StringComparison.OrdinalIgnoreCase) ||   // homing
                        command.StartsWith("G29", StringComparison.OrdinalIgnoreCase) ||   // bed leveling
                        command.StartsWith("G30", StringComparison.OrdinalIgnoreCase) ||   // probing
                        command.StartsWith("M109", StringComparison.OrdinalIgnoreCase) ||  // wait for hotend temp
                        command.StartsWith("M190", StringComparison.OrdinalIgnoreCase))    // wait for bed temp
                    {
                        ackTimeoutMs = 120000; // 120s
                    }

                    // dwell commands (G4 Sxxx or G4 Pxxx)
                    if (command.StartsWith("G4", StringComparison.OrdinalIgnoreCase))
                    {
                        var sIdx = command.IndexOf('S');
                        if (sIdx >= 0)
                        {
                            var token = command[(sIdx + 1)..].Trim().Split(' ', '\t')[0];
                            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var secs) && secs > 0)
                                ackTimeoutMs = Math.Max(ackTimeoutMs, (int)Math.Ceiling((secs + 10) * 1000));
                        }
                        else
                        {
                            var pIdx = command.IndexOf('P');
                            if (pIdx >= 0)
                            {
                                var token = command[(pIdx + 1)..].Trim().Split(' ', '\t')[0];
                                if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) && ms > 0)
                                    ackTimeoutMs = Math.Max(ackTimeoutMs, ms + 10000);
                            }
                        }
                    }

                    bool okReceived = false;
                    var sw = System.Diagnostics.Stopwatch.StartNew();

                    while (sw.ElapsedMilliseconds < ackTimeoutMs)
                    {
                        ct.ThrowIfCancellationRequested();

                        try
                        {
                            if (sharedPort.BytesToRead > 0)
                            {
                                string resp = sharedPort.ReadLine();
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
            }
            catch (Exception ex)
            {
                statusProgress?.Report($"Error: {ex.Message}");
                throw;
            }
        }
    }
}