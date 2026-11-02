using System.Collections.Generic;
using AngerFootVR.Core;
using Framework;
using UnityEngine;

namespace AngerFootVR.Body;

/// <summary>
/// Your body in VR: Anger Foot's own model (HeroModel), standing where the player is, scaled so his eyes are at the
/// game's eye height, wearing the shoes you have on (the game's own shoe meshes, switched the way the game switches its
/// kicking leg's: by each shoe's rig name, socks when the shoe shows them) and updating as soon as you change them. His
/// head is never drawn for you (you look out of it). Posed from your head and hands by BodyPose.
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
        BodyPose.Attach(_body.transform, _animator);

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

    /// <summary>Called after the body is posed: the twin takes the same pose, and its head stays.</summary>
    public static void PoseTwin(Transform neck, Transform head)
    {
        if (_twin == null || _bodyBones == null || _bodyBones.Length != _twinBones.Length) return;
        for (var i = 0; i < _bodyBones.Length; i++)
        {
            var a = _bodyBones[i];
            var b = _twinBones[i];
            b.localPosition = a.localPosition;
            b.localRotation = a.localRotation;
            b.localScale = a == neck || a == head ? Vector3.one : a.localScale;
        }

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
