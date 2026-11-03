using Framework;
using UnityEngine;

namespace AngerFootVR.Wardrobe;

/// <summary>
/// What the wardrobe has opened up, from the player's own progress (read from the game's save, never written): each
/// level beaten opens one of each kind (a pair of gloves and a body style), a level with star goals done two of each,
/// a level with all its star goals three. Levels beaten before count as well. Worked out afresh each time (no record
/// of its own to keep in step); only the count last shown is kept (BepInEx config), for the NEW! card.
/// </summary>
internal static class Unlocks
{
    private static float _nextCount;
    private static int _count = -1, _levels, _beaten, _stars;

    /// <summary>How many of each kind are open (besides each kind's first, always open).</summary>
    public static int Count
    {
        get
        {
            if (Time.unscaledTime >= _nextCount || _count < 0) Recount();
            return _count;
        }
    }

    public static void Recount()
    {
        _nextCount = Time.unscaledTime + 3f;
        var save = GameState.CurrentSave;
        var levels = ScriptableObjectSingleton<GameConfig>.Instance != null ? ScriptableObjectSingleton<GameConfig>.Instance.GameplayLevels : null;
        if (save == null || levels == null)
        {
            if (_count < 0) _count = 0;
            return;
        }

        var n = 0;
        _levels = levels.Length;
        _beaten = _stars = 0;
        foreach (var l in levels)
        {
            if (l == null) continue;
            var ls = save.GetLevelSave(l);
            if (ls == null || !ls.HasCompletedLevel) continue;
            _beaten++;
            var stars = ls.CompletedStarGoals != null ? ls.CompletedStarGoals.Count : 0;
            var all = l.StarGoals != null ? l.StarGoals.Length : 0;
            _stars += stars;
            n += 1 + (stars > 0 ? 1 : 0) + (all > 0 && stars >= all ? 1 : 0);
        }

        _count = n;
    }

    public static bool Open(int indexInKind) => indexInKind == 0 || indexInKind <= Count;

    public static string Describe() => $"UNLOCKS {Count} of each kind ({_beaten}/{_levels} levels beaten, {_stars} stars)";
}
