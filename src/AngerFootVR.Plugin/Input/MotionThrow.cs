using AngerFootVR.Core;
using Framework;
using HarmonyLib;
using UnityEngine;

namespace AngerFootVR.Input;

/// <summary>
/// Throwing your gun: hold the gun hand's grip, swing and let go. It leaves your hand the way your hand was moving
/// (steadied by where you look, and a little by where the gun points), as hard as you threw it (from a lob to a
/// fastball, 0.75 to 1.7 times the game's own throw), spinning the way your wrist turned; let go without a swing and it
/// goes where it points. The swing is read from the steadiest fast moment just before you let go (the hand is often
/// already slowing as it opens), not from one frame. Let go while the game can't throw yet (a pickup, a reload) and the
/// throw waits a moment for it. Only letting go of the grip throws: hands moving about never do. A gentle aim assist
/// takes a throw that's nearly on an enemy (within 12 degrees, in sight) onto them, lifting it for the drop.
/// </summary>
[HarmonyPatch]
internal static class MotionThrow
{
    private const float SwingSpeed = 1.5f; // m/s at the release: a throw (slower: let go where it points)
    private const float Wait = 0.35f; // (a release the game can't throw yet waits this long for it)
    private static int _requestFrame = -10;
    private static Vector3 _throwDir, _spin;
    private static float _throwAt = -10f, _cooldown, _power = 1f;
    private static bool _gripping, _pending;
    private static float _pendingAt;
    private static Vector3 _pendingDir, _pendingSpin;
    private static float _pendingPower;

    // the gun hand's last moments: speed and turn (for the release)
    private const int History = 12;
    private static readonly Vector3[] Vel = new Vector3[History];
    private static readonly float[] At = new float[History];
    private static readonly Quaternion[] Rot = new Quaternion[History];
    private static int _head;

    /// <summary>The game asks whether throw is held: a release holds it for its frame and the next (the game may ask
    /// before this sees it, and more than once a frame).</summary>
    public static bool Held => Time.frameCount - _requestFrame <= 1;

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

        if (VrControls.Down(GunHold.Hand, VrButton.Grip)) _gripping = true;
        if (_gripping && VrControls.Up(GunHold.Hand, VrButton.Grip))
        {
            _gripping = false;
            Release(pose); // (read now, thrown now or as soon as the game can)
        }

        if (_pending)
        {
            if (canThrow)
            {
                _pending = false;
                Throw(_pendingDir, _pendingPower, _pendingSpin);
            }
            else if (Time.unscaledTime - _pendingAt > Wait) _pending = false;
        }
    }

    private static void Release(VrHands.Pose pose)
    {
        var best = Swing();
        var aim = GunHold.Aim(pose) * Vector3.forward;
        Vector3 dir, spin;
        float power;
        if (best.magnitude >= SwingSpeed)
        {
            // the way the hand went, steadied by where you're looking (people look at what they throw at) and a little
            // by where the gun points; the faster the swing, the more it's the hand's own
            var gaze = VrRig.HeadRotation * Vector3.forward;
            var own = Mathf.Lerp(0.6f, 0.8f, Mathf.InverseLerp(SwingSpeed, 6f, best.magnitude));
            dir = (best.normalized * own + gaze * (0.85f - own) + aim * 0.15f).normalized;
            power = Mathf.Clamp(best.magnitude / 3.4f, 0.75f, 1.7f);
            spin = WristSpin();
            if (Dev.ActionLog.Enabled) Plugin.L.LogInfo($"ACTION thrown at {best.magnitude:F1} m/s, power {power:F2}");
        }
        else
        {
            dir = aim;
            power = 1f;
            spin = Vector3.zero;
            if (Dev.ActionLog.Enabled) Plugin.L.LogInfo("ACTION let go: thrown where it points");
        }

        var player = SingletonBehaviour<Player>.Instance;
        if (player.HasWeapon && player.HandRig != null && player.HandRig.CanThrow && Time.unscaledTime >= _cooldown)
        {
            Throw(dir, power, spin);
            return;
        }

        // (the game can't throw just now: it's thrown as soon as it can, the way you threw it)
        _pending = true;
        _pendingAt = Time.unscaledTime;
        _pendingDir = dir;
        _pendingPower = power;
        _pendingSpin = spin;
    }

    /// <summary>The swing at the release: over the last 0.15 s, the three frames in a row that went fastest, averaged
    /// (one frame's speed jumps about; the release itself is often already slowing).</summary>
    private static Vector3 Swing()
    {
        var best = Vector3.zero;
        var bestSpeed = 0f;
        for (var k = 0; k < History; k++)
        {
            var i = (_head - k + History) % History;
            if (Time.unscaledTime - At[i] > 0.15f) break;
            var sum = Vector3.zero;
            var n = 0;
            for (var j = 0; j < 3; j++)
            {
                var x = (i - j + History) % History;
                if (Time.unscaledTime - At[x] > 0.2f) break;
                sum += Vel[x];
                n++;
            }

            if (n == 0) continue;
            var v = sum / n;
            if (v.magnitude > bestSpeed)
            {
                bestSpeed = v.magnitude;
                best = v;
            }
        }

        return best;
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

    /// <summary>Dev: a throw from the gun hand (the test runtime's hands don't move): along the aim, or as a throw of
    /// this speed (m/s) along the aim.</summary>
    public static void DevThrow(float speed = 0f)
    {
        VrControls.DevPresent();
        var pose = VrHands.GetRaw(GunHold.Hand);
        var aim = GunHold.Aim(pose) * Vector3.forward;
        Throw(aim, speed > 0f ? Mathf.Clamp(speed / 3.4f, 0.75f, 1.7f) : 1f, Vector3.zero);
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

        // aim assist: nearly on an enemy (12 degrees, in sight, 30 m): onto them, lifted for the drop; else the game's
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
        var bestAngle = 12f;
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
