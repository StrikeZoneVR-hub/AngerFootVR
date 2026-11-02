using System.Collections.Generic;
using AngerFootVR.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR;

namespace AngerFootVR.UI;

/// <summary>
/// The laser pointer in the movie house (as InkVR's): point at the big screen with either hand and pull the trigger
/// (or press A/X or grip) to click the game's menus. The hand you last clicked with points; without controllers your
/// gaze points (and A/X/grip click), so the menus always work. The hit point on the screen becomes a pointer on the
/// game's own UI (its canvases draw through the VR UI camera), so its buttons light up and click the way the mouse
/// makes them.
/// </summary>
internal static class Laser
{
    private static GameObject _beam, _dot;
    private static XRNode _hand = XRNode.RightHand;
    private static GameObject _hover, _pressed;
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
        foreach (var h in new[] { XRNode.LeftHand, XRNode.RightHand })
            if (VrControls.Down(h, VrButton.Trigger))
                _hand = h;

        // the ray: the hand's aim, or your gaze
        var pose = VrHands.Get(_hand);
        Vector3 origin, dir;
        var gaze = !pose.Tracked;
        if (gaze)
        {
            origin = CinemaRig.HeadPosition;
            dir = CinemaRig.HeadRotation * Vector3.forward;
        }
        else
        {
            origin = pose.Position;
            dir = pose.Rotation * Quaternion.Euler(VrHands.AimPitch, 0f, 0f) * Vector3.forward; // the controller's pointing way, up from its grip
        }

        // where it meets the screen (a quad facing the room)
        var screen = MovieHouse.Screen.transform;
        var plane = new Plane(-screen.forward, screen.position);
        var ray = new Ray(origin, dir);
        Vector2 uv = default;
        var hit = plane.Raycast(ray, out var dist) && dist > 0f && dist < 30f;
        var end = origin + dir * (hit ? dist : 3f);
        if (hit)
        {
            var local = screen.InverseTransformPoint(end);
            hit = Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.y) <= 0.5f;
            uv = new Vector2(local.x + 0.5f, local.y + 0.5f);
        }

        OnScreen = hit;
        Draw(origin, end, hit, gaze);
        Point(hit, uv, gaze);
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

        // click: the pointing hand's trigger, or A/X or grip (gaze: any of those)
        var down = gaze
            ? VrControls.DownEither(VrButton.Primary) || VrControls.DownEither(VrButton.Grip) || VrControls.DownEither(VrButton.Trigger)
            : VrControls.Down(_hand, VrButton.Trigger) || VrControls.Down(_hand, VrButton.Grip);
        var up = gaze
            ? VrControls.UpEither(VrButton.Primary) || VrControls.UpEither(VrButton.Grip) || VrControls.UpEither(VrButton.Trigger)
            : VrControls.Up(_hand, VrButton.Trigger) || VrControls.Up(_hand, VrButton.Grip);
        if (Opening.Playing) down = up = false;
        if (down && target != null)
        {
            _pressed = ExecuteEvents.ExecuteHierarchy(target, _pointer, ExecuteEvents.pointerDownHandler) ?? ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
            _pointer.pointerPress = _pressed;
            VrControls.Rumble(_hand, 0.3f, 0.03f);
        }

        if (up && _pressed != null)
        {
            ExecuteEvents.Execute(_pressed, _pointer, ExecuteEvents.pointerUpHandler);
            var clicked = ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
            if (clicked == _pressed) ExecuteEvents.Execute(_pressed, _pointer, ExecuteEvents.pointerClickHandler);
            _pressed = null;
        }
    }

    private static void Leave()
    {
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
