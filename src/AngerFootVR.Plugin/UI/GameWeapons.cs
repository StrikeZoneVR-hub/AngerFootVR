using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AngerFootVR.UI;

/// <summary>
/// The game's own weapons for the movie house's rooms (the rage room's rack, the gun range): their prefabs, loaded
/// with the game (the random-weapon shoe lists them all, so they're there from the title), copied as pictures only
/// (made inside a switched-off holder, every script taken off before anything wakes), with where each is held: a
/// gun's muzzle and barrel from its own muzzle point, a melee weapon's blade end from its swing trail.
/// </summary>
internal static class GameWeapons
{
    private static readonly Dictionary<string, global::Weapon> ByName = new Dictionary<string, global::Weapon>();
    private static readonly FieldInfo Muzzle = AccessTools.Field(typeof(ProjectileWeapon), "_muzzlePosition");
    private static readonly FieldInfo Trail = AccessTools.Field(typeof(MeleeWeapon), "_trailSystem");
    private static Transform _holder;

    private static void Scan()
    {
        foreach (var w in Resources.FindObjectsOfTypeAll<global::Weapon>())
        {
            if (w == null || w.gameObject.scene.IsValid() || w.HoldableObjectType == null) continue;
            var key = w.HoldableObjectType.name;
            if (w.name != key && w.name == "Knife") key = "Knife"; // (the knife is a bat-kind of weapon with its own model)
            if (!ByName.ContainsKey(key)) ByName[key] = w;
        }
    }

    /// <summary>A weapon's prefab by its kind's name (Glock, Shotgun, Bat, Machete, ...).</summary>
    public static global::Weapon Find(string name)
    {
        if (!ByName.ContainsKey(name)) Scan();
        return ByName.TryGetValue(name, out var w) ? w : null;
    }

    /// <summary>Every kind of weapon the game has, guns first.</summary>
    public static List<global::Weapon> All()
    {
        Scan();
        return ByName.Values.OrderBy(w => w is MeleeWeapon ? 1 : 0).ThenBy(w => w.name).ToList();
    }

    /// <summary>A picture-only copy of a weapon (on the movie house's layer, under a body on the fun world's), and its
    /// length's axis (from the handle to the business end), the handle's point and the length, in its own frame.</summary>
    public static GameObject Model(global::Weapon prefab, Transform parent, out Vector3 axis, out Vector3 handle, out float length)
    {
        axis = Vector3.forward;
        handle = Vector3.zero;
        length = 0.5f;
        var copy = Copy(prefab, out var business, out _);
        if (copy == null) return null;
        var root = Wrap(copy, parent, out var bounds);
        length = 0.5f;
        // its long axis, pointing from the handle to the blade (or the muzzle)
        var size = bounds.size;
        var a = size.x >= size.y && size.x >= size.z ? Vector3.right : size.y >= size.z ? Vector3.up : Vector3.forward;
        var ext = Vector3.Dot(bounds.extents, a);
        length = ext * 2f;
        if (business.HasValue && Vector3.Dot(business.Value - bounds.center, a) < 0f) a = -a;
        axis = a;
        handle = bounds.center - a * (ext * 0.78f);
        return root;
    }

    /// <summary>A gun's copy, with its muzzle (point and the way it shoots) in its own frame.</summary>
    public static GameObject GunModel(global::Weapon prefab, Transform parent, out Vector3 muzzle, out Vector3 barrel, out Bounds bounds)
    {
        muzzle = Vector3.zero;
        barrel = Vector3.forward;
        bounds = default;
        var copy = Copy(prefab, out var business, out var dir);
        if (copy == null) return null;
        var root = Wrap(copy, parent, out bounds);
        barrel = dir ?? Vector3.forward;
        muzzle = business ?? bounds.center + Vector3.forward * bounds.extents.z;
        return root;
    }

    private static GameObject Copy(global::Weapon prefab, out Vector3? business, out Vector3? businessDir)
    {
        business = null;
        businessDir = null;
        if (prefab == null) return null;
        if (_holder == null)
        {
            var h = new GameObject("AngerFootVR weapon copies");
            h.SetActive(false);
            Object.DontDestroyOnLoad(h);
            _holder = h.transform;
        }

        // (made inside a switched-off holder: nothing of it wakes up before its scripts are gone)
        var copy = Object.Instantiate(prefab.gameObject, _holder);
        copy.name = "AngerFootVR " + prefab.name;
        Transform point = null;
        if (copy.GetComponent<ProjectileWeapon>() is { } pw && Muzzle != null) point = Muzzle.GetValue(pw) as Transform;
        if (point == null && copy.GetComponent<MeleeWeapon>() is { } mw && Trail != null && Trail.GetValue(mw) is ParticleSystem ps) point = ps.transform;
        if (point != null)
        {
            business = copy.transform.InverseTransformPoint(point.position);
            businessDir = copy.transform.InverseTransformDirection(point.forward);
        }

        for (var pass = 0; pass < 3; pass++)
            foreach (var c in copy.GetComponentsInChildren<Component>(true).Reverse())
            {
                if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer || c is SkinnedMeshRenderer) continue;
                try
                {
                    Object.DestroyImmediate(c);
                }
                catch
                {
                    // (one another needs: the next pass)
                }
            }

        // (the pickup's glow and its trail's bits: only its own model's pictures stay)
        foreach (var r in copy.GetComponentsInChildren<Renderer>(true))
            if (!r.gameObject.activeSelf || r.name.IndexOf("highlight", System.StringComparison.OrdinalIgnoreCase) >= 0 || r.name.IndexOf("outline", System.StringComparison.OrdinalIgnoreCase) >= 0)
                Object.DestroyImmediate(r.gameObject);
        foreach (var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = VrUi.Layer;
        return copy;
    }

    /// <summary>The copy under a body on the fun world's layer: a box round its pictures.</summary>
    private static GameObject Wrap(GameObject copy, Transform parent, out Bounds bounds)
    {
        var root = new GameObject(copy.name) { layer = FunWorld.PhysLayer };
        root.transform.SetParent(parent, false);
        copy.transform.SetParent(root.transform, false);
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        copy.transform.localScale = Vector3.one;
        bounds = new Bounds();
        var first = true;
        foreach (var r in copy.GetComponentsInChildren<Renderer>(true))
        {
            Mesh mesh = r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) continue;
            var m = root.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
            var b = mesh.bounds;
            for (var i = 0; i < 8; i++)
            {
                var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = m.MultiplyPoint3x4(corner);
                if (first)
                {
                    bounds = new Bounds(p, Vector3.zero);
                    first = false;
                }
                else bounds.Encapsulate(p);
            }

            if (r is SkinnedMeshRenderer s) s.updateWhenOffscreen = true;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        if (first) bounds = new Bounds(Vector3.zero, Vector3.one * 0.2f);
        var box = root.AddComponent<BoxCollider>();
        box.center = bounds.center;
        box.size = Vector3.Max(bounds.size, Vector3.one * 0.03f);
        copy.SetActive(true);
        return root;
    }

    // ---------------------------------------------------------------------------------- what a gun does

    /// <summary>What a gun is like (from its prefab): its shot sound, shots per pull, time between shots, spread,
    /// whether it fires while the trigger's held, what it fires.</summary>
    public sealed class GunInfo
    {
        public string Name;
        public string FireBank;
        public int Projectiles = 1;
        public float Cooldown = 0.2f;
        public float Spread = 1f;
        public bool Auto;
        public string Fires = "bullet"; // bullet, bolt, grenade, flame, hook
        public float Kick = 1f;
    }

    private static readonly FieldInfo FireSound = AccessTools.Field(typeof(ProjectileWeapon), "_playerFireSound");
    private static readonly FieldInfo Count = AccessTools.Field(typeof(ProjectileWeapon), "_projectileCount");
    private static readonly FieldInfo Cool = AccessTools.Field(typeof(ProjectileWeapon), "_fireCooldown");
    private static readonly FieldInfo Cone = AccessTools.Field(typeof(ProjectileWeapon), "_accuracyConeAngle");
    private static readonly FieldInfo Shot = AccessTools.Field(typeof(ProjectileWeapon), "_projectilePrefab");

    public static GunInfo Info(global::Weapon prefab)
    {
        var info = new GunInfo { Name = prefab.HoldableObjectType != null ? prefab.HoldableObjectType.name : prefab.name };
        if (prefab is ProjectileWeapon pw)
        {
            if (FireSound?.GetValue(pw) is Object bank && bank != null) info.FireBank = bank.name;
            if (Count?.GetValue(pw) is int n) info.Projectiles = Mathf.Clamp(n, 1, 16);
            if (Cool?.GetValue(pw) is float c) info.Cooldown = Mathf.Clamp(c, 0.02f, 2f);
            if (Cone?.GetValue(pw) is Framework.FloatRange cone) info.Spread = Mathf.Clamp(cone.Max, 0f, 20f);
            var trig = prefab.TriggerMechanism != null ? prefab.TriggerMechanism.GetType().Name : "";
            info.Auto = trig.IndexOf("Auto", System.StringComparison.OrdinalIgnoreCase) >= 0 || trig.IndexOf("Minigun", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        trig.IndexOf("Hold", System.StringComparison.OrdinalIgnoreCase) >= 0;
            var proj = Shot?.GetValue(pw) as GameObject;
            var pn = proj != null ? proj.name.ToLowerInvariant() : "";
            var name = info.Name.ToLowerInvariant();
            if (name.Contains("flame")) info.Fires = "flame";
            else if (name.Contains("grenade") || pn.Contains("grenade")) info.Fires = "grenade";
            else if (name.Contains("crossbow") || pn.Contains("bolt") || pn.Contains("arrow")) info.Fires = "bolt";
        }

        if (prefab is HookWeapon) info.Fires = "hook";
        var nm = info.Name.ToLowerInvariant();
        info.Kick = nm.Contains("shotgun") || nm.Contains("sniper") ? 2.4f : nm.Contains("minigun") || nm.Contains("rifle") ? 1.3f : nm.Contains("grenade") ? 1.8f : 1f;
        if (nm.Contains("minigun")) info.Auto = true;
        if (nm.Contains("uzzi") || nm.Contains("assault") || nm.Contains("flame")) info.Auto = true;
        return info;
    }

}
