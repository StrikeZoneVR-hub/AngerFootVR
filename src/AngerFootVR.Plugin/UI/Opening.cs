using System;
using System.Collections.Generic;
using AngerFootVR.Core;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.UI;

/// <summary>
/// The VR opening (about six seconds; A, B or the menu button skips it; OPTIONS > VR OPENING: first start only, every
/// start or off). You arrive in the movie house in the dark. A work light clunks on over you: you look down and it's
/// you, in your own shoes. The ANGER FOOT VR sign on the wall buzzes on; the house lights bang on one after another
/// round the walls; the projector winds up and the big screen fades in with the game, and the menu comes up on it.
/// The sounds are made here (nothing is shipped) and come from where each light is.
/// </summary>
internal static class Opening
{
    private const float Spot = 0.6f, Sign = 2.2f, House = 3.6f, Projector = 4.5f, Lobby = 5.5f, End = 6.5f;

    /// <summary>While it plays, the game's menus don't take your buttons (the skip does).</summary>
    public static bool Playing { get; private set; }

    private static float _t0 = -1f;
    private static bool _done;
    private static Light _spot;
    private static float[] _base;
    private static readonly List<(GameObject Go, float At)> Signs = new List<(GameObject, float)>();
    private static readonly HashSet<int> Fired = new HashSet<int>();
    private static AudioClip _clunk, _buzz, _whir;
    private static AudioSource _source;

    private static readonly (float T, int Sound, Vector3 At, float Volume)[] Sounds =
    {
        (Spot, 0, new Vector3(0f, 3.2f, -3.2f), 0.9f),
        (Sign, 1, new Vector3(-6.9f, 3.9f, 5.5f), 0.6f),
        (House, 0, new Vector3(-5.5f, 5.5f, 4f), 1f),
        (House + 0.3f, 0, new Vector3(5.5f, 5.5f, 4f), 1f),
        (House + 0.6f, 0, new Vector3(0f, 5.5f, -3.5f), 0.8f),
        (Projector, 2, new Vector3(0f, 4f, -6f), 0.7f),
        (Lobby, 0, new Vector3(-1f, 3.2f, -2.6f), 0.5f),
    };

    private static bool _force;

    /// <summary>Dev: plays it again (now, if in the movie house).</summary>
    public static void DevReplay()
    {
        _done = false;
        _t0 = -1f;
        _force = true;
        Fired.Clear();
    }

    public static void Update()
    {
        if (_done || !CinemaRig.Active || MovieHouse.Root == null) return;
        if (_t0 < 0f)
        {
            var dev = Environment.GetEnvironmentVariable("AFVR_DEVSAVES") == "1";
            var mode = VrSettings.Opening.Value;
            var play = _force || (dev
                ? Environment.GetEnvironmentVariable("AFVR_OPENING") == "1"
                : mode == VrSettings.OpeningMode.On || (mode == VrSettings.OpeningMode.FirstLaunchOnly && !VrSettings.OpeningSeen.Value));
            if (!play)
            {
                _done = true;
                return;
            }

            if (!dev) VrSettings.OpeningSeen.Value = true;
            Begin();
        }

        var t = Time.unscaledTime - _t0;
        if (t > 0.4f && (VrControls.DownEither(VrButton.Primary) || VrControls.DownEither(VrButton.Secondary) || VrControls.DownEither(VrButton.Menu)))
        {
            Plugin.L.LogInfo("Opening skipped");
            _t0 -= End - t;
            t = End;
            for (var i = 0; i < Sounds.Length; i++) Fired.Add(i);
        }

        for (var i = 0; i < Sounds.Length; i++)
        {
            if (Fired.Contains(i) || t < Sounds[i].T) continue;
            Fired.Add(i);
            PlaySound(Sounds[i].Sound, MovieHouse.Origin + Sounds[i].At, Sounds[i].Volume);
        }

        Apply(t);
        if (t >= End) Finish();
    }

    private static void Begin()
    {
        _t0 = Time.unscaledTime;
        Playing = true;
        _base = new float[MovieHouse.Lights.Count];
        for (var i = 0; i < _base.Length; i++) _base[i] = MovieHouse.Lights[i].intensity;
        Signs.Clear();
        foreach (var t in MovieHouse.Root.GetComponentsInChildren<Transform>(true))
        {
            var n = t.gameObject.name;
            var at = n == "Sign VR" ? Sign : n == "Wall Logo" ? Sign + 0.3f : n == "Sign VR MOD BY STRIKEZONEVR" ? Sign + 0.6f : n == "Sign EXIT" ? House + 0.6f : -1f;
            if (at > 0f) Signs.Add((t.gameObject, at));
        }

        // the work light over your spot
        var go = new GameObject("AngerFootVR Opening Light") { layer = MovieHouse.Root.gameObject.layer };
        go.transform.SetParent(MovieHouse.Root, false);
        go.transform.position = MovieHouse.Standing + new Vector3(0f, 3.3f, 0.15f);
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        _spot = go.AddComponent<Light>();
        _spot.type = LightType.Spot;
        _spot.spotAngle = 62f;
        _spot.range = 7f;
        _spot.color = new Color(1f, 0.95f, 0.88f);
        _spot.shadows = LightShadows.None;
        _spot.cullingMask = MovieHouse.Lights.Count > 0 ? MovieHouse.Lights[0].cullingMask | (1 << Body.VrBody.MirrorLayer) : ~0;
        _spot.renderMode = LightRenderMode.ForcePixel;

        MakeSounds();
        Plugin.L.LogInfo("Opening: playing");
        Apply(0f);
    }

    private static void Finish()
    {
        Apply(End + 1f);
        Playing = false;
        _done = true;
        if (_spot != null) UnityEngine.Object.Destroy(_spot.gameObject);
        Plugin.L.LogInfo("Opening: done");
    }

    /// <summary>A light snapping on: a stutter, then steady.</summary>
    private static float SnapOn(float since)
    {
        if (since < 0f) return 0f;
        if (since < 0.05f) return 1f;
        if (since < 0.1f) return 0.15f;
        if (since < 0.16f) return 0.8f;
        return 1f;
    }

    private static void Apply(float t)
    {
        // the work light, then the lobby light takes over
        if (_spot != null) _spot.intensity = 3.2f * SnapOn(t - Spot) * (1f - Mathf.Clamp01((t - Lobby) / 0.8f));
        var lights = MovieHouse.Lights;
        var on = new[] { House, House + 0.3f, Projector + 0.4f, House + 0.6f, Lobby };
        for (var i = 0; i < lights.Count && i < _base.Length; i++)
        {
            var k = i < on.Length ? (i == 2 ? Mathf.Clamp01((t - on[i]) / 1.2f) : i == 4 ? Mathf.Clamp01((t - on[i]) / 0.8f) : SnapOn(t - on[i])) : 1f;
            lights[i].intensity = _base[i] * k;
        }

        CinemaRig.AmbientScale = Mathf.Lerp(0.08f, 1f, Mathf.Clamp01((t - House) / 1.0f));

        foreach (var (go, at) in Signs)
        {
            var lit = SnapOn(t - at) > 0.5f;
            if (go.activeSelf != lit) go.SetActive(lit);
        }

        // the projector winds up: the picture fades in, then the menu on it
        var picture = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - Projector) / 1.3f));
        if (MovieHouse.Feed != null) MovieHouse.Feed.sharedMaterial.SetColor("_EmissionColor", Color.white * picture);
        if (MovieHouse.Screen != null) MovieHouse.Screen.sharedMaterial.color = new Color(1f, 1f, 1f, Mathf.Clamp01((t - Projector - 0.6f) / 0.9f));
    }

    // ---------------------------------------------------------------- sound

    private static void PlaySound(int which, Vector3 at, float volume)
    {
        var clip = which == 0 ? _clunk : which == 1 ? _buzz : _whir;
        if (clip == null) return;
        if (_source == null)
        {
            var go = new GameObject("AngerFootVR Opening Sound");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _source = go.AddComponent<AudioSource>();
            _source.spatialBlend = 0f;
            _source.playOnAwake = false;
        }

        // (the game's listener isn't in the movie house: each sound is panned from where it is to your head)
        var head = CinemaRig.HeadRotation;
        var dir = at - CinemaRig.HeadPosition;
        var pan = Mathf.Clamp(Vector3.Dot((head * Vector3.right).normalized, dir.normalized), -1f, 1f);
        var near = Mathf.Clamp(4f / Mathf.Max(1f, dir.magnitude), 0.35f, 1f);
        _source.panStereo = pan * 0.8f;
        _source.PlayOneShot(clip, volume * near);
    }

    private static void MakeSounds()
    {
        if (_clunk != null) return;
        const int rate = 44100;
        var rng = new System.Random(3);
        float Noise() => (float)(rng.NextDouble() * 2 - 1);

        // a big switch thrown and the light's coil kicking in: a click, a deep thunk, a ring of metal
        var n = (int)(rate * 0.9f);
        var clunk = new float[n];
        double ph = 0;
        float lp = 0;
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / rate;
            ph += 2 * Math.PI * (55 + 50 * Math.Exp(-t * 30)) / rate;
            lp += (Noise() - lp) * 0.25f;
            var click = Noise() * Math.Exp(-t * 400) * 0.9;
            var thunk = Math.Sin(ph) * Math.Exp(-t * 9) * 0.9;
            var ring = Math.Sin(2 * Math.PI * 820 * t) * Math.Exp(-t * 14) * 0.08 + Math.Sin(2 * Math.PI * 1310 * t) * Math.Exp(-t * 18) * 0.05;
            var hum = t > 0.05 ? Math.Sin(2 * Math.PI * 120 * t) * 0.05 * Math.Exp(-(t - 0.05) * 3) : 0;
            clunk[i] = (float)Math.Tanh((click + thunk + lp * Math.Exp(-t * 50) * 0.6 + ring + hum) * 1.4) * 0.9f;
        }

        // a neon tube flickering on
        n = (int)(rate * 0.6f);
        var buzz = new float[n];
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / rate;
            var on = t < 0.06 || (t > 0.1 && t < 0.13) || t > 0.18 ? 1.0 : 0.15;
            var env = Math.Min(1, t / 0.005) * Math.Min(1, (0.6 - t) / 0.2);
            var hum = Math.Sin(2 * Math.PI * 120 * t) * 0.5 + Math.Sin(2 * Math.PI * 240 * t) * 0.3 + Math.Sin(2 * Math.PI * 360 * t) * 0.18;
            buzz[i] = (float)(Math.Tanh((hum + Noise() * 0.2 * (0.5 + 0.5 * Math.Sin(2 * Math.PI * 120 * t))) * 2.2) * 0.35 * on * env);
        }

        // the projector winding up: a motor rising, and the shutter's chatter
        n = (int)(rate * 2.2f);
        var whir = new float[n];
        ph = 0;
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / rate;
            var speed = Math.Min(1, t / 1.2);
            ph += 2 * Math.PI * (40 + 80 * speed) / rate;
            var env = Math.Min(1, t / 0.2) * Math.Min(1, (2.2 - t) / 0.6);
            var motor = Math.Sin(ph) * 0.25 + Math.Sin(ph * 2) * 0.12;
            var chatter = Math.Sin(t * 2 * Math.PI * 24 * speed) > 0.92 ? Noise() * 0.35 : 0;
            whir[i] = (float)((motor + chatter) * env * 0.6);
        }

        AudioClip Clip(string name, float[] data)
        {
            var c = AudioClip.Create(name, data.Length, 1, rate, false);
            c.SetData(data, 0);
            UnityEngine.Object.DontDestroyOnLoad(c);
            return c;
        }

        _clunk = Clip("AngerFootVR Clunk", clunk);
        _buzz = Clip("AngerFootVR Buzz", buzz);
        _whir = Clip("AngerFootVR Projector", whir);
    }
}
