using System;
using System.Collections.Generic;
using UnityEngine;

namespace AngerFootVR.UI;

/// <summary>
/// The movie house's and rage room's sounds, made here (nothing shipped): the doors banging open, wood splintering,
/// glass shattering, china and plaster breaking, the TV's tube popping with a fizz, cardboard crunching, things
/// thudding down, and footsteps (soft on the carpet, harder on concrete). Each plays from where it happens, through a
/// small pool of 3D sources (the movie house listens from your head: CinemaRig).
/// </summary>
internal static class Sounds
{
    private const int Rate = 44100;
    private static readonly Dictionary<string, AudioClip[]> Clips = new Dictionary<string, AudioClip[]>();
    private static readonly List<AudioSource> Pool = new List<AudioSource>();
    private static int _next;
    [ThreadStatic] private static System.Random _rng; // (the synthesis's: set for each clip, on whichever thread makes it)
    private static readonly System.Random Pick = new System.Random(7);
    private static readonly string[] Names = { "door_bang", "wood", "glass", "china", "tv", "cardboard", "plaster", "thud", "step", "step_hard", "kick" };
    private static System.Threading.Tasks.Task<Dictionary<string, float[][]>> _warm;

    private static float Noise() => (float)(_rng.NextDouble() * 2 - 1);

    public static void Play(string name, Vector3 at, float volume, float pitch = 1f)
    {
        if (!Clips.ContainsKey(name)) Make(name);
        if (!Clips.TryGetValue(name, out var variants) || variants.Length == 0) return;
        if (Pool.Count == 0)
        {
            var root = new GameObject("AngerFootVR Sounds");
            UnityEngine.Object.DontDestroyOnLoad(root);
            for (var i = 0; i < 14; i++)
            {
                var go = new GameObject("Sound");
                go.transform.SetParent(root.transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 1.5f;
                s.maxDistance = 28f;
                s.dopplerLevel = 0f;
                s.ignoreListenerPause = true;
                Pool.Add(s);
            }
        }

        var src = Pool[_next];
        _next = (_next + 1) % Pool.Count;
        src.transform.position = at;
        src.pitch = pitch * (0.92f + (float)Pick.NextDouble() * 0.16f);
        src.volume = Mathf.Clamp01(volume);
        src.clip = variants[Pick.Next(variants.Length)];
        src.Play();
    }

    /// <summary>Every frame in the movie house, until done: the sounds made ahead of time on a worker thread (made
    /// when first played, a TV's took 57 ms and a bottle's 31: the first smash of each hitched).</summary>
    public static void Warm()
    {
        if (Clips.Count >= Names.Length) return;
        if (_warm == null)
        {
            _warm = System.Threading.Tasks.Task.Run(() =>
            {
                var all = new Dictionary<string, float[][]>();
                foreach (var n in Names) all[n] = new[] { Synth(n, 0), Synth(n, 1), Synth(n, 2) };
                return all;
            });
            return;
        }

        if (!_warm.IsCompleted) return;
        if (_warm.IsFaulted)
        {
            Plugin.L.LogWarning("Sounds: " + _warm.Exception?.GetBaseException().Message);
            foreach (var n in Names) Clips[n] = new AudioClip[0];
            return;
        }

        // (into clips on the main thread, a few a frame)
        var made = 0;
        foreach (var kv in _warm.Result)
        {
            if (Clips.ContainsKey(kv.Key)) continue;
            Clips[kv.Key] = ToClips(kv.Key, kv.Value);
            if (++made >= 3) break;
        }
    }

    private static void Make(string name)
    {
        if (Array.IndexOf(Names, name) < 0) return;
        Clips[name] = ToClips(name, new[] { Synth(name, 0), Synth(name, 1), Synth(name, 2) });
    }

    private static AudioClip[] ToClips(string name, float[][] variants)
    {
        var list = new AudioClip[variants.Length];
        for (var v = 0; v < variants.Length; v++)
        {
            var clip = AudioClip.Create($"AngerFootVR {name} {v}", variants[v].Length, 1, Rate, false);
            clip.SetData(variants[v], 0);
            UnityEngine.Object.DontDestroyOnLoad(clip);
            list[v] = clip;
        }

        return list;
    }

    /// <summary>One variant of a sound (plain maths: safe on a worker thread).</summary>
    private static float[] Synth(string name, int v)
    {
        _rng = new System.Random(Hash(name) + v * 977);
        switch (name)
        {
            case "door_bang": return Bang(0.9f, 55f, 0.6f);
            case "wood": return Wood();
            case "glass": return Glass();
            case "china": return China();
            case "tv": return Tv();
            case "cardboard": return Cardboard();
            case "plaster": return China(0.6f);
            case "thud": return Bang(0.3f, 90f, 0.25f);
            case "step": return Step(false);
            case "step_hard": return Step(true);
            default: return Whoosh();
        }
    }

    private static int Hash(string s)
    {
        var h = 17;
        foreach (var c in s) h = h * 31 + c;
        return h;
    }

    // ---------------------------------------------------------------- synthesis

    private static float[] Buf(float seconds) => new float[(int)(Rate * seconds)];

    /// <summary>A heavy impact: a pitched-down boom, a knock, a rattle.</summary>
    private static float[] Bang(float seconds, float pitch, float rattle)
    {
        var s = Buf(seconds);
        double ph = 0;
        float lp = 0, lp2 = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var t = (double)i / Rate;
            ph += 2 * Math.PI * (pitch + 80 * Math.Exp(-t * 25)) / Rate;
            lp += (Noise() - lp) * 0.2f;
            lp2 += (Noise() - lp2) * 0.05f;
            var v = Math.Sin(ph) * Math.Exp(-t * 7) + lp * Math.Exp(-t * 35) * 1.2 + lp2 * Math.Exp(-t * 6) * rattle * Math.Abs(Math.Sin(t * 130));
            s[i] = (float)Math.Tanh(v * 1.5) * 0.9f;
        }

        return s;
    }

    /// <summary>Wood splitting: cracks, a low knock, splinters clattering.</summary>
    private static float[] Wood()
    {
        var s = Bang(1.0f, 120f, 0.3f);
        for (var k = 0; k < 14; k++)
        {
            var at = (int)(Rate * (0.02 + _rng.NextDouble() * 0.6));
            var f = 300 + _rng.NextDouble() * 900;
            var amp = 0.5 * (1 - at / (double)s.Length);
            for (var j = 0; j < Rate * 0.05 && at + j < s.Length; j++)
            {
                var t = j / (double)Rate;
                s[at + j] += (float)((Math.Sin(2 * Math.PI * f * t) * 0.5 + Noise() * 0.8) * Math.Exp(-t * 90) * amp);
            }
        }

        return Clip(s);
    }

    /// <summary>Glass shattering: a bright crash and tinkling shards.</summary>
    private static float[] Glass()
    {
        var s = Buf(1.3f);
        float hp = 0, prev = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var t = (double)i / Rate;
            var x = Noise();
            hp = 0.85f * (hp + x - prev);
            prev = x;
            s[i] = (float)(hp * Math.Exp(-t * 9) * 0.8);
        }

        for (var k = 0; k < 40; k++)
        {
            var at = (int)(Rate * (_rng.NextDouble() * 1.1));
            var f = 2500 + _rng.NextDouble() * 5000;
            var amp = 0.18 * (1 - at / (double)s.Length);
            for (var j = 0; j < Rate * 0.08 && at + j < s.Length; j++)
            {
                var t = j / (double)Rate;
                s[at + j] += (float)(Math.Sin(2 * Math.PI * f * t) * Math.Exp(-t * 60) * amp);
            }
        }

        return Clip(s);
    }

    /// <summary>China or plaster breaking: a hard clack and chunks landing.</summary>
    private static float[] China(float ring = 1f)
    {
        var s = Buf(0.9f);
        for (var i = 0; i < s.Length; i++)
        {
            var t = (double)i / Rate;
            s[i] = (float)((Math.Sin(2 * Math.PI * 1900 * t) * 0.35 * ring + Math.Sin(2 * Math.PI * 1310 * t) * 0.25 * ring + Noise() * 0.6) * Math.Exp(-t * 28));
        }

        for (var k = 0; k < 12; k++)
        {
            var at = (int)(Rate * (0.08 + _rng.NextDouble() * 0.7));
            var f = 900 + _rng.NextDouble() * 2400;
            for (var j = 0; j < Rate * 0.04 && at + j < s.Length; j++)
            {
                var t = j / (double)Rate;
                s[at + j] += (float)((Math.Sin(2 * Math.PI * f * t) * 0.5 + Noise() * 0.5) * Math.Exp(-t * 110) * 0.35);
            }
        }

        return Clip(s);
    }

    /// <summary>A TV: the tube pops, glass, and an electric fizz dying away.</summary>
    private static float[] Tv()
    {
        var pop = Bang(0.4f, 70f, 0.1f);
        var glass = Glass();
        var s = Buf(1.4f);
        for (var i = 0; i < s.Length; i++)
        {
            var t = (double)i / Rate;
            var fizz = (Noise() * 0.5 + Math.Sin(2 * Math.PI * 60 * t) * 0.3) * Math.Exp(-t * 2.5) * (t > 0.1 ? 0.35 : 0) * (0.6 + 0.4 * Math.Sin(t * 70));
            s[i] = (float)((i < pop.Length ? pop[i] : 0) + (i < glass.Length ? glass[i] * 0.8f : 0) + fizz);
        }

        return Clip(s);
    }

    private static float[] Cardboard()
    {
        var s = Buf(0.5f);
        float lp = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var t = (double)i / Rate;
            lp += (Noise() - lp) * 0.35f;
            var crinkle = _rng.NextDouble() < 0.02 ? Noise() * 0.6 : 0;
            s[i] = (float)((lp * 1.2 + crinkle) * Math.Exp(-t * 10) + Math.Sin(2 * Math.PI * 95 * t) * Math.Exp(-t * 30) * 0.6);
        }

        return Clip(s);
    }

    private static float[] Step(bool hard)
    {
        var s = Buf(0.18f);
        float lp = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var t = (double)i / Rate;
            lp += (Noise() - lp) * (hard ? 0.5f : 0.12f);
            s[i] = (float)((lp * (hard ? 0.7 : 1.4) + Math.Sin(2 * Math.PI * (hard ? 140 : 80) * t) * 0.35) * Math.Exp(-t * (hard ? 45 : 30)) * 0.6);
        }

        return s;
    }

    private static float[] Whoosh()
    {
        var s = Buf(0.3f);
        float lp = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var t = (double)i / Rate;
            lp += (Noise() - lp) * (float)(0.05 + 0.3 * Math.Sin(Math.PI * t / 0.3));
            s[i] = (float)(lp * Math.Sin(Math.PI * t / 0.3) * 0.7);
        }

        return s;
    }

    private static float[] Clip(float[] s)
    {
        for (var i = 0; i < s.Length; i++) s[i] = (float)Math.Tanh(s[i] * 1.2);
        return s;
    }
}
