using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace AngerFootVR.Core;

/// <summary>
/// Sits between Unity's OpenXR provider and the OpenXR loader (the provider's own hook point, the one its features use:
/// xrGetInstanceProcAddr). The pre-1.0 provider that runs on Unity 2019.4 asks for some extensions whether or not the
/// runtime has them, and an instance asked for a missing extension isn't created at all; this drops the ones the
/// runtime doesn't offer (each one logged) so the rest of VR still starts.
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
        return _hookGetProcPtr;
    }

    private static int GetInstanceProcAddr(ulong instance, IntPtr name, out IntPtr function)
    {
        var result = _realGetProc(instance, name, out function);
        if (result >= 0 && function != IntPtr.Zero && Marshal.PtrToStringAnsi(name) == "xrCreateInstance")
        {
            _realCreate = Marshal.GetDelegateForFunctionPointer<CreateInstanceFn>(function);
            function = _hookCreatePtr;
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
