using System.Collections.Generic;
using System.IO;
using AngerFootVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.UI;

/// <summary>
/// RAGE MODE: hit the big red button by the rage room's door (touch it, point at it and pull the trigger, or kick
/// it) and the room's put back fresh, then 60 seconds to wreck it. Everything's worth points (a TV more than a
/// bottle, the jukebox and the fish tank most); smash things in quick succession and the combo climbs (x2, x3 ... x8),
/// with RAGE!! popping up in the game's own letters. The neon scoreboard keeps your best and your last five runs (in
/// the mod's own folder, never your save). The RESET lever on the wall (grip it, pull it down) puts the room back any
/// time.
/// </summary>
internal static class RageMode
{
    private const float Length = 60f;
    private static TextMeshPro _board, _title;
    private static Transform _cap;
    private static float _endsAt = -1f, _lastSmash = -10f, _pressedAt = -10f;
    private static int _score, _combo, _best;
    private static readonly List<int> Last = new List<int>();
    private static string File => Path.Combine(BepInEx.Paths.BepInExRootPath, "AngerFootVR", "rage-scores.txt");
    private static Transform _lever;
    private static float _leverDown, _leverStartY;
    private static bool _hooked;

    public static bool Running => _endsAt > 0f;

    private static int Points(string kind) => kind switch
    {
        "bottle_green" or "bottle_brown" => 50,
        "plate" => 60,
        "vase" => 80,
        "box" => 30,
        "gnome" => 120,
        "crate" or "chair" => 100,
        "table" => 150,
        "tv" => 250,
        "vending_glass" => 300,
        "barrel" => 350,
        "jukebox" or "fishtank" => 400,
        "pinata" => 500,
        "neon" => 200,
        "dummy" => 150,
        _ => 40,
    };

    public static void Build(Transform root)
    {
        if (!_hooked)
        {
            _hooked = true;
            RageRoom.Smashed += Smashed;
            FunWorld.Tick += Tick;
        }

        Load();
        _endsAt = -1f;
        var lo = MovieHouse.RageMin;
        var hi = MovieHouse.RageMax;
        // the button on its pedestal, just in by the door on the right
        var at = new Vector3(5.75f, 0f, hi.z - 0.75f);
        FunWorld.Picture("rage_button", root, at, Quaternion.identity);
        var cap = FunWorld.Thing("rage_button_cap", at + new Vector3(0f, 0.96f, 0f), 0f, 1f, false, true);
        if (cap != null)
        {
            _cap = cap.transform.Find("Visual");
            FunWorld.MakePress(cap, h => Start(), 0.14f, true).Where = () => cap.transform.position + Vector3.up * 0.04f;
        }

        FunWorld.Solid(at + new Vector3(0f, 0.47f, 0f), new Vector3(0.34f, 0.94f, 0.34f));
        _title = MovieHouse.SignFacing("RAGE MODE", new Vector3(6.9f, 3.25f, hi.z - 0.08f), 0.5f, new Color(1f, 0.2f, 0.25f), Vector3.back, FunWorld.Root);
        _board = MovieHouse.SignFacing("", new Vector3(6.9f, 2.45f, hi.z - 0.08f), 0.22f, new Color(1f, 0.85f, 0.25f), Vector3.back, FunWorld.Root);
        if (_board != null) _board.rectTransform.sizeDelta = new Vector2(3.6f, 1.6f);

        // the RESET lever on the wall left of the door
        var leverAt = new Vector3(1.7f, 1.25f, hi.z - 0.05f);
        FunWorld.Picture("lever_base", root, leverAt, Quaternion.Euler(0f, 180f, 0f));
        var handle = FunWorld.Picture("lever_handle", root, leverAt + new Vector3(0f, 0f, -0.1f), Quaternion.Euler(-35f, 180f, 0f));
        _lever = handle != null ? handle.transform : null;
        MovieHouse.SignFacing("RESET", leverAt + new Vector3(0f, 0.34f, -0.02f), 0.16f, new Color(0.15f, 0.95f, 1f), Vector3.back, FunWorld.Root);
        FunWorld.Handles.Add(new FunWorld.Handle
        {
            Where = () => _lever != null ? _lever.TransformPoint(new Vector3(0f, 0.32f, 0f)) : Vector3.zero,
            Reach = 0.16f,
            Begin = (h, p) =>
            {
                _leverStartY = p.y;
                _leverDown = 0f;
            },
            Drag = (h, p) =>
            {
                var down = Mathf.Clamp01((_leverStartY - p.y) / 0.25f);
                if (down >= 1f && _leverDown < 1f)
                {
                    GameSounds.PlayOr("Kick Door Lever", "thud", _lever.position, 1f);
                    VrControls.Rumble(h, 0.7f, 0.1f);
                    if (!Running) RageRoom.ResetAll();
                }

                _leverDown = down;
            },
            End = h => _leverDown = 0f,
        });
        Show();
    }

    private static void Start()
    {
        _pressedAt = Time.unscaledTime;
        RageRoom.ResetAll();
        _score = 0;
        _combo = 0;
        _endsAt = Time.unscaledTime + Length;
        GameSounds.PlayOr("World Map Start Level", "door_bang", CinemaRig.HeadPosition, 1f);
        Plugin.L.LogInfo("RAGE MODE: go");
    }

    private static void Smashed(string kind, Vector3 at)
    {
        if (!Running) return;
        var now = Time.unscaledTime;
        _combo = now - _lastSmash < 1.6f ? Mathf.Min(_combo + 1, 8) : 1;
        _lastSmash = now;
        var gained = Points(kind) * _combo;
        _score += gained;
        Pop(at, _combo >= 3 ? $"RAGE!! +{gained}" : $"+{gained}", _combo >= 3 ? new Color(1f, 0.2f, 0.3f) : new Color(1f, 0.85f, 0.25f));
    }

    /// <summary>The dummy took a good hit (it counts like a smash).</summary>
    public static void DummyHit(Vector3 at) => Smashed("dummy", at);

    private static void Tick()
    {
        // the lever springs back up; the button pops back up
        if (_lever != null) _lever.localRotation = Quaternion.Euler(-35f + 70f * _leverDown, 180f, 0f);
        if (_cap != null) _cap.localPosition = new Vector3(0f, Time.unscaledTime - _pressedAt < 0.25f ? -0.035f : 0f, 0f);
        if (Running && Time.unscaledTime >= _endsAt) Finish();
        if (Running || Time.frameCount % 15 == 0) Show();
        Pops();
    }

    private static void Finish()
    {
        _endsAt = -1f;
        _best = Mathf.Max(_best, _score);
        Last.Insert(0, _score);
        while (Last.Count > 5) Last.RemoveAt(Last.Count - 1);
        Save();
        GameSounds.PlayOr("Level Complete", "door_bang", CinemaRig.HeadPosition, 1f);
        Pop(CinemaRig.HeadPosition + CinemaRig.HeadRotation * Vector3.forward * 1.2f, $"SCORE {_score}", new Color(0.15f, 0.95f, 1f));
        Plugin.L.LogInfo($"RAGE MODE: {_score} (best {_best})");
    }

    private static void Show()
    {
        if (_board == null) return;
        if (Running)
        {
            var left = Mathf.CeilToInt(_endsAt - Time.unscaledTime);
            var combo = Time.unscaledTime - _lastSmash < 1.6f && _combo > 1 ? $"   x{_combo} COMBO" : "";
            _board.text = $"TIME {left}\nSCORE {_score}{combo}";
        }
        else
        {
            var last = Last.Count > 0 ? "LAST  " + string.Join("  ", Last) : "PRESS THE BUTTON: 60 SECONDS";
            _board.text = $"BEST {_best}\n{last}";
        }
    }

    // ---------------------------------------------------------------------------------- the pops

    private static readonly List<(TextMeshPro t, float born, Vector3 at)> Popping = new List<(TextMeshPro, float, Vector3)>();

    private static void Pop(Vector3 at, string text, Color c)
    {
        var face = CinemaRig.HeadPosition - at;
        face.y = 0f;
        var tmp = MovieHouse.SignFacing(text, MovieHouse.Root.InverseTransformPoint(at + Vector3.up * 0.4f), 0.3f, c, face.sqrMagnitude > 1e-4f ? face.normalized : Vector3.back, FunWorld.Root);
        if (tmp == null) return;
        Popping.Add((tmp, Time.unscaledTime, tmp.transform.localPosition));
        while (Popping.Count > 10)
        {
            if (Popping[0].t != null) Object.Destroy(Popping[0].t.gameObject);
            Popping.RemoveAt(0);
        }
    }

    private static void Pops()
    {
        for (var i = Popping.Count - 1; i >= 0; i--)
        {
            var (t, born, at) = Popping[i];
            var age = Time.unscaledTime - born;
            if (t == null || age > 1.1f)
            {
                if (t != null) Object.Destroy(t.gameObject);
                Popping.RemoveAt(i);
                continue;
            }

            t.transform.localPosition = at + Vector3.up * age * 0.5f;
            var s = 1f + Mathf.Sin(Mathf.Min(age * 12f, Mathf.PI)) * 0.25f;
            t.transform.localScale = Vector3.one * s;
            var c = t.color;
            t.color = new Color(c.r, c.g, c.b, Mathf.Clamp01((1.1f - age) / 0.4f));
        }
    }

    // ---------------------------------------------------------------------------------- the scores

    private static void Load()
    {
        try
        {
            if (!System.IO.File.Exists(File)) return;
            var lines = System.IO.File.ReadAllLines(File);
            _best = lines.Length > 0 && int.TryParse(lines[0], out var b) ? b : 0;
            Last.Clear();
            for (var i = 1; i < lines.Length && Last.Count < 5; i++)
                if (int.TryParse(lines[i], out var v))
                    Last.Add(v);
        }
        catch (System.Exception e)
        {
            Plugin.L.LogWarning("RAGE MODE scores: " + e.Message);
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File));
            var lines = new List<string> { _best.ToString() };
            foreach (var v in Last) lines.Add(v.ToString());
            System.IO.File.WriteAllLines(File, lines);
        }
        catch (System.Exception e)
        {
            Plugin.L.LogWarning("RAGE MODE scores: " + e.Message);
        }
    }

    /// <summary>Gone with the movie house (a run in progress ends unscored).</summary>
    public static void Clear()
    {
        _endsAt = -1f;
        _board = _title = null;
        _cap = _lever = null;
        foreach (var (t, _, _) in Popping)
            if (t != null)
                Object.Destroy(t.gameObject);
        Popping.Clear();
    }

    public static string Describe() => $"RAGEMODE running {Running} score {_score} combo {_combo} best {_best}";
}
