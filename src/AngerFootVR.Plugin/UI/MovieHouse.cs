using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace AngerFootVR.UI;

/// <summary>
/// The cinematic area: a run-down neon movie house in Anger Foot's city, built in code (like InkVR's theatre) from the
/// game's own textures and fonts, far below every level. The title menu, the pause menu and the level select play on
/// its big screen; you stand in its aisle as your own body, with a lobby mirror on your left. Built once, the first
/// time the title screen is up (the game's art is loaded then), and kept for the whole session.
/// </summary>
internal static class MovieHouse
{
    /// <summary>Where it stands (below and away from every level).</summary>
    public static readonly Vector3 Origin = new Vector3(0f, -5000f, 0f);

    /// <summary>Your spot: the aisle at the back of the seats, facing the screen (+z).</summary>
    public static readonly Vector3 Standing = Origin + new Vector3(0f, 0f, -3.2f);

    public const float ScreenWidth = 11f, ScreenHeight = 11f * 9f / 16f;

    public static Transform Root { get; private set; }
    public static Renderer Screen { get; private set; }

    /// <summary>The projection under the menu: the game's own picture.</summary>
    public static Renderer Feed { get; private set; }

    public static void ShowFeed(Texture feed)
    {
        if (Feed != null) Feed.sharedMaterial.SetTexture("_EmissionMap", feed);
    }
    public static readonly List<Light> Lights = new List<Light>();

    private static readonly Dictionary<Material, List<CombineInstance>> Parts = new Dictionary<Material, List<CombineInstance>>();
    private static Mesh _cube, _quad;
    private static int _layer;

    public static bool Build(int layer)
    {
        if (Root != null) return true;
        _layer = layer;
        var go = new GameObject("AngerFootVR Movie House") { layer = layer };
        Object.DontDestroyOnLoad(go);
        Root = go.transform;
        Root.position = Origin;
        _cube = Primitive(PrimitiveType.Cube);
        _quad = Primitive(PrimitiveType.Quad);
        Parts.Clear();

        var brick = Lit(Tex("T_Brickwall_AlbedoSpecular", "BricksRow_basecolor", "Brick_03"), new Color(0.42f, 0.36f, 0.4f), new Vector2(5f, 2.5f));
        var carpet = Lit(Tex("T_NightClub_Carpet_Black_v01", "WorldSpace_ShiningCarpet", "Carpet_04"), new Color(0.55f, 0.35f, 0.55f), new Vector2(6f, 9f));
        var ceiling = Lit(null, new Color(0.06f, 0.05f, 0.08f), Vector2.one);
        var velvet = Lit(null, new Color(0.5f, 0.04f, 0.08f), Vector2.one);
        var curtain = Lit(null, new Color(0.42f, 0.02f, 0.06f), Vector2.one);
        var wood = Lit(null, new Color(0.16f, 0.09f, 0.06f), Vector2.one);
        var metal = Lit(null, new Color(0.12f, 0.12f, 0.14f), Vector2.one);
        var black = Unlit(Color.black);
        var neonPink = Unlit(new Color(1f, 0.16f, 0.62f));
        var neonCyan = Unlit(new Color(0.15f, 0.95f, 1f));
        var neonYellow = Unlit(new Color(1f, 0.85f, 0.2f));

        // the room: 14 m wide, 22 m long, 7 m high, the screen at the far end (+z)
        Box(carpet, new Vector3(0f, -0.05f, 5f), new Vector3(14f, 0.1f, 22f));
        Box(ceiling, new Vector3(0f, 7.05f, 5f), new Vector3(14f, 0.1f, 22f));
        Box(brick, new Vector3(-7.05f, 3.5f, 5f), new Vector3(0.1f, 7f, 22f));
        Box(brick, new Vector3(7.05f, 3.5f, 5f), new Vector3(0.1f, 7f, 22f));
        Box(brick, new Vector3(0f, 3.5f, -6.05f), new Vector3(14f, 7f, 0.1f));
        Box(brick, new Vector3(0f, 3.5f, 16.05f), new Vector3(14f, 7f, 0.1f));

        // the stage and its curtains, the screen in a neon frame
        Box(wood, new Vector3(0f, 0.35f, 15.2f), new Vector3(13.8f, 0.7f, 1.6f));
        Box(neonPink, new Vector3(0f, 0.71f, 14.42f), new Vector3(13.8f, 0.03f, 0.03f));
        var sc = new Vector3(0f, 0.9f + ScreenHeight * 0.5f, 15.9f);
        Box(black, sc + new Vector3(0f, 0f, 0.06f), new Vector3(ScreenWidth + 0.3f, ScreenHeight + 0.3f, 0.05f));
        foreach (var (p, s) in Frame(sc, ScreenWidth + 0.35f, ScreenHeight + 0.35f, 0.07f)) Box(neonPink, p, s);
        for (var i = 0; i < 9; i++)
        {
            var d = (i % 2 == 0 ? 0.05f : -0.05f);
            Box(curtain, new Vector3(-6.75f + i * 0.13f, 3.6f, 15.75f + d), new Vector3(0.16f, 6.8f, 0.2f));
            Box(curtain, new Vector3(6.75f - i * 0.13f, 3.6f, 15.75f + d), new Vector3(0.16f, 6.8f, 0.2f));
        }

        for (var i = 0; i < 46; i++) Box(curtain, new Vector3(-6.9f + i * 0.3f, 6.75f, 15.65f + (i % 2 == 0 ? 0.04f : -0.04f)), new Vector3(0.32f, 0.6f, 0.18f)); // the pelmet

        // seats: six rows either side of the centre aisle, rising towards the back
        for (var row = 0; row < 6; row++)
        {
            var z = 11.5f - row * 1.55f;
            var rise = row * 0.12f;
            Box(wood, new Vector3(-3.6f, rise * 0.5f, z), new Vector3(5.4f, rise + 0.001f, 1.5f));
            Box(wood, new Vector3(3.6f, rise * 0.5f, z), new Vector3(5.4f, rise + 0.001f, 1.5f));
            for (var seat = 0; seat < 6; seat++)
            {
                foreach (var side in new[] { -1f, 1f })
                {
                    var x = side * (1.25f + seat * 0.85f);
                    Box(velvet, new Vector3(x, rise + 0.45f, z), new Vector3(0.62f, 0.14f, 0.55f));
                    Box(velvet, new Vector3(x, rise + 0.85f, z - 0.3f), new Vector3(0.62f, 0.7f, 0.12f)); // the back behind you: seats face the screen
                    Box(metal, new Vector3(x + 0.36f, rise + 0.6f, z - 0.05f), new Vector3(0.08f, 0.06f, 0.5f));
                }
            }
        }

        // neon trims along the walls, sconces, the exit sign
        Box(neonCyan, new Vector3(-6.98f, 5.6f, 5f), new Vector3(0.03f, 0.05f, 21.5f));
        Box(neonPink, new Vector3(6.98f, 5.6f, 5f), new Vector3(0.03f, 0.05f, 21.5f));
        for (var i = 0; i < 4; i++)
        {
            // (the left wall's second and third would touch the ANGER FOOT VR sign: they stand clear of it)
            Box(neonYellow, new Vector3(-6.95f, 3.2f, i == 1 ? 1.9f : i == 2 ? 8.7f : -1f + i * 4.5f), new Vector3(0.05f, 0.5f, 0.18f));
            Box(neonYellow, new Vector3(6.95f, 3.2f, -1f + i * 4.5f), new Vector3(0.05f, 0.5f, 0.18f));
        }

        Combine();

        // the screen (the game's UI plays on it), the logo over it, posters and graffiti from the game's art
        // the screen: the game's own picture (the projector feed) with its menu over it
        var feedMat = new Material(Shader.Find("Standard")) { color = Color.black };
        feedMat.EnableKeyword("_EMISSION");
        feedMat.SetColor("_EmissionColor", Color.white);
        feedMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        feedMat.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        Feed = Quad("Projection", sc, Quaternion.identity, new Vector2(ScreenWidth, ScreenHeight), feedMat).GetComponent<Renderer>();
        Screen = Quad("Screen", sc + new Vector3(0f, 0f, -0.01f), Quaternion.identity, new Vector2(ScreenWidth, ScreenHeight), UiMaterial(null, 3000)).GetComponent<Renderer>();
        var logo = Tex("Anger Foot Logo Pink 4x");
        var white = Tex("Anger Foot Logo White 4x");
        var graffiti = Tex("T_Graffiti_Neon_basecolor");
        for (var i = 0; i < 3; i++)
        {
            var z = 0.5f + i * 4.5f;
            if (graffiti != null) Quad("Graffiti", new Vector3(6.94f, 2.2f, z + 2.2f), Quaternion.Euler(0f, 90f, 0f), new Vector2(2.2f, 2.2f), UiMaterial(graffiti, 2500));
        }

        // the left wall: the ANGER FOOT VR sign, and the mod's credit under it
        var leftWall = Quaternion.Euler(0f, -90f, 0f);
        if (logo != null)
            Quad("Wall Logo", new Vector3(-6.93f, 4.1f, 4.3f), leftWall, new Vector2(2.2f * logo.width / logo.height, 2.2f), UiMaterial(logo, 3001));
        Text("VR", new Vector3(-6.92f, 3.85f, 6.6f), leftWall, 1.7f, new Color(0.15f, 0.95f, 1f));
        Text("VR MOD BY STRIKEZONEVR", new Vector3(-6.92f, 2.45f, 5.0f), leftWall, 0.42f, new Color(1f, 0.85f, 0.25f));
        if (white != null)
            foreach (var z in new[] { 10.4f, 13.2f })
            {
                Box2(metal, new Vector3(-6.97f, 2.3f, z), new Vector3(0.04f, 1.75f, 1.45f));
                Quad("Poster", new Vector3(-6.94f, 2.3f, z), leftWall, new Vector2(1.3f, 1.3f * white.height / white.width), UiMaterial(white, 2500));
            }

        Text("EXIT", new Vector3(0f, 5.2f, -5.98f), Quaternion.Euler(0f, 180f, 0f), 0.5f, new Color(1f, 0.25f, 0.25f));

        // the lobby mirror: a free-standing one two metres to your left, facing you across the aisle
        var mirrorAt = new Vector3(-2.1f, 1.3f, Standing.z - Origin.z);
        Box2(metal, mirrorAt + new Vector3(-0.06f, 0f, 0f), new Vector3(0.06f, 2.5f, 1.25f));
        Box2(metal, new Vector3(mirrorAt.x - 0.06f, 0.03f, mirrorAt.z), new Vector3(0.6f, 0.06f, 1.0f));
        // its neon frame (the glass faces +x: the bars run along z and y)
        Box2(neonCyanSolid(), mirrorAt + new Vector3(-0.02f, 1.17f, 0f), new Vector3(0.03f, 0.04f, 1.19f));
        Box2(neonCyanSolid(), mirrorAt + new Vector3(-0.02f, -1.17f, 0f), new Vector3(0.03f, 0.04f, 1.19f));
        Box2(neonCyanSolid(), mirrorAt + new Vector3(-0.02f, 0f, 0.57f), new Vector3(0.03f, 2.38f, 0.04f));
        Box2(neonCyanSolid(), mirrorAt + new Vector3(-0.02f, 0f, -0.57f), new Vector3(0.03f, 2.38f, 0.04f));
        new Mirror(Root, mirrorAt, Quaternion.Euler(0f, 90f, 0f), 1.1f, 2.3f, Color.white, layer);

        // light: neon colours from the sides, the screen's glow, a warm light at the back
        AddLight(new Vector3(-5.5f, 5.5f, 4f), new Color(1f, 0.25f, 0.7f), 2.2f, 13f);
        AddLight(new Vector3(5.5f, 5.5f, 4f), new Color(0.2f, 0.9f, 1f), 2.2f, 13f);
        AddLight(new Vector3(0f, 3.5f, 12.5f), new Color(0.7f, 0.75f, 1f), 1.6f, 11f);
        AddLight(new Vector3(0f, 5.5f, -3.5f), new Color(1f, 0.75f, 0.5f), 1.4f, 9f);
        AddLight(new Vector3(-1.0f, 3.2f, Standing.z - Origin.z + 0.6f), new Color(1f, 0.97f, 0.92f), 1.3f, 5f); // the lobby light: skin keeps its colour
        Plugin.L.LogInfo("Movie house built");
        return true;
    }

    private static Material _neonCyanSolid;
    private static Material neonCyanSolid() => _neonCyanSolid ??= Unlit(new Color(0.15f, 0.95f, 1f));

    private static Mesh Primitive(PrimitiveType type)
    {
        var tmp = GameObject.CreatePrimitive(type);
        var mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
        Object.Destroy(tmp);
        return mesh;
    }

    private static Texture2D Tex(params string[] names)
    {
        var all = Resources.FindObjectsOfTypeAll<Texture2D>();
        foreach (var n in names)
        {
            var t = all.FirstOrDefault(x => x != null && x.name == n);
            if (t != null)
            {
                t.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                return t;
            }
        }

        return null;
    }

    private static Material Lit(Texture2D tex, Color color, Vector2 tiling)
    {
        var m = new Material(Shader.Find("Standard")) { color = color };
        if (tex != null)
        {
            m.mainTexture = tex;
            m.mainTextureScale = tiling;
        }

        m.SetFloat("_Glossiness", 0.15f);
        m.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return m;
    }

    private static Material Unlit(Color color)
    {
        var m = new Material(Shader.Find("Unlit/Color")) { color = color };
        m.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return m;
    }

    private static Material UiMaterial(Texture tex, int queue)
    {
        var m = new Material(Shader.Find("UI/Default")) { mainTexture = tex, renderQueue = queue };
        m.SetInt("unity_GUIZTestMode", (int)CompareFunction.LessEqual);
        m.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return m;
    }

    /// <summary>A box into the combined mesh of its material.</summary>
    private static void Box(Material m, Vector3 pos, Vector3 size)
    {
        if (!Parts.TryGetValue(m, out var list)) Parts[m] = list = new List<CombineInstance>();
        list.Add(new CombineInstance { mesh = _cube, transform = Matrix4x4.TRS(pos, Quaternion.identity, size) });
    }

    /// <summary>A box of its own (built after the combined ones).</summary>
    private static void Box2(Material m, Vector3 pos, Vector3 size)
    {
        var go = new GameObject("Part") { layer = _layer };
        go.transform.SetParent(Root, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        go.AddComponent<MeshFilter>().sharedMesh = _cube;
        go.AddComponent<MeshRenderer>().sharedMaterial = m;
    }

    private static IEnumerable<(Vector3, Vector3)> Frame(Vector3 c, float w, float h, float t)
    {
        yield return (c + new Vector3(0f, h * 0.5f, 0f), new Vector3(w + t, t, t));
        yield return (c + new Vector3(0f, -h * 0.5f, 0f), new Vector3(w + t, t, t));
        yield return (c + new Vector3(-w * 0.5f, 0f, 0f), new Vector3(t, h, t));
        yield return (c + new Vector3(w * 0.5f, 0f, 0f), new Vector3(t, h, t));
    }

    private static void Combine()
    {
        foreach (var kv in Parts)
        {
            var mesh = new Mesh { name = "AngerFootVR Movie House " + kv.Key.name, indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(kv.Value.ToArray(), true, true);
            mesh.RecalculateBounds();
            var go = new GameObject("Built") { layer = _layer };
            go.transform.SetParent(Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = kv.Key;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        Parts.Clear();
    }

    private static GameObject Quad(string name, Vector3 pos, Quaternion rot, Vector2 size, Material m)
    {
        var go = new GameObject(name) { layer = _layer };
        go.transform.SetParent(Root, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot; // a quad shows its face to the -z side: identity faces you from the aisle
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        go.AddComponent<MeshFilter>().sharedMesh = _quad;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    private static void Text(string text, Vector3 pos, Quaternion rot, float size, Color color)
    {
        // the neon glow is a second copy of the letters just behind, thicker and faint (kept inside each letter's own
        // quad: a wide underlay spilled past it and showed the quad's square)
        Letters(text, pos + rot * new Vector3(0f, 0f, 0.01f), rot, size, new Color(color.r, color.g, color.b, 0.35f), 0.3f);
        Letters(text, pos, rot, size, color, 0f);
    }

    private static void Letters(string text, Vector3 pos, Quaternion rot, float size, Color color, float dilate)
    {
        var font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f.name == "ANGERFONT-HEADER SDF")
                   ?? Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault();
        var go = new GameObject("Sign " + text) { layer = _layer };
        go.transform.SetParent(Root, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot; // text reads from the -z side, like a quad
        var tmp = go.AddComponent<TextMeshPro>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size * 10f;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.rectTransform.sizeDelta = new Vector2(text.Length * size * 1.2f + 1f, size * 1.5f);
        tmp.enableWordWrapping = false;
        if (dilate > 0f) tmp.fontMaterial.SetFloat("_FaceDilate", dilate);
    }

    private static void AddLight(Vector3 pos, Color color, float intensity, float range)
    {
        var go = new GameObject("Light") { layer = _layer };
        go.transform.SetParent(Root, false);
        go.transform.localPosition = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
        l.cullingMask = 1 << _layer;
        l.renderMode = LightRenderMode.ForcePixel;
        Lights.Add(l);
    }
}
