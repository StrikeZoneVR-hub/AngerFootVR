using System.Reflection;
using Framework;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace AngerFootVR.Core;

/// <summary>
/// The level select in VR: the game's own World Map, where you sit in the car with the map book open on your lap.
/// Instead of the movie screen you are in the car: your head looks round from the driver's seat (the game's camera
/// gives the seat; your head the rest, its yaw only so the horizon stays level), the book stays where the game's arms
/// hold it, and a laser from either hand points at the map on its pages - the level it points at is picked (the game's
/// own mouse-pointing, driven by the laser), the trigger or A/X plays it, a grip turns to the next or previous region
/// (the game's own page turns), B/Y goes back, the stick still steps from level to level. The shoe screen (the game
/// lowers its camera to your feet) works the same, your head on top.
/// </summary>
internal static class MapRig
{
    /// <summary>The level select is up and in VR.</summary>
    public static bool Active { get; private set; }

    public static Vector3 HeadPosition { get; private set; }
    public static Quaternion HeadRotation { get; private set; } = Quaternion.identity;

    /// <summary>The World Map's scene is loaded (no player, the map book there).</summary>
    public static bool Wanted =>
        XrManager.IsRunning && !SingletonBehaviour<Player>.HasInstance && SingletonBehaviour<MapBook>.HasInstance &&
        SingletonBehaviour<WorldMap>.HasInstance && (VrSettings.VrMap == null || VrSettings.VrMap.Value);

    private static readonly FieldInfo LeftPage = AccessTools.Field(typeof(MapBook), "_leftPageCollider");
    private static readonly FieldInfo RightPage = AccessTools.Field(typeof(MapBook), "_rightPageCollider");
    private static readonly FieldInfo BookAnimator = AccessTools.Field(typeof(MapBook), "_bookAnimator");
    private static readonly FieldInfo UnlockRoutine = AccessTools.Field(typeof(WorldMap), "_unlockRoutine");
    private static readonly MethodInfo MouseSelection = AccessTools.Method(typeof(WorldMap), "UpdateMouseSelection");
    private static readonly FieldInfo Selected = AccessTools.Field(typeof(WorldMap), "_selectedLevelButton");

    private static Camera _cam;
    private static Transform _anchor, _bookRoot, _bookOldParent;
    private static Vector3 _basePos, _center;
    private static Quaternion _baseRot;
    private static float _eyeHeight;
    private static bool _calibrated;
    private static LineRenderer _laser;
    private static Transform _dot;
    private static XRNode _pointer = XRNode.RightHand;
    private static float _cardAt = -1f;

    /// <summary>Dev: the laser aimed at this point (the test runtime's controllers can't be aimed finely).</summary>
    public static Vector3? DevTarget;

    /// <summary>Each frame (Plugin.Update): into the car when the map comes up, out when it goes.</summary>
    public static void Update()
    {
        var want = Wanted;
        if (want && !Active) Enter();
        else if (!want && Active) Leave();
    }

    private static void Enter()
    {
        var book = SingletonBehaviour<MapBook>.Instance;
        _cam = book.FirstPersonCamera;
        if (_cam == null) return;
        Active = true;
        _calibrated = false;
        XRDevice.DisableAutoXRCameraTracking(_cam, true);
        _cam.stereoTargetEye = StereoTargetEyeMask.Both;
        _cam.targetTexture = null;
        _cam.nearClipPlane = Mathf.Min(_cam.nearClipPlane, 0.05f);
        _cam.cullingMask |= 1 << UI.VrUi.Layer; // (the menus' floating panel: the play panel, the shoe screen)

        // the book stays where the game holds it when your head moves: if it hangs off the camera, it hangs off a
        // stand-in at the camera's own (game) pose instead
        _anchor = new GameObject("AngerFootVR Map Anchor").transform;
        SceneManager.MoveGameObjectToScene(_anchor.gameObject, _cam.gameObject.scene);
        _anchor.SetPositionAndRotation(_cam.transform.position, _cam.transform.rotation);
        var animator = BookAnimator?.GetValue(book) as Animator;
        _bookRoot = null;
        if (animator != null)
        {
            for (var t = animator.transform; t != null; t = t.parent)
                if (t.parent == _cam.transform)
                {
                    _bookRoot = t;
                    break;
                }

            if (_bookRoot != null)
            {
                _bookOldParent = _bookRoot.parent;
                _bookRoot.SetParent(_anchor, true);
            }
        }

        EnsureLaser();
        Plugin.L.LogInfo($"Level select in VR: the car's camera '{_cam.name}', the book {(_bookRoot != null ? "re-anchored ('" + _bookRoot.name + "')" : "not on the camera")}");
    }

    private static void Leave()
    {
        Active = false;
        if (_bookRoot != null && _bookOldParent != null) _bookRoot.SetParent(_bookOldParent, true);
        _bookRoot = _bookOldParent = null;
        if (_laser != null) _laser.gameObject.SetActive(false);
        if (_dot != null) _dot.gameObject.SetActive(false);
        _cam = null;
        Plugin.L.LogInfo("Level select: left");
    }

    /// <summary>After the game's own updates (it sets its camera's pose each frame): the head, the laser, the pointing.</summary>
    public static void LateUpdate()
    {
        if (!Active) return;
        if (_cam == null)
        {
            Leave();
            return;
        }

        _basePos = _cam.transform.position;
        _baseRot = _cam.transform.rotation;
        if (_anchor != null) _anchor.SetPositionAndRotation(_basePos, _baseRot);
        if (!PlaceCamera()) return;
        Point();
    }

    /// <summary>Just before the frame is drawn: the camera again from the newest head pose.</summary>
    public static void BeforeRender()
    {
        if (Active && _cam != null && _calibrated) PlaceCamera();
    }

    private static float BaseYaw
    {
        get
        {
            var f = Vector3.ProjectOnPlane(_baseRot * Vector3.forward, Vector3.up);
            return f.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }
    }

    private static bool PlaceCamera()
    {
        if (!VrRig.TryGetTrackedHead(out var pos, out var rot)) return false;
        if (!_calibrated)
        {
            _center = new Vector3(pos.x, 0f, pos.z);
            _eyeHeight = pos.y;
            _calibrated = true;
        }

        var yaw = Quaternion.Euler(0f, BaseYaw, 0f);
        var offset = pos - _center;
        offset.y = Mathf.Clamp(pos.y - _eyeHeight, -0.4f, 0.25f);
        var lean = new Vector2(offset.x, offset.z);
        if (lean.magnitude > 0.35f) lean = lean.normalized * 0.35f;
        offset.x = lean.x;
        offset.z = lean.y;
        HeadPosition = _basePos + yaw * offset;
        HeadRotation = yaw * rot;
        _cam.transform.SetPositionAndRotation(HeadPosition, HeadRotation);
        // the hands round the head (for the laser)
        VrHands.UpdateAt(HeadPosition, pos, BaseYaw);
        return true;
    }

    /// <summary>Recentre: your head where it is now becomes the driver's seat.</summary>
    public static void Recenter() => _calibrated = false;

    // ------------------------------------------------------------------ pointing at the map

    private static void Point()
    {
        var book = SingletonBehaviour<MapBook>.Instance;
        var map = SingletonBehaviour<WorldMap>.Instance;
        var left = LeftPage?.GetValue(book) as MeshCollider;
        var right = RightPage?.GetValue(book) as MeshCollider;
        var ui = SingletonBehaviour<WorldMapUI>.HasInstance ? SingletonBehaviour<WorldMapUI>.Instance : null;
        var panel = SingletonBehaviour<WorldMapPlayLevelPanel>.HasInstance ? SingletonBehaviour<WorldMapPlayLevelPanel>.Instance : null;
        var pointing = book.IsOpen && ui != null && ui.ShowingBook && (panel == null || !panel.IsVisible) && UnlockRoutine?.GetValue(map) == null;

        // the level's card is up: the trigger (or A) plays it, B puts it away (as the game's own PLAY button and a click
        // beside the card do; the press that brought the card up doesn't count)
        if (panel != null && panel.IsVisible)
        {
            if (_cardAt < 0f) _cardAt = Time.unscaledTime;
            if (Time.unscaledTime - _cardAt > 0.35f)
            {
                if (VrControls.DownEither(VrButton.Trigger) || VrControls.Down(XRNode.RightHand, VrButton.Primary))
                {
                    Plugin.L.LogInfo("Level select: play");
                    map.PlaySelectedLevel();
                }
                else if (VrControls.DownEither(VrButton.Secondary))
                {
                    panel.Hide(instant: false);
                    if (SingletonBehaviour<WorldMapLevelPreview>.HasInstance) SingletonBehaviour<WorldMapLevelPreview>.Instance.Show(mouseSelection: true);
                }
            }
        }
        else _cardAt = -1f;

        // the hand that last pulled its trigger points (either hand works)
        if (VrControls.Down(XRNode.LeftHand, VrButton.Trigger)) _pointer = XRNode.LeftHand;
        else if (VrControls.Down(XRNode.RightHand, VrButton.Trigger)) _pointer = XRNode.RightHand;
        var hand = VrHands.Get(_pointer);
        if (!hand.Tracked)
        {
            _pointer = _pointer == XRNode.RightHand ? XRNode.LeftHand : XRNode.RightHand;
            hand = VrHands.Get(_pointer);
        }

        if (!pointing || !hand.Tracked)
        {
            ShowLaser(false, default, default);
            return;
        }

        var ray = new Ray(hand.Position, DevTarget.HasValue ? (DevTarget.Value - hand.Position).normalized : hand.Aim * Vector3.forward);
        RaycastHit hit = default;
        var onPage = (left != null && left.Raycast(ray, out hit, 5f)) || (right != null && right.Raycast(ray, out hit, 5f));
        ShowLaser(true, ray.origin, onPage ? hit.point : ray.origin + ray.direction * 1.2f);
        if (!onPage || MouseSelection == null) return;

        // the game's own mouse-pointing: the page's spot on the map, then the level button under it
        var mapCam = SingletonBehaviour<WorldMapCamera>.Instance.Camera;
        var r = mapCam.NormalizedScreenPointToRay(hit.textureCoord);
        if (!new Plane(Vector3.up, Vector3.zero).Raycast(r, out var d)) return;
        map.SetMapBookRaycastPosition(r.GetPoint(d));
        MouseSelection.Invoke(map, null);
    }

    private static void EnsureLaser()
    {
        if (_laser != null) return;
        var go = new GameObject("AngerFootVR Map Laser");
        Object.DontDestroyOnLoad(go);
        _laser = go.AddComponent<LineRenderer>();
        _laser.positionCount = 2;
        _laser.useWorldSpace = true;
        _laser.startWidth = 0.004f;
        _laser.endWidth = 0.0025f;
        var m = new Material(Shader.Find("Sprites/Default")) { name = "AngerFootVR Map Laser" };
        _laser.sharedMaterial = m;
        _laser.startColor = new Color(1f, 0.25f, 0.65f, 0.9f);
        _laser.endColor = new Color(1f, 0.4f, 0.75f, 0.6f);
        _laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _laser.receiveShadows = false;
        var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(dot.GetComponent<Collider>());
        dot.name = "AngerFootVR Map Laser Dot";
        Object.DontDestroyOnLoad(dot);
        dot.transform.localScale = Vector3.one * 0.012f;
        var dm = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.85f, 0.95f, 1f) };
        dot.GetComponent<Renderer>().sharedMaterial = dm;
        _dot = dot.transform;
    }

    private static void ShowLaser(bool on, Vector3 from, Vector3 to)
    {
        if (_laser == null) return;
        _laser.gameObject.SetActive(on);
        _dot.gameObject.SetActive(on);
        if (!on) return;
        // (on a layer the car's camera draws)
        var layer = 0;
        for (var l = 0; l < 32; l++)
            if ((_cam.cullingMask & (1 << l)) != 0)
            {
                layer = l;
                break;
            }

        _laser.gameObject.layer = layer;
        _dot.gameObject.layer = layer;
        _laser.SetPosition(0, from);
        _laser.SetPosition(1, to);
        _dot.position = to;
    }

    /// <summary>Dev: the levels on the open pages, each with the spot on the book where it's drawn.</summary>
    public static string Buttons(int aim)
    {
        if (!SingletonBehaviour<WorldMap>.HasInstance || !SingletonBehaviour<MapBook>.HasInstance) return "no map";
        var map = SingletonBehaviour<WorldMap>.Instance;
        var book = SingletonBehaviour<MapBook>.Instance;
        var b = new System.Text.StringBuilder("MAPBUTTONS region " + (map.CurrentRegion != null ? map.CurrentRegion.name : "none"));
        var i = 0;
        foreach (var btn in Object.FindObjectsOfType<WorldMapLevelButton>())
        {
            var uv = book.WorldMapWorldPositionToBookUV(btn.transform.position);
            var onBook = book.BookUVToMapbookSpace(uv);
            b.Append($" | {i}: {btn.Level.name} {btn.UnlockState} uv {uv} at {onBook}");
            if (i == aim) DevTarget = onBook;
            i++;
        }

        if (aim < 0) DevTarget = null;
        return b.ToString();
    }

    public static string Describe() =>
        $"MAP active={Active} wanted={Wanted} cam={(_cam != null ? _cam.name + " " + _cam.transform.position + " base " + _basePos + " " + _baseRot.eulerAngles : "none")} book={(_bookRoot != null ? _bookRoot.name : "none")} pointer={_pointer} head={HeadPosition} selected={(SingletonBehaviour<WorldMap>.HasInstance && Selected?.GetValue(SingletonBehaviour<WorldMap>.Instance) is WorldMapLevelButton b && b != null ? b.Level.name : "none")} laser={(_laser != null && _laser.gameObject.activeSelf ? _laser.GetPosition(0) + " -> " + _laser.GetPosition(1) : "off")}";
}
