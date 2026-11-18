using System;
using System.IO;

namespace PrinterGUI.Services
{
    public static class PrinterPaths
    {
        public static string UserHome
        {
            get
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrWhiteSpace(home))
                    home = Environment.GetEnvironmentVariable("HOME");
                if (string.IsNullOrWhiteSpace(home))
                    home = Environment.CurrentDirectory;
                return Path.GetFullPath(home);
            }
        }

        public static string ModelsDirectory => Path.Combine(UserHome, "stl");
        public static string GcodeDirectory => Path.Combine(UserHome, "gcode");
        public static string JsonDirectory => Path.Combine(UserHome, "json");
        public static string GummiesPointsJson => Path.Combine(JsonDirectory, "gummies_points.json");
        public static string SlicerConfig => Path.Combine(UserHome, "config.ini");
    }
}
