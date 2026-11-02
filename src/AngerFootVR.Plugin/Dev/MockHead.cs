using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace AngerFootVR.Dev;

/// <summary>Dev (Dark Revival's): the test runtime's head where a real one is (standing height, turned), set in the
/// runtime itself, so everything that reads the head (the rig, the body, the eyes the frame is drawn with) sees it, and
/// optionally turning from a thread of its own, as a real head moves between and during frames.</summary>
internal static class MockHead
{
    [DllImport("kernel32")] private static extern IntPtr GetModuleHandleW([MarshalAs(UnmanagedType.LPWStr)] string name);
    [DllImport("kernel32")] private static extern IntPtr GetProcAddress(IntPtr module, [MarshalAs(UnmanagedType.LPStr)] string name);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void SetSpaceFn(int type, Vector3 position, Quaternion orientation, int flags);

    private static SetSpaceFn _set;
    private static Thread _thread;
    private static volatile float _speed, _seconds, _yaw0, _pitch;
    private static Vector3 _pos;

    private static bool Ready()
    {
        if (_set != null) return true;
        var mod = GetModuleHandleW("mock_runtime.dll");
        var fn = mod == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(mod, "MockRuntime_SetReferenceSpace");
        if (fn == IntPtr.Zero)
        {
            Plugin.L.LogInfo("mockhead: not on the test runtime");
            return false;
        }

        _set = Marshal.GetDelegateForFunctionPointer<SetSpaceFn>(fn);
        return true;
    }

    /// <summary>The head at this height (metres above the floor), pitched (degrees, down positive) and turned.</summary>
    public static void Place(float height, float pitch, float yaw)
    {
        if (!Ready()) return;
        _pos = new Vector3(0f, height, 0f);
        _pitch = pitch;
        _yaw0 = yaw;
        // VIEW space (1): the head, relative to the play space
        _set(1, _pos, Quaternion.Euler(-pitch, -yaw, 0f), 0xF);
        Plugin.L.LogInfo($"mockhead at {height:F2} m, pitch {pitch:F0}, yaw {yaw:F0}");
    }

    /// <summary>The head turning at this speed for a while (from a thread), then back.</summary>
    public static void Spin(float degPerSecond, float seconds)
    {
        if (!Ready()) return;
        _speed = degPerSecond;
        _seconds = seconds;
        if (_thread != null && _thread.IsAlive) return;
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        _thread = new Thread(() =>
        {
            while (true)
            {
                var t = (System.Diagnostics.Stopwatch.GetTimestamp() - start) / (double)System.Diagnostics.Stopwatch.Frequency;
                if (t > _seconds) break;
                _set(1, _pos, Quaternion.Euler(-_pitch, -(_yaw0 + (float)(_speed * t)), 0f), 0xF);
                Thread.Sleep(1);
            }

            _set(1, _pos, Quaternion.Euler(-_pitch, -_yaw0, 0f), 0xF);
        }) { IsBackground = true, Name = "AFVR mock head" };
        _thread.Start();
        Plugin.L.LogInfo($"headspin {degPerSecond} deg/s for {seconds} s");
    }
}
