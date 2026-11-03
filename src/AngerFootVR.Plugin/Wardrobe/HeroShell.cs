using System.Collections.Generic;
using UnityEngine;

namespace AngerFootVR.Wardrobe;

/// <summary>
/// The hero's skin as the mod can use it: the game ships the hero's meshes locked (unreadable), so a readable copy is
/// rebuilt from the player's own copy of the game, once a session. A snapshot (BakeMesh, which works on locked meshes)
/// gives its shape; its skinning is recovered by moving each bone a little and seeing which way each point moves
/// (with linear skinning a point moves by its weight on the bones moved: a bone and the bones under it move together,
/// so each bone's own weight is what moves with it less what moves with the bones below it). The copy is skinned to
/// the same bones, so the wardrobe can make fitted shells of it (a glove over the hand, a suit over the body) that
/// follow every finger and joint exactly. Nothing of the game's is stored or shipped: it's rebuilt each session.
/// Built a bone a frame on the hero model's still copy (HeroModel.Template), so it never stalls a frame.
/// </summary>
internal sealed class HeroShell
{
    public string Name;
    public string[] BoneNames;
    public Matrix4x4[] Bind;
    public Vector3[] Verts, Normals;
    public Vector2[] Uv;
    public int[] Tris;
    public BoneWeight[] Weights;
    public float WeightError; // (the worst point's weights, summed, away from 1: the recovery's check)

    /// <summary>Each bone's own flesh: the points it carries most, in the bone's own space (for pieces sized to it).</summary>
    public Bounds[] Flesh;

    /// <summary>Where each bone stood at the snapshot, and the hero's up and forward then (all in the snapshot's space).</summary>
    public Vector3[] BonePos;

    public Vector3 Up = Vector3.up, Forward = Vector3.forward;

    public int Bone(string name) => System.Array.IndexOf(BoneNames, name);

    /// <summary>The bone a point is carried most by.</summary>
    public int Main(int i)
    {
        var w = Weights[i];
        var best = w.boneIndex0;
        var bw = w.weight0;
        if (w.weight1 > bw)
        {
            best = w.boneIndex1;
            bw = w.weight1;
        }

        if (w.weight2 > bw)
        {
            best = w.boneIndex2;
            bw = w.weight2;
        }

        if (w.weight3 > bw) best = w.boneIndex3;
        return best;
    }

    private void Finish(Transform root, Transform smr)
    {
        BonePos = new Vector3[Bind.Length];
        for (var j = 0; j < Bind.Length; j++) BonePos[j] = Bind[j].inverse.MultiplyPoint3x4(Vector3.zero);
        Flesh = new Bounds[Bind.Length];
        var seen = new bool[Bind.Length];
        for (var i = 0; i < Verts.Length; i++)
        {
            var j = Main(i);
            var p = Bind[j].MultiplyPoint3x4(Verts[i]);
            if (!seen[j])
            {
                Flesh[j] = new Bounds(p, Vector3.zero);
                seen[j] = true;
            }
            else Flesh[j].Encapsulate(p);
        }

        if (root != null)
        {
            Up = smr.InverseTransformDirection(root.up).normalized;
            Forward = smr.InverseTransformDirection(root.forward).normalized;
        }
    }

    private static readonly Dictionary<string, HeroShell> Built = new Dictionary<string, HeroShell>();
    private static readonly Dictionary<string, Builder> Building = new Dictionary<string, Builder>();

    public static HeroShell Get(string rendererName) => Built.TryGetValue(rendererName, out var s) ? s : null;

    public static bool IsBuilding => Building.Count > 0;

    public static string Describe()
    {
        var parts = new List<string>();
        foreach (var kv in Built) parts.Add($"{kv.Key} {kv.Value.Verts.Length}v err {kv.Value.WeightError:F3}");
        foreach (var kv in Building) parts.Add($"{kv.Key} building {kv.Value.Next}/{kv.Value.Count}");
        return "SHELLS " + string.Join(", ", parts.ToArray());
    }

    /// <summary>Starts rebuilding these of the hero's renderers (by name), a bone a frame.</summary>
    public static void Request(params string[] rendererNames)
    {
        var t = Body.HeroModel.Template;
        if (t == null) return;
        foreach (var name in rendererNames)
        {
            if (Built.ContainsKey(name) || Building.ContainsKey(name)) continue;
            foreach (var smr in t.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.name == name)
                {
                    Building[name] = new Builder(smr);
                    break;
                }
        }
    }

    /// <summary>Every frame: a step of each rebuild under way.</summary>
    public static void Update()
    {
        if (Building.Count == 0) return;
        var done = new List<string>();
        foreach (var kv in Building)
        {
            try
            {
                if (kv.Value.Step(out var shell))
                {
                    if (shell != null) Built[kv.Key] = shell;
                    done.Add(kv.Key);
                }
            }
            catch (System.Exception e)
            {
                Plugin.L.LogWarning($"Wardrobe: rebuilding '{kv.Key}' failed ({e.Message})");
                done.Add(kv.Key);
            }
        }

        foreach (var d in done) Building.Remove(d);
    }

    private sealed class Builder
    {
        private readonly SkinnedMeshRenderer _smr;
        private readonly Transform[] _bones;
        private readonly int[] _parent; // (each bone's nearest ancestor among the renderer's bones, -1 none)
        private readonly Mesh _bake = new Mesh();
        private Vector3[] _base;
        private float[][] _moved; // per bone, per point: how much moved with it
        private readonly System.Diagnostics.Stopwatch _time = new System.Diagnostics.Stopwatch();
        public int Next = -1;
        public int Count => _bones.Length;
        private const float Nudge = 0.1f; // m

        public Builder(SkinnedMeshRenderer smr)
        {
            _smr = smr;
            _bones = smr.bones;
            _parent = new int[_bones.Length];
            for (var i = 0; i < _bones.Length; i++)
            {
                _parent[i] = -1;
                if (_bones[i] == null) continue;
                for (var p = _bones[i].parent; p != null && _parent[i] < 0; p = p.parent)
                    _parent[i] = System.Array.IndexOf(_bones, p);
            }
        }

        /// <summary>One step (the snapshot, then a bone a step); true when done (shell: the result, or null).</summary>
        public bool Step(out HeroShell shell)
        {
            shell = null;
            _time.Start();
            if (Next < 0)
            {
                _smr.BakeMesh(_bake);
                _base = _bake.vertices;
                _moved = new float[_bones.Length][];
                Next = 0;
                _time.Stop();
                return false;
            }

            if (Next < _bones.Length)
            {
                var b = _bones[Next];
                if (b != null)
                {
                    var was = b.position;
                    var dir = _smr.transform.right; // (one way, in the snapshot's own space: its x)
                    b.position = was + dir * Nudge;
                    _smr.BakeMesh(_bake);
                    b.position = was;
                    var v = _bake.vertices;
                    var m = new float[v.Length];
                    var scale = Mathf.Max(1e-6f, _smr.transform.InverseTransformVector(dir * Nudge).x);
                    for (var i = 0; i < v.Length; i++) m[i] = (v[i].x - _base[i].x) / scale;
                    _moved[Next] = m;
                }

                Next++;
                _time.Stop();
                return false;
            }

            // each bone's own weight: what moved with it less what moved with the bones just below it
            var n = _base.Length;
            var own = new float[_bones.Length][];
            for (var j = 0; j < _bones.Length; j++)
            {
                if (_moved[j] == null) continue;
                var w = (float[])_moved[j].Clone();
                for (var c = 0; c < _bones.Length; c++)
                    if (_parent[c] == j && _moved[c] != null)
                        for (var i = 0; i < n; i++)
                            w[i] -= _moved[c][i];
                own[j] = w;
            }

            var weights = new BoneWeight[n];
            var worst = 0f;
            var idx = new int[4];
            var val = new float[4];
            for (var i = 0; i < n; i++)
            {
                for (var k = 0; k < 4; k++)
                {
                    idx[k] = 0;
                    val[k] = 0f;
                }

                var sum = 0f;
                for (var j = 0; j < _bones.Length; j++)
                {
                    if (own[j] == null) continue;
                    var w = own[j][i];
                    if (w <= 0.002f) continue;
                    sum += w;
                    // (the four biggest)
                    for (var k = 0; k < 4; k++)
                        if (w > val[k])
                        {
                            for (var s = 3; s > k; s--)
                            {
                                val[s] = val[s - 1];
                                idx[s] = idx[s - 1];
                            }

                            val[k] = w;
                            idx[k] = j;
                            break;
                        }
                }

                worst = Mathf.Max(worst, Mathf.Abs(sum - 1f));
                var t = val[0] + val[1] + val[2] + val[3];
                if (t < 1e-4f) t = 1f;
                weights[i] = new BoneWeight
                {
                    boneIndex0 = idx[0], weight0 = val[0] / t, boneIndex1 = idx[1], weight1 = val[1] / t,
                    boneIndex2 = idx[2], weight2 = val[2] / t, boneIndex3 = idx[3], weight3 = val[3] / t,
                };
            }

            // the bind poses: each bone as it stood at the snapshot, from the snapshot's own space
            var toWorld = _smr.transform.localToWorldMatrix;
            var bind = new Matrix4x4[_bones.Length];
            var names = new string[_bones.Length];
            for (var j = 0; j < _bones.Length; j++)
            {
                names[j] = _bones[j] != null ? _bones[j].name : "";
                bind[j] = _bones[j] != null ? _bones[j].worldToLocalMatrix * toWorld : Matrix4x4.identity;
            }

            // (the snapshot and the bind poses must agree on scale: the snapshot's vertices are taken as the
            // renderer's own space with its scale; if they came out without it, they're put into it)
            _smr.BakeMesh(_bake);
            shell = new HeroShell
            {
                Name = _smr.name, BoneNames = names, Bind = bind, Verts = _base, Normals = _bake.normals, Uv = _bake.uv, Tris = _bake.triangles, Weights = weights, WeightError = worst,
            };
            shell.Finish(Body.HeroModel.Template != null ? Body.HeroModel.Template.transform : null, _smr.transform);
            _time.Stop();
            Plugin.L.LogInfo($"Wardrobe: '{_smr.name}' rebuilt ({n} points, {_bones.Length} bones, worst weight sum off by {worst:F3}) in {_time.Elapsed.TotalMilliseconds:F0} ms over {_bones.Length + 2} frames");
            Object.Destroy(_bake);
            return true;
        }
    }

    // ------------------------------------------------------------------ shells

    /// <summary>A shell of the part of the skin these bones carry (by name, matched as the start of a bone's name),
    /// pushed out by `out` metres, as a mesh skinned like the skin (bones: the renderer's bone names, in order).</summary>
    public Mesh Shell(System.Func<string, bool> carries, float outward, out float coverage) => Shell(carries, outward, null, out coverage);

    /// <summary>As above, each point coloured (colour: the point's index in the skin, its position and facing in the
    /// snapshot's space; null: white).</summary>
    public Mesh Shell(System.Func<string, bool> carries, float outward, System.Func<int, Vector3, Vector3, Color32> colour, out float coverage)
    {
        var keep = new bool[Verts.Length];
        var boneIn = new bool[BoneNames.Length];
        for (var j = 0; j < BoneNames.Length; j++) boneIn[j] = carries(BoneNames[j]);
        var count = 0;
        for (var i = 0; i < Verts.Length; i++)
        {
            var w = Weights[i];
            var inside = (boneIn[w.boneIndex0] ? w.weight0 : 0f) + (boneIn[w.boneIndex1] ? w.weight1 : 0f) + (boneIn[w.boneIndex2] ? w.weight2 : 0f) + (boneIn[w.boneIndex3] ? w.weight3 : 0f);
            keep[i] = inside > 0.5f;
            if (keep[i]) count++;
        }

        coverage = Verts.Length > 0 ? (float)count / Verts.Length : 0f;
        var map = new int[Verts.Length];
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var weights = new List<BoneWeight>();
        var uvs = new List<Vector2>();
        var cols = new List<Color32>();
        for (var i = 0; i < Verts.Length; i++)
        {
            map[i] = -1;
            if (!keep[i]) continue;
            map[i] = verts.Count;
            var nrm = Normals != null && i < Normals.Length ? Normals[i] : Vector3.zero;
            verts.Add(Verts[i] + nrm * outward);
            normals.Add(nrm);
            weights.Add(Weights[i]);
            uvs.Add(Uv != null && i < Uv.Length ? Uv[i] : Vector2.zero);
            // (vertex colours aren't converted for the game's linear colour space as material colours are: done here)
            var c = colour != null ? (Color)colour(i, Verts[i], nrm) : Color.white;
            cols.Add(QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c);
        }

        var tris = new List<int>();
        for (var t = 0; t + 2 < Tris.Length; t += 3)
        {
            int a = map[Tris[t]], b = map[Tris[t + 1]], c = map[Tris[t + 2]];
            if (a < 0 || b < 0 || c < 0) continue;
            tris.Add(a);
            tris.Add(b);
            tris.Add(c);
        }

        var m = new Mesh { name = "AngerFootVR shell of " + Name };
        if (verts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(verts);
        m.SetNormals(normals);
        m.SetUVs(0, uvs);
        m.SetColors(cols);
        m.SetTriangles(tris, 0);
        m.boneWeights = weights.ToArray();
        m.bindposes = Bind;
        m.RecalculateBounds();
        return m;
    }
}
