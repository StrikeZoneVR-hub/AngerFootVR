using System.Collections.Generic;
using AngerFootVR.Core;
using UnityEngine;

namespace AngerFootVR.Wardrobe;

/// <summary>
/// THE WARDROBE: gloves and body styles for your VR body, all of them yours to wear from the start. Every piece is the
/// mod's own: fitted shells rebuilt each session from
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

    /// <summary>The chosen item (the kind's first if the choice is unknown).</summary>
    private static Item Chosen(List<Item> list, BepInEx.Configuration.ConfigEntry<string> entry) => Find(list, entry != null ? entry.Value : "");

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

    /// <summary>The movie house (and dev): wear this one now, and keep it.</summary>
    public static bool Choose(Item item)
    {
        var isGlove = Gloves.Contains(item);
        if (Plugin.DevRun)
        {
            // (a dev run shares the player's settings file: worn for now, never written there)
            if (isGlove) DevGlove = item;
            else DevBody = item;
        }
        else
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
        Glove("goldgloves", "GOLD GLOVES", "Solid gold, for the champ.", () => Fitted(0.004f, Style.Toon("goldglove", new Color(1f, 0.76f, 0.2f), 0.85f, 0.003f, 0.5f)));
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
        BodyStyle("skeleton", "THE SKELETON", "Just the bones.", () => Killbot(false, true));
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
    /// <summary>A rolled cuff round each wrist, snug on the skin (its tube resting on the forearm, this far out from it)
    /// and this long along the arm.</summary>
    private static void Cuff(Material m, float outward, float length, float back = 0.015f)
    {
        foreach (var (hand, fore) in new[] { (HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm), (HumanBodyBones.RightHand, HumanBodyBones.RightLowerArm) })
        {
            var h = T(hand);
            var f = T(fore);
            if (h == null || f == null) continue;
            var along = h.InverseTransformDirection(h.position - f.position).normalized;
            // (the ring's middle circle where the tube sits on the wrist: the torus's tube is 0.3 of its ring)
            var wrist = FleshRadius(f.name, 0.035f) * 0.9f;
            var ring = (wrist + outward) / 0.85f;
            Style.Rigid(h.name, Shapes.Torus(), m, -along * back, Quaternion.FromToRotation(Vector3.forward, along), new Vector3(ring, ring, length / 0.6f));
        }
    }

    private static void Boxing(Color c)
    {
        Style.Shell(Hands, 0.028f, Style.Toon("boxing " + c, c, 0.55f, 0.004f));
        Cuff(Style.Toon("cuff white", new Color(0.95f, 0.95f, 0.95f), 0.2f, 0.003f), 0.012f, 0.04f, 0.035f);
    }

    private static void Cartoon(Color c)
    {
        Style.Shell(Hands, 0.006f, Style.Toon("cartoon " + c, c, 0.15f, 0.004f));
        Cuff(Style.Toon("cartoon cuff " + c, c.grayscale < 0.5f ? new Color(0.95f, 0.95f, 0.95f) : c, 0.15f, 0.004f), 0.008f, 0.035f);
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

    /// <summary>The killbot's chrome (or gold), in a colour.</summary>
    private static Material MetalLike(Color c)
    {
        if (!Metals.TryGetValue(c, out var m) || m == null)
            Metals[c] = m = new Material(UI.Props.Mat(c.b > c.r ? "endo_chrome" : "endo_gold")) { color = c };
        return m;
    }

    private static readonly Dictionary<Color, Material> Metals = new Dictionary<Color, Material>();

    private static void Robot(Color c)
    {
        // (polished metal plating, like the killbot's, with dark joints just showing at the knuckles)
        Style.Shell(Hands, 0.005f, MetalLike(c));
        var joint = UI.Props.Mat("endo_dark");
        foreach (var n in Hands)
        {
            var r = FleshRadius(n, 0.01f);
            if (r < 0.02f) Style.Ball(n, Vector3.zero, r + 0.0065f, joint);
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
        Cuff(Style.Toon("oven cuff", new Color(0.95f, 0.93f, 0.88f), 0.1f, 0.003f), 0.016f, 0.045f, 0.03f);
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

    /// <summary>THE KILLBOT: the hero's own pieces hidden and a machine in their place, its parts (made in Blender:
    /// tools/blender/build_wardrobe.py) each riding on his bones and sized to them: hydraulic struts along the limbs,
    /// hinged elbows and knees, vertebrae up the spine, a ribcage, a pelvis, a skull with its jaw and red eyes, a palm
    /// plate and a segment on every finger bone, metal feet. Polished chrome (or gold) that shows the room. THE
    /// SKELETON is dressed the same way (bones: true): its own bone parts where the machine's differ, the rest in bone.</summary>
    private static void Killbot(bool gold, bool bones = false)
    {
        System.Func<string, string> swap = bones ? n => n switch { "endo_chrome" => "bone", "endo_dark" => "bone_dark", "endo_teeth" => "bone", "endo_eye" => "bone_socket", _ => n }
            : gold ? n => n == "endo_chrome" ? "endo_gold" : n : (System.Func<string, string>)null;
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
            if (bones)
            {
                model = "sk_bone";
                r *= 0.9f;
                joint = false;
            }

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
            if (t != null && !bones) Style.Part(t.name, "kb_shoulder", Vector3.zero, Quaternion.identity, Vector3.one * 1.1f, swap);
        }

        // the neck's cables, the spine's vertebrae
        var neck = T(HumanBodyBones.Neck);
        var head = T(HumanBodyBones.Head);
        if (neck != null && head != null && !bones)
        {
            var along = neck.InverseTransformPoint(head.position);
            Style.Part(neck.name, "kb_neck", Vector3.zero, Along(neck, along), new Vector3(1f, along.magnitude, 1f), swap);
        }

        var chain = new List<Transform>();
        foreach (var hb in new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head })
        {
            var t = T(hb);
            if (t != null && !chain.Contains(t) && (bones || hb != HumanBodyBones.Head)) chain.Add(t);
        }

        for (var k = 0; k + 1 < chain.Count; k++)
        {
            var a = chain[k];
            var along = a.InverseTransformPoint(chain[k + 1].position);
            // (the skeleton's neck: smaller vertebrae up to the skull)
            var size = bones && k == chain.Count - 2 ? 0.8f : 1.15f;
            for (var v = 0; v < 3; v++) Style.Part(a.name, "kb_vertebra", along * ((v + 0.5f) / 3f), Along(a, along), Vector3.one * size, swap);
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

        Fitted(T(HumanBodyBones.UpperChest) != null ? HumanBodyBones.UpperChest : T(HumanBodyBones.Chest) != null ? HumanBodyBones.Chest : HumanBodyBones.Spine, bones ? "sk_ribcage" : "kb_ribcage", 0.3f, 0.85f);
        Fitted(HumanBodyBones.Hips, "kb_pelvis", 0.26f, 0.8f);
        Fitted(HumanBodyBones.Head, bones ? "sk_skull" : "kb_skull", 0.2f, 0.85f);

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
            Style.Part(hand.name, bones ? "sk_palm" : "kb_palm", Vector3.zero, Quaternion.LookRotation(back, fingers.normalized), Vector3.one * (fingers.magnitude / 0.095f), swap);
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
                    if (bones) Style.Part(t.name, "sk_bone", Vector3.zero, Quaternion.LookRotation(backL, along.normalized), new Vector3(r * 0.8f / 0.05f, along.magnitude, r * 0.8f / 0.05f), swap);
                    else Style.Part(t.name, "kb_finger", Vector3.zero, Quaternion.LookRotation(backL, along.normalized), new Vector3(r / 0.01f, along.magnitude, r / 0.01f), swap);
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
            Style.Part(foot.name, bones ? "sk_foot" : "kb_foot", Vector3.zero, Quaternion.LookRotation(toward.normalized, up), Vector3.one * Mathf.Clamp(toward.magnitude / 0.16f, 0.6f, 1.8f), swap);
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

    public static string Describe() => $"WARDROBE wearing {_wornBody ?? "-"} + {_wornGlove ?? "-"}, {Style.PieceCount} pieces | {HeroShell.Describe()}";
}
