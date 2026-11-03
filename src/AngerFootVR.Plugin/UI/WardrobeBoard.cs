using AngerFootVR.Core;
using TMPro;
using UnityEngine;
using W = AngerFootVR.Wardrobe.Wardrobe;

namespace AngerFootVR.UI;

/// <summary>
/// THE WARDROBE in the movie house: a board beside the mirror. BODY and GLOVES, each with arrows either side: point at
/// one and pull the trigger (or touch it) and you're wearing the next, there in the mirror. Every one is open from the
/// start.
/// </summary>
internal static class WardrobeBoard
{
    private static readonly Vector3 At = new Vector3(-2.1f, 0f, -4.4f); // (beside the mirror, toward the front wall)
    private static TextMeshPro _body, _bodyBlurb, _glove, _gloveBlurb, _count;
    private static int _bodyAt = -1, _gloveAt = -1;
    private static float _nextShow;
    private static bool _built;

    public static void Init()
    {
        FunWorld.Built += () => FunWorld.Soon("wardrobe", Build);
        FunWorld.Tick += Tick;
        FunWorld.Clearing += () =>
        {
            _built = false;
            _body = _glove = null;
        };
    }

    private static void Build()
    {
        var root = FunWorld.Root;
        if (root == null) return;
        // the board: a dark deco panel in a gold frame on a post, its face toward the room (+x)
        Panel(root, At + new Vector3(-0.02f, 1.3f, 0f), new Vector3(0.04f, 1.75f, 1.0f), "deco_dark");
        Panel(root, At + new Vector3(-0.025f, 1.3f, 0f), new Vector3(0.035f, 1.82f, 1.07f), "deco_gold");
        Panel(root, At + new Vector3(-0.03f, 0.21f, 0f), new Vector3(0.06f, 0.42f, 0.12f), "metal_dark");
        Panel(root, At + new Vector3(-0.03f, 0.015f, 0f), new Vector3(0.36f, 0.03f, 0.5f), "metal_dark");
        FunWorld.Solid(At + new Vector3(-0.02f, 1.11f, 0f), new Vector3(0.06f, 2.22f, 1.07f));
        MovieHouse.Block(At, 0.12f, 0.5f);
        var x = At.x + 0.03f;
        Label("WARDROBE", new Vector3(x, 2.03f, At.z), 0.12f, new Color(1f, 0.16f, 0.62f), 0.9f);
        Label("BODY", new Vector3(x, 1.84f, At.z), 0.08f, new Color(1f, 0.85f, 0.25f), 0.9f);
        _body = Name(new Vector3(x, 1.69f, At.z));
        _bodyBlurb = Blurb(new Vector3(x, 1.54f, At.z));
        Label("GLOVES", new Vector3(x, 1.32f, At.z), 0.08f, new Color(1f, 0.85f, 0.25f), 0.9f);
        _glove = Name(new Vector3(x, 1.19f, At.z));
        _gloveBlurb = Blurb(new Vector3(x, 1.04f, At.z));
        _count = Label("", new Vector3(x, 0.72f, At.z), 0.036f, new Color(0.8f, 0.8f, 0.85f), 0.88f);
        foreach (var (y, gloves) in new[] { (1.69f, false), (1.19f, true) })
            foreach (var dir in new[] { -1, 1 })
            {
                // (facing the board from the room (+x), +z is to your right)
                var z = At.z + dir * 0.4f;
                Label(dir < 0 ? "<" : ">", new Vector3(x, y, z), 0.14f, new Color(1f, 0.16f, 0.62f), 0.15f);
                var b = FunWorld.Solid(new Vector3(x + 0.02f, y, z), new Vector3(0.08f, 0.2f, 0.16f));
                b.name = "Wardrobe " + (gloves ? "gloves " : "body ") + (dir < 0 ? "back" : "next");
                var d = dir;
                var g = gloves;
                FunWorld.MakePress(b, h => Step(g, d), 0.12f);
            }

        _built = true;
        _bodyAt = W.IndexOf(W.Bodies, (W.DevBody?.Id) ?? VrSettings.BodyStyle.Value);
        _gloveAt = W.IndexOf(W.Gloves, (W.DevGlove?.Id) ?? VrSettings.Gloves.Value);
        Show();
    }

    private static void Panel(Transform root, Vector3 local, Vector3 size, string mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(go.GetComponent<Collider>());
        go.name = "Wardrobe board";
        go.layer = VrUi.Layer;
        go.transform.SetParent(root, false);
        go.transform.localPosition = local;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = Props.Mat(mat);
    }

    /// <summary>An item's name: one line between the arrows, smaller for a long one (it had wrapped onto the row below).</summary>
    private static TextMeshPro Name(Vector3 local)
    {
        var t = Label("", local, 0.07f, new Color(0.15f, 0.95f, 1f), 0.64f);
        if (t == null) return null;
        t.enableWordWrapping = false;
        t.enableAutoSizing = true;
        t.fontSizeMax = 0.7f;
        t.fontSizeMin = 0.35f;
        t.rectTransform.sizeDelta = new Vector2(0.64f, 0.1f);
        return t;
    }

    /// <summary>An item's line about it: two lines at most, smaller if it needs more.</summary>
    private static TextMeshPro Blurb(Vector3 local)
    {
        var t = Label("", local, 0.042f, new Color(0.9f, 0.9f, 0.9f), 0.7f);
        if (t == null) return null;
        t.enableAutoSizing = true;
        t.fontSizeMax = 0.42f;
        t.fontSizeMin = 0.26f;
        t.rectTransform.sizeDelta = new Vector2(0.7f, 0.11f);
        return t;
    }

    private static TextMeshPro Label(string text, Vector3 local, float size, Color c, float width)
    {
        var t = MovieHouse.SignFacing(text, local, size, c, Vector3.right, FunWorld.Root);
        if (t != null)
        {
            t.rectTransform.sizeDelta = new Vector2(width, size * 4f);
            t.enableWordWrapping = true;
        }

        return t;
    }

    /// <summary>The next (or last) body style or pair of gloves: worn if it's open.</summary>
    private static void Step(bool gloves, int dir)
    {
        var list = gloves ? W.Gloves : W.Bodies;
        var i = Mathf.Max(0, gloves ? _gloveAt : _bodyAt);
        i = (i + dir + list.Count) % list.Count;
        if (gloves) _gloveAt = i;
        else _bodyAt = i;
        var item = list[i];
        W.Choose(item);
        GameSounds.PlayOr("Shoe Short shoe", "step", CinemaRig.HeadPosition, 0.7f);
        Show();
    }

    private static void Tick()
    {
        if (!_built || Time.unscaledTime < _nextShow) return;
        _nextShow = Time.unscaledTime + 1f;
        Show();
    }

    private static void Show()
    {
        if (_body == null) return;
        void Row(TextMeshPro name, TextMeshPro blurb, System.Collections.Generic.List<W.Item> list, int at)
        {
            var item = list[Mathf.Clamp(at < 0 ? 0 : at, 0, list.Count - 1)];
            name.text = item.Name;
            blurb.text = item.Blurb.ToUpperInvariant();
        }

        Row(_body, _bodyBlurb, W.Bodies, _bodyAt);
        Row(_glove, _gloveBlurb, W.Gloves, _gloveAt);
        var b = Mathf.Clamp(_bodyAt, 0, W.Bodies.Count - 1) + 1;
        var g = Mathf.Clamp(_gloveAt, 0, W.Gloves.Count - 1) + 1;
        _count.text = $"BODY {b} OF {W.Bodies.Count}   GLOVES {g} OF {W.Gloves.Count}\nTHE MIRROR'S RIGHT BESIDE YOU";
    }
}
