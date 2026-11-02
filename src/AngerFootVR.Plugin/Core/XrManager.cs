using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.Core;

/// <summary>
/// Brings up OpenXR inside the (non-XR) game build: Unity's native OpenXR provider (UnityOpenXR.dll, registered by its
/// UnitySubsystems manifest), the XR display + input subsystems created from their descriptors, and the session's
/// lifecycle, as com.unity.xr.openxr's own loader does. Once the display subsystem runs, the built-in pipeline renders
/// the cameras that target the screen in stereo to the headset. (Ported from InkVR's and MOUSE VR's; in this Mono build
/// Unity's own public subsystem API does what InkVR had to do by hand under IL2CPP.)
/// </summary>
public static class XrManager
{
    public enum State
    {
        Off,
        WaitingForSession,
        Running,
        Failed
    }

    public static State Current { get; private set; } = State.Off;
    public static string RuntimeName { get; private set; } = "";
    public static XRDisplaySubsystem Display { get; private set; }
    public static XRInputSubsystem Input { get; private set; }
    public static OpenXRNative.NativeEvent SessionState { get; private set; }
    public static bool IsRunning => Current == State.Running && Display != null && Display.running;

    private static readonly Queue<(OpenXRNative.NativeEvent e, ulong payload)> Events = new();
    private static readonly object Lock = new();
    private static OpenXRNative.ReceiveNativeEventDelegate _callback; // must stay referenced

    public static string PluginDir => Path.Combine(Application.dataPath, "Plugins", "x86_64");

    public static bool Initialize()
    {
        if (Current != State.Off) return Current != State.Failed;
        try
        {
            var provider = Path.Combine(PluginDir, "UnityOpenXR.dll");
            var loader = Path.Combine(PluginDir, "openxr_loader.dll");
            if (!File.Exists(provider) || !File.Exists(loader))
                return Fail("UnityOpenXR.dll / openxr_loader.dll missing from the game's Plugins folder (tools/install-xr.sh).");
            if (!OpenXRNative.Preload(provider)) return Fail("Could not load UnityOpenXR.dll");

            // the order com.unity.xr.openxr's own OpenXRLoader.Initialize uses
            var legacy = OpenXRNative.IsLegacy;
            Plugin.L.LogInfo($"OpenXR provider: {(legacy ? "pre-1.0 (legacy entry points)" : "1.x")}");
            if (!legacy) OpenXRNative.SetSuccessfullyInitialized(false);
            var loaded = legacy
                ? OpenXRNative.LoadOpenXRLibraryLegacy(Path.Combine(PluginDir, "openxr_loader"))
                : OpenXRNative.LoadOpenXRLibrary(Encoding.Unicode.GetBytes(loader + '\0'));
            if (!loaded) return Fail("Could not load the OpenXR loader.");
            OpenXRNative.SetProcAddressPtrAndLoadStage1(OpenXRHook.Wrap(OpenXRNative.GetProcAddressPtr(true)));
            SetApplicationInfo(legacy);
            _callback = OnNativeEvent;
            OpenXRNative.SetCallbacks(_callback);
            // (the game's shaders were built without VR: no single-pass instanced variants; each eye on its own)
            OpenXRNative.SetRenderMode(OpenXRNative.RenderMode.MultiPass);
            OpenXRNative.SetDepthSubmissionMode(0);
            Optional(() => OpenXRNative.SetSymmetricProjection(false));
            Optional(() => OpenXRNative.SetLatencyOptimization(0));
            Optional(() => OpenXRNative.SetUseOpenXRPredictedTime(true));
            if (!OpenXRNative.InitializeSession())
                return Fail("session_InitializeSession failed: " + OpenXRNative.LastError());

            Display = Create<XRDisplaySubsystemDescriptor, XRDisplaySubsystem>("OpenXR Display");
            Input = Create<XRInputSubsystemDescriptor, XRInputSubsystem>("OpenXR Input");
            if (Display == null || Input == null)
                return Fail("OpenXR subsystems not registered (is UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json installed?)");

            if (OpenXRNative.HasExport("NativeConfig_GetRuntimeName") && OpenXRNative.GetRuntimeName(out var namePtr))
                RuntimeName = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(namePtr) ?? "";
            Plugin.L.LogInfo($"OpenXR initialized. Runtime: '{RuntimeName}', graphics {SystemInfo.graphicsDeviceType}");
            Current = State.WaitingForSession;
            // (the test runtime doesn't pace frames: without a cap the game spins at 500+ fps for nothing)
            Rendering.VrQuality.FramePacing();
            Plugin.L.LogInfo($"Frame pacing: v-sync off, cap {Application.targetFrameRate}");
            if (!OpenXRNative.CreateSessionIfNeeded())
                return Fail("session_CreateSessionIfNeeded failed: " + OpenXRNative.LastError());
            return true;
        }
        catch (Exception e)
        {
            return Fail(e.ToString());
        }
    }

    public static void Update()
    {
        if (Current is State.Off or State.Failed) return;
        OpenXRNative.PumpMessageLoop();
        while (true)
        {
            (OpenXRNative.NativeEvent e, ulong payload) ev;
            lock (Lock)
            {
                if (Events.Count == 0) break;
                ev = Events.Dequeue();
            }

            Handle(ev.e, ev.payload);
        }
    }

    private static void OnNativeEvent(OpenXRNative.NativeEvent e, ulong payload)
    {
        lock (Lock) Events.Enqueue((e, payload));
    }

    private static void Handle(OpenXRNative.NativeEvent e, ulong payload)
    {
        if (e >= OpenXRNative.NativeEvent.XrIdle && e <= OpenXRNative.NativeEvent.XrInstanceLossPending) SessionState = e;
        if (e != OpenXRNative.NativeEvent.XrSessionStateChanged && e != OpenXRNative.NativeEvent.XrSetupConfigValues)
            Plugin.L.LogInfo($"OpenXR event {e} 0x{payload:X}");
        switch (e)
        {
            case OpenXRNative.NativeEvent.XrReady:
                Begin();
                break;
            case OpenXRNative.NativeEvent.XrStopping:
                Input?.Stop();
                Display?.Stop();
                OpenXRNative.EndSession();
                Current = State.WaitingForSession;
                break;
        }
    }

    private static void Begin()
    {
        OpenXRNative.BeginSession();
        Display.Start();
        Input.Start();
        if (!Input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor))
            Plugin.L.LogWarning("Floor tracking origin not supported; using device origin.");
        if (Display.running && Input.running)
        {
            Current = State.Running;
            Rendering.VrQuality.OnVrStarted();
            Plugin.L.LogInfo($"OpenXR session running. Origin={Input.GetTrackingOriginMode()} passes={Display.GetRenderPassCount()} XRSettings.enabled={XRSettings.enabled} device='{XRSettings.loadedDeviceName}' eye={XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight}");
        }
        else Fail($"Subsystems failed to start (display={Display.running}, input={Input.running})");
    }

    private static TSubsystem Create<TDescriptor, TSubsystem>(string id)
        where TDescriptor : IntegratedSubsystemDescriptor<TSubsystem>
        where TSubsystem : IntegratedSubsystem<TDescriptor>
    {
        var list = new List<TDescriptor>();
        SubsystemManager.GetSubsystemDescriptors(list);
        var names = new StringBuilder();
        foreach (var d in list)
        {
            names.Append(d.id).Append(", ");
            if (d.id != id) continue;
            var s = d.Create();
            Plugin.L.LogInfo($"Created subsystem '{id}': {(s != null ? "ok" : "null")}");
            return s;
        }

        Plugin.L.LogWarning($"Descriptor '{id}' not found. Registered: {names}");
        return null;
    }

    private static void SetApplicationInfo(bool legacy)
    {
        var app = Hash(Application.version);
        if (legacy) OpenXRNative.SetApplicationInfoLegacy(Application.productName, app, Hash(Application.unityVersion));
        else OpenXRNative.SetApplicationInfo(Application.productName, Application.version, app, Application.unityVersion);
    }

    /// <summary>The version hash the OpenXR package sends: the first 4 bytes of the MD5, big-endian.</summary>
    private static uint Hash(string text)
    {
        byte[] data;
        using (var md5 = MD5.Create()) data = md5.ComputeHash(Encoding.UTF8.GetBytes(text));
        if (BitConverter.IsLittleEndian) Array.Reverse(data);
        return BitConverter.ToUInt32(data, 0);
    }

    /// <summary>Calls a provider setting that older providers don't have.</summary>
    private static void Optional(Action call)
    {
        try
        {
            call();
        }
        catch (EntryPointNotFoundException e)
        {
            Plugin.L.LogInfo("Provider lacks " + e.Message);
        }
    }

    private static bool Fail(string why)
    {
        Plugin.L.LogError("VR init failed: " + why);
        Current = State.Failed;
        return false;
    }
}
