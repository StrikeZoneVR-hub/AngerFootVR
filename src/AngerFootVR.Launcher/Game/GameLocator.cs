using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AngerFootVR.Launcher.Game
{
    /// <summary>Paths of the installed game (Steam app 1978590).</summary>
    public sealed class GameInstall
    {
        public const string ExeName = "Anger Foot.exe";
        public const int SteamAppId = 1978590;

        public string Dir;
        public string Exe => Path.Combine(Dir, ExeName);
        public string DataDir => Path.Combine(Dir, "Anger Foot_Data");
        public string BepInExDir => Path.Combine(Dir, "BepInEx");
        public string PluginDll => Path.Combine(BepInExDir, "plugins", "AngerFootVR", "AngerFootVR.dll");
        public string ConfigFile => Path.Combine(BepInExDir, "config", "com.strikezonevr.angerfootvr.cfg");
        public string LogFile => Path.Combine(BepInExDir, "LogOutput.log");
        public string DoorstopIni => Path.Combine(Dir, "doorstop_config.ini");
        public string OpenXRProvider => Path.Combine(DataDir, "Plugins", "x86_64", "UnityOpenXR.dll");
        public string OpenXRLoader => Path.Combine(DataDir, "Plugins", "x86_64", "openxr_loader.dll");
        public string SubsystemManifest => Path.Combine(DataDir, "UnitySubsystems", "UnityOpenXR", "UnitySubsystemsManifest.json");

        /// <summary>What the mod takes from your copy of the game for the launcher (its title music, logos and title
        /// picture), written the first time the game runs with the mod.</summary>
        public string ArtDir => Path.Combine(BepInExDir, "AngerFootVR", "launcher");

        public string SteamBuildId;
    }

    public static class GameLocator
    {
        private static string SettingsFile => Path.Combine(LauncherPaths.AppData, "game_path.txt");

        public static GameInstall Find()
        {
            foreach (var dir in Candidates())
            {
                if (string.IsNullOrEmpty(dir) || !File.Exists(Path.Combine(dir, GameInstall.ExeName))) continue;
                var g = new GameInstall { Dir = dir, SteamBuildId = ReadBuildId(dir) };
                Remember(dir);
                return g;
            }

            return null;
        }

        public static void Remember(string dir)
        {
            try
            {
                Directory.CreateDirectory(LauncherPaths.AppData);
                File.WriteAllText(SettingsFile, dir);
            }
            catch
            {
                // best effort
            }
        }

        private static IEnumerable<string> Candidates()
        {
            // 1. Next to the launcher (dropped into the game folder), or one folder up.
            var here = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            yield return here;
            yield return Path.GetDirectoryName(here);

            // 2. Last known location.
            if (File.Exists(SettingsFile)) yield return File.ReadAllText(SettingsFile).Trim();

            // 3. Steam libraries.
            foreach (var lib in SteamLibraries())
            {
                var manifest = Path.Combine(lib, "steamapps", $"appmanifest_{GameInstall.SteamAppId}.acf");
                var installDir = "Anger Foot";
                if (File.Exists(manifest))
                {
                    var m = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
                    if (m.Success) installDir = m.Groups[1].Value;
                }

                yield return Path.Combine(lib, "steamapps", "common", installDir);
            }
        }

        public static string SteamRoot()
        {
            var path = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            if (string.IsNullOrEmpty(path)) path = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
            return string.IsNullOrEmpty(path) ? null : path.Replace('/', '\\');
        }

        public static IEnumerable<string> SteamLibraries()
        {
            var root = SteamRoot();
            if (root == null) yield break;
            yield return root;
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) yield break;
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                yield return m.Groups[1].Value.Replace(@"\\", @"\");
        }

        private static string ReadBuildId(string gameDir)
        {
            try
            {
                var steamapps = Path.GetDirectoryName(Path.GetDirectoryName(gameDir));
                var manifest = Path.Combine(steamapps ?? "", $"appmanifest_{GameInstall.SteamAppId}.acf");
                if (!File.Exists(manifest)) return null;
                var m = Regex.Match(File.ReadAllText(manifest), "\"buildid\"\\s+\"(\\d+)\"");
                return m.Success ? m.Groups[1].Value : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>OpenXR runtimes on this PC: (display name, manifest path).</summary>
        public static List<(string Name, string Json)> OpenXRRuntimes()
        {
            var list = new List<(string, string)>();
            void Add(string name, string json)
            {
                if (!string.IsNullOrEmpty(json) && File.Exists(json) && list.All(r => !string.Equals(r.Item2, json, StringComparison.OrdinalIgnoreCase)))
                    list.Add((name, json));
            }

            Add("VIRTUAL DESKTOP", @"C:\Program Files\Virtual Desktop Streamer\OpenXR\virtualdesktop-openxr.json");
            Add("META HORIZON LINK", @"C:\Program Files\Oculus\Support\oculus-runtime\oculus_openxr_64.json");
            Add("META HORIZON LINK", @"C:\Program Files\Meta Horizon\Support\oculus-runtime\oculus_openxr_64.json");
            foreach (var lib in SteamLibraries())
                Add("STEAMVR", Path.Combine(lib, "steamapps", "common", "SteamVR", "steamxr_win64.json"));
            return list;
        }

        public static string ActiveRuntimeJson() =>
            Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Khronos\OpenXR\1", "ActiveRuntime", null) as string;

        public static string FriendlyRuntime(string json)
        {
            if (string.IsNullOrEmpty(json)) return "None set";
            var lower = json.ToLowerInvariant();
            if (lower.Contains("virtualdesktop")) return "Virtual Desktop (VDXR)";
            if (lower.Contains("oculus")) return "Meta Horizon Link";
            if (lower.Contains("steamxr") || lower.Contains("steamvr")) return "SteamVR";
            if (lower.Contains("mock")) return "Unity mock runtime";
            return Path.GetFileNameWithoutExtension(json);
        }
    }

    public static class LauncherPaths
    {
        public static string AppData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AngerFootVR");
        public static string Payload => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AngerFootVR-Payload");
    }

    public static class LauncherLog
    {
        private static readonly object Gate = new object();

        public static string FilePath => Path.Combine(LauncherPaths.AppData, "launcher.log");

        public static void Write(string line)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(LauncherPaths.AppData);
                    File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}");
                }
            }
            catch
            {
                // logging must never break the launcher
            }
        }
    }
}
