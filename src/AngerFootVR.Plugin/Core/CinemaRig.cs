using System.Collections.Generic;
using Framework;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.Core;

/// <summary>
/// The cinematic area's view. Whenever you're not playing (the title, menus, loading, a paused game) you stand in the
/// movie house as your own body: the game's cameras stop drawing to the headset (they draw into a tiny texture, which
/// spares the GPU), the cinema camera shows the movie house from your tracked head, and the game's UI plays on its
/// big screen. The movie house's own light and air (no fog, a dark ambient) are swapped in only while its camera draws.
/// </summary>
internal static class CinemaRig
{
    public static bool Active { get; private set; }
    public static Camera Camera { get; private set; }

    /// <summary>Your head in the movie house this frame.</summary>
    public static Vector3 HeadPosition { get; private set; }

    public static Quaternion HeadRotation { get; private set; } = Quaternion.identity;

    /// <summary>The cinema's facing (degrees): turning there turns this.</summary>
    public static float Yaw;

    /// <summary>The movie house's ambient light (the opening brings it up from the dark).</summary>
    public static float AmbientScale = 1f;

    /// <summary>Dev: stay in the cinema even in play.</summary>
    public static bool DevForce;

    private static readonly Dictionary<Camera, (StereoTargetEyeMask eye, RenderTexture target)> Parked = new Dictionary<Camera, (StereoTargetEyeMask, RenderTexture)>();
    private static RenderTexture _sink;
    private static float _nextScan;
    private static Vector3 _center;
    private static bool _centered;
    private static Vector3 _walk; // how far you've walked from your spot (world metres)

    /// <summary>Walking speed in the movie house (metres a second at full stick).</summary>
    public const float WalkSpeed = 2.0f;

    private static bool Walkable(Vector3 p) => UI.MovieHouse.Walkable(p);

    private static bool _savedFog;
    private static Color _savedAmbient;
    private static UnityEngine.Rendering.AmbientMode _savedMode;

    private static bool Wanted =>
        XrManager.IsRunning && (DevForce || !SingletonBehaviour<Player>.HasInstance || GameState.IsGamePausedOrNotFocused) && !MapRig.Wanted;

    public static void Update()
    {
        var want = Wanted;
        if (want && !Active) Enter();
        else if (!want && Active) Leave();
        if (!Active) return;
        if (Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + 0.25f;
            ParkGameCameras();
            Listen(true); // (a scene load brings its own listener)
        }
    }

    /// <summary>After the game's own updates: the head, hands and body for this frame.</summary>
    public static void LateUpdate()
    {
        if (!Active || Camera == null) return;
        VrRig.TryGetTrackedHead(out var tracked, out var rot);
        if (!_centered)
        {
            _center = new Vector3(tracked.x, 0f, tracked.z);
            _centered = true;
        }

        var height = tracked.y > 0.5f ? tracked.y : 1.62f; // the test runtime's head sits on the floor
        var local = new Vector3(tracked.x - _center.x, height, tracked.z - _center.z);

        // the right stick turns you (snap or smooth, as in play), round where you stand: the menus are the laser's
        var turn = VrRig.TurnDelta(VrControls.DevTurn ?? VrControls.Stick(XRNode.RightHand).x, ref _snapHeld);
        if (turn != 0f)
        {
            var flatLocal = new Vector3(local.x, 0f, local.z);
            _walk += Quaternion.Euler(0f, Yaw, 0f) * flatLocal - Quaternion.Euler(0f, Yaw + turn, 0f) * flatLocal;
            Yaw += turn;
        }

        var yawRot = Quaternion.Euler(0f, Yaw, 0f);

        // walking: the left stick, the way you look, on the open floor (the aisles, the lobby; not through the seats)
        var stick = VrControls.DevMove != Vector2.zero ? VrControls.DevMove : VrControls.Stick(XRNode.LeftHand);
        if (_seated && stick.magnitude > 0.5f) UI.Lobby.GetUp();
        if (stick.magnitude > 0.2f && !_seated)
        {
            var look = Vector3.ProjectOnPlane(yawRot * rot * Vector3.forward, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, look);
            var step = (look * stick.y + right * stick.x) * (WalkSpeed * Mathf.Clamp01(stick.magnitude) * Time.unscaledDeltaTime);
            var feet = UI.MovieHouse.Standing + _walk + yawRot * new Vector3(local.x, 0f, local.z) - UI.MovieHouse.Origin;
            if (Walkable(feet + new Vector3(step.x, 0f, 0f))) _walk.x += step.x;
            if (Walkable(feet + new Vector3(0f, 0f, step.z))) _walk.z += step.z;
        }

        HeadPosition = UI.MovieHouse.Standing + _walk + yawRot * local;
        HeadRotation = yawRot * rot;
        Camera.transform.SetPositionAndRotation(HeadPosition, HeadRotation);
        _height = height;
        VrHands.UpdateAt(HeadPosition, tracked, Yaw);
        // your standing eye height: the highest your head has been since you came in (or recentred)
        if (tracked.y > 0.5f) _standingEye = Mathf.Max(_standingEye, tracked.y);
        Body.HeroBody.PoseCinema(HeadPosition, HeadRotation, UI.MovieHouse.Standing.y + _seatRise, _standingEye > 0.5f ? _standingEye : height);

        UI.MovieHouse.Animate();
        UI.RageRoom.Prepare();
        UI.FunWorld.Update();
        Interact();
    }

    private static Vector3 _lastStep;

    /// <summary>Kicks (B/X: your leg swings; at the padded doors they burst open, in the rage room things break) and
    /// footsteps (soft on the carpet, harder on the rage room's concrete).</summary>
    private static void Interact()
    {
        var gazing = !VrHands.Get(XRNode.LeftHand).Tracked && !VrHands.Get(XRNode.RightHand).Tracked;
        var kick = VrControls.Down(XRNode.LeftHand, VrButton.Primary) || VrControls.Down(XRNode.RightHand, VrButton.Secondary) && !UI.Laser.OnScreen;
        if (kick && !(gazing && UI.Laser.OnScreen))
        {
            Body.HeroBody.Kick();
            var facing = HeadRotation * Vector3.forward;
            if (!UI.MovieHouse.TryKickDoor(HeadPosition, facing)) UI.FunWorld.Kick(HeadPosition, facing);
        }

        var feet = new Vector3(HeadPosition.x, UI.MovieHouse.Standing.y, HeadPosition.z);
        var stepped = false;
        if ((feet - _lastStep).magnitude > 0.7f)
        {
            if (_lastStep != Vector3.zero && (feet - _lastStep).magnitude < 3f)
            {
                stepped = true;
                UI.Sounds.Play(UI.MovieHouse.HardFloor(feet - UI.MovieHouse.Origin) ? "step_hard" : "step", feet, 0.3f);
            }

            _lastStep = feet;
        }

        UI.Fx.Update(feet, stepped);
    }

    private static readonly List<AudioListener> Silenced = new List<AudioListener>();

    /// <summary>The movie house listens from your head (its sounds come from where they happen); the game's own listener
    /// rests meanwhile (its music and menu sounds are flat, they play the same).</summary>
    private static void Listen(bool here)
    {
        if (Camera == null) return;
        var mine = Camera.GetComponent<AudioListener>() ?? Camera.gameObject.AddComponent<AudioListener>();
        if (here)
        {
            foreach (var l in Object.FindObjectsOfType<AudioListener>())
                if (l != mine && l.enabled)
                {
                    l.enabled = false;
                    Silenced.Add(l);
                }

            mine.enabled = true;
        }
        else
        {
            mine.enabled = false;
            foreach (var l in Silenced)
                if (l != null)
                    l.enabled = true;
            Silenced.Clear();
        }
    }

    private static float _height = 1.62f, _standingEye;
    private static bool _snapHeld;

    /// <summary>Just before the frame is drawn: the camera again, from the newest head pose (the one Unity draws the
    /// eyes with), as Unity's own tracked pose driver does.</summary>
    public static void BeforeRender()
    {
        if (!Active || Camera == null || !_centered) return;
        if (!VrRig.TryGetTrackedHead(out var tracked, out var rot)) return;
        var yawRot = Quaternion.Euler(0f, Yaw, 0f);
        var local = new Vector3(tracked.x - _center.x, tracked.y > 0.5f ? tracked.y : _height, tracked.z - _center.z);
        var before = HeadRotation;
        HeadPosition = UI.MovieHouse.Standing + _walk + yawRot * local;
        HeadRotation = yawRot * rot;
        Camera.transform.SetPositionAndRotation(HeadPosition, HeadRotation);
        HeadTiming.Note(before, HeadRotation);
    }

    private static void Enter()
    {
        if (!UI.MovieHouse.Build(UI.VrUi.Layer)) return;
        UI.MovieHouse.Root.gameObject.SetActive(true);
        VrHands.LetGo(); // (whatever the level had in your hands stays there)
        UI.Dummy.TryCapture(); // (paused in a level: a goon of the game's for the rage room's dummy)
        if (Camera == null)
        {
            var go = new GameObject("AngerFootVR Cinema Camera");
            Object.DontDestroyOnLoad(go);
            Camera = go.AddComponent<Camera>();
            Camera.stereoTargetEye = StereoTargetEyeMask.Both;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = Color.black;
            Camera.cullingMask = 1 << UI.VrUi.Layer;
            Camera.nearClipPlane = 0.12f; // the jacket's collar stands close round your eyes
            Camera.farClipPlane = 60f;
            Camera.depth = 1000;
            Camera.allowHDR = false;
            Camera.allowMSAA = true;
            XRDevice.DisableAutoXRCameraTracking(Camera, true);
            UI.Mirror.Viewer = Camera;
            UnityEngine.Camera.onPreCull += LightsOn;
            UnityEngine.Camera.onPostRender += LightsOff;
        }

        Camera.gameObject.SetActive(true);
        _centered = false;
        _walk = Vector3.zero; // back to your spot facing the screen
        Yaw = 0f;
        _seated = false;
        _seatRise = 0f;
        Active = true;
        UI.MovieHouse.CloseDoors();
        ParkGameCameras();
        Listen(true);
        Plugin.L.LogInfo("Cinema: in the movie house");
    }

    /// <summary>Dev: you stand at this spot of the movie house (local metres), facing this way (degrees).</summary>
    public static void DevGo(float x, float z, float yaw)
    {
        if (!Active) return;
        VrRig.TryGetTrackedHead(out var tracked, out _);
        Yaw = yaw;
        var local = Quaternion.Euler(0f, yaw, 0f) * new Vector3(tracked.x - _center.x, 0f, tracked.z - _center.z);
        _walk = UI.MovieHouse.Origin + new Vector3(x, 0f, z) - UI.MovieHouse.Standing - local;
        _walk.y = 0f;
    }

    private static bool _seated;
    private static float _seatRise;

    /// <summary>Whether you're sitting in one of the movie house's seats.</summary>
    public static bool Seated => _seated;

    /// <summary>Sit in a seat (its cushion's middle, local): your eyes go to a seated height over it, facing the screen.</summary>
    public static void Sit(Vector3 seatLocal, float rise)
    {
        if (!Active) return;
        VrRig.TryGetTrackedHead(out var tracked, out _);
        Yaw = 0f;
        var local = new Vector3(tracked.x - _center.x, 0f, tracked.z - _center.z);
        var eye = _standingEye > 0.5f ? _standingEye : 1.62f;
        _walk = UI.MovieHouse.Origin + new Vector3(seatLocal.x, 0f, seatLocal.z) - UI.MovieHouse.Standing - local;
        _walk.y = rise + 1.12f - eye; // (seated, your eyes are about 1.12 m over the seat's floor)
        _seatRise = rise;
        _seated = true;
    }

    /// <summary>Up out of the seat, into the aisle (local).</summary>
    public static void Stand(Vector3 aisleLocal)
    {
        if (!_seated) return;
        _seated = false;
        _seatRise = 0f;
        DevGo(aisleLocal.x, aisleLocal.z, Yaw);
    }

    /// <summary>Back to your spot facing the screen, centred on where you stand now.</summary>
    public static void Recenter()
    {
        _centered = false;
        _walk = Vector3.zero;
        Yaw = 0f;
        _standingEye = 0f;
        Body.HeroBody.Replant();
    }

    private static void Leave()
    {
        Active = false;
        if (Camera != null) Camera.gameObject.SetActive(false);
        // (it shares the menu panel's layer, 5 km under the world: the car's camera, drawing the panel, showed it out of
        // the windows. Only there while you are)
        if (UI.MovieHouse.Root != null) UI.MovieHouse.Root.gameObject.SetActive(false);
        foreach (var kv in Parked)
        {
            if (kv.Key == null) continue;
            // a camera the game has given a picture of its own to draw into while it was parked (the level select's
            // map, drawn onto the book's pages) keeps it, and stays off the headset: given the headset back, VR drew it
            // with the headset's view, and the book showed the whole city small and dark
            if (kv.Key.targetTexture != _sink)
            {
                kv.Key.stereoTargetEye = StereoTargetEyeMask.None;
                continue;
            }

            kv.Key.targetTexture = kv.Value.target;
            kv.Key.stereoTargetEye = kv.Value.eye;
        }

        Parked.Clear();
        Listen(false);
        UI.RageRoom.Clear();
        UI.RageMode.Clear();
        UI.Lobby.Clear();
        UI.Arcade.Clear();
        UI.GunRange.Clear();
        UI.Dummy.Clear();
        UI.FunWorld.Clear();
        UI.Fx.Clear();
        Plugin.L.LogInfo("Cinema: back to the game");
    }

    /// <summary>The projector feed: what the game's cameras draw while you're here (shown on the big screen).</summary>
    public static RenderTexture Feed => _sink;

    /// <summary>
    /// The game's cameras (new ones too) stop drawing to the headset while you're here and draw the game's own picture
    /// (the title's animated scene, a paused level) into the projector feed instead, in their usual order, so the
    /// screen shows exactly what the flat game would; the menu plays over it.
    /// </summary>
    private static void ParkGameCameras()
    {
        if (_sink == null)
        {
            _sink = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { name = "AngerFootVR projector feed", antiAliasing = 2 };
            _sink.Create();
            Object.DontDestroyOnLoad(_sink);
            UI.MovieHouse.ShowFeed(_sink);
        }

        foreach (var cam in UnityEngine.Camera.allCameras)
        {
            if (cam == null || cam == Camera || Parked.ContainsKey(cam) || cam.targetTexture != null) continue;
            if (cam.name.StartsWith("AngerFootVR")) continue;
            Parked[cam] = (cam.stereoTargetEye, cam.targetTexture);
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.targetTexture = _sink;
        }

        var dead = new List<Camera>();
        foreach (var c in Parked.Keys)
            if (c == null)
                dead.Add(c);
        foreach (var c in dead) Parked.Remove(c);
    }

    private static void LightsOn(Camera cam)
    {
        if (cam != Camera && cam.name != "AngerFootVR MirrorCamera") return;
        _savedFog = RenderSettings.fog;
        _savedAmbient = RenderSettings.ambientLight;
        _savedMode = RenderSettings.ambientMode;
        RenderSettings.fog = false;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.2f, 0.19f, 0.21f) * AmbientScale; // dim, near-neutral: the neon gives the colour
    }

    private static void LightsOff(Camera cam)
    {
        if (cam != Camera && cam.name != "AngerFootVR MirrorCamera") return;
        RenderSettings.fog = _savedFog;
        RenderSettings.ambientMode = _savedMode;
        RenderSettings.ambientLight = _savedAmbient;
    }
}
