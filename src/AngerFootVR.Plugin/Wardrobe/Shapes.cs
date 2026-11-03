using System.Collections.Generic;
using UnityEngine;

namespace AngerFootVR.Wardrobe;

/// <summary>The wardrobe's simple shapes, made once: a torus (cuffs, knuckle rings, ribs) and a cone (studs).</summary>
internal static class Shapes
{
    private static Mesh _torus, _cone;

    /// <summary>A torus round the z axis: ring radius 1, tube radius 0.3.</summary>
    public static Mesh Torus()
    {
        if (_torus != null) return _torus;
        const int n = 24, m = 10;
        const float tube = 0.3f;
        var v = new List<Vector3>();
        var nr = new List<Vector3>();
        var t = new List<int>();
        for (var i = 0; i <= n; i++)
        {
            var a = i * Mathf.PI * 2f / n;
            var ring = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            for (var j = 0; j <= m; j++)
            {
                var b = j * Mathf.PI * 2f / m;
                var dir = ring * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b);
                v.Add(ring + dir * tube);
                nr.Add(dir);
            }
        }

        for (var i = 0; i < n; i++)
        for (var j = 0; j < m; j++)
        {
            int a0 = i * (m + 1) + j, a1 = a0 + 1, b0 = (i + 1) * (m + 1) + j, b1 = b0 + 1;
            t.AddRange(new[] { a0, a1, b0, b0, a1, b1 });
        }

        _torus = new Mesh { name = "AngerFootVR torus" };
        _torus.SetVertices(v);
        _torus.SetNormals(nr);
        _torus.SetTriangles(t, 0);
        _torus.RecalculateBounds();
        return _torus;
    }

    /// <summary>A cone, base radius 0.5 at y 0, its tip at y 1.</summary>
    public static Mesh Cone()
    {
        if (_cone != null) return _cone;
        const int n = 12;
        var v = new List<Vector3>();
        var t = new List<int>();
        for (var i = 0; i < n; i++)
        {
            var a = i * Mathf.PI * 2f / n;
            var b = (i + 1) * Mathf.PI * 2f / n;
            var p0 = new Vector3(Mathf.Cos(a) * 0.5f, 0f, Mathf.Sin(a) * 0.5f);
            var p1 = new Vector3(Mathf.Cos(b) * 0.5f, 0f, Mathf.Sin(b) * 0.5f);
            var k = v.Count;
            v.Add(p0);
            v.Add(Vector3.up);
            v.Add(p1);
            t.AddRange(new[] { k, k + 1, k + 2 });
            k = v.Count;
            v.Add(p0);
            v.Add(p1);
            v.Add(Vector3.zero);
            t.AddRange(new[] { k, k + 1, k + 2 });
        }

        _cone = new Mesh { name = "AngerFootVR cone" };
        _cone.SetVertices(v);
        _cone.SetTriangles(t, 0);
        _cone.RecalculateNormals();
        _cone.RecalculateBounds();
        return _cone;
    }
}
