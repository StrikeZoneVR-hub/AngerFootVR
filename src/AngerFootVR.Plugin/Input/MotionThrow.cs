using System.Reflection;
using AngerFootVR.Core;
using Framework;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.Input;

/// <summary>
/// Throwing, tossing and catching your gun (1.2), by PLAY MODE:
/// - STANDING: your gun is really in your hand. Hold its grip and let go mid-swing, and it leaves your hand right then,
///   from where it was in your hand, turned as it was, the way your hand was going (running, your speed too) and
///   spinning the way your wrist turned. A gentle toss goes exactly as gently as you tossed it (up and over to your other
///   hand); a real throw flies as hard as the game's own throw and harder (a good overarm throw is the game's; THROW HELP
///   takes a hard throw that's nearly on an enemy onto them; a toss is never helped). Let go with your hand still and it
///   stays in your hand.
/// - SEATED: press the gun hand's grip and your gun flies at the enemy you point it at.
/// Either way: squeeze a grip as a gun comes to that hand (or have it squeezed as it gets there) and you've caught it:
/// it's in that hand now, its trigger shoots. A gun lying within reach is grabbed the same way (the game's own pick-up,
/// one it would let you walk over and take). With your other hand on the gun too (TWO-HAND GUNS), let go with the gun hand
/// (seated: press its grip) and it's handed over. A toss you catch was never a throw (the level's "no throwing" star
/// still stands).
/// </summary>
[HarmonyPatch]
internal static class MotionThrow
{
    /// <summary>Dev: seated or standing for now (the player's settings file untouched).</summary>
    public static bool? DevSeated;

    public static bool Seated => DevSeated ?? (VrSettings.PlayMode != null && VrSettings.PlayMode.Value == VrSettings.PlayStyle.Seated);
    private static bool Help => VrSettings.ThrowHelp == null || VrSettings.ThrowHelp.Value;

    private const float KeepBelow = 0.9f; // m/s at the gun: let go slower than this (standing) and it stays in your hand
    private const float TossTop = 3f; // m/s: up to this, it goes exactly as your hand sent it; faster, a throw
    private const float Wait = 0.35f; // (a release the game can't throw yet waits this long for it)

    // the game's own, for its throw without its throw animation (the gun leaves your hand as you let go) and its pick-up
    private static readonly FieldInfo PlayerWeapon = AccessTools.Field(typeof(Player), "_weapon");
    private static readonly FieldInfo PlayerAttack = AccessTools.Field(typeof(Player), "_lastAttackTime");
    private static readonly FieldInfo PlayerPickups = AccessTools.Field(typeof(Player), "_allowWeaponPickups");
    private static readonly FieldInfo RigObject = AccessTools.Field(typeof(HandRig), "_currentHandRigObject");
    private static readonly FieldInfo RigHidden = AccessTools.Field(typeof(HandRig), "_weaponIsHidden");
    private static readonly FieldInfo RigThrowTime = AccessTools.Field(typeof(HandRig), "_lastThrowTime");
    private static readonly FieldInfo RigCustom = AccessTools.Field(typeof(HandRig), "_isPlayingCustomAnimation");
    private static readonly FieldInfo ThrowColliderField = AccessTools.Field(typeof(Weapon), "_throwCollider");
    private static readonly FieldInfo WeaponMuzzle = AccessTools.Field(typeof(ProjectileWeapon), "_muzzlePosition");

    // each hand's last moments: speed and turn
    private const int History = 12;

    private sealed class Track
    {
        public readonly Vector3[] Vel = new Vector3[History];
        public readonly float[] At = new float[History];
        public readonly Quaternion[] Rot = new Quaternion[History];
        public int Head;
    }

    private static readonly Track[] Tracks = { new Track(), new Track() };

    private struct Throw
    {
        public XRNode Hand;
        public bool Aimed; // seated: where it points, at the enemy
        public Vector3 AimDir, HandVel, Spin, PlayerVel;
    }

    private static Throw _now;
    private static bool _releasing, _taking, _gripping, _pending, _relHas;
    private static float _pendingAt, _handOverAt = -1f, _cooldown;
    private static Throw _pendingThrow;
    private static Vector3 _relCentre;
    private static Quaternion _relRot;

    // your gun in the air
    private static Weapon _tossed;
    private static Collider[] _tossedColliders = new Collider[0];
    private static float _tossAt, _tossPrevThrow;
    private static bool _tossHit;

    /// <summary>Dev: frames left to log the gun in the air.</summary>
    public static int DevTrace;

    /// <summary>Dev: a hand's grip squeezed (and held) at this time, to catch with.</summary>
    public static float DevSqueezeAt = -1f;
    public static XRNode DevSqueezeHand;
    private static XRNode _tossFrom;

    private static int _player;
    private static Animator _settling;
    private static float _settleStart, _settleUntil;
    private static readonly Collider[] Buffer = new Collider[32];
    private static readonly int EmptyIdle = Animator.StringToHash("Idle"); // (the hands' own, empty)

    // (dev: what happened)
    public static int Thrown, Tossed, Kept, Caught, Grabbed, HandedOver;
    public static string Last = "none yet";

    /// <summary>The game asks whether throw is held: never (the mod throws, when you let go).</summary>
    public static bool Held => false;

    public static void Update()
    {
        var inLevel = XrManager.IsRunning && !CinemaRig.Active && SingletonBehaviour<Player>.HasInstance;
        if (!inLevel)
        {
            _gripping = _pending = false;
            _handOverAt = -1f;
            EndSettle();
            return;
        }

        var player = SingletonBehaviour<Player>.Instance;
        if (player.GetInstanceID() != _player)
        {
            // a new level (or the level again): your gun hand is yours again
            _player = player.GetInstanceID();
            _tossed = null;
            EndSettle();
            ToYourHand();
        }

        Record(XRNode.LeftHand);
        Record(XRNode.RightHand);
        if (DevSqueezeAt > 0f && Time.unscaledTime >= DevSqueezeAt)
        {
            DevSqueezeAt = -1f;
            VrControls.DevHold(DevSqueezeHand, VrButton.Grip, true);
            Plugin.L.LogInfo($"TOSS dev squeeze {DevSqueezeHand} at {Time.unscaledTime - _tossAt:F2} s");
        }
        Settle();
        if (player.Health == null || !player.Health.IsAlive)
        {
            _gripping = _pending = false;
            _handOverAt = -1f;
            return;
        }

        if (!player.HasWeapon)
        {
            _gripping = _pending = false;
            _handOverAt = -1f;
            Catch(player);
            return;
        }

        if (!GunHold.Holding) return;
        var hand = GunHold.Hand;
        var pose = VrHands.GetRaw(hand);
        var other = (Arsenal.Supported || Arsenal.TwoHands) && !Arsenal.Dual && !GripAdjust.Placing; // (your other hand's on it too)

        if (Seated)
        {
            _gripping = false;
            if (VrControls.Down(hand, VrButton.Grip))
            {
                if (other) HandOver();
                else Ask(player, new Throw { Hand = hand, Aimed = true, AimDir = GunHold.Aim(pose) * Vector3.forward });
            }
        }
        else
        {
            if (VrControls.Down(hand, VrButton.Grip))
            {
                _gripping = true;
                _handOverAt = -1f;
            }

            if (_gripping && VrControls.Up(hand, VrButton.Grip))
            {
                _gripping = false;
                if (other) _handOverAt = Time.unscaledTime + 0.25f; // (handed over if the other hand keeps hold: letting go of both is just letting go)
                else
                {
                    var t = Measure(hand, pose);
                    if (t.HasValue) Ask(player, t.Value);
                    else
                    {
                        Kept++;
                        if (Dev.ActionLog.Enabled) Plugin.L.LogInfo("ACTION let go with the hand still: kept");
                    }
                }
            }

            if (_handOverAt > 0f && Time.unscaledTime >= _handOverAt)
            {
                _handOverAt = -1f;
                if ((Arsenal.Supported || Arsenal.TwoHands) && VrControls.Held(Other(hand), VrButton.Grip)) HandOver();
            }
        }

        if (_pending)
        {
            if (CanThrow(player))
            {
                _pending = false;
                Release(player, _pendingThrow);
            }
            else if (Time.unscaledTime - _pendingAt > Wait) _pending = false;
        }
    }

    private static XRNode Other(XRNode h) => h == XRNode.LeftHand ? XRNode.RightHand : XRNode.LeftHand;

    private static bool CanThrow(Player player) => player.HasWeapon && player.HandRig != null && player.HandRig.CanThrow && Time.unscaledTime >= _cooldown;

    private static void Record(XRNode hand)
    {
        var tr = Tracks[hand == XRNode.LeftHand ? 0 : 1];
        var pose = VrHands.GetRaw(hand);
        tr.Head = (tr.Head + 1) % History;
        tr.Vel[tr.Head] = pose.Tracked ? pose.Velocity : Vector3.zero;
        tr.At[tr.Head] = Time.unscaledTime;
        tr.Rot[tr.Head] = pose.Rotation;
    }

    /// <summary>A throw as your hand let go of it (standing), or none (your hand was still: it stays in your hand).</summary>
    private static Throw? Measure(XRNode hand, VrHands.Pose pose)
    {
        var tr = Tracks[hand == XRNode.LeftHand ? 0 : 1];
        var v = Swing(tr);
        var spin = WristSpin(tr);
        // (the gun's middle is out past your grip: a flick of the wrist sends it faster than the hand)
        if (GunHold.HeldPose(out var centre, out _)) v += Vector3.Cross(spin, centre - pose.Position);
        if (v.magnitude < KeepBelow) return null;
        return new Throw { Hand = hand, HandVel = v, Spin = spin };
    }

    private static void Ask(Player player, Throw t)
    {
        if (CanThrow(player))
        {
            Release(player, t);
            return;
        }

        // (the game can't throw just now: it's thrown as soon as it can, the way you threw it)
        _pending = true;
        _pendingAt = Time.unscaledTime;
        _pendingThrow = t;
    }

    /// <summary>The swing at the release: over the last 0.15 s, the three frames in a row that went fastest, averaged
    /// (one frame's speed jumps about; the release itself is often already slowing).</summary>
    private static Vector3 Swing(Track tr)
    {
        var best = Vector3.zero;
        var bestSpeed = 0f;
        for (var k = 0; k < History; k++)
        {
            var i = (tr.Head - k + History) % History;
            if (Time.unscaledTime - tr.At[i] > 0.15f) break;
            var sum = Vector3.zero;
            var n = 0;
            for (var j = 0; j < 3; j++)
            {
                var x = (i - j + History) % History;
                if (Time.unscaledTime - tr.At[x] > 0.2f) break;
                sum += tr.Vel[x];
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
    private static Vector3 WristSpin(Track tr)
    {
        var a = (tr.Head - 4 + History) % History;
        var dt = tr.At[tr.Head] - tr.At[a];
        if (dt <= 0.001f || dt > 0.25f) return Vector3.zero;
        (tr.Rot[tr.Head] * Quaternion.Inverse(tr.Rot[a])).ToAngleAxis(out var deg, out var axis);
        if (deg > 180f) deg -= 360f;
        if (float.IsNaN(axis.x)) return Vector3.zero;
        return axis * (deg * Mathf.Deg2Rad / dt);
    }

    private static Vector3 PlayerVelocity(Player player)
    {
        var m = player.Movement;
        if (m == null) return Vector3.zero;
        var v = m.MeasuredVelocity;
        if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || v.sqrMagnitude > 900f) v = m.Velocity;
        return float.IsNaN(v.x) ? Vector3.zero : v;
    }

    /// <summary>The gun leaves your hand now: the game's own throw (as Player.ThrowWeapon and its hand rig do it), without
    /// waiting for its throw animation's moment of release.</summary>
    private static void Release(Player player, Throw t)
    {
        var weapon = player.Weapon;
        var rig = player.HandRig;
        if (weapon == null || rig == null || PlayerWeapon == null) return;
        _relHas = GunHold.HeldPose(out _relCentre, out _relRot);
        t.PlayerVel = PlayerVelocity(player);
        _now = t;
        _tossPrevThrow = rig.LastThrowTime;

        GameState.LogEvent("Throw Weapon: " + weapon.name, weapon);
        SingletonBehaviour<GameplayManager>.Instance.OnWeaponThrown();
        SingletonBehaviour<TutorialManager>.Instance.DismissPrompt(TutorialPrompt.Throwing);
        PlayerAttack?.SetValue(player, Time.time);
        PlayerWeapon.SetValue(player, null);
        if (RigObject?.GetValue(rig) is HandRigObject obj && obj != null) obj.gameObject.SetActive(false);
        RigHidden?.SetValue(rig, true);
        RigThrowTime?.SetValue(rig, Time.time);
        if (rig.Animator != null) rig.Animator.SetInteger("WeaponType", -1);
        _releasing = true;
        try
        {
            weapon.OnThrown();
        }
        finally
        {
            _releasing = false;
        }

        _cooldown = Time.unscaledTime + 0.2f;
        _tossed = weapon;
        _tossedColliders = weapon.GetComponentsInChildren<Collider>(false);
        _tossAt = Time.unscaledTime;
        _tossHit = false;
        _tossFrom = t.Hand;
        VrControls.Rumble(t.Hand, 0.35f, 0.04f);
    }

    /// <summary>A gentle toss has no blow in it: the game's thrown gun knocks down whoever it passes close by.</summary>
    private static void NoBlow(GameObject thrown)
    {
        var w = thrown.GetComponent<Weapon>();
        if (w != null && ThrowColliderField?.GetValue(w) is Behaviour c && c != null) c.enabled = false;
    }

    /// <summary>The thrown gun (or the bottle you drank) leaves your hand, the way you threw it.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(HoldableObjectType), nameof(HoldableObjectType.ThrowObject))]
    private static void FromTheHand(GameObject thrownObject)
    {
        if (!XrManager.IsRunning || CinemaRig.Active || thrownObject == null) return;
        var body = thrownObject.GetComponent<Rigidbody>();
        if (body == null) return;
        if (!_releasing)
        {
            Bottle(thrownObject, body);
            return;
        }

        var t = _now;
        var game = Mathf.Max(5f, body.velocity.magnitude); // (the game's own throw speed, its shoe powers' too)
        var head = VrRig.HeadPosition;

        // where it was in your hand, turned as it was (its barrel along the barrel, its top up)
        var tr = thrownObject.transform;
        var from = _relHas ? _relCentre : VrHands.GetRaw(t.Hand).Position;
        if (_relHas)
        {
            var centre = Centre(thrownObject, body);
            var localCentre = tr.InverseTransformPoint(centre);
            var rot = _relRot;
            if (thrownObject.GetComponent<Weapon>() is ProjectileWeapon pw && WeaponMuzzle?.GetValue(pw) is Transform m && m != null)
                rot = _relRot * Quaternion.Inverse(Quaternion.Inverse(tr.rotation) * Quaternion.LookRotation(m.forward, m.up));
            tr.rotation = rot;
            tr.position += from - tr.TransformPoint(localCentre);
            if (DevTrace > 0 && thrownObject.GetComponent<Weapon>() is ProjectileWeapon pw2 && WeaponMuzzle?.GetValue(pw2) is Transform m2 && m2 != null)
                Plugin.L.LogInfo($"TOSS pose: in hand fwd {_relRot * Vector3.forward:F2} up {_relRot * Vector3.up:F2} | thrown muzzle fwd {m2.forward:F2} up {m2.up:F2} (muzzle in the gun {Quaternion.Inverse(tr.rotation) * m2.forward:F2}) | root fwd {tr.forward:F2} centre off {(centre - tr.position).magnitude:F2}");
        }
        else tr.position = from;

        // not through a wall: if something's between your head and the gun, it leaves from your side of it
        if (Physics.Linecast(head, from, out var wall, LayerMasks.ThingsThrownOrDroppedObjectsDepenetrateFrom))
        {
            var back = wall.point + (head - wall.point).normalized * 0.12f;
            tr.position += back - from;
            from = back;
        }

        Vector3 vel;
        var helped = false;
        if (t.Aimed)
        {
            // seated: where it points, at the enemy you point it at (else the game's own homing: on what's in line)
            var dir = t.AimDir;
            var target = Assist(from, dir, game, 15f);
            if (target.HasValue)
            {
                dir = target.Value;
                helped = true;
            }
            else if (Physics.Raycast(from, dir, out var hit, 100f, LayerMasks.ThingsPlayerProjectilesCanHit)) dir = (hit.point - from).normalized;
            vel = dir * game;
            Thrown++;
        }
        else
        {
            // standing: as your hand sent it (a toss as it was, a throw up to the game's own and more), with your speed
            var s = t.HandVel.magnitude;
            var speed = Mathf.Min(s * Gain(s, game), game * 1.7f);
            var dir = t.HandVel / Mathf.Max(s, 1e-4f);
            if (Help && speed >= 12f)
            {
                var target = Assist(from, dir, speed, 9f);
                if (target.HasValue)
                {
                    dir = target.Value;
                    helped = true;
                }
            }

            vel = dir * speed + t.PlayerVel;
            body.angularVelocity = Vector3.ClampMagnitude(t.Spin, 30f);
            if (speed < 5f)
            {
                NoBlow(thrownObject);
                Tossed++;
            }
            else Thrown++;
        }

        body.velocity = vel;
        Last = $"{(t.Aimed ? "seated throw" : vel.magnitude < 5f ? "toss" : "throw")} from the {(t.Hand == XRNode.LeftHand ? "left" : "right")} hand at {vel.magnitude:F1} m/s{(helped ? " onto an enemy" : "")} (hand {t.HandVel.magnitude:F1} m/s, spin {t.Spin.magnitude:F0} rad/s, you {t.PlayerVel.magnitude:F1} m/s)";
        if (Dev.ActionLog.Enabled) Plugin.L.LogInfo("ACTION " + Last + $" from {from:F2} towards {vel.normalized:F2}");
    }

    /// <summary>How much faster than your hand it goes: a toss (up to 3 m/s) exactly as fast, a good overarm throw (6.5
    /// m/s at the hand) as fast as the game's own throw, smoothly between.</summary>
    private static float Gain(float s, float game)
    {
        var full = Mathf.Max(1f, game / 6.5f);
        var t = Mathf.Clamp01((s - TossTop) / (6f - TossTop));
        t = t * t * (3f - 2f * t);
        return Mathf.Lerp(1f, full, t);
    }

    private static Vector3 Centre(GameObject go, Rigidbody body)
    {
        var have = false;
        var b = new Bounds();
        foreach (var r in go.GetComponentsInChildren<Renderer>(false))
        {
            if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (!have) b = r.bounds;
            else b.Encapsulate(r.bounds);
            have = true;
            if (DevTrace > 0) Plugin.L.LogInfo($"TOSS part {r.name} ({r.GetType().Name}, layer {r.gameObject.layer}) centre {(r.bounds.center - go.transform.position):F2} size {r.bounds.size:F2}");
        }

        return have ? b.center : body.worldCenterOfMass;
    }

    /// <summary>The drink's bottle (the game throws it once you've drunk): from your hand, where it points.</summary>
    private static void Bottle(GameObject thrown, Rigidbody body)
    {
        var pose = VrHands.GetRaw(GunHold.Hand);
        if (!pose.Tracked) return;
        var speed = body.velocity.magnitude;
        var dir = GunHold.Aim(pose) * Vector3.forward;
        var from = pose.Position + dir * 0.15f;
        var head = VrRig.HeadPosition;
        if (Physics.Linecast(head, from, out _, LayerMasks.ThingsThrownOrDroppedObjectsDepenetrateFrom)) from = head + dir * 0.1f;
        thrown.transform.position = from;
        var target = Assist(from, dir, speed, 12f);
        if (target.HasValue) dir = target.Value;
        else if (Physics.Raycast(from, dir, out var hit, 100f, LayerMasks.ThingsPlayerProjectilesCanHit)) dir = (hit.point - from).normalized;
        body.velocity = dir * speed;
    }

    /// <summary>Nearly on an enemy (within this many degrees, in sight, 30 m): onto them, lifted for the drop.</summary>
    private static Vector3? Assist(Vector3 from, Vector3 dir, float speed, float within)
    {
        Vector3? best = null;
        var bestAngle = within;
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
            var t = dist / Mathf.Max(5f, speed);
            best = (to + Vector3.up * (0.5f * 9.81f * t * t)).normalized;
            bestAngle = angle;
        }

        return best;
    }

    // ------------------------------------------------------------------ catching and grabbing

    /// <summary>Where a hand's palm is (as the body draws it from its controller).</summary>
    private static Vector3 Palm(XRNode hand, VrHands.Pose pose)
    {
        var s = hand == XRNode.LeftHand ? -1f : 1f;
        var local = Quaternion.LookRotation(Vector3.down, new Vector3(s, 0, 0));
        return pose.Position + pose.Rotation * (new Vector3(0.032f * s, 0, 0) - local * new Vector3(0, 0, 0.05f)) + pose.Rotation * local * new Vector3(0, 0, 0.045f);
    }

    /// <summary>How far a gun is from a point (to its solid parts: their own shapes, or a hollow mesh's box).</summary>
    private static float Near(Collider[] colliders, Vector3 at, Weapon w)
    {
        var best = float.MaxValue;
        foreach (var c in colliders)
        {
            if (c == null || !c.enabled || c.isTrigger) continue;
            var exact = c is BoxCollider || c is SphereCollider || c is CapsuleCollider || c is MeshCollider mc && mc.convex;
            best = Mathf.Min(best, exact ? (c.ClosestPoint(at) - at).sqrMagnitude : c.bounds.SqrDistance(at));
        }

        if (best == float.MaxValue && w != null && w.Rigidbody != null) best = (w.Rigidbody.worldCenterOfMass - at).sqrMagnitude;
        return Mathf.Sqrt(best);
    }

    private static void Catch(Player player)
    {
        if (DevTrace > 0 && _tossed != null && _tossed.Rigidbody != null)
        {
            DevTrace--;
            if (DevTrace % 3 == 0)
            {
                var head = VrRig.HeadPosition;
                var yaw = Quaternion.Inverse(Quaternion.Euler(0f, VrRig.HeadRotation.eulerAngles.y, 0f));
                var pl = Palm(XRNode.LeftHand, VrHands.GetRaw(XRNode.LeftHand));
                var pr = Palm(XRNode.RightHand, VrHands.GetRaw(XRNode.RightHand));
                Plugin.L.LogInfo($"TOSS TRACE {Time.unscaledTime - _tossAt:F2} s at {yaw * (_tossed.Rigidbody.position - head):F2} {_tossed.Rigidbody.velocity.magnitude:F1} m/s | from the left palm {Near(_tossedColliders, pl, _tossed):F3} right {Near(_tossedColliders, pr, _tossed):F3} | left palm {yaw * (pl - head):F2} right {yaw * (pr - head):F2}");
            }
        }

        if (PlayerPickups != null && !(bool)PlayerPickups.GetValue(player)) return;
        if (!TryCatch(player, XRNode.LeftHand)) TryCatch(player, XRNode.RightHand);
    }

    private static bool TryCatch(Player player, XRNode hand)
    {
        var pose = VrHands.GetRaw(hand);
        if (!pose.Tracked) return false;
        var down = VrControls.Down(hand, VrButton.Grip);
        if (!down && !VrControls.Held(hand, VrButton.Grip)) return false;
        var palm = Palm(hand, pose);

        // your own gun in the air: caught squeezing as it comes, or with the hand squeezed as it gets there
        if (_tossed != null && !_tossed.IsHeld && Time.unscaledTime - _tossAt < 4f)
        {
            if (Near(_tossedColliders, palm, _tossed) < (down ? 0.15f : 0.08f)) return Take(player, _tossed, hand, true);
        }

        if (!down) return false;
        // a gun within reach: grabbed, as the game's own pick-up would take it (one that can be picked up, in sight)
        var n = Physics.OverlapSphereNonAlloc(palm, 0.6f, Buffer, 1 << 22, QueryTriggerInteraction.Collide);
        Weapon best = null;
        var bestD = 0.16f;
        for (var i = 0; i < n; i++)
        {
            var w = Buffer[i] != null ? Buffer[i].GetComponentInParent<Weapon>() : null;
            if (w == null || w.IsHeld || !w.CanBePickedUpRightNow) continue;
            var d = Near(w.GetComponentsInChildren<Collider>(false), palm, w);
            if (d < bestD)
            {
                bestD = d;
                best = w;
            }
        }

        if (best == null) return false;
        var eye = VrRig.HeadPosition;
        var to = palm - eye;
        if (Physics.Raycast(eye, to.normalized, out var hit, to.magnitude, LayerMasks.ThingsThatBlockPickupLOS, QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<Weapon>() != best) return false;
        return Take(player, best, hand, best == _tossed);
    }

    /// <summary>The gun is in this hand now: the game's own pick-up (Player.TryPickupWeapon's, this gun and no other).</summary>
    private static bool Take(Player player, Weapon weapon, XRNode hand, bool own)
    {
        var rig = player.HandRig;
        if (rig == null || player.HasWeapon || weapon == null || weapon.IsHeld || PlayerWeapon == null) return false;
        if (RigCustom != null && (bool)RigCustom.GetValue(rig)) return false;
        if (!own && !rig.CanPickupWeapon) return false;
        SwitchTo(weapon is DualPistolsWeapon ? XRNode.RightHand : hand); // (the pair: one pistol in each hand, the right's the first)
        _taking = true;
        try
        {
            PlayerWeapon.SetValue(player, weapon);
            weapon.Pickup(spawnedInHands: false);
            rig.PickUpWeapon(weapon);
        }
        finally
        {
            _taking = false;
        }

        GameState.LogEvent("Pickup Weapon: " + weapon.name, weapon);
        var caught = own && Time.unscaledTime - _tossAt < 3f;
        if (caught && !_tossHit)
        {
            // (a toss you caught was never a throw: the level's count of throws and its "no throwing" star as they were)
            var stats = SingletonBehaviour<GameplayManager>.Instance.CurrentLevelStats.GameplayStats;
            if (stats.WeaponsThrown > 0) stats.WeaponsThrown--;
            RigThrowTime?.SetValue(rig, _tossPrevThrow);
        }

        if (caught) Caught++;
        else Grabbed++;
        Last = $"{(caught ? "caught" : "grabbed")} {weapon.name} in the {(hand == XRNode.LeftHand ? "left" : "right")} hand{(caught ? $" {Time.unscaledTime - _tossAt:F2} s after it left the {(_tossFrom == XRNode.LeftHand ? "left" : "right")}" : "")}";
        Plugin.L.LogInfo("Gun " + Last);
        _tossed = null;
        _gripping = !Seated && VrControls.Held(GunHold.Hand, VrButton.Grip); // (in hand and squeezed: let go mid-swing and it's thrown again)
        _pending = false;
        VrControls.Rumble(GunHold.Hand, 0.5f, 0.05f);

        // the game's hand comes up into its pose quick (it plays its pick-up), your palm in it all the while
        _settling = rig.Animator;
        if (_settling != null) _settling.speed = 10f;
        _settleStart = Time.unscaledTime;
        _settleUntil = Time.unscaledTime + 0.45f;
        GunHold.SettleUntil = _settleUntil;
        return true;
    }

    private static void Settle()
    {
        if (_settling == null)
        {
            _settling = null;
            return;
        }

        var now = Time.unscaledTime;
        var up = now - _settleStart > 0.06f && GunHold.InIdle(_settling) && _settling.GetCurrentAnimatorStateInfo(0).shortNameHash != EmptyIdle;
        if (up || now > _settleUntil) EndSettle();
    }

    private static void EndSettle()
    {
        if (_settling != null) _settling.speed = 1f;
        _settling = null;
        GunHold.SettleUntil = 0f;
    }

    /// <summary>Your other hand, holding the gun too, has it now.</summary>
    private static void HandOver()
    {
        var to = Other(GunHold.Hand);
        SwitchTo(to);
        _gripping = !Seated && VrControls.Held(to, VrButton.Grip);
        VrControls.Rumble(to, 0.45f, 0.05f);
        HandedOver++;
        Last = $"handed over to the {(to == XRNode.LeftHand ? "left" : "right")} hand";
        Plugin.L.LogInfo("Gun " + Last);
    }

    /// <summary>The gun's in this hand from now on (its trigger shoots, its grip throws; the other's grip interacts).</summary>
    public static void SwitchTo(XRNode hand)
    {
        _handOverAt = -1f;
        if (VrInputProvider.GunHand == hand) return;
        VrInputProvider.GunHand = hand;
        VrHands.LetGo();
        Arsenal.HandChanged();
        GunDetails.HandChanged();
        _gripping = false;
    }

    /// <summary>Your gun hand as you set it (GUN HAND).</summary>
    private static void ToYourHand() =>
        SwitchTo(VrSettings.GunHand != null && VrSettings.GunHand.Value == VrSettings.Hand.Left ? XRNode.LeftHand : XRNode.RightHand);

    /// <summary>A gun you walk over (the game's own pick-up) goes to your gun hand.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Player), nameof(Player.TryPickupWeapon))]
    private static void WalkedOver(Player __instance, bool __result)
    {
        if (!__result || _taking || !XrManager.IsRunning) return;
        if (__instance.Weapon is DualPistolsWeapon) SwitchTo(XRNode.RightHand);
        else ToYourHand();
        if (__instance.Weapon == _tossed) _tossed = null;
        VrControls.Rumble(GunHold.Hand, 0.45f, 0.05f);
    }

    /// <summary>Your own toss isn't picked up by walking into it for a moment (it's for your hands to catch).</summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Player), nameof(Player.TryPickupWeapon))]
    private static bool NotYet(Weapon weapon) => _taking || weapon == null || weapon != _tossed || !XrManager.IsRunning || Time.unscaledTime - _tossAt > 0.9f;

    /// <summary>A level's gun given to you at the start goes to your gun hand.</summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Player), nameof(Player.SpawnPlayerWeapon))]
    private static void Given(GameObject prefab)
    {
        if (!XrManager.IsRunning || prefab == null) return;
        if (prefab.GetComponent<DualPistolsWeapon>() != null) SwitchTo(XRNode.RightHand);
        else ToYourHand();
    }

    /// <summary>Your toss hit someone: a real throw (it counts as one, even caught).</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Health), nameof(Health.OnDamageTaken))]
    private static void Landed(IDamageSource damageSource)
    {
        if (_tossed == null || damageSource is not Component c || c == null) return;
        if (c.GetComponentInParent<Weapon>() == _tossed) _tossHit = true;
    }

    // ------------------------------------------------------------------ dev

    /// <summary>Dev: a release from the gun hand as if it moved this fast (m/s, your head's frame: x right, y up, z on),
    /// or a seated throw.</summary>
    public static string DevToss(Vector3 headVel, bool seated)
    {
        if (!SingletonBehaviour<Player>.HasInstance) return "no player";
        VrControls.DevPresent();
        var player = SingletonBehaviour<Player>.Instance;
        if (!player.HasWeapon) return "no gun";
        var hand = GunHold.Hand;
        var pose = VrHands.GetRaw(hand);
        var yaw = Quaternion.Euler(0f, VrRig.HeadRotation.eulerAngles.y, 0f);
        var t = seated
            ? new Throw { Hand = hand, Aimed = true, AimDir = GunHold.Aim(pose) * Vector3.forward }
            : new Throw { Hand = hand, HandVel = yaw * headVel };
        if (!CanThrow(player)) return "the game can't throw just now";
        Release(player, t);
        return Last;
    }

    public static string Describe() =>
        $"THROWS mode {(Seated ? "SEATED" : "STANDING")} help {Help} gun hand {GunHold.Hand} holding {GunHold.Holding} supported {Arsenal.Supported} two hands {Arsenal.TwoHands} gripping {_gripping} pending {_pending} settling {_settling != null} hand anim speed {(SingletonBehaviour<Player>.HasInstance && SingletonBehaviour<Player>.Instance.HandRig != null && SingletonBehaviour<Player>.Instance.HandRig.Animator != null ? SingletonBehaviour<Player>.Instance.HandRig.Animator.speed : -1f):F1} | thrown {Thrown} tossed {Tossed} kept {Kept} caught {Caught} grabbed {Grabbed} handed over {HandedOver} | last: {Last}" +
        (_tossed != null ? $" | out of hand: {_tossed.name} hit someone {_tossHit} at {(_tossed.Rigidbody != null ? _tossed.Rigidbody.position.ToString("F2") + " " + _tossed.Rigidbody.velocity.magnitude.ToString("F1") + " m/s" : "?")} {Time.unscaledTime - _tossAt:F2} s" : "");
}
