using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace AngerFootVR.Core;

/// <summary>
/// Sits between Unity's OpenXR provider and the OpenXR loader (the provider's own hook point, the one its features use:
/// xrGetInstanceProcAddr). The pre-1.0 provider that runs on Unity 2019.4 asks for some extensions whether or not the
/// runtime has them, and an instance asked for a missing extension isn't created at all; this drops the ones the
/// runtime doesn't offer (each one logged) so the rest of VR still starts. It also reads each frame's display time from
/// xrWaitFrame (the controllers are located for it) and resolves the functions the mod calls itself.
/// </summary>
internal static class OpenXRHook
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetInstanceProcAddrFn(ulong instance, IntPtr name, out IntPtr function);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateInstanceFn(IntPtr createInfo, IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumerateExtensionsFn(IntPtr layerName, uint capacity, out uint count, IntPtr properties);

    // XrInstanceCreateInfo (x64): enabledExtensionCount at 312, enabledExtensionNames (const char* const*) at 320
    private const int ExtensionCountOffset = 312, ExtensionNamesOffset = 320;

    // XrExtensionProperties: type, next, char extensionName[128], uint32 extensionVersion => 152 bytes
    private const int ExtensionPropertiesSize = 152, XrTypeExtensionProperties = 2;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int WaitFrameFn(ulong session, IntPtr waitInfo, IntPtr frameState);

    private static WaitFrameFn _realWait, _hookWait;
    private static IntPtr _hookWaitPtr;
    private static long _predictedTime, _predictedPeriod, _waitTicks, _waits;

    /// <summary>The display time of the frame being made (from the provider's xrWaitFrame), in OpenXR nanoseconds.</summary>
    public static long PredictedDisplayTime => System.Threading.Interlocked.Read(ref _predictedTime);

    /// <summary>The headset's frame period (nanoseconds).</summary>
    public static long PredictedDisplayPeriod => System.Threading.Interlocked.Read(ref _predictedPeriod);

    /// <summary>Average time spent held in xrWaitFrame since the last call (ms), and how many frames.</summary>
    public static (double ms, long frames) TakeWaitStats()
    {
        var ticks = System.Threading.Interlocked.Exchange(ref _waitTicks, 0);
        var n = System.Threading.Interlocked.Exchange(ref _waits, 0);
        return (n > 0 ? ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / n : -1, n);
    }

    /// <summary>An OpenXR function through the loader (for the mod's own calls).</summary>
    public static IntPtr Resolve(ulong instance, string name)
    {
        var n = Marshal.StringToHGlobalAnsi(name);
        try
        {
            return _realGetProc != null && _realGetProc(instance, n, out var fn) >= 0 ? fn : IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(n);
        }
    }

    private static int WaitFrame(ulong session, IntPtr waitInfo, IntPtr frameState)
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var r = _realWait(session, waitInfo, frameState);
        System.Threading.Interlocked.Add(ref _waitTicks, System.Diagnostics.Stopwatch.GetTimestamp() - t0);
        System.Threading.Interlocked.Increment(ref _waits);
        // XrFrameState: type, next, predictedDisplayTime (16), predictedDisplayPeriod (24), shouldRender
        if (r >= 0 && frameState != IntPtr.Zero)
        {
            System.Threading.Interlocked.Exchange(ref _predictedTime, Marshal.ReadInt64(frameState, 16));
            System.Threading.Interlocked.Exchange(ref _predictedPeriod, Marshal.ReadInt64(frameState, 24));
        }

        return r;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateReferenceSpaceFn(ulong session, IntPtr createInfo, out ulong space);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int LocateViewsFn(ulong session, IntPtr locateInfo, IntPtr viewState, uint capacity, IntPtr count, IntPtr views);

    private static CreateReferenceSpaceFn _realRefSpace, _hookRefSpace;
    private static LocateViewsFn _realLocateViews, _hookLocateViews;
    private static IntPtr _hookRefSpacePtr, _hookLocateViewsPtr;

    /// <summary>The reference spaces the provider made: handle -> type (1 view, 2 local, 3 stage).</summary>
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, int> SpaceTypes = new System.Collections.Concurrent.ConcurrentDictionary<ulong, int>();

    /// <summary>The last xrLocateViews: the space the eyes were asked for in, and the left eye's position there.</summary>
    public static volatile string LastViews = "";

    private static int CreateReferenceSpace(ulong session, IntPtr createInfo, out ulong space)
    {
        var r = _realRefSpace(session, createInfo, out space);
        // XrReferenceSpaceCreateInfo: type, next, referenceSpaceType (16)
        if (r >= 0) SpaceTypes[space] = Marshal.ReadInt32(createInfo, 16);
        return r;
    }

    private static int LocateViews(ulong session, IntPtr locateInfo, IntPtr viewState, uint capacity, IntPtr count, IntPtr views)
    {
        var r = _realLocateViews(session, locateInfo, viewState, capacity, count, views);
        if (r >= 0 && views != IntPtr.Zero && capacity > 0)
        {
            // XrViewLocateInfo: type, next, viewConfigurationType (16), displayTime (24), space (32)
            // XrView (64 bytes): type, next, pose (16: orientation x y z w, position x y z at 32), fov
            var space = (ulong)Marshal.ReadInt64(locateInfo, 32);
            SpaceTypes.TryGetValue(space, out var type);
            var x = BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(views, 32)), 0);
            var y = BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(views, 36)), 0);
            var z = BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(views, 40)), 0);
            LastViews = $"views located in space {space} (type {type}: {(type == 1 ? "VIEW" : type == 2 ? "LOCAL" : type == 3 ? "STAGE" : "?")}) left eye at ({x:F3}, {y:F3}, {z:F3})";
        }

        return r;
    }

    private static GetInstanceProcAddrFn _realGetProc, _hookGetProc; // all kept referenced for the process's life
    private static CreateInstanceFn _realCreate, _hookCreate;
    private static IntPtr _hookGetProcPtr, _hookCreatePtr;

    /// <summary>Returns the hook to hand the provider in place of the loader's xrGetInstanceProcAddr.</summary>
    public static IntPtr Wrap(IntPtr loaderGetInstanceProcAddr)
    {
        _realGetProc = Marshal.GetDelegateForFunctionPointer<GetInstanceProcAddrFn>(loaderGetInstanceProcAddr);
        _hookGetProc = GetInstanceProcAddr;
        _hookGetProcPtr = Marshal.GetFunctionPointerForDelegate(_hookGetProc);
        _hookCreate = CreateInstance;
        _hookCreatePtr = Marshal.GetFunctionPointerForDelegate(_hookCreate);
        _hookWait = WaitFrame;
        _hookWaitPtr = Marshal.GetFunctionPointerForDelegate(_hookWait);
        _hookRefSpace = CreateReferenceSpace;
        _hookRefSpacePtr = Marshal.GetFunctionPointerForDelegate(_hookRefSpace);
        _hookLocateViews = LocateViews;
        _hookLocateViewsPtr = Marshal.GetFunctionPointerForDelegate(_hookLocateViews);
        return _hookGetProcPtr;
    }

    private static int GetInstanceProcAddr(ulong instance, IntPtr name, out IntPtr function)
    {
        var result = _realGetProc(instance, name, out function);
        if (result < 0 || function == IntPtr.Zero) return result;
        var n = Marshal.PtrToStringAnsi(name);
        if (n == "xrCreateInstance")
        {
            _realCreate = Marshal.GetDelegateForFunctionPointer<CreateInstanceFn>(function);
            function = _hookCreatePtr;
        }
        else if (n == "xrWaitFrame")
        {
            _realWait = Marshal.GetDelegateForFunctionPointer<WaitFrameFn>(function);
            function = _hookWaitPtr;
        }
        else if (n == "xrCreateReferenceSpace")
        {
            _realRefSpace = Marshal.GetDelegateForFunctionPointer<CreateReferenceSpaceFn>(function);
            function = _hookRefSpacePtr;
        }
        else if (n == "xrLocateViews")
        {
            _realLocateViews = Marshal.GetDelegateForFunctionPointer<LocateViewsFn>(function);
            function = _hookLocateViewsPtr;
        }

        return result;
    }

    private static int CreateInstance(IntPtr createInfo, IntPtr instance)
    {
        var count = Marshal.ReadInt32(createInfo, ExtensionCountOffset);
        var names = Marshal.ReadIntPtr(createInfo, ExtensionNamesOffset);
        var available = RuntimeExtensions();
        if (available == null || count == 0 || names == IntPtr.Zero) return _realCreate(createInfo, instance);

        var keep = new List<IntPtr>();
        var dropped = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var p = Marshal.ReadIntPtr(names, i * IntPtr.Size);
            var ext = Marshal.PtrToStringAnsi(p);
            if (available.Contains(ext)) keep.Add(p);
            else dropped.Add(ext);
        }

        if (dropped.Count == 0) return _realCreate(createInfo, instance);
        Plugin.L.LogInfo($"OpenXR: the runtime lacks {string.Join(", ", dropped.ToArray())}; starting without it");
        var array = Marshal.AllocHGlobal(IntPtr.Size * Math.Max(1, keep.Count));
        try
        {
            for (var i = 0; i < keep.Count; i++) Marshal.WriteIntPtr(array, i * IntPtr.Size, keep[i]);
            Marshal.WriteInt32(createInfo, ExtensionCountOffset, keep.Count);
            Marshal.WriteIntPtr(createInfo, ExtensionNamesOffset, array);
            return _realCreate(createInfo, instance);
        }
        finally
        {
            Marshal.WriteInt32(createInfo, ExtensionCountOffset, count);
            Marshal.WriteIntPtr(createInfo, ExtensionNamesOffset, names);
            Marshal.FreeHGlobal(array);
        }
    }

    /// <summary>The extensions the active runtime offers (null if they can't be listed).</summary>
    private static HashSet<string> RuntimeExtensions()
    {
        var name = Marshal.StringToHGlobalAnsi("xrEnumerateInstanceExtensionProperties");
        try
        {
            if (_realGetProc(0, name, out var fn) < 0 || fn == IntPtr.Zero) return null;
            var enumerate = Marshal.GetDelegateForFunctionPointer<EnumerateExtensionsFn>(fn);
            if (enumerate(IntPtr.Zero, 0, out var n, IntPtr.Zero) < 0 || n == 0) return null;
            var buffer = Marshal.AllocHGlobal(ExtensionPropertiesSize * (int)n);
            try
            {
                for (var i = 0; i < n; i++)
                {
                    var item = buffer + i * ExtensionPropertiesSize;
                    for (var b = 0; b < ExtensionPropertiesSize; b += 4) Marshal.WriteInt32(item, b, 0);
                    Marshal.WriteInt32(item, 0, XrTypeExtensionProperties);
                }

                if (enumerate(IntPtr.Zero, n, out n, buffer) < 0) return null;
                var set = new HashSet<string>();
                for (var i = 0; i < n; i++) set.Add(Marshal.PtrToStringAnsi(buffer + i * ExtensionPropertiesSize + 16));
                return set;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(name);
        }
    }
}
