using System.Linq;
using AngerFootVR.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.Input;

/// <summary>
/// What you can pick up or use (bottles, drinks, the lizard, doors, buttons, people to talk to) is chosen by your
/// interact hand (the one without the gun): reach it to the thing, or point it at the thing, and squeeze its grip. The
/// game chose by where you look (it was "locked to the head"); when your hand is on nothing, looking still works, so it
/// works for everyone.
/// </summary>
[HarmonyPatch]
internal static class HandInteract
{
    private const float Reach = 0.3f; // m round the hand
    private const float PointRange = 2.5f; // m along the pointing hand (and within the thing's own range of you)

    private static readonly AccessTools.FieldRef<Player, Computer> UsedComputer = AccessTools.FieldRefAccess<Player, Computer>("_usedComputer");
    private static readonly Collider[] Near = new Collider[24];

    private static XRNode InteractHand => VrInputProvider.GunHand == XRNode.RightHand ? XRNode.LeftHand : XRNode.RightHand;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Player), "FindFocusedInteractable")]
    private static bool ByHand(Player __instance, ref Interactable __result)
    {
        if (!XrManager.IsRunning || CinemaRig.Active || !VrControls.Present) return true;
        var pose = VrHands.GetRaw(InteractHand);
        if (!pose.Tracked) return true;
        if (__instance.HandRig == null || !__instance.HandRig.CanInteract || UsedComputer(__instance) != null) return true;
        var hand = pose.Position;

        // within reach of the hand: the nearest
        Interactable best = null;
        var bestD = float.MaxValue;
        var n = Physics.OverlapSphereNonAlloc(hand, Reach, Near, LayerMasks.ThingsThatCanBeInteractable, QueryTriggerInteraction.Collide);
        for (var i = 0; i < n; i++)
        {
            var it = Usable(Near[i]);
            if (it == null) continue;
            var d = (Near[i].ClosestPoint(hand) - hand).sqrMagnitude;
            if (d < bestD)
            {
                bestD = d;
                best = it;
            }
        }

        // else what the hand points at (in sight of the hand, and near enough to you, as the game's own rule)
        if (best == null)
        {
            var dir = pose.Aim * Vector3.forward;
            var hits = Physics.SphereCastAll(hand, 0.12f, dir, PointRange, LayerMasks.ThingsThatCanBeInteractable, QueryTriggerInteraction.Collide);
            foreach (var h in hits.OrderBy(h => h.distance))
            {
                var it = Usable(h.collider);
                if (it == null) continue;
                var feetTo = h.point - __instance.transform.position;
                feetTo.y = 0f;
                if (feetTo.sqrMagnitude > it.InteractionRange * it.InteractionRange) continue;
                var to = h.collider.bounds.center - hand;
                if (Physics.Raycast(new Ray(hand, to.normalized), out var block, to.magnitude, LayerMasks.ThingsThatBlockInteractionLOS) && block.collider != h.collider) continue;
                best = it;
                break;
            }
        }

        if (best == null) return true; // (where you look, as the game does)
        __result = best;
        return false;
    }

    private static Interactable Usable(Collider c)
    {
        var it = c != null ? c.GetComponentInParent<Interactable>() : null;
        return it != null && it.enabled && it.CanBeInteractedWith() ? it : null;
    }
}
