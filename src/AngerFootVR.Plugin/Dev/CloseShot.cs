using System.Collections;
using UnityEngine;

namespace AngerFootVR.Dev;

/// <summary>
/// Dev: "closeshot name yaw pitch dist [hand|enemy|self]": a close picture round a point (the gun hand's palm, the
/// nearest goon's chest, your own chest), from yaw degrees round it (0 in front, 90 from its right) and pitch degrees
/// up, dist metres away, in the gun's frame for the hand: to judge grips, wounds and blood from any side.
/// </summary>
internal static class CloseShot
{
    public static Vector3? Target;
    public static Quaternion Frame = Quaternion.identity;

    public static IEnumerator Take(string arg)
    {
        var a = arg.Split(' ');
        var name = a.Length > 0 ? a[0] : "close";
        var yaw = a.Length > 1 ? float.Parse(a[1]) : 90f;
        var pitch = a.Length > 2 ? float.Parse(a[2]) : 10f;
        var dist = a.Length > 3 ? float.Parse(a[3]) : 0.35f;
        var what = a.Length > 4 ? a[4] : "hand";
        yield return new WaitForEndOfFrame();
        // (the player's own view camera: Camera.main is the game's weapon camera)
        var main = Framework.SingletonBehaviour<PlayerCamera>.HasInstance ? Framework.SingletonBehaviour<PlayerCamera>.Instance.MainCamera : Camera.main;
        Vector3 at;
        Quaternion frame;
        if (what == "enemy")
        {
            var head = Core.VrRig.HeadPosition;
            Enemy best = null;
            var bestD = float.MaxValue;
            foreach (var e in Object.FindObjectsOfType<Enemy>())
            {
                if (e == null || e.Rig == null) continue;
                var d = (e.transform.position - head).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = e;
                }
            }

            if (best == null)
            {
                Plugin.L.LogInfo("closeshot: no enemy");
                yield break;
            }

            at = best.Rig.GetCenterPosition();
            var f = best.transform.forward;
            f.y = 0;
            frame = Quaternion.LookRotation(f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward);
        }
        else if (what == "self")
        {
            var root = Body.VrBody.Root;
            if (root == null) yield break;
            at = root.position + Vector3.up * 0.35f * root.lossyScale.y;
            frame = Quaternion.Euler(0f, root.eulerAngles.y, 0f);
        }
        else if (what == "hands")
        {
            // between your two hands, turned with your head (yaw 180 pitch 30: over your shoulder, as you see them)
            var l = Body.HeroBody.HandBone(0);
            var r = Body.HeroBody.HandBone(1);
            if (l == null || r == null) yield break;
            at = (l.position + r.position) * 0.5f + Vector3.up * 0.08f;
            frame = Quaternion.Euler(0f, Core.VrRig.HeadRotation.eulerAngles.y, 0f);
        }
        else if (what == "point" && Target.HasValue)
        {
            at = Target.Value;
            frame = Frame;
        }
        else
        {
            if (!Input.GunHold.GunPalm(out at, out _, out var rig) || rig == null)
            {
                var hb = Body.HeroBody.HandBone(1);
                if (hb == null) yield break;
                at = hb.position;
                frame = hb.rotation;
            }
            else frame = Quaternion.LookRotation(rig.forward, rig.up);
        }

        var go = new GameObject("AngerFootVR closeshot");
        var cam = go.AddComponent<Camera>();
        if (main != null) cam.CopyFrom(main);
        cam.stereoTargetEye = StereoTargetEyeMask.None;
        cam.cullingMask |= (1 << UI.VrUi.Layer) | (1 << Body.VrBody.MirrorLayer);
        cam.fieldOfView = 40f;
        cam.clearFlags = CameraClearFlags.SolidColor; // (the game's camera only clears depth: its sky is drawn by another)
        cam.backgroundColor = RenderSettings.fogColor;
        cam.nearClipPlane = 0.02f;
        var dir = frame * (Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward);
        go.transform.position = at + dir * dist;
        go.transform.LookAt(at, frame * Vector3.up);
        var rt = new RenderTexture(1200, 1200, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
        cam.targetTexture = rt;
        Body.HeroBody.ShowHead(what == "self");
        cam.Render();
        Body.HeroBody.ShowHead(false);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        RenderTexture.active = prev;
        var folder = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, System.IO.Path.Combine("AngerFootVR", "shots"));
        System.IO.Directory.CreateDirectory(folder);
        var file = System.IO.Path.Combine(folder, $"{name}_left_{System.DateTime.Now:HHmmss}.png");
        System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
        Object.Destroy(tex);
        cam.targetTexture = null;
        rt.Release();
        Object.Destroy(rt);
        Object.Destroy(go);
        Plugin.L.LogInfo("closeshot: " + file);
    }
}
