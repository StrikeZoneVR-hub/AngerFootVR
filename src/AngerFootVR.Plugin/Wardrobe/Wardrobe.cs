using System.Collections.Generic;
using AngerFootVR.Core;
using UnityEngine;

namespace AngerFootVR.Wardrobe;

/// <summary>
/// THE WARDROBE: gloves and body styles for your VR body, opened up as you beat levels (Unlocks: one of each kind a
/// level, two or three with its star goals). Every piece is the mod's own: fitted shells rebuilt each session from
/// the hero's skin in your copy of the game (HeroShell), the hero's own look in the mod's cartoon shader, and rigid
/// pieces sized to his bones. The mirror twin wears the same, so the mirror, your shadow and the BEHIND YOU view show it.
/// Chosen at the movie house's wardrobe (and kept in the BepInEx config).
/// </summary>
internal static class Wardrobe
{
    public sealed class Item
    {
        public string Id, Name, Blurb;
        public System.Action Put;
    }

    public static readonly List<Item> Gloves = new List<Item>();
    public static readonly List<Item> Bodies = new List<Item>();

    private static string _wornGlove, _wornBody;

    /// <summary>Dev: worn in place of the choice (never written to the config; open or not).</summary>
    public static Item DevBody, DevGlove;
    private static bool _ready;
    private static float _nextCheck;
    private static Transform _bodyRoot;

    public static void Init()
    {
        BuildLists();
    }

    public static Item Find(List<Item> list, string id) => list.Find(i => i.Id == id) ?? list[0];

    public static int IndexOf(List<Item> list, string id) => Mathf.Max(0, list.FindIndex(i => i.Id == id));

    public static bool IsOpen(List<Item> list, Item item) => Unlocks.Open(list.IndexOf(item));

    /// <summary>Every frame: the skin rebuilt once a session (at the first chance), then what's chosen kept on the body
    /// (put on again when the body is made anew or the choice changes).</summary>
    public static void Update()
    {
        if (!XrManager.IsRunning || Body.HeroModel.Template == null) return;
        if (!_ready)
        {
            if (Style.Skin == null)
            {
                if (!HeroShell.IsBuilding) HeroShell.Request("AngerfootBody");
                return;
            }

            _ready = Rendering.Shaders.Get("AngerFootVR/Toon") != null;
            if (!_ready) return;
        }

        if (Time.unscaledTime < _nextCheck) return;
        _nextCheck = Time.unscaledTime + 0.25f;
        var root = Body.VrBody.Root;
        if (root == null) return;
        var glove = DevGlove ?? Chosen(Gloves, VrSettings.Gloves);
        var body = DevBody ?? Chosen(Bodies, VrSettings.BodyStyle);
        if (root == _bodyRoot && glove.Id == _wornGlove && body.Id == _wornBody) return;
        Dress(body, glove);
        _bodyRoot = root;
    }

    /// <summary>The chosen item if it's open (else the kind's first: a locked choice is never worn).</summary>
    private static Item Chosen(List<Item> list, BepInEx.Configuration.ConfigEntry<string> entry)
    {
        var item = Find(list, entry != null ? entry.Value : "");
        return IsOpen(list, item) ? item : list[0];
    }

    public static void Dress(Item body, Item glove)
    {
        Style.Undress();
        try
        {
            body.Put?.Invoke();
            glove.Put?.Invoke();
        }
        catch (System.Exception e)
        {
            Plugin.L.LogWarning($"Wardrobe: dressing in {body.Id} + {glove.Id} failed ({e.Message})");
            Style.Undress();
        }

        _wornBody = body.Id;
        _wornGlove = glove.Id;
        Plugin.L.LogInfo($"Wardrobe: wearing {body.Name} and {glove.Name} ({Style.PieceCount} pieces)");
    }

    /// <summary>The movie house (and dev): wear this one now, and keep it (if it's open).</summary>
    public static bool Choose(Item item)
    {
        var isGlove = Gloves.Contains(item);
        var list = isGlove ? Gloves : Bodies;
        if (!IsOpen(list, item)) return false;
        (isGlove ? VrSettings.Gloves : VrSettings.BodyStyle).Value = item.Id;
        _nextCheck = 0f;
        return true;
    }

    // ------------------------------------------------------------------ the bones

    private static Animator Anim => Body.VrBody.TwinAnimator ?? Body.VrBody.Animator; // (the twin: never shrunk)

    private static string B(HumanBodyBones b)
    {
        var t = Anim != null ? Anim.GetBoneTransform(b) : null;
        return t != null ? t.name : null;
    }

    private static Transform T(HumanBodyBones b) => Anim != null ? Anim.GetBoneTransform(b) : null;

    private static HashSet<string> Hands => Fit.Under(Anim, HumanBodyBones.LeftHand, HumanBodyBones.RightHand);

    private static HashSet<string> Forearms => Fit.Under(Anim, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm);

    /// <summary>The body under the clothes of a suit: everything but the head and neck, the hands and the feet.</summary>
    private static HashSet<string> Clothed
    {
        get
        {
            var all = Fit.Under(Anim, HumanBodyBones.Hips);
            all.ExceptWith(Fit.Under(Anim, HumanBodyBones.Neck, HumanBodyBones.Head));
            all.ExceptWith(Hands);
            all.ExceptWith(Fit.Under(Anim, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot));
            return all;
        }
    }

    // ------------------------------------------------------------------ the lists

    private static Item Glove(string id, string name, string blurb, System.Action put)
    {
        var i = new Item { Id = id, Name = name, Blurb = blurb, Put = put };
        Gloves.Add(i);
        return i;
    }

    private static Item BodyStyle(string id, string name, string blurb, System.Action put)
    {
        var i = new Item { Id = id, Name = name, Blurb = blurb, Put = put };
        Bodies.Add(i);
        return i;
    }

    private static void BuildLists()
    {
        Gloves.Clear();
        Bodies.Clear();
        // (the order they open in: the designs first, then their other colours)
        Glove("bare", "BARE KNUCKLES", "Your own two hands.", null);
        Glove("leather", "DRIVING GLOVES", "Brown leather, for the getaway.", () => Fitted(0.004f, Style.Toon("leather", new Color(0.33f, 0.17f, 0.08f), 0.25f, 0.003f)));
        Glove("boxing", "BOXING GLOVES", "Big, red and padded.", () => Boxing(new Color(0.85f, 0.08f, 0.1f)));
        Glove("dish", "DISH GLOVES", "Yellow rubber, up to the elbow.", () => Long(Style.Toon("dish", new Color(1f, 0.86f, 0.12f), 0.6f, 0.003f)));
        Glove("cartoon", "CARTOON GLOVES", "White, four fingers' worth of cartoon.", () => Cartoon(Color.white));
        Glove("tape", "TAPED KNUCKLES", "Wrapped for a scrap.", Taped);
        Glove("brass", "BRASS KNUCKLES", "Bare hands, with a little extra.", Brass);
        Glove("robot", "ROBOT HANDS", "Chrome, with the joints showing.", () => Robot(new Color(0.78f, 0.8f, 0.84f)));
        Glove("spiked", "PUNK GLOVES", "Black leather, studded.", Spiked);
        Glove("oven", "OVEN MITTS", "Quilted. Hot stuff.", Oven);
        Glove("gold", "GOLD GLOVES", "Solid gold, for the champ.", () => Fitted(0.004f, Style.Toon("goldglove", new Color(1f, 0.76f, 0.2f), 0.85f, 0.003f, 0.5f)));
        Glove("boxing_blue", "BOXING GLOVES (BLUE)", "Big, blue and padded.", () => Boxing(new Color(0.12f, 0.3f, 0.9f)));
        Glove("leather_black", "DRIVING GLOVES (BLACK)", "Black leather.", () => Fitted(0.004f, Style.Toon("leather_black", new Color(0.07f, 0.06f, 0.07f), 0.3f, 0.003f)));
        Glove("dish_pink", "DISH GLOVES (PINK)", "Pink rubber, up to the elbow.", () => Long(Style.Toon("dish_pink", new Color(1f, 0.42f, 0.7f), 0.6f, 0.003f)));
        Glove("cartoon_black", "CARTOON GLOVES (BLACK)", "Black, with white cuffs.", () => Cartoon(new Color(0.08f, 0.07f, 0.08f)));
        Glove("robot_gold", "ROBOT HANDS (GOLD)", "Gold plate, with the joints showing.", () => Robot(new Color(1f, 0.76f, 0.2f)));

        BodyStyle("classic", "CLASSIC", "The man himself, as the game draws him.", null);
        BodyStyle("toon", "CARTOON SHADED", "Flat cartoon colour and a bold ink outline.", () => Style.Restyle(m => Style.ToonOf(m, 0.006f)));
        BodyStyle("hitman", "THE HITMAN", "A black suit, a white shirt, a tie. Business.", () => Suit(new Color(0.06f, 0.06f, 0.07f), new Color(0.95f, 0.95f, 0.95f), new Color(0.05f, 0.05f, 0.06f)));
        BodyStyle("tracksuit", "TRACKSUIT GOON", "Pink, with stripes. Dress like the goons.", () => Tracksuit(new Color(0.95f, 0.3f, 0.65f)));
        BodyStyle("killbot", "THE KILLBOT", "A chrome endoskeleton, red eyes. It came from the future.", () => Killbot(false));
        BodyStyle("gold", "GOLDEN STATUE", "Solid gold, head to toe.", () => Statue(new Color(0.85f, 0.56f, 0.12f), 0.85f));
        BodyStyle("skeleton", "THE SKELETON", "Just the bones.", () => Endoskeleton(new Color(0.94f, 0.9f, 0.8f), 0.1f, false));
        BodyStyle("silhouette", "SILHOUETTE", "Ink black, a white outline.", Silhouette);
        BodyStyle("hitman_white", "THE HITMAN (WHITE)", "A white suit for a big night.", () => Suit(new Color(0.92f, 0.92f, 0.9f), new Color(0.12f, 0.12f, 0.14f), new Color(0.75f, 0.08f, 0.1f)));
        BodyStyle("tracksuit_green", "TRACKSUIT GOON (GREEN)", "Green, with stripes.", () => Tracksuit(new Color(0.2f, 0.75f, 0.3f)));
        BodyStyle("killbot_gold", "THE KILLBOT (GOLD)", "A gold-plated endoskeleton.", () => Killbot(true));
        BodyStyle("marble", "MARBLE STATUE", "Carved in white marble.", () => Statue(new Color(0.93f, 0.92f, 0.9f), 0.35f));
        BodyStyle("tracksuit_blue", "TRACKSUIT GOON (BLUE)", "Blue, with stripes.", () => Tracksuit(new Color(0.15f, 0.35f, 0.9f)));
        BodyStyle("bronze", "BRONZE STATUE", "Cast in bronze.", () => Statue(new Color(0.72f, 0.42f, 0.2f), 0.7f));
    }

    // ------------------------------------------------------------------ gloves

    private static void Fitted(float outward, Material m) => Style.Shell(Hands, outward, m);

    private static void Long(Material m) => Style.Shell(Forearms, 0.004f, m);

    /// <summary>A ring round the wrist (a cuff), along the forearm, on both hands.</summary>
    private static void Cuff(Material m, float radius, float thick)
    {
        foreach (var (hand, fore) in new[] { (HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm), (HumanBodyBones.RightHand, HumanBodyBones.RightLowerArm) })
        {
            var h = T(hand);
            var f = T(fore);
            if (h == null || f == null) continue;
            var along = h.InverseTransformDirection(h.position - f.position).normalized;
            Style.Rigid(h.name, Shapes.Torus(), m, -along * 0.01f, Quaternion.FromToRotation(Vector3.forward, along), new Vector3(radius, radius, thick));
        }
    }

    private static void Boxing(Color c)
    {
        Style.Shell(Hands, 0.028f, Style.Toon("boxing " + c, c, 0.55f, 0.004f));
        Cuff(Style.Toon("cuff white", new Color(0.95f, 0.95f, 0.95f), 0.2f, 0.003f), 0.055f, 0.06f);
    }

    private static void Cartoon(Color c)
    {
        Style.Shell(Hands, 0.006f, Style.Toon("cartoon " + c, c, 0.15f, 0.004f));
        Cuff(Style.Toon("cartoon cuff " + c, c.grayscale < 0.5f ? new Color(0.95f, 0.95f, 0.95f) : c, 0.15f, 0.004f), 0.05f, 0.05f);
    }

    private static void Taped()
    {
        // (the palm and the knuckles: the hand and the first bone of each finger)
        var set = new HashSet<string>();
        var h = Fit.Under(Anim, HumanBodyBones.LeftHand, HumanBodyBones.RightHand);
        set.Add(B(HumanBodyBones.LeftHand));
        set.Add(B(HumanBodyBones.RightHand));
        foreach (var f in new[] { HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftLittleProximal, HumanBodyBones.RightIndexProximal, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightRingProximal, HumanBodyBones.RightLittleProximal })
        {
            var n = B(f);
            if (n != null) set.Add(n);
        }

        set.IntersectWith(h);
        var skin = Style.Skin;
        Style.Shell(set, 0.003f, Style.Toon("tape", Color.white, 0.1f, 0.002f, 0.65f, true), (i, p, n) =>
        {
            // (stripes of tape: bands across the hand)
            var band = Mathf.Repeat(Vector3.Dot(p, skin.Up) * 45f, 1f);
            return band < 0.18f ? new Color32(190, 185, 175, 255) : new Color32(245, 242, 232, 255);
        });
    }

    private static void Brass()
    {
        var m = Style.Toon("brass", new Color(0.85f, 0.62f, 0.25f), 0.8f, 0.002f, 0.5f);
        foreach (var f in new[] { HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftLittleProximal, HumanBodyBones.RightIndexProximal, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightRingProximal, HumanBodyBones.RightLittleProximal })
        {
            var t = T(f);
            if (t == null || t.childCount == 0) continue;
            var along = t.InverseTransformPoint(t.GetChild(0).position);
            var r = FleshRadius(t.name, 0.012f) * 1.25f;
            Style.Rigid(t.name, Shapes.Torus(), m, along * 0.3f, Quaternion.FromToRotation(Vector3.forward, along.normalized), new Vector3(r, r, r * 0.9f));
        }
    }

    private static void Robot(Color c)
    {
        Style.Shell(Hands, 0.005f, Style.Toon("robot " + c, c, 0.9f, 0.003f, 0.45f));
        var joint = Style.Toon("robot joint", new Color(0.12f, 0.12f, 0.14f), 0.6f, 0f, 0.5f);
        foreach (var n in Hands)
        {
            var r = FleshRadius(n, 0.01f);
            if (r < 0.02f) Style.Ball(n, Vector3.zero, r * 1.15f, joint);
        }
    }

    private static void Spiked()
    {
        Style.Shell(Hands, 0.004f, Style.Toon("punk", new Color(0.06f, 0.05f, 0.06f), 0.35f, 0.003f));
        var stud = Style.Toon("stud", new Color(0.85f, 0.86f, 0.9f), 0.9f, 0.002f, 0.5f);
        for (var side = 0; side < 2; side++)
        {
            var hand = T(side == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            var index = T(side == 0 ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            var middle = T(side == 0 ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            var little = T(side == 0 ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
            if (hand == null || index == null || middle == null || little == null) continue;
            var fingers = hand.InverseTransformPoint(middle.position).normalized;
            var across = hand.InverseTransformPoint(index.position) - hand.InverseTransformPoint(little.position);
            var back = (side == 0 ? 1f : -1f) * Vector3.Cross(fingers, across).normalized;
            foreach (var f in new[] { index, middle, T(side == 0 ? HumanBodyBones.LeftRingProximal : HumanBodyBones.RightRingProximal), little })
            {
                if (f == null) continue;
                var at = hand.InverseTransformPoint(f.position) + back * FleshRadius(hand.name, 0.02f) * 0.55f;
                Style.Rigid(hand.name, Shapes.Cone(), stud, at, Quaternion.FromToRotation(Vector3.up, back), new Vector3(0.012f, 0.022f, 0.012f));
            }
        }
    }

    private static void Oven()
    {
        var skin = Style.Skin;
        Style.Shell(Hands, 0.02f, Style.Toon("oven", Color.white, 0.1f, 0.004f, 0.6f, true), (i, p, n) =>
        {
            // (quilted: a diamond pattern of orange and a darker seam)
            var a = Mathf.Repeat((p.x + p.y) * 30f, 1f);
            var b = Mathf.Repeat((p.x - p.y) * 30f, 1f);
            return a < 0.12f || b < 0.12f ? new Color32(170, 70, 20, 255) : new Color32(240, 120, 40, 255);
        });
        Cuff(Style.Toon("oven cuff", new Color(0.95f, 0.93f, 0.88f), 0.1f, 0.003f), 0.06f, 0.07f);
    }

    // ------------------------------------------------------------------ bodies

    private static void Statue(Color c, float gloss) => Style.Restyle(m => Style.ToonOf(m, 0.004f, c, false, gloss));

    private static void Silhouette()
    {
        var ink = Style.Toon("silhouette", new Color(0.04f, 0.035f, 0.05f), 0f, 0.006f, 0.9f);
        ink.SetColor("_OutlineColor", new Color(0.95f, 0.95f, 0.95f));
        Style.Restyle(m => ink);
    }

    /// <summary>A suit: trousers and a jacket over the body, a shirt and a tie down the front.</summary>
    private static void Suit(Color cloth, Color shirt, Color tie)
    {
        var skin = Style.Skin;
        Frame(out var up, out var right, out var fwd, out var neck, out var hips);
        var torso = Fit.Under(Anim, HumanBodyBones.Spine);
        torso.ExceptWith(Fit.Under(Anim, HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder, HumanBodyBones.Neck));
        var len = Mathf.Max(0.05f, Vector3.Dot(neck - hips, up));
        Style.Hide("Jacket2", "Bands");
        Style.BareOnly(Clothed);
        Style.Shell(Clothed, 0.012f, Style.Toon("suit", Color.white, 0.2f, 0.005f, 0.6f, true), (i, p, n) =>
        {
            var j = skin.Main(i);
            if (j < 0 || !torso.Contains(skin.BoneNames[j])) return cloth;
            var h = Vector3.Dot(p - hips, up) / len; // (0 at the hips, 1 at the neck)
            var off = Mathf.Abs(Vector3.Dot(p - neck, right));
            var front = Vector3.Dot(p - (hips + neck) * 0.5f, fwd) > 0f && Vector3.Dot(n, fwd) > 0.2f;
            if (!front || h < 0.3f) return cloth;
            if (off < 0.022f && h < 0.97f) return tie;
            var vee = 0.02f + (h - 0.45f) * 0.22f; // (the shirt's V, widest at the collar)
            if (h > 0.45f && off < vee) return shirt;
            return cloth;
        });
    }

    private static void Tracksuit(Color c)
    {
        var skin = Style.Skin;
        Frame(out var up, out var right, out var fwd, out var neck, out var hips);
        var limbs = Fit.Under(Anim, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg);
        var mid = (neck + hips) * 0.5f;
        var stripe = new Color(0.97f, 0.97f, 0.97f);
        Style.Hide("Jacket2");
        Style.BareOnly(Clothed);
        Style.Shell(Clothed, 0.01f, Style.Toon("tracksuit", Color.white, 0.25f, 0.004f, 0.6f, true), (i, p, n) =>
        {
            var j = skin.Main(i);
            if (j < 0 || !limbs.Contains(skin.BoneNames[j])) return c;
            // (two stripes down the outside of each arm and leg: the side facing away from the body's middle)
            var outward = Mathf.Sign(Vector3.Dot(p - mid, right));
            var face = Vector3.Dot(n, right) * outward;
            return face > 0.93f || face > 0.72f && face < 0.8f ? stripe : c;
        });
    }

    /// <summary>An endoskeleton (chrome: the killbot; bone: the skeleton), chunky in the game's cartoon way: the hero's
    /// own pieces hidden, thick rods along his bones with knobbly joints (and pistons beside them for the killbot), a
    /// ribcage, a pelvis, a skull with its jaw and teeth; red glowing eyes for the killbot.</summary>
    private static void Endoskeleton(Color c, float gloss, bool robot)
    {
        Style.HideAllBut();
        var metal = Style.Toon(robot ? "endo " + c : "bone", c, gloss, 0.005f, robot ? 0.4f : 0.72f);
        var dark = Style.Toon(robot ? "endo dark" : "bone dark", robot ? new Color(0.16f, 0.16f, 0.19f) : new Color(0.08f, 0.06f, 0.06f), robot ? 0.6f : 0f, 0.003f, 0.5f);
        var twin = Body.VrBody.Roots[1];
        var up = twin != null ? twin.up : Vector3.up;
        var fwdW = twin != null ? twin.forward : Vector3.forward;

        void Limb(HumanBodyBones from, HumanBodyBones to, float r)
        {
            var a = T(from);
            var b = T(to);
            if (a == null || b == null) return;
            var along = a.InverseTransformPoint(b.position);
            Style.Rod(a.name, along, r, metal);
            Style.Ball(b.name, Vector3.zero, r * 1.6f, robot ? dark : metal);
            if (robot)
            {
                // (a piston beside the bone)
                var side = Vector3.Cross(along.normalized, a.InverseTransformDirection(fwdW)).normalized * r * 1.3f;
                Style.Rigid(a.name, Style.Rod(), dark, along * 0.5f + side, Quaternion.FromToRotation(Vector3.up, along.normalized), new Vector3(r * 0.9f, along.magnitude * 0.32f, r * 0.9f));
            }
            else Style.Ball(a.name, along * 0.08f, r * 1.4f, metal); // (a bone's knobbly end)
        }

        Limb(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, 0.042f);
        Limb(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.036f);
        Limb(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 0.042f);
        Limb(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.036f);
        Limb(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 0.055f);
        Limb(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.046f);
        Limb(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.055f);
        Limb(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.046f);
        Limb(HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, 0.034f);
        Limb(HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, 0.034f);

        // the spine: vertebrae up from the hips to the head
        var chain = new List<Transform>();
        foreach (var hb in new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head })
        {
            var t = T(hb);
            if (t != null && !chain.Contains(t)) chain.Add(t);
        }

        for (var k = 0; k + 1 < chain.Count; k++)
        {
            var a = chain[k];
            var along = a.InverseTransformPoint(chain[k + 1].position);
            Style.Rod(a.name, along, 0.03f, metal);
            for (var v = 1; v <= 3; v++) Style.Box(a.name, along * (v / 4f), Quaternion.FromToRotation(Vector3.up, along.normalized), new Vector3(0.075f, 0.022f, 0.06f), robot ? dark : metal);
        }

        // the pelvis
        var hips = T(HumanBodyBones.Hips);
        if (hips != null)
        {
            var f = Flesh(hips.name);
            Style.Rigid(hips.name, Style.Ball(), metal, f.center, Quaternion.identity, new Vector3(f.size.x * 0.75f, f.size.y * 0.45f, f.size.z * 0.55f));
        }

        // the ribcage: rings round the chest, biggest in the middle
        var chest = T(HumanBodyBones.UpperChest) ?? T(HumanBodyBones.Chest) ?? T(HumanBodyBones.Spine);
        if (chest != null)
        {
            var f = Flesh(chest.name);
            var upL = chest.InverseTransformDirection(up).normalized;
            var fwdL = chest.InverseTransformDirection(fwdW).normalized;
            var w = Mathf.Max(f.size.x, Mathf.Max(f.size.y, f.size.z));
            for (var k = 0; k < 5; k++)
            {
                var at = f.center + upL * (w * (0.22f - k * 0.1f));
                var sz = w * (0.32f + Mathf.Sin((k + 0.5f) / 5f * Mathf.PI) * 0.08f);
                Style.Rigid(chest.name, Shapes.Torus(), metal, at, Quaternion.LookRotation(upL, fwdL), new Vector3(sz, sz * 0.78f, 0.12f));
            }

            if (robot) Style.Box(chest.name, f.center + fwdL * w * 0.05f, Quaternion.LookRotation(fwdL, upL), new Vector3(w * 0.12f, w * 0.5f, w * 0.12f), dark); // (the sternum: a power core)
        }

        // hands' and feet's bones
        foreach (var n in Hands)
        {
            var r = FleshRadius(n, 0.01f);
            if (r < 0.025f) Style.Ball(n, Vector3.zero, Mathf.Max(0.011f, r * 0.85f), metal);
        }

        foreach (var foot in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
        {
            var t = T(foot);
            if (t == null) continue;
            var f = Flesh(t.name);
            Style.Rigid(t.name, Style.Box(), metal, f.center, Quaternion.identity, f.size * 0.8f);
        }

        // the skull, its jaw and teeth, the eyes
        var head = T(HumanBodyBones.Head);
        if (head != null)
        {
            var f = Flesh(head.name);
            var fwd = head.InverseTransformDirection(fwdW).normalized;
            var upH = head.InverseTransformDirection(up).normalized;
            var right = Vector3.Cross(upH, fwd).normalized;
            var size = f.size.magnitude / 1.732f;
            Style.Rigid(head.name, Style.Ball(), metal, f.center + upH * size * 0.06f, Quaternion.LookRotation(fwd, upH), new Vector3(size * 0.9f, size * 0.88f, size * 0.95f));
            Style.Box(head.name, f.center + fwd * size * 0.2f - upH * size * 0.36f, Quaternion.LookRotation(fwd, upH), new Vector3(size * 0.6f, size * 0.2f, size * 0.5f), metal);
            var teeth = Style.Toon(robot ? "killbot teeth" : "skull teeth", robot ? new Color(0.85f, 0.87f, 0.9f) : new Color(0.98f, 0.96f, 0.9f), 0.3f, 0.002f, 0.7f);
            for (var k = -3; k <= 3; k++)
                Style.Box(head.name, f.center + fwd * size * 0.44f - upH * size * 0.24f + right * k * size * 0.07f, Quaternion.LookRotation(fwd, upH), new Vector3(size * 0.055f, size * 0.09f, size * 0.04f), teeth);
            var eye = robot ? Style.Toon("killbot eye", new Color(1f, 0.08f, 0.05f), 0f, 0f, 1f) : dark;
            if (robot) eye.SetFloat("_Flat", 1f);
            foreach (var sd in new[] { -1f, 1f })
            {
                var at = f.center + fwd * size * 0.4f + upH * size * 0.05f + right * sd * size * 0.2f;
                Style.Ball(head.name, at, size * (robot ? 0.085f : 0.12f), eye);
                if (robot) Style.Ball(head.name, at - fwd * size * 0.02f, size * 0.13f, dark); // (the socket round the glow)
            }

            if (!robot) Style.Rigid(head.name, Shapes.Cone(), dark, f.center + fwd * size * 0.46f - upH * size * 0.08f, Quaternion.LookRotation(-upH, fwd), new Vector3(size * 0.08f, size * 0.1f, size * 0.06f)); // (the nose's hole)
        }
    }

    /// <summary>THE KILLBOT: the hero's own pieces hidden and a machine in their place, its parts (made in Blender:
    /// tools/blender/build_wardrobe.py) each riding on his bones and sized to them: hydraulic struts along the limbs,
    /// hinged elbows and knees, vertebrae up the spine, a ribcage, a pelvis, a skull with its jaw and red eyes, a palm
    /// plate and a segment on every finger bone, metal feet. Polished chrome (or gold) that shows the room.</summary>
    private static void Killbot(bool gold)
    {
        System.Func<string, string> swap = gold ? n => n == "endo_chrome" ? "endo_gold" : n : (System.Func<string, string>)null;
        Style.HideAllBut();
        var twin = Body.VrBody.Roots[1];
        var upW = twin != null ? twin.up : Vector3.up;
        var fwdW = twin != null ? twin.forward : Vector3.forward;

        // (a part's own up along the bone, its front the body's front as near as the bone allows)
        Quaternion Along(Transform bone, Vector3 along)
        {
            var front = Vector3.ProjectOnPlane(bone.InverseTransformDirection(fwdW), along);
            if (front.sqrMagnitude < 1e-4f) front = Vector3.ProjectOnPlane(bone.InverseTransformDirection(upW), along);
            return Quaternion.LookRotation(front.normalized, along.normalized);
        }

        void Limb(HumanBodyBones from, HumanBodyBones to, string model, float r, bool joint)
        {
            var a = T(from);
            var b = T(to);
            if (a == null || b == null) return;
            var along = a.InverseTransformPoint(b.position);
            Style.Part(a.name, model, Vector3.zero, Along(a, along), new Vector3(r / 0.05f, along.magnitude, r / 0.05f), swap);
            if (joint)
            {
                var alongB = b.InverseTransformDirection(b.position - a.position);
                Style.Part(b.name, "kb_joint", Vector3.zero, Along(b, alongB), Vector3.one * (r / 0.05f), swap);
            }
        }

        Limb(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, "kb_upper", 0.045f, true);
        Limb(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, "kb_lower", 0.04f, false);
        Limb(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, "kb_upper", 0.045f, true);
        Limb(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, "kb_lower", 0.04f, false);
        Limb(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, "kb_upper", 0.062f, true);
        Limb(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, "kb_lower", 0.055f, false);
        Limb(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, "kb_upper", 0.062f, true);
        Limb(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, "kb_lower", 0.055f, false);
        foreach (var arm in new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm })
        {
            var t = T(arm);
            if (t != null) Style.Part(t.name, "kb_shoulder", Vector3.zero, Quaternion.identity, Vector3.one * 1.1f, swap);
        }

        // the neck's cables, the spine's vertebrae
        var neck = T(HumanBodyBones.Neck);
        var head = T(HumanBodyBones.Head);
        if (neck != null && head != null)
        {
            var along = neck.InverseTransformPoint(head.position);
            Style.Part(neck.name, "kb_neck", Vector3.zero, Along(neck, along), new Vector3(1f, along.magnitude, 1f), swap);
        }

        var chain = new List<Transform>();
        foreach (var hb in new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck })
        {
            var t = T(hb);
            if (t != null && !chain.Contains(t)) chain.Add(t);
        }

        for (var k = 0; k + 1 < chain.Count; k++)
        {
            var a = chain[k];
            var along = a.InverseTransformPoint(chain[k + 1].position);
            for (var v = 0; v < 3; v++) Style.Part(a.name, "kb_vertebra", along * ((v + 0.5f) / 3f), Along(a, along), Vector3.one * 1.15f, swap);
        }

        // the ribcage, the pelvis, the skull: life-size parts fitted to the chest, the hips, the head
        void Fitted(HumanBodyBones hb, string model, float design, float fill)
        {
            var t = T(hb);
            if (t == null) return;
            var f = Flesh(t.name);
            var up = t.InverseTransformDirection(upW).normalized;
            var fwd = Vector3.ProjectOnPlane(t.InverseTransformDirection(fwdW), up).normalized;
            var width = Mathf.Abs(Vector3.Dot(f.size, Vector3.Cross(up, fwd).normalized));
            var s = Mathf.Clamp(Mathf.Max(width, Mathf.Max(f.size.x, Mathf.Min(f.size.y, f.size.z))) * fill / design, 0.5f, 2.5f);
            Style.Part(t.name, model, f.center, Quaternion.LookRotation(fwd, up), Vector3.one * s, swap);
        }

        Fitted(T(HumanBodyBones.UpperChest) != null ? HumanBodyBones.UpperChest : T(HumanBodyBones.Chest) != null ? HumanBodyBones.Chest : HumanBodyBones.Spine, "kb_ribcage", 0.3f, 0.85f);
        Fitted(HumanBodyBones.Hips, "kb_pelvis", 0.26f, 0.8f);
        Fitted(HumanBodyBones.Head, "kb_skull", 0.2f, 0.85f);

        // the hands: a palm plate, a segment on every finger bone
        for (var side = 0; side < 2; side++)
        {
            var hand = T(side == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            var index = T(side == 0 ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            var middle = T(side == 0 ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            var little = T(side == 0 ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
            if (hand == null || middle == null) continue;
            var fingers = hand.InverseTransformPoint(middle.position);
            var across = index != null && little != null ? hand.InverseTransformPoint(index.position) - hand.InverseTransformPoint(little.position) : Vector3.right;
            var back = ((side == 0 ? 1f : -1f) * Vector3.Cross(fingers.normalized, across)).normalized;
            Style.Part(hand.name, "kb_palm", Vector3.zero, Quaternion.LookRotation(back, fingers.normalized), Vector3.one * (fingers.magnitude / 0.095f), swap);
            foreach (var f in new[] { "Index", "Middle", "Ring", "Little", "Thumb" })
            {
                Transform prev = null;
                foreach (var part in new[] { "Proximal", "Intermediate", "Distal" })
                {
                    var t = T((HumanBodyBones)System.Enum.Parse(typeof(HumanBodyBones), (side == 0 ? "Left" : "Right") + f + part));
                    if (t == null) continue;
                    var next = t.childCount > 0 ? t.GetChild(0) : null;
                    var along = next != null ? t.InverseTransformPoint(next.position) : prev != null ? t.InverseTransformDirection(t.position - prev.position) * 0.8f : Vector3.up * 0.02f;
                    var backL = Vector3.ProjectOnPlane(t.InverseTransformDirection(hand.TransformDirection(back)), along).normalized;
                    var r = Mathf.Max(0.006f, FleshRadius(t.name, 0.009f) * 0.8f);
                    Style.Part(t.name, "kb_finger", Vector3.zero, Quaternion.LookRotation(backL, along.normalized), new Vector3(r / 0.01f, along.magnitude, r / 0.01f), swap);
                    prev = t;
                }
            }
        }

        // the feet
        foreach (var (footB, toeB) in new[] { (HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes), (HumanBodyBones.RightFoot, HumanBodyBones.RightToes) })
        {
            var foot = T(footB);
            if (foot == null) continue;
            var toe = T(toeB);
            var up = foot.InverseTransformDirection(upW).normalized;
            var toward = toe != null ? Vector3.ProjectOnPlane(foot.InverseTransformPoint(toe.position), up) : Vector3.ProjectOnPlane(foot.InverseTransformDirection(fwdW), up) * 0.15f;
            Style.Part(foot.name, "kb_foot", Vector3.zero, Quaternion.LookRotation(toward.normalized, up), Vector3.one * Mathf.Clamp(toward.magnitude / 0.16f, 0.6f, 1.8f), swap);
        }
    }

    // ------------------------------------------------------------------ the body's own directions

    /// <summary>The body's up, right and forward in the skin's snapshot (from its bones: hips to neck, left arm to
    /// right), and its middle line's top and bottom.</summary>
    private static bool Frame(out Vector3 up, out Vector3 right, out Vector3 fwd, out Vector3 neck, out Vector3 hips)
    {
        up = Vector3.up;
        right = Vector3.right;
        fwd = Vector3.forward;
        neck = hips = Vector3.zero;
        var skin = Style.Skin;
        if (skin == null) return false;
        int n = skin.Bone(B(HumanBodyBones.Neck)), h = skin.Bone(B(HumanBodyBones.Hips)), l = skin.Bone(B(HumanBodyBones.LeftUpperArm)), r = skin.Bone(B(HumanBodyBones.RightUpperArm));
        if (n < 0 || h < 0 || l < 0 || r < 0) return false;
        neck = skin.BonePos[n];
        hips = skin.BonePos[h];
        up = (neck - hips).normalized;
        right = Vector3.ProjectOnPlane(skin.BonePos[r] - skin.BonePos[l], up).normalized;
        fwd = Vector3.Cross(right, up).normalized;
        return true;
    }

    // ------------------------------------------------------------------ sizes

    private static Bounds Flesh(string bone)
    {
        var skin = Style.Skin;
        var j = skin != null ? skin.Bone(bone) : -1;
        return j >= 0 ? skin.Flesh[j] : new Bounds(Vector3.zero, Vector3.one * 0.05f);
    }

    private static float FleshRadius(string bone, float fallback)
    {
        var skin = Style.Skin;
        var j = skin != null ? skin.Bone(bone) : -1;
        if (j < 0) return fallback;
        var s = skin.Flesh[j].size;
        var r = Mathf.Min(s.x, Mathf.Min(s.y, s.z)) * 0.5f;
        return r > 0.001f ? r : fallback;
    }

    public static string Describe() => $"WARDROBE wearing {_wornBody ?? "-"} + {_wornGlove ?? "-"}, {Style.PieceCount} pieces | {Unlocks.Describe()} | {HeroShell.Describe()}";
}
