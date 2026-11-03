using System.Collections.Generic;
using AngerFootVR.Core;
using Framework;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.Input;

/// <summary>
/// SWAT BACK (VR settings): an enemy's grenade (or anything the game lets you kick back at them) that comes close, hit
/// it with your hand, fast, and back it goes, exactly as a kick sends it (the game's own kick back: it flies at them,
/// it counts for the kicked-grenade goals). Kicking them still works as ever.
/// </summary>
internal static class Swat
{
    private static readonly Collider[] Hits = new Collider[24];
    private static readonly Dictionary<KickableProjectile, float> Done = new Dictionary<KickableProjectile, float>();
    private static float _nextClean;

    /// <summary>Dev: an enemy grenade (any loaded) hanging in the air in front of you, to swat.</summary>
    public static string DevGrenade()
    {
        if (!SingletonBehaviour<Player>.HasInstance) return "no player";
        KickableProjectile prefab = null;
        foreach (var k in Resources.FindObjectsOfTypeAll<KickableProjectile>())
            if (k != null && k.GetComponent<Grenade>() != null)
            {
                prefab = k;
                break;
            }

        if (prefab == null)
            foreach (var k in Resources.FindObjectsOfTypeAll<KickableProjectile>())
                if (k != null)
                {
                    prefab = k;
                    break;
                }

        if (prefab == null) return "no kickable projectile loaded";
        var fwd = Vector3.ProjectOnPlane(VrRig.HeadRotation * Vector3.forward, Vector3.up).normalized;
        var g = Object.Instantiate(prefab.gameObject, VrRig.HeadPosition + fwd * 0.45f + Vector3.up * -0.25f + Vector3.Cross(Vector3.up, fwd) * 0.2f, Quaternion.identity);
        g.SetActive(true);
        var rb = g.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.useGravity = false;
            rb.velocity = Vector3.zero;
        }

        return $"grenade '{prefab.name}' at {g.transform.position:F2} (owner none, hanging)";
    }

    public static void Update()
    {
        if (!XrManager.IsRunning || CinemaRig.Active || MapRig.Active || !SingletonBehaviour<Player>.HasInstance) return;
        if (VrSettings.HandSwat != null && !VrSettings.HandSwat.Value) return;
        var now = Time.unscaledTime;
        foreach (var hand in new[] { XRNode.LeftHand, XRNode.RightHand })
        {
            var pose = VrHands.Get(hand);
            if (!pose.Tracked || pose.Velocity.magnitude < 2.2f) continue;
            var n = Physics.OverlapSphereNonAlloc(pose.Position, 0.3f, Hits, ~0, QueryTriggerInteraction.Collide);
            for (var i = 0; i < n; i++)
            {
                var kp = Hits[i] != null ? Hits[i].GetComponentInParent<KickableProjectile>() : null;
                if (kp == null || kp.HasBeenKicked || (Done.TryGetValue(kp, out var at) && now - at < 0.5f)) continue;
                if (kp.GetComponent<IDamageSource>() is IDamageSource src && src.GetDamageOwner() is Player) continue; // (your own: they're on their way)
                Done[kp] = now;
                var dir = Vector3.Lerp(pose.Velocity.normalized, VrRig.HeadRotation * Vector3.forward, 0.5f).normalized;
                try
                {
                    kp.OnKicked(PlayerMeleeAttack.NormalKick, kp.transform.position, dir, Hits[i]);
                }
                catch (System.Exception e)
                {
                    Plugin.L.LogWarning("Swat: " + e.Message);
                    continue;
                }

                var body = kp.GetComponent<Rigidbody>();
                if (body != null) body.useGravity = true;
                VrControls.Rumble(hand, 0.9f, 0.12f);
                Plugin.L.LogInfo($"Swat: {kp.name} hit back by the {(hand == XRNode.LeftHand ? "left" : "right")} hand ({pose.Velocity.magnitude:F1} m/s)");
            }
        }

        if (now > _nextClean)
        {
            _nextClean = now + 5f;
            var gone = new List<KickableProjectile>();
            foreach (var kv in Done)
                if (kv.Key == null || now - kv.Value > 5f)
                    gone.Add(kv.Key);
            foreach (var k in gone) Done.Remove(k);
        }
    }
}
