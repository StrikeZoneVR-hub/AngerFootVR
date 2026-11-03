using System.Collections.Generic;
using SoundManager;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AngerFootVR.UI;

/// <summary>
/// Anger Foot's own sounds in the movie house's rooms: the clips of the game's sound banks (window smashes, wood
/// splintering, bottles, TVs, cans, splashes, explosions, every gun's shot), taken from the player's copy as they're
/// loaded (the title has most; a level adds the rest, kept for the session), played from where things happen through
/// a pool of 3D sources at the game's own sound effects volume. A sound the game hasn't loaded yet: the mod's own
/// (Sounds) plays instead. Music clips too (the jukebox), at the game's music volume.
/// </summary>
internal static class GameSounds
{
    private static readonly Dictionary<string, SingleSoundBank> Banks = new Dictionary<string, SingleSoundBank>();
    private static readonly Dictionary<string, AudioClip> Music = new Dictionary<string, AudioClip>();
    private static readonly List<AudioSource> Pool = new List<AudioSource>();
    private static readonly Dictionary<string, float> LastPlayed = new Dictionary<string, float>();
    private static int _next;
    private static bool _hooked;
    private static Transform _root;

    public static void Init()
    {
        if (_hooked) return;
        _hooked = true;
        SceneManager.sceneLoaded += (s, m) => _scanAt = Time.unscaledTime + 1f;
    }

    private static float _scanAt = 0f;

    /// <summary>The banks and music loaded now (a scene load brings more).</summary>
    public static void Scan()
    {
        foreach (var b in Resources.FindObjectsOfTypeAll<SingleSoundBank>())
            if (b != null && b.Clips != null && b.Clips.Length > 0 && !Banks.ContainsKey(b.name))
                Banks[b.name] = b;
        foreach (var c in Resources.FindObjectsOfTypeAll<AudioClip>())
            if (c != null && c.length > 40f && !Music.ContainsKey(c.name))
                Music[c.name] = c;
    }

    private static void Rescan()
    {
        if (_scanAt >= 0f && Time.unscaledTime >= _scanAt)
        {
            _scanAt = -1f;
            Scan();
        }
    }

    public static bool Has(string bank)
    {
        Rescan();
        return Banks.ContainsKey(bank);
    }

    /// <summary>One of a bank's clips from a place (false: the game hasn't that bank loaded).</summary>
    public static bool Play(string bank, Vector3 at, float volume = 1f, float pitch = 1f, float cooldown = 0.03f)
    {
        if (string.IsNullOrEmpty(bank)) return false;
        Rescan();
        if (Banks.Count == 0) Scan();
        if (!Banks.TryGetValue(bank, out var b) || b == null) return false;
        var clips = b.Clips;
        if (clips == null || clips.Length == 0) return false;
        // (the same sound twice at once is just louder: a little gap between them)
        if (LastPlayed.TryGetValue(bank, out var last) && Time.unscaledTime - last < cooldown) return true;
        LastPlayed[bank] = Time.unscaledTime;
        var clip = clips[Random.Range(0, clips.Length)];
        if (clip == null) return false;
        var src = Source();
        src.transform.position = at;
        var vr = b.VolumeRange;
        var pr = b.PitchRange;
        src.volume = Mathf.Clamp01(volume * Random.Range(vr.Min, vr.Max) * 2f * Mathf.Clamp01(GameSettings.SFXVolume / 0.75f));
        src.pitch = pitch * Random.Range(pr.Min, pr.Max);
        src.clip = clip;
        src.loop = false;
        src.Play();
        return true;
    }

    /// <summary>A game sound, or the mod's own if the game hasn't it loaded.</summary>
    public static void PlayOr(string bank, string own, Vector3 at, float volume = 1f, float pitch = 1f)
    {
        if (!Play(bank, at, volume, pitch)) Sounds.Play(own, at, volume, pitch);
    }

    /// <summary>A looping source on a thing (the TV's static, the flamethrower's roar, the water cooler's glug); null
    /// if the game hasn't the bank loaded. Its owner stops and destroys it.</summary>
    public static AudioSource Loop(string bank, Transform on, float volume)
    {
        Rescan();
        if (!Banks.TryGetValue(bank, out var b) || b == null || b.Clips == null || b.Clips.Length == 0) return null;
        var go = new GameObject("AngerFootVR loop " + bank);
        go.transform.SetParent(on, false);
        var s = Configure(go.AddComponent<AudioSource>());
        s.clip = b.Clips[0];
        s.loop = true;
        s.volume = Mathf.Clamp01(volume * Mathf.Clamp01(GameSettings.SFXVolume / 0.75f));
        s.Play();
        return s;
    }

    /// <summary>The game's music tracks loaded now (the jukebox's).</summary>
    public static List<AudioClip> Tracks()
    {
        Rescan();
        if (Music.Count == 0) Scan();
        var list = new List<AudioClip>();
        foreach (var c in Music.Values)
            if (c != null)
                list.Add(c);
        list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return list;
    }

    public static float MusicVolume => Mathf.Clamp01(GameSettings.MusicVolume / 0.75f);

    private static AudioSource Source()
    {
        if (Pool.Count == 0)
        {
            var root = new GameObject("AngerFootVR Game Sounds");
            Object.DontDestroyOnLoad(root);
            _root = root.transform;
            for (var i = 0; i < 20; i++)
            {
                var go = new GameObject("Sound");
                go.transform.SetParent(_root, false);
                Pool.Add(Configure(go.AddComponent<AudioSource>()));
            }
        }

        // (the oldest that's finished, else the oldest)
        for (var k = 0; k < Pool.Count; k++)
        {
            var s = Pool[(_next + k) % Pool.Count];
            if (!s.isPlaying)
            {
                _next = (_next + k + 1) % Pool.Count;
                return s;
            }
        }

        var o = Pool[_next];
        _next = (_next + 1) % Pool.Count;
        return o;
    }

    public static AudioSource Configure(AudioSource s)
    {
        s.playOnAwake = false;
        s.spatialBlend = 1f;
        s.rolloffMode = AudioRolloffMode.Linear;
        s.minDistance = 1.5f;
        s.maxDistance = 30f;
        s.dopplerLevel = 0f;
        s.ignoreListenerPause = true;
        return s;
    }

    public static string Describe() => $"GAMESOUNDS banks {Banks.Count} music {Music.Count}";
}
