using UnityEngine;

namespace AngerFootVR.Body;

/// <summary>IK helpers for the VR body (it starts each frame from its neutral pose, so these stay stable).</summary>
internal static class BodyIk
{
    /// <summary>
    /// Two-bone IK (shoulder-elbow-wrist, hip-knee-ankle): works out where the middle joint must be for the end to
    /// reach the target (as near as the limb allows), on the pole point's side, then swings each bone onto it.
    /// </summary>
    public static void TwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole)
    {
        var aPos = a.position;
        var la = (b.position - aPos).magnitude;
        var lb = (c.position - b.position).magnitude;
        if (la < 1e-5f || lb < 1e-5f) return;
        var toTarget = target - aPos;
        var d = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(la - lb) + 1e-4f, (la + lb) * 0.9999f);
        var dir = toTarget.sqrMagnitude > 1e-10f ? toTarget.normalized : (c.position - aPos).normalized;

        // the middle joint: x along the line to the target, h out towards the pole
        var x = (la * la - lb * lb + d * d) / (2f * d);
        var h = Mathf.Sqrt(Mathf.Max(0f, la * la - x * x));
        var side = Vector3.ProjectOnPlane(pole - aPos, dir);
        if (side.sqrMagnitude < 1e-8f) side = Vector3.ProjectOnPlane(b.position - aPos, dir);
        if (side.sqrMagnitude < 1e-8f) side = Vector3.Cross(dir, Vector3.up);
        var joint = aPos + dir * x + side.normalized * h;

        a.rotation = Quaternion.FromToRotation(b.position - aPos, joint - aPos) * a.rotation;
        var end = aPos + dir * d;
        b.rotation = Quaternion.FromToRotation(c.position - b.position, end - b.position) * b.rotation;
    }

    /// <summary>Turns a bone (and so its children) so the point "from" moves towards "to" by the fraction k.</summary>
    public static void Lean(Transform bone, Vector3 from, Vector3 to, float k)
    {
        var p = bone.position;
        if ((from - p).sqrMagnitude < 1e-8f || (to - p).sqrMagnitude < 1e-8f) return;
        var q = Quaternion.FromToRotation(from - p, to - p);
        bone.rotation = Quaternion.Slerp(Quaternion.identity, q, k) * bone.rotation;
    }
}
