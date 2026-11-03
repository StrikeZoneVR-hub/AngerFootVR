using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace AngerFootVR.UI;

/// <summary>
/// The cinematic area: a neon picture palace in Anger Foot's city, far below every level. Its furniture is the mod's
/// own Blender models (props.afvp: cinema seats on raked rows, velvet curtains and valance, the art deco proscenium,
/// the projection booth, deco sconces, padded lobby doors), its walls and signs the game's own textures and fonts. The
/// title menu, the pause menu and the level select play on its big screen, the projector's beam (dust drifting in it)
/// and a light in front of it taking the picture's colours; bulbs chase along the stage. You stand in the aisle as
/// your own body, a mirror on your left; kick the padded doors at the back and they burst open on the rage room.
/// Built once, the first time the title screen is up (the game's art is loaded then), and kept for the whole session.
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
        _sphere = Primitive(PrimitiveType.Sphere);
        Parts.Clear();
        var models = Props.Load();

        var brick = Lit(Tex("T_Brickwall_AlbedoSpecular", "BricksRow_basecolor", "Brick_03"), new Color(0.42f, 0.36f, 0.4f), new Vector2(5f, 3.5f));
        var carpet = Lit(Tex("T_NightClub_Carpet_Black_v01", "WorldSpace_ShiningCarpet", "Carpet_04"), new Color(0.55f, 0.35f, 0.55f), new Vector2(6f, 9f));
        var riserCarpet = Lit(Tex("T_NightClub_Carpet_Black_v01", "WorldSpace_ShiningCarpet", "Carpet_04"), new Color(0.42f, 0.22f, 0.4f), new Vector2(3f, 1f));
        var ceiling = Lit(null, new Color(0.05f, 0.04f, 0.07f), Vector2.one);
        var stageWood = Lit(null, new Color(0.14f, 0.08f, 0.05f), Vector2.one);
        var metal = Lit(null, new Color(0.12f, 0.12f, 0.14f), Vector2.one);
        var concrete = Lit(Tex("T_Concrete_basecolor", "Concrete_01", "T_Concrete"), new Color(0.45f, 0.45f, 0.47f), new Vector2(4f, 4f));
        var black = Unlit(Color.black);
        var neonPink = Unlit(new Color(1f, 0.16f, 0.62f));
        var neonCyan = Unlit(new Color(0.15f, 0.95f, 1f));

        // ---- the hall: 14 m wide, 22 m long, 11 m high (the proscenium's crown fits), the screen at the far end (+z) ----
        Box(carpet, new Vector3(0f, -0.05f, 5f), new Vector3(14f, 0.1f, 22f));
        Box(ceiling, new Vector3(0f, Height + 0.05f, 5f), new Vector3(14f, 0.1f, 22f));
        // the left wall, with the gun range's doorway cut in it near the back (z -5.4 to -3.6, 2.4 m high)
        Box(brick, new Vector3(-7.05f, Height / 2, (RangeDoorZ1 + 16f) / 2), new Vector3(0.1f, Height, 16f - RangeDoorZ1));
        Box(brick, new Vector3(-7.05f, Height / 2, (-6f + RangeDoorZ0) / 2), new Vector3(0.1f, Height, RangeDoorZ0 + 6f));
        Box(brick, new Vector3(-7.05f, (Height + 2.42f) / 2, (RangeDoorZ0 + RangeDoorZ1) / 2), new Vector3(0.1f, Height - 2.42f, RangeDoorZ1 - RangeDoorZ0));
        Box(brick, new Vector3(7.05f, Height / 2, 5f), new Vector3(0.1f, Height, 22f));
        Box(brick, new Vector3(0f, Height / 2, 16.25f), new Vector3(14f, Height, 0.1f));
        // the back wall, with the rage room's doorway cut in it (x 3.0 to 5.0, 2.4 m high)
        Box(brick, new Vector3((-7f + ArcadeDoorX0) / 2, Height / 2, -6.05f), new Vector3(ArcadeDoorX0 + 7f, Height, 0.1f));
        Box(brick, new Vector3((ArcadeDoorX1 - 1f) / 2, Height / 2, -6.05f), new Vector3(-1f - ArcadeDoorX1, Height, 0.1f));
        Box(brick, new Vector3((ArcadeDoorX0 + ArcadeDoorX1) / 2, (Height + 2.42f) / 2, -6.05f), new Vector3(ArcadeDoorX1 - ArcadeDoorX0, Height - 2.42f, 0.1f));
        Box(brick, new Vector3(2f, Height / 2, -6.05f), new Vector3(2f, Height, 0.1f));
        Box(brick, new Vector3(0f, (BoothHeight - 0.58f) / 2, -6.05f), new Vector3(2f, BoothHeight - 0.58f, 0.1f));
        Box(brick, new Vector3(0f, (BoothHeight + 0.58f + Height) / 2, -6.05f), new Vector3(2f, Height - BoothHeight - 0.58f, 0.1f));
        Box(brick, new Vector3(6.0f, Height / 2, -6.05f), new Vector3(2f, Height, 0.1f));
        Box(brick, new Vector3(4.0f, (Height + 2.42f) / 2, -6.05f), new Vector3(2f, Height - 2.42f, 0.1f));

        // ---- the stage, the screen and its frame, the curtains ----
        Box(stageWood, new Vector3(0f, StageHeight / 2, 14.6f), new Vector3(13.8f, StageHeight, 3.2f));
        Box(neonPink, new Vector3(0f, StageHeight + 0.01f, 13.02f), new Vector3(13.8f, 0.025f, 0.025f));
        var sc = new Vector3(0f, StageHeight + 0.95f + ScreenHeight * 0.5f, 15.9f);
        Box(black, sc + new Vector3(0f, 0f, 0.06f), new Vector3(ScreenWidth + 0.3f, ScreenHeight + 0.3f, 0.05f));
        if (models)
        {
            Model("proscenium", new Vector3(0f, StageHeight, 15.92f), 0f);
            Model("proscenium_neon", new Vector3(0f, StageHeight, 15.92f), 0f);
            _curtainL = Model("curtain", new Vector3(-6.55f, StageHeight, 15.75f), 0f)?.transform;
            _curtainR = Model("curtain", new Vector3(6.55f, StageHeight, 15.75f), 180f)?.transform;
            Model("valance", new Vector3(0f, StageHeight + 7.45f, 15.7f), 0f);
        }

        // ---- the seats: six rows a side of the centre aisle, on carpeted risers stepping up towards the back ----
        for (var row = 0; row < Rows; row++)
        {
            var z = FrontRowZ - row * RowDepth;
            var rise = row * RowRise;
            if (rise > 0f)
            {
                Box(riserCarpet, new Vector3(-(AisleHalf + SeatBlock / 2), rise / 2, z), new Vector3(SeatBlock, rise, RowDepth));
                Box(riserCarpet, new Vector3(AisleHalf + SeatBlock / 2, rise / 2, z), new Vector3(SeatBlock, rise, RowDepth));
            }

            for (var seat = 0; seat < SeatsPerSide; seat++)
                foreach (var side in new[] { -1f, 1f })
                {
                    var x = side * (AisleHalf + 0.38f + seat * SeatPitch);
                    if (models) Instance("seat", new Vector3(x, rise, z - 0.1f), 0f);
                    if (models && seat == SeatsPerSide - 1 && side > 0) Instance("seat_end", new Vector3(x, rise, z - 0.1f), 0f);
                    if (models && seat == 0 && side < 0) Instance("seat_end", new Vector3(x, rise, z - 0.1f), 0f);
                }

            // a step light at each block's aisle end, on its riser
            if (models)
                foreach (var side in new[] { -1f, 1f })
                    Instance("step_light", new Vector3(side * (AisleHalf + 0.05f), Mathf.Max(0.04f, rise - 0.06f), z - RowDepth / 2 - 0.03f), 180f);
        }

        // ---- the walls: neon trims, deco sconces, the booth ----
        Box(neonCyan, new Vector3(-6.98f, 6.4f, 5f), new Vector3(0.03f, 0.05f, 21.5f));
        Box(neonPink, new Vector3(6.98f, 6.4f, 5f), new Vector3(0.03f, 0.05f, 21.5f));
        if (models)
        {
            foreach (var z in new[] { -2.5f, 1.9f, 9.6f, 13.6f }) Instance("sconce", new Vector3(-6.98f, 3.6f, z), 90f);
            foreach (var z in new[] { -2.5f, 2.6f, 7.7f, 12.8f }) Instance("sconce", new Vector3(6.98f, 3.6f, z), -90f);
            Model("booth", new Vector3(0f, BoothHeight, -6.0f), 0f);
        }

        // ---- the lobby's doors to the rage room, its sign ----
        if (models)
        {
            Model("door_frame", new Vector3(DoorX, 0f, -6.0f), 0f);
            _doorL = Hinge("door_left", new Vector3(DoorX - 0.95f, 0f, -6.0f));
            _doorR = Hinge("door_right", new Vector3(DoorX + 0.95f, 0f, -6.0f));
        }

        BuildRageShell(concrete, brick, metal);
        BuildArcadeShell(carpet, brick, metal);
        BuildRangeShell(concrete, brick, metal);
        Combine();
        CombineInstances();

        // ---- the screen (the game's UI plays on it), the logo, posters and graffiti from the game's art ----
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
        for (var i = 0; i < 2; i++)
            if (graffiti != null) Quad("Graffiti", new Vector3(6.94f, 2.4f, 0.4f + i * 5.2f), Quaternion.Euler(0f, 90f, 0f), new Vector2(2.4f, 2.4f), UiMaterial(graffiti, 2500));

        // the left wall: the ANGER FOOT VR sign, and the mod's credit under it
        var leftWall = Quaternion.Euler(0f, -90f, 0f);
        if (logo != null)
            Quad("Wall Logo", new Vector3(-6.93f, 4.6f, 4.3f), leftWall, new Vector2(2.4f * logo.width / logo.height, 2.4f), UiMaterial(logo, 3001));
        Text("VR", new Vector3(-6.92f, 4.35f, 6.75f), leftWall, 1.8f, new Color(0.15f, 0.95f, 1f));
        Text("VR MOD BY STRIKEZONEVR", new Vector3(-6.92f, 2.85f, 5.0f), leftWall, 0.42f, new Color(1f, 0.85f, 0.25f));
        if (white != null)
            foreach (var z in new[] { 11.0f, 13.2f })
            {
                Box2(metal, new Vector3(-6.97f, 2.4f, z), new Vector3(0.04f, 1.75f, 1.45f));
                Quad("Poster", new Vector3(-6.94f, 2.4f, z), leftWall, new Vector2(1.3f, 1.3f * white.height / white.width), UiMaterial(white, 2500));
            }

        // the rage room's sign over its doors, flickering like an old one
        _rageSign = Text("RAGE ROOM", new Vector3(DoorX, 2.95f, -5.97f), Quaternion.Euler(0f, 180f, 0f), 0.62f, new Color(1f, 0.2f, 0.25f));
        Text("KICK TO ENTER", new Vector3(DoorX, 2.6f, -5.97f), Quaternion.Euler(0f, 180f, 0f), 0.24f, new Color(1f, 0.85f, 0.25f));

        // ---- the lobby mirror: a free-standing one two metres to your left, facing you across the aisle ----
        var mirrorAt = new Vector3(-2.1f, 1.3f, Standing.z - Origin.z);
        Box2(metal, mirrorAt + new Vector3(-0.06f, 0f, 0f), new Vector3(0.06f, 2.5f, 1.25f));
        Box2(metal, new Vector3(mirrorAt.x - 0.06f, 0.03f, mirrorAt.z), new Vector3(0.6f, 0.06f, 1.0f));
        Box2(neonCyanSolid(), mirrorAt + new Vector3(-0.02f, 1.17f, 0f), new Vector3(0.03f, 0.04f, 1.19f));
        Box2(neonCyanSolid(), mirrorAt + new Vector3(-0.02f, -1.17f, 0f), new Vector3(0.03f, 0.04f, 1.19f));
        Box2(neonCyanSolid(), mirrorAt + new Vector3(-0.02f, 0f, 0.57f), new Vector3(0.03f, 2.38f, 0.04f));
        Box2(neonCyanSolid(), mirrorAt + new Vector3(-0.02f, 0f, -0.57f), new Vector3(0.03f, 2.38f, 0.04f));
        new Mirror(Root, mirrorAt, Quaternion.Euler(0f, 90f, 0f), 1.1f, 2.3f, Color.white, layer);

        // ---- the stage's chasing bulbs, the projector's beam and its dust ----
        BuildBulbs();
        BuildBeam(sc);

        // ---- light: neon colours from the sides, the screen's glow, warm at the back, the lobby light, the sconces ----
        AddLight(new Vector3(-5.5f, 6.5f, 4f), new Color(1f, 0.25f, 0.7f), 2.2f, 14f);
        AddLight(new Vector3(5.5f, 6.5f, 4f), new Color(0.2f, 0.9f, 1f), 2.2f, 14f);
        AddLight(new Vector3(0f, 4.0f, 12.0f), new Color(0.7f, 0.75f, 1f), 1.6f, 12f); // the screen's (takes its colours: Animate)
        AddLight(new Vector3(0f, 6.0f, -3.5f), new Color(1f, 0.75f, 0.5f), 1.4f, 10f);
        AddLight(new Vector3(-1.0f, 3.2f, Standing.z - Origin.z + 0.6f), new Color(1f, 0.97f, 0.92f), 1.3f, 5f); // the lobby light: skin keeps its colour
        AddLight(new Vector3(-6.2f, 3.6f, 5.6f), new Color(1f, 0.72f, 0.45f), 0.9f, 6f); // the sconces' pools
        AddLight(new Vector3(6.2f, 3.6f, 5.0f), new Color(1f, 0.72f, 0.45f), 0.9f, 6f);
        AddLight(new Vector3(DoorX, 2.4f, -5.2f), new Color(1f, 0.25f, 0.3f), 1.0f, 4f); // the sign's glow on the doors
        AddLight(new Vector3(0f, 8.5f, 5.0f), new Color(1f, 0.82f, 0.62f), 1.1f, 13f); // warm house lights over the seats
        AddLight(new Vector3(0f, 8.5f, 10.5f), new Color(1f, 0.82f, 0.62f), 0.9f, 12f);
        ScreenLight = Lights[2];

        RageSigns();
        Plugin.L.LogInfo($"Movie house built ({(models ? "with its models" : "without its models: props.afvp missing")})");
        return true;
    }

    // ---- layout (local metres; the screen at +z) ----
    public const float Height = 11f, StageHeight = 0.7f, BoothHeight = 7.0f, DoorX = 4.0f;
    public const int Rows = 6, SeatsPerSide = 8;
    public const float FrontRowZ = 11.4f, RowDepth = 1.45f, RowRise = 0.17f, AisleHalf = 1.0f, SeatPitch = 0.62f, SeatBlock = 5.25f;

    /// <summary>The rage room behind the lobby doors (local): x -1 to 9, z -16.3 to -6.1, 4.5 m high.</summary>
    public static readonly Vector3 RageMin = new Vector3(-1f, 0f, -16.3f), RageMax = new Vector3(9f, 4.5f, -6.1f);

    /// <summary>The arcade behind the back wall's left doorway: x -13.5 to -1.2, z -16.3 to -6.1, 4.5 m high.</summary>
    public static readonly Vector3 ArcadeMin = new Vector3(-13.5f, 0f, -16.3f), ArcadeMax = new Vector3(-1.2f, 4.5f, -6.1f);

    /// <summary>The gun range through the left wall's doorway: x -13.5 to -7.1, z -6 to 16, 4 m high.</summary>
    public static readonly Vector3 RangeMin = new Vector3(-13.5f, 0f, -6f), RangeMax = new Vector3(-7.1f, 4f, 16f);

    public const float ArcadeDoorX0 = -5.6f, ArcadeDoorX1 = -3.6f, RangeDoorZ0 = -5.4f, RangeDoorZ1 = -3.6f;

    /// <summary>The gun range's shooting line (its benches, local z): you stand behind it.</summary>
    public const float GunRangeLine = -1.8f;

    /// <summary>Things standing on the floors (the rooms put them here: the arcade's machines, the counters): no walking
    /// through them (local boxes on the floor: x, z centre and half sizes).</summary>
    public static readonly List<(Vector2 centre, Vector2 half)> Obstacles = new List<(Vector2, Vector2)>();

    public static void Block(Vector3 centre, float halfX, float halfZ)
    {
        var o = (new Vector2(centre.x, centre.z), new Vector2(halfX, halfZ));
        if (!Obstacles.Contains(o)) Obstacles.Add(o);
    }

    public static Light ScreenLight { get; private set; }

    private static Transform _curtainL, _curtainR;
    private static float _curtains, _curtainsTo;
    private static readonly Dictionary<Light, float> HouseIntensity = new Dictionary<Light, float>();
    private static bool _lightsOn = true;

    /// <summary>The curtains drawn across the screen (true) or back (false); they move there.</summary>
    public static void Curtains(bool closed) => _curtainsTo = closed ? 1f : 0f;

    public static bool CurtainsClosed => _curtainsTo > 0.5f;

    /// <summary>The house lights on or down (the screen's own light and the neon stay).</summary>
    public static void HouseLights(bool on)
    {
        _lightsOn = on;
        foreach (var l in Lights)
        {
            if (l == null) continue;
            if (!HouseIntensity.ContainsKey(l)) HouseIntensity[l] = l.intensity;
            if (l != ScreenLight) l.intensity = on ? HouseIntensity[l] : HouseIntensity[l] * 0.12f;
        }
    }

    public static bool LightsOn => _lightsOn;

    private static void AnimateCurtains()
    {
        if (_curtainL == null) return;
        if (Mathf.Approximately(_curtains, _curtainsTo)) return;
        _curtains = Mathf.MoveTowards(_curtains, _curtainsTo, Time.unscaledDeltaTime * 0.45f);
        var e = _curtains * _curtains * (3f - 2f * _curtains);
        _curtainL.localPosition = new Vector3(Mathf.Lerp(-6.55f, -2.9f, e), StageHeight, 15.75f);
        _curtainR.localPosition = new Vector3(Mathf.Lerp(6.55f, 2.9f, e), StageHeight, 15.75f);
        _curtainL.localScale = new Vector3(Mathf.Lerp(1f, 1.55f, e), 1f, 1f);
        _curtainR.localScale = new Vector3(Mathf.Lerp(1f, 1.55f, e), 1f, 1f);
    }

    private static readonly Dictionary<Material, List<CombineInstance>> InstanceParts = new Dictionary<Material, List<CombineInstance>>();
    private static Mesh _sphere;

    /// <summary>One of the mod's models, on its own (its look from Props), turned about y.</summary>
    private static GameObject Model(string name, Vector3 pos, float yaw)
    {
        var go = Props.Spawn(name, Root, pos, Quaternion.Euler(0f, yaw, 0f), _layer);
        if (go != null) go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    /// <summary>A model into the combined meshes (one per look): the seats, steps and sconces are many.</summary>
    private static void Instance(string name, Vector3 pos, float yaw)
    {
        var m = Props.Get(name);
        if (m == null) return;
        var t = Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
        for (var i = 0; i < m.Materials.Length; i++)
        {
            var mat = Props.Mat(m.Materials[i]);
            if (!InstanceParts.TryGetValue(mat, out var list)) InstanceParts[mat] = list = new List<CombineInstance>();
            list.Add(new CombineInstance { mesh = m.Mesh, subMeshIndex = i, transform = t });
        }
    }

    private static void CombineInstances()
    {
        foreach (var kv in InstanceParts)
        {
            var mesh = new Mesh { name = "AngerFootVR Movie House " + kv.Key.name, indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(kv.Value.ToArray(), true, true);
            mesh.RecalculateBounds();
            Object.DontDestroyOnLoad(mesh);
            var go = new GameObject("Built " + kv.Key.name) { layer = _layer };
            go.transform.SetParent(Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = kv.Key;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        InstanceParts.Clear();
    }

    // ================================================================================== the rage room's doors

    private static Transform _doorL, _doorR;
    private static float _doorOpenAt = -1f;

    public static bool DoorOpen => _doorOpenAt >= 0f;

    private static Transform Hinge(string leaf, Vector3 hingeAt)
    {
        var pivot = new GameObject("Hinge " + leaf) { layer = _layer }.transform;
        pivot.SetParent(Root, false);
        pivot.localPosition = hingeAt;
        Props.Spawn(leaf, pivot, Vector3.zero, Quaternion.identity, _layer);
        return pivot;
    }

    /// <summary>A kick from where you stand: the doors burst open if you're at them, facing them.</summary>
    public static bool TryKickDoor(Vector3 head, Vector3 facing)
    {
        if (Root == null || DoorOpen || _doorL == null) return false;
        var door = Root.TransformPoint(new Vector3(DoorX, 1.1f, -6.0f));
        var to = door - head;
        to.y = 0f;
        facing.y = 0f;
        if (to.magnitude > 1.9f || Vector3.Dot(to.normalized, facing.normalized) < 0.35f) return false;
        _doorOpenAt = Time.unscaledTime;
        Sounds.Play("door_bang", door, 1f);
        Plugin.L.LogInfo("Rage room doors kicked open");
        return true;
    }

    /// <summary>The doors shut again (coming back to the movie house).</summary>
    public static void CloseDoors()
    {
        _doorOpenAt = -1f;
        if (_doorL != null) _doorL.localRotation = Quaternion.identity;
        if (_doorR != null) _doorR.localRotation = Quaternion.identity;
    }

    private static void AnimateDoors()
    {
        if (_doorL == null) return;
        var a = 0f;
        if (DoorOpen)
        {
            // flung open, overswinging a little, settling against their stops
            var t = Time.unscaledTime - _doorOpenAt;
            a = 100f * (1f - Mathf.Exp(-t * 9f) * Mathf.Cos(t * 14f));
        }

        _doorL.localRotation = Quaternion.Euler(0f, a, 0f);
        _doorR.localRotation = Quaternion.Euler(0f, -a, 0f);
    }

    // ================================================================================== walking

    /// <summary>Whether the floor here is hard (the rage room's, the arcade's and the range's concrete) or carpet.</summary>
    public static bool HardFloor(Vector3 local) => local.z < -6.1f || local.x < -7.05f;

    /// <summary>The open floor (local, at your feet): inside the hall's walls, off the stage, not through the seats or
    /// the mirror; through the doors into the rage room once they're open.</summary>
    public static bool Walkable(Vector3 p)
    {
        const float r = 0.25f; // your body's radius
        foreach (var (c, h) in Obstacles)
            if (Mathf.Abs(p.x - c.x) < h.x + r && Mathf.Abs(p.z - c.y) < h.y + r)
                return false;
        if (DoorOpen && Mathf.Abs(p.x - DoorX) < 0.95f - r && p.z > -6.6f && p.z < -5.5f) return true; // the doorway
        if (p.x > ArcadeDoorX0 + r && p.x < ArcadeDoorX1 - r && p.z > -6.6f && p.z < -5.5f) return true; // the arcade's doorway
        if (p.z > RangeDoorZ0 + r && p.z < RangeDoorZ1 - r && p.x > -7.6f && p.x < -6.5f) return true; // the range's
        if (p.z < -6.1f && p.x < ArcadeMax.x)
            return p.x > ArcadeMin.x + r && p.x < ArcadeMax.x - r && p.z > ArcadeMin.z + r && p.z < ArcadeMax.z - r;
        if (p.x < -7.1f)
            return p.x > RangeMin.x + r && p.x < RangeMax.x - r && p.z > RangeMin.z + r && p.z < GunRangeLine - r;
        if (p.z < -6.1f)
            return DoorOpen && p.x > RageMin.x + r && p.x < RageMax.x - r && p.z > RageMin.z + r && p.z < RageMax.z - r;
        if (p.x < -6.9f + r || p.x > 6.9f - r || p.z < -5.95f + r || p.z > 12.95f - r) return false;
        var back = FrontRowZ - (Rows - 1) * RowDepth - RowDepth / 2 - 0.15f;
        var front = FrontRowZ + RowDepth / 2;
        if (p.z > back - r && p.z < front + r && Mathf.Abs(p.x) > AisleHalf - r && Mathf.Abs(p.x) < AisleHalf + SeatBlock + r) return false;
        var m = new Vector2(p.x + 2.15f, p.z - (Standing.z - Origin.z));
        return !(Mathf.Abs(m.x) < 0.35f + r && Mathf.Abs(m.y) < 0.65f + r);
    }

    // ================================================================================== life

    private static readonly List<Renderer> Bulbs = new List<Renderer>();
    private static Material _bulbOn, _bulbOff;
    private static TextMeshPro _rageSign;
    private static Material _beamMat;
    private static RenderTexture _small;
    private static Color _screenColour = new Color(0.6f, 0.6f, 0.7f);
    private static float _nextSample, _nextChase;
    private static int _chase;
    private static bool _sampling;

    private static void BuildBulbs()
    {
        _bulbOn = Unlit(new Color(1f, 0.85f, 0.55f));
        _bulbOff = Unlit(new Color(0.25f, 0.18f, 0.1f));
        for (var i = 0; i < 40; i++)
        {
            var go = new GameObject("Bulb") { layer = _layer };
            go.transform.SetParent(Root, false);
            go.transform.localPosition = new Vector3(-6.75f + i * (13.5f / 39f), StageHeight + 0.05f, 13.08f);
            go.transform.localScale = Vector3.one * 0.07f;
            go.AddComponent<MeshFilter>().sharedMesh = _sphere;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _bulbOff;
            r.shadowCastingMode = ShadowCastingMode.Off;
            Bulbs.Add(r);
        }
    }

    /// <summary>The projector's beam from the booth's lens to the screen (a faint glow that takes the picture's colours),
    /// and dust drifting in it.</summary>
    private static void BuildBeam(Vector3 screenCentre)
    {
        var lens = new Vector3(0.15f, BoothHeight, -5.75f);
        var hw = ScreenWidth / 2;
        var hh = ScreenHeight / 2;
        var corners = new[]
        {
            screenCentre + new Vector3(-hw, -hh, -0.05f), screenCentre + new Vector3(hw, -hh, -0.05f),
            screenCentre + new Vector3(hw, hh, -0.05f), screenCentre + new Vector3(-hw, hh, -0.05f)
        };
        var mesh = new Mesh { name = "AngerFootVR Beam" };
        var verts = new List<Vector3>();
        var cols = new List<Color>();
        var tris = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % 4];
            var k = verts.Count;
            verts.Add(lens);
            verts.Add(a);
            verts.Add(b);
            cols.Add(new Color(1f, 1f, 1f, 0.42f));
            cols.Add(new Color(1f, 1f, 1f, 0.05f));
            cols.Add(new Color(1f, 1f, 1f, 0.05f));
            tris.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 1 }); // both sides
        }

        mesh.SetVertices(verts);
        mesh.SetColors(cols);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        Object.DontDestroyOnLoad(mesh);
        _beamMat = new Material(Shader.Find("Legacy Shaders/Particles/Additive")) { name = "AngerFootVR Beam" };
        _beamMat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        _beamMat.mainTexture = Texture2D.whiteTexture;
        _beamMat.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        var go = new GameObject("Projector Beam") { layer = _layer };
        go.transform.SetParent(Root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = _beamMat;
        r.shadowCastingMode = ShadowCastingMode.Off;

        // dust in the beam: slow, faint, lit by it
        var dust = new GameObject("Beam Dust") { layer = _layer };
        dust.transform.SetParent(Root, false);
        dust.transform.localPosition = lens;
        dust.transform.localRotation = Quaternion.LookRotation(screenCentre - lens);
        var ps = dust.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = 36f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
        main.startColor = new Color(1f, 0.95f, 0.85f, 0.6f);
        main.maxParticles = 900;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.useUnscaledTime = true;
        var emission = ps.emission;
        emission.rateOverTime = 22f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = Mathf.Atan(hw / (screenCentre - lens).magnitude) * Mathf.Rad2Deg * 0.8f;
        shape.radius = 0.2f;
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.12f;
        noise.frequency = 0.2f;
        var dustMat = new Material(Shader.Find("Legacy Shaders/Particles/Additive")) { name = "AngerFootVR Dust", mainTexture = SoftDot() };
        dustMat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        dustMat.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        var pr = dust.GetComponent<ParticleSystemRenderer>();
        pr.sharedMaterial = dustMat;
        pr.shadowCastingMode = ShadowCastingMode.Off;
        ps.Play();
    }

    private static Texture2D SoftDot()
    {
        const int n = 32;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "AngerFootVR Soft Dot", wrapMode = TextureWrapMode.Clamp };
        for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
        {
            var d = new Vector2(x - n / 2 + 0.5f, y - n / 2 + 0.5f).magnitude / (n / 2);
            var a = Mathf.Clamp01(1f - d);
            t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
        }

        t.Apply();
        t.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return t;
    }

    /// <summary>Every frame in the movie house: the bulbs chase, the old sign stutters, the doors swing, the screen's
    /// colours light the room and its beam.</summary>
    public static void Animate()
    {
        if (Root == null) return;
        var now = Time.unscaledTime;
        AnimateCurtains();
        if (now >= _nextChase && Bulbs.Count > 0)
        {
            _nextChase = now + 0.14f;
            _chase++;
            for (var i = 0; i < Bulbs.Count; i++) Bulbs[i].sharedMaterial = (i + _chase) % 3 == 0 ? _bulbOn : _bulbOff;
        }

        if (_rageSign != null)
        {
            var k = Mathf.Floor(now * 11f);
            var h = Mathf.Abs(Mathf.Sin(k * 12.9898f) * 43758.5453f) % 1f;
            var on = h > 0.04f;
            var c = _rageSign.color;
            c.a = on ? 1f : 0.25f;
            _rageSign.color = c;
        }

        AnimateDoors();
        SampleScreen(now);
        if (ScreenLight != null) ScreenLight.color = Color.Lerp(ScreenLight.color, _screenColour, Time.unscaledDeltaTime * 3f);
        if (_beamMat != null)
        {
            // the beam keeps its brightness (a dark menu would put it out): only its colour is the picture's, the
            // brighter the picture the stronger
            var c = _screenColour;
            var peak = Mathf.Max(0.05f, Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
            var hue = Color.Lerp(new Color(1f, 0.95f, 0.85f), new Color(c.r / peak, c.g / peak, c.b / peak), 0.4f);
            var beam = hue * (0.075f + 0.05f * Mathf.Clamp01(peak * 2f)); // (a haze: you see it through two of its sides)
            beam.a = 0.5f;
            _beamMat.SetColor("_TintColor", Color.Lerp(_beamMat.GetColor("_TintColor"), beam, Time.unscaledDeltaTime * 3f));
        }
    }

    /// <summary>The picture's average colour, a few times a second (read back from the GPU without waiting).</summary>
    private static void SampleScreen(float now)
    {
        if (_sampling || now < _nextSample || Feed == null) return;
        var feed = Feed.sharedMaterial.GetTexture("_EmissionMap");
        if (feed == null || !SystemInfo.supportsAsyncGPUReadback) return;
        _nextSample = now + 0.25f;
        if (_small == null)
        {
            _small = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32) { name = "AngerFootVR Screen Sample" };
            _small.Create();
        }

        Graphics.Blit(feed, _small);
        _sampling = true;
        UnityEngine.Rendering.AsyncGPUReadback.Request(_small, 0, req =>
        {
            _sampling = false;
            if (req.hasError) return;
            var data = req.GetData<Color32>();
            float r = 0, g = 0, b = 0;
            for (var i = 0; i < data.Length; i++)
            {
                r += data[i].r;
                g += data[i].g;
                b += data[i].b;
            }

            var n = Mathf.Max(1, data.Length) * 255f;
            var c = new Color(r / n, g / n, b / n);
            // brightened (a dark picture still lights the front rows a little), never grey
            var max = Mathf.Max(0.15f, c.maxColorComponent);
            _screenColour = Color.Lerp(new Color(0.55f, 0.55f, 0.65f), c / max, 0.7f);
        });
    }

    // ================================================================================== the rage room's walls

    private static void BuildRageShell(Material concrete, Material brick, Material metal)
    {
        var lo = RageMin;
        var hi = RageMax;
        var mid = (lo + hi) / 2;
        var size = hi - lo;
        var dark = Lit(brick.mainTexture as Texture2D, new Color(0.28f, 0.26f, 0.3f), new Vector2(4f, 2f));
        Box(concrete, new Vector3(mid.x, -0.05f, mid.z), new Vector3(size.x, 0.1f, size.z));
        Box(Lit(null, new Color(0.08f, 0.08f, 0.09f), Vector2.one), new Vector3(mid.x, hi.y + 0.05f, mid.z), new Vector3(size.x, 0.1f, size.z));
        Box(dark, new Vector3(lo.x - 0.05f, hi.y / 2, mid.z), new Vector3(0.1f, hi.y, size.z));
        Box(dark, new Vector3(hi.x + 0.05f, hi.y / 2, mid.z), new Vector3(0.1f, hi.y, size.z));
        Box(dark, new Vector3(mid.x, hi.y / 2, lo.z - 0.05f), new Vector3(size.x, hi.y, 0.1f));
        // the wall on the hall's side (the doorway's in the hall's back wall: this is the rest of the rage room's front)
        Box(dark, new Vector3((lo.x + 2.95f) / 2, hi.y / 2, hi.z + 0.05f - 0.2f), new Vector3(2.95f - lo.x, hi.y, 0.1f));
        Box(dark, new Vector3((5.05f + hi.x) / 2, hi.y / 2, hi.z + 0.05f - 0.2f), new Vector3(hi.x - 5.05f, hi.y, 0.1f));
        // fluorescent tubes, harsh
        var tube = Unlit(new Color(0.9f, 0.95f, 1f));
        foreach (var x in new[] { 1.5f, 6.5f })
        foreach (var z in new[] { -9f, -13.5f })
        {
            Box(metal, new Vector3(x, hi.y - 0.06f, z), new Vector3(0.25f, 0.08f, 1.6f));
            Box(tube, new Vector3(x, hi.y - 0.11f, z), new Vector3(0.08f, 0.04f, 1.5f));
        }

        // a stripe of hazard paint round the floor's edge
        var hazard = Unlit(new Color(0.95f, 0.75f, 0.1f));
        Box(hazard, new Vector3(mid.x, 0.005f, lo.z + 0.15f), new Vector3(size.x - 0.2f, 0.012f, 0.08f));
        Box(hazard, new Vector3(lo.x + 0.15f, 0.005f, mid.z), new Vector3(0.08f, 0.012f, size.z - 0.2f));
        Box(hazard, new Vector3(hi.x - 0.15f, 0.005f, mid.z), new Vector3(0.08f, 0.012f, size.z - 0.2f));

        AddLight(new Vector3(1.5f, hi.y - 0.4f, -11f), new Color(0.88f, 0.94f, 1f), 1.6f, 8f);
        AddLight(new Vector3(6.5f, hi.y - 0.4f, -11f), new Color(0.88f, 0.94f, 1f), 1.6f, 8f);
        AddLight(new Vector3(4f, 2.2f, -15.5f), new Color(1f, 0.2f, 0.3f), 1.2f, 6f);
    }

    /// <summary>The arcade: a carpeted room of neon under a dark ceiling, its machines put in by the Arcade.</summary>
    private static void BuildArcadeShell(Material carpet, Material brick, Material metal)
    {
        var lo = ArcadeMin;
        var hi = ArcadeMax;
        var mid = (lo + hi) / 2;
        var size = hi - lo;
        var dark = Lit(brick.mainTexture as Texture2D, new Color(0.22f, 0.18f, 0.28f), new Vector2(5f, 2f));
        var floor = Lit(carpet.mainTexture as Texture2D, new Color(0.45f, 0.3f, 0.6f), new Vector2(5f, 4f));
        Box(floor, new Vector3(mid.x, -0.05f, mid.z), new Vector3(size.x, 0.1f, size.z));
        Box(Lit(null, new Color(0.04f, 0.03f, 0.06f), Vector2.one), new Vector3(mid.x, hi.y + 0.05f, mid.z), new Vector3(size.x, 0.1f, size.z));
        Box(dark, new Vector3(lo.x - 0.05f, hi.y / 2, mid.z), new Vector3(0.1f, hi.y, size.z));
        Box(dark, new Vector3(hi.x + 0.05f, hi.y / 2, mid.z), new Vector3(0.1f, hi.y, size.z));
        Box(dark, new Vector3(mid.x, hi.y / 2, lo.z - 0.05f), new Vector3(size.x, hi.y, 0.1f));
        // (its front wall is the hall's back wall, the doorway in it; to its left, the gun range's back wall)
        Box(dark, new Vector3((lo.x - 7.1f) / 2, hi.y / 2, hi.z + 0.05f), new Vector3(-7.1f - lo.x, hi.y, 0.1f));
        var pink = Unlit(new Color(1f, 0.16f, 0.62f));
        var cyan = Unlit(new Color(0.15f, 0.95f, 1f));
        Box(pink, new Vector3(lo.x + 0.03f, 3.6f, mid.z), new Vector3(0.03f, 0.05f, size.z - 0.4f));
        Box(cyan, new Vector3(hi.x - 0.03f, 3.6f, mid.z), new Vector3(0.03f, 0.05f, size.z - 0.4f));
        Box(cyan, new Vector3(mid.x, 3.6f, lo.z + 0.03f), new Vector3(size.x - 0.4f, 0.05f, 0.03f));
        foreach (var x in new[] { -11f, -7.3f, -3.6f })
        foreach (var z in new[] { -9f, -13.5f })
            Box(metal, new Vector3(x, hi.y - 0.04f, z), new Vector3(0.9f, 0.06f, 0.9f));
        AddLight(new Vector3(-10.5f, 3.6f, -11f), new Color(1f, 0.3f, 0.75f), 1.8f, 9f);
        AddLight(new Vector3(-4f, 3.6f, -11f), new Color(0.25f, 0.85f, 1f), 1.8f, 9f);
        AddLight(new Vector3(-7.3f, 3.9f, -8.5f), new Color(1f, 0.9f, 0.75f), 1.2f, 8f);
        AddLight(new Vector3(-7.3f, 3.9f, -14f), new Color(1f, 0.9f, 0.75f), 1.2f, 8f);
        Text("ARCADE", new Vector3((ArcadeDoorX0 + ArcadeDoorX1) / 2, 2.95f, -5.97f), Quaternion.Euler(0f, 180f, 0f), 0.62f, new Color(0.15f, 0.95f, 1f));
        Text("ARCADE", new Vector3((ArcadeDoorX0 + ArcadeDoorX1) / 2, 2.95f, -6.16f), Quaternion.identity, 0.5f, new Color(0.15f, 0.95f, 1f));
    }

    /// <summary>The gun range: a long concrete room, benches across it with the guns laid out behind, targets out to
    /// 20 m (put in by the GunRange).</summary>
    private static void BuildRangeShell(Material concrete, Material brick, Material metal)
    {
        var lo = RangeMin;
        var hi = RangeMax;
        var mid = (lo + hi) / 2;
        var size = hi - lo;
        var dark = Lit(brick.mainTexture as Texture2D, new Color(0.3f, 0.3f, 0.32f), new Vector2(3f, 1.6f));
        var side = Lit(concrete.mainTexture as Texture2D, new Color(0.36f, 0.36f, 0.38f), new Vector2(10f, 1.6f));
        Box(concrete, new Vector3(mid.x, -0.05f, mid.z), new Vector3(size.x, 0.1f, size.z));
        Box(Lit(null, new Color(0.06f, 0.06f, 0.07f), Vector2.one), new Vector3(mid.x, hi.y + 0.05f, mid.z), new Vector3(size.x, 0.1f, size.z));
        Box(side, new Vector3(lo.x - 0.05f, hi.y / 2, mid.z), new Vector3(0.1f, hi.y, size.z));
        Box(dark, new Vector3(mid.x, hi.y / 2, hi.z + 0.05f), new Vector3(size.x, hi.y, 0.1f));
        // (its right wall is the hall's left wall with the doorway; the arcade's front wall is its back)
        // the backstop at the far end: dark rubber
        Box(Lit(null, new Color(0.05f, 0.05f, 0.06f), Vector2.one), new Vector3(mid.x, hi.y / 2, hi.z - 0.3f), new Vector3(size.x, hi.y, 0.5f));
        // lane lines on the floor, distance marks every 5 m
        var paint = Unlit(new Color(0.95f, 0.75f, 0.1f));
        var white = Unlit(new Color(0.85f, 0.85f, 0.85f));
        foreach (var x in new[] { lo.x + size.x / 3f, lo.x + size.x * 2f / 3f })
            Box(paint, new Vector3(x, 0.006f, (GunRangeLine + hi.z) / 2), new Vector3(0.05f, 0.012f, hi.z - GunRangeLine));
        for (var d = 5; d <= 15; d += 5)
        {
            Box(white, new Vector3(mid.x, 0.006f, GunRangeLine + d), new Vector3(size.x, 0.012f, 0.06f));
            Text($"{d} M", new Vector3(lo.x + 0.04f, 1.2f, GunRangeLine + d), Quaternion.Euler(0f, -90f, 0f), 0.3f, new Color(1f, 0.85f, 0.25f));
        }

        // baffles across the ceiling, lights over the benches and down the lanes
        for (var z = 1f; z < hi.z - 1f; z += 4f) Box(metal, new Vector3(mid.x, hi.y - 0.25f, z), new Vector3(size.x, 0.5f, 0.15f));
        var tube = Unlit(new Color(0.9f, 0.95f, 1f));
        foreach (var z in new[] { -4.5f, -2.5f })
            Box(tube, new Vector3(mid.x, hi.y - 0.05f, z), new Vector3(size.x - 0.6f, 0.04f, 0.08f));
        AddLight(new Vector3(mid.x, 3.5f, -3.8f), new Color(1f, 0.97f, 0.9f), 1.6f, 7f);
        AddLight(new Vector3(mid.x, 3.5f, 6f), new Color(0.9f, 0.95f, 1f), 1.0f, 10f);
        AddLight(new Vector3(mid.x, 3.5f, 13.5f), new Color(1f, 0.45f, 0.35f), 1.2f, 8f);
        Text("GUN RANGE", new Vector3(-6.97f, 2.95f, (RangeDoorZ0 + RangeDoorZ1) / 2), Quaternion.Euler(0f, 90f, 0f), 0.5f, new Color(1f, 0.85f, 0.25f));
        Text("GUN RANGE", new Vector3(lo.x + 0.05f, 3.3f, -4f), Quaternion.Euler(0f, -90f, 0f), 0.55f, new Color(1f, 0.85f, 0.25f));
    }

    /// <summary>A sign on a wall that faces back into its room (the hall's back wall, the rage room's far one).</summary>
    public static TextMeshPro Sign(string text, Vector3 localPos, float size, Color color, bool glow = true) =>
        glow ? Text(text, localPos, Quaternion.Euler(0f, 180f, 0f), size, color) : Letters(text, localPos, Quaternion.Euler(0f, 180f, 0f), size, color, 0f);

    /// <summary>A neon sign read from the side it faces (local), glowing.</summary>
    /// (Given a parent (a room's things, gone with the room), the sign goes with it.)
    public static TextMeshPro SignFacing(string text, Vector3 localPos, float size, Color color, Vector3 facing, Transform parent = null)
    {
        if (Root == null) return null;
        var before = Root.childCount;
        var tmp = Text(text, localPos, Quaternion.Euler(0f, Mathf.Atan2(-facing.x, -facing.z) * Mathf.Rad2Deg, 0f), size, color);
        if (parent != null)
            for (var i = Root.childCount - 1; i >= before; i--)
                Root.GetChild(i).SetParent(parent, true);
        return tmp;
    }

    /// <summary>The rage room's own signs (after the hall's are built).</summary>
    public static void RageSigns()
    {
        // (signs read from their -z side: these face back into the room)
        Text("SMASH IT", new Vector3(4f, 3.0f, RageMin.z + 0.06f), Quaternion.Euler(0f, 180f, 0f), 0.9f, new Color(1f, 0.2f, 0.3f));
        Text("A / X  TO KICK", new Vector3(4f, 2.3f, RageMin.z + 0.06f), Quaternion.Euler(0f, 180f, 0f), 0.3f, new Color(1f, 0.85f, 0.25f));
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

    private static TextMeshPro Text(string text, Vector3 pos, Quaternion rot, float size, Color color)
    {
        // the neon glow is a second copy of the letters just behind, thicker and faint (kept inside each letter's own
        // quad: a wide underlay spilled past it and showed the quad's square)
        Letters(text, pos + rot * new Vector3(0f, 0f, 0.01f), rot, size, new Color(color.r, color.g, color.b, 0.35f), 0.3f);
        return Letters(text, pos, rot, size, color, 0f);
    }

    private static TextMeshPro Letters(string text, Vector3 pos, Quaternion rot, float size, Color color, float dilate)
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
        return tmp;
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
