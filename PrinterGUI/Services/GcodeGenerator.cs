using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PrinterGUI.Models;

namespace PrinterGUI.Services
{
    public class SlicerResult
    {
        public bool Success { get; set; }
        public string? GcodePath { get; set; }
        public string Output { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public int ExitCode { get; set; }
        public int? LayerCount { get; set; }
        public TimeSpan? EstimatedPrintTime { get; set; }
    }

    /// <summary>
    /// Wrapper that invokes PrusaSlicer CLI to produce G-code.
    /// Use <paramref name="prusaSlicerPath"/> to point to the executable (default 'prusa-slicer').
    /// If <paramref name="profilePath"/> is provided it will be passed with --load.
    /// Common overrides (layerHeight, infillPercent, nozzle temps, printSpeed) are translated to CLI flags.
    /// The "dryingTemp", "dryingTime" and "dryingTimeRT" parameters are NOT passed to PrusaSlicer; they are saved as sidecar metadata
    /// next to the generated .gcode file for later use by an external post-processor (now also available as a managed component).
    /// The slice will now be considered failed if the managed post-processor fails.
    /// You may supply additional flags via extraArgs.
    /// </summary>
    public static class GcodeGenerator
    {
        public static async Task<SlicerResult> SliceWithPrusaAsync(
            string stlPath,
            string outputGcodePath,
            double layerHeight = 0.3,
            int infillPercent = 90,
            int nozzleTemp = 0,
            int dryingTemp = 0,                // stored for later post-processing
            int dryingTime = 0,               // stored for later post-processing (minutes)
            int dryingTimeRT = 0,             // stored for later post-processing (minutes, RT)
            double printSpeed = 11.5,
            string prusaSlicerPath = "prusa-slicer",
            string? profilePath = "/home/raspberrypie/config.ini",
            string? extraArgs = null,
            TimeSpan? timeout = null,
            IProgress<string>? outputProgress = null,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(stlPath))
                throw new FileNotFoundException("Input STL not found", stlPath);

            if (profilePath != null && !File.Exists(profilePath))
                throw new FileNotFoundException("Profile file not found", profilePath);

            timeout ??= TimeSpan.FromMinutes(10);
            Directory.CreateDirectory(Path.GetDirectoryName(outputGcodePath) ?? Path.GetTempPath());

            // Build argument list
            var args = new StringBuilder();

            // Use export mode
            args.Append("--export-gcode ");
            // load profile if provided
            if (!string.IsNullOrWhiteSpace(profilePath))
            {
                args.Append("--load ").Append($"\"{profilePath}\" ").Append(' ');
            }

            // Output path
            args.Append("--output ").Append($"\"{outputGcodePath}\" ").Append(' ');

            // Basic overrides. Note: verify these flag names against your prusa-slicer version.
            // If your build uses different names, pass replacements in extraArgs or adjust here.
            args.Append("--first-layer-height ").Append(layerHeight.ToString(CultureInfo.InvariantCulture)).Append(' ');
            args.Append("--layer-height ").Append(layerHeight.ToString(CultureInfo.InvariantCulture)).Append(' ');

            // --fill-density must be followed immediately by a value and the '%' symbol with no space (e.g. 90%)
            var fillArg = infillPercent.ToString(CultureInfo.InvariantCulture) + "%";
            args.Append("--fill-density ").Append(fillArg).Append(' ');

            args.Append("--first-layer-temperature ").Append(nozzleTemp.ToString(CultureInfo.InvariantCulture)).Append(' ');
            args.Append("--temperature ").Append(nozzleTemp.ToString(CultureInfo.InvariantCulture)).Append(' ');
            // NOTE: dryingTemp, dryingTime and dryingTimeRT are intentionally NOT passed to PrusaSlicer.
            // args.Append("--bed-temperature ").Append(dryingTemp.ToString(CultureInfo.InvariantCulture)).Append(' ');
            // print speed may be global percentage or mm/s depending on version; use --print-speed for common builds
            args.Append("--infill-speed ").Append(printSpeed.ToString(CultureInfo.InvariantCulture)).Append(' ');
            args.Append("--first-layer-infill-speed ").Append(printSpeed.ToString(CultureInfo.InvariantCulture)).Append(' ');
            args.Append("--perimeter-speed ").Append(printSpeed.ToString(CultureInfo.InvariantCulture)).Append(' ');
            args.Append("--external-perimeter-speed ").Append(printSpeed.ToString(CultureInfo.InvariantCulture)).Append(' ');

            // append any extra args the caller wants
            if (!string.IsNullOrWhiteSpace(extraArgs))
            {
                args.Append(extraArgs).Append(' ');
            }

            // input model(s)
            args.Append($"\"{stlPath}\"");

            var result = new SlicerResult();
            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();

            using var proc = new Process();
            proc.StartInfo.FileName = prusaSlicerPath;
            proc.StartInfo.Arguments = args.ToString();
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.RedirectStandardError = true;
            proc.EnableRaisingEvents = true;

            // Prepare a permanent copy of the command-line invocation so it persists after slicing.
            // Declared outside the try so it can be included in the metadata later.
            string launchCmd = $"{proc.StartInfo.FileName} {proc.StartInfo.Arguments}";
            try
            {
                // Best-effort: write the command line to a file next to the intended .gcode file.
                // This is the permanent record you requested.
                try
                {
                    var cmdPath = outputGcodePath + ".cmdline.txt";
                    File.WriteAllText(cmdPath, launchCmd, Encoding.UTF8);
                }
                catch
                {
                    // ignore write errors; this is best-effort
                }
            }
            catch
            {
                // no-op
            }

            proc.OutputDataReceived += (s, e) =>
            {
                if (e.Data is null) return;
                stdoutBuilder.AppendLine(e.Data);
                outputProgress?.Report(e.Data);
            };
            proc.ErrorDataReceived += (s, e) =>
            {
                if (e.Data is null) return;
                stderrBuilder.AppendLine(e.Data);
                outputProgress?.Report(e.Data);
            };

            try
            {
                // report final command-line for debugging/verification (also written permanently above)
                outputProgress?.Report($"Launching: {launchCmd}");

                if (!proc.Start())
                {
                    result.Error = "Failed to start PrusaSlicer process";
                    return result;
                }

                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var waitForExit = Task.Run(() =>
                {
                    proc.WaitForExit();
                    return proc.ExitCode;
                }, linkedCts.Token);

                var completed = await Task.WhenAny(waitForExit, Task.Delay(timeout.Value, linkedCts.Token)).ConfigureAwait(false);
                if (completed != waitForExit)
                {
                    try
                    {
                        linkedCts.Cancel();
                        if (!proc.HasExited)
                        {
                            proc.Kill(true);
                        }
                    }
                    catch { }
                    result.Error = $"Slicer timed out after {timeout.Value}";
                    result.Output = stdoutBuilder.ToString();
                    result.ExitCode = -1;
                    return result;
                }

                result.ExitCode = waitForExit.Result;
                result.Output = stdoutBuilder.ToString();
                result.Error = stderrBuilder.ToString();
                result.Success = result.ExitCode == 0 && File.Exists(outputGcodePath);

                // Best-effort parse
                TryParseLayerCount(result.Output, out int? layers);
                result.LayerCount = layers;
                TryParseEstimatedTime(result.Output, out TimeSpan? est);
                result.EstimatedPrintTime = est;

                if (!result.Success && File.Exists(outputGcodePath))
                    result.Success = true;

                // If gcode was produced, save a sidecar metadata JSON for downstream post-processing
                if (File.Exists(outputGcodePath))
                {
                    try
                    {
                        var meta = new
                        {
                            GeneratedAt = DateTime.UtcNow.ToString("o"),
                            StlPath = Path.GetFullPath(stlPath),
                            GcodePath = Path.GetFullPath(outputGcodePath),
                            LayerHeight = layerHeight,
                            InfillPercent = infillPercent,
                            DryingTemp = dryingTemp, // stored for later post-processor
                            DryingTime = dryingTime, // stored for later post-processor (minutes)
                            DryingTimeRT = dryingTimeRT, // stored for later post-processor (minutes, RT)
                            PrusaSlicerExe = prusaSlicerPath,
                            ProfileUsed = profilePath,
                            CommandLine = launchCmd
                        };

                        string metaPath = outputGcodePath + ".meta.json";
                        var js = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(metaPath, js, Encoding.UTF8);

                        // Run managed post-processor and fail the slice if the post-processor fails.
                        outputProgress?.Report("Running post-processor...");
                        var postResult = await PostProcessGcode.ProcessAsync(outputGcodePath, outputProgress, cancellationToken).ConfigureAwait(false);

                        // Append diagnostics from post-processor
                        if (postResult != null)
                        {
                            foreach (var m in postResult.Messages)
                                stderrBuilder.AppendLine(m);

                            foreach (var kv in postResult.Counts)
                                stdoutBuilder.AppendLine($"{kv.Key}: {kv.Value}");
                        }
                        else
                        {
                            stderrBuilder.AppendLine("Post-processor returned null result.");
                        }

                        // If post-processing failed, mark the slice as failed and return immediately with diagnostics.
                        if (postResult == null || !postResult.Success)
                        {
                            var msgSb = new StringBuilder();
                            msgSb.Append("Post-processing failed");
                            if (postResult != null && postResult.Messages.Count > 0)
                            {
                                msgSb.Append(": ");
                                msgSb.Append(string.Join(" | ", postResult.Messages));
                            }
                            else if (postResult == null)
                            {
                                msgSb.Append(": no result returned");
                            }

                            result.Success = false;
                            result.Error = msgSb.ToString();
                            result.Output = stdoutBuilder.ToString();
                            result.ExitCode = -2; // indicate post-processing failure
                            outputProgress?.Report("Post-processor failed; marking slice as failed.");
                            return result;
                        }

                        outputProgress?.Report("Post-processor finished.");
                        // update result outputs to include any post-processor messages
                        result.Output = stdoutBuilder.ToString();
                        result.Error = stderrBuilder.ToString();
                    }
                    catch (Exception ex)
                    {
                        // If the post-processor throws, treat as a post-processing failure and fail the slice.
                        result.Success = false;
                        result.Error = $"Post-processor threw an exception: {ex.Message}";
                        result.Output = stdoutBuilder.ToString();
                        result.ExitCode = -3;
                        outputProgress?.Report("Post-processor threw an exception; marking slice as failed.");
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }

        static bool TryParseLayerCount(string output, out int? layerCount)
        {
            layerCount = null;
            if (string.IsNullOrEmpty(output)) return false;
            var m = Regex.Match(output, @"(\d+)\s+layers", RegexOptions.IgnoreCase);
            if (!m.Success) m = Regex.Match(output, @"Layers[:=]\s*(\d+)", RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int v))
            {
                layerCount = v;
                return true;
            }
            return false;
        }
        //test
        static bool TryParseEstimatedTime(string output, out TimeSpan? estimated)
        {
            estimated = null;
            if (string.IsNullOrEmpty(output)) return false;
            var m = Regex.Match(output, @"Estimated.*?(\d{1,2}:\d{2}:\d{2})", RegexOptions.IgnoreCase);
            if (!m.Success) m = Regex.Match(output, @"Estimated.*?(\d{1,2}:\d{2})", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                if (TimeSpan.TryParse(m.Groups[1].Value, out var ts))
                {
                    estimated = ts;
                    return true;
                }
            }
            m = Regex.Match(output, @"Estimated.*?(\d+)\s*s(ec)?", RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int secs))
            {
                estimated = TimeSpan.FromSeconds(secs);
                return true;
            }
            return false;
        }
    }
}