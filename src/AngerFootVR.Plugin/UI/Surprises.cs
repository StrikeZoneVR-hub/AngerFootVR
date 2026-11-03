using System.Collections.Generic;
using System.IO;
using System.Linq;
using AngerFootVR.Core;
using Framework;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.UI;

/// <summary>
/// The 1.0 surprises (slow motion was tried and taken out at the player's word):
/// - FLIP THEM OFF: enemies who see your middle finger (right stick click) come for you (the game's own alert, as a
///   gunshot nearby does);
/// - the HALL OF FAME on the movie house's back wall: your levels, stars and fastest time (read from your save), and
///   the rage room's best;
/// - the rage room keeps score: SMASHED this visit, BEST ever (kept in the mod's own folder, never your save).
/// </summary>
[HarmonyPatch]
internal static class Surprises
{
    private static bool InPlay => XrManager.IsRunning && !CinemaRig.Active && SingletonBehaviour<Player>.HasInstance;

    // ------------------------------------------------------------------ flip them off

    private static readonly Dictionary<Enemy, float> Flipped = new Dictionary<Enemy, float>();
    private static float _nextFlipCheck, _nextWhy;

    public static void Update()
    {
        if (!InPlay || VrSettings.FlipOff == null || !VrSettings.FlipOff.Value) return;
        if (Body.HeroBody.FlipAmount(1) < 0.85f || Time.unscaledTime < _nextFlipCheck) return;
        _nextFlipCheck = Time.unscaledTime + 0.2f;
        var head = VrRig.HeadPosition;
        var look = VrRig.HeadRotation * Vector3.forward;
        var hand = VrHands.Get(XRNode.RightHand);
        var any = false;
        var why = Plugin.DevRun ? new System.Text.StringBuilder() : null;
        foreach (var e in Object.FindObjectsOfType<Enemy>())
        {
            if (e == null || e.AI == null || e.Health == null || !e.Health.IsAlive) continue;
            var chest = e.transform.position + Vector3.up * 1.3f;
            var to = chest - head;
            if (to.magnitude > 30f || Vector3.Angle(look, to) > 35f)
            {
                why?.Append($" {e.name}: {to.magnitude:F0} m {Vector3.Angle(look, to):F0} deg;");
                continue;
            }

            if (Flipped.TryGetValue(e, out var at) && Time.unscaledTime - at < 4f) continue;
            // (in sight: nothing but the enemy itself, or other enemies, in the way)
            if (Physics.Linecast(head, chest, out var hit, LayerMasks.ThingsPlayerProjectilesCanHit) && hit.collider.GetComponentInParent<Enemy>() == null)
            {
                why?.Append($" {e.name}: blocked by {hit.collider.name};");
                continue;
            }
            Flipped[e] = Time.unscaledTime;
            e.AI.Alert(hand.Tracked ? hand.Position : head, AlertLevel.Alerted);
            any = true;
        }

        if (any)
        {
            VrControls.Rumble(XRNode.RightHand, 0.35f, 0.06f);
            Plugin.L.LogInfo("Flipped them off");
        }
        else if (why != null && Time.unscaledTime > _nextWhy)
        {
            _nextWhy = Time.unscaledTime + 2f;
            Plugin.L.LogInfo("Flip: nobody saw it:" + why);
        }
    }

    // ------------------------------------------------------------------ the hall of fame

    private static TextMeshPro _fame;
    private static float _nextFame;

    /// <summary>In the movie house: the board on the back wall, from your save (a few times a second at most).</summary>
    public static void HallOfFame()
    {
        if (!CinemaRig.Active || MovieHouse.Root == null || Time.unscaledTime < _nextFame) return;
        _nextFame = Time.unscaledTime + 2f;
        if (_fame == null)
        {
            MovieHouse.Sign("HALL OF FAME", new Vector3(-3.4f, 3.25f, -5.97f), 0.42f, new Color(1f, 0.25f, 0.6f));
            _fame = MovieHouse.Sign("", new Vector3(-3.4f, 2.2f, -5.97f), 0.2f, new Color(1f, 0.85f, 0.3f), glow: false);
            _fame.enableWordWrapping = false;
            _fame.alignment = TextAlignmentOptions.Center;
            _fame.rectTransform.sizeDelta = new Vector2(4.2f, 1.8f);
        }

        _fame.text = Records();
    }

    private static string Records()
    {
        var save = GameState.CurrentSave;
        var lines = new List<string>();
        if (save == null) lines.Add("PICK A SAVE TO SEE YOUR RECORDS");
        else
        {
            var levels = ScriptableObjectSingleton<GameConfig>.Instance.GameplayLevels ?? new Level[0];
            int beaten = 0, stars = 0, starsMax = 0;
            float fastest = float.MaxValue;
            string fastestName = null;
            foreach (var level in levels)
            {
                if (level == null) continue;
                starsMax += level.StarGoals != null ? level.StarGoals.Length : 0;
                var ls = save.GetLevelSave(level);
                if (ls == null || !ls.HasCompletedLevel) continue;
                beaten++;
                stars += ls.CompletedStarGoals?.Count ?? 0;
                if (ls.BestCompletionTime > 0f && ls.BestCompletionTime < fastest)
                {
                    fastest = ls.BestCompletionTime;
                    fastestName = level.name;
                }
            }

            lines.Add($"LEVELS BEATEN   {beaten} / {levels.Length}");
            lines.Add($"STARS   {stars} / {starsMax}");
            if (fastestName != null) lines.Add($"FASTEST   {fastest:F1} S");
        }

        lines.Add($"RAGE ROOM BEST   {RageBest} SMASHED");
        return string.Join("\n", lines);
    }

    // ------------------------------------------------------------------ the rage room's score

    private static int _smashed, _best = -1;
    private static TextMeshPro _score;
    private static string BestFile => Path.Combine(BepInEx.Paths.BepInExRootPath, "AngerFootVR", "rage-best.txt");

    public static int RageBest
    {
        get
        {
            if (_best < 0)
            {
                _best = 0;
                try
                {
                    if (File.Exists(BestFile)) int.TryParse(File.ReadAllText(BestFile).Trim(), out _best);
                }
                catch
                {
                    // (no best yet)
                }
            }

            return _best;
        }
    }

    /// <summary>A fresh visit: the count back to nothing.</summary>
    public static void RageVisit()
    {
        _smashed = 0;
        if (_score == null && MovieHouse.Root != null)
            _score = MovieHouse.Sign("", new Vector3(4f, 1.98f, MovieHouse.RageMin.z + 0.06f), 0.3f, new Color(0.3f, 0.95f, 1f));
        ShowScore();
    }

    /// <summary>Something smashed.</summary>
    public static void Smashed()
    {
        _smashed++;
        if (_smashed > RageBest)
        {
            _best = _smashed;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(BestFile));
                File.WriteAllText(BestFile, _best.ToString());
            }
            catch
            {
                // (the score's just for fun)
            }
        }

        ShowScore();
    }

    private static void ShowScore()
    {
        if (_score == null) return;
        _score.text = $"SMASHED {_smashed}    BEST {RageBest}";
    }
}
