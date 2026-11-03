using System.Collections.Generic;
using AngerFootVR.Core;
using Framework;
using UnityEngine;

namespace AngerFootVR.Body;

/// <summary>
/// Your body in VR: Anger Foot's own model (HeroModel), standing where the player is, scaled so his eyes are at the
/// game's eye height, wearing the shoes you have on (the game's own shoe meshes, switched the way the game switches its
/// kicking leg's: by each shoe's rig name, socks when the shoe shows them) and updating as soon as you change them. His
/// head is never drawn for you (you look out of it). Posed from your head and hands by HeroBody (InkVR's posing).
/// </summary>
internal static class VrBody
{
    private static GameObject _body;
    private static Animator _animator;
    private static Transform _head;
    private static readonly List<GameObject> Shoes = new List<GameObject>();
    private static GameObject _socks;
    private static ShoeType _wearing;
    private static float _nextShoeCheck;
    private static UnityEngine.Rendering.LightProbeUsage _probes = (UnityEngine.Rendering.LightProbeUsage)(-1);

    // the mirror twin: the same body with its head, posed as yours every frame, seen only by mirrors (the skin is
    // worked out once a frame, so a head hidden for your eyes can't be shown again for a mirror's)
    private static GameObject _twin;
    private static Transform[] _bodyBones, _twinBones;
    private static GameObject[] _bodyParts, _twinParts;

    /// <summary>The layer only mirrors (and dev pictures) draw: the twin's.</summary>
    public const int MirrorLayer = 7;

    public static Transform Root => _body != null ? _body.transform : null;
    public static Animator Animator => _animator;
    public static bool Active => _body != null && _body.activeSelf;

    public static void Update()
    {
        var inPlay = XrManager.IsRunning && (SingletonBehaviour<Player>.HasInstance || CinemaRig.Active);
        if (!inPlay)
        {
            if (_body != null) _body.SetActive(false);
            if (_twin != null) _twin.SetActive(false);
            return;
        }

        if (_body == null && !Spawn()) return;
        if (!_body.activeSelf) _body.SetActive(true);
        if (_twin != null && !_twin.activeSelf) _twin.SetActive(true);
        // in the movie house the game scene's light probes (made for a level far away) don't light him: the room does
        var probes = CinemaRig.Active ? UnityEngine.Rendering.LightProbeUsage.Off : UnityEngine.Rendering.LightProbeUsage.BlendProbes;
        if (_probes != probes)
        {
            _probes = probes;
            foreach (var r in _body.GetComponentsInChildren<Renderer>(true)) r.lightProbeUsage = probes;
            if (_twin != null) foreach (var r in _twin.GetComponentsInChildren<Renderer>(true)) r.lightProbeUsage = probes;
        }
        ShadowFromTwin(!CinemaRig.Active);
        if (Time.unscaledTime >= _nextShoeCheck)
        {
            _nextShoeCheck = Time.unscaledTime + 0.5f;
            SyncShoes();
        }
    }

    private static bool Spawn()
    {
        if (HeroModel.Template == null) return false;
        _body = Object.Instantiate(HeroModel.Template);
        _body.name = "AngerFootVR Body";
        Object.DontDestroyOnLoad(_body);
        foreach (var t in _body.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = UI.VrUi.Layer; // drawn by the game's camera, collides with nothing
        foreach (var r in _body.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        foreach (var s in _body.GetComponentsInChildren<SkinnedMeshRenderer>(true)) s.updateWhenOffscreen = true; // its bounds follow the posed bones
        _animator = _body.GetComponent<Animator>();
        _animator.enabled = false;
        _head = _animator.GetBoneTransform(HumanBodyBones.Head);
        FindShoes();
        _body.SetActive(true);

        _twin = Object.Instantiate(HeroModel.Template);
        _twin.name = "AngerFootVR Body (mirror twin)";
        Object.DontDestroyOnLoad(_twin);
        foreach (var t in _twin.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = MirrorLayer;
        foreach (var r in _twin.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // your body casts the shadow
        foreach (var s in _twin.GetComponentsInChildren<SkinnedMeshRenderer>(true)) s.updateWhenOffscreen = true;
        _twin.GetComponent<Animator>().enabled = false;
        _twin.SetActive(true);
        _bodyBones = _body.GetComponentsInChildren<Transform>(true);
        _twinBones = _twin.GetComponentsInChildren<Transform>(true);
        _bodyParts = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(_body.GetComponentsInChildren<Renderer>(true), r => r.gameObject));
        _twinParts = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(_twin.GetComponentsInChildren<Renderer>(true), r => r.gameObject));
        HeroBody.Attach(_body.transform, _animator, _twin.transform);
        Plugin.L.LogInfo($"VR body spawned: {Shoes.Count} shoe meshes, socks {(_socks != null ? "found" : "none")}, head bone {(_head != null ? _head.name : "none")}");
        return true;
    }

    private static readonly Dictionary<ShoeType, GameObject> ShoeMesh = new Dictionary<ShoeType, GameObject>();

    /// <summary>
    /// The model's shoe meshes, and which shoe each is. The model names its meshes after the shoes' names in the game
    /// (RocketShoes, SlideKickers), not after the first-person rig's (Jetpack, Rapid_Kicks), so each shoe is matched by
    /// its name, its text term, its rig name and its asset name, compared as plain letters and digits.
    /// </summary>
    private static void FindShoes()
    {
        Shoes.Clear();
        ShoeMesh.Clear();
        _socks = null;
        var meshes = new List<GameObject>();
        foreach (var r in _body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (r.name == HeroModel.SocksMesh || r.name.StartsWith("Socks")) _socks = r.gameObject;
            else if (r.rootBone != null && (r.rootBone.name.StartsWith("Hip_") || r.rootBone.name.StartsWith("Knee_"))) meshes.Add(r.gameObject);
        }

        Shoes.AddRange(meshes);
        var lines = new List<string>();
        foreach (var shoe in ShoeType.AllShoeTypes)
        {
            if (shoe == null || shoe == ShoeType.BarefootShoe) continue;
            // the game's own tag first
            if (HeroModel.ShoeMeshes.TryGetValue(shoe, out var tagged))
            {
                var t = meshes.Find(m => m.name == tagged);
                if (t != null)
                {
                    ShoeMesh[shoe] = t;
                    lines.Add($"{shoe.name}={t.name}");
                    continue;
                }
            }

            // a shoe the model has no tag for wears the mesh of a tagged shoe sharing its first-person model
            var twin = System.Linq.Enumerable.FirstOrDefault(HeroModel.ShoeMeshes.Keys, k => k.RigRendererName == shoe.RigRendererName && k != shoe);
            if (twin != null)
            {
                var t = meshes.Find(m => m.name == HeroModel.ShoeMeshes[twin]);
                if (t != null)
                {
                    ShoeMesh[shoe] = t;
                    lines.Add($"{shoe.name}={t.name} (as {twin.name})");
                    continue;
                }
            }

            var keys = new List<string>();
            try { keys.Add(Plain(shoe.ShoeName != null ? shoe.ShoeName.GetTranslation() : "")); } catch { }
            if (shoe.ShoeName != null && !string.IsNullOrEmpty(shoe.ShoeName.Term))
                foreach (var part in shoe.ShoeName.Term.Split('/')) keys.Add(Plain(part));
            keys.Add(Plain(shoe.RigRendererName));
            keys.Add(Plain(shoe.name));
            // generic words ("shoe", "shoes", "name") would match anything
            keys = keys.ConvertAll(k => k.EndsWith("shoes") && k.Length > 6 ? k : k.EndsWith("shoe") && k.Length > 5 ? k.Substring(0, k.Length - 4) : k);
            keys.RemoveAll(k => k.Length < 4 || k == "shoe" || k == "shoes" || k == "name" || k == "names" || k == "description");
            var match = meshes.Find(m => keys.Contains(Plain(m.name)));
            if (match == null)
                foreach (var k in keys)
                {
                    match = meshes.Find(m => Plain(m.name).Length >= 5 && (k.Contains(Plain(m.name)) || Plain(m.name).Contains(k)));
                    if (match != null) break;
                }

            if (match == null) lines.Add($"[{shoe.name} keys {string.Join("/", keys.ToArray())}]");
            if (match != null) ShoeMesh[shoe] = match;
            lines.Add($"{shoe.name}={(match != null ? match.name : "?")}");
        }

        Plugin.L.LogInfo($"VR body shoes ({meshes.Count} meshes): {string.Join(", ", lines.ToArray())}");
    }

    private static string Plain(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var b = new System.Text.StringBuilder();
        foreach (var c in s.ToLowerInvariant())
            if (char.IsLetterOrDigit(c))
                b.Append(c);
        return b.ToString();
    }

    private static bool _shown = true;

    /// <summary>The body steps aside while your head is down near the floor (no body can stand under it).</summary>
    public static void SetShown(bool shown)
    {
        if (_shown == shown || _body == null) return;
        _shown = shown;
        foreach (var r in _body.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = !shown;
        if (_twin != null) foreach (var r in _twin.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = !shown;
    }

    private static Renderer[] _bodyR, _twinR;
    private static UnityEngine.Rendering.ShadowCastingMode[] _twinModes;
    private static float _nextRenderers;

    /// <summary>The monitor's BEHIND YOU camera: it draws the twin (head and all, posed with you, where you are), not the
    /// body you see (its head is shrunk away); everything put back after.</summary>
    public static void ForBehindView(bool on)
    {
        if (_body == null || _twin == null) return;
        if (_bodyR == null || _twinR == null || (on && Time.unscaledTime > _nextRenderers))
        {
            _nextRenderers = Time.unscaledTime + 2f;
            _bodyR = _body.GetComponentsInChildren<Renderer>(true);
            _twinR = _twin.GetComponentsInChildren<Renderer>(true);
            _twinModes = new UnityEngine.Rendering.ShadowCastingMode[_twinR.Length];
        }

        foreach (var r in _bodyR)
            if (r != null)
                r.forceRenderingOff = on || !_shown;
        for (var i = 0; i < _twinR.Length; i++)
        {
            var r = _twinR[i];
            if (r == null) continue;
            if (on)
            {
                _twinModes[i] = r.shadowCastingMode;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            else r.shadowCastingMode = _twinModes[i];
        }
    }

    /// <summary>After posing: the twin wears what the body wears (the shoes, the socks).</summary>
    private static bool? _twinShadow;

    /// <summary>
    /// Your shadow, head and all: the body you see has its head shrunk away (so it's never in front of your eyes), so
    /// its shadow had no head. Out of the movie house the mirror twin (posed with you, head and all) casts your shadow
    /// instead, shadow only, on the body's layer (the levels' lamps light that layer; some leave the mirror's out). In the
    /// movie house it's the mirror's reflection again (and nothing there casts shadows).
    /// </summary>
    private static void ShadowFromTwin(bool twin)
    {
        if (_twinShadow == twin || _body == null || _twin == null) return;
        _twinShadow = twin;
        foreach (var r in _body.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = twin ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
        foreach (var t in _twin.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = twin ? UI.VrUi.Layer : MirrorLayer;
        foreach (var r in _twin.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = twin ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>Dev: who casts your shadow. 0: the body (as before); 1: the mirror twin on its own layer; 2: the twin on
    /// the body's layer, shadow only.</summary>
    public static string DevShadow(int mode)
    {
        if (_body == null || _twin == null) return "no body";
        _twinShadow = mode == 2;
        foreach (var r in _body.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = mode == 0 ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        foreach (var t in _twin.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = mode == 2 ? UI.VrUi.Layer : MirrorLayer;
        foreach (var r in _twin.GetComponentsInChildren<Renderer>(true))
            r.shadowCastingMode = mode == 0 ? UnityEngine.Rendering.ShadowCastingMode.Off : mode == 1 ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        return "shadow mode " + mode;
    }

    public static void SyncTwinParts()
    {
        if (_twin == null || _bodyParts == null || _twinParts == null) return;
        if (_bodyParts.Length == _twinParts.Length)
            for (var i = 0; i < _bodyParts.Length; i++)
                if (_twinParts[i].activeSelf != _bodyParts[i].activeSelf)
                    _twinParts[i].SetActive(_bodyParts[i].activeSelf);
    }

    private static void SyncShoes()
    {
        var wanted = GameState.CurrentSave != null ? GameState.CurrentSave.GetSelectedShoeType() : null;
        if (wanted == _wearing && _wearing != null) return;
        _wearing = wanted;
        foreach (var s in Shoes) s.SetActive(false);
        if (_socks != null) _socks.SetActive(wanted != null && wanted.ShowSocks);
        if (wanted == null || wanted == ShoeType.BarefootShoe)
        {
            Plugin.L.LogInfo("VR body: barefoot");
            return;
        }

        ShoeMesh.TryGetValue(wanted, out var on);
        if (on != null) on.SetActive(true);
        Plugin.L.LogInfo($"VR body wears {wanted.name} ({(on != null ? on.name : "no mesh matched")})");
    }

    /// <summary>Dev: the body's state.</summary>
    public static string Describe() =>
        _body == null
            ? "BODY none (template " + (HeroModel.Template != null ? "ready" : "not taken yet: visit the title screen") + ")"
            : $"BODY at {_body.transform.position} rot {_body.transform.eulerAngles.y:F0} scale {_body.transform.localScale.x:F2} wearing {(_wearing != null ? _wearing.name : "?")} active {_body.activeSelf}";
}
