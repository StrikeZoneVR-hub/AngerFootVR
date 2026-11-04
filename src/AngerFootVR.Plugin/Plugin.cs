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
[BepInPlugin("com.strikezonevr.angerfootvr", "ANGER FOOT VR", "1.2.0")]
public class Plugin : BaseUnityPlugin
{
    /// <summary>A dev run (tools/game.sh: test saves, action log, no opening). Never when the launcher started the game
    /// (it sets AFVR_MODE): a player's run once came up as a dev run and played on the test saves.</summary>
    internal static readonly bool DevRun = IsDevRun();

    /// <summary>tools/game.sh starts a dev run with a token in BepInEx/AngerFootVR/dev-run.token and the same token in the
    /// environment, and deletes the file when the run ends: a dev setting left in some other program's environment
    /// (Steam's, once) can't make a player's run a dev run.</summary>
    private static bool IsDevRun()
    {
        if (Environment.GetEnvironmentVariable("AFVR_DEVSAVES") != "1" || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AFVR_MODE"))) return false;
        // the launcher leaves launch.txt for the run it starts (it survives Steam restarting the game; the environment
        // doesn't): a run the launcher started is the player's, whatever its environment says (one of theirs logged
        // "Dev run"). tools/game.sh deletes the file before a dev run.
        if (File.Exists(Path.Combine(Paths.BepInExRootPath, Path.Combine("AngerFootVR", "launch.txt")))) return false;
        var token = Environment.GetEnvironmentVariable("AFVR_DEVTOKEN");
        var file = Path.Combine(Paths.BepInExRootPath, Path.Combine("AngerFootVR", "dev-run.token"));
        try
        {
            return !string.IsNullOrEmpty(token) && File.Exists(file) && File.ReadAllText(file).Trim() == token;
        }
        catch
        {
            return false;
        }
    }

    private static System.Collections.IEnumerator SavePage(Camera mc, string tag)
    {
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
        var rt = mc.targetTexture;
        if (rt == null) yield break;
        var copy = RenderTexture.GetTemporary(rt.width, rt.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(rt, copy);
        var was = RenderTexture.active;
        RenderTexture.active = copy;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        RenderTexture.active = was;
        RenderTexture.ReleaseTemporary(copy);
        var dir = Path.Combine(Paths.BepInExRootPath, Path.Combine("AngerFootVR", "shots"));
        var file = Path.Combine(dir, $"mappage_{(tag.Length > 0 ? tag : "as-is")}_{DateTime.Now:HHmmss}.png");
        File.WriteAllBytes(file, tex.EncodeToPNG());
        UnityEngine.Object.Destroy(tex);
        L.LogInfo("MAPDIAG page -> " + file);
    }

    private static System.Collections.IEnumerator AirKick(int frames)
    {
        Core.VrControls.DevPress(XRNode.RightHand, Core.VrButton.Primary, 6);
        for (var i = 0; i < frames; i++) yield return null;
        Dev.LegRecorder.Start(60);
        Core.VrControls.DevPress(XRNode.RightHand, Core.VrButton.Secondary, 3);
    }

    internal static ManualLogSource L;
    internal static Plugin Instance;
    private float _startXrAt = -1f, _nextReport, _nextCmd, _nextWatch;
    private int _frames;
    private float _frameTime, _worst;
    private float _nextHitchLog;
    private string _cmdFile;

    /// <summary>The game quits itself past this much private memory (MB): a dev-run safety net, after a test loop of
    /// another mod once exhausted the laptop's memory.</summary>
    private static readonly long MaxMemoryMb = long.TryParse(Environment.GetEnvironmentVariable("AFVR_MAX_MB"), out var mb) ? mb : 9000;

    private void Awake()
    {
        L = Logger;
        Instance = this;
        Application.onBeforeRender += () =>
        {
            CinemaRig.BeforeRender();
            VrRig.BeforeRender();
            MapRig.BeforeRender();
            Dev.LegRecorder.Tick();
        };
        VrSettings.Init(Config);
        Rendering.BloodDecals.Init();
        // (the mod's shaders, gore textures and models read now: never mid-level, on the first shot)
        try
        {
            Rendering.Shaders.Get("AngerFootVR/WoundCarve");
            UI.Props.Load();
        }
        catch (Exception e)
        {
            L.LogWarning("Preload: " + e.Message);
        }
        new HarmonyLib.Harmony("com.strikezonevr.angerfootvr").PatchAll(typeof(Plugin).Assembly);
        Body.HeroModel.Init();
        L.LogInfo($"ANGER FOOT VR 1.2.0 loading. Unity {Application.unityVersion}, game {Application.version}, graphics {SystemInfo.graphicsDeviceType} ({SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsMemorySize} MB)");
        var displays = new System.Collections.Generic.List<XRDisplaySubsystemDescriptor>();
        SubsystemManager.GetSubsystemDescriptors(displays);
        L.LogInfo($"VRCHECK XR display descriptors: {displays.Count} [{string.Join(", ", displays.Select(d => d.id).ToArray())}]");
        _cmdFile = Path.Combine(Paths.BepInExRootPath, Path.Combine("AngerFootVR", "cmd.txt"));
        Directory.CreateDirectory(Path.GetDirectoryName(_cmdFile));
        File.WriteAllText(_cmdFile, "");
        // VR starts a few seconds in, once the game's first scene is up (AFVR_NOXR=1 leaves it off)
        // the launcher's choices (PLAY FLAT: the mod stays out of the way; the VR runtime chosen in OPTIONS)
        var launch = ReadLaunchFile();
        var flat = Environment.GetEnvironmentVariable("AFVR_MODE") == "flat" || launch.mode == "flat";
        if (!string.IsNullOrEmpty(launch.runtime) && File.Exists(launch.runtime))
        {
            Environment.SetEnvironmentVariable("XR_RUNTIME_JSON", launch.runtime); // (read by the OpenXR loader when VR starts)
            L.LogInfo("VR runtime chosen in the launcher: " + launch.runtime);
        }

        if (Environment.GetEnvironmentVariable("AFVR_NOXR") != "1" && !flat) _startXrAt = 4f;
        else L.LogInfo("Started flat from the launcher: VR stays off");
    }

    /// <summary>BepInEx/AngerFootVR/launch.txt, written by the launcher just before it starts the game (read once, then
    /// deleted; a stale one, from a launch that never came up, is ignored).</summary>
    private static (string mode, string runtime) ReadLaunchFile()
    {
        var file = Path.Combine(Paths.BepInExRootPath, Path.Combine("AngerFootVR", "launch.txt"));
        try
        {
            if (!File.Exists(file)) return (null, null);
            string mode = null, runtime = null;
            long at = 0;
            foreach (var line in File.ReadAllLines(file))
            {
                var i = line.IndexOf('=');
                if (i < 0) continue;
                var k = line.Substring(0, i);
                var v = line.Substring(i + 1).Trim();
                if (k == "mode") mode = v;
                else if (k == "runtime") runtime = v;
                else if (k == "at") long.TryParse(v, out at);
            }

            File.Delete(file);
            if (at > 0 && (DateTime.UtcNow - new DateTime(at, DateTimeKind.Utc)).TotalMinutes > 3) return (null, null);
            L.LogInfo($"Started from the launcher: {mode}");
            return (mode, runtime);
        }
        catch (Exception e)
        {
            L.LogWarning("launch.txt: " + e.Message);
            return (null, null);
        }
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
        XrActions.Update();
        VrControls.Update();
        Input.GripAdjust.Update();
        MapRig.Update();
        CinemaRig.Update();
        UI.Opening.Update();
        UI.VrUi.Update();
        Body.VrBody.Update();
        Body.ViewModels.Update();
        UI.LauncherArt.Update();
        Rendering.SpectatorView.Update();
        Rendering.BehindView.Update();
        Recentering();
        Input.GunSpin.Update();
        Input.MotionThrow.Update();
        Input.MotionDrink.Update();
        UI.Surprises.Update();
        UI.Surprises.HallOfFame();
        VrSettings.Poll();
        Rendering.VrQuality.Update();
        Rendering.Gore.Update();
        Input.GunDetails.Update();
        var dt = Time.unscaledDeltaTime;
        _frames++;
        _frameTime += dt;
        _worst = Mathf.Max(_worst, dt);
        if (dt > 0.1f && now >= _nextHitchLog && XrManager.IsRunning)
        {
            // (a stall long enough to feel in the headset: what scene, so a player's log says where)
            _nextHitchLog = now + 2f;
            L.LogInfo($"HITCH {dt * 1000f:F0} ms (scene {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, frame {Time.frameCount})");
        }
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

    private float _recenterHeld;

    /// <summary>Holding the left stick in recentres you (in play: on where you stand, at your height; in the movie
    /// house: back to your spot facing the screen). (The right stick's click is the middle finger.)</summary>
    private void Recentering()
    {
        if (!XrManager.IsRunning) return;
        if (!VrControls.Held(XRNode.LeftHand, VrButton.StickClick))
        {
            _recenterHeld = 0f;
            return;
        }

        if (_recenterHeld < 0f) return;
        _recenterHeld += Time.unscaledDeltaTime;
        if (_recenterHeld < 0.8f) return;
        _recenterHeld = -1f; // once a hold
        if (CinemaRig.Active) CinemaRig.Recenter();
        else if (MapRig.Active) MapRig.Recenter();
        else VrRig.Recenter();
        VrControls.Rumble(XRNode.LeftHand, 0.5f, 0.08f);
        VrControls.Rumble(XRNode.RightHand, 0.5f, 0.08f);
        L.LogInfo("Recentred");
    }

    private void LateUpdate()
    {
        CinemaRig.LateUpdate();
        MapRig.LateUpdate();
        UI.Laser.Update();
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
        var (waitMs, waits) = OpenXRHook.TakeWaitStats();
        var period = OpenXRHook.PredictedDisplayPeriod / 1e6;
        var headOk = VrRig.TryGetTrackedHead(out var headNow, out var headRotNow);
        L.LogInfo($"VRHEAD tracked={headOk} at {headNow:F2} facing {headRotNow.eulerAngles.y:F0} | {HeadTiming.Take()} | {OpenXRHook.LastViews}");
        L.LogInfo($"VRPERF controllers={VrControls.Present} ({(XrActions.Ready ? XrActions.Profile : "no actions")}) headset {(period > 0 ? 1000.0 / period : 0):F0} Hz, xrWaitFrame {waitMs:F1} ms x{waits} | frame {avg:F2} ms avg ({(avg > 0 ? 1000f / avg : 0):F1} fps), worst {_worst * 1000f:F1} ms, memory {mb} MB | xr {XrManager.Current} running={XrManager.IsRunning} XRSettings.enabled={XRSettings.enabled} eye {XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight} | camera {(cam != null ? $"'{cam.name}' stereo={cam.stereoEnabled} target={cam.stereoTargetEye} fov={cam.fieldOfView:F0}" : "none")}");
        _frames = 0;
        _frameTime = 0;
        _worst = 0;
    }

    private System.Collections.IEnumerator Later(float seconds, string line)
    {
        yield return new WaitForSecondsRealtime(seconds);
        try
        {
            File.AppendAllText(_cmdFile, line + Environment.NewLine);
            _nextCmd = 0f; // (read the next frame, not at the next quarter second)
        }
        catch (Exception e)
        {
            L.LogWarning("after: " + e.Message);
        }
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
                    case "head":
                    {
                        // head <pitch> <yaw> [roll] | head off: fake the head's rotation (the test runtime's head is still)
                        var a = arg.Split(' ');
                        Core.VrRig.DevHeadEuler = a[0] == "off" ? (Vector3?)null : new Vector3(float.Parse(a[0]), float.Parse(a[1]), a.Length > 2 ? float.Parse(a[2]) : 0f);
                        L.LogInfo("dev head " + (Core.VrRig.DevHeadEuler?.ToString() ?? "off"));
                        break;
                    }
                    case "lookenemy":
                    {
                        // turn the (test runtime's) head to the nearest living enemy in sight
                        var head = Core.VrRig.HeadPosition;
                        Enemy best = null;
                        var bestD = float.MaxValue;
                        foreach (var e in UnityEngine.Object.FindObjectsOfType<Enemy>())
                        {
                            if (e == null || e.Health == null || !e.Health.IsAlive) continue;
                            var d = (e.transform.position - head).magnitude;
                            if (d < bestD)
                            {
                                bestD = d;
                                best = e;
                            }
                        }

                        if (best == null)
                        {
                            L.LogInfo("no enemies");
                            break;
                        }

                        var to = best.transform.position + Vector3.up * 1.3f - head;
                        var world = Quaternion.LookRotation(to).eulerAngles;
                        world.y -= Core.VrRig.PlayYaw; // (the head's turn is in the play space)
                        Core.VrRig.DevHeadEuler = new Vector3(world.x, world.y, 0f);
                        L.LogInfo($"looking at {best.name} {bestD:F1} m away");
                        break;
                    }
                    case "press":
                    {
                        // press <left|right> <trigger|grip|primary|secondary|stickclick|menu> [frames]: hold a button
                        var a = arg.Split(' ');
                        var hand = a[0] == "left" ? XRNode.LeftHand : XRNode.RightHand;
                        var b = (Core.VrButton)Enum.Parse(typeof(Core.VrButton), a[1], true);
                        Core.VrControls.DevPress(hand, b, a.Length > 2 ? int.Parse(a[2]) : 6);
                        break;
                    }
                    case "move":
                    {
                        var a = arg.Split(' ');
                        Core.VrControls.DevMove = a[0] == "off" ? Vector2.zero : new Vector2(float.Parse(a[0]), float.Parse(a[1]));
                        break;
                    }
                    case "rstick":
                    {
                        // rstick x y | off: the right stick, for the test runtime
                        var a = arg.Split(' ');
                        Core.VrControls.DevStickR = a[0] == "off" ? (Vector2?)null : new Vector2(Dev.MockControllers.F(a[0]), Dev.MockControllers.F(a[1]));
                        break;
                    }
                    case "turn":
                        Core.VrControls.DevTurn = arg == "off" ? (float?)null : float.Parse(arg);
                        break;
                    case "recenter":
                        Core.VrRig.Recenter();
                        break;
                    case "rig":
                        L.LogInfo($"RIG playYaw={Core.VrRig.PlayYaw:F1} head={Core.VrRig.HeadPosition} rot={Core.VrRig.HeadRotation.eulerAngles} camFwd={Player.CameraForward} tracked={(Core.VrRig.TryGetTrackedHead(out var hp, out var hr) ? hp + " " + hr.eulerAngles : "none")}");
                        break;
                    case "canvases":
                    {
                        foreach (var c in Resources.FindObjectsOfTypeAll<Canvas>())
                        {
                            if (c == null || !c.isRootCanvas || !c.gameObject.scene.IsValid()) continue;
                            L.LogInfo($"CANVAS '{c.name}' active={c.isActiveAndEnabled} mode={c.renderMode} order={c.sortingOrder} layer={c.gameObject.layer} cam={(c.worldCamera != null ? c.worldCamera.name : "-")} scaler={(c.GetComponent<UnityEngine.UI.CanvasScaler>()?.uiScaleMode.ToString() ?? "-")} scene={c.gameObject.scene.name}");
                        }

                        var used = 0;
                        foreach (var cam in Camera.allCameras) used |= cam.cullingMask;
                        var names = new System.Text.StringBuilder();
                        for (var i = 0; i < 32; i++) names.Append($"{i}:{LayerMask.LayerToName(i)}{((used & (1 << i)) != 0 ? "*" : "")} ");
                        L.LogInfo("LAYERS (* = some camera draws it) " + names);
                        break;
                    }
                    case "panel":
                    {
                        var a = arg.Split(' ');
                        UI.VrUi.DevWidth = a[0] == "off" ? (float?)null : float.Parse(a[0]);
                        UI.VrUi.DevDistance = a.Length > 1 ? float.Parse(a[1]) : (float?)null;
                        break;
                    }
                    case "panelfill":
                        UI.VrUi.DevFill(arg != "off");
                        break;
                    case "hands":
                        Dev.DevPlay.Hands(arg);
                        break;
                    case "handpose":
                    {
                        // handpose l|r x y z pitch yaw roll: a dev hand (head-yaw metres, degrees); handpose l|r off
                        var a2 = arg.Split(' ');
                        (Vector3, Vector3)? devHp = a2.Length >= 7 ? (new Vector3(float.Parse(a2[1]), float.Parse(a2[2]), float.Parse(a2[3])), new Vector3(float.Parse(a2[4]), float.Parse(a2[5]), float.Parse(a2[6]))) : ((Vector3, Vector3)?)null;
                        if (a2[0] == "l") Core.VrHands.DevLeft = devHp;
                        else Core.VrHands.DevRight = devHp;
                        L.LogInfo($"dev hand {a2[0]} {(devHp.HasValue ? devHp.Value.ToString() : "off")}");
                        break;
                    }
                    case "devbtn":
                    {
                        // devbtn l|r <button> on|off: a dev controller button held (the test runtime's buttons don't come through)
                        var a2 = arg.Split(' ');
                        var node = a2[0] == "l" ? UnityEngine.XR.XRNode.LeftHand : UnityEngine.XR.XRNode.RightHand;
                        var b = (Core.VrButton)Enum.Parse(typeof(Core.VrButton), a2[1], true);
                        Core.VrControls.DevHold(node, b, a2.Length < 3 || a2[2] != "off");
                        L.LogInfo($"dev button {a2[0]} {b} {(a2.Length < 3 || a2[2] != "off" ? "held" : "let go")}");
                        break;
                    }
                    case "gundetails":
                        L.LogInfo(Input.GunDetails.Describe());
                        break;
                    case "gripinfo":
                        L.LogInfo(Body.GripFingers.Describe() + " | " + Body.HeroSkin.GripReport + $" | supported {Input.Arsenal.Supported} two hands {Input.Arsenal.TwoHands} fore {Input.GunHold.ForeInRig} | {Input.Arsenal.DevFore}");
                        break;
                    case "contact":
                        Input.GunHold.DevNoContact = arg == "off";
                        L.LogInfo("grip contact " + (Input.GunHold.DevNoContact ? "off" : "on"));
                        break;
                    case "fingerr":
                    {
                        var a2 = arg.Split(' ');
                        Body.GripFingers.RadiusBase = float.Parse(a2[0]);
                        Body.GripFingers.RadiusMid = float.Parse(a2[1]);
                        Body.GripFingers.RadiusTip = float.Parse(a2[2]);
                        L.LogInfo(Body.GripFingers.Describe());
                        break;
                    }
                    case "support":
                    {
                        // support dx dy dz pitch yaw roll: the support hand against the gun hand (metres, degrees)
                        var a2 = arg.Split(' ');
                        Input.Arsenal.SupportMove = new Vector3(float.Parse(a2[0]), float.Parse(a2[1]), float.Parse(a2[2]));
                        if (a2.Length >= 6) Input.Arsenal.SupportTurn = new Vector3(float.Parse(a2[3]), float.Parse(a2[4]), float.Parse(a2[5]));
                        if (a2.Length >= 9) Input.Arsenal.SupportAlong = new Vector3(float.Parse(a2[6]), float.Parse(a2[7]), float.Parse(a2[8]));
                        L.LogInfo($"support {Input.Arsenal.SupportMove} {Input.Arsenal.SupportTurn}");
                        break;
                    }
                    case "shoe":
                    {
                        // shoe <name part>: wear these shoes (dev runs keep their own save, so the player's is untouched)
                        if (!Dev.DevSaves.Enabled) break;
                        var shoe = System.Linq.Enumerable.FirstOrDefault(ShoeType.AllShoeTypes, x => x != null && x.name.IndexOf(arg, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (shoe != null) GameState.CurrentSave.SetSelectedShoeType(shoe);
                        L.LogInfo("dev shoe " + (shoe != null ? shoe.name : "none matches " + arg));
                        break;
                    }
                    case "assets":
                    {
                        foreach (var n in new[] { "Standard", "Unlit/Color", "Unlit/Texture", "Unlit/Transparent", "Sprites/Default", "UI/Default", "Legacy Shaders/Diffuse", "Mobile/Diffuse", "TextMeshPro/Distance Field", "TextMeshPro/Mobile/Distance Field", "Skybox/Procedural", "Particles/Standard Unlit", "Legacy Shaders/Particles/Additive" })
                            L.LogInfo($"ASSET shader {n}: {(Shader.Find(n) != null ? "yes" : "no")}");
                        var used = new System.Collections.Generic.HashSet<string>();
                        foreach (var m in Resources.FindObjectsOfTypeAll<Material>()) if (m != null && m.shader != null) used.Add(m.shader.name);
                        L.LogInfo("ASSET shaders in use: " + string.Join(", ", System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Take(used, 60))));
                        L.LogInfo("ASSET fonts: " + string.Join(", ", System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>(), f => f.name))));
                        var tex = System.Linq.Enumerable.Where(Resources.FindObjectsOfTypeAll<Texture2D>(), t => t != null && System.Text.RegularExpressions.Regex.IsMatch(t.name, arg.Length > 0 ? arg : "(?i)poster|neon|sign|logo|brick|carpet|curtain|cinema|movie|title|key ?art"));
                        L.LogInfo("ASSET textures: " + string.Join(", ", System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Take(System.Linq.Enumerable.Select(tex, t => $"{t.name}({t.width}x{t.height})"), 80))));
                        break;
                    }
                    case "cinema":
                        CinemaRig.DevForce = arg == "on";
                        break;
                    case "bodyik":
                        L.LogInfo("BODYPOSE " + Body.HeroBody.Describe());
                        break;
                    case "bodyshot":
                        StartCoroutine(Dev.DevPlay.BodyShot(arg));
                        break;
                    case "closeshot":
                        StartCoroutine(Dev.CloseShot.Take(arg));
                        break;
                    case "gun":
                        L.LogInfo(Input.GunHold.Describe());
                        break;
                    case "body":
                        L.LogInfo(Body.VrBody.Describe());
                        break;
                    case "uipanel":
                        L.LogInfo(UI.VrUi.Describe());
                        break;
                    case "testcube":
                    {
                        // a bright cube 2 m along each enabled camera's forward (dev: where does a camera really look?)
                        foreach (var c in Camera.allCameras)
                        {
                            if (c.targetTexture != null) continue;
                            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                            cube.name = "AngerFootVR test cube " + c.name;
                            Destroy(cube.GetComponent<Collider>());
                            cube.layer = UI.VrUi.Layer;
                            cube.transform.position = c.transform.position + c.transform.forward * 2f;
                            cube.transform.localScale = Vector3.one * 0.25f;
                            var m = new Material(Shader.Find("UI/Default")) { color = c.name.Contains("UI") ? Color.cyan : Color.yellow };
                            m.renderQueue = 4000;
                            m.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
                            cube.GetComponent<MeshRenderer>().sharedMaterial = m;
                            L.LogInfo($"test cube for '{c.name}' at {cube.transform.position} (camera {c.transform.position} fwd {c.transform.forward})");
                        }

                        break;
                    }
                    case "gfx":
                    {
                        var b = new System.Text.StringBuilder($"GFX shadows={QualitySettings.shadows} res={QualitySettings.shadowResolution} dist={QualitySettings.shadowDistance} cascades={QualitySettings.shadowCascades} msaa={QualitySettings.antiAliasing} pixelLights={QualitySettings.pixelLightCount} lodBias={QualitySettings.lodBias} aniso={QualitySettings.anisotropicFiltering} texLimit={QualitySettings.masterTextureLimit} softParticles={QualitySettings.softParticles}");
                        foreach (var c in Camera.allCameras)
                        {
                            var ppl = c.GetComponent<UnityEngine.Rendering.PostProcessing.PostProcessLayer>();
                            b.Append($" | cam '{c.name}' hdr={c.allowHDR} msaa={c.allowMSAA} path={c.actualRenderingPath} depth={c.depthTextureMode} far={c.farClipPlane}{(ppl != null ? $" ppl aa={ppl.antialiasingMode} enabled={ppl.enabled}" : "")}");
                        }

                        foreach (var v in UnityEngine.Object.FindObjectsOfType<UnityEngine.Rendering.PostProcessing.PostProcessVolume>())
                            if (v.sharedProfile != null)
                                b.Append($" | volume '{v.name}' w={v.weight} global={v.isGlobal}: " + string.Join(",", v.sharedProfile.settings.Where(x => x.active && x.enabled.value).Select(x => x.GetType().Name).ToArray()));
                        foreach (var mb in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                            if (mb.enabled && mb.GetType().Name.IndexOf("Occlusion", StringComparison.Ordinal) >= 0 || mb.GetType().Name.IndexOf("Fog", StringComparison.Ordinal) >= 0 || mb.GetType().Name.IndexOf("Volumetric", StringComparison.Ordinal) >= 0)
                                b.Append($" | {mb.GetType().Name} on '{mb.name}' enabled={mb.enabled}");
                        L.LogInfo(b.ToString());
                        break;
                    }
                    case "perf":
                    {
                        // perf <ppl on|off> | <aa none|fxaa|smaa|taa> | <shadowdist m> | <shadows on|off> | <effect Name on|off>: dev experiments for the cost of each
                        var a = arg.Split(' ');
                        var layers = UnityEngine.Object.FindObjectsOfType<UnityEngine.Rendering.PostProcessing.PostProcessLayer>();
                        switch (a[0])
                        {
                            case "ppl":
                                foreach (var l in layers) l.enabled = a[1] == "on";
                                break;
                            case "aa":
                                foreach (var l in layers)
                                    l.antialiasingMode = a[1] == "fxaa" ? UnityEngine.Rendering.PostProcessing.PostProcessLayer.Antialiasing.FastApproximateAntialiasing
                                        : a[1] == "smaa" ? UnityEngine.Rendering.PostProcessing.PostProcessLayer.Antialiasing.SubpixelMorphologicalAntialiasing
                                        : a[1] == "taa" ? UnityEngine.Rendering.PostProcessing.PostProcessLayer.Antialiasing.TemporalAntialiasing
                                        : UnityEngine.Rendering.PostProcessing.PostProcessLayer.Antialiasing.None;
                                break;
                            case "tex":
                                QualitySettings.masterTextureLimit = int.Parse(a[1]);
                                break;
                            case "shadowdist":
                                QualitySettings.shadowDistance = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
                                break;
                            case "shadows":
                                QualitySettings.shadows = a[1] == "on" ? ShadowQuality.All : ShadowQuality.Disable;
                                break;
                            case "effect":
                                foreach (var v in UnityEngine.Object.FindObjectsOfType<UnityEngine.Rendering.PostProcessing.PostProcessVolume>())
                                    if (v.sharedProfile != null)
                                        foreach (var x in v.sharedProfile.settings)
                                            if (x.GetType().Name == a[1])
                                                x.active = a[2] == "on";
                                break;
                        }

                        L.LogInfo("perf " + arg);
                        break;
                    }
                    case "cap":
                        // cap <fps|-1>: dev frame cap (the test runtime's 90 by default), to measure headroom
                        Rendering.VrQuality.DevCap = int.Parse(arg);
                        Application.targetFrameRate = int.Parse(arg);
                        L.LogInfo("frame cap " + Application.targetFrameRate);
                        break;
                    case "eyescale":
                        // eyescale <scale>: render the eyes bigger (a real headset's resolution) to measure the cost
                        XRSettings.eyeTextureResolutionScale = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
                        L.LogInfo($"eye scale {XRSettings.eyeTextureResolutionScale} -> {XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight}");
                        break;
                    case "eyes":
                    {
                        // where each stereo camera is, and where its eyes are drawn from (the view matrices)
                        var b = new System.Text.StringBuilder("EYES");
                        foreach (var c in Camera.allCameras)
                        {
                            if (!c.stereoEnabled) continue;
                            var l = c.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse.GetColumn(3);
                            var r = c.GetStereoViewMatrix(Camera.StereoscopicEye.Right).inverse.GetColumn(3);
                            b.Append($" | '{c.name}' cam {c.transform.position:F3} eyes L {(Vector3)l:F3} R {(Vector3)r:F3} fwd {c.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse.MultiplyVector(Vector3.back):F2} camfwd {c.transform.forward:F2}");
                        }

                        b.Append(" | " + OpenXRHook.LastViews + " | spaces " + string.Join(",", OpenXRHook.SpaceTypes.Select(kv => kv.Key + ":" + kv.Value).ToArray()) + " app " + XrManager.AppSpace);
                        var hd = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
                        if (hd.TryGetFeatureValue(CommonUsages.devicePosition, out var headTracked)) b.Append($" | tracked head {headTracked:F3}");
                        L.LogInfo(b.ToString());
                        break;
                    }
                    case "shaders":
                    {
                        var names = new[] { "Standard", "Standard (Specular setup)", "Particles/Standard Surface", "Particles/Standard Unlit", "Legacy Shaders/Diffuse", "Legacy Shaders/VertexLit", "Legacy Shaders/Bumped Diffuse", "Nature/Tree Soft Occlusion Bark", "Sprites/Default", "Unlit/Texture", "Unlit/Color", "UI/Default", "Mobile/Diffuse", "Legacy Shaders/Particles/Additive", "Legacy Shaders/Particles/Alpha Blended", "Particles/Additive", "Legacy Shaders/Self-Illumin/Diffuse" };
                        L.LogInfo("SHADERS " + string.Join(", ", System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(names, n => n + "=" + (Shader.Find(n) != null ? "yes" : "no")))));
                        var all = Resources.FindObjectsOfTypeAll<Shader>();
                        L.LogInfo("SHADERS loaded: " + string.Join(", ", System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(all, x => x.name))));
                        break;
                    }
                    case "props":
                    {
                        // the game's prefabs that are always loaded (GlobalAssets), with what each is
                        var b = new System.Text.StringBuilder("PROPS");
                        var pairs = Framework.ScriptableObjectSingleton<GlobalAssets>.Instance.PrefabGUIDPairs;
                        foreach (var pair in pairs)
                        {
                            var go = pair.Prefab;
                            if (go == null) continue;
                            var brk = go.GetComponentInChildren<BreakableProp>(true);
                            var rb = go.GetComponent<Rigidbody>();
                            var r = go.GetComponentInChildren<Renderer>(true);
                            b.Append($" | {go.name} L{go.layer}{(brk != null ? $" BREAK({brk.Sturdiness},{brk.BrokenObjects.Length})" : "")}{(rb != null ? $" rb{rb.mass:F1}" : "")}{(r != null ? $" {r.bounds.size:F1}" : "")}");
                        }

                        L.LogInfo(b.ToString());
                        var layers = new System.Text.StringBuilder("LAYERS");
                        for (var i = 0; i < 32; i++)
                            if (!string.IsNullOrEmpty(LayerMask.LayerToName(i)))
                                layers.Append($" {i}={LayerMask.LayerToName(i)}");
                        L.LogInfo(layers.ToString());
                        break;
                    }
                    case "fingers":
                    {
                        // fingers <index> <fist> <thumb> <middle finger> | off: the hands' curls held (the test
                        // runtime's controllers send no trigger or grip)
                        var a = arg.Split(' ');
                        Body.HeroBody.DevFingers = a[0] == "off" ? (Vector4?)null
                            : new Vector4(Dev.MockControllers.F(a[0]), Dev.MockControllers.F(a[1]), Dev.MockControllers.F(a[2]), a.Length > 3 ? Dev.MockControllers.F(a[3]) : 0f);
                        L.LogInfo("fingers " + (Body.HeroBody.DevFingers?.ToString() ?? "from the controllers"));
                        break;
                    }
                    case "thumb":
                    {
                        // thumb <across> <base> <mid> <tip> [sign]: the thumb's angles at a full curl
                        var a = arg.Split(' ');
                        Body.HeroSkin.ThumbAcross = Dev.MockControllers.F(a[0]);
                        Body.HeroSkin.ThumbBase = Dev.MockControllers.F(a[1]);
                        Body.HeroSkin.ThumbMid = Dev.MockControllers.F(a[2]);
                        Body.HeroSkin.ThumbTip = Dev.MockControllers.F(a[3]);
                        if (a.Length > 4) Body.HeroSkin.ThumbSign = Dev.MockControllers.F(a[4]);
                        L.LogInfo($"thumb across {Body.HeroSkin.ThumbAcross} base {Body.HeroSkin.ThumbBase} mid {Body.HeroSkin.ThumbMid} tip {Body.HeroSkin.ThumbTip} sign {Body.HeroSkin.ThumbSign}");
                        break;
                    }
                    case "kick":
                    {
                        // kick [lift reach]: the body kicks (with its height above the hip and its reach)
                        var a = arg.Split(' ');
                        if (a.Length >= 2 && a[0].Length > 0)
                        {
                            Body.Skeleton.KickLift = Dev.MockControllers.F(a[0]);
                            Body.Skeleton.KickReach = Dev.MockControllers.F(a[1]);
                        }

                        Body.Skeleton.DevKickFreeze = a.Length > 2 && a[2] == "hold";

                        Body.HeroBody.Kick();
                        L.LogInfo($"kick lift {Body.Skeleton.KickLift} reach {Body.Skeleton.KickReach}");
                        break;
                    }
                    case "drink":
                    {
                        // drink [beer|energy|lizard]: the first one in the level comes into your hand (as if grabbed)
                        MonoBehaviour thing = arg == "energy" ? UnityEngine.Object.FindObjectOfType<EnergyDrink>() : arg == "lizard" ? UnityEngine.Object.FindObjectOfType<Lizard>() : (MonoBehaviour)UnityEngine.Object.FindObjectOfType<BeerBottle>();
                        if (thing is BeerBottle b) b.OnInteraction();
                        else if (thing is EnergyDrink e) e.OnInteraction();
                        else if (thing is Lizard z) z.OnInteraction();
                        L.LogInfo(thing == null ? "no " + arg + " here" : Input.MotionDrink.Describe());
                        break;
                    }
                    case "scopeeye":
                        L.LogInfo(Input.Arsenal.ScopeEye());
                        break;
                    case "give":
                        L.LogInfo(Input.Arsenal.Give(arg));
                        break;
                    case "gripmode":
                        Input.GunHold.DevMode = int.Parse(arg);
                        L.LogInfo(Input.GunHold.Describe());
                        break;
                    case "go":
                    {
                        // go x z [yaw]: stand at this spot of the movie house (local metres)
                        var a = arg.Split(' ');
                        CinemaRig.DevGo(Dev.MockControllers.F(a[0]), Dev.MockControllers.F(a[1]), a.Length > 2 ? Dev.MockControllers.F(a[2]) : 0f);
                        break;
                    }
                    case "rage":
                        if (arg == "open") UI.MovieHouse.TryKickDoor(UI.MovieHouse.Root.TransformPoint(new Vector3(UI.MovieHouse.DoorX, 1.6f, -5.0f)), Vector3.back);
                        else if (arg == "kick") UI.RageRoom.Kick(CinemaRig.HeadPosition, CinemaRig.HeadRotation * Vector3.forward);
                        L.LogInfo(UI.RageRoom.Describe());
                        break;
                    case "ctl":
                    {
                        var a = arg.Split(' ');
                        Dev.MockControllers.Place(a[0], new Vector3(Dev.MockControllers.F(a[1]), Dev.MockControllers.F(a[2]), Dev.MockControllers.F(a[3])),
                            a.Length >= 7 ? new Vector3(Dev.MockControllers.F(a[4]), Dev.MockControllers.F(a[5]), Dev.MockControllers.F(a[6])) : Vector3.zero);
                        break;
                    }
                    case "ctlbtn":
                    {
                        var a = arg.Split(' ');
                        Dev.MockControllers.Button(a[0], a[1], Dev.MockControllers.F(a[2]));
                        break;
                    }
                    case "ctlstick":
                    {
                        var a = arg.Split(' ');
                        Dev.MockControllers.Stick(a[0], Dev.MockControllers.F(a[1]), Dev.MockControllers.F(a[2]));
                        break;
                    }
                    case "mockhead":
                    {
                        // mockhead <height m> [pitch down] [yaw]: the test runtime's head where a real one is
                        var a = arg.Split(' ');
                        var inv = System.Globalization.CultureInfo.InvariantCulture;
                        Dev.MockHead.Place(float.Parse(a[0], inv), a.Length > 1 ? float.Parse(a[1], inv) : 0f, a.Length > 2 ? float.Parse(a[2], inv) : 0f);
                        break;
                    }
                    case "headspin":
                    {
                        var a = arg.Split(' ');
                        var inv = System.Globalization.CultureInfo.InvariantCulture;
                        Dev.MockHead.Spin(float.Parse(a[0], inv), a.Length > 1 ? float.Parse(a[1], inv) : 3f);
                        break;
                    }
                    case "controllers":
                        L.LogInfo(XrActions.Describe());
                        break;
                    case "opening":
                        UI.Opening.DevReplay();
                        break;
                    case "monitor":
                        if (arg == "dump") L.LogInfo("monitor eye picture: " + Rendering.SpectatorView.Dump(Path.Combine(Paths.BepInExRootPath, Path.Combine("AngerFootVR", "shots/monitor_eye.png"))));
                        if (arg == "eye" || arg == "steady" || arg == "behind") VrSettings.Monitor.Value = arg == "eye" ? VrSettings.MonitorView.Eye : arg == "behind" ? VrSettings.MonitorView.Behind : VrSettings.MonitorView.Steady;
                        L.LogInfo($"{Rendering.SpectatorView.State} | {Rendering.BehindView.State} | running={XrManager.IsRunning} setting={VrSettings.Monitor.Value} mode={XRSettings.gameViewRenderMode}");
                        break;
                    case "mirror":
                        // the desktop window shows: "both" (both eyes whole), "left" (cropped), "none"
                        XRSettings.gameViewRenderMode = arg == "both" ? GameViewRenderMode.BothEyes : arg == "none" ? GameViewRenderMode.None : GameViewRenderMode.LeftEye;
                        L.LogInfo("mirror " + XRSettings.gameViewRenderMode);
                        break;
                    case "safe":
                        Dev.DevPlay.Safe(arg != "off");
                        break;
                    case "worldmap":
                        GameState.LoadWorldMap(LoadingTransition.FadeToAndFromBlack);
                        break;
                    case "map":
                        L.LogInfo(MapRig.Describe());
                        break;
                    case "mapbuttons":
                        // mapbuttons [n]: the levels on the map and where they are on the book; n aims the laser at one (-1: off)
                        L.LogInfo(MapRig.Buttons(arg.Length > 0 ? int.Parse(arg) : -2));
                        break;
                    case "levels":
                        Dev.DevPlay.Levels("");
                        break;
                    case "load":
                        Dev.DevPlay.Levels(arg);
                        break;
                    case "meshprobe":
                        L.LogInfo(Dev.MeshProbe.Probe());
                        break;
                    case "modshaders":
                        L.LogInfo(Rendering.Shaders.Describe());
                        break;
                    case "heroexport":
                        L.LogInfo(Dev.MeshProbe.ExportHero());
                        break;
                    case "baketest":
                    {
                        // can a locked goon mesh be baked into a readable snapshot? (the true surface, for the holes)
                        var sb = new System.Text.StringBuilder("BAKETEST");
                        foreach (var e in UnityEngine.Object.FindObjectsOfType<Enemy>())
                        {
                            foreach (var smr in e.GetComponentsInChildren<SkinnedMeshRenderer>())
                            {
                                var baked = new Mesh();
                                var t0 = System.Diagnostics.Stopwatch.StartNew();
                                try
                                {
                                    smr.BakeMesh(baked);
                                    sb.Append($" | '{smr.name}' baked {baked.vertexCount} verts {baked.triangles.Length / 3} tris readable={baked.isReadable} in {t0.Elapsed.TotalMilliseconds:F2} ms");
                                }
                                catch (Exception ex)
                                {
                                    sb.Append($" | '{smr.name}' FAILED {ex.Message}");
                                }

                                UnityEngine.Object.Destroy(baked);
                            }

                            break;
                        }

                        L.LogInfo(sb.ToString());
                        break;
                    }
                    case "holeinfo":
                        foreach (var mr in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
                            if (mr.name.StartsWith("AngerFootVR hole"))
                                L.LogInfo($"HOLEINFO '{mr.name}' active={mr.gameObject.activeInHierarchy} enabled={mr.enabled} visible={mr.isVisible} layer={mr.gameObject.layer} at {mr.transform.position:F2} scale {mr.transform.lossyScale:F3} bounds {mr.bounds.size:F2} shader '{mr.sharedMaterial?.shader.name}' queue {mr.sharedMaterial?.renderQueue} parent '{mr.transform.parent?.name}' camMask {(Camera.main != null ? Camera.main.cullingMask.ToString("X") : "-")}");
                        break;
                    case "holetest":
                    {
                        // hole [kind] [up] [side]: a wound (glock, uzzi, rifle, sniper, shotgun, blast, bite, bruise) on the goon in view
                        var a2 = arg.Split(' ');
                        L.LogInfo(Dev.MeshProbe.HoleTest(a2.Length > 0 && a2[0].Length > 0 ? a2[0] : "glock", a2.Length > 1 ? Dev.MockControllers.F(a2[1]) : 0.15f, a2.Length > 2 ? Dev.MockControllers.F(a2[2]) : 0f));
                        break;
                    }
                    case "wounds":
                        if (arg == "hits") Rendering.Wounds.DevHits = !Rendering.Wounds.DevHits;
                        L.LogInfo(Rendering.Wounds.Describe());
                        break;
                    case "woundme":
                    {
                        // woundme [bullet|explosion|melee] [x y z]: you're hit from in front (head-yaw space aim point), as the game hits you
                        var a2 = arg.Split(' ');
                        var type = a2.Length > 0 && a2[0] == "explosion" ? DamageType.Explosion : a2.Length > 0 && a2[0] == "melee" ? DamageType.MeleeWeapon : DamageType.Bullet;
                        var yawQ = Quaternion.Euler(0f, Core.VrRig.HeadRotation.eulerAngles.y, 0f);
                        var aim = a2.Length >= 4 ? new Vector3(Dev.MockControllers.F(a2[1]), Dev.MockControllers.F(a2[2]), Dev.MockControllers.F(a2[3])) : new Vector3(0f, -0.45f, 0.1f);
                        var at = Core.VrRig.HeadPosition + yawQ * aim;
                        var dir = yawQ * Vector3.back;
                        Rendering.Wounds.OnPlayer(new Damage(1f, type), at, dir);
                        L.LogInfo(Rendering.Wounds.Describe());
                        break;
                    }
                        break;
                    case "grip":
                        // grip on|off: GUN GRIP ADJUST for now (dev: the cfg untouched); grip: how it stands
                        if (arg == "on") Input.GripAdjust.DevOn = true;
                        else if (arg == "off") Input.GripAdjust.DevOn = false;
                        L.LogInfo(Input.GripAdjust.Describe());
                        break;
                    case "textcheck":
                        L.LogInfo(Dev.TextCheck.Run());
                        break;
                    case "givegun":
                        Dev.DevPlay.GiveGun(arg);
                        break;
                    case "bones":
                    {
                        // bones <object name part>|<bone name part>: the matching transforms under it
                        var a = arg.Split('|');
                        var rootT = System.Linq.Enumerable.FirstOrDefault(Resources.FindObjectsOfTypeAll<Transform>(), x => x != null && x.gameObject.scene.IsValid() && x.name.IndexOf(a[0], StringComparison.OrdinalIgnoreCase) >= 0);
                        if (rootT == null) break;
                        foreach (var t in rootT.GetComponentsInChildren<Transform>(true))
                            if (a.Length < 2 || t.name.IndexOf(a[1], StringComparison.OrdinalIgnoreCase) >= 0)
                                L.LogInfo($"BONE {t.name} parent {(t.parent != null ? t.parent.name : "-")} local {t.localPosition} {t.localEulerAngles} rootrel {rootT.InverseTransformPoint(t.position)}");
                        break;
                    }
                    case "vrtab":
                        // vrtab [n]: open SETTINGS and the VR tab (n: the n-th tab)
                        L.LogInfo(UI.VrSettingsTab.Open(arg.Length > 0 ? int.Parse(arg) : -1));
                        break;
                    case "laser":
                        L.LogInfo(UI.Laser.Describe());
                        break;
                    case "focus":
                    {
                        var pl = Framework.SingletonBehaviour<Player>.HasInstance ? Framework.SingletonBehaviour<Player>.Instance : null;
                        L.LogInfo($"FOCUS {(pl != null && pl.FocusedInteractable != null ? pl.FocusedInteractable.name + " at " + pl.FocusedInteractable.transform.position : "nothing")} | left hand {Core.VrHands.GetRaw(XRNode.LeftHand).Position} right hand {Core.VrHands.GetRaw(XRNode.RightHand).Position}");
                        break;
                    }
                    case "mapdiag":
                    {
                        // mapdiag [pp|ao|aa|lights]: the map camera's page saved as a PNG; pp/ao/aa: its post-processing,
                        // its occlusion or its anti-aliasing switched off first
                        if (!Framework.SingletonBehaviour<WorldMapCamera>.HasInstance) break;
                        var wmc = Framework.SingletonBehaviour<WorldMapCamera>.Instance;
                        var mc = wmc.Camera;
                        var ppl = mc.GetComponent<UnityEngine.Rendering.PostProcessing.PostProcessLayer>();
                        if (arg == "pp" && ppl != null) ppl.enabled = false;
                        if (arg == "ao")
                            foreach (var v in UnityEngine.Object.FindObjectsOfType<UnityEngine.Rendering.PostProcessing.PostProcessVolume>())
                                if (v.profile != null && v.profile.TryGetSettings(out UnityEngine.Rendering.PostProcessing.AmbientOcclusion ao)) { ao.enabled.Override(false); ao.active = false; }
                        foreach (var b in mc.GetComponents<Behaviour>()) L.LogInfo($"MAPDIAG map camera component {b.GetType().Name} enabled {b.enabled}");
                        L.LogInfo($"MAPDIAG target {(mc.targetTexture != null ? mc.targetTexture.width + "x" + mc.targetTexture.height + " aa" + mc.targetTexture.antiAliasing + " fmt " + mc.targetTexture.format : "none")} clear {mc.clearFlags} bg {mc.backgroundColor} hdr {mc.allowHDR} msaa {mc.allowMSAA} stereo {mc.stereoEnabled} pages {wmc.MapBookRenderTextures?.Count}");
                        L.LogInfo($"MAPDIAG projection ortho {mc.orthographic} size {mc.orthographicSize} fov {mc.fieldOfView} aspect {mc.aspect} eye {mc.stereoTargetEye} pos {mc.transform.position} rot {mc.transform.eulerAngles} m00 {mc.projectionMatrix.m00:F3} m11 {mc.projectionMatrix.m11:F3} near {mc.nearClipPlane} far {mc.farClipPlane} usePhysical {mc.usePhysicalProperties} focal {mc.focalLength}");
                        if (arg == "reset") { mc.ResetProjectionMatrix(); mc.ResetAspect(); }
                        StartCoroutine(SavePage(mc, arg));
                        break;
                    }
                    case "airkick":
                        // airkick [frames]: jump (A), then kick (B) that many frames later, recording the game's legs
                        StartCoroutine(AirKick(arg.Length > 0 ? int.Parse(arg) : 12));
                        break;
                    case "interactables":
                    case "gonear":
                    {
                        // interactables: what can be used, nearest first; gonear <n>: stand 0.7 m in front of the n-th
                        var pl = Framework.SingletonBehaviour<Player>.HasInstance ? Framework.SingletonBehaviour<Player>.Instance : null;
                        if (pl == null) break;
                        var all = System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderBy(System.Linq.Enumerable.Where(UnityEngine.Object.FindObjectsOfType<Interactable>(), x => x != null && x.enabled), x => (x.transform.position - pl.transform.position).sqrMagnitude));
                        if (cmd == "interactables")
                        {
                            for (var i = 0; i < Math.Min(8, all.Count); i++)
                                L.LogInfo($"INTERACTABLE {i}: {all[i].name} at {all[i].transform.position} ({(all[i].transform.position - pl.transform.position).magnitude:F1} m) range {all[i].InteractionRange}");
                            break;
                        }

                        var n = arg.Length > 0 ? int.Parse(arg) : 0;
                        if (n >= all.Count) break;
                        var target = all[n].transform.position;
                        var flat = target - pl.transform.position;
                        flat.y = 0f;
                        var at = target - flat.normalized * 0.7f;
                        at.y = pl.transform.position.y;
                        pl.Rigidbody.position = at;
                        pl.transform.position = at;
                        L.LogInfo($"GONEAR {all[n].name}: standing at {at}, it is {(target - Core.VrRig.HeadPosition)} from the head");
                        break;
                    }
                    case "complete":
                        // complete: finish the level now (the level complete screen)
                        GameState.CompleteLevel();
                        break;
                    case "shadow":
                        L.LogInfo(Body.VrBody.DevShadow(int.Parse(arg)));
                        break;
                    case "testlight":
                    {
                        // testlight [on|off]: a sun from behind you, hard shadows (to look at your shadow)
                        var old = GameObject.Find("AngerFootVR Test Light");
                        if (old != null) UnityEngine.Object.Destroy(old);
                        if (arg == "off") break;
                        var lg = new GameObject("AngerFootVR Test Light");
                        var light = lg.AddComponent<Light>();
                        light.type = LightType.Directional;
                        light.shadows = LightShadows.Hard;
                        light.intensity = 1.2f;
                        var yawF = Core.VrRig.HeadRotation.eulerAngles.y;
                        lg.transform.rotation = Quaternion.Euler(35f, yawF, 0f);
                        L.LogInfo("test light from behind, yaw " + yawF.ToString("F0"));
                        break;
                    }
                    case "lights":
                        foreach (var lt in UnityEngine.Object.FindObjectsOfType<Light>())
                            if (lt.shadows != LightShadows.None || lt.type == LightType.Directional)
                                L.LogInfo($"LIGHT '{lt.name}' {lt.type} shadows {lt.shadows} intensity {lt.intensity:F2} at {lt.transform.position} rot {lt.transform.eulerAngles} range {lt.range:F1} mask {lt.cullingMask:X} mode {lt.renderMode} dist to head {(lt.transform.position - Core.VrRig.HeadPosition).magnitude:F1}");
                        L.LogInfo($"LIGHTS shadow distance {QualitySettings.shadowDistance} shadows {QualitySettings.shadows} pixel lights {QualitySettings.pixelLightCount}");
                        break;
                    case "tp":
                    {
                        // tp x z: stand there (on the floor found under it)
                        var a = arg.Split(' ');
                        var pl = Framework.SingletonBehaviour<Player>.HasInstance ? Framework.SingletonBehaviour<Player>.Instance : null;
                        if (pl == null) break;
                        var x = Dev.MockControllers.F(a[0]);
                        var z = Dev.MockControllers.F(a[1]);
                        var y = pl.transform.position.y;
                        if (Physics.Raycast(new Vector3(x, a.Length > 2 ? Dev.MockControllers.F(a[2]) : 6f, z), Vector3.down, out var floor, 20f, LayerMasks.ThingsPlayerCanStandOn)) y = floor.point.y;
                        var at = new Vector3(x, y + 0.05f, z);
                        pl.Rigidbody.position = at;
                        pl.transform.position = at;
                        L.LogInfo("tp " + at);
                        break;
                    }
                    case "tpfront":
                    {
                        // tpfront x z [m]: stand that far from the spot, facing it (the way you face now)
                        var a = arg.Split(' ');
                        var pl = Framework.SingletonBehaviour<Player>.HasInstance ? Framework.SingletonBehaviour<Player>.Instance : null;
                        if (pl == null) break;
                        var fwd = Vector3.ProjectOnPlane(VrRig.HeadRotation * Vector3.forward, Vector3.up).normalized;
                        var target = new Vector3(Dev.MockControllers.F(a[0]), pl.transform.position.y, Dev.MockControllers.F(a[1]));
                        var at = target - fwd * (a.Length > 2 ? Dev.MockControllers.F(a[2]) : 1.1f);
                        if (Physics.Raycast(at + Vector3.up * 1.5f, Vector3.down, out var floor, 3f, LayerMasks.ThingsPlayerCanStandOn)) at.y = floor.point.y + 0.05f;
                        pl.Rigidbody.position = at;
                        pl.transform.position = at;
                        L.LogInfo($"tpfront {at:F2} facing {fwd:F2}");
                        break;
                    }
                    case "goenemy":
                    {
                        // stand 1.1 m from the nearest living enemy, the play space facing it
                        var pl = Framework.SingletonBehaviour<Player>.HasInstance ? Framework.SingletonBehaviour<Player>.Instance : null;
                        if (pl == null) break;
                        Enemy near = null;
                        var nd = float.MaxValue;
                        foreach (var e in UnityEngine.Object.FindObjectsOfType<Enemy>())
                        {
                            if (e == null || e.Health == null || !e.Health.IsAlive) continue;
                            var d = (e.transform.position - pl.transform.position).sqrMagnitude;
                            if (d < nd)
                            {
                                nd = d;
                                near = e;
                            }
                        }

                        if (near == null)
                        {
                            L.LogInfo("no enemies");
                            break;
                        }

                        // (the enemy straight ahead the way the play space faces: the test runtime's controllers are placed in it)
                        var away = -(Quaternion.Euler(0f, Core.VrRig.PlayYaw, 0f) * Vector3.forward);
                        var at = near.transform.position + away * (arg.Length > 0 ? Dev.MockControllers.F(arg) : 1.1f) + Vector3.up * 0.05f;
                        pl.Rigidbody.position = at;
                        pl.transform.position = at;
                        L.LogInfo($"goenemy: by {near.name} at {at:F2}");
                        break;
                    }
                    case "handdiag":
                        L.LogInfo(Body.HeroBody.HandDiag());
                        break;
                    case "legrec":
                        Dev.LegRecorder.Start(arg.Length > 0 ? int.Parse(arg) : 60);
                        break;
                    case "throw":
                        // throw [m/s]: a seated throw from the gun hand (where it points); with a speed: a standing throw that fast along your view
                        L.LogInfo("THROW " + (arg.Length > 0 ? Input.MotionThrow.DevToss(new Vector3(0f, 0f, Dev.MockControllers.F(arg)), false) : Input.MotionThrow.DevToss(Vector3.zero, true)));
                        break;
                    case "toss":
                    {
                        // toss x y z: the gun hand lets go of the gun moving this fast (m/s, your view's frame: x right, y up, z on)
                        var a = arg.Split(' ');
                        // toss x y z l|r s: and that hand squeezes its grip s seconds later (held: devbtn <hand> Grip off to let go)
                        Input.MotionThrow.DevTrace = 120;
                        if (a.Length >= 5)
                        {
                            Input.MotionThrow.DevSqueezeHand = a[3] == "l" ? UnityEngine.XR.XRNode.LeftHand : UnityEngine.XR.XRNode.RightHand;
                            Input.MotionThrow.DevSqueezeAt = Time.unscaledTime + Dev.MockControllers.F(a[4]);
                        }
                        L.LogInfo("TOSS " + Input.MotionThrow.DevToss(new Vector3(Dev.MockControllers.F(a[0]), Dev.MockControllers.F(a[1]), Dev.MockControllers.F(a[2])), false));
                        break;
                    }
                    case "seated":
                        // seated on|off|clear: PLAY MODE for now (the settings file untouched)
                        Input.MotionThrow.DevSeated = arg == "on" ? true : arg == "off" ? false : (bool?)null;
                        L.LogInfo(Input.MotionThrow.Describe());
                        break;
                    case "gunhand":
                        // gunhand l|r: the gun to that hand for now (as a hand-over)
                        Input.MotionThrow.SwitchTo(arg == "l" ? UnityEngine.XR.XRNode.LeftHand : UnityEngine.XR.XRNode.RightHand);
                        L.LogInfo(Input.MotionThrow.Describe());
                        break;
                    case "throws":
                        L.LogInfo(Input.MotionThrow.Describe());
                        break;
                    case "after":
                    {
                        // after <s> <command>: the command, that many seconds from now
                        var sp2 = arg.IndexOf(' ');
                        if (sp2 > 0) StartCoroutine(Later(Dev.MockControllers.F(arg.Substring(0, sp2)), arg.Substring(sp2 + 1)));
                        break;
                    }
                    case "flick":
                    {
                        // flick l|r <rad/s>: a wrist flick on that hand (the test runtime's hands don't turn)
                        var a = arg.Split(' ');
                        L.LogInfo("SPIN " + Input.GunSpin.DevFlick(a[0] == "l" ? UnityEngine.XR.XRNode.LeftHand : UnityEngine.XR.XRNode.RightHand, Dev.MockControllers.F(a[1])));
                        break;
                    }
                    case "spins":
                        L.LogInfo(Input.GunSpin.Describe());
                        break;
                    case "homecoming":
                    {
                        // homecoming: walk into the apartment's shoe-room trigger (Homecoming Story: the shoe wall explodes)
                        var hc = UnityEngine.Object.FindObjectOfType<HomecomingStoryController>();
                        var pl = Framework.SingletonBehaviour<Player>.HasInstance ? Framework.SingletonBehaviour<Player>.Instance : null;
                        if (hc == null || pl == null)
                        {
                            L.LogInfo("homecoming: not in the Homecoming Story scene");
                            break;
                        }

                        var b = (Bounds)HarmonyLib.AccessTools.Field(typeof(HomecomingStoryController), "_triggerBounds").GetValue(hc);
                        var at = b.center + Vector3.down * (b.extents.y * 0.5f);
                        pl.Rigidbody.position = at;
                        pl.transform.position = at;
                        L.LogInfo($"homecoming: at the trigger {at:F2}");
                        break;
                    }
                    case "lift":
                        // lift <metres>|off: VIEW HEIGHT for now (the settings file untouched)
                        Core.VrRig.DevLift = arg == "off" ? (float?)null : Dev.MockControllers.F(arg);
                        L.LogInfo($"view lift {Core.VrRig.Lift:F2} m");
                        break;
                    case "viewfx":
                        // viewfx [hit|fade]: the hit's flash or a story level's fade from black, over your view
                        L.LogInfo(arg.Length > 0 ? Rendering.ViewFx.DevFlash(arg) : Rendering.ViewFx.Describe());
                        for (var qi = 0; qi < 4; qi++) L.LogInfo("VIEW FX " + Rendering.ViewFx.Quad(qi));
                        break;
                    case "gripzero":
                        Input.GripAdjust.DevZero = arg != "off";
                        L.LogInfo("grip offsets " + (Input.GripAdjust.DevZero ? "off" : "on"));
                        break;
                    case "spawnblood":
                    {
                        // spawnblood [m]: the game's own blood splash (with its decals) this far in front of you
                        if (!Framework.SingletonBehaviour<BloodCamera>.HasInstance) break;
                        var fwd = Core.VrRig.HeadRotation * Vector3.forward;
                        fwd.y = 0f;
                        fwd.Normalize();
                        var at = Core.VrRig.HeadPosition + fwd * (arg.Length > 0 ? Dev.MockControllers.F(arg) : 1.5f) - Vector3.up * 0.5f;
                        Framework.SingletonBehaviour<BloodCamera>.Instance.SpawnBlood(at, fwd, spawnDecalToo: true);
                        L.LogInfo($"blood spawned at {at} (show blood {GameSettings.ShowBlood})");
                        break;
                    }
                    case "blood":
                        // blood [on|off]: the blood decals' depth per eye (on) or the game's own mono way (off)
                        if (arg == "on" || arg == "off") Rendering.BloodDecals.On = arg == "on";
                        L.LogInfo(Rendering.BloodDecals.Describe());
                        break;
                    case "gunoff":
                    {
                        // gunoff: how far the hand drawn on the gun is from where the gun was placed for your hand
                        var t = typeof(Input.GunHold);
                        var rig = HarmonyLib.AccessTools.Field(t, "_rig").GetValue(null) as HandRig;
                        if (rig == null) break;
                        var now = (Vector3)HarmonyLib.AccessTools.Field(t, "_palmNow").GetValue(null);
                        var rest = (Vector3)HarmonyLib.AccessTools.Field(t, "_palmRest").GetValue(null);
                        var an = rig.Animator;
                        var si = an.GetCurrentAnimatorStateInfo(0);
                        var clips = an.GetCurrentAnimatorClipInfo(0);
                        var idleField = HarmonyLib.AccessTools.Field(typeof(HandRig), "_isInIdleState");
                        L.LogInfo($"GUNOFF hand off its rest {(rig.transform.TransformPoint(now) - rig.transform.TransformPoint(rest)).magnitude:F3} m (now {now:F3} rest {rest:F3}) | state {si.shortNameHash} (Idle {Animator.StringToHash("Idle")}) t {si.normalizedTime:F2} len {si.length:F2} loop {si.loop} transition {an.IsInTransition(0)} rig idle {idleField?.GetValue(rig)} clips {string.Join(", ", System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(clips, c => c.clip.name + " " + c.weight.ToString("F2"))))} speed {an.speed} timescale {Time.timeScale} layers {an.layerCount}");
                        break;
                    }
                    case "weapondump":
                    {
                        // weapondump: the game's weapon prefabs loaded now (not in a scene), and the random weapon shoe's list
                        var sb = new System.Text.StringBuilder("WEAPONS");
                        foreach (var w in Resources.FindObjectsOfTypeAll<Weapon>())
                            if (w != null && !w.gameObject.scene.IsValid())
                                sb.Append($" | {w.name} [{(w.HoldableObjectType != null ? w.HoldableObjectType.name : "?")}] {w.GetType().Name} model {(w.RenderModel != null ? w.RenderModel.name : "-")}");
                        foreach (var a in Resources.FindObjectsOfTypeAll<RandomWeaponDropAbility>())
                            if (HarmonyLib.AccessTools.Field(typeof(RandomWeaponDropAbility), "_weaponPrefabs")?.GetValue(a) is GameObject[] list)
                                sb.Append($" || drop list {list.Length}: {string.Join(", ", System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(list, g => g != null ? g.name : "null")))}");
                        L.LogInfo(sb.ToString());
                        break;
                    }
                    case "sounddump":
                    {
                        // sounddump [part]: the game's sound banks loaded now (name, clips) and its long clips (music)
                        var sb = new System.Text.StringBuilder("SOUNDS");
                        foreach (var b in Resources.FindObjectsOfTypeAll<SoundManager.SingleSoundBank>())
                            if (b != null && (arg.Length == 0 || b.name.IndexOf(arg, StringComparison.OrdinalIgnoreCase) >= 0))
                                sb.Append($" | {b.name} ({(b.Clips != null ? b.Clips.Length : 0)})");
                        L.LogInfo(sb.ToString());
                        var mb = new System.Text.StringBuilder("MUSIC");
                        foreach (var c in Resources.FindObjectsOfTypeAll<AudioClip>())
                            if (c != null && c.length > 40f)
                                mb.Append($" | {c.name} {c.length:F0}s {c.loadType}");
                        L.LogInfo(mb.ToString());
                        break;
                    }
                    case "stall":
                        // stall <ms>: the game stops for that long, as a slow PC's hitch would (tests the timing of things)
                        System.Threading.Thread.Sleep(Mathf.Clamp(arg.Length > 0 ? int.Parse(arg) : 500, 0, 3000));
                        break;
                    case "probe":
                        // probe: the facts the grip, wound and bleeding plans need (dev)
                        L.LogInfo(Dev.ImmersionProbe.Run());
                        break;
                    case "probe2":
                        // probe2 skin|fp: goons' and your skins (sub-meshes, shader, UV overlap); the first-person fingers on the gun
                        L.LogInfo(Dev.ImmersionProbe2.Run(arg));
                        break;
                    case "soundcheck":
                    {
                        // soundcheck [stop]: the level's looping props and Shepard tone doors whose sound has gone back to the
                        // game's pool while they still hold it (what broke kicks); stop: one of them stopped again, as they do
                        var loopF = HarmonyLib.AccessTools.Field(typeof(PropSoundLoop), "_loop");
                        var doorF = HarmonyLib.AccessTools.Field(typeof(ShepardToneDoor), "_sound");
                        int held = 0, stale = 0;
                        SoundManager.SoundInstance one = null;
                        void Look(object o, System.Reflection.FieldInfo f)
                        {
                            if (!(f.GetValue(o) is SoundManager.SoundInstance si) || si == null) return;
                            held++;
                            if (!si.IsPooled) return;
                            stale++;
                            one ??= si;
                        }

                        foreach (var x in UnityEngine.Object.FindObjectsOfType<PropSoundLoop>()) Look(x, loopF);
                        foreach (var x in UnityEngine.Object.FindObjectsOfType<ShepardToneDoor>()) Look(x, doorF);
                        if (arg == "stop" && one != null) one.StopAndDestroy();
                        L.LogInfo($"soundcheck: {held} sounds held by props and doors, {stale} of them already back in the pool{(arg == "stop" && one != null ? "; one stopped again" : "")}");
                        break;
                    }
                    case "scenetest":
                    {
                        // scenetest: an empty scene made and unloaded (the game's sound pool then stops the level's sounds:
                        // what the rage room's own scene did on every unload)
                        var sc = UnityEngine.SceneManagement.SceneManager.CreateScene("AngerFootVR scene test " + Time.frameCount);
                        UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(sc);
                        var doors = new System.Collections.Generic.List<string>();
                        foreach (var d in UnityEngine.Object.FindObjectsOfType<ShepardToneDoor>()) doors.Add(d.name + " " + d.transform.position.ToString("F1"));
                        L.LogInfo("scenetest: made and unloaded " + sc.name + "; scenes now " + UnityEngine.SceneManagement.SceneManager.sceneCount + "; Shepard tone doors: " + string.Join(", ", doors.ToArray()));
                        break;
                    }
                    case "pause":
                        // pause | resume: the game's own pause, as its menu button and CONTINUE do it
                        GameState.PauseGame();
                        break;
                    case "resume":
                        GameState.ResumeGame();
                        break;
                    case "playcard":
                        // playcard <level name part>: the map's play card raised for that level (its star goals)
                        L.LogInfo(UI.MenuFixes.DevPlayCard(arg));
                        break;
                    case "gorehit":
                        // gorehit head|body|kick|explode: the nearest living enemy, as the game does it
                        L.LogInfo(Rendering.Gore.DevHit(arg));
                        break;
                    case "gore":
                        // gore [off|game|extra|pink|red|you]: the VR gore setting (this run only, not saved) and how it stands
                        if (arg == "off" || arg == "game" || arg == "extra") VrSettings.Gore.BoxedValue = (VrSettings.GoreLevel)System.Enum.Parse(typeof(VrSettings.GoreLevel), arg, true);
                        if (arg == "you") Rendering.Gore.DevOnYou();
                        if (arg == "splash")
                        {
                            var f = Core.VrRig.HeadRotation * Vector3.forward;
                            f.y = 0f;
                            Body.BloodOnYou.Splash(Core.VrRig.HeadPosition + f.normalized * 1.0f + Vector3.down * 0.5f, 1.5f);
                        }
                        if (arg == "youworld") Body.BloodOnYou.DevWorld = !Body.BloodOnYou.DevWorld;
                        if (arg == "pink" || arg == "red") VrSettings.BloodColour.BoxedValue = (VrSettings.BloodTint)System.Enum.Parse(typeof(VrSettings.BloodTint), arg, true);
                        L.LogInfo(Rendering.Gore.Describe());
                        break;
                    case "laserscroll":
                        // laserscroll <-1..1|off>: push the right stick for the laser's list scrolling
                        UI.Laser.DevScroll = arg == "off" ? (float?)null : Dev.MockControllers.F(arg);
                        break;
                    case "vrset":
                        // vrset <row label part>: change that VR tab row through its own control (a click on it)
                        L.LogInfo(UI.VrSettingsTab.DevChange(arg));
                        break;
                    case "clicklog":
                        Input.InputPatches.DevClickLog = arg != "off";
                        break;
                    case "vrdefault":
                        L.LogInfo(UI.VrSettingsTab.DevDefault());
                        break;
                    case "settingsdump":
                        UI.VrSettingsTab.Dump();
                        break;
                    case "feedshot":
                    {
                        // the movie screen's picture as a PNG ("feedshot ui <name>": the game's menus)
                        var ui = arg.StartsWith("ui");
                        if (ui) arg = arg.Substring(2).Trim();
                        var feed = ui ? UI.VrUi.Texture : Core.CinemaRig.Feed;
                        if (feed == null) break;
                        var dir = Path.Combine(Paths.BepInExRootPath, Path.Combine("AngerFootVR", "shots"));
                        Directory.CreateDirectory(dir);
                        var was = RenderTexture.active;
                        RenderTexture.active = feed;
                        var tex = new Texture2D(feed.width, feed.height, TextureFormat.RGB24, false);
                        tex.ReadPixels(new Rect(0, 0, feed.width, feed.height), 0, 0);
                        RenderTexture.active = was;
                        var file = Path.Combine(dir, $"{(arg.Length > 0 ? arg : "feed")}_{DateTime.Now:HHmmss}.png");
                        File.WriteAllBytes(file, tex.EncodeToPNG());
                        UnityEngine.Object.Destroy(tex);
                        L.LogInfo("Feed -> " + file);
                        break;
                    }
                    case "hier":
                        Dev.DevPlay.Hierarchy(arg);
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
