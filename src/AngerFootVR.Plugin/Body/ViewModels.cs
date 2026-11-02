using AngerFootVR.Core;
using Framework;
using UnityEngine;

namespace AngerFootVR.Body;

/// <summary>
/// The game's first-person models (the arms that hang under its camera and the kicking legs with their shorts and
/// shoes) aren't drawn in VR: your body is your arms and legs. The guns the arms hold stay (they go into the body's
/// hand). They are hidden with forceRenderingOff, which the game never touches, so its own showing and hiding of shoes
/// and guns carries on underneath.
/// </summary>
internal static class ViewModels
{
    private static float _next;

    public static void Update()
    {
        if (!XrManager.IsRunning || !SingletonBehaviour<Player>.HasInstance || Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.5f;
        var player = SingletonBehaviour<Player>.Instance;
        var feet = player.GetComponentInChildren<FootRig>(true);
        if (feet != null)
            foreach (var r in feet.GetComponentsInChildren<Renderer>(true))
                r.forceRenderingOff = true;
        var hands = player.HandRig;
        if (hands != null)
            foreach (var r in hands.GetComponentsInChildren<Renderer>(true))
                if (r.GetComponentInParent<HandRigObject>() == null)
                    r.forceRenderingOff = true; // the arms, bands and sleeves; not what they hold
    }
}
