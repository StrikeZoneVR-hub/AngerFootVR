using AngerFootVR.Core;
using Framework;
using HarmonyLib;
using UnityEngine;

namespace AngerFootVR.Input;

/// <summary>
/// Throwing your gun: the gun hand's grip throws it, from your hand, where it points (the game's own throw, at its own
/// speed). A gentle aim assist takes a throw that's nearly on an enemy (within 10 degrees, in sight) onto them, lifting
/// it for the drop. (Throws by swinging or flicking the hand were tried and taken out: only the grip throws.)
/// </summary>
[HarmonyPatch]
internal static class MotionThrow
{
    /// <summary>The game's throw button: the gun hand's grip.</summary>
    public static bool Held => VrControls.Held(GunHold.Hand, VrButton.Grip);

    /// <summary>The thrown gun (or the bottle you drank) leaves your hand, the way you threw it.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(HoldableObjectType), nameof(HoldableObjectType.ThrowObject))]
    private static void FromTheHand(GameObject thrownObject)
    {
        if (!XrManager.IsRunning || CinemaRig.Active || thrownObject == null) return;
        var pose = VrHands.GetRaw(GunHold.Hand);
        if (!pose.Tracked) return;
        var body = thrownObject.GetComponent<Rigidbody>();
        if (body == null) return;
        var speed = body.velocity.magnitude;
        var dir = GunHold.Aim(pose) * Vector3.forward;
        var from = pose.Position + dir * 0.15f;

        // not through a wall: if something's between your head and your hand, it leaves from your head
        var head = VrRig.HeadPosition;
        if (Physics.Linecast(head, from, out _, LayerMasks.ThingsThrownOrDroppedObjectsDepenetrateFrom)) from = head + dir * 0.1f;
        thrownObject.transform.position = from;

        // aim assist: nearly on an enemy (10 degrees, in sight, 30 m): onto them, lifted for the drop; else the game's
        // own homing: on what's in line
        var target = Assist(from, dir, speed);
        if (target.HasValue) dir = target.Value;
        else if (Physics.Raycast(from, dir, out var hit, 100f, LayerMasks.ThingsPlayerProjectilesCanHit)) dir = (hit.point - from).normalized;
        body.velocity = dir * speed;
        if (Dev.ActionLog.Enabled) Plugin.L.LogInfo($"ACTION thrown from the hand at {from} towards {dir} ({speed:F0} m/s){(target.HasValue ? " onto an enemy" : "")}");
    }

    private static Vector3? Assist(Vector3 from, Vector3 dir, float speed)
    {
        Vector3? best = null;
        var bestAngle = 10f;
        foreach (var e in Object.FindObjectsOfType<Enemy>())
        {
            if (e == null || e.Health == null || !e.Health.IsAlive) continue;
            var chest = e.transform.position + Vector3.up * 1.2f;
            var to = chest - from;
            var dist = to.magnitude;
            if (dist > 30f || dist < 0.5f) continue;
            var angle = Vector3.Angle(dir, to);
            if (angle > bestAngle) continue;
            if (Physics.Linecast(from, chest, out var hit, LayerMasks.ThingsPlayerProjectilesCanHit) && hit.collider.GetComponentInParent<Enemy>() != e) continue;
            // the drop on the way: aim a little over them
            var t = dist / Mathf.Max(5f, speed);
            best = (to + Vector3.up * (0.5f * 9.81f * t * t)).normalized;
            bestAngle = angle;
        }

        return best;
    }
}
