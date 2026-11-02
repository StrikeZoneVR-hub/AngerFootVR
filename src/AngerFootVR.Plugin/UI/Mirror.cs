using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AngerFootVR.UI;

/// <summary>
/// A real mirror (InkVR's, which the user liked in its theatre): each eye gets its own reflection, rendered from that
/// eye reflected through the glass, so the depth is right in a headset and the reflection stays put as you move. Your
/// whole body shows in it, head and all (the head is drawn only for the mirror: you look out of it). It renders only
/// when the cinema camera can see it.
/// </summary>
internal sealed class Mirror
{
    private static readonly List<Mirror> All = new List<Mirror>();
    private static Camera _cam;
    private static bool _hooked, _rendering;

    private readonly Transform _t;
    private readonly float _w, _h;
    private readonly RenderTexture[] _rt = new RenderTexture[2];
    private readonly Material _mat;

    /// <summary>The camera whose eyes the mirrors reflect (the cinema's).</summary>
    public static Camera Viewer;

    /// <summary>The glass: centre, facing <c>forward</c> (into the room), w by h metres.</summary>
    public Mirror(Transform parent, Vector3 localPos, Quaternion localRot, float w, float h, Color tint, int layer)
    {
        var go = new GameObject("AngerFootVR Mirror") { layer = layer };
        _t = go.transform;
        _t.SetParent(parent, false);
        _t.localPosition = localPos;
        _t.localRotation = localRot;
        _w = w;
        _h = h;

        // the quad faces +z; u runs with local x (the mirror camera's own left to right), so the image lands where it belongs
        var mesh = new Mesh { name = "AngerFootVR MirrorGlass" };
        mesh.vertices = new[] { new Vector3(-w * 0.5f, -h * 0.5f, 0), new Vector3(w * 0.5f, -h * 0.5f, 0), new Vector3(w * 0.5f, h * 0.5f, 0), new Vector3(-w * 0.5f, h * 0.5f, 0) };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        mesh.colors = new[] { tint, tint, tint, tint };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        _mat = new Material(Shader.Find("UI/Default")) { name = "AngerFootVR Mirror", renderQueue = 2002 };
        _mat.SetInt("unity_GUIZTestMode", (int)CompareFunction.LessEqual);
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = _mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;

        var px = 900;
        var py = Mathf.Clamp(Mathf.RoundToInt(px * h / w), 64, 1400);
        for (var i = 0; i < 2; i++)
        {
            _rt[i] = new RenderTexture(px, py, 24) { name = "AngerFootVR MirrorEye" + i, antiAliasing = 2 };
            _rt[i].Create();
            Object.DontDestroyOnLoad(_rt[i]);
        }

        _mat.mainTexture = _rt[0];
        All.Add(this);
        if (!_hooked)
        {
            _hooked = true;
            Camera.onPreCull += BeforeCull;
        }
    }

    private static void BeforeCull(Camera cam)
    {
        if (All.Count == 0 || cam == null || cam != Viewer || _rendering) return;
        var active = cam.stereoActiveEye;
        var eye = active == Camera.MonoOrStereoscopicEye.Right ? 1 : 0;
        var eyePos = cam.stereoEnabled && active != Camera.MonoOrStereoscopicEye.Mono
            ? (Vector3)cam.GetStereoViewMatrix(eye == 1 ? Camera.StereoscopicEye.Right : Camera.StereoscopicEye.Left).inverse.GetColumn(3)
            : cam.transform.position;
        for (var i = All.Count - 1; i >= 0; i--)
        {
            var m = All[i];
            if (m._t == null)
            {
                All.RemoveAt(i);
                continue;
            }

            if (m._t.gameObject.activeInHierarchy) m.Render(cam, eye, eyePos);
        }
    }

    private void Render(Camera main, int eye, Vector3 eyePos)
    {
        var n = _t.forward;
        var c = _t.position;
        var d = Vector3.Dot(eyePos - c, n);
        if (d < 0.02f) return; // behind the glass
        var to = c - eyePos;
        var dist = to.magnitude;
        if (dist > 12f) return;
        if (dist > 1.2f && Vector3.Angle(main.transform.forward, to) > 75f) return; // not in view

        if (_cam == null)
        {
            _cam = new GameObject("AngerFootVR MirrorCamera").AddComponent<Camera>();
            Object.DontDestroyOnLoad(_cam.gameObject);
            _cam.enabled = false;
            _cam.stereoTargetEye = StereoTargetEyeMask.None;
            _cam.renderingPath = RenderingPath.Forward;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.allowHDR = false;
            _cam.allowMSAA = true;
        }

        // the eye, reflected through the glass, looking back through it into the room
        var reflected = eyePos - 2f * d * n;
        _cam.transform.SetPositionAndRotation(reflected, Quaternion.LookRotation(n, _t.up));
        _cam.cullingMask = main.cullingMask | (1 << Body.VrBody.MirrorLayer); // and your body's twin, head and all
        _cam.backgroundColor = main.backgroundColor;

        // a plain frustum straight through the glass, wide enough for its corners, the near plane just past the glass;
        // the glass shows the part of the picture inside its corners
        var s = _t.lossyScale;
        var hw = _w * 0.5f * s.x;
        var hh = _h * 0.5f * s.y;
        var local = _cam.transform.InverseTransformPoint(c);
        var depth = Mathf.Max(0.02f, local.z);
        var tx = Mathf.Max(Mathf.Abs(local.x - hw), Mathf.Abs(local.x + hw)) / depth * 1.01f;
        var ty = Mathf.Max(Mathf.Abs(local.y - hh), Mathf.Abs(local.y + hh)) / depth * 1.01f;
        _cam.ResetProjectionMatrix();
        _cam.nearClipPlane = depth + 0.004f * Mathf.Max(1f, s.x);
        _cam.farClipPlane = 40f * Mathf.Max(1f, s.x);
        _cam.fieldOfView = 2f * Mathf.Atan(ty) * Mathf.Rad2Deg;
        _cam.aspect = tx / ty;
        var u0 = 0.5f + (local.x - hw) / (2f * depth * tx);
        var u1 = 0.5f + (local.x + hw) / (2f * depth * tx);
        var v0 = 0.5f + (local.y - hh) / (2f * depth * ty);
        var v1 = 0.5f + (local.y + hh) / (2f * depth * ty);
        _cam.targetTexture = _rt[eye];

        _rendering = true;
        try
        {
            _cam.Render();
        }
        finally
        {
            _rendering = false;
        }

        _mat.mainTexture = _rt[eye];
        _mat.mainTextureOffset = new Vector2(u0, v0);
        _mat.mainTextureScale = new Vector2(u1 - u0, v1 - v0);
    }
}
