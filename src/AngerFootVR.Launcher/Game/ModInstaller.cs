using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AngerFootVR.Launcher.Game
{
    /// <summary>
    /// Installs Anger Foot VR into the game folder: BepInEx 5 and the mod from the release's AngerFootVR-Payload folder,
    /// and Unity's OpenXR provider (0.1.2-preview.2, the one that starts on the game's Unity 2019.4) downloaded from
    /// Unity's package registry (never redistributed by the mod). The game's own files are never changed.
    /// </summary>
    public static class ModInstaller
    {
        public const string OpenXRPackageUrl = "https://download.packages.unity.com/com.unity.xr.openxr/-/com.unity.xr.openxr-0.1.2-preview.2.tgz";
        public const string OpenXRPackageSha256 = "e1be113da24f2f9ca3ce290b5f2efb2f645c70e095d0f984f1cb94d98cae6804";

        public static bool BepInExInstalled(GameInstall g) =>
            File.Exists(Path.Combine(g.Dir, "winhttp.dll")) &&
            File.Exists(g.DoorstopIni) &&
            File.Exists(Path.Combine(g.BepInExDir, "core", "BepInEx.dll"));

        /// <summary>Whether BepInEx's loader is switched on (it is off between dev runs).</summary>
        public static bool LoaderEnabled(GameInstall g) =>
            File.Exists(g.DoorstopIni) && Regex.IsMatch(File.ReadAllText(g.DoorstopIni), @"(?m)^\s*enabled\s*=\s*true");

        public static bool ProviderInstalled(GameInstall g) =>
            File.Exists(g.OpenXRProvider) && File.Exists(g.OpenXRLoader) && File.Exists(g.SubsystemManifest);

        public static Version InstalledVersion(GameInstall g) => DllVersion(g.PluginDll);

        public static string PayloadDir => Directory.Exists(LauncherPaths.Payload) ? LauncherPaths.Payload : null;

        public static Version PayloadVersion =>
            PayloadDir == null ? null : DllVersion(Path.Combine(PayloadDir, "BepInEx", "plugins", "AngerFootVR", "AngerFootVR.dll"));

        public static bool NeedsInstall(GameInstall g) =>
            !BepInExInstalled(g) || !LoaderEnabled(g) || !ProviderInstalled(g) || InstalledVersion(g) == null ||
            (PayloadVersion != null && PayloadVersion > InstalledVersion(g));

        private static Version DllVersion(string path)
        {
            if (!File.Exists(path)) return null;
            var v = FileVersionInfo.GetVersionInfo(path);
            return new Version(v.FileMajorPart, v.FileMinorPart, v.FileBuildPart);
        }

        public static void Install(GameInstall g, Action<string> status)
        {
            if (PayloadDir != null)
            {
                status("COPYING BEPINEX AND ANGER FOOT VR...");
                CopyTree(PayloadDir, g.Dir);
            }
            else if (!BepInExInstalled(g) || InstalledVersion(g) == null)
            {
                throw new InvalidOperationException("The AngerFootVR-Payload folder is missing. Keep it next to Anger Foot VR.exe (it comes in the release zip).");
            }

            EnableLoader(g);
            if (!ProviderInstalled(g)) InstallProvider(g, status);
            status("ANGER FOOT VR IS INSTALLED.");
            LauncherLog.Write("Install complete: mod " + InstalledVersion(g));
        }

        private static void EnableLoader(GameInstall g)
        {
            if (!File.Exists(g.DoorstopIni)) return;
            var text = File.ReadAllText(g.DoorstopIni);
            var on = Regex.Replace(text, @"(?m)^(\s*enabled\s*=\s*)\w+", "${1}true");
            if (on != text) File.WriteAllText(g.DoorstopIni, on);
        }

        private static void CopyTree(string from, string to)
        {
            foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, dir.Substring(from.Length).TrimStart('\\')));
            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                var rel = file.Substring(from.Length).TrimStart('\\');
                var dest = Path.Combine(to, rel);
                // keep the player's own settings
                if (rel.StartsWith(@"BepInEx\config\", StringComparison.OrdinalIgnoreCase) && File.Exists(dest)) continue;
                File.Copy(file, dest, true);
            }
        }

        private static void InstallProvider(GameInstall g, Action<string> status)
        {
            status("DOWNLOADING UNITY'S OPENXR PLUGIN (3 MB)...");
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            byte[] tgz;
            using (var web = new WebClient())
            {
                web.Headers[HttpRequestHeader.UserAgent] = "AngerFootVR-Launcher";
                tgz = web.DownloadData(OpenXRPackageUrl);
            }

            using (var sha = SHA256.Create())
            {
                var hash = BitConverter.ToString(sha.ComputeHash(tgz)).Replace("-", "").ToLowerInvariant();
                if (hash != OpenXRPackageSha256) throw new InvalidDataException("The OpenXR package download did not match the expected checksum.");
            }

            status("INSTALLING THE OPENXR PLUGIN...");
            Directory.CreateDirectory(Path.GetDirectoryName(g.OpenXRProvider));
            Directory.CreateDirectory(Path.GetDirectoryName(g.SubsystemManifest));
            var wanted = new (string Entry, string Dest)[]
            {
                ("package/Runtime/windows/x64/UnityOpenXR.dll", g.OpenXRProvider),
                ("package/RuntimeLoaders/windows/x64/openxr_loader.dll", g.OpenXRLoader),
                ("package/Runtime/UnitySubsystemsManifest.json", g.SubsystemManifest),
            };
            using (var gz = new GZipStream(new MemoryStream(tgz), CompressionMode.Decompress))
            {
                foreach (var (name, data) in Tar(gz))
                foreach (var w in wanted)
                    if (name == w.Entry)
                        File.WriteAllBytes(w.Dest, data);
            }

            if (!ProviderInstalled(g)) throw new InvalidDataException("The OpenXR package did not contain the expected files.");
        }

        private static System.Collections.Generic.IEnumerable<(string Name, byte[] Data)> Tar(Stream s)
        {
            var header = new byte[512];
            string paxPath = null;
            while (ReadFull(s, header, 512))
            {
                if (header[0] == 0) yield break;
                var name = Cstr(header, 0, 100);
                var prefix = Cstr(header, 345, 155);
                if (prefix.Length > 0) name = prefix + "/" + name;
                var size = Convert.ToInt64(Cstr(header, 124, 12).Trim().Trim('\0').PadLeft(1, '0'), 8);
                var type = (char)header[156];
                var data = new byte[size];
                ReadFull(s, data, (int)size);
                var pad = (int)((512 - size % 512) % 512);
                if (pad > 0) ReadFull(s, new byte[pad], pad);

                if (type == 'x')
                {
                    foreach (var rec in Encoding.UTF8.GetString(data).Split('\n'))
                    {
                        var i = rec.IndexOf(" path=", StringComparison.Ordinal);
                        if (i >= 0) paxPath = rec.Substring(i + 6);
                    }

                    continue;
                }

                if (paxPath != null)
                {
                    name = paxPath;
                    paxPath = null;
                }

                if (type == '0' || type == '\0') yield return (name, data);
            }
        }

        private static string Cstr(byte[] b, int off, int len)
        {
            var end = off;
            while (end < off + len && b[end] != 0) end++;
            return Encoding.ASCII.GetString(b, off, end - off);
        }

        private static bool ReadFull(Stream s, byte[] buf, int n)
        {
            var got = 0;
            while (got < n)
            {
                var k = s.Read(buf, got, n - got);
                if (k <= 0) return false;
                got += k;
            }

            return true;
        }

        /// <summary>Takes the mod out: BepInEx's loader, the OpenXR files and the mod (the game's own files are untouched;
        /// your settings and the launcher art stay unless <paramref name="everything"/>).</summary>
        public static void Uninstall(GameInstall g, bool everything = false)
        {
            foreach (var f in new[]
                     {
                         Path.Combine(g.Dir, "winhttp.dll"), g.DoorstopIni, Path.Combine(g.Dir, ".doorstop_version"),
                         g.OpenXRProvider, g.OpenXRLoader, g.SubsystemManifest,
                     })
                if (File.Exists(f))
                    File.Delete(f);
            var manifestDir = Path.GetDirectoryName(g.SubsystemManifest);
            if (manifestDir != null && Directory.Exists(manifestDir) && Directory.GetFileSystemEntries(manifestDir).Length == 0) Directory.Delete(manifestDir);
            var plugin = Path.GetDirectoryName(g.PluginDll);
            if (plugin != null && Directory.Exists(plugin)) Directory.Delete(plugin, true);
            if (everything && Directory.Exists(g.BepInExDir)) Directory.Delete(g.BepInExDir, true);
            LauncherLog.Write("Uninstalled Anger Foot VR from " + g.Dir + (everything ? " (everything)" : ""));
        }
    }
}
