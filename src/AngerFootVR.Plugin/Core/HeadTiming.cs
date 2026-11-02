using UnityEngine;

namespace AngerFootVR.Core;

/// <summary>
/// How far the head turned between the rig's LateUpdate reading and the one just before the frame is drawn (the camera
/// is placed again then). Reported in VRPERF lines: on a headset, a few tenths of a degree a frame is normal when
/// turning; without the second placement that much showed as the whole view wobbling.
/// </summary>
internal static class HeadTiming
{
    private static float _sum, _max;
    private static int _n;

    public static void Note(Quaternion lateUpdate, Quaternion beforeRender)
    {
        var a = Quaternion.Angle(lateUpdate, beforeRender);
        _sum += a;
        _max = Mathf.Max(_max, a);
        _n++;
    }

    public static string Take()
    {
        var s = _n > 0 ? $"head moved {_sum / _n:F2} deg avg (max {_max:F2}) between update and render, {_n} frames" : "no render-time head placement";
        _sum = _max = 0f;
        _n = 0;
        return s;
    }
}
