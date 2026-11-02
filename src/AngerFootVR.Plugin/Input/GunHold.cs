using AngerFootVR.Core;
using Framework;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.Input;

/// <summary>
/// Guns in your hand (an early look at the motion controls). The game animates its guns on its first-person rig (sway,
/// recoil, reloading), with the gun hanging off the rig's right wrist and the barrel along the rig's forward. In VR that
/// whole rig (its arms hidden: your body's arms are yours) is placed every frame, after the game's own sway, so its hand
/// is on your body's hand (wrist on wrist, fingers along fingers: the gun sits in your grip as the game's own hand holds
/// it), turned about the wrist so the barrel runs along your aim: the game's own gun, animations and all, in your hand.
/// Shots leave its muzzle along your aim; the crosshair in the middle of your view goes (you aim with your hand).
/// </summary>
[HarmonyPatch]
internal static class GunHold
{
    /// <summary>The hand the guns are in.</summary>
    public static XRNode Hand => VrInputProvider.GunHand;

    private const string WristName = "FPSHands_03_Rig:Wrist_R", MiddleName = "FPSHands_03_Rig:MiddleFinger1_R", ThumbName = "FPSHands_03_Rig:ThumbFinger1_R";
    private static Transform _wrist, _middle, _thumb;
    private static HandRig _rig;

    /// <summary>Whether a gun is in your hand this frame.</summary>
    public static bool Holding { get; private set; }

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
        if (!Aiming(out var pose) || !SingletonBehaviour<Player>.HasInstance) return;
        var player = SingletonBehaviour<Player>.Instance;
        if (!player.HasWeapon || player.HandRig != __instance) return;
        if (_rig != __instance || _wrist == null)
        {
            _rig = __instance;
            _wrist = _middle = _thumb = null;
            foreach (var t in __instance.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == WristName) _wrist = t;
                else if (t.name == MiddleName) _middle = t;
                else if (t.name == ThumbName) _thumb = t;
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

        // the game's own first-person hand, in the same kind of frame (it holds the gun as the game animates it)
        var wristInRig = rig.InverseTransformPoint(_wrist.position);
        Quaternion rigRot;
        if (_middle != null && _thumb != null)
        {
            var along = _middle.position - _wrist.position;
            var thumb = Vector3.ProjectOnPlane(_thumb.position - _wrist.position, along.normalized);
            var back = s > 0 ? Vector3.Cross(thumb, along) : Vector3.Cross(along, thumb);
            var fpsFrame = Quaternion.Inverse(rig.rotation) * Quaternion.LookRotation(along, back);
            // its hand onto yours, then turned about the wrist just enough that the barrel runs along your aim
            rigRot = handRot * Quaternion.Inverse(fpsFrame);
            rigRot = Quaternion.FromToRotation(rigRot * Vector3.forward, aim * Vector3.forward) * rigRot;
        }
        else rigRot = aim;

        rig.SetPositionAndRotation(handPos - rigRot * Vector3.Scale(wristInRig, rig.lossyScale), rigRot);
        Holding = true;
    }

    public static string Describe()
    {
        var pose = VrHands.Get(Hand);
        var b = new System.Text.StringBuilder($"GUN holding={Holding} hand={pose.Tracked} {pose.Position} rig={(_rig != null ? _rig.transform.position + " " + _rig.transform.eulerAngles : "none")} wrist={(_wrist != null ? _wrist.position.ToString() : "none")}");
        if (_rig != null)
            foreach (var r in _rig.GetComponentsInChildren<Renderer>(true))
                if (r.gameObject.activeInHierarchy)
                    b.Append($" | {r.name}{(r.enabled ? "" : "(disabled)")}{(r.forceRenderingOff ? "(hidden)" : "")} {r.bounds.center}");
        var wc = SingletonBehaviour<PlayerCamera>.HasInstance ? SingletonBehaviour<PlayerCamera>.Instance.WeaponCamera : null;
        if (wc != null) b.Append($" | weaponcam {wc.transform.position} fwd {wc.transform.forward} near {wc.nearClipPlane} mask {wc.cullingMask:X} enabled {wc.enabled}");
        return b.ToString();
    }

    /// <summary>Shots leave the gun in your hand along your aim (the game fires from the middle of the view).</summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ProjectileWeapon), "CalculateProjectileSpawn")]
    private static bool FromTheHand(ref Vector3 position, ref Vector3 direction)
    {
        if (!Aiming(out var pose)) return true;
        var aim = Aim(pose);
        direction = aim * Vector3.forward;
        position = pose.Position + direction * 0.22f + aim * Vector3.up * 0.06f; // about where the muzzle is
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
