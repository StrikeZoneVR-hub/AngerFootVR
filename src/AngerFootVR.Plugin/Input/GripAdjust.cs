using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AngerFootVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.Input;

/// <summary>
/// GUN GRIP ADJUST (VR SETTINGS, or the button on the gun range's bench): with it on, hold a gun and set how it sits in
/// your hand, live, gun by gun. The gun hand's stick moves it: ANGLE (up/down tilts the barrel, left/right turns it),
/// HEIGHT (up/down, left/right sideways), DEPTH (up/down forward and back, left/right rolls it); a click of the stick
/// goes to the next, holding it in puts this gun back as it was. Or squeeze your other hand's grip on the gun and place
/// it there by hand. A line over the gun says which page and where the gun is. Each gun keeps its own
/// (BepInEx/AngerFootVR/gun-grips.txt): in a level the gun and the game's hand holding it move together, against your
/// controller (the angle you aim at); on the range, the gun moves in your hand. The game's own animations, recoil and
/// reloads go on as before. Turn it off again when you're done: the sticks are yours again.
/// </summary>
internal static class GripAdjust
{
    public struct Offset
    {
        public Vector3 Move; // metres: x your right, y up, z forward (along the barrel)
        public Vector3 Turn; // degrees: x tilt (down +), y turn (right +), z roll

        public bool IsZero => Move.sqrMagnitude < 1e-8f && Turn.sqrMagnitude < 1e-4f;
    }

    /// <summary>Where each gun sits by default (else as the mod first placed them). Your adjustments override it. The
    /// Glock's is the tester's own, set with GUN GRIP ADJUST on 11-03 ("whatever I feel is best will be the new default").</summary>
    private static readonly Dictionary<string, Offset> Defaults = new Dictionary<string, Offset>
    {
        ["level/Glock"] = new Offset { Move = new Vector3(-0.0215f, -0.1046f, 0.0217f), Turn = new Vector3(8.9978f, 5.3195f, -30.5825f) },
    };

    private static readonly Dictionary<string, Offset> Chosen = new Dictionary<string, Offset>();
    private static string File => Path.Combine(BepInEx.Paths.BepInExRootPath, "AngerFootVR", "gun-grips.txt");
    private static readonly string[] Pages = { "ANGLE", "HEIGHT", "DEPTH" };
    private const float MaxMove = 0.15f, MaxTurn = 60f;

    /// <summary>Dev: on for now (the player's settings file untouched).</summary>
    public static bool DevOn;

    public static bool On => DevOn || VrSettings.GripAdjust != null && VrSettings.GripAdjust.Value;

    private static bool _loaded, _dirty;
    private static float _saveAt, _clickDown = -1f;
    private static int _page;
    private static string _key, _name;
    private static XRNode _hand;
    private static Vector3 _labelAt, _gunAt;
    private static int _seenFrame = -10;
    private static TextMeshPro _label;

    // placing it by hand: the other hand's pose (in the gun hand's aim) and the offset when the grip went down
    private static bool _placing;
    private static Vector3 _grabPos;
    private static Quaternion _grabRot;
    private static Offset _grabFrom;

    /// <summary>Whether your other hand is placing the gun (its grip's then not the two-hand grip).</summary>
    public static bool Placing => _placing && Adjusting;

    /// <summary>A gun's adjusting is going on this frame (a gun in hand, the mode on).</summary>
    public static bool Adjusting => On && Time.frameCount - _seenFrame <= 2;

    public static Offset Get(string key)
    {
        Load();
        if (key != null && Chosen.TryGetValue(key, out var o)) return o;
        return key != null && Defaults.TryGetValue(key, out var d) ? d : default;
    }

    /// <summary>The offset as a pose in the gun hand's aim (position, rotation).</summary>
    public static Pose PoseOf(string key)
    {
        var o = Get(key);
        return o.IsZero ? Pose.identity : new Pose(o.Move, Quaternion.Euler(o.Turn));
    }

    /// <summary>Held this frame: the gun (its key and name), the hand it's in, where it is and where its line goes (world).</summary>
    public static void Holding(string key, string name, XRNode hand, Vector3 gunAt, Vector3 labelAt)
    {
        if (!On) return;
        if (key != _key)
        {
            _key = key;
            _placing = false;
        }

        _name = name;
        _hand = hand;
        _labelAt = labelAt;
        _gunAt = gunAt;
        _seenFrame = Time.frameCount;
    }

    /// <summary>Every frame, straight after the controllers are read (so the sticks it takes are taken this frame).</summary>
    public static void Update()
    {
        if (!Adjusting || _key == null)
        {
            VrControls.StickTaken = null;
            _placing = false;
            if (_card != null && _card.gameObject.activeSelf) _card.gameObject.SetActive(false);
            if (_dirty && Time.unscaledTime >= _saveAt) Save();
            return;
        }

        VrControls.StickTaken = _hand;
        var o = Get(_key);
        var before = o;
        var dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

        // the stick: this page's two ways (slow near the middle, for the last degree or millimetre)
        var s = VrControls.RawStick(_hand);
        Vector2 Curve(Vector2 v) => new Vector2(Shape(v.x), Shape(v.y));
        var c = Curve(s);
        switch (_page)
        {
            case 0:
                o.Turn.x -= c.y * 30f * dt;
                o.Turn.y += c.x * 30f * dt;
                break;
            case 1:
                o.Move.y += c.y * 0.06f * dt;
                o.Move.x += c.x * 0.06f * dt;
                break;
            default:
                o.Move.z += c.y * 0.06f * dt;
                o.Turn.z -= c.x * 30f * dt;
                break;
        }

        // the stick's click: a tap, the next page; held in, this gun as it was
        if (VrControls.RawHeld(_hand, VrButton.StickClick))
        {
            if (_clickDown < 0f) _clickDown = Time.unscaledTime;
            if (Time.unscaledTime - _clickDown > 1f && _clickDown > 0f)
            {
                o = Defaults.TryGetValue(_key, out var d) ? d : default;
                _clickDown = 0f; // (done: no page change on letting go)
                VrControls.Rumble(_hand, 0.6f, 0.15f);
                Plugin.L.LogInfo($"Grip: {_key} put back as it was");
            }
        }
        else if (_clickDown >= 0f)
        {
            if (_clickDown > 0f)
            {
                _page = (_page + 1) % Pages.Length;
                VrControls.Rumble(_hand, 0.25f, 0.04f);
            }

            _clickDown = -1f;
        }

        // your other hand on the gun: placed where you put it
        var other = _hand == XRNode.LeftHand ? XRNode.RightHand : XRNode.LeftHand;
        var gunPose = VrHands.GetRaw(_hand);
        var otherPose = VrHands.GetRaw(other);
        if (gunPose.Tracked && otherPose.Tracked)
        {
            var inv = Quaternion.Inverse(gunPose.Aim);
            var relPos = inv * (otherPose.Position - gunPose.Position);
            var relRot = inv * otherPose.Rotation;
            if (!_placing && VrControls.Down(other, VrButton.Grip) && (otherPose.Position - _gunAt).magnitude < 0.3f)
            {
                _placing = true;
                _grabPos = relPos;
                _grabRot = relRot;
                _grabFrom = o;
                VrControls.Rumble(other, 0.4f, 0.06f);
            }
            else if (_placing && !VrControls.Held(other, VrButton.Grip)) _placing = false;

            if (_placing)
            {
                var turn = relRot * Quaternion.Inverse(_grabRot);
                o.Move = _grabFrom.Move + (relPos - _grabPos);
                o.Turn = Euler(turn * Quaternion.Euler(_grabFrom.Turn));
            }
        }

        o.Move = Vector3.ClampMagnitude(o.Move, MaxMove);
        o.Turn = new Vector3(Mathf.Clamp(Wrap(o.Turn.x), -MaxTurn, MaxTurn), Mathf.Clamp(Wrap(o.Turn.y), -MaxTurn, MaxTurn), Mathf.Clamp(Wrap(o.Turn.z), -90f, 90f));
        if ((o.Move - before.Move).sqrMagnitude > 1e-10f || (o.Turn - before.Turn).sqrMagnitude > 1e-6f)
        {
            Chosen[_key] = o;
            _dirty = true;
            _saveAt = Time.unscaledTime + 1.5f;
        }

        if (_dirty && Time.unscaledTime >= _saveAt) Save();
        Show(o);
    }

    private static float Shape(float v)
    {
        var a = Mathf.Abs(v);
        if (a < 0.2f) return 0f;
        var t = (a - 0.2f) / 0.8f;
        return Mathf.Sign(v) * t * t;
    }

    private static float Wrap(float a) => Mathf.DeltaAngle(0f, a);

    private static Vector3 Euler(Quaternion q)
    {
        var e = q.eulerAngles;
        return new Vector3(Wrap(e.x), Wrap(e.y), Wrap(e.z));
    }

    private static Transform _card;

    /// <summary>The line about the gun: on a small dark card beside it (your inner side, out of the way of your aim), so
    /// nothing behind it shows through or runs into its letters.</summary>
    private static void Show(Offset o)
    {
        if (_card == null)
        {
            var root = new GameObject("AngerFootVR grip card") { layer = UI.VrUi.Layer };
            Object.DontDestroyOnLoad(root);
            _card = root.transform;
            var back = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(back.GetComponent<Collider>());
            back.layer = UI.VrUi.Layer;
            back.transform.SetParent(_card, false);
            back.transform.localPosition = new Vector3(0f, 0f, 0.004f);
            back.transform.localScale = new Vector3(0.3f, 0.13f, 0.004f);
            var mr = back.GetComponent<MeshRenderer>();
            mr.sharedMaterial = UI.Props.Mat("deco_dark");
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var go = new GameObject("Text") { layer = UI.VrUi.Layer };
            go.transform.SetParent(_card, false);
            _label = go.AddComponent<TextMeshPro>();
            var font = UI.MovieHouse.Font;
            if (font != null) _label.font = font;
            _label.fontSize = 0.2f;
            _label.alignment = TextAlignmentOptions.Center;
            _label.color = new Color(1f, 0.85f, 0.25f);
            _label.enableWordWrapping = false;
            _label.enableAutoSizing = true;
            _label.fontSizeMin = 0.1f;
            _label.fontSizeMax = 0.2f;
            _label.rectTransform.sizeDelta = new Vector2(0.28f, 0.12f);
        }

        if (!_card.gameObject.activeSelf) _card.gameObject.SetActive(true);
        var head = CinemaRig.Active ? CinemaRig.HeadPosition : VrRig.HeadPosition;
        var headRot = CinemaRig.Active ? CinemaRig.HeadRotation : VrRig.HeadRotation;
        // (beside the gun on the side towards your middle, a little up: you glance at it, it isn't over your sights)
        var flatRight = Vector3.ProjectOnPlane(headRot * Vector3.right, Vector3.up).normalized;
        var at = _gunAt + flatRight * (_hand == XRNode.RightHand ? -0.2f : 0.2f) + Vector3.up * 0.05f;
        _card.position = at;
        var away = at - head;
        if (away.sqrMagnitude > 1e-4f) _card.rotation = Quaternion.LookRotation(away.normalized, Vector3.up); // (read from your side)
        var page = _page switch
        {
            0 => $"TILT {(-o.Turn.x):+0;-0;0}°   TURN {o.Turn.y:+0;-0;0}°",
            1 => $"UP {o.Move.y * 100f:+0.0;-0.0;0.0}   SIDE {o.Move.x * 100f:+0.0;-0.0;0.0} CM",
            _ => $"FORWARD {o.Move.z * 100f:+0.0;-0.0;0.0} CM   ROLL {(-o.Turn.z):+0;-0;0}°",
        };
        _label.text = $"<color=#FF29A0>{_name}  {Pages[_page]} {_page + 1}/3</color>\n{page}\n<size=62%><color=#BBBBBB>STICK MOVES IT · CLICK: NEXT\nHOLD CLICK: RESET · OR GRAB IT</color></size>";
    }

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!System.IO.File.Exists(File)) return;
            foreach (var line in System.IO.File.ReadAllLines(File))
            {
                var i = line.LastIndexOf('=');
                if (i <= 0) continue;
                var v = line.Substring(i + 1).Split(',');
                if (v.Length != 6) continue;
                var f = new float[6];
                var ok = true;
                for (var k = 0; k < 6; k++) ok &= float.TryParse(v[k], NumberStyles.Float, CultureInfo.InvariantCulture, out f[k]);
                if (ok) Chosen[line.Substring(0, i)] = new Offset { Move = new Vector3(f[0], f[1], f[2]), Turn = new Vector3(f[3], f[4], f[5]) };
            }

            Plugin.L.LogInfo($"Grip: {Chosen.Count} guns as you set them");
        }
        catch (System.Exception e)
        {
            Plugin.L.LogWarning("Grip: " + e.Message);
        }
    }

    private static void Save()
    {
        _dirty = false;
        foreach (var kv in Chosen) Plugin.L.LogInfo($"Grip: {kv.Key} move {kv.Value.Move * 100f:F1} cm turn {kv.Value.Turn:F1}");
        if (Plugin.DevRun) return; // (a dev run keeps them for now only)
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File));
            var lines = new List<string>();
            foreach (var kv in Chosen)
            {
                var m = kv.Value.Move;
                var t = kv.Value.Turn;
                lines.Add(kv.Key + "=" + string.Join(",", new[] { m.x, m.y, m.z, t.x, t.y, t.z }.Select(x => x.ToString("0.####", CultureInfo.InvariantCulture))));
            }

            System.IO.File.WriteAllLines(File, lines);
        }
        catch (System.Exception e)
        {
            Plugin.L.LogWarning("Grip: " + e.Message);
        }
    }

    public static string Describe() => $"GRIP on {On} adjusting {Adjusting} gun '{_key}' page {Pages[_page]} placing {_placing} offset {Get(_key).Move * 100f:F1} cm {Get(_key).Turn:F1} | {Chosen.Count} set";
}
