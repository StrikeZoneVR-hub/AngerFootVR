using System.Reflection;
using AngerFootVR.Core;
using Framework;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.Input;

/// <summary>
/// Throwing your gun, by PLAY MODE:
/// - STANDING (the throw from 1.2 build 4, the player's pick): hold the gun hand's grip, swing and let go. It leaves your
///   hand the way your hand was moving (steadied by where you look, and a little by where the gun points), as hard as
///   you threw it (from a lob to a fastball, 0.75 to 1.7 times the game's own throw), spinning the way your wrist turned;
///   let go without a swing and it goes where it points. The swing is read from the steadiest fast moment just before
///   you let go (the hand is often already slowing as it opens), not from one frame. Only letting go of the grip
///   throws: hands moving about never do. THROW HELP takes a throw that's nearly on an enemy (within 12 degrees, in
///   sight) onto them, lifting it for the drop.
/// - SEATED: press the gun hand's grip and your gun flies at the enemy you point it at (15 degrees).
/// It's always the game's own throw (its throw animation, its hands empty after it): the mod only says when, and where
/// the gun goes as it leaves your hand. (Build 5 let go of the gun itself, without the game's throw: the game's hands
/// stayed in the pistol's pose, and walking over guns no longer picked them up.)
/// Either way: with your other hand on the gun too (TWO-HAND GUNS), let go with the gun hand (seated: press its grip)
/// and it's handed over. With your hands empty, squeeze a grip on a gun within reach and it's in that hand (the game's
/// own pick-up, one it would let you walk over and take).
/// </summary>
[HarmonyPatch]
internal static class MotionThrow
{
    /// <summary>Dev: seated or standing for now (the player's settings file untouched).</summary>
    public static bool? DevSeated;

    public static bool Seated => DevSeated ?? (VrSettings.PlayMode != null && VrSettings.PlayMode.Value == VrSettings.PlayStyle.Seated);
    private static bool Help => VrSettings.ThrowHelp == null || VrSettings.ThrowHelp.Value;

    private const float SwingSpeed = 1.5f; // m/s at the release: a throw (slower: let go where it points)
    private const float Wait = 0.35f; // (a release the game can't throw yet waits this long for it)

    // the game's own, for its pick-up and the dev check
    private static readonly FieldInfo PlayerWeapon = AccessTools.Field(typeof(Player), "_weapon");
    private static readonly FieldInfo PlayerPickups = AccessTools.Field(typeof(Player), "_allowWeaponPickups");
    private static readonly FieldInfo RigCustom = AccessTools.Field(typeof(HandRig), "_isPlayingCustomAnimation");

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

    // the throw asked of the game (it throws on its next look at the throw button), and how it goes as it leaves
    private static int _requestFrame = -10;
    private static Vector3 _throwDir, _spin;
    private static float _throwAt = -10f, _cooldown, _power = 1f;
    private static bool _aimed;
    private static bool _gripping, _pending, _taking;
    private static float _pendingAt, _handOverAt = -1f;
    private static Vector3 _pendingDir, _pendingSpin, _deferDir, _deferSpin;
    private static float _deferPower;
    private static float _pendingPower;
    private static bool _pendingAimed;

    private static int _player;
    private static Animator _settling;
    private static float _settleStart, _settleUntil;
    private static readonly Collider[] Buffer = new Collider[32];
    private static readonly int EmptyIdle = Animator.StringToHash("Idle"); // (the hands' own, empty)

    // (dev: what happened)
    public static int Thrown, Grabbed, HandedOver;
    public static string Last = "none yet";

    /// <summary>The game asks whether throw is held: a release holds it for its frame and the next (the game may ask
    /// before this sees it, and more than once a frame).</summary>
    public static bool Held => Time.frameCount - _requestFrame <= 1;

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
            EndSettle();
            ToYourHand();
        }

        Record(XRNode.LeftHand);
        Record(XRNode.RightHand);
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
            Grab(player);
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
                else Ask(player, GunHold.Aim(pose) * Vector3.forward, 1f, Vector3.zero, true);
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
                Measure(hand, pose, out var dir, out var power, out var spin);
                if (other && VrControls.Held(Other(hand), VrButton.Grip))
                {
                    // (your other hand's holding it too: it's handed over if that hand keeps hold a moment; if it lets go
                    // as well, it's thrown then, the way the gun hand threw it)
                    _handOverAt = Time.unscaledTime + 0.25f;
                    _deferDir = dir;
                    _deferPower = power;
                    _deferSpin = spin;
                }
                else Ask(player, dir, power, spin, false);
            }

            if (_handOverAt > 0f)
            {
                if (!VrControls.Held(Other(hand), VrButton.Grip))
                {
                    _handOverAt = -1f;
                    Ask(player, _deferDir, _deferPower, _deferSpin, false);
                }
                else if (Time.unscaledTime >= _handOverAt)
                {
                    _handOverAt = -1f;
                    HandOver();
                }
            }
        }

        if (_pending)
        {
            if (CanThrow(player))
            {
                _pending = false;
                Throw(_pendingDir, _pendingPower, _pendingSpin, _pendingAimed);
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

    /// <summary>Standing: the gun hand let go of its grip. A swing throws it the way the hand went; no swing, where it points.</summary>
    private static void Measure(XRNode hand, VrHands.Pose pose, out Vector3 dir, out float power, out Vector3 spin)
    {
        var tr = Tracks[hand == XRNode.LeftHand ? 0 : 1];
        var best = Swing(tr);
        var aim = GunHold.Aim(pose) * Vector3.forward;
        if (best.magnitude >= SwingSpeed)
        {
            // the way the hand went, steadied by where you're looking (people look at what they throw at) and a little
            // by where the gun points; the faster the swing, the more it's the hand's own
            var gaze = VrRig.HeadRotation * Vector3.forward;
            var own = Mathf.Lerp(0.6f, 0.8f, Mathf.InverseLerp(SwingSpeed, 6f, best.magnitude));
            dir = (best.normalized * own + gaze * (0.85f - own) + aim * 0.15f).normalized;
            power = Mathf.Clamp(best.magnitude / 3.4f, 0.75f, 1.7f);
            spin = WristSpin(tr);
            if (Dev.ActionLog.Enabled) Plugin.L.LogInfo($"ACTION thrown at {best.magnitude:F1} m/s, power {power:F2}");
        }
        else
        {
            dir = aim;
            power = 1f;
            spin = Vector3.zero;
            if (Dev.ActionLog.Enabled) Plugin.L.LogInfo("ACTION let go: thrown where it points");
        }
    }

    /// <summary>The throw, now if the game can, else as soon as it can (a pickup, a reload), the way you threw it.</summary>
    private static void Ask(Player player, Vector3 dir, float power, Vector3 spin, bool aimed)
    {
        if (CanThrow(player))
        {
            Throw(dir, power, spin, aimed);
            return;
        }

        _pending = true;
        _pendingAt = Time.unscaledTime;
        _pendingDir = dir;
        _pendingPower = power;
        _pendingSpin = spin;
        _pendingAimed = aimed;
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

    /// <summary>The game throws on its next look at the throw button (its own throw animation, its own release).</summary>
    private static void Throw(Vector3 dir, float power, Vector3 spin, bool aimed)
    {
        _throwDir = dir;
        _power = power;
        _spin = spin;
        _aimed = aimed;
        _throwAt = Time.unscaledTime;
        _requestFrame = Time.frameCount;
        _cooldown = Time.unscaledTime + 0.5f;
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
        var aimed = recent && _aimed;
        var speed = body.velocity.magnitude * (recent ? _power : 1f);
        var dir = recent ? _throwDir : GunHold.Aim(pose) * Vector3.forward;
        var from = pose.Position + dir * 0.15f;

        // not through a wall: if something's between your head and your hand, it leaves from your head
        var head = VrRig.HeadPosition;
        if (Physics.Linecast(head, from, out _, LayerMasks.ThingsThrownOrDroppedObjectsDepenetrateFrom)) from = head + dir * 0.1f;
        thrownObject.transform.position = from;

        // nearly on an enemy (seated: within 15 degrees, always; standing: 12, with THROW HELP): onto them, lifted for
        // the drop; else the game's own homing: on what's in line
        Vector3? target = aimed ? Assist(from, dir, speed, 15f) : Help ? Assist(from, dir, speed, 12f) : null;
        if (target.HasValue) dir = target.Value;
        else if (Physics.Raycast(from, dir, out var hit, 100f, LayerMasks.ThingsPlayerProjectilesCanHit)) dir = (hit.point - from).normalized;
        body.velocity = dir * speed;
        if (recent && _spin.magnitude > 6f) body.angularVelocity = Vector3.ClampMagnitude(_spin, 35f);
        if (recent)
        {
            Thrown++;
            Last = $"{(aimed ? "seated throw" : "throw")} from the {(GunHold.Hand == XRNode.LeftHand ? "left" : "right")} hand at {speed:F0} m/s (power {_power:F2}){(target.HasValue ? " onto an enemy" : "")}";
        }

        if (Dev.ActionLog.Enabled) Plugin.L.LogInfo($"ACTION thrown from the hand at {from} towards {dir} ({speed:F0} m/s){(target.HasValue ? " onto an enemy" : "")}");
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
            // the drop on the way: aim a little over them
            var t = dist / Mathf.Max(5f, speed);
            best = (to + Vector3.up * (0.5f * 9.81f * t * t)).normalized;
            bestAngle = angle;
        }

        return best;
    }

    // ------------------------------------------------------------------ grabbing a gun within reach

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

    private static void Grab(Player player)
    {
        if (PlayerPickups != null && !(bool)PlayerPickups.GetValue(player)) return;
        if (!TryGrab(player, XRNode.LeftHand)) TryGrab(player, XRNode.RightHand);
    }

    private static bool TryGrab(Player player, XRNode hand)
    {
        var pose = VrHands.GetRaw(hand);
        if (!pose.Tracked || !VrControls.Down(hand, VrButton.Grip)) return false;
        var palm = Palm(hand, pose);

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
        return Take(player, best, hand);
    }

    /// <summary>The gun is in this hand now: the game's own pick-up (Player.TryPickupWeapon's, this gun and no other).</summary>
    private static bool Take(Player player, Weapon weapon, XRNode hand)
    {
        var rig = player.HandRig;
        if (rig == null || player.HasWeapon || weapon == null || weapon.IsHeld || PlayerWeapon == null) return false;
        if (RigCustom != null && (bool)RigCustom.GetValue(rig)) return false;
        if (!rig.CanPickupWeapon) return false;
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
        Grabbed++;
        Last = $"grabbed {weapon.name} in the {(hand == XRNode.LeftHand ? "left" : "right")} hand";
        Plugin.L.LogInfo("Gun " + Last);
        _gripping = !Seated && VrControls.Held(GunHold.Hand, VrButton.Grip); // (in hand and squeezed: let go mid-swing and it's thrown)
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
        VrControls.Rumble(GunHold.Hand, 0.45f, 0.05f);
    }

    /// <summary>A level's gun given to you at the start goes to your gun hand.</summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Player), nameof(Player.SpawnPlayerWeapon))]
    private static void Given(GameObject prefab)
    {
        if (!XrManager.IsRunning || prefab == null) return;
        if (prefab.GetComponent<DualPistolsWeapon>() != null) SwitchTo(XRNode.RightHand);
        else ToYourHand();
    }

    // ------------------------------------------------------------------ dev

    /// <summary>Dev: a throw from the gun hand (the test runtime's hands don't move): seated (where it points), or a
    /// standing release of a swing this fast (m/s, your head's frame: x right, y up, z on; 0: let go without a swing).</summary>
    public static string DevThrow(Vector3 headVel, bool seated)
    {
        if (!SingletonBehaviour<Player>.HasInstance) return "no player";
        VrControls.DevPresent();
        var player = SingletonBehaviour<Player>.Instance;
        if (!player.HasWeapon) return "no gun";
        var hand = GunHold.Hand;
        var pose = VrHands.GetRaw(hand);
        if (seated)
        {
            Ask(player, GunHold.Aim(pose) * Vector3.forward, 1f, Vector3.zero, true);
            return "seated throw asked";
        }

        // (as a swing read at the release: written into the gun hand's last moments)
        var tr = Tracks[hand == XRNode.LeftHand ? 0 : 1];
        var v = Quaternion.Euler(0f, VrRig.HeadRotation.eulerAngles.y, 0f) * headVel;
        for (var k = 0; k < 3; k++)
        {
            var i = (tr.Head - k + History) % History;
            tr.Vel[i] = v;
            tr.At[i] = Time.unscaledTime;
        }

        Measure(hand, pose, out var dir, out var power, out var spin);
        Ask(player, dir, power, spin, false);
        return $"standing release at {headVel.magnitude:F1} m/s asked (power {power:F2})";
    }

    /// <summary>Dev ("pickupcheck [try]"): what the game's own walk-over pick-up asks (Player.TryPickupWeapon), now, for
    /// the nearest gun lying in the level; "try" asks the game to pick it up (without its line-of-sight test).</summary>
    public static string PickupCheck(bool attempt)
    {
        if (!SingletonBehaviour<Player>.HasInstance) return "PICKUP no player";
        var player = SingletonBehaviour<Player>.Instance;
        var rig = player.HandRig;
        var anim = rig != null ? rig.Animator : null;
        var state = anim != null ? anim.GetCurrentAnimatorStateInfo(0) : default;
        var clips = anim != null ? anim.GetCurrentAnimatorClipInfo(0) : new AnimatorClipInfo[0];
        Weapon near = null;
        var best = float.MaxValue;
        foreach (var w in Object.FindObjectsOfType<Weapon>())
        {
            if (w == null || w.IsHeld) continue;
            var d = (w.transform.position - player.transform.position).sqrMagnitude;
            if (d < best)
            {
                best = d;
                near = w;
            }
        }

        var s = $"PICKUP holding {(player.Weapon != null ? player.Weapon.name : "nothing")}, pickups allowed {(PlayerPickups != null ? PlayerPickups.GetValue(player) : "?")}, hands can pick up {(rig != null && rig.CanPickupWeapon)}" +
                $" (custom anim {(rig != null && RigCustom != null ? RigCustom.GetValue(rig) : "?")}, state empty-idle {state.shortNameHash == EmptyIdle}, clip {(clips.Length > 0 && clips[0].clip != null ? clips[0].clip.name : "-")}, in transition {(anim != null && anim.IsInTransition(0))}, WeaponType {(anim != null ? anim.GetInteger("WeaponType") : 0)})" +
                $" | nearest gun {(near != null ? $"{near.name} {Mathf.Sqrt(best):F1} m, can be picked up {near.CanBePickedUpRightNow}" : "none")}";
        if (attempt && near != null) s += $" | the game's pick-up: {player.TryPickupWeapon(near, considerLOS: false)}";
        return s;
    }

    public static string Describe() =>
        $"THROWS mode {(Seated ? "SEATED" : "STANDING")} help {Help} gun hand {GunHold.Hand} holding {GunHold.Holding} supported {Arsenal.Supported} two hands {Arsenal.TwoHands} gripping {_gripping} pending {_pending} settling {_settling != null} | thrown {Thrown} grabbed {Grabbed} handed over {HandedOver} | last: {Last}";
}
