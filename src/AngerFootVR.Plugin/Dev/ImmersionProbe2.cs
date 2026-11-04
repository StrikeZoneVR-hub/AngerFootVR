using System.Linq;
using System.Text;
using Framework;
using UnityEngine;

namespace AngerFootVR.Dev;

/// <summary>
/// Dev (the 1.2 immersion build): "probe2 skin": the nearest living goon's renderers (sub-meshes, bones, the character
/// shader's properties) and whether its UVs overlap (mirrored halves would paint one wound on both sides), and the same
/// for your own body; "probe2 fp": the first-person rig's finger chains (names, lengths) for the gun in hand, and where
/// its left hand is against the gun (on a rifle's front: the support hand's own pose).
/// </summary>
internal static class ImmersionProbe2
{
    public static string Run(string what)
    {
        var sb = new StringBuilder("PROBE2 " + what);
        try
        {
            if (what == "fp") Fp(sb);
            else Skin(sb);
        }
        catch (System.Exception e)
        {
            sb.Append("\n FAILED " + e);
        }

        return sb.ToString();
    }

    private static void Skin(StringBuilder sb)
    {
        var head = Core.VrRig.HeadPosition;
        var enemy = Object.FindObjectsOfType<Enemy>().Where(e => e != null && e.Health != null && e.Health.IsAlive && e.Rig is HumanoidRig).OrderBy(e => (e.transform.position - head).sqrMagnitude).FirstOrDefault();
        if (enemy != null) Renderers(sb, "ENEMY " + enemy.name, enemy.gameObject);
        var body = GameObject.Find("AngerFootVR Body");
        if (body != null) Renderers(sb, "HERO", body);
    }

    private static void Renderers(StringBuilder sb, string label, GameObject root)
    {
        sb.Append($"\n {label}");
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            var smr = r as SkinnedMeshRenderer;
            var mesh = smr != null ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
            sb.Append($"\n  R {r.name} [{r.GetType().Name}] layer {r.gameObject.layer} active {r.gameObject.activeInHierarchy} mesh {(mesh != null ? mesh.name : "-")} subs {(mesh != null ? mesh.subMeshCount : 0)} verts {(mesh != null ? mesh.vertexCount : 0)} bounds {r.bounds.size:F2}");
            if (smr != null) sb.Append($" bones {smr.bones.Length} root {(smr.rootBone != null ? smr.rootBone.name : "-")} quality {smr.quality} offscreen {smr.updateWhenOffscreen}");
            foreach (var m in r.sharedMaterials)
                if (m != null)
                    sb.Append($"\n    MAT {m.name} '{m.shader.name}' q {m.renderQueue} kw [{string.Join(",", m.shaderKeywords)}]");
            if (smr == null || mesh == null || mesh.vertexCount < 1500) continue;
            // the shader's properties (once a renderer)
            var sh = smr.sharedMaterial != null ? smr.sharedMaterial.shader : null;
            if (sh != null)
            {
                var props = new StringBuilder();
                for (var i = 0; i < sh.GetPropertyCount(); i++)
                {
                    var n = sh.GetPropertyName(i);
                    var t = sh.GetPropertyType(i);
                    var m = smr.sharedMaterial;
                    var v = t == UnityEngine.Rendering.ShaderPropertyType.Color ? m.GetColor(n).ToString() :
                        t == UnityEngine.Rendering.ShaderPropertyType.Vector ? m.GetVector(n).ToString() :
                        t == UnityEngine.Rendering.ShaderPropertyType.Texture ? (m.GetTexture(n) != null ? m.GetTexture(n).name + " " + m.GetTexture(n).width + "x" + m.GetTexture(n).height : "null") :
                        m.GetFloat(n).ToString("F3");
                    props.Append($" {n}={v}");
                }

                sb.Append("\n    PROPS" + props);
            }

            // the UVs: how much of the texture more than one triangle covers (mirrored halves: about half)
            var baked = new Mesh();
            smr.BakeMesh(baked);
            for (var s = 0; s < baked.subMeshCount; s++)
            {
                var uv = baked.uv;
                var tris = baked.GetTriangles(s);
                const int N = 256;
                var cover = new byte[N * N];
                long covered = 0, twice = 0, outside = 0;
                for (var i = 0; i + 2 < tris.Length; i += 3)
                {
                    var a = uv[tris[i]];
                    var b = uv[tris[i + 1]];
                    var c = uv[tris[i + 2]];
                    if (a.x < 0 || a.x > 1 || a.y < 0 || a.y > 1) outside++;
                    var minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) * N), 0, N - 1);
                    var maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) * N), 0, N - 1);
                    var minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)) * N), 0, N - 1);
                    var maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y)) * N), 0, N - 1);
                    for (var y = minY; y <= maxY; y++)
                    for (var x = minX; x <= maxX; x++)
                    {
                        var p = new Vector2((x + 0.5f) / N, (y + 0.5f) / N);
                        if (!Inside(p, a, b, c)) continue;
                        var k = y * N + x;
                        if (cover[k] == 1) twice++;
                        if (cover[k] == 0) covered++;
                        if (cover[k] < 255) cover[k]++;
                    }
                }

                sb.Append($"\n    UV sub {s}: {tris.Length / 3} tris, covers {covered * 100f / (N * N):F0}% of the texture, {twice * 100f / Mathf.Max(1, covered):F0}% of that more than once, {outside} tris outside 0-1; tangents {baked.tangents.Length > 0}");
            }

            Object.Destroy(baked);
        }

        foreach (var c in root.GetComponentsInChildren<Collider>(true).Take(24))
            sb.Append($" | C {c.name} L{c.gameObject.layer}{(c.isTrigger ? " trig" : "")} {c.GetType().Name}");
    }

    private static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float Cross(Vector2 u, Vector2 v, Vector2 w) => (v.x - u.x) * (w.y - u.y) - (v.y - u.y) * (w.x - u.x);
        var d1 = Cross(a, b, p);
        var d2 = Cross(b, c, p);
        var d3 = Cross(c, a, p);
        var neg = d1 < 0 || d2 < 0 || d3 < 0;
        var pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }

    private static void Fp(StringBuilder sb)
    {
        if (!SingletonBehaviour<Player>.HasInstance) return;
        var player = SingletonBehaviour<Player>.Instance;
        var rig = player.HandRig;
        if (rig == null) return;
        sb.Append($"\n weapon {(player.Weapon != null ? player.Weapon.name + " " + player.Weapon.HoldableObjectType : "none")} rig scale {rig.transform.lossyScale:F3}");
        var all = rig.GetComponentsInChildren<Transform>(true);
        Transform Find(string n) => all.FirstOrDefault(t => t.name == "FPSHands_03_Rig:" + n);
        foreach (var side in new[] { "R", "L" })
        {
            var wrist = Find("Wrist_" + side);
            if (wrist == null) continue;
            sb.Append($"\n {side} wrist at {rig.transform.InverseTransformPoint(wrist.position):F3} (rig)");
            foreach (var f in new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" })
            {
                var chain = new StringBuilder();
                Transform prev = null;
                for (var j = 1; j <= 4; j++)
                {
                    var t = Find($"{f}Finger{j}_{side}");
                    if (t == null) continue;
                    chain.Append($" {j}:{(t.parent != null ? t.parent.name.Replace("FPSHands_03_Rig:", "") : "-")}>{(prev != null ? (t.position - prev.position).magnitude : (t.position - wrist.position).magnitude):F3}");
                    prev = t;
                }

                sb.Append($"\n  {f}{chain}");
            }
        }

        // the gun's own parts: baked, and how near each wrist's palm is to them
        var parts = rig.GetComponentsInChildren<SkinnedMeshRenderer>(false).Where(r => r.sharedMaterial != null && !r.sharedMaterial.shader.name.Contains("CHARACTERS")).ToArray();
        var wr = Find("Wrist_R");
        var wl = Find("Wrist_L");
        var mr = Find("MiddleFinger1_R");
        var ml = Find("MiddleFinger1_L");
        foreach (var p in parts)
        {
            var baked = new Mesh();
            p.BakeMesh(baked);
            var v = baked.vertices;
            float dr = float.MaxValue, dl = float.MaxValue;
            var palmR = wr != null && mr != null ? (wr.position + mr.position) * 0.5f : Vector3.zero;
            var palmL = wl != null && ml != null ? (wl.position + ml.position) * 0.5f : Vector3.zero;
            var scaleCheck = baked.bounds.size.magnitude / Mathf.Max(1e-4f, p.bounds.size.magnitude);
            foreach (var x in v)
            {
                var w = p.transform.TransformPoint(x);
                dr = Mathf.Min(dr, (w - palmR).magnitude);
                dl = Mathf.Min(dl, (w - palmL).magnitude);
            }

            sb.Append($"\n PART {p.name} verts {v.Length} tris {baked.triangles.Length / 3} root {(p.rootBone != null ? p.rootBone.name : "-")} bones {p.bones.Length} ({string.Join(",", p.bones.Take(4).Select(b => b != null ? b.name.Replace("FPSHands_03_Rig:", "") : "null").ToArray())}) baked/world size {scaleCheck:F2} smr scale {p.transform.lossyScale:F2} nearest to R palm {dr:F3} m, L palm {dl:F3} m");
            Object.Destroy(baked);
        }
    }
}
