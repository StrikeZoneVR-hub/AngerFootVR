using System.Linq;
using AngerFootVR.Core;
using Framework;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.Input;

/// <summary>
/// What you hold, in your hand. The game animates its guns, bats, boxes on its first-person rig (sway, recoil,
/// reloading), hanging off the rig's right hand. In VR that whole rig (its arms hidden: your body's arms are yours) is
/// placed every frame, after the game's own sway, so its hand is exactly on your body's hand (palm in palm, fingers along
/// fingers): the thing sits in your grip just as the game's own hand holds it. (A left hand holds it in the game's hand
/// mirrored.) A gun fires from its muzzle along its barrel, so shots go where it visibly points; the crosshair in the
/// middle of your view goes (you aim with your hand). Your fingers take the thing's own grip: round a gun's handle with
/// the index on its trigger, a fist round a bat, open under a box.
/// </summary>
[HarmonyPatch]
internal static class GunHold
{
    /// <summary>The hand the guns are in.</summary>
    public static XRNode Hand => VrInputProvider.GunHand;

    private const string WristName = "FPSHands_03_Rig:Wrist_R", MiddleName = "FPSHands_03_Rig:MiddleFinger1_R", ThumbName = "FPSHands_03_Rig:ThumbFinger1_R";
    private static Transform _wrist, _middle, _thumb, _wristL, _middleL, _plungerRoot, _plungerEnd;
    private static HandRig _rig;

    /// <summary>Whether a gun (or anything the game holds on its rig) is in your hand this frame.</summary>
    public static bool Holding { get; private set; }

    /// <summary>The held thing's grip for your fingers: x the fist, y the index (below 0: on the gun's trigger), z the thumb.</summary>
    public static Vector3 HeldGrip { get; private set; } = new Vector3(1f, -1f, 0.8f);

    /// <summary>Dev: 1 = the old way (the rig turned about the wrist onto the aim pose; shots along the aim).</summary>
    public static int DevMode;

    // where the palm sits along your hand from the wrist (HeroSkin puts the body's palm there)
    private const float PalmAlong = 0.045f;
    private static Transform _muzzle;
    private static HoldableObjectType _held;
    private static readonly System.Reflection.FieldInfo MuzzleField = AccessTools.Field(typeof(HandRigGunObject), "_muzzlePosition");
    private static float _barrelOff, _heldSince;
    private static Vector3 _palmRest, _muzzleRest, _axisRest;
    private static string _axisObj = "";

    private static Vector3 GripFor(HoldableObjectType t)
    {
        if (t == HoldableObjectType.ShoeBox || t == HoldableObjectType.PizzaBox || t == HoldableObjectType.SnakeBucket)
            return new Vector3(0.35f, 0.3f, 0.3f); // hands open under it
        if (t == HoldableObjectType.Shield) return new Vector3(0.85f, 0.85f, 0.6f);
        if (t == HoldableObjectType.Bat || t == HoldableObjectType.Machete || t == HoldableObjectType.PizzaCutter ||
            t == HoldableObjectType.Whip || t == HoldableObjectType.GimpClaw)
            return new Vector3(1f, 1f, 0.9f); // a fist round the handle
        if (t == HoldableObjectType.BeerBottle || t == HoldableObjectType.EnergyDrink || t == HoldableObjectType.Grenade)
            return new Vector3(0.8f, 0.75f, 0.7f);
        return new Vector3(1f, -1f, 0.8f); // a gun: round its grip, the index on the trigger
    }

    private static bool IsGun => HeldGrip.y < 0f;

    private static bool Aiming(out VrHands.Pose pose)
    {
        pose = VrHands.Get(Hand);
        return XrManager.IsRunning && !CinemaRig.Active && pose.Tracked;
    }

    /// <summary>Where the barrel points: the controller's aim pose.</summary>
    public static Quaternion Aim(VrHands.Pose pose) => pose.Aim;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(HandRig), "LateUpdate")]
    private static void PlaceRig(HandRig __instance)
    {
        Holding = false;
        if (!Aiming(out var pose) || !SingletonBehaviour<Player>.HasInstance)
        {
            Arsenal.Nothing();
            return;
        }

        var player = SingletonBehaviour<Player>.Instance;
        if (player.HandRig != __instance) return;
        if (!player.HasWeapon)
        {
            Arsenal.Nothing();
            return;
        }
        if (_rig != __instance || _wrist == null)
        {
            _rig = __instance;
            _wrist = _middle = _thumb = _wristL = _middleL = _plungerRoot = _plungerEnd = null;
            foreach (var t in __instance.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == WristName) _wrist = t;
                else if (t.name == MiddleName) _middle = t;
                else if (t.name == ThumbName) _thumb = t;
                else if (t.name == "FPSHands_03_Rig:Wrist_L") _wristL = t;
                else if (t.name == "FPSHands_03_Rig:MiddleFinger1_L") _middleL = t;
                else if (t.name == "FPSHands_03_Rig:Plunger_Root") _plungerRoot = t;
                else if (t.name == "FPSHands_03_Rig:Plunger") _plungerEnd = t;
            }

            if (_wrist == null) return;
        }

        var rig = __instance.transform;
        var aim = Aim(pose);
        // your hand (the body's): InkVR's grip-to-hand frame, z along the fingers, y out of the back of the hand
        var s = Hand == XRNode.LeftHand ? -1f : 1f;
        var local = Quaternion.LookRotation(Vector3.down, new Vector3(s, 0, 0));
        var handRot = pose.Rotation * local;
        var handPos = pose.Position + pose.Rotation * (new Vector3(0.032f * s, 0, 0) - local * new Vector3(0, 0, 0.05f));

        // what's held: its grip for your fingers, its muzzle
        var type = player.Weapon != null ? player.Weapon.HoldableObjectType : null;
        if (type != _held || _muzzle == null || !_muzzle.gameObject.activeInHierarchy)
        {
            if (type != _held)
            {
                _heldSince = Time.unscaledTime;
                _palmRest = Vector3.zero;
            }

            _held = type;
            HeldGrip = GripFor(type);
            _muzzle = null;
            if (MuzzleField != null)
                foreach (var g in __instance.GetComponentsInChildren<HandRigGunObject>(false))
                    if (MuzzleField.GetValue(g) is Transform m && m != null)
                    {
                        _muzzle = m;
                        break;
                    }
        }

        // a left hand: the game's (right) first-person hand, mirrored
        var ls = rig.localScale;
        var wantX = Mathf.Abs(ls.x) * (DevMode == 1 ? 1f : s);
        if (!Mathf.Approximately(ls.x, wantX)) rig.localScale = new Vector3(wantX, ls.y, ls.z);

        // the game's own first-person hand, in the same kind of frame (it holds the thing as the game animates it)
        Quaternion rigRot;
        Vector3 anchorInRig, anchor;
        if (_middle != null && _thumb != null)
        {
            var along = _middle.position - _wrist.position;
            var thumb = Vector3.ProjectOnPlane(_thumb.position - _wrist.position, along.normalized);
            // (a right hand's back from its thumb; a mirrored one's, the other way round)
            var mirrored = rig.lossyScale.x < 0f;
            var back = (DevMode == 1 ? s < 0 : mirrored) ? Vector3.Cross(along, thumb) : Vector3.Cross(thumb, along);
            var fpsFrame = Quaternion.Inverse(rig.rotation) * Quaternion.LookRotation(along, back);
            rigRot = handRot * Quaternion.Inverse(fpsFrame);
            // a gun: its barrel on your aim, rolled with your controller (matched hand to hand, the game's pistol
            // grip points it at the floor); held things (bats, boxes, bottles) sit in your hand as the game's hand holds them
            if (IsGun && DevMode != 1) rigRot = _axisRest != Vector3.zero ? aim * Quaternion.FromToRotation(_axisRest, Vector3.forward) : aim;
            if (DevMode == 1)
            {
                // (the old way: turned about the wrist onto the aim)
                rigRot = Quaternion.FromToRotation(rigRot * Vector3.forward, aim * Vector3.forward) * rigRot;
                anchorInRig = rig.InverseTransformPoint(_wrist.position);
                anchor = handPos;
            }
            else
            {
                // its palm in yours (the hands differ in size: the palms meet, not the wrists); where its palm rests in
                // the rig is taken once the thing is in hand, so the game's own recoil and reloads still move it
                var palm = _wrist.position + along * 0.5f - back.normalized * (along.magnitude * 0.12f);
                anchorInRig = rig.InverseTransformPoint(palm);
                if (Time.unscaledTime - _heldSince < 0.6f || _palmRest == Vector3.zero)
                {
                    _palmRest = anchorInRig;
                    _muzzleRest = _muzzle != null ? rig.InverseTransformPoint(_muzzle.position) : Vector3.zero;
                    // no muzzle (the plunger): its length, from your palm to its middle, is what points
                    _axisRest = Vector3.zero;
                    if (_muzzle == null && type == HoldableObjectType.Plunger && _plungerRoot != null && _plungerEnd != null)
                    {
                        // the plunger's own bones: the one nearer a hand is the handle in your palm
                        float Near(Vector3 p) => Mathf.Min((p - _wrist.position).sqrMagnitude, _wristL != null ? (p - _wristL.position).sqrMagnitude : float.MaxValue);
                        var rootIsHandle = Near(_plungerRoot.position) <= Near(_plungerEnd.position);
                        var handle = rootIsHandle ? _plungerRoot.position : _plungerEnd.position;
                        var cup = rootIsHandle ? _plungerEnd.position : _plungerRoot.position;
                        anchorInRig = rig.InverseTransformPoint(handle);
                        _palmRest = anchorInRig;
                        // (the cup's on the far side of the root from the other bone: the mesh grows from the root
                        // away from it)
                        var a = anchorInRig - rig.InverseTransformPoint(cup);
                        if (a.sqrMagnitude > 1e-6f) _axisRest = a.normalized;
                        _axisObj = rootIsHandle ? "plunger bones (root in hand)" : "plunger bones (end in hand)";
                        // the handle: the mesh as it's posed now, its end furthest from the cup (a little in from it)
                        try
                        {
                            var skin = __instance.GetComponentsInChildren<SkinnedMeshRenderer>(false).FirstOrDefault(r => r.name.Contains("Plunger"));
                            if (skin != null && _axisRest != Vector3.zero)
                            {
                                var baked = new Mesh();
                                skin.BakeMesh(baked);
                                var verts = baked.vertices;
                                var axisW = rig.TransformDirection(_axisRest);
                                float lo = float.MaxValue, hi = float.MinValue;
                                Vector3 loAt = default;
                                foreach (var v in verts)
                                {
                                    var w = skin.transform.TransformPoint(v);
                                    var d = Vector3.Dot(w, axisW);
                                    if (d < lo)
                                    {
                                        lo = d;
                                        loAt = w;
                                    }

                                    if (d > hi) hi = d;
                                }

                                Object.Destroy(baked);
                                if (verts.Length > 0)
                                {
                                    anchorInRig = rig.InverseTransformPoint(loAt + axisW * ((hi - lo) * 0.08f));
                                    _palmRest = anchorInRig;
                                    _axisObj += $", handle from the mesh ({hi - lo:F2} m long)";
                                }
                            }
                        }
                        catch (System.Exception e)
                        {
                            _axisObj += ", mesh unreadable: " + e.Message;
                        }
                    }
                    else if (_muzzle == null)
                    {
                        var obj = __instance.GetComponentsInChildren<HandRigObject>(false).FirstOrDefault(o => o.isActiveAndEnabled && o.GetComponentInChildren<Renderer>(false) != null);
                        _axisObj = obj != null ? obj.name : "none";
                        var rs = obj != null ? obj.GetComponentsInChildren<Renderer>(false) : new Renderer[0];
                        if (rs.Length > 0)
                        {
                            var b = rs[0].bounds;
                            foreach (var r in rs) b.Encapsulate(r.bounds);
                            // (the game holds some things in its other hand: the plunger. Then that hand's palm is the grip)
                            if (_wristL != null && _middleL != null && (_wristL.position - b.center).sqrMagnitude < (_wrist.position - b.center).sqrMagnitude)
                            {
                                anchorInRig = rig.InverseTransformPoint(_wristL.position + (_middleL.position - _wristL.position) * 0.5f);
                                _palmRest = anchorInRig;
                            }

                            var a = rig.InverseTransformPoint(b.center) - anchorInRig;
                            if (a.sqrMagnitude > 1e-4f) _axisRest = a.normalized;
                        }
                    }
                }
                else anchorInRig = _palmRest;
                anchor = handPos + handRot * new Vector3(0f, 0f, PalmAlong);
            }
        }
        else
        {
            rigRot = aim;
            anchorInRig = rig.InverseTransformPoint(_wrist.position);
            anchor = handPos;
        }

        if (IsGun && DevMode != 1) rigRot = Arsenal.TwoHand(rig, rigRot, anchor, anchorInRig, _muzzleRest, type);
        rig.SetPositionAndRotation(anchor - rigRot * Vector3.Scale(anchorInRig, rig.lossyScale), rigRot);
        _barrelOff = Vector3.Angle(rig.forward, aim * Vector3.forward);
        Holding = true;
        Arsenal.AfterPlace(__instance, rig, anchorInRig, _muzzleRest, player.Weapon, type, IsGun);
    }

    /// <summary>Where the held gun points from (its muzzle) and the way (its barrel).</summary>
    public static void BarrelRay(VrHands.Pose pose, out Vector3 position, out Vector3 direction) => Barrel(pose, out position, out direction);

    /// <summary>Where a held gun's shots go: along its barrel (the old way: along the aim pose).</summary>
    private static void Barrel(VrHands.Pose pose, out Vector3 position, out Vector3 direction)
    {
        var aim = Aim(pose);
        if (DevMode == 1 || !Holding || !IsGun || _rig == null)
        {
            direction = aim * Vector3.forward;
            position = pose.Position + direction * 0.22f + aim * Vector3.up * 0.06f; // about where the muzzle is
            return;
        }

        direction = _axisRest != Vector3.zero ? _rig.transform.TransformDirection(_axisRest) : _rig.transform.forward;
        position = _muzzle != null ? _muzzle.position : pose.Position + direction * 0.3f;
    }

    public static string Describe()
    {
        var pose = VrHands.Get(Hand);
        var line = "";
        if (_rig != null && _muzzle != null)
        {
            var rigT = _rig.transform;
            var palmW = rigT.TransformPoint(_palmRest);
            var anim = AccessTools.Field(typeof(HandRig), "_animator")?.GetValue(_rig) as Animator;
            line = $" barrel line {Vector3.Angle(rigT.forward, _muzzle.position - palmW):F0} deg (rig {rigT.InverseTransformDirection(_muzzle.position - palmW).normalized}) muzzle fwd {Vector3.Angle(rigT.forward, _muzzle.forward):F0} deg idle={(anim != null && anim.GetCurrentAnimatorStateInfo(0).IsName("Idle"))} muzzleRest {_muzzleRest} palmRest {_palmRest}";
        }

        line += $" axis {_axisRest} from '{_axisObj}'";

        var b = new System.Text.StringBuilder(line + $" GUN holding={Holding} mode={DevMode} held={(_held != null ? _held.ToString() : "none")} grip={HeldGrip} barrel off aim {_barrelOff:F1} deg muzzle={(_muzzle != null ? _muzzle.position.ToString() : "none")} hand={pose.Tracked} {pose.Position} rig={(_rig != null ? _rig.transform.position + " " + _rig.transform.eulerAngles + " scale " + _rig.transform.localScale : "none")} wrist={(_wrist != null ? _wrist.position.ToString() : "none")}");
        if (_rig != null)
            foreach (var r in _rig.GetComponentsInChildren<Renderer>(true))
                if (r.gameObject.activeInHierarchy)
                    b.Append($" | {r.name}{(r.enabled ? "" : "(disabled)")}{(r.forceRenderingOff ? "(hidden)" : "")} {r.bounds.center}");
        var wc = SingletonBehaviour<PlayerCamera>.HasInstance ? SingletonBehaviour<PlayerCamera>.Instance.WeaponCamera : null;
        if (wc != null) b.Append($" | weaponcam {wc.transform.position} fwd {wc.transform.forward} near {wc.nearClipPlane} mask {wc.cullingMask:X} enabled {wc.enabled}");
        return b.ToString();
    }

    /// <summary>Shots leave the gun in your hand along its barrel (the game fires from the middle of the view).</summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ProjectileWeapon), "CalculateProjectileSpawn")]
    private static bool FromTheHand(ProjectileWeapon __instance, ref Vector3 position, ref Vector3 direction)
    {
        if (!Aiming(out var pose)) return true;
        Arsenal.ChooseDualHand(__instance);
        if (!Arsenal.DualShot(__instance, out position, out direction)) Barrel(pose, out position, out direction);
        if (Dev.ActionLog.Enabled) Plugin.L.LogInfo($"ACTION shot from the hand at {position} towards {direction}");
        return false;
    }

    /// <summary>No crosshair in the middle of your view while you aim with your hand.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Crosshair), "Update")]
    private static void NoCrosshair(Crosshair __instance)
    {
        if (!Aiming(out _)) return;
        foreach (Transform child in __instance.transform)
            if (child.gameObject.activeSelf)
                child.gameObject.SetActive(false);
    }
}
