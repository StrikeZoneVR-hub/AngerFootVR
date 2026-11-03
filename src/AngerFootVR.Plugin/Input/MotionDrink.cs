using System.Reflection;
using AngerFootVR.Core;
using Framework;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.Input;

/// <summary>
/// Drinks for real: grab a beer, an energy drink (or the lizard) and it's in your hand; bring it to your mouth and the
/// game's own drink goes off (its sound, its effect, the empty thrown away from your hand). Hold it as long as you
/// like (ten seconds and it drinks itself). OPTIONS: MOTION DRINK off = grabbing it drinks it, as in the game.
/// </summary>
[HarmonyPatch]
internal static class MotionDrink
{
    private static MonoBehaviour _held;
    private static GameObject _copy;
    private static XRNode _hand;
    private static float _since;
    private static bool _allow;
    private static MethodInfo _drink;

    /// <summary>The hand holding a drink this frame (the body's fingers close round it).</summary>
    public static bool HoldingIn(XRNode hand) => _copy != null && _hand == hand;

    private static bool Wanted =>
        XrManager.IsRunning && !CinemaRig.Active && SingletonBehaviour<Player>.HasInstance && (VrSettings.MotionDrink == null || VrSettings.MotionDrink.Value);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(BeerBottle), nameof(BeerBottle.OnInteraction))]
    private static bool Beer(BeerBottle __instance) => Grab(__instance, AccessTools.Method(typeof(BeerBottle), nameof(BeerBottle.OnInteraction)));

    [HarmonyPrefix]
    [HarmonyPatch(typeof(EnergyDrink), nameof(EnergyDrink.OnInteraction))]
    private static bool Can(EnergyDrink __instance) => Grab(__instance, AccessTools.Method(typeof(EnergyDrink), nameof(EnergyDrink.OnInteraction)));

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Lizard), nameof(Lizard.OnInteraction))]
    private static bool Snack(Lizard __instance) => Grab(__instance, AccessTools.Method(typeof(Lizard), nameof(Lizard.OnInteraction)));

    /// <summary>Into your hand instead of straight down (the game's own drink runs when it reaches your mouth).</summary>
    private static bool Grab(MonoBehaviour thing, MethodInfo drink)
    {
        if (_allow || !Wanted) return true;
        if (_copy != null) return false; // (one at a time)
        var hand = VrInputProvider.GunHand == XRNode.RightHand ? XRNode.LeftHand : XRNode.RightHand;
        if (!VrHands.Get(hand).Tracked) return true;

        // a look-alike in your hand (just its looks: the original waits, switched off, for the drink)
        var original = thing.gameObject;
        original.SetActive(false);
        var copy = Object.Instantiate(original);
        foreach (var c in copy.GetComponentsInChildren<Component>(true))
            if (c != null && !(c is Transform) && !(c is MeshFilter) && !(c is MeshRenderer) && !(c is SkinnedMeshRenderer))
                Object.DestroyImmediate(c);
        copy.name = "AngerFootVR Drink";
        copy.SetActive(true);
        _copy = copy;
        _held = thing;
        _drink = drink;
        _hand = hand;
        _since = Time.unscaledTime;
        VrControls.Rumble(hand, 0.3f, 0.04f);
        Plugin.L.LogInfo($"Drink in hand: {original.name}");
        return false;
    }

    /// <summary>Each frame: the drink in your hand; at your mouth (or after ten seconds) it's drunk.</summary>
    public static void Update()
    {
        if (_copy == null) return;
        if (_held == null || !Wanted)
        {
            Drop();
            return;
        }

        var pose = VrHands.GetRaw(_hand);
        if (!pose.Tracked) return;
        // in your fist, standing up out of it
        var s = _hand == XRNode.LeftHand ? -1f : 1f;
        var palm = pose.Position + pose.Rotation * new Vector3(0.025f * s, -0.01f, 0.02f);
        var up = pose.Rotation * Vector3.up; // (a fist round it: its length along the fist, up out of the thumb side)
        _copy.transform.rotation = Quaternion.FromToRotation(Vector3.up, up);
        var bounds = new Bounds();
        var any = false;
        foreach (var r in _copy.GetComponentsInChildren<Renderer>())
        {
            if (!any) bounds = r.bounds;
            else bounds.Encapsulate(r.bounds);
            any = true;
        }

        _copy.transform.position = palm - (any ? bounds.center - _copy.transform.position : Vector3.zero);

        var mouth = VrRig.HeadPosition + VrRig.HeadRotation * new Vector3(0f, -0.09f, 0.06f);
        var top = palm + up * (any ? bounds.extents.magnitude : 0.1f);
        if ((top - mouth).magnitude < 0.12f || (palm - mouth).magnitude < 0.14f || Time.unscaledTime - _since > 10f) Drink();
    }

    private static void Drink()
    {
        var thing = _held;
        var drink = _drink;
        Object.Destroy(_copy);
        _copy = null;
        _held = null;
        if (thing == null || drink == null) return;
        thing.gameObject.SetActive(true);
        Plugin.L.LogInfo($"Drunk: {thing.name}");
        _allow = true;
        try
        {
            drink.Invoke(thing, null);
        }
        finally
        {
            _allow = false;
        }

        VrControls.Rumble(_hand, 0.45f, 0.08f);
    }

    /// <summary>Gone (a level change, a death): back where it was.</summary>
    private static void Drop()
    {
        if (_copy != null) Object.Destroy(_copy);
        _copy = null;
        if (_held != null) _held.gameObject.SetActive(true);
        _held = null;
    }

    public static string Describe() => _copy != null ? $"DRINK holding {(_held != null ? _held.name : "?")} in {_hand} for {Time.unscaledTime - _since:F1} s" : "DRINK none";
}
