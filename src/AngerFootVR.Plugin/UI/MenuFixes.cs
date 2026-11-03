using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Framework;
using HarmonyLib;
using UnityEngine;

namespace AngerFootVR.UI;

/// <summary>
/// Game menus that place things by the desktop window's pixels, which in VR (the menus drawn into the VR picture) put
/// them off the picture:
/// - the level's play card in the car (its star goals, best time and PLAY): it takes its raised and lowered places as
///   world positions as it starts, while its canvas is still the window's, and puts itself back there every frame;
/// - the level complete screen's star, which pops out 100 "pixels" before flying to the counter (100 metres on the VR
///   picture: it left the picture for half a second).
/// </summary>
[HarmonyPatch]
internal static class MenuFixes
{
    // ------------------------------------------------------------------ the play card
    private static readonly FieldInfo Panel = AccessTools.Field(typeof(WorldMapPlayLevelPanel), "_panel");
    private static readonly FieldInfo Raised = AccessTools.Field(typeof(WorldMapPlayLevelPanel), "_raisedPosition");
    private static readonly FieldInfo Lowered = AccessTools.Field(typeof(WorldMapPlayLevelPanel), "_loweredPosition");
    private static readonly FieldInfo Spring = AccessTools.Field(typeof(WorldMapPlayLevelPanel), "_panelPositionSpring");
    private static readonly FieldInfo Visible = AccessTools.Field(typeof(WorldMapPlayLevelPanel), "_isVisible");
    private static readonly Dictionary<WorldMapPlayLevelPanel, (Vector3 raised, Vector3 lowered)> Local = new Dictionary<WorldMapPlayLevelPanel, (Vector3, Vector3)>();

    /// <summary>Its two places, taken against its parent (they stay right whatever the canvas becomes).</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(WorldMapPlayLevelPanel), "Start")]
    private static void Placed(WorldMapPlayLevelPanel __instance)
    {
        if (Panel.GetValue(__instance) is not RectTransform panel || panel.parent == null) return;
        var parent = panel.parent;
        Local[__instance] = (parent.InverseTransformPoint((Vector3)Raised.GetValue(__instance)), parent.InverseTransformPoint((Vector3)Lowered.GetValue(__instance)));
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(WorldMapPlayLevelPanel), "Update")]
    private static void OnTheVrPicture(WorldMapPlayLevelPanel __instance)
    {
        if (!Local.TryGetValue(__instance, out var local) || Panel.GetValue(__instance) is not RectTransform panel || panel.parent == null) return;
        var canvas = panel.GetComponentInParent<Canvas>();
        if (canvas == null || canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceCamera) return;
        var parent = panel.parent;
        var raised = parent.TransformPoint(local.raised);
        var lowered = parent.TransformPoint(local.lowered);
        Raised.SetValue(__instance, raised);
        Lowered.SetValue(__instance, lowered);
        if (Spring.GetValue(__instance) is not Spring3 spring) return;
        var target = (bool)Visible.GetValue(__instance) ? raised : lowered;
        spring.TargetValue = target;
        // (still at a window-pixel place: thousands of canvas lengths away. Straight to where it should be)
        var size = Mathf.Max(0.001f, (raised - lowered).magnitude);
        if ((spring.CurrentValue - target).magnitude > size * 4f) spring.CurrentValue = target;
    }

    /// <summary>Dev: the play card raised for a level (as choosing it on the map does), and where it is.</summary>
    public static string DevPlayCard(string match)
    {
        if (!SingletonBehaviour<WorldMapPlayLevelPanel>.HasInstance) return "no play card here";
        var card = SingletonBehaviour<WorldMapPlayLevelPanel>.Instance;
        var level = System.Linq.Enumerable.FirstOrDefault(Resources.FindObjectsOfTypeAll<Level>(), l => l != null && l.name.IndexOf(match, System.StringComparison.OrdinalIgnoreCase) >= 0);
        if (level == null) return "no level matches " + match;
        card.Show(level);
        var panel = Panel.GetValue(card) as RectTransform;
        return $"PLAYCARD {level.name}: visible {card.IsVisible}, panel at {(panel != null ? panel.position.ToString("F3") : "?")} raised {(Vector3)Raised.GetValue(card):F3} lowered {(Vector3)Lowered.GetValue(card):F3} (fixed {Local.ContainsKey(card)})";
    }

    // ------------------------------------------------------------------ the level complete star

    /// <summary>The star's pop-out, in its canvas's own units (100 pixels of the window, as the game means it).</summary>
    public static float PopOut()
    {
        var screen = SingletonBehaviour<LevelCompleteScreen>.HasInstance ? SingletonBehaviour<LevelCompleteScreen>.Instance : null;
        var canvas = screen != null ? screen.GetComponentInParent<Canvas>() : null;
        if (canvas == null || canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceCamera) return -100f;
        return -100f * canvas.rootCanvas.transform.lossyScale.x;
    }

    [HarmonyPatch]
    private static class StarPopOut
    {
        private static MethodBase TargetMethod() => AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(LevelCompleteScreen), "FillStarCounterRoutine"));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var popOut = AccessTools.Method(typeof(MenuFixes), nameof(PopOut));
            var n = 0;
            foreach (var c in code)
            {
                if (c.opcode == OpCodes.Ldc_R4 && c.operand is float f && Mathf.Approximately(f, -100f))
                {
                    n++;
                    c.opcode = OpCodes.Call; // (in place: its labels stay with it)
                    c.operand = popOut;
                }

                yield return c;
            }

            if (n == 0) Plugin.L.LogWarning("Level complete star: its pop-out wasn't found (it may leave the picture for a moment)");
        }
    }
}
