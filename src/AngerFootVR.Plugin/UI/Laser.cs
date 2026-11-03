using System.Collections.Generic;
using AngerFootVR.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR;

namespace AngerFootVR.UI;

/// <summary>
/// The laser pointer in the movie house (as InkVR's): point at the big screen with either hand, from anywhere in the
/// room, and pull the trigger (or press grip) to click the game's menus. The laser is only there while a hand points at
/// the screen (pointing anywhere else, your hands are your own); if both do, the one you last clicked with. Without
/// controllers your gaze points (and A/X/grip click), so the menus always work. The hit point on the screen becomes a pointer on the
/// game's own UI (its canvases draw through the VR UI camera), so its buttons light up and click the way the mouse
/// makes them. Lists scroll: push the right stick up or down while you point at one, or hold the trigger on it and
/// drag (sliders and scroll bars drag the same way).
/// </summary>
internal static class Laser
{
    private static GameObject _beam, _dot;
    private static XRNode _hand = XRNode.RightHand;
    private static GameObject _hover, _pressed, _drag;
    private static bool _dragging;
    private static PointerEventData _pointer;
    private static readonly List<RaycastResult> Hits = new List<RaycastResult>();

    /// <summary>Whether the laser is on the screen this frame (the menu's stick navigation steps aside).</summary>
    public static bool OnScreen { get; private set; }

    public static void Update()
    {
        if (!CinemaRig.Active || MovieHouse.Screen == null)
        {
            Show(false);
            Leave();
            OnScreen = false;
            return;
        }

        EnsureVisuals();

        // which hand points at the screen: the laser is only there while a hand points at it (from anywhere in the
        // room), and off the screen your hands are free; with no controllers, your gaze points
        var left = Aim(XRNode.LeftHand, out var lo, out var ld, out var luv, out var ldist);
        var right = Aim(XRNode.RightHand, out var ro, out var rd, out var ruv, out var rdist);
        var anyHand = VrHands.Get(XRNode.LeftHand).Tracked || VrHands.Get(XRNode.RightHand).Tracked;
        if (left && right)
        {
            if (VrControls.Down(XRNode.LeftHand, VrButton.Trigger)) _hand = XRNode.LeftHand;
            else if (VrControls.Down(XRNode.RightHand, VrButton.Trigger)) _hand = XRNode.RightHand;
        }
        else if (left) _hand = XRNode.LeftHand;
        else if (right) _hand = XRNode.RightHand;

        var gaze = !anyHand;
        Vector3 origin, dir;
        Vector2 uv;
        float dist;
        bool hit;
        if (gaze)
        {
            origin = CinemaRig.HeadPosition;
            dir = CinemaRig.HeadRotation * Vector3.forward;
            hit = OnTheScreen(origin, dir, out uv, out dist);
        }
        else
        {
            var useLeft = _hand == XRNode.LeftHand;
            hit = useLeft ? left : right;
            origin = useLeft ? lo : ro;
            dir = useLeft ? ld : rd;
            uv = useLeft ? luv : ruv;
            dist = useLeft ? ldist : rdist;
        }

        OnScreen = hit;
        if (!hit)
        {
            Show(false);
            Leave();
            return;
        }

        Draw(origin, origin + dir * dist, true, gaze);
        Point(true, uv, gaze);
    }

    /// <summary>A hand's pointing ray, and where it meets the screen (false: it doesn't, or the hand isn't tracked).</summary>
    private static bool Aim(XRNode hand, out Vector3 origin, out Vector3 dir, out Vector2 uv, out float dist)
    {
        var pose = VrHands.Get(hand);
        origin = pose.Position;
        dir = pose.Aim * Vector3.forward; // the controller's pointing way (its aim pose)
        uv = default;
        dist = 0f;
        // (a hand holding something in the rooms is busy; the big screen only from the hall, not through its walls)
        if (FunWorld.Busy(hand) || MovieHouse.Root == null) return false;
        var local = MovieHouse.Root.InverseTransformPoint(origin);
        if (local.z < -6.05f || Mathf.Abs(local.x) > 7.05f) return false;
        return pose.Tracked && OnTheScreen(origin, dir, out uv, out dist);
    }

    private static bool OnTheScreen(Vector3 origin, Vector3 dir, out Vector2 uv, out float dist)
    {
        uv = default;
        var screen = MovieHouse.Screen.transform;
        var plane = new Plane(-screen.forward, screen.position);
        if (!plane.Raycast(new Ray(origin, dir), out dist) || dist <= 0f || dist > 200f) return false;
        var local = screen.InverseTransformPoint(origin + dir * dist);
        if (Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.y) > 0.5f) return false;
        uv = new Vector2(local.x + 0.5f, local.y + 0.5f);
        return true;
    }

    private static void Point(bool hit, Vector2 uv, bool gaze)
    {
        var events = EventSystem.current;
        if (events == null)
        {
            Leave();
            return;
        }

        _pointer ??= new PointerEventData(events);
        _pointer.Reset();
        _pointer.button = PointerEventData.InputButton.Left;
        _pointer.position = new Vector2(uv.x * VrUi.Width, uv.y * VrUi.Height);
        GameObject target = null;
        if (hit)
        {
            Hits.Clear();
            events.RaycastAll(_pointer, Hits);
            if (Hits.Count > 0)
            {
                target = Hits[0].gameObject;
                _pointer.pointerCurrentRaycast = Hits[0];
            }
        }

        if (target != _hover)
        {
            if (_hover != null) ExecuteEvents.ExecuteHierarchy(_hover, _pointer, ExecuteEvents.pointerExitHandler);
            if (target != null) ExecuteEvents.ExecuteHierarchy(target, _pointer, ExecuteEvents.pointerEnterHandler);
            _hover = target;
        }

        // click: the pointing hand's trigger or grip (gaze: A, a grip or a trigger)
        var down = gaze
            ? VrControls.Down(XRNode.RightHand, VrButton.Primary) || VrControls.DownEither(VrButton.Grip) || VrControls.DownEither(VrButton.Trigger)
            : VrControls.Down(_hand, VrButton.Trigger) || VrControls.Down(_hand, VrButton.Grip);
        var up = gaze
            ? VrControls.Up(XRNode.RightHand, VrButton.Primary) || VrControls.UpEither(VrButton.Grip) || VrControls.UpEither(VrButton.Trigger)
            : VrControls.Up(_hand, VrButton.Trigger) || VrControls.Up(_hand, VrButton.Grip);
        if (Opening.Playing) down = up = false;
        if (down && target != null)
        {
            _pointer.pressPosition = _pointer.position;
            _pointer.pointerPressRaycast = _pointer.pointerCurrentRaycast;
            _pressed = ExecuteEvents.ExecuteHierarchy(target, _pointer, ExecuteEvents.pointerDownHandler) ?? ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
            _pointer.pointerPress = _pressed;
            // what a drag from here would move (a list, a slider, a scroll bar)
            // (each of the game's rows listens for drags itself and does nothing with them: a drag on a row moves its
            // list; sliders and scroll bars drag themselves)
            _drag = ExecuteEvents.GetEventHandler<IDragHandler>(target);
            if (_drag == null || _drag.GetComponent<UnityEngine.UI.Slider>() == null && _drag.GetComponent<UnityEngine.UI.Scrollbar>() == null)
            {
                var list = target.GetComponentInParent<UnityEngine.UI.ScrollRect>();
                if (list != null) _drag = list.gameObject;
            }
            _dragging = false;
            if (_drag != null) ExecuteEvents.Execute(_drag, _pointer, ExecuteEvents.initializePotentialDrag);
            VrControls.Rumble(_hand, 0.3f, 0.03f);
        }

        // held and moved: a drag (it isn't a click then)
        if (_drag != null && !up)
        {
            if (!_dragging && (_pointer.position - _pointer.pressPosition).magnitude > 24f)
            {
                _dragging = true;
                _pointer.pointerDrag = _drag;
                _pointer.dragging = true;
                ExecuteEvents.Execute(_drag, _pointer, ExecuteEvents.beginDragHandler);
                if (Dev.ActionLog.Enabled) Plugin.L.LogInfo($"LASER drag on '{_drag.name}' ({string.Join(",", System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(_drag.GetComponents<IDragHandler>(), h => h.GetType().Name)))}) from {_pointer.pressPosition} cam {(_pointer.pressEventCamera != null ? _pointer.pressEventCamera.name : "none")}");
                if (_pressed != null && _pressed != _drag) ExecuteEvents.Execute(_pressed, _pointer, ExecuteEvents.pointerUpHandler);
                if (_pressed != _drag) _pressed = null;
            }

            if (_dragging)
            {
                _pointer.delta = _pointer.position - _lastPosition;
                ExecuteEvents.Execute(_drag, _pointer, ExecuteEvents.dragHandler);
            }
        }

        if (up)
        {
            if (_dragging && _drag != null)
            {
                ExecuteEvents.Execute(_drag, _pointer, ExecuteEvents.endDragHandler);
                if (_pressed != null) ExecuteEvents.Execute(_pressed, _pointer, ExecuteEvents.pointerUpHandler);
                _pressed = null;
            }
            else if (_pressed != null)
            {
                ExecuteEvents.Execute(_pressed, _pointer, ExecuteEvents.pointerUpHandler);
                var clicked = ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
                if (clicked == _pressed) ExecuteEvents.Execute(_pressed, _pointer, ExecuteEvents.pointerClickHandler);
                _pressed = null;
            }

            _drag = null;
            _dragging = false;
            _pointer.dragging = false;
            _pointer.pointerDrag = null;
        }

        // the right stick scrolls the list under the laser
        if (target != null && !_dragging) Scroll(target);
        _lastPosition = _pointer.position;
    }

    private static Vector2 _lastPosition;

    /// <summary>Dev: what the laser is on.</summary>
    public static string Describe()
    {
        var list = _hover != null ? _hover.GetComponentInParent<UnityEngine.UI.ScrollRect>() : null;
        return $"LASER on screen {OnScreen} hand {_hand} at {(_pointer != null ? _pointer.position.ToString() : "-")} over '{(_hover != null ? _hover.name : "-")}' list {(list != null ? list.name + " " + list.verticalNormalizedPosition.ToString("F2") : "-")} dragging {_dragging}";
    }

    /// <summary>Dev: a stick push to scroll with (the test runtime's sticks don't come through).</summary>
    public static float? DevScroll;

    private static void Scroll(GameObject target)
    {
        var stick = DevScroll.HasValue ? new Vector2(0f, DevScroll.Value) : VrControls.Stick(XRNode.RightHand);
        if (Mathf.Abs(stick.y) < 0.3f || Mathf.Abs(stick.y) < Mathf.Abs(stick.x)) return;
        var list = target.GetComponentInParent<UnityEngine.UI.ScrollRect>();
        if (list == null || !list.vertical || list.content == null || list.viewport == null) return;
        var room = list.content.rect.height - list.viewport.rect.height;
        if (room <= 1f) return;
        // about 600 of the list's units a second at full push
        var push = Mathf.Sign(stick.y) * (Mathf.Abs(stick.y) - 0.3f) / 0.7f;
        list.velocity = Vector2.zero;
        list.verticalNormalizedPosition = Mathf.Clamp01(list.verticalNormalizedPosition + push * 600f * Time.unscaledDeltaTime / room);
    }

    private static void Leave()
    {
        if (_dragging && _drag != null && _pointer != null) ExecuteEvents.Execute(_drag, _pointer, ExecuteEvents.endDragHandler);
        _drag = null;
        _dragging = false;
        if (_hover != null && _pointer != null) ExecuteEvents.ExecuteHierarchy(_hover, _pointer, ExecuteEvents.pointerExitHandler);
        _hover = null;
        _pressed = null;
    }

    private static void EnsureVisuals()
    {
        if (_beam != null) return;
        var mat = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, 0.2f, 0.65f) };
        _beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _beam.name = "AngerFootVR Laser";
        Object.Destroy(_beam.GetComponent<Collider>());
        _beam.layer = VrUi.Layer;
        _beam.GetComponent<Renderer>().sharedMaterial = mat;
        _beam.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Object.DontDestroyOnLoad(_beam);
        _dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _dot.name = "AngerFootVR Laser Dot";
        Object.Destroy(_dot.GetComponent<Collider>());
        _dot.layer = VrUi.Layer;
        _dot.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = Color.white };
        Object.DontDestroyOnLoad(_dot);
    }

    private static void Draw(Vector3 from, Vector3 to, bool hit, bool gaze)
    {
        // gaze shows only the dot on the screen; a hand shows its beam
        _beam.SetActive(!gaze);
        if (!gaze)
        {
            var d = to - from;
            _beam.transform.SetPositionAndRotation(from + d * 0.5f, Quaternion.LookRotation(d));
            _beam.transform.localScale = new Vector3(0.006f, 0.006f, d.magnitude);
        }

        _dot.SetActive(hit);
        _dot.transform.position = to;
        _dot.transform.localScale = Vector3.one * 0.06f;
    }

    private static void Show(bool on)
    {
        if (_beam != null && _beam.activeSelf != on) _beam.SetActive(on);
        if (_dot != null && _dot.activeSelf != on) _dot.SetActive(on);
    }
}
