using AngerFootVR.Core;
using Framework;
using HarmonyLib;
using UnityEngine;

namespace AngerFootVR.Input;

/// <summary>
/// Throwing your gun, for real. Hold the gun hand's grip, swing and let go: it leaves your hand the way your hand was
/// going (steadied a little by where the gun points), as hard as you threw it (from a lob to a fastball, 0.85 to 1.6
/// times the game's own throw), spinning the way your wrist turned. A tap of the grip without a swing throws it where it
/// points, as before, so a throw never needs a big arm. A flick of the gun hand (no grip) throws too. A gentle aim
/// assist takes a throw that's nearly on an enemy (within 10 degrees, in sight) onto them, lifting it for the drop.
/// OPTIONS: SWING THROW off = the grip throws as soon as it's squeezed; MOTION THROW off = no flick.
/// </summary>
[HarmonyPatch]
internal static class MotionThrow
{
    private const float FlickSpeed = 2.6f; // m/s forward
    private const float SwingSpeed = 1.5f; // m/s at the release: a swing (slower: a tap)
    private static int _requestFrame = -10;
    private static Vector3 _throwDir, _spin;
    private static float _throwAt = -10f, _cooldown, _power = 1f;
    private static bool _gripping;

    // the gun hand's last moments: speed and turn (for the release)
    private const int History = 12;
    private static readonly Vector3[] Vel = new Vector3[History];
    private static readonly float[] At = new float[History];
    private static readonly Quaternion[] Rot = new Quaternion[History];
    private static int _head;

    /// <summary>The game asks whether throw is held: a throw holds it for its frame and the next (the game may ask
    /// before this sees it, and more than once a frame).</summary>
    public static bool Held => Time.frameCount - _requestFrame <= 1;

    private static bool Swinging => VrSettings.SwingThrow == null || VrSettings.SwingThrow.Value;

    /// <summary>What the gun hand's grip means to the game this frame: with SWING THROW the throw goes on the grip's
    /// release (Held); without it, as soon as it's squeezed.</summary>
    public static bool GripThrows => !Swinging && VrControls.Held(GunHold.Hand, VrButton.Grip);

    public static void Update()
    {
        if (!SingletonBehaviour<Player>.HasInstance || !GunHold.Holding)
        {
            _gripping = false;
            return;
        }

        var player = SingletonBehaviour<Player>.Instance;
        var pose = VrHands.GetRaw(GunHold.Hand);
        _head = (_head + 1) % History;
        Vel[_head] = pose.Velocity;
        At[_head] = Time.unscaledTime;
        Rot[_head] = pose.Rotation;
        var canThrow = player.HasWeapon && player.HandRig != null && player.HandRig.CanThrow && Time.unscaledTime >= _cooldown;

        // the grip: hold, swing, let go (or a tap)
        if (Swinging)
        {
            if (VrControls.Down(GunHold.Hand, VrButton.Grip)) _gripping = true;
            if (_gripping && VrControls.Up(GunHold.Hand, VrButton.Grip))
            {
                _gripping = false;
                if (canThrow) Release(pose);
            }
        }

        // the flick (no grip)
        if (VrSettings.MotionThrow == null || !VrSettings.MotionThrow.Value || !canThrow || _gripping) return;
        if (VrControls.Held(GunHold.Hand, VrButton.Trigger) || VrControls.Held(GunHold.Hand, VrButton.Grip)) return;
        var v = pose.Velocity;
        var aim = GunHold.Aim(pose) * Vector3.forward;
        var facing = VrRig.HeadRotation * Vector3.forward;
        facing.y = 0f;
        if (v.magnitude < FlickSpeed || Vector3.Dot(v.normalized, aim) < 0.45f || Vector3.Dot(new Vector3(v.x, 0f, v.z).normalized, facing.normalized) < 0.3f) return;
        Throw(Vector3.Slerp(aim, v.normalized, 0.5f).normalized, 1f, Vector3.zero);
        VrControls.Rumble(GunHold.Hand, 0.6f, 0.07f);
        if (Dev.ActionLog.Enabled) Plugin.L.LogInfo($"ACTION flick throw at {v.magnitude:F1} m/s");
    }

    private static void Release(VrHands.Pose pose)
    {
        // the fastest moment of the last 0.12 s (the release itself is often already slowing)
        var best = Vector3.zero;
        for (var i = 0; i < History; i++)
            if (Time.unscaledTime - At[i] < 0.12f && Vel[i].sqrMagnitude > best.sqrMagnitude)
                best = Vel[i];
        var aim = GunHold.Aim(pose) * Vector3.forward;
        if (best.magnitude >= SwingSpeed)
        {
            var dir = Vector3.Slerp(aim, best.normalized, 0.75f).normalized;
            var power = Mathf.Clamp(best.magnitude / 3.2f, 0.85f, 1.6f);
            Throw(dir, power, WristSpin());
            if (Dev.ActionLog.Enabled) Plugin.L.LogInfo($"ACTION swing throw at {best.magnitude:F1} m/s, power {power:F2}");
        }
        else Throw(aim, 1f, Vector3.zero); // a tap: where it points
    }

    /// <summary>How fast the wrist was turning over the last few frames (radians a second, world).</summary>
    private static Vector3 WristSpin()
    {
        var a = (_head - 4 + History) % History;
        var dt = At[_head] - At[a];
        if (dt <= 0.001f) return Vector3.zero;
        (Rot[_head] * Quaternion.Inverse(Rot[a])).ToAngleAxis(out var deg, out var axis);
        if (deg > 180f) deg -= 360f;
        return axis * (deg * Mathf.Deg2Rad / dt);
    }

    private static void Throw(Vector3 dir, float power, Vector3 spin)
    {
        _throwDir = dir;
        _power = power;
        _spin = spin;
        _throwAt = Time.unscaledTime;
        _requestFrame = Time.frameCount;
        _cooldown = Time.unscaledTime + 0.5f;
    }

    /// <summary>Dev: a throw from the gun hand (the test runtime's hands don't move): along the aim, or as a swing of
    /// this speed (m/s) along the aim.</summary>
    public static void DevFlick(float speed = 0f)
    {
        VrControls.DevPresent();
        var pose = VrHands.Get(GunHold.Hand);
        var aim = GunHold.Aim(pose) * Vector3.forward;
        Throw(aim, speed > 0f ? Mathf.Clamp(speed / 3.2f, 0.85f, 1.6f) : 1f, Vector3.zero);
    }

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
        var recent = Time.unscaledTime - _throwAt < 0.8f;
        var speed = body.velocity.magnitude * (recent ? _power : 1f);
        var dir = recent ? _throwDir : GunHold.Aim(pose) * Vector3.forward;
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
        if (recent && _spin.magnitude > 6f) body.angularVelocity = Vector3.ClampMagnitude(_spin, 35f);
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
