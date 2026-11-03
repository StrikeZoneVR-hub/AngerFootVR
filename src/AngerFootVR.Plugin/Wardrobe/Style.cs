using System.Collections.Generic;
using UnityEngine;

namespace AngerFootVR.Wardrobe;

/// <summary>
/// Dressing the VR body (and its mirror twin, the same way): fitted shells of the hero's skin, his own pieces hidden
/// or shaded another way, rigid pieces riding on a bone (sized to the bone's own flesh). Everything put on is taken
/// off again (and his own look put back) by Undress.
/// </summary>
internal static class Style
{
    private static readonly List<GameObject> Pieces = new List<GameObject>();
    private static readonly Dictionary<Renderer, (Material[] mats, bool enabled)> Original = new Dictionary<Renderer, (Material[], bool)>();
    private static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
    private static Mesh _ball, _rod, _box;

    public static HeroShell Skin => HeroShell.Get("AngerfootBody");

    // ------------------------------------------------------------------ materials

    /// <summary>A cartoon material (the mod's toon shader with its ink outline): colour, gloss, outline width.</summary>
    public static Material Toon(string key, Color colour, float gloss = 0f, float outline = 0.004f, float shade = 0.6f, bool vertexColours = false)
    {
        if (Mats.TryGetValue(key, out var m) && m != null) return m;
        var sh = Rendering.Shaders.Get("AngerFootVR/Toon");
        m = new Material(sh != null ? sh : Shader.Find("Standard")) { name = "AngerFootVR wardrobe " + key, color = colour, enableInstancing = true };
        m.SetFloat("_Gloss", gloss);
        m.SetFloat("_Outline", outline);
        m.SetFloat("_Shade", shade);
        m.SetFloat("_VertexColour", vertexColours ? 1f : 0f);
        Mats[key] = m;
        return m;
    }

    // ------------------------------------------------------------------ the hero's own pieces

    private static IEnumerable<Renderer> Own()
    {
        foreach (var r in Body.VrBody.AllRenderers())
            if (r != null && !r.name.StartsWith("AngerFootVR"))
                yield return r;
    }

    private static void Keep(Renderer r)
    {
        if (!Original.ContainsKey(r)) Original[r] = (r.sharedMaterials, r.enabled);
    }

    /// <summary>The hero's own pieces of these names hidden (both copies).</summary>
    public static void Hide(params string[] names)
    {
        foreach (var r in Own())
            if (System.Array.IndexOf(names, r.name) >= 0)
            {
                Keep(r);
                r.enabled = false;
            }
    }

    /// <summary>All the hero's own pieces hidden but these (shoes and socks are left as the shoe choice has them).</summary>
    public static void HideAllBut(params string[] keep)
    {
        foreach (var r in Own())
        {
            if (System.Array.IndexOf(keep, r.name) >= 0) continue;
            Keep(r);
            r.enabled = false;
        }
    }

    /// <summary>The hero's own pieces shaded another way: each of their materials made into another (a new one).</summary>
    public static void Restyle(System.Func<Material, Material> make)
    {
        foreach (var r in Own())
        {
            Keep(r);
            var src = Original[r].mats;
            var mats = new Material[src.Length];
            for (var i = 0; i < src.Length; i++) mats[i] = src[i] != null ? make(src[i]) : null;
            r.sharedMaterials = mats;
        }
    }

    /// <summary>The hero's own look and texture in the cartoon shader (his colours as drawn, an ink outline round him).</summary>
    public static Material ToonOf(Material src, float outline = 0.005f, Color? tint = null, bool keepTexture = true, float gloss = 0f)
    {
        var key = $"toon {src.GetInstanceID()} {outline} {tint} {keepTexture} {gloss}";
        if (Mats.TryGetValue(key, out var m) && m != null) return m;
        var sh = Rendering.Shaders.Get("AngerFootVR/Toon");
        m = new Material(sh) { name = "AngerFootVR toon " + src.name, enableInstancing = true };
        var tex = src.mainTexture;
        if (keepTexture && tex != null) m.mainTexture = tex;
        m.color = tint ?? Color.white;
        m.SetFloat("_Outline", outline);
        m.SetFloat("_Gloss", gloss);
        Mats[key] = m;
        return m;
    }

    // ------------------------------------------------------------------ pieces

    /// <summary>A shell of the skin over these bones (a set of bone names), pushed out by `outward`, worn by both copies.</summary>
    public static bool Shell(HashSet<string> bones, float outward, Material mat, System.Func<int, Vector3, Vector3, Color32> colour = null)
    {
        var skin = Skin;
        if (skin == null) return false;
        var mesh = skin.Shell(n => bones.Contains(n), outward, colour, out _);
        foreach (var r in Body.VrBody.AllRenderers())
        {
            if (!(r is SkinnedMeshRenderer smr) || smr.name != skin.Name) continue;
            var go = new GameObject("AngerFootVR worn shell") { layer = smr.gameObject.layer };
            go.transform.SetParent(smr.transform.parent, false);
            go.transform.localPosition = smr.transform.localPosition;
            go.transform.localRotation = smr.transform.localRotation;
            go.transform.localScale = smr.transform.localScale;
            var worn = go.AddComponent<SkinnedMeshRenderer>();
            worn.sharedMesh = mesh;
            worn.bones = smr.bones;
            worn.rootBone = smr.rootBone;
            worn.sharedMaterial = mat;
            worn.updateWhenOffscreen = true;
            worn.shadowCastingMode = smr.shadowCastingMode;
            Pieces.Add(go);
        }

        return true;
    }

    /// <summary>Under a suit: the hero's own skin hidden, and only what the suit leaves bare (the head, the hands, the
    /// feet: the bones not covered) drawn again from the rebuilt skin with his own material. (Close to your eyes the
    /// headset's near clip cuts the suit's surface before the skin under it: with the skin there, his own sleeves had
    /// shown through at the shoulders.)</summary>
    public static void BareOnly(HashSet<string> covered)
    {
        var skin = Skin;
        if (skin == null) return;
        Material own = null;
        foreach (var r in Own())
            if (r.name == skin.Name)
            {
                own = Original.TryGetValue(r, out var o) ? o.mats[0] : r.sharedMaterial;
                break;
            }

        if (own == null) return;
        var bare = new HashSet<string>(skin.BoneNames);
        bare.ExceptWith(covered);
        Shell(bare, 0f, own);
        Hide(skin.Name);
    }

    /// <summary>A rigid piece on a bone (by name) of both copies: placed in the bone's own space.</summary>
    public static void Rigid(string bone, Mesh mesh, Material mat, Vector3 localPos, Quaternion localRot, Vector3 localScale)
    {
        // (sized against the twin's bone: the first-person body's head is shrunk out of your eyes' way, and what's on
        // it must shrink with it)
        var roots = Body.VrBody.Roots;
        var twinBone = roots[1] != null ? FindDeep(roots[1], bone) : null;
        foreach (var root in roots)
        {
            if (root == null) continue;
            var b = FindDeep(root, bone);
            if (b == null) continue;
            var ref_ = twinBone != null ? twinBone : b;
            var go = new GameObject("AngerFootVR worn " + bone) { layer = b.gameObject.layer };
            go.transform.SetParent(b, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            // (in the bone's space without its scale)
            var ls = ref_.lossyScale;
            go.transform.localScale = new Vector3(localScale.x / Mathf.Max(1e-4f, ls.x), localScale.y / Mathf.Max(1e-4f, ls.y), localScale.z / Mathf.Max(1e-4f, ls.z));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            // (drawn as this copy's own skin is: the twin only casts your shadow in a level)
            var own = ReferenceRenderer(root);
            if (own != null) mr.shadowCastingMode = own.shadowCastingMode;
            Pieces.Add(go);
        }
    }

    /// <summary>One of the mod's models (props.afvp) on a bone of both copies, its materials by name (some swapped).</summary>
    public static void Part(string bone, string model, Vector3 localPos, Quaternion localRot, Vector3 localScale, System.Func<string, string> swap = null)
    {
        if (!UI.Props.Load()) return;
        var m = UI.Props.Get(model);
        if (m == null) return;
        var mats = new Material[m.Materials.Length];
        for (var i = 0; i < mats.Length; i++) mats[i] = UI.Props.Mat(swap != null ? swap(m.Materials[i]) : m.Materials[i]);
        var before = Pieces.Count;
        Rigid(bone, m.Mesh, mats[0], localPos, localRot, localScale);
        for (var i = before; i < Pieces.Count; i++)
            if (Pieces[i] != null)
                Pieces[i].GetComponent<MeshRenderer>().sharedMaterials = mats;
    }

    /// <summary>A rod from a bone to its child (or a point on it), riding on the bone: radius in metres (a limb's bone).</summary>
    public static void Rod(string bone, Vector3 toLocal, float radius, Material mat)
    {
        var len = toLocal.magnitude;
        if (len < 1e-3f) return;
        Rigid(bone, Rod(), mat, toLocal * 0.5f, Quaternion.FromToRotation(Vector3.up, toLocal / len), new Vector3(radius * 2f, len * 0.5f, radius * 2f));
    }

    public static void Ball(string bone, Vector3 local, float radius, Material mat) => Rigid(bone, Ball(), mat, local, Quaternion.identity, Vector3.one * radius * 2f);

    public static void Box(string bone, Vector3 local, Quaternion rot, Vector3 size, Material mat) => Rigid(bone, Box(), mat, local, rot, size);

    /// <summary>A copy's own skin (how it's drawn: the twin's is shadow only in a level).</summary>
    private static Renderer ReferenceRenderer(Transform root)
    {
        var skin = Skin;
        foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (skin != null && r.name == skin.Name)
                return r;
        return null;
    }

    private static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        for (var i = 0; i < t.childCount; i++)
        {
            var f = FindDeep(t.GetChild(i), name);
            if (f != null) return f;
        }

        return null;
    }

    public static Mesh Ball() => _ball != null ? _ball : _ball = Primitive(PrimitiveType.Sphere);
    public static Mesh Rod() => _rod != null ? _rod : _rod = Primitive(PrimitiveType.Capsule);
    public static Mesh Box() => _box != null ? _box : _box = Primitive(PrimitiveType.Cube);

    private static Mesh Primitive(PrimitiveType t)
    {
        var go = GameObject.CreatePrimitive(t);
        var m = go.GetComponent<MeshFilter>().sharedMesh;
        Object.Destroy(go);
        return m;
    }

    // ------------------------------------------------------------------ undressing

    public static void Undress()
    {
        foreach (var go in Pieces)
            if (go != null)
                Object.Destroy(go);
        Pieces.Clear();
        foreach (var kv in Original)
        {
            if (kv.Key == null) continue;
            kv.Key.sharedMaterials = kv.Value.mats;
            kv.Key.enabled = kv.Value.enabled;
        }

        Original.Clear();
    }

    public static int PieceCount => Pieces.Count;
}
