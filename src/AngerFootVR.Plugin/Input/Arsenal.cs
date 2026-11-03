using System.Linq;
using System.Reflection;
using AngerFootVR.Core;
using Framework;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.Input;

/// <summary>
/// The guns, the VR way (on top of GunHold, which puts the game's own gun in your hand):
/// - two-handed guns (rifles, the shotgun, the sniper, crossbow, launcher, flamethrower, minigun): bring your other
///   hand to the front of the gun and squeeze its grip, and that hand holds the gun there and steers the barrel with
///   you (TWO-HAND AIM);
/// - dual pistols: a pistol in each hand, each trigger fires its own (both: they take turns, as in the game);
/// - an ammo count on the back of every gun (the HUD's crosshair and its count are gone in VR), EMPTY when it's time
///   to throw it;
/// - the sniper's scope: put your eye to it and it magnifies (the game's sniper has no zoom of its own);
/// - the plunger's hook flies from the plunger in your hand, where it points (the game fires it from your view);
/// - the controllers buzz: each shot (harder for the big guns, in both hands when held with two), a dry click when
///   empty, a kick, a kicked door, a kicked enemy, a throw, a gun caught, a shoe power charging.
/// </summary>
[HarmonyPatch]
internal static class Arsenal
{
    private static XRNode GunHand => GunHold.Hand;
    private static XRNode OffHand => GunHand == XRNode.RightHand ? XRNode.LeftHand : XRNode.RightHand;
    private static bool InPlay => XrManager.IsRunning && !CinemaRig.Active && SingletonBehaviour<Player>.HasInstance;

    public static bool IsTwoHanded(HoldableObjectType t) =>
        t == HoldableObjectType.AssaultRifle || t == HoldableObjectType.Shotgun || t == HoldableObjectType.SniperRifle ||
        t == HoldableObjectType.Crossbow || t == HoldableObjectType.GrenadeLauncher || t == HoldableObjectType.FlameThrower ||
        t == HoldableObjectType.Minigun || t == HoldableObjectType.CatBossMinigun;

    private static bool IsHeavy(HoldableObjectType t) =>
        t == HoldableObjectType.Shotgun || t == HoldableObjectType.SniperRifle || t == HoldableObjectType.GrenadeLauncher ||
        t == HoldableObjectType.Crossbow;

    // ------------------------------------------------------------------ two hands

    /// <summary>Your other hand holds the gun's front this frame.</summary>
    public static bool TwoHands { get; private set; }

    private static Vector3 _foreRest;

    /// <summary>From GunHold, before the gun is placed: with your other hand on its front, the barrel swings onto that
    /// hand (round your gun hand's palm).</summary>
    public static Quaternion TwoHand(Transform rig, Quaternion rigRot, Vector3 anchor, Vector3 anchorInRig, Vector3 muzzleInRig, HoldableObjectType type)
    {
        var was = TwoHands;
        TwoHands = false;
        VrHands.Hold(OffHand == XRNode.LeftHand ? 0 : 1, null, null);
        if (!IsTwoHanded(type) || muzzleInRig == Vector3.zero || (VrSettings.TwoHandAim != null && !VrSettings.TwoHandAim.Value)) return rigRot;
        var off = VrHands.GetRaw(OffHand);
        if (!off.Tracked || !VrControls.Held(OffHand, VrButton.Grip)) return rigRot;

        // the front grip: a little over half way from the grip to the muzzle, under the barrel
        _foreRest = anchorInRig + (muzzleInRig - anchorInRig) * 0.55f + Vector3.down * 0.04f;
        var scale = rig.lossyScale;
        var have = rigRot * Vector3.Scale(_foreRest - anchorInRig, scale);
        var fore = anchor + have;
        if ((off.Position - fore).magnitude > (was ? 0.3f : 0.17f)) return rigRot;

        var want = off.Position - anchor;
        if (want.magnitude < 0.12f) return rigRot;
        rigRot = Quaternion.FromToRotation(have, want) * rigRot;
        TwoHands = true;
        // your other hand's on the gun now (the body's hand goes to it)
        VrHands.Hold(OffHand == XRNode.LeftHand ? 0 : 1, anchor + rigRot * Vector3.Scale(_foreRest - anchorInRig, scale), null);
        if (!was) VrControls.Rumble(OffHand, 0.35f, 0.04f);
        return rigRot;
    }

    // ------------------------------------------------------------------ dual pistols

    private static readonly FieldInfo FireLeftNext = AccessTools.Field(typeof(DualPistolsWeapon), "_fireLeftHandNext");
    private static readonly FieldInfo LeftModel = AccessTools.Field(typeof(HandRigDualPistolObject), "_leftHandModel");
    private static readonly FieldInfo LeftMuzzle = AccessTools.Field(typeof(HandRigDualPistolObject), "_leftHandMuzzlePosition");
    private static readonly FieldInfo Chains = AccessTools.Field(typeof(HandRigDualPistolObject), "_chainModels");

    /// <summary>Dual pistols in both hands this frame.</summary>
    public static bool Dual { get; private set; }

    private static Transform _leftGun, _leftMuzzle;
    private static Quaternion _leftGunInRig;
    private static Vector3 _leftGunPosInRig, _leftGripInRig;

    private static void PlaceLeftPistol(HandRig handRig, Transform rig)
    {
        Dual = false;
        if (GunHand != XRNode.RightHand) return; // (a left gun hand: the pair stays as the game holds it)
        var obj = handRig.GetComponentsInChildren<HandRigDualPistolObject>(false).FirstOrDefault();
        if (obj == null || LeftModel?.GetValue(obj) is not GameObject model || model == null) return;
        var left = VrHands.Get(XRNode.LeftHand);
        if (!left.Tracked) return;
        if (_leftGun != model.transform)
        {
            _leftGun = model.transform;
            _leftMuzzle = LeftMuzzle?.GetValue(obj) as Transform;
            // where it sits in the rig as the game holds it (its grip: about under its middle, a little back)
            _leftGunInRig = Quaternion.Inverse(rig.rotation) * _leftGun.rotation;
            _leftGunPosInRig = rig.InverseTransformPoint(_leftGun.position);
            var b = model.GetComponentsInChildren<Renderer>().Select(r => r.bounds).DefaultIfEmpty(new Bounds(_leftGun.position, Vector3.zero)).Aggregate((a, c) =>
            {
                a.Encapsulate(c);
                return a;
            });
            _leftGripInRig = rig.InverseTransformPoint(b.center - rig.forward * (b.extents.z * 0.35f) - rig.up * (b.extents.y * 0.4f));
            if (Chains?.GetValue(obj) is GameObject[] chain)
                foreach (var c in chain)
                    if (c != null)
                        c.SetActive(false); // (the chain between them can't stretch from hand to hand)
        }

        // the left pistol: on your left aim, its grip in your left palm (the same offsets the right one has)
        var s = -1f;
        var local = Quaternion.LookRotation(Vector3.down, new Vector3(s, 0, 0));
        var palm = left.Position + left.Rotation * (new Vector3(0.032f * s, 0, 0) - local * new Vector3(0, 0, 0.05f)) + left.Rotation * local * new Vector3(0, 0, 0.045f);
        var rot = left.Aim * _leftGunInRig;
        var scale = rig.lossyScale;
        _leftGun.SetPositionAndRotation(palm + left.Aim * Vector3.Scale(_leftGunPosInRig - _leftGripInRig, scale), rot);
        Dual = true;
    }

    /// <summary>Which pistol fires: the hand whose trigger is pulled (both: they take turns).</summary>
    public static void ChooseDualHand(ProjectileWeapon weapon)
    {
        if (!Dual || weapon is not DualPistolsWeapon || FireLeftNext == null) return;
        var l = VrControls.Held(XRNode.LeftHand, VrButton.Trigger);
        var r = VrControls.Held(XRNode.RightHand, VrButton.Trigger);
        if (l && !r) FireLeftNext.SetValue(weapon, true);
        else if (r && !l) FireLeftNext.SetValue(weapon, false);
    }

    /// <summary>A dual pistol shot: from the pistol that fires, along its hand's aim.</summary>
    public static bool DualShot(ProjectileWeapon weapon, out Vector3 position, out Vector3 direction)
    {
        position = direction = default;
        if (!Dual || weapon is not DualPistolsWeapon d || !d.FireLeftHandNext) return false;
        var left = VrHands.Get(XRNode.LeftHand);
        direction = left.Aim * Vector3.forward;
        position = _leftMuzzle != null ? _leftMuzzle.position : left.Position + direction * 0.2f;
        return true;
    }

    // ------------------------------------------------------------------ the ammo count

    private static TextMeshPro _ammo;
    private static int _shown = -2;

    private static void AmmoCount(Transform rig, Vector3 anchorInRig, Weapon weapon, bool gun)
    {
        if (_ammo == null)
        {
            var go = new GameObject("AngerFootVR Ammo");
            Object.DontDestroyOnLoad(go);
            _ammo = go.AddComponent<TextMeshPro>();
            var font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f.name == "ANGERFONT-HEADER SDF")
                       ?? Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault();
            if (font != null) _ammo.font = font;
            _ammo.alignment = TextAlignmentOptions.Center;
            _ammo.enableWordWrapping = false;
            _ammo.rectTransform.sizeDelta = new Vector2(0.12f, 0.04f);
            _ammo.fontSize = 0.22f;
            _ammo.outlineWidth = 0.25f;
            _ammo.outlineColor = new Color32(0, 0, 0, 200);
        }

        var store = weapon != null ? weapon.AmmoStorage : null;
        var show = gun && store != null && weapon.RequiresAmmo && (VrSettings.AmmoCounter == null || VrSettings.AmmoCounter.Value);
        _ammo.gameObject.SetActive(show);
        if (!show) return;
        // on the back of the gun, over your hand, facing you (the text reads from behind)
        var layer = rig.gameObject.layer;
        foreach (var r in rig.GetComponentsInChildren<Renderer>(false))
        {
            layer = r.gameObject.layer;
            break;
        }

        _ammo.gameObject.layer = layer;
        var t = _ammo.transform;
        t.position = rig.TransformPoint(anchorInRig) + rig.rotation * new Vector3(0f, 0.085f, 0.0f);
        t.rotation = rig.rotation;
        t.localScale = Vector3.one;
        var n = store.CurrentAmmo;
        var empty = n <= 0;
        if (n != _shown || empty)
        {
            _shown = n;
            if (empty) _ammo.text = Time.unscaledTime % 0.6f < 0.4f ? "EMPTY" : "";
            else _ammo.text = n.ToString();
            var low = store.StartingAmmo > 0 && n <= Mathf.Max(1, store.StartingAmmo / 5);
            _ammo.color = empty ? new Color(1f, 0.25f, 0.35f) : low ? new Color(1f, 0.45f, 0.2f) : new Color(1f, 0.85f, 0.2f);
        }
    }

    // ------------------------------------------------------------------ the sniper's scope

    private static Camera _scopeCam;
    private static RenderTexture _scopeTex;
    private static GameObject _scope;
    private static Material _lensMat;

    /// <summary>The sniper's scope (the game's rifle has none): a black tube on top of it, a round lens at its back with
    /// cross hairs; put your eye to it and it magnifies (only then: a scope is a second view of the world).</summary>
    private static void Scope(Transform rig, Vector3 anchorInRig, Vector3 muzzleInRig, HoldableObjectType type)
    {
        var want = type == HoldableObjectType.SniperRifle && muzzleInRig != Vector3.zero && (VrSettings.SniperScope == null || VrSettings.SniperScope.Value);
        if (!want)
        {
            if (_scope != null) _scope.SetActive(false);
            if (_scopeCam != null) _scopeCam.enabled = false;
            return;
        }

        if (_scope == null) BuildScope();
        // over the barrel, from about its middle back towards your hand (the rig may be scaled: offsets in metres)
        var s = rig.lossyScale;
        var muzzleW = rig.TransformPoint(muzzleInRig);
        var palmW = rig.TransformPoint(anchorInRig);
        var fwd = rig.forward;
        // (as on a real rifle: the eyepiece just behind and over your gun hand, the tube a little over the barrel)
        var barrelHeight = Vector3.Dot(muzzleW - palmW, rig.up);
        var centre = palmW + rig.up * Mathf.Max(barrelHeight + 0.05f, 0.1f) + fwd * 0.03f;
        _scope.SetActive(true);
        _scope.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(fwd, rig.up));
        foreach (var t in _scope.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = rig.gameObject.layer;

        var lens = _scope.transform.TransformPoint(new Vector3(0f, 0f, -0.095f));
        var eye = VrRig.HeadPosition;
        var looking = (eye - lens).magnitude < 0.22f && Vector3.Dot(VrRig.HeadRotation * Vector3.forward, fwd) > 0.9f;
        var main = SingletonBehaviour<PlayerCamera>.Instance.MainCamera;
        _scopeCam.enabled = looking;
        _lensMat.color = looking ? Color.white : new Color(0.05f, 0.07f, 0.1f);
        if (!looking) return;
        _scopeCam.cullingMask = main.cullingMask & ~(1 << rig.gameObject.layer) & ~(1 << UI.VrUi.Layer);
        _scopeCam.clearFlags = CameraClearFlags.SolidColor; // (the game's camera only clears depth: its sky is drawn by another)
        _scopeCam.backgroundColor = RenderSettings.fogColor;
        _scopeCam.farClipPlane = main.farClipPlane;
        _scopeCam.transform.SetPositionAndRotation(_scope.transform.TransformPoint(new Vector3(0f, 0f, 0.1f)), _scope.transform.rotation);
    }

    private static void BuildScope()
    {
        _scope = new GameObject("AngerFootVR Scope");
        Object.DontDestroyOnLoad(_scope);
        var black = new Material(Shader.Find("Standard")) { color = new Color(0.04f, 0.04f, 0.05f) };
        black.SetFloat("_Glossiness", 0.7f);
        // the tube (a cylinder lies along y: turned onto z), a ring at each end
        void Part(PrimitiveType p, Vector3 pos, Vector3 euler, Vector3 scale)
        {
            var g = GameObject.CreatePrimitive(p);
            Object.Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(_scope.transform, false);
            g.transform.localPosition = pos;
            g.transform.localEulerAngles = euler;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = black;
        }

        Part(PrimitiveType.Cylinder, Vector3.zero, new Vector3(90f, 0f, 0f), new Vector3(0.032f, 0.09f, 0.032f));
        Part(PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.085f), new Vector3(90f, 0f, 0f), new Vector3(0.042f, 0.012f, 0.042f));
        Part(PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.07f), new Vector3(90f, 0f, 0f), new Vector3(0.038f, 0.008f, 0.038f)); // (short of the lens)
        Part(PrimitiveType.Cube, new Vector3(0f, -0.035f, 0.03f), Vector3.zero, new Vector3(0.012f, 0.04f, 0.012f)); // (the mounts)
        Part(PrimitiveType.Cube, new Vector3(0f, -0.035f, -0.04f), Vector3.zero, new Vector3(0.012f, 0.04f, 0.012f));

        // the lens: a disc showing what the scope sees, cross hairs over it
        _scopeTex = new RenderTexture(512, 512, 24) { name = "AngerFootVR Scope" };
        var disc = new Mesh { name = "AngerFootVR Scope Lens" };
        const int n = 40;
        var v = new Vector3[n + 1];
        var uv = new Vector2[n + 1];
        var tris = new int[n * 3];
        v[0] = Vector3.zero;
        uv[0] = new Vector2(0.5f, 0.5f);
        for (var i = 0; i < n; i++)
        {
            var a = i * Mathf.PI * 2f / n;
            v[i + 1] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.0175f;
            uv[i + 1] = new Vector2(0.5f - 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a)); // (seen from behind: mirrored)
            tris[i * 3] = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = (i + 1) % n + 1;
        }

        disc.vertices = v;
        disc.uv = uv;
        disc.triangles = tris;
        var lens = new GameObject("Lens");
        lens.transform.SetParent(_scope.transform, false);
        lens.transform.localPosition = new Vector3(0f, 0f, -0.0915f);
        lens.transform.localRotation = Quaternion.identity;
        lens.AddComponent<MeshFilter>().sharedMesh = disc;
        _lensMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = _scopeTex }; // (a shader the game has)
        lens.AddComponent<MeshRenderer>().sharedMaterial = _lensMat;
        var hair = new Material(Shader.Find("Sprites/Default")) { color = Color.black };
        foreach (var size in new[] { new Vector3(0.034f, 0.0008f, 1f), new Vector3(0.0008f, 0.034f, 1f) })
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(_scope.transform, false);
            q.transform.localPosition = new Vector3(0f, 0f, -0.0925f);
            q.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            q.transform.localScale = size;
            q.GetComponent<Renderer>().sharedMaterial = hair;
        }

        var go = new GameObject("AngerFootVR Scope Camera");
        Object.DontDestroyOnLoad(go);
        _scopeCam = go.AddComponent<Camera>();
        _scopeCam.stereoTargetEye = StereoTargetEyeMask.None;
        _scopeCam.targetTexture = _scopeTex;
        _scopeCam.fieldOfView = 8f;
        _scopeCam.nearClipPlane = 0.2f;
        _scopeCam.enabled = false;
    }

    /// <summary>From GunHold, once the gun is placed: the left pistol, the count, the scope.</summary>
    public static void AfterPlace(HandRig handRig, Transform rig, Vector3 anchorInRig, Vector3 muzzleInRig, Weapon weapon, HoldableObjectType type, bool gun)
    {
        // (each on its own: one going wrong never takes the others, or the gun in your hand, with it)
        Guard("left pistol", () => PlaceLeftPistol(handRig, rig));
        Guard("ammo count", () => AmmoCount(rig, anchorInRig, weapon, gun));
        Guard("scope", () => Scope(rig, anchorInRig, muzzleInRig, type));
    }

    private static readonly System.Collections.Generic.HashSet<string> Failed = new System.Collections.Generic.HashSet<string>();

    private static void Guard(string what, System.Action act)
    {
        if (Failed.Contains(what)) return;
        try
        {
            act();
        }
        catch (System.Exception e)
        {
            Failed.Add(what);
            Plugin.L.LogError($"Arsenal: the {what} failed and is off for this session: {e}");
        }
    }

    /// <summary>Nothing in your hand: the count and the scope go, your hands are your own.</summary>
    public static void Nothing()
    {
        Dual = false;
        TwoHands = false;
        VrHands.LetGo();
        if (_ammo != null) _ammo.gameObject.SetActive(false);
        if (_scope != null) _scope.SetActive(false);
        if (_scopeCam != null) _scopeCam.enabled = false;
        _leftGun = null;
    }

    // ------------------------------------------------------------------ the plunger

    /// <summary>The plunger's hook leaves the plunger in your hand, the way it points.</summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(HookWeapon), "FireHook")]
    private static void HookFromTheHand(ref Ray ray, bool guideAim)
    {
        if (!guideAim || !InPlay) return;
        var pose = VrHands.GetRaw(GunHand);
        if (!pose.Tracked) return;
        GunHold.BarrelRay(pose, out var from, out var dir);
        ray = new Ray(from, dir);
    }

    // ------------------------------------------------------------------ the buzz

    private static void Both(float amount, float seconds)
    {
        VrControls.Rumble(XRNode.LeftHand, amount, seconds);
        VrControls.Rumble(XRNode.RightHand, amount, seconds);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(HandRig), nameof(HandRig.FireWeapon))]
    private static void Fired(ProjectileWeapon weapon)
    {
        if (!InPlay || weapon == null) return;
        var heavy = IsHeavy(weapon.HoldableObjectType);
        var hand = GunHand;
        if (Dual && weapon is DualPistolsWeapon d) hand = d.FireLeftHandNext ? XRNode.LeftHand : XRNode.RightHand;
        VrControls.Rumble(hand, heavy ? 0.95f : 0.6f, heavy ? 0.1f : 0.05f);
        if (TwoHands) VrControls.Rumble(OffHand, heavy ? 0.6f : 0.35f, heavy ? 0.08f : 0.04f);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(HandRig), nameof(HandRig.TryFireEmptyWeapon))]
    private static void Empty()
    {
        if (InPlay) VrControls.Rumble(GunHand, 0.25f, 0.025f);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Player), nameof(Player.OnKick))]
    private static void Kicked()
    {
        if (InPlay) Both(0.3f, 0.05f);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Door), nameof(Door.OnKicked))]
    private static void DoorKicked()
    {
        if (InPlay) Both(1f, 0.14f);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Enemy), nameof(Enemy.OnKicked))]
    private static void EnemyKicked()
    {
        if (InPlay) Both(0.75f, 0.09f);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Player), nameof(Player.ThrowWeapon))]
    private static void Threw()
    {
        if (InPlay) VrControls.Rumble(GunHand, 0.55f, 0.06f);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(HandRig), nameof(HandRig.PickUpWeapon))]
    private static void Caught()
    {
        if (InPlay) VrControls.Rumble(GunHand, 0.45f, 0.05f);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(FootRig), nameof(FootRig.HoldSpecialAttack))]
    private static void Charging()
    {
        if (InPlay) Both(0.12f, 0.03f);
    }

    // ------------------------------------------------------------------ dev

    /// <summary>Dev: where the scope's lens is from your eye (head-local metres), and whether it's magnifying.</summary>
    public static string ScopeEye()
    {
        if (_scope == null || !_scope.activeSelf) return "SCOPE none";
        var lens = _scope.transform.TransformPoint(new Vector3(0f, 0f, -0.095f));
        var d = Quaternion.Inverse(VrRig.HeadRotation) * (lens - VrRig.HeadPosition);
        var main = SingletonBehaviour<PlayerCamera>.HasInstance ? SingletonBehaviour<PlayerCamera>.Instance.MainCamera : null;
        return $"SCOPE main clear {(main != null ? main.clearFlags.ToString() + " mask " + main.cullingMask.ToString("X") + " depth " + main.depth + " rendering " + main.actualRenderingPath : "none")} scopecam {(_scopeCam != null ? _scopeCam.cullingMask.ToString("X") + " at " + _scopeCam.transform.position : "none")} lens from the eye {d.x:F3} {d.y:F3} {d.z:F3} looking={(_scopeCam != null && _scopeCam.enabled)} dist {(lens - VrRig.HeadPosition).magnitude:F3} dot {Vector3.Dot(VrRig.HeadRotation * Vector3.forward, _scope.transform.forward):F2} head {VrRig.HeadRotation.eulerAngles} scope {_scope.transform.eulerAngles}";
    }

    /// <summary>Dev: any gun the game has loaded (in the level or as a prefab) into your hand.</summary>
    public static string Give(string match)
    {
        if (!SingletonBehaviour<Player>.HasInstance) return "no player";
        var all = Resources.FindObjectsOfTypeAll<Weapon>().Where(w => w != null && (w is ProjectileWeapon || w is HookWeapon)).ToArray();
        var names = string.Join(", ", all.Select(w => w.name).Distinct().OrderBy(n => n).ToArray());
        var gun = all.FirstOrDefault(w => !string.IsNullOrEmpty(match) && w.name.IndexOf(match, System.StringComparison.OrdinalIgnoreCase) >= 0 && !w.gameObject.scene.IsValid())
                  ?? all.FirstOrDefault(w => !string.IsNullOrEmpty(match) && w.name.IndexOf(match, System.StringComparison.OrdinalIgnoreCase) >= 0);
        if (gun == null) return "WEAPONS loaded: " + names;
        SingletonBehaviour<Player>.Instance.SpawnPlayerWeapon(gun.gameObject);
        return $"gave {gun.name} ({gun.HoldableObjectType}) | loaded: {names}";
    }
}
