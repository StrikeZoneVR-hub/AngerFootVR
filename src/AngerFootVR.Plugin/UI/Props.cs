using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace AngerFootVR.UI;

/// <summary>
/// The mod's own models (props.afvp, made in Blender by tools/blender/build_props.py and shipped with the mod): the
/// movie house's seats, curtains, proscenium, booth and doors, and the rage room's breakables with their pieces. Read
/// once per session. Each model's vertex colours carry its baked ambient occlusion, so they're drawn with Unity's
/// Particles/Standard Surface (lit like Standard, multiplied by vertex colour); each part's material is made here from
/// its name (velvet, chrome, gold, glass...).
/// </summary>
internal static class Props
{
    public sealed class Model
    {
        public string Name, Group;
        public Vector3 Offset; // a piece's middle, from its whole model's origin
        public Mesh Mesh;
        public string[] Materials;
    }

    private static readonly Dictionary<string, Model> Models = new Dictionary<string, Model>();
    private static readonly Dictionary<string, List<Model>> Pieces = new Dictionary<string, List<Model>>();
    private static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
    private static bool _loaded;

    public static string File => Path.Combine(Path.Combine(Paths.PluginPath, "AngerFootVR"), "props.afvp");

    public static bool Load()
    {
        if (_loaded) return Models.Count > 0;
        _loaded = true;
        try
        {
            if (!System.IO.File.Exists(File))
            {
                Plugin.L.LogWarning("Models missing: " + File);
                return false;
            }

            using (var r = new BinaryReader(System.IO.File.OpenRead(File)))
            {
                if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "AFVP" || r.ReadUInt32() != 1) throw new InvalidDataException("not a props file (version 1)");
                var count = (int)r.ReadUInt32();
                for (var m = 0; m < count; m++)
                {
                    var model = new Model { Name = Str(r), Group = Str(r), Offset = V3(r) };
                    var n = (int)r.ReadUInt32();
                    var pos = new Vector3[n];
                    var nrm = new Vector3[n];
                    var uv = new Vector2[n];
                    var col = new Color32[n];
                    for (var i = 0; i < n; i++)
                    {
                        pos[i] = V3(r);
                        nrm[i] = V3(r);
                        uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
                        col[i] = new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
                    }

                    var mesh = new Mesh { name = "AngerFootVR " + model.Name };
                    if (n > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    mesh.vertices = pos;
                    mesh.normals = nrm;
                    mesh.uv = uv;
                    mesh.colors32 = col;
                    var subs = (int)r.ReadUInt32();
                    mesh.subMeshCount = subs;
                    model.Materials = new string[subs];
                    for (var s = 0; s < subs; s++)
                    {
                        model.Materials[s] = Str(r);
                        var k = (int)r.ReadUInt32();
                        var idx = new int[k];
                        for (var i = 0; i < k; i++) idx[i] = (int)r.ReadUInt32();
                        mesh.SetTriangles(idx, s);
                    }

                    mesh.RecalculateBounds();
                    mesh.RecalculateTangents();
                    UnityEngine.Object.DontDestroyOnLoad(mesh);
                    model.Mesh = mesh;
                    Models[model.Name] = model;
                    if (model.Group.Length > 0)
                    {
                        if (!Pieces.TryGetValue(model.Group, out var list)) Pieces[model.Group] = list = new List<Model>();
                        list.Add(model);
                    }
                }
            }

            Plugin.L.LogInfo($"Models: {Models.Count} read ({Pieces.Count} breakables)");
            return true;
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("Models couldn't be read: " + e.Message);
            Models.Clear();
            return false;
        }
    }

    private static string Str(BinaryReader r) => Encoding.UTF8.GetString(r.ReadBytes(r.ReadUInt16()));
    private static Vector3 V3(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

    public static Model Get(string name) => Models.TryGetValue(name, out var m) ? m : null;

    public static List<Model> PiecesOf(string name) => Pieces.TryGetValue(name, out var l) ? l : new List<Model>();

    /// <summary>A model as an object (renderer and its materials), under a parent, on a layer.</summary>
    public static GameObject Spawn(string name, Transform parent, Vector3 localPos, Quaternion localRot, int layer, float scale = 1f)
    {
        var m = Get(name);
        if (m == null) return null;
        var go = new GameObject("AngerFootVR " + name) { layer = layer };
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        go.transform.localScale = Vector3.one * scale;
        go.AddComponent<MeshFilter>().sharedMesh = m.Mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterials = MaterialsOf(m);
        return go;
    }

    public static Material[] MaterialsOf(Model m)
    {
        var mats = new Material[m.Materials.Length];
        for (var i = 0; i < mats.Length; i++) mats[i] = Mat(m.Materials[i]);
        return mats;
    }

    /// <summary>A part's look by its name.</summary>
    public static Material Mat(string name)
    {
        if (Mats.TryGetValue(name, out var m) && m != null) return m;
        // (colour, smoothness, metallic, glow)
        (Color c, float s, float metal, Color glow) look = name switch
        {
            "velvet" => (new Color(0.55f, 0.05f, 0.1f), 0.25f, 0f, Color.black),
            "velvet_curtain" => (new Color(0.5f, 0.03f, 0.08f), 0.22f, 0f, Color.black),
            "seat_plastic" => (new Color(0.08f, 0.07f, 0.09f), 0.45f, 0f, Color.black),
            "metal_dark" => (new Color(0.16f, 0.16f, 0.18f), 0.55f, 0.6f, Color.black),
            "armrest" => (new Color(0.22f, 0.11f, 0.07f), 0.55f, 0f, Color.black),
            "chrome" => (new Color(0.82f, 0.82f, 0.85f), 0.85f, 1f, Color.black),
            "gold" => (new Color(0.95f, 0.7f, 0.25f), 0.6f, 1f, Color.black),
            "deco_dark" => (new Color(0.11f, 0.06f, 0.13f), 0.5f, 0.2f, Color.black),
            "deco_gold" => (new Color(0.9f, 0.66f, 0.26f), 0.55f, 0.9f, Color.black),
            "booth_inside" => (new Color(0.02f, 0.02f, 0.03f), 0.1f, 0f, Color.black),
            "lens_glow" => (new Color(1f, 0.95f, 0.85f), 0.9f, 0f, new Color(2.5f, 2.3f, 2f)),
            "door_leather" => (new Color(0.45f, 0.05f, 0.08f), 0.45f, 0f, Color.black),
            "door_frame" => (new Color(0.09f, 0.05f, 0.11f), 0.5f, 0.2f, Color.black),
            "glass_dark" => (new Color(0.05f, 0.07f, 0.1f), 0.92f, 0f, Color.black),
            "step_glow" => (new Color(1f, 0.6f, 0.25f), 0.5f, 0f, new Color(2f, 1.1f, 0.4f)),
            "sconce_glass" => (new Color(1f, 0.8f, 0.6f), 0.6f, 0f, new Color(1.6f, 1.1f, 0.7f)),
            // the rage room's things
            "wood_crate" => (new Color(0.66f, 0.48f, 0.3f), 0.25f, 0f, Color.black),
            "wood_crate_dark" => (new Color(0.45f, 0.31f, 0.19f), 0.25f, 0f, Color.black),
            "wood_light" => (new Color(0.78f, 0.57f, 0.36f), 0.35f, 0f, Color.black),
            "wood_dark" => (new Color(0.38f, 0.23f, 0.13f), 0.35f, 0f, Color.black),
            "tv_plastic" => (new Color(0.36f, 0.22f, 0.12f), 0.55f, 0f, Color.black),
            "jukebox_glow" => (new Color(1f, 0.6f, 0.25f), 0.8f, 0f, new Color(1.9f, 0.85f, 0.35f)),
            "glass_aqua" => (new Color(0.75f, 0.95f, 1f, 0.22f), 0.95f, 0f, Color.black),
            "water_blue" => (new Color(0.15f, 0.55f, 0.9f, 0.42f), 0.9f, 0f, new Color(0.02f, 0.08f, 0.16f)),
            "gravel" => (new Color(0.6f, 0.5f, 0.42f), 0.2f, 0f, Color.black),
            "fish_orange" => (new Color(1f, 0.5f, 0.08f), 0.6f, 0f, new Color(0.15f, 0.05f, 0f)),
            "tv_screen" => (new Color(0.12f, 0.16f, 0.14f), 0.95f, 0f, new Color(0.02f, 0.05f, 0.04f)),
            "tv_knob" => (new Color(0.7f, 0.7f, 0.72f), 0.6f, 0.8f, Color.black),
            "tv_inside" => (new Color(0.18f, 0.32f, 0.18f), 0.3f, 0.2f, Color.black),
            "glass_shard" => (new Color(0.65f, 0.85f, 0.8f), 0.95f, 0f, Color.black),
            "glass_green" => (new Color(0.12f, 0.5f, 0.18f), 0.92f, 0f, Color.black),
            "glass_brown" => (new Color(0.4f, 0.17f, 0.04f), 0.92f, 0f, Color.black),
            "bottle_label" => (new Color(0.92f, 0.86f, 0.68f), 0.3f, 0f, Color.black),
            "china_blue" => (new Color(0.16f, 0.26f, 0.78f), 0.88f, 0f, Color.black),
            "china_white" => (new Color(0.96f, 0.95f, 0.92f), 0.88f, 0f, Color.black),
            "china_inside" => (new Color(0.92f, 0.9f, 0.86f), 0.2f, 0f, Color.black),
            "cardboard" => (new Color(0.72f, 0.54f, 0.33f), 0.15f, 0f, Color.black),
            "tape" => (new Color(0.85f, 0.75f, 0.52f), 0.6f, 0f, Color.black),
            "gnome_coat" => (new Color(0.16f, 0.32f, 0.85f), 0.5f, 0f, Color.black),
            "gnome_skin" => (new Color(0.96f, 0.76f, 0.62f), 0.5f, 0f, Color.black),
            "gnome_beard" => (new Color(0.96f, 0.96f, 0.96f), 0.4f, 0f, Color.black),
            "gnome_hat" => (new Color(0.88f, 0.1f, 0.1f), 0.5f, 0f, Color.black),
            "plaster" => (new Color(0.92f, 0.9f, 0.84f), 0.1f, 0f, Color.black),
            "concrete" => (new Color(0.42f, 0.42f, 0.44f), 0.15f, 0f, Color.black),
            // 1.2: the rage room's new things, the lobby, the arcade
            "vending_red" => (new Color(0.72f, 0.05f, 0.07f), 0.62f, 0.1f, Color.black),
            "vending_logo" => (new Color(1f, 0.95f, 0.9f), 0.7f, 0f, new Color(1.7f, 1.5f, 1.3f)),
            "can_red" => (new Color(0.82f, 0.08f, 0.06f), 0.75f, 0.6f, Color.black),
            "can_blue" => (new Color(0.08f, 0.3f, 0.88f), 0.75f, 0.6f, Color.black),
            "can_green" => (new Color(0.1f, 0.65f, 0.22f), 0.75f, 0.6f, Color.black),
            "plastic_white" => (new Color(0.92f, 0.92f, 0.9f), 0.5f, 0f, Color.black),
            "plastic_red" => (new Color(0.82f, 0.09f, 0.1f), 0.55f, 0f, Color.black),
            "plastic_orange" => (new Color(1f, 0.45f, 0.05f), 0.55f, 0f, new Color(0.1f, 0.03f, 0f)),
            "water_jug" => (new Color(0.4f, 0.7f, 1f, 0.32f), 0.95f, 0f, Color.black),
            "pinata_pink" => (new Color(1f, 0.32f, 0.66f), 0.12f, 0f, new Color(0.08f, 0.01f, 0.04f)),
            "pinata_yellow" => (new Color(1f, 0.85f, 0.2f), 0.12f, 0f, Color.black),
            "pinata_cyan" => (new Color(0.2f, 0.88f, 1f), 0.12f, 0f, Color.black),
            "candy_red" => (new Color(0.95f, 0.12f, 0.2f), 0.85f, 0f, Color.black),
            "candy_yellow" => (new Color(1f, 0.85f, 0.15f), 0.85f, 0f, Color.black),
            "candy_blue" => (new Color(0.2f, 0.45f, 1f), 0.85f, 0f, Color.black),
            "barrel_red" => (new Color(0.72f, 0.08f, 0.05f), 0.45f, 0.35f, Color.black),
            "hazard_yellow" => (new Color(1f, 0.78f, 0.08f), 0.5f, 0f, Color.black),
            "steel" => (new Color(0.62f, 0.62f, 0.64f), 0.65f, 1f, Color.black),
            // (metallic but not wholly: lit by the lamps as well as showing the room, so it never goes black in a dark one)
            "endo_chrome" => (new Color(0.8f, 0.82f, 0.86f), 0.86f, 0.72f, Color.black),
            "endo_gold" => (new Color(0.98f, 0.72f, 0.28f), 0.84f, 0.75f, Color.black),
            "endo_dark" => (new Color(0.2f, 0.2f, 0.23f), 0.6f, 0.6f, Color.black),
            "endo_teeth" => (new Color(0.92f, 0.93f, 0.95f), 0.75f, 0.6f, Color.black),
            "endo_eye" => (new Color(1f, 0.12f, 0.06f), 0.9f, 0f, new Color(5f, 0.3f, 0.15f)),
            "steel_dark" => (new Color(0.25f, 0.25f, 0.27f), 0.5f, 0.9f, Color.black),
            "rubber_black" => (new Color(0.035f, 0.035f, 0.04f), 0.3f, 0f, Color.black),
            "button_red" => (new Color(0.95f, 0.08f, 0.08f), 0.8f, 0f, new Color(1.4f, 0.05f, 0.05f)),
            "lamp" => (new Color(0.35f, 0.1f, 0.06f), 0.8f, 0f, Color.black),
            "lamp_on" => (new Color(1f, 0.8f, 0.4f), 0.8f, 0f, new Color(3f, 1.8f, 0.6f)),
            "popcorn" => (new Color(1f, 0.93f, 0.66f), 0.2f, 0f, new Color(0.08f, 0.06f, 0.02f)),
            "popcorn_red" => (new Color(0.85f, 0.08f, 0.1f), 0.6f, 0f, Color.black),
            "stripe_white" => (new Color(0.95f, 0.95f, 0.92f), 0.5f, 0f, Color.black),
            "stripe_red" => (new Color(0.85f, 0.08f, 0.1f), 0.5f, 0f, Color.black),
            "cup_white" => (new Color(0.96f, 0.96f, 0.94f), 0.4f, 0f, Color.black),
            "brass" => (new Color(0.86f, 0.66f, 0.3f), 0.65f, 1f, Color.black),
            "goon_pink" => (new Color(1f, 0.36f, 0.72f), 0.25f, 0f, Color.black),
            "goon_green" => (new Color(0.2f, 0.75f, 0.36f), 0.25f, 0f, Color.black),
            "goon_skin" => (new Color(0.95f, 0.76f, 0.62f), 0.3f, 0f, Color.black),
            "granny_purple" => (new Color(0.55f, 0.32f, 0.78f), 0.2f, 0f, Color.black),
            "jeans_blue" => (new Color(0.22f, 0.3f, 0.55f), 0.2f, 0f, Color.black),
            "hair_white" => (new Color(0.95f, 0.95f, 0.96f), 0.3f, 0f, Color.black),
            "hair_black" => (new Color(0.07f, 0.05f, 0.05f), 0.3f, 0f, Color.black),
            "pizza_cheese" => (new Color(1f, 0.8f, 0.28f), 0.35f, 0f, Color.black),
            "pizza_crust" => (new Color(0.82f, 0.56f, 0.26f), 0.2f, 0f, Color.black),
            "pepperoni" => (new Color(0.72f, 0.12f, 0.08f), 0.45f, 0f, Color.black),
            "ball_red" => (new Color(0.88f, 0.1f, 0.1f), 0.45f, 0f, Color.black),
            "felt_green" => (new Color(0.1f, 0.42f, 0.2f), 0.1f, 0f, Color.black),
            "lane_wood" => (new Color(0.86f, 0.66f, 0.42f), 0.88f, 0f, Color.black),
            _ => (new Color(0.6f, 0.6f, 0.6f), 0.3f, 0f, Color.black),
        };

        if (name.StartsWith("neon_"))
        {
            m = new Material(Shader.Find("Unlit/Color"))
            {
                color = name == "neon_pink" ? new Color(1f, 0.16f, 0.62f) : name == "neon_cyan" ? new Color(0.15f, 0.95f, 1f) : new Color(1f, 0.85f, 0.2f)
            };
        }
        else
        {
            m = new Material(Shader.Find("Particles/Standard Surface") ?? Shader.Find("Standard")) { name = "AngerFootVR " + name, color = look.c };
            m.SetFloat("_Glossiness", look.s);
            m.SetFloat("_Metallic", look.metal);
            if (look.glow.maxColorComponent > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", look.glow);
            }

            m.renderQueue = 2000;
            if (look.c.a < 1f)
            {
                // see-through (the fish tank's glass and water): blended, drawn after the solid things
                m.SetFloat("_Mode", 2f);
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.DisableKeyword("_ALPHATEST_ON");
                m.EnableKeyword("_ALPHABLEND_ON");
                m.renderQueue = 3000;
            }
        }

        m.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        Mats[name] = m;
        return m;
    }
}
