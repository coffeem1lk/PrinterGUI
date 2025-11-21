using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace PrinterGUI.Services
{
    public class PostProcessResult
    {
        public bool Success { get; set; }
        public Dictionary<string,int> Counts { get; } = new();
        public List<string> Messages { get; } = new();
    }

    /// <summary>
    /// Post-process a generated .gcode file using the sidecar .meta.json produced by GcodeGenerator.
    /// Replaces these exact quoted placeholders:
    ///   M141 S"drying_temp"       -> M141 S<dryingTemp>
    ///   G4 S"drying_time"         -> G4 S<dryingTimeSeconds>
    ///   G4 S"drying_time_RT"      -> G4 S<dryingTimeRTSeconds>
    /// Creates a backup <file>.orig.gcode (if not present) and writes a <file>.postproc.json report.
    /// </summary>
    public static class PostProcessGcode
    {
        public static async Task<PostProcessResult> ProcessAsync(string gcodePath, IProgress<string>? progress = null, CancellationToken ct = default)
        {
            var result = new PostProcessResult();
            if (string.IsNullOrWhiteSpace(gcodePath)) { result.Messages.Add("No gcode path provided."); return result; }
            var gpath = Path.GetFullPath(gcodePath);
            if (!File.Exists(gpath))
            {
                result.Messages.Add($"G-code not found: {gpath}");
                return result;
            }

            ct.ThrowIfCancellationRequested();

            // read meta
            var metaPath = gpath + ".meta.json";
            Dictionary<string, object?> meta = new();
            if (File.Exists(metaPath))
            {
                try
                {
                    var mt = await File.ReadAllTextAsync(metaPath, ct).ConfigureAwait(false);
                    meta = JsonSerializer.Deserialize<Dictionary<string, object?>>(mt) ?? new();
                }
                catch (Exception ex)
                {
                    result.Messages.Add($"Failed to read meta: {ex.Message}");
                }
            }
            else
            {
                result.Messages.Add("No sidecar meta found; placeholders will only be replaced if CLI overrides are provided.");
            }

            // helpers to resolve values (meta keys use same names written by GcodeGenerator)
            static int? ToInt(object? o)
            {
                if (o is null) return null;
                if (o is JsonElement je)
                {
                    if (je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var v)) return v;
                    if (je.ValueKind == JsonValueKind.String && int.TryParse(je.GetString(), out v)) return v;
                    return null;
                }
                if (o is int i) return i;
                if (o is long l) return (int)l;
                if (o is double d) return (int)d;
                if (int.TryParse(o.ToString(), out var p)) return p;
                return null;
            }

            var dryingTemp = ToInt(meta.GetValueOrDefault("DryingTemp"));
            var dryingTimeMin = ToInt(meta.GetValueOrDefault("DryingTime"));
            var dryingTimeRTMin = ToInt(meta.GetValueOrDefault("DryingTimeRT"));

            // make backup if not present
            try
            {
                var backup = gpath + ".orig.gcode";
                if (!File.Exists(backup))
                {
                    File.Copy(gpath, backup);
                    result.Messages.Add($"Backup created: {Path.GetFileName(backup)}");
                }
            }
            catch (Exception ex)
            {
                result.Messages.Add($"Backup failed: {ex.Message}");
            }

            ct.ThrowIfCancellationRequested();

            // Read lines and replace placeholders only when the exact quoted placeholders are present
            string[] lines;
            try
            {
                lines = await File.ReadAllLinesAsync(gpath, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result.Messages.Add($"Failed to read gcode: {ex.Message}");
                return result;
            }

            var outLines = new List<string>(lines.Length);
            var reM141 = new Regex(@"^\s*(M141)\s+S""([^""]+)""(.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var reG4 = new Regex(@"^\s*(G4)\s+S""([^""]+)""(.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            int countM141 = 0, countG4_drying_time = 0, countG4_drying_time_rt = 0;

            int? dryingTimeSeconds = dryingTimeMin.HasValue ? dryingTimeMin.Value * 60 : null;
            int? dryingTimeRTSeconds = dryingTimeRTMin.HasValue ? dryingTimeRTMin.Value * 60 : null;

            foreach (var ln in lines)
            {
                ct.ThrowIfCancellationRequested();
                var replaced = false;
                var m = reM141.Match(ln);
                if (m.Success)
                {
                    var inner = m.Groups[2].Value;
                    if (string.Equals(inner, "drying_temp", StringComparison.OrdinalIgnoreCase) && dryingTemp.HasValue)
                    {
                        outLines.Add($"{m.Groups[1].Value} S{dryingTemp.Value}{m.Groups[3].Value}");
                        countM141++;
                        replaced = true;
                    }
                }

                if (!replaced)
                {
                    var m2 = reG4.Match(ln);
                    if (m2.Success)
                    {
                        var inner = m2.Groups[2].Value;
                        if (string.Equals(inner, "drying_time", StringComparison.OrdinalIgnoreCase) && dryingTimeSeconds.HasValue)
                        {
                            outLines.Add($"{m2.Groups[1].Value} S{dryingTimeSeconds.Value}{m2.Groups[3].Value}");
                            countG4_drying_time++;
                            replaced = true;
                        }
                        else if (string.Equals(inner, "drying_time_RT", StringComparison.OrdinalIgnoreCase) && dryingTimeRTSeconds.HasValue)
                        {
                            outLines.Add($"{m2.Groups[1].Value} S{dryingTimeRTSeconds.Value}{m2.Groups[3].Value}");
                            countG4_drying_time_rt++;
                            replaced = true;
                        }
                    }
                }

                if (!replaced) outLines.Add(ln);
            }

            result.Counts["M141"] = countM141;
            result.Counts["G4_drying_time"] = countG4_drying_time;
            result.Counts["G4_drying_time_RT"] = countG4_drying_time_rt;

            // Insert processing marker near top (after initial comments) if not already present
            bool hasMarker = outLines.Count > 0 && outLines.Take(50).Any(l => l != null && l.Contains("; POST-PROCESSED-BY: post_process_gcode.cs"));
            if (!hasMarker)
            {
                var header = new List<string>
                {
                    "; POST-PROCESSED-BY: post_process_gcode.cs",
                    $"; ProcessedAt: {DateTime.UtcNow:O}",
                    $"; Replacements: M141={countM141}, G4_drying_time={countG4_drying_time}, G4_drying_time_RT={countG4_drying_time_rt}",
                    ""
                };

                int insertIndex = 0;
                for (int i = 0; i < outLines.Count; i++)
                {
                    var s = outLines[i].Trim();
                    if (s.Length > 0 && !s.StartsWith(";") && !s.StartsWith("("))
                    {
                        insertIndex = i;
                        break;
                    }
                    insertIndex = i + 1;
                }

                outLines.InsertRange(insertIndex, header);
            }

            // Write file atomically
            try
            {
                var tmp = gpath + ".tmp";
                await File.WriteAllLinesAsync(tmp, outLines, ct).ConfigureAwait(false);
                File.Replace(tmp, gpath, gpath + ".bak", ignoreMetadataErrors: true);
                result.Success = true;
                result.Messages.Add("Placeholders replaced and file updated.");
            }
            catch (Exception ex)
            {
                // fallback write
                try
                {
                    await File.WriteAllLinesAsync(gpath, outLines, ct).ConfigureAwait(false);
                    result.Success = true;
                    result.Messages.Add("Placeholders replaced and file updated (fallback).");
                }
                catch (Exception ex2)
                {
                    result.Messages.Add($"Failed to write updated gcode: {ex.Message}; fallback: {ex2.Message}");
                    result.Success = false;
                }
            }

            // Write postproc JSON report
            try
            {
                var post = new
                {
                    ProcessedAt = DateTime.UtcNow.ToString("o"),
                    Counts = result.Counts,
                    Messages = result.Messages,
                    MetaFound = File.Exists(metaPath)
                };
                var postJson = JsonSerializer.Serialize(post, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(gpath + ".postproc.json", postJson, ct).ConfigureAwait(false);
            }
            catch
            {
                // ignore errors writing report
            }

            progress?.Report($"Post-processor: replaced {countM141} M141, {countG4_drying_time} G4(drying_time), {countG4_drying_time_rt} G4(drying_time_RT)");
            return result;
        }
    }
}