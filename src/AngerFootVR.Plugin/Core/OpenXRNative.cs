using System;
using System.Runtime.InteropServices;

namespace AngerFootVR.Core;

/// <summary>
/// Entry points of Unity's native OpenXR provider (UnityOpenXR.dll from com.unity.xr.openxr), the same calls the
/// package's managed OpenXRLoader makes. The game was built without XR, so the mod plays the role of that loader.
/// (Ported from InkVR's and MOUSE VR's; the provider is loaded from the game's Plugins folder by name.)
/// </summary>
public static class OpenXRNative
{
    private const string Lib = "UnityOpenXR";

    // Mirrors OpenXRFeature.NativeEvent.
    public enum NativeEvent
    {
        XrSetupConfigValues,
        XrSystemIdChanged,
        XrInstanceChanged,
        XrSessionChanged,
        XrBeginSession,
        XrSessionStateChanged,
        XrChangedSpaceApp,
        XrEndSession,
        XrDestroySession,
        XrDestroyInstance,
        XrIdle,
        XrReady,
        XrSynchronized,
        XrVisible,
        XrFocused,
        XrStopping,
        XrExiting,
        XrLossPending,
        XrInstanceLossPending,
        XrRestartRequested,
        XrRequestRestartLoop,
        XrRequestGetSystemLoop
    }

    public enum RenderMode
    {
        MultiPass,
        SinglePassInstanced
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ReceiveNativeEventDelegate(NativeEvent e, ulong payload);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryW(string path);

    [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    private static IntPtr _module;

    /// <summary>Load the provider from the game's Plugins folder first, so DllImport("UnityOpenXR") finds that module.</summary>
    public static bool Preload(string fullPath) => (_module = LoadLibraryW(fullPath)) != IntPtr.Zero;

    /// <summary>Whether the loaded provider has this entry point (older providers lack some).</summary>
    public static bool HasExport(string name) => _module != IntPtr.Zero && GetProcAddress(_module, name) != IntPtr.Zero;

    /// <summary>Providers before 1.0 (the only ones whose start-up runs on Unity 2019.4) take the loader as an ANSI name
    /// without ".dll", and the app's and engine's versions as hashes.</summary>
    public static bool IsLegacy => !HasExport("session_SetSuccessfullyInitialized");

    [DllImport(Lib, EntryPoint = "main_LoadOpenXRLibrary", CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool LoadOpenXRLibraryLegacy(string loaderPathWithoutExtension);

    [DllImport(Lib, EntryPoint = "NativeConfig_SetApplicationInfo", CharSet = CharSet.Ansi)]
    public static extern void SetApplicationInfoLegacy(string applicationName, uint applicationVersionHash, uint engineVersionHash);

    [DllImport(Lib, EntryPoint = "main_LoadOpenXRLibrary")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool LoadOpenXRLibrary(byte[] loaderPathWide);

    [DllImport(Lib, EntryPoint = "main_UnloadOpenXRLibrary")]
    public static extern void UnloadOpenXRLibrary();

    [DllImport(Lib, EntryPoint = "NativeConfig_SetCallbacks")]
    public static extern void SetCallbacks(ReceiveNativeEventDelegate callback);

    [DllImport(Lib, EntryPoint = "NativeConfig_SetApplicationInfo", CharSet = CharSet.Ansi)]
    public static extern void SetApplicationInfo(string applicationName, string applicationVersion, uint applicationVersionHash, string engineVersion);

    [DllImport(Lib, EntryPoint = "NativeConfig_SetRenderMode")]
    public static extern void SetRenderMode(RenderMode mode);

    [DllImport(Lib, EntryPoint = "NativeConfig_SetLatencyOptimization")]
    public static extern void SetLatencyOptimization(int mode);

    [DllImport(Lib, EntryPoint = "NativeConfig_SetDepthSubmissionMode")]
    public static extern void SetDepthSubmissionMode(int mode);

    [DllImport(Lib, EntryPoint = "NativeConfig_SetSymmetricProjection")]
    public static extern void SetSymmetricProjection([MarshalAs(UnmanagedType.I1)] bool enabled);

    [DllImport(Lib, EntryPoint = "NativeConfig_SetUseOpenXRPredictedTime")]
    public static extern void SetUseOpenXRPredictedTime([MarshalAs(UnmanagedType.I1)] bool enabled);

    [DllImport(Lib, EntryPoint = "NativeConfig_GetRuntimeName")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool GetRuntimeName(out IntPtr runtimeNamePtr);

    [DllImport(Lib, EntryPoint = "NativeConfig_GetProcAddressPtr")]
    public static extern IntPtr GetProcAddressPtr([MarshalAs(UnmanagedType.I1)] bool loaderDefault);

    [DllImport(Lib, EntryPoint = "NativeConfig_SetProcAddressPtrAndLoadStage1")]
    public static extern void SetProcAddressPtrAndLoadStage1(IntPtr func);

    [DllImport(Lib, EntryPoint = "session_InitializeSession")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool InitializeSession();

    [DllImport(Lib, EntryPoint = "session_CreateSessionIfNeeded")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool CreateSessionIfNeeded();

    [DllImport(Lib, EntryPoint = "session_BeginSession")]
    public static extern void BeginSession();

    [DllImport(Lib, EntryPoint = "session_EndSession")]
    public static extern void EndSession();

    [DllImport(Lib, EntryPoint = "session_DestroySession")]
    public static extern void DestroySession();

    [DllImport(Lib, EntryPoint = "session_SetSuccessfullyInitialized")]
    public static extern void SetSuccessfullyInitialized([MarshalAs(UnmanagedType.I1)] bool value);

    [DllImport(Lib, EntryPoint = "session_GetLastError")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool GetLastError(out IntPtr error);

    [DllImport(Lib, EntryPoint = "messagepump_PumpMessageLoop")]
    public static extern void PumpMessageLoop();

    public static string LastError()
    {
        try
        {
            return GetLastError(out var p) ? Marshal.PtrToStringAnsi(p) : "";
        }
        catch
        {
            return "";
        }
    }
}
