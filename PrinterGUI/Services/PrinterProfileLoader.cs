using System;
using System.IO;
using System.Text.Json;
using PrinterGUI.Models;

namespace PrinterGUI.Services
{
    public static class PrinterProfileLoader
    {
        public static PrinterProfile Load(string? path = null)
        {
            path ??= Path.Combine(AppContext.BaseDirectory, "printer-profile.json");

            if (!File.Exists(path))
                throw new FileNotFoundException("Printer profile not found. Configure printer-profile.json before starting the application.", path);

            try
            {
                var json = File.ReadAllText(path);
                var profile = JsonSerializer.Deserialize<PrinterProfile>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (profile is null)
                    throw new InvalidDataException("Printer profile is empty.");

                if (string.IsNullOrWhiteSpace(profile.Name))
                    throw new InvalidDataException("Printer profile name must not be empty.");

                if (profile.DoorTravelMm <= 0)
                    throw new InvalidDataException("Printer profile DoorTravelMm must be greater than zero.");

                if (profile.BedSizeXMm <= 0 || profile.BedSizeYMm <= 0)
                    throw new InvalidDataException("Printer profile bed dimensions must be greater than zero.");

                if (profile.ZHomeXMm < 0 || profile.ZHomeXMm > profile.BedSizeXMm ||
                    profile.ZHomeYMm < 0 || profile.ZHomeYMm > profile.BedSizeYMm)
                    throw new InvalidDataException("Printer profile Z homing coordinates must be within the bed dimensions.");

                if (profile.OdfZHomeXMm < 0 || profile.OdfZHomeXMm > profile.BedSizeXMm ||
                    profile.OdfZHomeYMm < 0 || profile.OdfZHomeYMm > profile.BedSizeYMm)
                    throw new InvalidDataException("Printer profile ODF Z homing coordinates must be within the bed dimensions.");

                return profile;
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Printer profile is not valid JSON: {ex.Message}", ex);
            }
        }
    }
}
