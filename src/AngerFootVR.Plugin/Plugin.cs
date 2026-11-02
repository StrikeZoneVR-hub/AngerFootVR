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
[BepInPlugin("com.strikezonevr.angerfootvr", "ANGER FOOT VR", "0.1.1")]
public class Plugin : BaseUnityPlugin
{
    /// <summary>A dev run (tools/game.sh: test saves, action log, no opening). Never when the launcher started the game
    /// (it sets AFVR_MODE): a player's run once came up as a dev run and played on the test saves.</summary>
    internal static readonly bool DevRun = Environment.GetEnvironmentVariable("AFVR_DEVSAVES") == "1" && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AFVR_MODE"));

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
        Application.onBeforeRender += () =>
        {
            CinemaRig.BeforeRender();
            VrRig.BeforeRender();
        };
        VrSettings.Init(Config);
        new HarmonyLib.Harmony("com.strikezonevr.angerfootvr").PatchAll(typeof(Plugin).Assembly);
        Body.HeroModel.Init();
        L.LogInfo($"ANGER FOOT VR 0.1.1 loading. Unity {Application.unityVersion}, game {Application.version}, graphics {SystemInfo.graphicsDeviceType} ({SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsMemorySize} MB)");
        var displays = new System.Collections.Generic.List<XRDisplaySubsystemDescriptor>();
        SubsystemManager.GetSubsystemDescriptors(displays);
        L.LogInfo($"VRCHECK XR display descriptors: {displays.Count} [{string.Join(", ", displays.Select(d => d.id).ToArray())}]");
        _cmdFile = Path.Combine(Paths.BepInExRootPath, Path.Combine("AngerFootVR", "cmd.txt"));
        Directory.CreateDirectory(Path.GetDirectoryName(_cmdFile));
        File.WriteAllText(_cmdFile, "");
        // VR starts a few seconds in, once the game's first scene is up (AFVR_NOXR=1 leaves it off)
        // (the launcher's PLAY FLAT starts the game with AFVR_MODE=flat: the mod stays out of the way)
        if (Environment.GetEnvironmentVariable("AFVR_NOXR") != "1" && Environment.GetEnvironmentVariable("AFVR_MODE") != "flat") _startXrAt = 4f;
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
        CinemaRig.Update();
        UI.Opening.Update();
        UI.VrUi.Update();
        Body.VrBody.Update();
        Body.ViewModels.Update();
        UI.LauncherArt.Update();
        Rendering.SpectatorView.Update();
        Recentering();
        Input.MotionThrow.Update();
        VrSettings.Poll();
        Rendering.VrQuality.Update();
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

    private float _recenterHeld;

    /// <summary>Holding the right stick in recentres you (in play: on where you stand, at your height; in the movie
    /// house: back to your spot facing the screen).</summary>
    private void Recentering()
    {
        if (!XrManager.IsRunning) return;
        if (!VrControls.Held(XRNode.RightHand, VrButton.StickClick))
        {
            _recenterHeld = 0f;
            return;
        }

        if (_recenterHeld < 0f) return;
        _recenterHeld += Time.unscaledDeltaTime;
        if (_recenterHeld < 0.8f) return;
        _recenterHeld = -1f; // once a hold
        if (CinemaRig.Active) CinemaRig.Recenter();
        else VrRig.Recenter();
        VrControls.Rumble(XRNode.LeftHand, 0.5f, 0.08f);
        VrControls.Rumble(XRNode.RightHand, 0.5f, 0.08f);
        L.LogInfo("Recentred");
    }

    private void LateUpdate()
    {
        CinemaRig.LateUpdate();
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
                        Body.BodyPose.DevStage = int.Parse(arg);
                        break;
                    case "bodyshot":
                        StartCoroutine(Dev.DevPlay.BodyShot(arg));
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
                    case "flick":
                        Input.MotionThrow.DevFlick();
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
                        if (arg == "eye" || arg == "steady") VrSettings.Monitor.Value = arg == "eye" ? VrSettings.MonitorView.Eye : VrSettings.MonitorView.Steady;
                        L.LogInfo($"{Rendering.SpectatorView.State} | running={XrManager.IsRunning} setting={VrSettings.Monitor.Value} mode={XRSettings.gameViewRenderMode}");
                        break;
                    case "mirror":
                        // the desktop window shows: "both" (both eyes whole), "left" (cropped), "none"
                        XRSettings.gameViewRenderMode = arg == "both" ? GameViewRenderMode.BothEyes : arg == "none" ? GameViewRenderMode.None : GameViewRenderMode.LeftEye;
                        L.LogInfo("mirror " + XRSettings.gameViewRenderMode);
                        break;
                    case "safe":
                        Dev.DevPlay.Safe(arg != "off");
                        break;
                    case "levels":
                        Dev.DevPlay.Levels("");
                        break;
                    case "load":
                        Dev.DevPlay.Levels(arg);
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
