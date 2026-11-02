using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AngerFootVR.Core;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR;

/// <summary>
/// ANGER FOOT VR. Phase 0 (feasibility): does Unity's OpenXR provider start in this Unity 2019.4 build, and does the
/// built-in pipeline draw both eyes? It logs what it finds (VRCHECK / VRPERF lines) and takes dev commands from
/// BepInEx/AngerFootVR/cmd.txt (one per line) so a test can be driven from outside.
/// </summary>
[BepInPlugin("com.strikezonevr.angerfootvr", "ANGER FOOT VR", "0.0.1")]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource L;
    internal static Plugin Instance;
    private float _startXrAt = -1f, _nextReport, _nextCmd, _nextWatch;
    private int _frames;
    private float _frameTime, _worst;
    private string _cmdFile;

    /// <summary>The game quits itself past this much private memory (MB): a dev-run safety net, after a test loop of
    /// another mod once exhausted the laptop's memory.</summary>
    private static readonly long MaxMemoryMb = long.TryParse(Environment.GetEnvironmentVariable("AFVR_MAX_MB"), out var mb) ? mb : 9000;

    private void Awake()
    {
        L = Logger;
        Instance = this;
        new HarmonyLib.Harmony("com.strikezonevr.angerfootvr").PatchAll(typeof(Plugin).Assembly);
        L.LogInfo($"ANGER FOOT VR 0.0.1 loading. Unity {Application.unityVersion}, game {Application.version}, graphics {SystemInfo.graphicsDeviceType} ({SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsMemorySize} MB)");
        var displays = new System.Collections.Generic.List<XRDisplaySubsystemDescriptor>();
        SubsystemManager.GetSubsystemDescriptors(displays);
        L.LogInfo($"VRCHECK XR display descriptors: {displays.Count} [{string.Join(", ", displays.Select(d => d.id).ToArray())}]");
        _cmdFile = Path.Combine(Paths.BepInExRootPath, Path.Combine("AngerFootVR", "cmd.txt"));
        Directory.CreateDirectory(Path.GetDirectoryName(_cmdFile));
        File.WriteAllText(_cmdFile, "");
        // VR starts a few seconds in, once the game's first scene is up (AFVR_NOXR=1 leaves it off)
        if (Environment.GetEnvironmentVariable("AFVR_NOXR") != "1") _startXrAt = 4f;
    }

    private void Update()
    {
        var now = Time.realtimeSinceStartup;
        if (_startXrAt > 0f && now >= _startXrAt)
        {
            _startXrAt = -1f;
            XrManager.Initialize();
        }

        XrManager.Update();
        var dt = Time.unscaledDeltaTime;
        _frames++;
        _frameTime += dt;
        _worst = Mathf.Max(_worst, dt);
        if (now >= _nextReport)
        {
            _nextReport = now + 5f;
            Report();
        }

        if (now >= _nextWatch)
        {
            _nextWatch = now + 2f;
            Watchdog();
        }

        if (now >= _nextCmd)
        {
            _nextCmd = now + 0.25f;
            Commands();
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MemoryCounters
    {
        public uint cb, PageFaultCount;
        public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
            QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage;
    }

    [System.Runtime.InteropServices.DllImport("kernel32", EntryPoint = "K32GetProcessMemoryInfo")]
    private static extern bool GetProcessMemoryInfo(IntPtr process, out MemoryCounters counters, uint size);

    /// <summary>The game's private memory in MB, from Windows (Mono's Process counters read 0 here).</summary>
    private static long MemoryMb()
    {
        using (var p = Process.GetCurrentProcess())
            return GetProcessMemoryInfo(p.Handle, out var c, (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MemoryCounters)))
                ? (long)c.PrivateUsage.ToUInt64() / (1024 * 1024)
                : -1;
    }

    private static void Watchdog()
    {
        var mb = MemoryMb();
        if (mb <= MaxMemoryMb) return;
        L.LogError($"SAFETY: the game is using {mb} MB (limit {MaxMemoryMb} MB); quitting before it strains the PC.");
        Application.Quit();
    }

    private void Report()
    {
        var cam = Camera.main;
        var avg = _frames > 0 ? _frameTime / _frames * 1000f : 0f;
        var mb = MemoryMb();
        L.LogInfo($"VRPERF frame {avg:F2} ms avg ({(avg > 0 ? 1000f / avg : 0):F1} fps), worst {_worst * 1000f:F1} ms, memory {mb} MB | xr {XrManager.Current} running={XrManager.IsRunning} XRSettings.enabled={XRSettings.enabled} eye {XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight} | camera {(cam != null ? $"'{cam.name}' stereo={cam.stereoEnabled} target={cam.stereoTargetEye} fov={cam.fieldOfView:F0}" : "none")}");
        _frames = 0;
        _frameTime = 0;
        _worst = 0;
    }

    private void Commands()
    {
        string[] lines;
        try
        {
            if (!File.Exists(_cmdFile) || new FileInfo(_cmdFile).Length == 0) return;
            lines = File.ReadAllLines(_cmdFile);
            File.WriteAllText(_cmdFile, "");
        }
        catch
        {
            return;
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            L.LogInfo("> " + line);
            var sp = line.IndexOf(' ');
            var cmd = sp < 0 ? line : line.Substring(0, sp);
            var arg = sp < 0 ? "" : line.Substring(sp + 1);
            try
            {
                switch (cmd)
                {
                    case "state":
                        Report();
                        break;
                    case "xr":
                        XrManager.Initialize();
                        break;
                    case "shot":
                    {
                        var dir = Path.Combine(Paths.BepInExRootPath, Path.Combine("AngerFootVR", "shots"));
                        Directory.CreateDirectory(dir);
                        var file = Path.Combine(dir, $"{(arg.Length > 0 ? arg : "shot")}_{DateTime.Now:HHmmss}.png");
                        ScreenCapture.CaptureScreenshot(file);
                        L.LogInfo("Screenshot -> " + file);
                        break;
                    }
                    case "cameras":
                        foreach (var c in Camera.allCameras)
                            L.LogInfo($"CAMERA '{c.name}' depth={c.depth} enabled={c.enabled} stereo={c.stereoEnabled} eye={c.stereoTargetEye} target={(c.targetTexture != null ? c.targetTexture.name : "screen")} mask={c.cullingMask:X} pos={c.transform.position} rot={c.transform.eulerAngles} parent={(c.transform.parent != null ? c.transform.parent.name : "none")} clip={c.nearClipPlane}-{c.farClipPlane}");
                        break;
                    case "scene":
                        for (var i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                            L.LogInfo("SCENE " + UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).name);
                        break;
                    case "eyeshot":
                        Dev.EyeShot.Arm(arg.Length > 0 ? arg : "eye");
                        break;
                    case "level":
                        GameState.LoadFirstLevel(LoadingTransition.FadeToAndFromBlack);
                        break;
                    case "title":
                        GameState.LoadTitleScreen(LoadingTransition.FadeFromBlackOnly);
                        break;
                    case "quit":
                        Application.Quit();
                        break;
                    default:
                        L.LogWarning("Unknown command " + cmd);
                        break;
                }
            }
            catch (Exception e)
            {
                L.LogError($"Command '{line}' failed: {e}");
            }
        }
    }
}
