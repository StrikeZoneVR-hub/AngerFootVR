using AngerFootVR.Core;
using Framework;
using HarmonyLib;
using UnityEngine;

namespace AngerFootVR.Input;

/// <summary>
/// Throwing your gun. The gun hand's grip throws it (as ever), and so does a flick: snap your gun hand forward (not with
/// the trigger held) and the game's own throw goes off. Either way the gun leaves your hand, not the middle of your view,
/// and flies where you threw it (a flick: the way your hand went, steadied by where it points; the grip: where it
/// points), at the game's own throw speed, still homing on what's in line the way the game's throw does. OPTIONS > MOTION
/// THROW turns the flick off.
/// </summary>
[HarmonyPatch]
internal static class MotionThrow
{
    private const float FlickSpeed = 2.6f; // m/s forward
    private static int _requestFrame = -10;
    private static Vector3 _flickDir;
    private static float _flickAt = -10f, _cooldown;

    /// <summary>The game asks whether throw is held: a flick holds it for its frame and the next (the game may ask
    /// before this sees the flick, and more than once a frame).</summary>
    public static bool Held => Time.frameCount - _requestFrame <= 1;

    public static void Update()
    {
        if (VrSettings.MotionThrow == null || !VrSettings.MotionThrow.Value || !GunHold.Holding || Time.unscaledTime < _cooldown) return;
        if (!SingletonBehaviour<Player>.HasInstance) return;
        var player = SingletonBehaviour<Player>.Instance;
        if (!player.HasWeapon || player.HandRig == null || !player.HandRig.CanThrow) return;
        if (VrControls.Held(GunHold.Hand, VrButton.Trigger)) return;
        var pose = VrHands.Get(GunHold.Hand);
        var v = pose.Velocity;
        var aim = GunHold.Aim(pose) * Vector3.forward;
        var facing = VrRig.HeadRotation * Vector3.forward;
        facing.y = 0f;
        // a flick: fast, out and away from you, roughly the way the gun points
        if (v.magnitude < FlickSpeed || Vector3.Dot(v.normalized, aim) < 0.45f || Vector3.Dot(new Vector3(v.x, 0f, v.z).normalized, facing.normalized) < 0.3f) return;
        _flickDir = Vector3.Slerp(aim, v.normalized, 0.5f).normalized;
        _flickAt = Time.unscaledTime;
        _requestFrame = Time.frameCount;
        _cooldown = Time.unscaledTime + 0.7f;
        VrControls.Rumble(GunHold.Hand, 0.6f, 0.07f);
        if (Dev.ActionLog.Enabled) Plugin.L.LogInfo($"ACTION flick throw at {v.magnitude:F1} m/s towards {_flickDir}");
    }

    /// <summary>Dev: a flick of the gun hand forward (the test runtime's hands don't move).</summary>
    public static void DevFlick()
    {
        VrControls.DevPresent();
        var pose = VrHands.Get(GunHold.Hand);
        _flickDir = GunHold.Aim(pose) * Vector3.forward;
        _flickAt = Time.unscaledTime;
        _requestFrame = Time.frameCount;
    }

    /// <summary>The thrown gun leaves your hand, the way you threw it.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(HoldableObjectType), nameof(HoldableObjectType.ThrowObject))]
    private static void FromTheHand(GameObject thrownObject)
    {
        if (!XrManager.IsRunning || CinemaRig.Active || thrownObject == null) return;
        var pose = VrHands.Get(GunHold.Hand);
        if (!pose.Tracked) return;
        var body = thrownObject.GetComponent<Rigidbody>();
        if (body == null) return;
        var speed = body.velocity.magnitude;
        var dir = Time.unscaledTime - _flickAt < 0.8f ? _flickDir : GunHold.Aim(pose) * Vector3.forward;
        var from = pose.Position + dir * 0.15f;

        // not through a wall: if something's between your head and your hand, it leaves from your head
        var head = VrRig.HeadPosition;
        if (Physics.Linecast(head, from, out _, LayerMasks.ThingsThrownOrDroppedObjectsDepenetrateFrom)) from = head + dir * 0.1f;
        thrownObject.transform.position = from;

        // homing the way the game's throw does: on what's in line
        if (Physics.Raycast(from, dir, out var hit, 100f, LayerMasks.ThingsPlayerProjectilesCanHit)) dir = (hit.point - from).normalized;
        body.velocity = dir * speed;
        if (Dev.ActionLog.Enabled) Plugin.L.LogInfo($"ACTION gun thrown from the hand at {from} towards {dir} ({speed:F0} m/s)");
    }
}
