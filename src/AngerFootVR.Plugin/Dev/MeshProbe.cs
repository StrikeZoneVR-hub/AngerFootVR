using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using UnityEngine;

namespace AngerFootVR.Dev;

/// <summary>Dev: what the goons' and the hero's meshes are made of (for the cartoon holes and the wardrobe), the hero's
/// skeleton and body written out for Blender (a local modelling reference only, never shipped), and a test hole.</summary>
internal static class MeshProbe
{
    public static string Probe()
    {
        var sb = new StringBuilder("MESHPROBE");
        if (Body.HeroModel.Template != null)
            foreach (var r in Body.HeroModel.Template.GetComponentsInChildren<SkinnedMeshRenderer>(true).Take(6))
                sb.Append($" | hero '{r.name}' mesh '{r.sharedMesh?.name}' readable={r.sharedMesh?.isReadable} verts={r.sharedMesh?.vertexCount} bones={r.bones.Length} subs={r.sharedMesh?.subMeshCount} shader '{r.sharedMaterial?.shader.name}'");
        var seen = 0;
        foreach (var e in Object.FindObjectsOfType<Enemy>())
        {
            if (e == null || seen >= 4) continue;
            seen++;
            foreach (var r in e.GetComponentsInChildren<SkinnedMeshRenderer>(true).Take(4))
                sb.Append($" | goon '{e.name}' '{r.name}' mesh '{r.sharedMesh?.name}' readable={r.sharedMesh?.isReadable} verts={r.sharedMesh?.vertexCount} tris={(r.sharedMesh != null && r.sharedMesh.isReadable ? r.sharedMesh.triangles.Length / 3 : -1)} bones={r.bones.Length} shader '{r.sharedMaterial?.shader.name}'");
        }

        return sb.ToString();
    }

    /// <summary>The hero's body: its skeleton (name, parent, bind pose) and each body mesh (vertices, normals, uvs,
    /// triangles, bone weights) in its bind space, as JSON for tools/blender (a modelling reference, kept locally).</summary>
    public static string ExportHero()
    {
        var t = Body.HeroModel.Template;
        if (t == null) return "no hero model (visit the title first)";
        var dir = Path.Combine(Paths.BepInExRootPath, Path.Combine("AngerFootVR", "hero-export"));
        Directory.CreateDirectory(dir);
        var n = 0;
        foreach (var r in t.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var m = r.sharedMesh;
            if (m == null || !m.isReadable) continue;
            var sb = new StringBuilder();
            sb.Append("{\"name\":\"").Append(r.name).Append("\",\"active\":").Append(r.gameObject.activeSelf ? "true" : "false").Append(",\"bones\":[");
            for (var i = 0; i < r.bones.Length; i++)
            {
                var b = r.bones[i];
                var bp = m.bindposes[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":\"").Append(b != null ? b.name : "").Append("\",\"parent\":\"").Append(b != null && b.parent != null ? b.parent.name : "").Append("\",\"bind\":[");
                for (var k = 0; k < 16; k++) sb.Append(k > 0 ? "," : "").Append(bp[k].ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                sb.Append("]}");
            }

            sb.Append("],\"verts\":[");
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var v = m.vertices;
            for (var i = 0; i < v.Length; i++) sb.Append(i > 0 ? "," : "").Append(v[i].x.ToString("R", inv)).Append(',').Append(v[i].y.ToString("R", inv)).Append(',').Append(v[i].z.ToString("R", inv));
            sb.Append("],\"uv\":[");
            var uv = m.uv;
            for (var i = 0; i < uv.Length; i++) sb.Append(i > 0 ? "," : "").Append(uv[i].x.ToString("R", inv)).Append(',').Append(uv[i].y.ToString("R", inv));
            sb.Append("],\"tris\":[");
            var tr = m.triangles;
            for (var i = 0; i < tr.Length; i++) sb.Append(i > 0 ? "," : "").Append(tr[i]);
            sb.Append("],\"weights\":[");
            var bw = m.boneWeights;
            for (var i = 0; i < bw.Length; i++)
            {
                var w = bw[i];
                sb.Append(i > 0 ? "," : "").Append(w.boneIndex0).Append(',').Append(w.weight0.ToString("R", inv)).Append(',').Append(w.boneIndex1).Append(',').Append(w.weight1.ToString("R", inv))
                    .Append(',').Append(w.boneIndex2).Append(',').Append(w.weight2.ToString("R", inv)).Append(',').Append(w.boneIndex3).Append(',').Append(w.weight3.ToString("R", inv));
            }

            sb.Append("]}");
            File.WriteAllText(Path.Combine(dir, r.name + ".json"), sb.ToString());
            n++;
        }

        return $"hero exported: {n} meshes to {dir}";
    }

    /// <summary>A hole through the goon nearest the middle of your view, from your head (radius in metres): through the
    /// first of its body parts on the way.</summary>
    public static string HoleTest(float radius)
    {
        var head = Core.VrRig.HeadPosition;
        var fwd = Core.VrRig.HeadRotation * Vector3.forward;
        Enemy best = null;
        var bestA = 25f;
        foreach (var e in Object.FindObjectsOfType<Enemy>())
        {
            if (e == null || e.Rig == null) continue;
            var a = Vector3.Angle(fwd, e.Rig.GetCenterPosition() - head);
            if (a < bestA)
            {
                bestA = a;
                best = e;
            }
        }

        if (best == null) return "no goon within 25 degrees of the view";
        var target = best.Rig.GetCenterPosition() + Vector3.up * 0.15f;
        var dir = (target - head).normalized;
        Collider part = null;
        var point = target;
        var nearest = float.MaxValue;
        foreach (var h in Physics.RaycastAll(head, dir, 30f, ~0, QueryTriggerInteraction.Ignore))
            if (h.collider != null && h.collider.transform.IsChildOf(best.transform) && Rendering.Holes.IsPart(h.collider) && h.distance < nearest)
            {
                nearest = h.distance;
                part = h.collider;
                point = h.point;
            }

        var made = radius < 0f ? Rendering.Holes.Test(best.gameObject, part, point, dir, -radius, true) : Rendering.Holes.Test(best.gameObject, part, point, dir, radius);
        return $"hole test on {best.name} {bestA:F0} deg off the view at {(point - head).magnitude:F1} m (part {(part != null ? part.name + " layer " + part.gameObject.layer : "none")}): {made} | {Rendering.Holes.Describe()}";
    }
}
