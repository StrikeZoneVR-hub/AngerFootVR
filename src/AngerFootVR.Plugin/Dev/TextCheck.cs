using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace AngerFootVR.Dev;

/// <summary>
/// Dev: every label in the movie house (its signs, boards, buttons' names) that runs into another one. Two checks: on
/// the wall (two pieces of text facing the same way, near the same plane, whose letters overlap), and as you'd see
/// them (from the places you stand in each room, one in front of another with less than a little gap between them,
/// both readable from there). A sign's own glow (its second, fainter copy) doesn't count. Also the ones whose text
/// spills out of its own box sideways.
/// </summary>
internal static class TextCheck
{
    private struct Label
    {
        public TextMeshPro T;
        public Vector3 Centre, Normal, Right, Up; // (Normal: the side it's read from)
        public float HalfW, HalfH;
        public string Text;
    }

    // where you stand (movie house local, x z) and look round from, room by room
    private static readonly (string name, Vector2 at)[] Views =
    {
        ("lobby", new Vector2(0f, -3.2f)), ("aisle", new Vector2(0f, 1.5f)), ("left wall", new Vector2(-5.4f, -0.4f)), ("right wall", new Vector2(5.4f, -0.4f)),
        ("back left", new Vector2(-4.5f, -5f)), ("back right", new Vector2(5f, -5f)), ("rage room", new Vector2(4f, -9f)), ("rage back", new Vector2(4f, -13f)),
    };

    public static string Run()
    {
        var root = UI.MovieHouse.Root;
        if (root == null) return "TEXTCHECK no movie house";
        var labels = new List<Label>();
        foreach (var t in Object.FindObjectsOfType<TextMeshPro>())
        {
            if (t == null || !t.isActiveAndEnabled || string.IsNullOrEmpty(t.text) || t.text.Trim().Length == 0) continue;
            if (t.transform.parent != null && t.transform.parent.GetComponent<TextMeshPro>() != null) continue; // (a glow)
            if ((t.transform.position - root.position).magnitude > 60f) continue;
            t.ForceMeshUpdate();
            var b = t.textBounds;
            if (b.size.x <= 0f || b.size.y <= 0f) continue;
            var tr = t.transform;
            var right = tr.TransformVector(new Vector3(b.extents.x, 0f, 0f));
            var up = tr.TransformVector(new Vector3(0f, b.extents.y, 0f));
            labels.Add(new Label
            {
                T = t, Centre = tr.TransformPoint(b.center), Normal = -tr.forward, Right = right.normalized, Up = up.normalized,
                HalfW = right.magnitude, HalfH = up.magnitude, Text = Clean(t.text),
            });
        }

        var sb = new StringBuilder();
        var wall = 0;
        for (var i = 0; i < labels.Count; i++)
        for (var j = i + 1; j < labels.Count; j++)
        {
            var a = labels[i];
            var b = labels[j];
            if (Vector3.Dot(a.Normal, b.Normal) < 0.7f) continue; // (facing other ways: not read together)
            if (a.Text == b.Text && (a.Centre - b.Centre).magnitude < 0.05f) continue; // (a sign and its own glow)
            var d = b.Centre - a.Centre;
            if (Mathf.Abs(Vector3.Dot(d, a.Normal)) > 0.35f) continue; // (far apart in depth: the view check's)
            var bc = new Vector2(Vector3.Dot(d, a.Right), Vector3.Dot(d, a.Up));
            var bx = Mathf.Abs(Vector3.Dot(b.Right, a.Right)) * b.HalfW + Mathf.Abs(Vector3.Dot(b.Up, a.Right)) * b.HalfH;
            var by = Mathf.Abs(Vector3.Dot(b.Right, a.Up)) * b.HalfW + Mathf.Abs(Vector3.Dot(b.Up, a.Up)) * b.HalfH;
            // (a small gap between lines counts too: touching letters read as one line)
            var ox = Mathf.Min(a.HalfW, bc.x + bx) - Mathf.Max(-a.HalfW, bc.x - bx);
            var oy = Mathf.Min(a.HalfH, bc.y + by) - Mathf.Max(-a.HalfH, bc.y - by);
            var gap = 0.015f;
            if (ox <= -gap || oy <= -gap) continue;
            wall++;
            sb.Append($"\nWALL '{a.Text}' @{Local(a.Centre)} x '{b.Text}' @{Local(b.Centre)} overlap {ox:F2}x{oy:F2} m");
        }

        // as seen: each label's box as angles from the eye, nearer one in front of a farther one
        var seen = 0;
        var reported = new HashSet<(int, int)>();
        foreach (var (name, at) in Views)
        {
            var eye = root.TransformPoint(new Vector3(at.x, 1.65f, at.y));
            var shown = new List<(int i, float dist)>();
            for (var i = 0; i < labels.Count; i++)
            {
                var l = labels[i];
                var to = eye - l.Centre;
                var dist = to.magnitude;
                if (dist > 10f || dist < 0.3f || Vector3.Dot(to.normalized, l.Normal) < 0.25f) continue; // (too far, or not read from here)
                if (Hidden(eye, l.Centre)) continue; // (behind a wall, a board, a counter)
                shown.Add((i, dist));
            }

            for (var x = 0; x < shown.Count; x++)
            for (var y = x + 1; y < shown.Count; y++)
            {
                var (i, di) = shown[x];
                var (j, dj) = shown[y];
                var key = (Mathf.Min(i, j), Mathf.Max(i, j));
                if (reported.Contains(key)) continue;
                if (labels[i].Text == labels[j].Text && (labels[i].Centre - labels[j].Centre).magnitude < 0.05f) continue; // (its own glow)
                var deg = Seen(eye, labels[i], labels[j]);
                if (deg < 0.3f) continue; // (degrees into each other: under that, a sliver of a corner)
                reported.Add(key);
                seen++;
                sb.Append($"\nSEEN from {name}: '{labels[i].Text}' @{Local(labels[i].Centre)} ({di:F1} m) x '{labels[j].Text}' @{Local(labels[j].Centre)} ({dj:F1} m) into each other {deg:F1} deg");
            }
        }

        foreach (var l in labels)
        {
            var box = l.T.rectTransform.rect;
            var w = box.width * l.T.transform.lossyScale.x;
            if (l.T.enableWordWrapping && l.HalfW * 2f > w + 0.02f)
                sb.Append($"\nSPILLS '{l.Text}' @{Local(l.Centre)} {l.HalfW * 2f:F2} m wide in a {w:F2} m box");
        }

        return $"TEXTCHECK {labels.Count} labels: {wall} on walls, {seen} as seen" + sb;
    }

    /// <summary>One of the hall's walls between the eye and there (only the rage room's doorway lets you see through).</summary>
    private static bool Hidden(Vector3 eye, Vector3 at)
    {
        var root = UI.MovieHouse.Root;
        var e = root.InverseTransformPoint(eye);
        var a = root.InverseTransformPoint(at);
        return Crosses(e, a, 2, -5.97f, out var p) && !(p.y < 2.42f && Mathf.Abs(p.x - UI.MovieHouse.DoorX) < 0.95f);
    }

    private static bool Crosses(Vector3 a, Vector3 b, int axis, float plane, out Vector3 at)
    {
        at = default;
        var da = a[axis] - plane;
        var db = b[axis] - plane;
        if (da * db >= 0f) return false;
        at = Vector3.Lerp(a, b, da / (da - db));
        return true;
    }

    /// <summary>How far two labels run into each other as the eye sees them (degrees; 0: apart): both boxes projected
    /// flat onto the view, their shapes tested edge by edge.</summary>
    private static float Seen(Vector3 eye, Label a, Label b)
    {
        var f = ((a.Centre + b.Centre) * 0.5f - eye).normalized;
        var r = Vector3.Cross(Vector3.up, f).normalized;
        if (r.sqrMagnitude < 1e-4f) r = Vector3.right;
        var u = Vector3.Cross(f, r);
        var pa = Flat(eye, a, f, r, u);
        var pb = Flat(eye, b, f, r, u);
        if (pa == null || pb == null) return 0f;
        var depth = float.MaxValue;
        foreach (var poly in new[] { pa, pb })
            for (var k = 0; k < 4; k++)
            {
                var e = poly[(k + 1) % 4] - poly[k];
                var axis = new Vector2(-e.y, e.x).normalized;
                float a0 = float.MaxValue, a1 = float.MinValue, b0 = float.MaxValue, b1 = float.MinValue;
                foreach (var q in pa)
                {
                    var t = Vector2.Dot(q, axis);
                    a0 = Mathf.Min(a0, t);
                    a1 = Mathf.Max(a1, t);
                }

                foreach (var q in pb)
                {
                    var t = Vector2.Dot(q, axis);
                    b0 = Mathf.Min(b0, t);
                    b1 = Mathf.Max(b1, t);
                }

                var over = Mathf.Min(a1, b1) - Mathf.Max(a0, b0);
                if (over <= 0f) return 0f;
                depth = Mathf.Min(depth, over);
            }

        return Mathf.Atan(depth) * Mathf.Rad2Deg;
    }

    private static Vector2[] Flat(Vector3 eye, Label l, Vector3 f, Vector3 r, Vector3 u)
    {
        var pts = new Vector2[4];
        var k = 0;
        foreach (var (sx, sy) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
        {
            var p = l.Centre + l.Right * (l.HalfW * sx) + l.Up * (l.HalfH * sy) - eye;
            var z = Vector3.Dot(p, f);
            if (z <= 0.05f) return null;
            pts[k++] = new Vector2(Vector3.Dot(p, r) / z, Vector3.Dot(p, u) / z);
        }

        return pts;
    }

    private static string Local(Vector3 w) => UI.MovieHouse.Root.InverseTransformPoint(w).ToString("F2");

    private static string Clean(string s)
    {
        s = System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", "").Replace("\n", " / ");
        return s.Length > 40 ? s.Substring(0, 40) + "..." : s;
    }
}
