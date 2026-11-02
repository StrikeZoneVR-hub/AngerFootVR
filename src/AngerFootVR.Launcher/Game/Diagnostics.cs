using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace AngerFootVR.Launcher.Game
{
    public enum CheckLevel { Ok, Warn, Fail, Info }

    public sealed class Check
    {
        public string Title;
        public string Detail;
        public CheckLevel Level;
    }

    /// <summary>The VR CHECK page: everything that has to be right before pressing PLAY IN VR.</summary>
    public static class Diagnostics
    {
        /// <summary>The game's Steam build the mod was made and tested on.</summary>
        public const string TestedBuild = "";

        public static List<Check> Run(GameInstall g)
        {
            var list = new List<Check>();
            void Add(string title, CheckLevel level, string detail) => list.Add(new Check { Title = title, Level = level, Detail = detail });

            if (g == null)
            {
                Add("ANGER FOOT", CheckLevel.Fail, "Not found. Install it on Steam, or put this launcher in the game's folder.");
                return list;
            }

            Add("ANGER FOOT", CheckLevel.Ok, g.SteamBuildId == null ? g.Dir : $"Steam build {g.SteamBuildId}");

            if (!ModInstaller.BepInExInstalled(g))
                Add("BEPINEX 5", CheckLevel.Fail, "Not installed. Press INSTALL.");
            else
                Add("BEPINEX 5", ModInstaller.LoaderEnabled(g) ? CheckLevel.Ok : CheckLevel.Warn,
                    ModInstaller.LoaderEnabled(g) ? "Installed" : "Installed but switched off. Press INSTALL to switch it on.");

            var installed = ModInstaller.InstalledVersion(g);
            var payload = ModInstaller.PayloadVersion;
            if (installed == null)
                Add("ANGER FOOT VR", CheckLevel.Fail, "Not installed. Press INSTALL.");
            else if (payload != null && payload > installed)
                Add("ANGER FOOT VR", CheckLevel.Warn, $"Version {installed} installed; {payload} is ready. Press INSTALL to update.");
            else
                Add("ANGER FOOT VR", CheckLevel.Ok, $"Version {installed}");

            Add("UNITY OPENXR", ModInstaller.ProviderInstalled(g) ? CheckLevel.Ok : CheckLevel.Fail,
                ModInstaller.ProviderInstalled(g) ? "Installed (0.1.2)" : "Missing. INSTALL downloads it from Unity.");

            var chosen = LauncherSettings.Runtime;
            var active = GameLocator.ActiveRuntimeJson();
            var runtimeJson = string.IsNullOrEmpty(chosen) ? active : chosen;
            if (string.IsNullOrEmpty(runtimeJson) || !File.Exists(runtimeJson))
                Add("VR RUNTIME", CheckLevel.Fail, "No OpenXR runtime. Install Virtual Desktop's streamer, SteamVR or Meta Horizon Link.");
            else
                Add("VR RUNTIME", CheckLevel.Ok, GameLocator.FriendlyRuntime(runtimeJson) + (string.IsNullOrEmpty(chosen) ? " (PC default)" : " (chosen in OPTIONS)"));

            if (runtimeJson != null && runtimeJson.IndexOf("virtualdesktop", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var vd = GameLaunch.VirtualDesktopStreamerRunning();
                Add("VIRTUAL DESKTOP", vd ? CheckLevel.Ok : CheckLevel.Warn,
                    vd ? "Streamer running. Connect from your Quest, then PLAY IN VR." : "Streamer not running. Start it and connect from your Quest first.");
            }

            Add("STEAM", GameLaunch.SteamRunning() ? CheckLevel.Ok : CheckLevel.Info,
                GameLaunch.SteamRunning() ? "Running" : "Not running. The launcher starts it for you.");

            foreach (var gpu in Gpus()) Add("GRAPHICS", gpu.Level, gpu.Detail);

            var free = FreeMemoryGb();
            if (free > 0)
                Add("MEMORY", free >= 6 ? CheckLevel.Ok : CheckLevel.Warn,
                    free >= 6 ? $"{free:0.#} GB free" : $"{free:0.#} GB free. Close some programs first: VR needs about 6 GB.");

            var plan = PowerPlan();
            if (plan != null)
            {
                var saver = plan.IndexOf("saver", StringComparison.OrdinalIgnoreCase) >= 0;
                Add("POWER PLAN", saver ? CheckLevel.Warn : CheckLevel.Ok, saver ? plan + ". Switch to Balanced or Best performance for smooth VR." : plan);
            }

            if (GameLaunch.GameRunning()) Add("GAME", CheckLevel.Info, "Already running.");
            return list;
        }

        private static IEnumerable<Check> Gpus()
        {
            var found = new List<Check>();
            try
            {
                using (var q = new ManagementObjectSearcher("SELECT Name, DriverVersion FROM Win32_VideoController"))
                    foreach (ManagementObject o in q.Get())
                    {
                        var name = o["Name"] as string ?? "Unknown GPU";
                        if (name.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Basic", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        var discrete = Regex.IsMatch(name, "NVIDIA|GeForce|RTX|GTX|Radeon RX|Radeon Pro|Arc", RegexOptions.IgnoreCase);
                        found.Add(new Check { Level = discrete ? CheckLevel.Ok : CheckLevel.Warn, Detail = $"{name} (driver {o["DriverVersion"]})" + (discrete ? "" : ". A dedicated GPU is recommended for VR.") });
                    }
            }
            catch (Exception e)
            {
                LauncherLog.Write("GPU query failed: " + e.Message);
            }

            // laptops list the integrated GPU too: show the dedicated one when there is one
            return found.Any(c => c.Level == CheckLevel.Ok) ? found.Where(c => c.Level == CheckLevel.Ok) : found;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatus
        {
            public uint Length, Load;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll")]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

        public static double FreeMemoryGb()
        {
            var s = new MemoryStatus { Length = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
            return GlobalMemoryStatusEx(ref s) ? s.AvailPhys / 1073741824.0 : 0;
        }

        private static string PowerPlan()
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg", "/getactivescheme") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                using (var p = Process.Start(psi))
                {
                    var o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);
                    var m = Regex.Match(o, @"\(([^)]+)\)");
                    return m.Success ? m.Groups[1].Value : null;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
