using System.Collections.Generic;
using System.Reflection;
using AngerFootVR.Core;
using Framework;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.Input;

/// <summary>
/// SWING FOR REAL: a melee weapon in your hand (a bat, a machete: the game gives the player no swing of its own with
/// them) hits what it's swung through, with the weapon's own damage, when you swing it fast.
/// </summary>
internal static class MotionMelee
{
    private static readonly FieldInfo DamageField = AccessTools.Field(typeof(MeleeWeapon), "_damage");
    private static readonly RaycastHit[] Hits = new RaycastHit[16];
    private static readonly Collider[] Near = new Collider[16];
    private static float _next;

    public static void Update()
    {
        if (!XrManager.IsRunning || CinemaRig.Active || MapRig.Active || !SingletonBehaviour<Player>.HasInstance) return;
        var player = SingletonBehaviour<Player>.Instance;
        if (!(player.Weapon is MeleeWeapon weapon) || Time.unscaledTime < _next) return;
        var pose = VrHands.Get(VrInputProvider.GunHand);
        if (!pose.Tracked || pose.Velocity.magnitude < 4.5f) return;
        _next = Time.unscaledTime + 0.4f;
        // the way the weapon's head came (the hand is where the swing got to): swept back along it, and what's round the
        // head now
        var dir = pose.Velocity.normalized;
        var head = pose.Position + pose.Aim * Vector3.forward * 0.5f;
        var n = Physics.SphereCastNonAlloc(head - dir * 0.7f, 0.3f, dir, Hits, 0.9f, LayerMasks.ThingsMeleeWeaponsCanHit, QueryTriggerInteraction.Collide);
        var m = Physics.OverlapSphereNonAlloc(head, 0.35f, Near, LayerMasks.ThingsMeleeWeaponsCanHit, QueryTriggerInteraction.Collide);
        if (Plugin.DevRun)
        {
            var all = Physics.OverlapSphere(head, 0.8f, ~0, QueryTriggerInteraction.Collide);
            var names = string.Join(", ", System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(System.Linq.Enumerable.Take(all, 12), c => $"{c.name}[{c.gameObject.layer}{(c.isTrigger ? " trig" : "")}]")));
            Plugin.L.LogInfo($"Motion melee (dev): swing through {head:F2} along {dir:F2}: {n} swept, {m} round the head; within 0.8 m: {names}");
        }
        if (n + m == 0) return;
        // (the weapon's own damage is made for an enemy swinging it at you, and the game's melee-weapon damage doesn't
        // hurt enemies at all: a swing of yours hits as a kick does, knock-back and all)
        var own = DamageField?.GetValue(weapon) is Damage d ? d.EnemyAmount : 0f;
        var damage = new Damage(Mathf.Max(own, 100f), DamageType.MeleeWeapon.DamagesEnemies ? DamageType.MeleeWeapon : DamageType.Kick);
        var list = new List<DamageInstance>();
        for (var i = 0; i < n + m; i++)
        {
            var c = i < n ? Hits[i].collider : Near[i - n];
            if (c == null || c.GetComponentInParent<Player>() != null) continue;
            var point = i < n && Hits[i].point != Vector3.zero ? Hits[i].point : c.bounds.ClosestPoint(head);
            list.Add(new DamageInstance(damage, point, dir, PlayerMeleeAttack.NormalKick, c)); // (a source the game knows: the player)
        }

        if (list.Count == 0) return;
        if (DamageManager.TryDealMaxDamage(list))
            foreach (var hitOn in list)
                if (hitOn.Collider != null && hitOn.Collider.GetComponentInParent<Enemy>() is Enemy e && e.Rig != null && e.Rig.GetWorldMaterial() is { SpawnsBlood: true })
                {
                    Body.BloodOnYou.Close(hitOn.Position);
                    break;
                }
        VrControls.Rumble(VrInputProvider.GunHand, 0.9f, 0.1f);
        Plugin.L.LogInfo($"Motion melee: {weapon.name} swung through {list.Count} ({pose.Velocity.magnitude:F1} m/s)");
    }
}
