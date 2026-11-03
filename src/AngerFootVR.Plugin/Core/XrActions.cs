using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace AngerFootVR.Core;

/// <summary>
/// The VR controllers, read through the mod's own OpenXR action set (InkVR's, which is tested on Virtual Desktop, SteamVR
/// and Link). Unity's pre-1.0 provider only shows controllers to the game when its managed half sets up their actions,
/// and that half isn't here, so without this the headset tracks but the controllers do nothing (the first test build).
/// Profiles: Quest Touch, Index, Vive wands, Windows MR and the simple controller. The set is attached to the provider's
/// own session, synced each frame, and the hands are located at the frame's predicted display time (from xrWaitFrame)
/// in the provider's play space, the same space the head is in.
/// </summary>
internal static unsafe class XrActions
{
    public sealed class Hand
    {
        public bool Active, PoseValid;
        public Vector3 GripPosition, AimPosition;
        public Quaternion GripRotation = Quaternion.identity, AimRotation = Quaternion.identity;
        public Vector2 Stick;
        public float Trigger, Grip;
        public bool StickClick, Primary, Secondary, Menu, ThumbTouch;
    }

    public static readonly Hand Left = new Hand(), Right = new Hand();
    public static bool Ready => _attached;
    public static string Profile { get; private set; } = "";

    // ---------------------------------------------------------------- OpenXR types (openxr.h 1.0)

    [StructLayout(LayoutKind.Sequential)] private struct Quat { public float x, y, z, w; }
    [StructLayout(LayoutKind.Sequential)] private struct Vec3 { public float x, y, z; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Posef
    {
        public Quat orientation;
        public Vec3 position;
        public static Posef Identity => new Posef { orientation = new Quat { w = 1 } };

        // OpenXR is right-handed (-z forward), Unity left-handed (+z forward)
        public Vector3 UnityPosition => new Vector3(position.x, position.y, -position.z);
        public Quaternion UnityRotation => new Quaternion(-orientation.x, -orientation.y, orientation.z, orientation.w);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ActionSetCreateInfo
    {
        public int type;
        public void* next;
        public fixed byte actionSetName[64];
        public fixed byte localizedActionSetName[128];
        public uint priority;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ActionCreateInfo
    {
        public int type;
        public void* next;
        public fixed byte actionName[64];
        public int actionType;
        public uint countSubactionPaths;
        public ulong* subactionPaths;
        public fixed byte localizedActionName[128];
    }

    [StructLayout(LayoutKind.Sequential)] private struct SuggestedBinding { public ulong action, binding; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProfileSuggestedBinding
    {
        public int type;
        public void* next;
        public ulong interactionProfile;
        public uint countSuggestedBindings;
        public SuggestedBinding* suggestedBindings;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ActionSpaceCreateInfo
    {
        public int type;
        public void* next;
        public ulong action, subactionPath;
        public Posef poseInActionSpace;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AttachInfo
    {
        public int type;
        public void* next;
        public uint countActionSets;
        public ulong* actionSets;
    }

    [StructLayout(LayoutKind.Sequential)] private struct ActiveActionSet { public ulong actionSet, subactionPath; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SyncInfo
    {
        public int type;
        public void* next;
        public uint countActiveActionSets;
        public ActiveActionSet* activeActionSets;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GetInfo
    {
        public int type;
        public void* next;
        public ulong action, subactionPath;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StateBool
    {
        public int type;
        public void* next;
        public uint currentState, changedSinceLastSync;
        public long lastChangeTime;
        public uint isActive;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StateFloat
    {
        public int type;
        public void* next;
        public float currentState;
        public uint changedSinceLastSync;
        public long lastChangeTime;
        public uint isActive;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StateVec2
    {
        public int type;
        public void* next;
        public float x, y;
        public uint changedSinceLastSync;
        public long lastChangeTime;
        public uint isActive;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StatePose
    {
        public int type;
        public void* next;
        public uint isActive;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpaceLocation
    {
        public int type;
        public void* next;
        public ulong locationFlags;
        public Posef pose;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HapticInfo
    {
        public int type;
        public void* next;
        public ulong action, subactionPath;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HapticVibration
    {
        public int type;
        public void* next;
        public long duration;
        public float frequency, amplitude;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProfileState
    {
        public int type;
        public void* next;
        public ulong interactionProfile;
    }

    private const int TypeHapticVibration = 13, TypeStateBool = 23, TypeStateFloat = 24, TypeStateVec2 = 25, TypeStatePose = 27,
        TypeActionSetCreate = 28, TypeActionCreate = 29, TypeActionSpaceCreate = 38, TypeSpaceLocation = 42,
        TypeProfileSuggested = 51, TypeProfileState = 53, TypeGetInfo = 58, TypeHapticInfo = 59, TypeAttach = 60, TypeSync = 61;

    private const int Boolean = 1, Float = 2, Vector2f = 3, Pose = 4, Vibration = 100;

    // ---------------------------------------------------------------- entry points (through the loader)

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int StringToPathFn(ulong instance, byte* path, ulong* result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PathToStringFn(ulong instance, ulong path, uint capacity, uint* count, byte* buffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateActionSetFn(ulong instance, ActionSetCreateInfo* info, ulong* set);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateActionFn(ulong set, ActionCreateInfo* info, ulong* action);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SuggestFn(ulong instance, ProfileSuggestedBinding* info);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int AttachFn(ulong session, AttachInfo* info);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateActionSpaceFn(ulong session, ActionSpaceCreateInfo* info, ulong* space);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SyncFn(ulong session, SyncInfo* info);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetBoolFn(ulong session, GetInfo* info, StateBool* state);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetFloatFn(ulong session, GetInfo* info, StateFloat* state);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetVec2Fn(ulong session, GetInfo* info, StateVec2* state);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetPoseFn(ulong session, GetInfo* info, StatePose* state);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int LocateSpaceFn(ulong space, ulong baseSpace, long time, SpaceLocation* location);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int HapticFn(ulong session, HapticInfo* info, HapticVibration* vibration);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ProfileFn(ulong session, ulong topLevelPath, ProfileState* state);

    private static StringToPathFn _stringToPath;
    private static PathToStringFn _pathToString;
    private static CreateActionSetFn _createActionSet;
    private static CreateActionFn _createAction;
    private static SuggestFn _suggest;
    private static AttachFn _attach;
    private static CreateActionSpaceFn _createActionSpace;
    private static SyncFn _sync;
    private static GetBoolFn _getBool;
    private static GetFloatFn _getFloat;
    private static GetVec2Fn _getVec2;
    private static GetPoseFn _getPose;
    private static LocateSpaceFn _locateSpace;
    private static HapticFn _haptic;
    private static ProfileFn _profile;

    private static T Fn<T>(ulong instance, string name) where T : class
    {
        var p = OpenXRHook.Resolve(instance, name);
        if (p == IntPtr.Zero) throw new Exception(name + " unavailable");
        return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
    }

    // ---------------------------------------------------------------- state

    private static ulong _instance, _session, _set, _left, _right;
    private static ulong _gripPose, _aimPose, _stickA, _stickClickA, _triggerA, _gripA, _primaryA, _secondaryA, _menuA, _hapticA, _thumbA;
    private static ulong _gripSpaceL, _gripSpaceR, _aimSpaceL, _aimSpaceR;
    private static bool _created, _attached, _failed;
    private static float _profileCheckAt;

    public static void Update()
    {
        if (_failed || !XrManager.IsRunning) return;
        if (!_created && XrManager.Instance != 0) Create(XrManager.Instance);
        if (_created && !_attached) Attach();
        if (!_attached) return;

        var active = new ActiveActionSet { actionSet = _set };
        var sync = new SyncInfo { type = TypeSync, countActiveActionSets = 1, activeActionSets = &active };
        if (_sync(_session, &sync) < 0) return; // (not focused is a success code)
        Read(Left, _left);
        Read(Right, _right);
        LocatePoses();

        if (Time.unscaledTime > _profileCheckAt)
        {
            _profileCheckAt = Time.unscaledTime + 2f;
            var state = new ProfileState { type = TypeProfileState };
            if (_profile(_session, _right, &state) == 0)
            {
                var name = PathString(state.interactionProfile);
                if (name != Profile)
                {
                    Profile = name;
                    Plugin.L.LogInfo($"Controller profile: '{name}'");
                }
            }
        }
    }

    /// <summary>The hands, located for the frame's display time (again right before posing, for less lag).</summary>
    public static void LocatePoses()
    {
        if (!_attached) return;
        var time = OpenXRHook.PredictedDisplayTime;
        var space = XrManager.AppSpace;
        if (time == 0 || space == 0) return;
        Locate(Left, _gripSpaceL, _aimSpaceL, space, time);
        Locate(Right, _gripSpaceR, _aimSpaceR, space, time);
    }

    public static void Haptic(bool left, float amplitude, float seconds)
    {
        if (!_attached) return;
        var info = new HapticInfo { type = TypeHapticInfo, action = _hapticA, subactionPath = left ? _left : _right };
        var vib = new HapticVibration { type = TypeHapticVibration, duration = (long)(seconds * 1e9), frequency = 0, amplitude = Mathf.Clamp01(amplitude) };
        _haptic(_session, &info, &vib);
    }

    private static void Create(ulong instance)
    {
        _instance = instance;
        try
        {
            _stringToPath = Fn<StringToPathFn>(instance, "xrStringToPath");
            _pathToString = Fn<PathToStringFn>(instance, "xrPathToString");
            _createActionSet = Fn<CreateActionSetFn>(instance, "xrCreateActionSet");
            _createAction = Fn<CreateActionFn>(instance, "xrCreateAction");
            _suggest = Fn<SuggestFn>(instance, "xrSuggestInteractionProfileBindings");
            _attach = Fn<AttachFn>(instance, "xrAttachSessionActionSets");
            _createActionSpace = Fn<CreateActionSpaceFn>(instance, "xrCreateActionSpace");
            _sync = Fn<SyncFn>(instance, "xrSyncActions");
            _getBool = Fn<GetBoolFn>(instance, "xrGetActionStateBoolean");
            _getFloat = Fn<GetFloatFn>(instance, "xrGetActionStateFloat");
            _getVec2 = Fn<GetVec2Fn>(instance, "xrGetActionStateVector2f");
            _getPose = Fn<GetPoseFn>(instance, "xrGetActionStatePose");
            _locateSpace = Fn<LocateSpaceFn>(instance, "xrLocateSpace");
            _haptic = Fn<HapticFn>(instance, "xrApplyHapticFeedback");
            _profile = Fn<ProfileFn>(instance, "xrGetCurrentInteractionProfile");
        }
        catch (Exception e)
        {
            Plugin.L.LogError("Controllers: OpenXR functions missing (" + e.Message + ")");
            _failed = true;
            return;
        }

        _left = Path("/user/hand/left");
        _right = Path("/user/hand/right");
        var info = new ActionSetCreateInfo { type = TypeActionSetCreate };
        CopyName(info.actionSetName, 64, "angerfootvr");
        CopyName(info.localizedActionSetName, 128, "Anger Foot VR");
        ulong set;
        if (Check(_createActionSet(instance, &info, &set), "xrCreateActionSet") < 0)
        {
            _failed = true;
            return;
        }

        _set = set;
        _gripPose = Action("grip_pose", "Grip pose", Pose);
        _aimPose = Action("aim_pose", "Aim pose", Pose);
        _stickA = Action("thumbstick", "Thumbstick", Vector2f);
        _stickClickA = Action("thumbstick_click", "Thumbstick click", Boolean);
        _triggerA = Action("trigger", "Trigger", Float);
        _gripA = Action("grip", "Grip", Float);
        _primaryA = Action("primary", "A / X", Boolean);
        _secondaryA = Action("secondary", "B / Y", Boolean);
        _menuA = Action("menu", "Menu", Boolean);
        _hapticA = Action("haptic", "Haptics", Vibration);
        _thumbA = Action("thumb_touch", "Thumb rest touch", Boolean);
        SuggestTouch();
        SuggestIndex();
        SuggestVive();
        SuggestWindowsMr();
        SuggestSimple();
        _created = true;
        Plugin.L.LogInfo("Controllers: actions created");
    }

    private static ulong Action(string name, string localized, int type)
    {
        var sub = stackalloc ulong[2];
        sub[0] = _left;
        sub[1] = _right;
        var info = new ActionCreateInfo { type = TypeActionCreate, actionType = type, countSubactionPaths = 2, subactionPaths = sub };
        CopyName(info.actionName, 64, name);
        CopyName(info.localizedActionName, 128, localized);
        ulong action;
        Check(_createAction(_set, &info, &action), "xrCreateAction " + name);
        return action;
    }

    private static void Common(List<(ulong, string)> b, string p, bool squeezeValue = true)
    {
        b.Add((_gripPose, p + "input/grip/pose"));
        b.Add((_aimPose, p + "input/aim/pose"));
        b.Add((_triggerA, p + "input/trigger/value"));
        b.Add((_gripA, p + (squeezeValue ? "input/squeeze/value" : "input/squeeze/click")));
        b.Add((_hapticA, p + "output/haptic"));
    }

    private static void SuggestTouch()
    {
        var b = new List<(ulong, string)>();
        foreach (var h in new[] { "left", "right" })
        {
            var p = $"/user/hand/{h}/";
            Common(b, p);
            b.Add((_stickA, p + "input/thumbstick"));
            b.Add((_stickClickA, p + "input/thumbstick/click"));
            b.Add((_thumbA, p + "input/thumbstick/touch"));
        }

        foreach (var t in new[] { "left/input/x", "left/input/y", "right/input/a", "right/input/b" }) b.Add((_thumbA, "/user/hand/" + t + "/touch"));
        b.Add((_primaryA, "/user/hand/left/input/x/click"));
        b.Add((_secondaryA, "/user/hand/left/input/y/click"));
        b.Add((_menuA, "/user/hand/left/input/menu/click"));
        b.Add((_primaryA, "/user/hand/right/input/a/click"));
        b.Add((_secondaryA, "/user/hand/right/input/b/click"));
        // the thumb rest too (a thumb resting there folds over the fist); a runtime that doesn't know it gets the rest
        var withRest = new List<(ulong, string)>(b) { (_thumbA, "/user/hand/left/input/thumbrest/touch"), (_thumbA, "/user/hand/right/input/thumbrest/touch") };
        if (Suggest("/interaction_profiles/oculus/touch_controller", withRest, quiet: true) < 0)
            Suggest("/interaction_profiles/oculus/touch_controller", b);
        else Plugin.L.LogInfo("Controllers: Touch thumb rests bound");
    }

    private static void SuggestIndex()
    {
        var b = new List<(ulong, string)>();
        foreach (var h in new[] { "left", "right" })
        {
            var p = $"/user/hand/{h}/";
            Common(b, p);
            b.Add((_stickA, p + "input/thumbstick"));
            b.Add((_stickClickA, p + "input/thumbstick/click"));
            b.Add((_primaryA, p + "input/a/click"));
            b.Add((_secondaryA, p + "input/b/click"));
            b.Add((_thumbA, p + "input/thumbstick/touch"));
            b.Add((_thumbA, p + "input/a/touch"));
            b.Add((_thumbA, p + "input/b/touch"));
        }

        Suggest("/interaction_profiles/valve/index_controller", b);
    }

    /// <summary>Vive wands: the trackpad is the stick (its click A/X), the right menu button B, the left one pauses.</summary>
    private static void SuggestVive()
    {
        var b = new List<(ulong, string)>();
        foreach (var h in new[] { "left", "right" })
        {
            var p = $"/user/hand/{h}/";
            Common(b, p, false);
            b.Add((_stickA, p + "input/trackpad"));
            b.Add((_primaryA, p + "input/trackpad/click"));
        }

        b.Add((_menuA, "/user/hand/left/input/menu/click"));
        b.Add((_secondaryA, "/user/hand/right/input/menu/click"));
        Suggest("/interaction_profiles/htc/vive_controller", b);
    }

    /// <summary>Windows MR: the thumbstick is the stick, the touchpad click A/X, the right menu button B, the left pauses.</summary>
    private static void SuggestWindowsMr()
    {
        var b = new List<(ulong, string)>();
        foreach (var h in new[] { "left", "right" })
        {
            var p = $"/user/hand/{h}/";
            Common(b, p, false);
            b.Add((_stickA, p + "input/thumbstick"));
            b.Add((_stickClickA, p + "input/thumbstick/click"));
            b.Add((_primaryA, p + "input/trackpad/click"));
        }

        b.Add((_menuA, "/user/hand/left/input/menu/click"));
        b.Add((_secondaryA, "/user/hand/right/input/menu/click"));
        Suggest("/interaction_profiles/microsoft/motion_controller", b);
    }

    private static void SuggestSimple()
    {
        var b = new List<(ulong, string)>();
        foreach (var h in new[] { "left", "right" })
        {
            var p = $"/user/hand/{h}/";
            b.Add((_gripPose, p + "input/grip/pose"));
            b.Add((_aimPose, p + "input/aim/pose"));
            b.Add((_primaryA, p + "input/select/click"));
            b.Add((_menuA, p + "input/menu/click"));
            b.Add((_hapticA, p + "output/haptic"));
        }

        Suggest("/interaction_profiles/khr/simple_controller", b);
    }

    private static int Suggest(string profile, List<(ulong action, string path)> bindings, bool quiet = false)
    {
        var arr = stackalloc SuggestedBinding[bindings.Count];
        for (var i = 0; i < bindings.Count; i++) arr[i] = new SuggestedBinding { action = bindings[i].action, binding = Path(bindings[i].path) };
        var info = new ProfileSuggestedBinding { type = TypeProfileSuggested, interactionProfile = Path(profile), countSuggestedBindings = (uint)bindings.Count, suggestedBindings = arr };
        var r = _suggest(_instance, &info);
        return quiet ? r : Check(r, "suggest " + profile);
    }

    private static void Attach()
    {
        _session = XrManager.Session;
        if (_session == 0 || XrManager.AppSpace == 0) return;
        var set = _set;
        var info = new AttachInfo { type = TypeAttach, countActionSets = 1, actionSets = &set };
        var r = _attach(_session, &info);
        if (r < 0)
        {
            Plugin.L.LogError($"Controllers: couldn't attach to the OpenXR session (XrResult {r})");
            _failed = true;
            return;
        }

        _gripSpaceL = Space(_gripPose, _left);
        _gripSpaceR = Space(_gripPose, _right);
        _aimSpaceL = Space(_aimPose, _left);
        _aimSpaceR = Space(_aimPose, _right);
        _attached = true;
        Plugin.L.LogInfo("Controllers: actions attached to the session");
    }

    private static ulong Space(ulong action, ulong hand)
    {
        var info = new ActionSpaceCreateInfo { type = TypeActionSpaceCreate, action = action, subactionPath = hand, poseInActionSpace = Posef.Identity };
        ulong space;
        Check(_createActionSpace(_session, &info, &space), "xrCreateActionSpace");
        return space;
    }

    private static void Read(Hand h, ulong hand)
    {
        h.Stick = Vec2(_stickA, hand, out var stickActive);
        h.Trigger = Float1(_triggerA, hand, out var triggerActive);
        h.Grip = Float1(_gripA, hand, out _);
        h.StickClick = Bool(_stickClickA, hand);
        h.Primary = Bool(_primaryA, hand);
        h.Secondary = Bool(_secondaryA, hand);
        h.Menu = Bool(_menuA, hand);
        h.ThumbTouch = Bool(_thumbA, hand);
        var get = new GetInfo { type = TypeGetInfo, action = _gripPose, subactionPath = hand };
        var pose = new StatePose { type = TypeStatePose };
        _getPose(_session, &get, &pose);
        h.Active = pose.isActive != 0 || stickActive || triggerActive;
    }

    private static void Locate(Hand h, ulong grip, ulong aim, ulong space, long time)
    {
        var loc = new SpaceLocation { type = TypeSpaceLocation };
        h.PoseValid = _locateSpace(grip, space, time, &loc) == 0 && (loc.locationFlags & 0x3) == 0x3;
        if (h.PoseValid)
        {
            h.GripPosition = loc.pose.UnityPosition;
            h.GripRotation = loc.pose.UnityRotation;
        }

        loc = new SpaceLocation { type = TypeSpaceLocation };
        if (_locateSpace(aim, space, time, &loc) == 0 && (loc.locationFlags & 0x3) == 0x3)
        {
            h.AimPosition = loc.pose.UnityPosition;
            h.AimRotation = loc.pose.UnityRotation;
        }
        else if (h.PoseValid)
        {
            h.AimPosition = h.GripPosition;
            h.AimRotation = h.GripRotation * Quaternion.Euler(VrHands.AimPitch, 0f, 0f);
        }
    }

    private static bool Bool(ulong action, ulong hand)
    {
        var get = new GetInfo { type = TypeGetInfo, action = action, subactionPath = hand };
        var st = new StateBool { type = TypeStateBool };
        return _getBool(_session, &get, &st) == 0 && st.isActive != 0 && st.currentState != 0;
    }

    private static float Float1(ulong action, ulong hand, out bool active)
    {
        var get = new GetInfo { type = TypeGetInfo, action = action, subactionPath = hand };
        var st = new StateFloat { type = TypeStateFloat };
        active = _getFloat(_session, &get, &st) == 0 && st.isActive != 0;
        return active ? st.currentState : 0f;
    }

    private static Vector2 Vec2(ulong action, ulong hand, out bool active)
    {
        var get = new GetInfo { type = TypeGetInfo, action = action, subactionPath = hand };
        var st = new StateVec2 { type = TypeStateVec2 };
        active = _getVec2(_session, &get, &st) == 0 && st.isActive != 0;
        return active ? new Vector2(st.x, st.y) : Vector2.zero;
    }

    private static ulong Path(string path)
    {
        var bytes = Encoding.ASCII.GetBytes(path + "\0");
        ulong result;
        fixed (byte* p = bytes) Check(_stringToPath(_instance, p, &result), "xrStringToPath " + path);
        return result;
    }

    private static string PathString(ulong path)
    {
        if (path == 0) return "";
        var buffer = stackalloc byte[256];
        uint count;
        return _pathToString(_instance, path, 256, &count, buffer) == 0 ? Marshal.PtrToStringAnsi((IntPtr)buffer) : "?";
    }

    private static void CopyName(byte* dest, int capacity, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var n = Math.Min(bytes.Length, capacity - 1);
        for (var i = 0; i < n; i++) dest[i] = bytes[i];
        dest[n] = 0;
    }

    private static int Check(int result, string what)
    {
        if (result < 0) Plugin.L.LogWarning($"Controllers: {what} failed (XrResult {result})");
        return result;
    }

    public static string Describe() =>
        $"CONTROLLERS ready={_attached} failed={_failed} profile='{Profile}' L active={Left.Active} pose={Left.PoseValid} {Left.GripPosition} R active={Right.Active} pose={Right.PoseValid} {Right.GripPosition} trig {Left.Trigger:F2}/{Right.Trigger:F2} stick {Left.Stick}/{Right.Stick} time {OpenXRHook.PredictedDisplayTime} space {XrManager.AppSpace}";
}
