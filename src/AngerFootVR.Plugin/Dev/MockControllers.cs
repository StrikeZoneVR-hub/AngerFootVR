using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using AngerFootVR.Core;
using UnityEngine;

namespace AngerFootVR.Dev;

/// <summary>
/// Dev runs on the test runtime: real controller input through OpenXR's conformance automation
/// (XR_EXT_conformance_automation), so the whole path a headset's controllers take (the mod's action set, the hands, the
/// body's arms, the gun, the laser) can be checked here. Poses are set in the play space as a headset reports them
/// (Unity axes: grip +z along the handle, the back of the hand outward; the aim pose 40 degrees down from the grip, as
/// Quest Touch's).
///   ctl left|right x y z [pitch yaw roll]     the grip pose (metres, degrees)
///   ctlbtn left|right a|b|x|y|trigger|grip|menu|stick value
///   ctlstick left|right x y
/// </summary>
internal static unsafe class MockControllers
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Posef
    {
        public float qx, qy, qz, qw, px, py, pz;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Vec2
    {
        public float x, y;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ActiveFn(ulong session, ulong profile, ulong top, uint active);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int BoolFn(ulong session, ulong top, ulong input, uint state);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FloatFn(ulong session, ulong top, ulong input, float state);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Vec2Fn(ulong session, ulong top, ulong input, Vec2 state);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int LocationFn(ulong session, ulong top, ulong input, ulong space, Posef pose);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PathFn(ulong instance, byte* path, ulong* result);

    private static ActiveFn _active;
    private static BoolFn _bool;
    private static FloatFn _float;
    private static Vec2Fn _vec2;
    private static LocationFn _location;
    private static PathFn _path;

    private static bool Ready()
    {
        if (_location != null) return true;
        if (!OpenXRHook.AutomationEnabled || XrManager.Instance == 0)
        {
            Plugin.L.LogInfo("ctl: no controller automation (dev runs on the test runtime only)");
            return false;
        }

        T Fn<T>(string name) where T : class => Marshal.GetDelegateForFunctionPointer(OpenXRHook.Resolve(XrManager.Instance, name), typeof(T)) as T;
        _active = Fn<ActiveFn>("xrSetInputDeviceActiveEXT");
        _bool = Fn<BoolFn>("xrSetInputDeviceStateBoolEXT");
        _float = Fn<FloatFn>("xrSetInputDeviceStateFloatEXT");
        _vec2 = Fn<Vec2Fn>("xrSetInputDeviceStateVector2fEXT");
        _location = Fn<LocationFn>("xrSetInputDeviceLocationEXT");
        _path = Fn<PathFn>("xrStringToPath");
        foreach (var h in new[] { "left", "right" })
            _active(XrManager.Session, Path("/interaction_profiles/oculus/touch_controller"), Path("/user/hand/" + h), 1);
        return true;
    }

    private static ulong Path(string s)
    {
        var b = Encoding.ASCII.GetBytes(s + "\0");
        ulong r;
        fixed (byte* p = b) _path(XrManager.Instance, p, &r);
        return r;
    }

    private static Posef ToXr(Vector3 p, Quaternion q) => new Posef { qx = -q.x, qy = -q.y, qz = q.z, qw = q.w, px = p.x, py = p.y, pz = -p.z };

    /// <summary>The grip (and the aim, 40 degrees down from it) of one hand, in the play space.</summary>
    public static void Place(string hand, Vector3 pos, Vector3 euler)
    {
        if (!Ready()) return;
        var top = Path("/user/hand/" + hand);
        var grip = Quaternion.Euler(euler);
        var r1 = _location(XrManager.Session, top, Path($"/user/hand/{hand}/input/grip/pose"), XrManager.AppSpace, ToXr(pos, grip));
        var r2 = _location(XrManager.Session, top, Path($"/user/hand/{hand}/input/aim/pose"), XrManager.AppSpace, ToXr(pos, grip * Quaternion.Euler(40f, 0f, 0f)));
        Plugin.L.LogInfo($"ctl {hand} grip at {pos:F2} turned {euler:F0} ({r1}, {r2})");
    }

    public static void Button(string hand, string button, float value)
    {
        if (!Ready()) return;
        var top = Path("/user/hand/" + hand);
        var p = $"/user/hand/{hand}/input/";
        int r;
        switch (button)
        {
            case "trigger":
                r = _float(XrManager.Session, top, Path(p + "trigger/value"), value);
                break;
            case "grip":
                r = _float(XrManager.Session, top, Path(p + "squeeze/value"), value);
                break;
            case "stick":
                r = _bool(XrManager.Session, top, Path(p + "thumbstick/click"), value > 0.5f ? 1u : 0u);
                break;
            default:
                r = _bool(XrManager.Session, top, Path(p + button + "/click"), value > 0.5f ? 1u : 0u);
                break;
        }

        Plugin.L.LogInfo($"ctlbtn {hand} {button} {value} ({r})");
    }

    public static void Stick(string hand, float x, float y)
    {
        if (!Ready()) return;
        var r = _vec2(XrManager.Session, Path("/user/hand/" + hand), Path($"/user/hand/{hand}/input/thumbstick"), new Vec2 { x = x, y = y });
        Plugin.L.LogInfo($"ctlstick {hand} {x:F2},{y:F2} ({r})");
    }

    public static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
}
