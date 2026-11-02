using System;
using System.IO;
using BepInEx;
using Framework;
using HarmonyLib;
using UnityEngine;

namespace AngerFootVR.UI;

/// <summary>
/// The VR launcher's music and pictures, taken from your copy of the game the first time it shows them (nothing is
/// shipped): the title screen's music (its samples when the game keeps them decoded, otherwise recorded as it plays),
/// the game's logos, and the title screen's own picture. Written once into BepInEx/AngerFootVR/launcher in the game
/// folder, where the launcher finds them.
/// </summary>
[HarmonyPatch]
internal static class LauncherArt
{
    public static readonly string Folder = Path.Combine(Paths.BepInExRootPath, Path.Combine("AngerFootVR", "launcher"));
    private static string MusicFile => Path.Combine(Folder, "title-music.wav");
    private const float RecordSeconds = 80f;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(AudioManager), nameof(AudioManager.PlayMusic), typeof(GameObject), typeof(Type))]
    private static void MusicStarted(Music __result)
    {
        if (__result == null || File.Exists(MusicFile) || _taking) return;
        // the title music (it starts on the logo screen and plays on into the title)
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        var config = ScriptableObjectSingleton<GameConfig>.Instance;
        if (scene != config.LogoScreen.SceneName && scene != config.TitleScreen.SceneName) return;
        _taking = true;
        Plugin.Instance.StartCoroutine(TakeMusic(__result));
    }

    private static bool _taking;

    private static System.Collections.IEnumerator TakeMusic(Music music)
    {
        yield return null; // its sources are made in its Awake/Start
        if (music == null) yield break;
        Directory.CreateDirectory(Folder);

        // the whole loop, straight from the game's samples
        var clip = (music as SingleTrackMusic)?.TrackClip;
        if (clip != null && clip.loadType == AudioClipLoadType.DecompressOnLoad)
        {
            for (var i = 0; i < 300 && clip.loadState != AudioDataLoadState.Loaded; i++) yield return null;
            var data = new float[clip.samples * clip.channels];
            if (clip.loadState == AudioDataLoadState.Loaded && clip.GetData(data, 0))
            {
                WriteWav(MusicFile, data, clip.channels, clip.frequency);
                Plugin.L.LogInfo($"Launcher music: '{clip.name}' ({clip.length:F0} s) from the game's samples");
                yield break;
            }
        }

        // otherwise record what it plays
        AudioSource source = null;
        for (var i = 0; i < 300 && source == null; i++)
        {
            if (music == null) yield break;
            var sources = Traverse.Create(music).Field("_allSources").GetValue<System.Collections.Generic.List<AudioSource>>();
            if (sources != null)
                foreach (var s in sources)
                    if (s != null && s.clip != null && s.isPlaying && (source == null || s.volume > source.volume))
                        source = s;
            if (source == null) yield return null;
        }

        if (source == null) yield break;
        var tap = source.gameObject.AddComponent<MusicTap>();
        tap.Begin(AudioSettings.outputSampleRate, RecordSeconds);
        while (tap != null && !tap.Done && source != null) yield return null;
        if (tap == null) yield break;
        WriteWav(MusicFile, tap.Samples, tap.Channels, tap.Rate, tap.Count);
        UnityEngine.Object.Destroy(tap);
        Plugin.L.LogInfo($"Launcher music: '{source.clip.name}' recorded ({tap.Count / Mathf.Max(1, tap.Channels * tap.Rate)} s)");
    }

    /// <summary>Records the source's output (it sits on the source, so it hears only the music).</summary>
    private class MusicTap : MonoBehaviour
    {
        public float[] Samples;
        public int Count, Channels = 2, Rate;
        public bool Done;
        private volatile bool _on;

        public void Begin(int rate, float seconds)
        {
            Rate = rate;
            Samples = new float[(int)(rate * seconds) * 2];
            _on = true;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!_on || Done) return;
            Channels = channels;
            var n = Mathf.Min(data.Length, Samples.Length - Count);
            Array.Copy(data, 0, Samples, Count, n);
            Count += n;
            if (Count >= Samples.Length) Done = true;
        }
    }

    private static float _titleSince = -1f;
    private static bool _pictured;

    /// <summary>Once per install: the pictures, a few seconds into the title screen (its camera move done).</summary>
    public static void Update()
    {
        if (_pictured) return;
        if (!SingletonBehaviour<TitleScreen>.HasInstance || Core.CinemaRig.Feed == null)
        {
            _titleSince = -1f;
            return;
        }

        if (_titleSince < 0f) _titleSince = Time.unscaledTime;
        if (Time.unscaledTime - _titleSince < 8f) return;
        _pictured = true;
        TakePictures(Core.CinemaRig.Feed);
    }

    /// <summary>Takes the pictures the launcher shows (once): the logos, and the title screen's picture.</summary>
    public static void TakePictures(Texture titlePicture)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            Save("logo-pink.png", Find("Anger Foot Logo Pink 4x"));
            Save("logo-white.png", Find("Anger Foot Logo White 4x"));
            if (titlePicture != null) Save("title.png", titlePicture);
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("Launcher pictures: " + e.Message);
        }
    }

    private static Texture Find(string name)
    {
        foreach (var t in Resources.FindObjectsOfTypeAll<Texture2D>())
            if (t != null && t.name == name)
                return t;
        return null;
    }

    private static void Save(string file, Texture tex)
    {
        var path = Path.Combine(Folder, file);
        if (tex == null || File.Exists(path)) return;
        // through a render texture: the game's textures aren't readable from script
        var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(tex, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var read = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        File.WriteAllBytes(path, read.EncodeToPNG());
        UnityEngine.Object.Destroy(read);
        Plugin.L.LogInfo("Launcher picture: " + file);
    }

    /// <summary>16-bit PCM, peak-normalised (the recording carries the game's music volume setting).</summary>
    private static void WriteWav(string path, float[] samples, int channels, int rate, int count = -1)
    {
        if (count < 0) count = samples.Length;
        var peak = 0.0001f;
        for (var i = 0; i < count; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        var gain = Mathf.Min(0.89f / peak, 8f);
        using (var w = new BinaryWriter(File.Create(path)))
        {
            w.Write(new[] { 'R', 'I', 'F', 'F' });
            w.Write(36 + count * 2);
            w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            w.Write(16);
            w.Write((short)1);
            w.Write((short)channels);
            w.Write(rate);
            w.Write(rate * channels * 2);
            w.Write((short)(channels * 2));
            w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' });
            w.Write(count * 2);
            for (var i = 0; i < count; i++) w.Write((short)Mathf.Clamp(samples[i] * gain * 32767f, -32768f, 32767f));
        }
    }
}
