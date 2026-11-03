using System.Linq;
using System.Text;
using AngerFootVR.Core;
using Framework;
using TMPro;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.UI;

/// <summary>
/// WRIST WATCH (VR settings): a watch on your left wrist in a level. Turn the wrist to look at it and it lights up: the
/// level's time, your ammo, the star goals as you go (ticked off when done), without the game's own on-screen goals.
/// </summary>
internal static class WristWatch
{
    private static GameObject _watch;
    private static TextMeshPro _text;
    private static Renderer _face;
    private static float _shown, _nextText;

    public static void Update()
    {
        var want = XrManager.IsRunning && VrSettings.WristWatch != null && VrSettings.WristWatch.Value && !CinemaRig.Active && !MapRig.Active &&
                   SingletonBehaviour<Player>.HasInstance && SingletonBehaviour<GameplayManager>.HasInstance;
        if (!want)
        {
            if (_watch != null) _watch.SetActive(false);
            return;
        }

        var free = Input.VrInputProvider.GunHand == XRNode.RightHand ? XRNode.LeftHand : XRNode.RightHand;
        var pose = VrHands.Get(free);
        if (!pose.Tracked)
        {
            if (_watch != null) _watch.SetActive(false);
            return;
        }

        if (_watch == null) Make();
        if (_watch == null) return;
        _watch.SetActive(true);
        // on the back of the wrist (where the body wears its wristband), just behind the hand: the way the back of the
        // hand faces (out from the thumb's side of the grip), a little up
        var left = free == XRNode.LeftHand;
        var n = (pose.Rotation * new Vector3(left ? -1f : 1f, 0.45f, 0f)).normalized;
        var bone = Body.HeroBody.HandBone(left ? 0 : 1);
        Vector3 at, along;
        if (bone != null && bone.parent != null)
        {
            along = (bone.position - bone.parent.position).normalized; // (the forearm, towards the hand)
            at = bone.position - along * 0.05f + n * 0.045f;
        }
        else
        {
            along = pose.Rotation * Vector3.forward;
            at = pose.Position + n * 0.035f - along * 0.085f;
        }

        // the writing the right way up for you, however the wrist is turned
        var up = Vector3.ProjectOnPlane(VrRig.HeadRotation * Vector3.up, n);
        if (up.sqrMagnitude < 1e-4f) up = along;
        _watch.transform.SetPositionAndRotation(at, Quaternion.LookRotation(-n, up.normalized));
        // lit when you look at it
        var toEye = (VrRig.HeadPosition - at).normalized;
        var facing = Vector3.Dot(n, toEye);
        _shown = Mathf.MoveTowards(_shown, facing > 0.35f ? 1f : 0f, Time.unscaledDeltaTime * 5f);
        if (_text != null) _text.alpha = _shown;
        if (_face != null) _face.enabled = true;
        if (_shown > 0.01f && Time.unscaledTime > _nextText)
        {
            _nextText = Time.unscaledTime + 0.1f;
            _text.text = Text();
        }
    }

    private static string Text()
    {
        var sb = new StringBuilder();
        var gm = SingletonBehaviour<GameplayManager>.Instance;
        sb.Append(gm.CurrentLevelStats != null ? gm.CurrentLevelStats.LevelTime.ToStopwatchString() : "--:--");
        var player = SingletonBehaviour<Player>.Instance;
        if (player.HasWeapon && player.Weapon.RequiresAmmo && player.Weapon.AmmoStorage != null) sb.Append("\nAMMO ").Append(player.Weapon.AmmoStorage.CurrentAmmo);
        if (gm.ActiveStarGoals != null)
            foreach (var g in gm.ActiveStarGoals)
            {
                if (g == null) continue;
                var name = "";
                try
                {
                    name = g.StarGoalData.GetLocalizedName();
                }
                catch
                {
                    name = g.name;
                }

                var done = g.IsComplete();
                var fail = !done && !g.IsCompletable();
                sb.Append(done ? "\n<color=#FFD83A>*</color> " : fail ? "\n<color=#FF4D6A>x</color> " : "\n- ").Append(name.ToUpperInvariant());
                var progress = g.GetProgressString();
                if (!done && !string.IsNullOrEmpty(progress)) sb.Append(' ').Append(progress);
            }

        return sb.ToString();
    }

    private static void Make()
    {
        _watch = new GameObject("AngerFootVR Wrist Watch") { layer = VrUi.Layer };
        Object.DontDestroyOnLoad(_watch);
        var face = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(face.GetComponent<Collider>());
        face.layer = VrUi.Layer;
        face.transform.SetParent(_watch.transform, false);
        face.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        face.transform.localPosition = new Vector3(0f, 0f, 0.004f);
        face.transform.localScale = new Vector3(0.062f, 0.004f, 0.062f);
        _face = face.GetComponent<MeshRenderer>();
        _face.sharedMaterial = Props.Mat("deco_dark");
        _face.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(rim.GetComponent<Collider>());
        rim.layer = VrUi.Layer;
        rim.transform.SetParent(_watch.transform, false);
        rim.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        rim.transform.localPosition = new Vector3(0f, 0f, 0.006f);
        rim.transform.localScale = new Vector3(0.068f, 0.003f, 0.068f);
        rim.GetComponent<MeshRenderer>().sharedMaterial = Props.Mat("deco_gold");
        var go = new GameObject("Text") { layer = VrUi.Layer };
        go.transform.SetParent(_watch.transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, -0.001f);
        _text = go.AddComponent<TextMeshPro>();
        var font = MovieHouse.Font;
        if (font != null) _text.font = font;
        _text.fontSize = 0.06f;
        _text.alignment = TextAlignmentOptions.Center;
        _text.color = new Color(0.15f, 0.95f, 1f);
        _text.enableWordWrapping = true;
        _text.rectTransform.sizeDelta = new Vector2(0.11f, 0.08f);
        _text.enableAutoSizing = true;
        _text.fontSizeMin = 0.02f;
        _text.fontSizeMax = 0.06f;
    }
}
