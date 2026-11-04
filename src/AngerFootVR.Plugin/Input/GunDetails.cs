using AngerFootVR.Core;
using Framework;
using HarmonyLib;
using UnityEngine;

namespace AngerFootVR.Input;

/// <summary>
/// The gun's small things, the way they'd be if it were really in your hand (1.2's immersion update):
/// - the muzzle flash is seen in VR, at the muzzle (the game drew it on its flat weapon camera's own layer only), with a
///   brief flash of light round you;
/// - the spent casings fly out of the gun's own ejection port (the game moved them from its flat weapon camera's view
///   into the world's, which in VR put them a metre off);
/// - SOLID GUNS: the gun stops at a wall instead of going into it (the hand holding it stays with it), so a shot never
///   starts inside the wall;
/// - GUN WEIGHT: the heavy guns (sniper, minigun, launchers, the flamethrower) swing a touch heavier in one hand; your
///   other hand on them steadies them.
/// </summary>
[HarmonyPatch]
internal static class GunDetails
{
    private static bool InLevel => XrManager.IsRunning && !CinemaRig.Active && SingletonBehaviour<Player>.HasInstance;

    // ------------------------------------------------------------------ the muzzle flash

    private static readonly System.Reflection.FieldInfo Flashes = AccessTools.Field(typeof(HandRigGunObject), "_muzzleFlashes");
    private static Light _light;
    private static float _lightUntil;
    public static int FlashesShown;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(HandRigGunObject), "CreateMuzzleFlash")]
    private static void FlashSeen(HandRigGunObject __instance, Transform muzzlePosition)
    {
        if (!InLevel || Flashes == null) return;
        try
        {
            if (Flashes.GetValue(__instance) is System.Collections.Generic.List<GameObject> list)
                foreach (var go in list)
                {
                    if (go == null || go.layer == UI.VrUi.Layer) continue;
                    foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = UI.VrUi.Layer;
                    FlashesShown++;
                    if (FlashesShown <= 2) Plugin.L.LogInfo($"Muzzle flash seen in VR: {go.name} at {go.transform.position:F2}");
                }

            if (muzzlePosition != null) Light(muzzlePosition.position);
        }
        catch (System.Exception e)
        {
            Plugin.L.LogWarning("Muzzle flash: " + e.Message);
        }
    }

    private static void Light(Vector3 at)
    {
        if (_light == null)
        {
            var go = new GameObject("AngerFootVR muzzle light");
            Object.DontDestroyOnLoad(go);
            _light = go.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.range = 4.5f;
            _light.color = new Color(1f, 0.72f, 0.38f);
            _light.shadows = LightShadows.None;
            _light.renderMode = LightRenderMode.ForcePixel;
        }

        _light.transform.position = at;
        _light.intensity = Random.Range(2.2f, 3.2f);
        _light.enabled = true;
        _lightUntil = Time.time + 0.045f;
    }

    /// <summary>Every frame: the flash of light goes out.</summary>
    public static void Update()
    {
        if (_light != null && _light.enabled && Time.time > _lightUntil) _light.enabled = false;
    }

    // ------------------------------------------------------------------ the casings

    [HarmonyPrefix]
    [HarmonyPatch(typeof(HandRigGunObject), "EjectShell")]
    private static bool FromThePort(HandRigGunObject __instance, ProjectileWeapon weapon, Transform ejectionPosition)
    {
        if (!InLevel || !GunHold.Holding) return true;
        if (ejectionPosition == null || weapon == null || weapon.ShellPrefab == null || !GameSettings.SpawnBulletCasings) return false;
        var shell = weapon.ShellPrefab.GetComponent<EjectedShell>();
        var delay = shell != null ? shell.EjectionDelay : 0f;
        var prefab = weapon.ShellPrefab;
        __instance.StartCoroutine(Eject(prefab, ejectionPosition, delay));
        return false;
    }

    private static System.Collections.IEnumerator Eject(GameObject prefab, Transform port, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        if (port == null || prefab == null) yield break;
        var shell = Object.Instantiate(prefab, port.position, port.rotation);
        _shells++;
        if (_shells <= 2) Plugin.L.LogInfo($"Casing from the port at {port.position:F2} (hand {VrHands.GetRaw(GunHold.Hand).Position:F2}) layer {shell.layer}");
    }

    private static int _shells;

    // ------------------------------------------------------------------ solid guns

    private static int WallMask => LayerMasks.ThingsBloodSplatterCanHit;
    private static float _pushed;

    /// <summary>From GunHold, just after the gun is placed: a gun whose barrel would go into a wall is held back out of
    /// it, along the barrel (the hand with it). Returns how far it moved.</summary>
    public static Vector3 StopAtWalls(Transform rig, Vector3 grip, Vector3 muzzle)
    {
        _pushed = 0f;
        if (VrSettings.SolidGuns != null && !VrSettings.SolidGuns.Value) return Vector3.zero;
        // (from your eye to the grip first: a hand through a wall takes the gun with it no further than the wall)
        var eye = VrRig.HeadPosition;
        var back = Vector3.zero;
        var toGrip = grip - eye;
        if (toGrip.magnitude > 0.05f && Physics.SphereCast(eye, 0.03f, toGrip.normalized, out var hg, toGrip.magnitude, WallMask, QueryTriggerInteraction.Ignore))
            back += toGrip.normalized * -(toGrip.magnitude - hg.distance + 0.02f);
        var g = grip + back;
        var barrel = muzzle + back - g;
        var len = barrel.magnitude;
        if (len > 0.04f && Physics.SphereCast(g, 0.022f, barrel / len, out var hit, len + 0.02f, WallMask, QueryTriggerInteraction.Ignore))
            back += barrel / len * -(len + 0.02f - hit.distance);
        if (back.sqrMagnitude < 1e-8f) return Vector3.zero;
        rig.position += back;
        _pushed = back.magnitude;
        return back;
    }

    // ------------------------------------------------------------------ weight

    private static Quaternion _heavy;
    private static bool _heavyHas;

    private static bool IsHeavy(HoldableObjectType t) =>
        t == HoldableObjectType.SniperRifle || t == HoldableObjectType.Minigun || t == HoldableObjectType.CatBossMinigun || t == HoldableObjectType.GrenadeLauncher ||
        t == HoldableObjectType.FlameThrower || t == HoldableObjectType.Crossbow;

    /// <summary>The gun's in your other hand now: its weight starts from there.</summary>
    public static void HandChanged() => _heavyHas = false;

    /// <summary>The aim of a heavy gun in one hand: it follows the hand a little behind (two hands: as the hand is).</summary>
    public static Quaternion Weight(Quaternion aim, HoldableObjectType type)
    {
        if (type == null || !IsHeavy(type) || Arsenal.TwoHands || (VrSettings.GunWeight != null && !VrSettings.GunWeight.Value))
        {
            _heavyHas = false;
            return aim;
        }

        if (!_heavyHas)
        {
            _heavy = aim;
            _heavyHas = true;
            return aim;
        }

        var dt = Mathf.Clamp(Time.unscaledDeltaTime, 1e-4f, 0.05f);
        var k = type == HoldableObjectType.Minigun || type == HoldableObjectType.CatBossMinigun ? 14f : 20f;
        _heavy = Quaternion.Slerp(_heavy, aim, 1f - Mathf.Exp(-k * dt));
        return _heavy;
    }

    public static string Describe() => $"GUN DETAILS flashes seen {FlashesShown} light {(_light != null && _light.enabled)} shells {_shells} pushed {_pushed:F3} m";
}
