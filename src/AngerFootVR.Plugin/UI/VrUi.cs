using System.Collections.Generic;
using AngerFootVR.Core;
using Framework;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.UI;

/// <summary>
/// The game's UI in the headset. The game draws its menus and HUD on screen-overlay canvases, which only ever reach the
/// desktop window. In VR they draw through a UI camera into one 4K texture instead, shown on a panel in front of you:
/// big and centred for menus and the pause menu, smaller and lower for the HUD in play. The panel is drawn by the
/// game's own camera, last and over the world (a menu never sinks into a wall): a separate camera stacked after the
/// game's post-processing comes out flipped in the headset. It follows your head's turn lazily, so it stays put while
/// you glance around.
/// </summary>
internal static class VrUi
{
    public const int Width = 3840, Height = 2160;

    /// <summary>The panel's layer: one the game's view camera draws on the title and in levels (PropNonColliding, which
    /// nothing collides with; the panel has no collider anyway).</summary>
    public const int Layer = 15;

    private const float CaptureDepth = -10000f; // the UI camera sits far below any level, seeing only its canvases

    private static RenderTexture _texture;
    private static Camera _uiCamera;
    private static GameObject _panel;
    private static Material _material;
    private static readonly HashSet<Canvas> Moved = new HashSet<Canvas>();
    private static float _nextScan, _panelYaw, _panelPitch;

    /// <summary>Dev: overrides the menu panel's width and distance (metres).</summary>
    public static float? DevWidth, DevDistance;
    private static bool _placed, _placedForHud;
    private static Camera _placedFor;

    public static void Update()
    {
        if (!XrManager.IsRunning) return;
        EnsureObjects();
        if (Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + 0.25f;
            CaptureCanvases();
        }

        // in the movie house the UI plays on its big screen instead of the floating panel
        _panel.SetActive(!CinemaRig.Active);
        if (MovieHouse.Screen != null && MovieHouse.Screen.sharedMaterial.mainTexture != _texture) MovieHouse.Screen.sharedMaterial.mainTexture = _texture;
        if (!CinemaRig.Active) PlacePanel();
    }

    private static void EnsureObjects()
    {
        if (_texture == null)
        {
            _texture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
                { name = "AngerFootVR_UI", useMipMap = true, autoGenerateMips = true, antiAliasing = 1, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            _texture.Create();
            Object.DontDestroyOnLoad(_texture);
        }

        if (_uiCamera == null)
        {
            var go = new GameObject("AngerFootVR UI Camera");
            Object.DontDestroyOnLoad(go);
            go.transform.position = new Vector3(0f, CaptureDepth, 0f);
            _uiCamera = go.AddComponent<Camera>();
            _uiCamera.stereoTargetEye = StereoTargetEyeMask.None;
            _uiCamera.targetTexture = _texture;
            _uiCamera.clearFlags = CameraClearFlags.SolidColor;
            _uiCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _uiCamera.cullingMask = 1 << 5; // the game's UI layer
            _uiCamera.orthographic = true;
            _uiCamera.nearClipPlane = 0.1f;
            _uiCamera.farClipPlane = 10f;
            _uiCamera.depth = -100;
            _uiCamera.allowHDR = false;
            _uiCamera.allowMSAA = false;
        }

        if (_panel == null)
        {
            _panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _panel.name = "AngerFootVR UI Panel";
            Object.DontDestroyOnLoad(_panel);
            Object.Destroy(_panel.GetComponent<Collider>());
            _panel.layer = Layer;
            _material = new Material(Shader.Find("UI/Default")) { mainTexture = _texture };
            _material.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            _material.renderQueue = 4000; // after everything else the camera draws
            _panel.GetComponent<MeshRenderer>().sharedMaterial = _material;
            _placed = false;
        }
    }

    /// <summary>Every screen-overlay canvas (new menus spawn their own) draws into the UI texture instead.</summary>
    private static void CaptureCanvases()
    {
        foreach (var canvas in Object.FindObjectsOfType<Canvas>())
        {
            if (canvas == null || !canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _uiCamera;
            canvas.planeDistance = 1f;
            if (Moved.Add(canvas)) Plugin.L.LogInfo($"UI '{canvas.name}' now draws into the VR panel");
        }

        Moved.RemoveWhere(c => c == null);
    }

    /// <summary>Dev: shows the panel as a solid tinted quad (true) or its UI texture again (false).</summary>
    public static void DevFill(bool fill)
    {
        if (_material == null) return;
        _material.mainTexture = fill ? Texture2D.whiteTexture : (Texture)_texture;
        _material.color = fill ? new Color(1f, 0f, 1f, 0.5f) : Color.white;
    }

    public static string Describe()
    {
        var ok = HeadPose(out var hp, out var hr);
        var main = Camera.main;
        return $"UIPANEL player={SingletonBehaviour<Player>.HasInstance} head={(ok ? hp + " " + hr.eulerAngles : "none")} panel={(_panel != null ? _panel.transform.position + " " + _panel.transform.eulerAngles + " scale " + _panel.transform.localScale + " layer " + _panel.layer + " active " + _panel.activeInHierarchy : "none")} main={(main != null ? main.name + " " + main.transform.position + " " + main.transform.eulerAngles + " parent " + (main.transform.parent != null ? main.transform.parent.name : "none") + " mask " + main.cullingMask.ToString("X") : "none")}";
    }

    private static bool InPlay => SingletonBehaviour<Player>.HasInstance && !GameState.IsGamePausedOrNotFocused;

    /// <summary>
    /// Where your head is in the world: in play the VR rig knows; elsewhere the game's own camera is the view.
    /// </summary>
    private static bool HeadPose(out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;
        if (SingletonBehaviour<Player>.HasInstance)
        {
            position = VrRig.HeadPosition;
            rotation = VrRig.HeadRotation;
            return position != Vector3.zero;
        }

        // the level select in the car: your head there
        if (MapRig.Active)
        {
            position = MapRig.HeadPosition;
            rotation = MapRig.HeadRotation;
            return position != Vector3.zero;
        }

        // elsewhere (title, menus) the game poses its camera itself, and that is the view: centre on it
        var main = Camera.main;
        if (main == null) return false;
        position = main.transform.position;
        rotation = main.transform.rotation;
        return true;
    }

    private static void PlacePanel()
    {
        if (!HeadPose(out var headPos, out var headRot)) return;
        var forward = Vector3.ProjectOnPlane(headRot * Vector3.forward, Vector3.up);
        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(headRot * Vector3.up, Vector3.up);
        var yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        var hud = InPlay;
        // menus centre on the view (the title's camera looks up at its hero); the HUD sits by the horizon
        var pitch = hud ? 0f : Mathf.Clamp(Mathf.DeltaAngle(0f, headRot.eulerAngles.x), -40f, 40f);
        var follow = hud ? 25f : 40f;
        // a new view (another scene's camera) or a switch between menus and play: straight in front of you again
        var view = Camera.main;
        if (view != _placedFor || hud != _placedForHud) _placed = false;
        _placedFor = view;
        _placedForHud = hud;
        if (!_placed)
        {
            _panelYaw = yaw;
            _panelPitch = pitch;
            _placed = true;
        }
        else if (Mathf.Abs(Mathf.DeltaAngle(_panelYaw, yaw)) > follow || Mathf.Abs(_panelPitch - pitch) > follow)
        {
            _panelYaw = Mathf.MoveTowardsAngle(_panelYaw, yaw, 240f * Time.unscaledDeltaTime);
            _panelPitch = Mathf.MoveTowards(_panelPitch, pitch, 240f * Time.unscaledDeltaTime);
        }

        // menus: 1.8 m away, 2.1 m wide, at eye level; HUD: 1.5 m away, 1.5 m wide, a little below eye level; in the
        // car (the map book is in your lap, under a metre away): 0.7 m away, 1 m wide, over the book
        var car = MapRig.Active;
        var distance = DevDistance ?? (car ? 0.7f : hud ? 1.5f : 1.8f);
        var width = DevWidth ?? (car ? 1.0f : hud ? 1.5f : 2.1f);
        var drop = hud ? 12f : 4f;
        var rot = Quaternion.Euler(_panelPitch + drop, _panelYaw, 0f);
        _panel.transform.SetPositionAndRotation(headPos + rot * Vector3.forward * distance, rot);
        _panel.transform.localScale = new Vector3(width, width * Height / Width, 1f);
    }
}
