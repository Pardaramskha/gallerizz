using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Gallerizz
{
    // Persistance minimale (clé=valeur) dans %APPDATA%\Gallerizz\settings.txt.
    internal static class Settings
    {
        private static readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;

        private static string FilePath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gallerizz");
                return Path.Combine(dir, "settings.txt");
            }
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) Values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch { }
        }

        internal static int GetInt(string key, int fallback)
        {
            EnsureLoaded();
            string s;
            int v;
            if (Values.TryGetValue(key, out s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                return v;
            return fallback;
        }

        internal static void SetInt(string key, int value)
        {
            EnsureLoaded();
            Values[key] = value.ToString(CultureInfo.InvariantCulture);
            Save();
        }

        private static void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(FilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var lines = new List<string>();
                foreach (var kv in Values) lines.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(FilePath, lines);
            }
            catch { }
        }
    }
}
