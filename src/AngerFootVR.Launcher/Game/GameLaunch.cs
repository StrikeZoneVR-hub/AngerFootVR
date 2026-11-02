using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace AngerFootVR.Launcher.Game
{
    public static class GameLaunch
    {
        public static bool GameRunning() => Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GameInstall.ExeName)).Length > 0;
        public static bool SteamRunning() => Process.GetProcessesByName("steam").Length > 0;

        /// <summary>
        /// Starts the game directly (not through Steam's launch options) so the chosen OpenXR runtime and play mode reach
        /// it as environment variables. Steam still has to be running for the game's Steamworks.
        /// </summary>
        public static Process Start(GameInstall game, bool vr, string runtimeJson, Action<string> status)
        {
            if (!File.Exists(game.Exe)) throw new FileNotFoundException("Game executable not found", game.Exe);

            // lets the exe start without Steam relaunching it through the Steam client (and dropping our environment)
            var appId = Path.Combine(game.Dir, "steam_appid.txt");
            if (!File.Exists(appId)) File.WriteAllText(appId, GameInstall.SteamAppId.ToString());

            if (!SteamRunning())
            {
                var root = GameLocator.SteamRoot();
                var steamExe = root == null ? null : Path.Combine(root, "steam.exe");
                if (steamExe != null && File.Exists(steamExe))
                {
                    status?.Invoke("WAKING UP STEAM...");
                    Process.Start(new ProcessStartInfo(steamExe, "-silent") { UseShellExecute = true });
                    for (var i = 0; i < 40 && !SteamRunning(); i++) Thread.Sleep(500);
                    Thread.Sleep(4000); // let it sign in
                }
            }

            var psi = new ProcessStartInfo(game.Exe) { WorkingDirectory = game.Dir, UseShellExecute = false };
            psi.EnvironmentVariables["AFVR_MODE"] = vr ? "vr" : "flat";
            // never a dev run's settings in a player's game
            foreach (var dev in new[] { "AFVR_DEVSAVES", "AFVR_REALSAVES", "AFVR_OPENING", "AFVR_NOXR", "AFVR_AO", "AFVR_TEXLIMIT", "AFVR_MAX_MB" })
                psi.EnvironmentVariables.Remove(dev);
            psi.EnvironmentVariables.Remove("XR_RUNTIME_JSON");
            if (vr && !string.IsNullOrEmpty(runtimeJson) && File.Exists(runtimeJson))
                psi.EnvironmentVariables["XR_RUNTIME_JSON"] = runtimeJson;

            // the same in a file the mod reads as it starts (an environment doesn't survive the game being restarted by
            // Steam or Windows on its way up)
            try
            {
                var dir = Path.Combine(game.BepInExDir, "AngerFootVR");
                Directory.CreateDirectory(dir);
                File.WriteAllLines(Path.Combine(dir, "launch.txt"), new[]
                {
                    "mode=" + (vr ? "vr" : "flat"),
                    "runtime=" + (vr && !string.IsNullOrEmpty(runtimeJson) && File.Exists(runtimeJson) ? runtimeJson : ""),
                    "at=" + DateTime.UtcNow.Ticks,
                });
            }
            catch (Exception e)
            {
                LauncherLog.Write("couldn't write launch.txt: " + e.Message);
            }

            LauncherLog.Write($"Launching {(vr ? "VR" : "flat")} runtime={(string.IsNullOrEmpty(runtimeJson) ? "(PC default)" : runtimeJson)}");
            status?.Invoke(vr ? "STARTING ANGER FOOT IN VR..." : "STARTING ANGER FOOT...");
            return Process.Start(psi);
        }

        public static bool VirtualDesktopStreamerRunning() =>
            Process.GetProcesses().Any(p =>
            {
                try
                {
                    return p.ProcessName.StartsWith("VirtualDesktop.Streamer", StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            });

        public static void OpenFolder(string path)
        {
            if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
        }

        public static void OpenFile(string path)
        {
            if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }
}
