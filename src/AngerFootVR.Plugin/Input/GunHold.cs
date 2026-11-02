using AngerFootVR.Core;
using Framework;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.Input;

/// <summary>
/// Guns in your hand (an early look at the motion controls). The game animates its guns on its first-person rig (sway,
/// recoil, reloading), with the gun hanging off the rig's right wrist and the barrel along the rig's forward. In VR that
/// whole rig (its arms hidden: your body's arms are yours) is placed every frame, after the game's own sway, so its
/// wrist is at your controller and the barrel along your aim: the game's own gun, animations and all, in your hand.
/// Shots leave its muzzle along your aim; the crosshair in the middle of your view goes (you aim with your hand).
/// </summary>
[HarmonyPatch]
internal static class GunHold
{
    /// <summary>The hand the guns are in.</summary>
    public static XRNode Hand => VrInputProvider.GunHand;

    private const string WristName = "FPSHands_03_Rig:Wrist_R";
    private static Transform _wrist;
    private static HandRig _rig;

    /// <summary>Whether a gun is in your hand this frame.</summary>
    public static bool Holding { get; private set; }

    private static bool Aiming(out VrHands.Pose pose)
    {
        pose = VrHands.Get(Hand);
        return XrManager.IsRunning && !CinemaRig.Active && pose.Tracked;
    }

    /// <summary>Where the barrel points: the controller's aim (tilted up from its grip).</summary>
    public static Quaternion Aim(VrHands.Pose pose) => pose.Rotation * Quaternion.Euler(VrHands.AimPitch, 0f, 0f);

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
            _wrist = null;
            foreach (var t in __instance.GetComponentsInChildren<Transform>(true))
                if (t.name == WristName)
                {
                    _wrist = t;
                    break;
                }

            if (_wrist == null) return;
        }

        // the rig turned to your aim, moved so its wrist sits just behind your grip
        var rig = __instance.transform;
        var aim = Aim(pose);
        var wristInRig = rig.InverseTransformPoint(_wrist.position);
        var grip = pose.Position - aim * new Vector3(0f, 0.02f, 0.06f);
        rig.SetPositionAndRotation(grip - aim * Vector3.Scale(wristInRig, rig.lossyScale), aim);
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
