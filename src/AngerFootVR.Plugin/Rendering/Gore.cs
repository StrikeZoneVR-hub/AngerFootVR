using System.Collections.Generic;
using System.Reflection;
using AngerFootVR.Core;
using Framework;
using HarmonyLib;
using UnityEngine;

namespace AngerFootVR.Rendering;

/// <summary>
/// GORE (VR settings): OFF (no blood at all), GAME (the game's own), EXTRA (the game's and more, built from its own blood
/// effects, "Realistic Blood"):
/// - kicks: a kicked enemy's blood hits the wall behind them, with a bigger burst; kicking a body bleeds too;
/// - bodies slammed into walls and floors leave a splat where they hit;
/// - head pops: a headshot kill takes the head off (the head bone shrunk away, the way the game's Big Heads shoe grows
///   it), with a burst and a neck fountain (not bosses, nor the enemies whose heads are their own business);
/// - bullet wounds spurt (the game's own spurt, which its enemies never used);
/// - explosion kills and exploding corpses spray blood round them;
/// - wounds stay on enemies and go with them as they ragdoll, and blood lands on bodies and on you (your shoe when you
///   kick, your hand and gun up close): the blood's depth picture takes in enemies, bodies and you;
/// - BLOOD COLOUR: the game's pink, or red.
/// Everything follows the game's own Show Blood setting. The extras keep to a budget (the oldest go first); the game's
/// own blood stays for the level, as it always did.
/// </summary>
[HarmonyPatch]
internal static class Gore
{
    public static bool Extra => XrManager.IsRunning && GameSettings.ShowBlood && VrSettings.Gore != null && VrSettings.Gore.Value == VrSettings.GoreLevel.Extra && SingletonBehaviour<BloodCamera>.HasInstance;

    private static bool NoBlood => XrManager.IsRunning && VrSettings.Gore != null && VrSettings.Gore.Value == VrSettings.GoreLevel.Off;

    private const int Budget = 90; // extra blood objects alive at once (each a few renderers drawn per eye)
    private const int PerFrame = 6; // instantiated in one frame at most (the rest the next frames)
    private static readonly Queue<GameObject> Alive = new Queue<GameObject>();
    private static readonly Queue<System.Action> Pending = new Queue<System.Action>();

    private static readonly FieldInfo DefaultColour = AccessTools.Field(typeof(BloodCamera), "_defaultColour");
    private static readonly FieldInfo HeadshotColour = AccessTools.Field(typeof(BloodCamera), "_headshotColour");
    private static readonly Color Red = new Color(0.55f, 0.02f, 0.03f, 1f);
    private static readonly Color RedHeadshot = new Color(0.42f, 0.01f, 0.02f, 1f);
    private static Color _pink = new Color(0.8679245f, 0.2169811f, 0.6776269f, 1f), _pinkHeadshot = new Color(0.8679245f, 0.2169811f, 0.6776269f, 1f);

    private static bool IsRed => XrManager.IsRunning && VrSettings.BloodColour != null && VrSettings.BloodColour.Value == VrSettings.BloodTint.Red;

    /// <summary>The blood's colour now: the game's own pink (as its level has it), or red.</summary>
    public static Color Colour => IsRed ? Red : _pink;

    // ------------------------------------------------------------------ every frame

    private static BloodCamera _level;
    private static bool? _red;
    private static GameObject _gameSpurt;
    private static bool _spurting;

    public static void Update()
    {
        Body.BloodOnYou.Update();
        if (!SingletonBehaviour<BloodCamera>.HasInstance) return;
        var bc = SingletonBehaviour<BloodCamera>.Instance;
        if (bc != _level)
        {
            // a new level (its own blood camera): the game's own colours as it has them, and nothing of the last level's
            _level = bc;
            _red = null;
            if (DefaultColour?.GetValue(bc) is Color d) _pink = d;
            if (HeadshotColour?.GetValue(bc) is Color h) _pinkHeadshot = h;
            NewLevel();
        }

        // the blood colour, the game's own way (all its blood takes it from here)
        if (_red != IsRed)
        {
            _red = IsRed;
            DefaultColour?.SetValue(bc, IsRed ? Red : _pink);
            HeadshotColour?.SetValue(bc, IsRed ? RedHeadshot : _pinkHeadshot);
        }

        // bullet wounds spurt (the game's own spurt, on every enemy it hits): on with EXTRA, the game's way otherwise
        Spurts(Extra);
        Holes.Update();

        // the extras due, a few a frame
        for (var i = 0; i < PerFrame && Pending.Count > 0; i++)
        {
            try
            {
                Pending.Dequeue()();
            }
            catch (System.Exception e)
            {
                Plugin.L.LogWarning("Gore: " + e.Message);
            }
        }
    }

    private static void Spurts(bool on)
    {
        var enemy = WorldMaterial.Enemy;
        var flesh = WorldMaterial.StomachFlesh;
        if (enemy == null || flesh == null || on == _spurting) return;
        if (on)
        {
            _gameSpurt = enemy.NormalAlignedBulletImpactEffectPrefab;
            enemy.NormalAlignedBulletImpactEffectPrefab = flesh.NormalAlignedBulletImpactEffectPrefab;
        }
        else enemy.NormalAlignedBulletImpactEffectPrefab = _gameSpurt;

        _spurting = on;
    }

    // ------------------------------------------------------------------ making blood (the game's own effects)

    private static void Track(GameObject go)
    {
        Alive.Enqueue(go);
        while (Alive.Count > Budget)
        {
            var old = Alive.Dequeue();
            if (old != null) Object.Destroy(old);
        }
    }

    /// <summary>A burst of flying blood (BloodCamera.SpawnBlood's own way), with its pool on the floor or not.</summary>
    public static void Burst(Vector3 at, Vector3 dir, bool pool = true) => Pending.Enqueue(() =>
    {
        if (!SingletonBehaviour<BloodCamera>.HasInstance) return;
        var bc = SingletonBehaviour<BloodCamera>.Instance;
        if (bc.BloodFX == null || bc.BloodFX.Length == 0) return;
        var yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + 180f;
        var go = Object.Instantiate(bc.BloodFX[Random.Range(0, bc.BloodFX.Length)], at, Quaternion.Euler(0f, yaw + 90f, 0f));
        var s = go.GetComponent<BFX_BloodSettings>();
        if (s != null)
        {
            s.Colour = s.DecalColour = Colour;
            s.FreezeDecalDisappearance = true;
            s.LightIntensityMultiplier = 1f;
            s.AnimationSpeed = 2f;
            s.DecalRenderinMode = BFX_BloodSettings._DecalRenderinMode.AverageRayBetwenForwardAndFloor;
        }

        if (!pool)
            foreach (var d in go.GetComponentsInChildren<BFX_DecalSettings>())
                Object.Destroy(d.gameObject);
        Track(go);
    });

    /// <summary>A splat stuck to a surface (BloodCamera.SpawnBackSplatter's own way); on a part, it goes with the part.</summary>
    public static void Splat(Vector3 at, Vector3 dir, float scale = 1f, Transform onto = null) => Pending.Enqueue(() =>
    {
        if (!SingletonBehaviour<BloodCamera>.HasInstance) return;
        var bc = SingletonBehaviour<BloodCamera>.Instance;
        if (bc.BloodAttach == null || (onto == null && dir == Vector3.zero)) return;
        var go = Object.Instantiate(bc.BloodAttach);
        var t = go.transform;
        t.position = at;
        t.localScale = Vector3.one * (Random.Range(0.75f, 1.2f) * 3f * scale);
        t.rotation = Quaternion.LookRotation(dir == Vector3.zero ? Vector3.down : dir) * Quaternion.Euler(90f, 0f, 0f);
        if (onto != null) t.SetParent(onto, true);
        var p = go.GetComponentInChildren<BFX_ShaderProperies>();
        if (p != null)
        {
            p.ForceTime = true;
            p.ForcedTime = Random.Range(0.05f, 0.2f);
            p.SetColour(Colour);
            p.transform.localRotation = Quaternion.Euler(0f, Random.value * 360f, 0f);
        }

        Track(go);
    });

    /// <summary>The wall (or floor) behind a hit, along the way the blood flies, gets a splat.</summary>
    public static void WallBehind(Vector3 from, Vector3 dir, float scale = 1f)
    {
        if (dir == Vector3.zero) return;
        if (Physics.Raycast(from, dir.normalized, out var hit, 6f, LayerMasks.ThingsBloodSplatterCanHit))
            Splat(hit.point, dir.normalized, scale);
    }

    // ------------------------------------------------------------------ OFF: no blood at all

    [HarmonyPrefix]
    [HarmonyPatch(typeof(BloodCamera), nameof(BloodCamera.SpawnBlood), typeof(Vector3), typeof(Vector3), typeof(Color), typeof(bool), typeof(Enemy))]
    private static bool NoSpawnBlood() => !NoBlood;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(BloodCamera), nameof(BloodCamera.SpawnBackSplatter), typeof(Vector3), typeof(Vector3), typeof(Vector3), typeof(Color), typeof(Enemy))]
    private static bool NoBackSplatter() => !NoBlood;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(EffectsManager), nameof(EffectsManager.CreateHeadshotEffect))]
    private static void NoHeadshotEffect(ref bool showVisual)
    {
        if (NoBlood) showVisual = false; // (its sound stays)
    }

    // ------------------------------------------------------------------ kicks

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Enemy), nameof(Enemy.OnKicked))]
    private static void BeforeKick(Enemy __instance, out float __state) => __state = __instance.Health != null ? __instance.Health.CurrentHealth : 0f;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Enemy), nameof(Enemy.OnKicked))]
    private static void Kicked(Enemy __instance, Vector3 position, Vector3 direction, Collider collider, float __state)
    {
        if (!Extra || __instance == null || __instance.Rig == null) return;
        var wasAlive = __state > 0f;
        var material = wasAlive ? __instance.Rig.GetWorldMaterial() : __instance.Rig.GetCorpseWorldMaterial();
        if (material == null || !material.SpawnsBlood) return;
        if (wasAlive)
        {
            if (__instance.Health.CurrentHealth >= __state) return; // (the kick didn't land: a shield, a block)
            WallBehind(position, direction, 1.15f);
            Burst(position, direction);
            Wound(__instance, collider, position, direction);
            if (__instance.Health.IsDead) Body.BloodOnYou.Splash(position, 0.6f); // (a kick that killed: a little on your legs and front)
        }
        else Burst(position, direction); // a body bleeds when you kick it

        Body.BloodOnYou.Kicked(position);
    }

    // ------------------------------------------------------------------ bodies against walls

    private static readonly Dictionary<Enemy, (float at, int count)> Smears = new Dictionary<Enemy, (float, int)>();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Enemy), nameof(Enemy.OnPartCollision))]
    private static void Slammed(Enemy __instance, Collision collision)
    {
        if (!Extra || __instance == null || __instance.Health == null || !__instance.Health.IsDead || collision == null) return;
        var layer = collision.collider != null ? collision.collider.gameObject.layer : -1;
        _slams++;
        if (layer == 8 || layer == 23 || layer == 16) _slamTop = Mathf.Max(_slamTop, collision.relativeVelocity.magnitude);
        _slamLayers |= layer >= 0 ? 1 << layer : 0;
        if (collision.relativeVelocity.magnitude < 4.5f || collision.contactCount == 0) return; // (a body dropping hard; a kicked one slams at 10+)
        if (layer != 8 && layer != 23 && layer != 16) return; // walls, floors, roofs
        Smears.TryGetValue(__instance, out var s);
        if (s.count >= 4 || Time.time - s.at < 0.3f) return;
        Smears[__instance] = (Time.time, s.count + 1);
        var c = collision.GetContact(0);
        Splat(c.point, -c.normal, 0.8f);
    }

    // ------------------------------------------------------------------ head pops

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameplayManager), nameof(GameplayManager.OnEnemyKilled))]
    private static void Killed(Enemy enemy, IEnemyPart hitPart, Damage damage)
    {
        if (!Extra || enemy == null || damage.Type == null) return;
        // any bloody kill close by spatters the side of you that faced it (1.1 only ever stained your shoe: the
        // hand's needed a bloody hit within 2.2 m of your eyes, which shooting hardly ever is)
        if (enemy.Rig != null && enemy.Rig.GetWorldMaterial() is { SpawnsBlood: true } && damage.Type != DamageType.Explosion)
            Body.BloodOnYou.Splash(enemy.Rig.GetCenterPosition(), 0.8f);
        if (hitPart == null) return;
        if (!hitPart.IsHead() || !damage.Type.CanHeadshot) return;
        if (enemy.IsBoss || enemy.Rig is not HumanoidRig rig || rig is WallClimbingRig) return;
        if (enemy.GetComponentInChildren<SuicideBomber>() != null || enemy.GetComponentInChildren<AssaultRifleEnemy>() != null || enemy.GetComponentInChildren<GrenadeLauncherEnemy>() != null) return;
        var head = rig.LookIKHeadBone;
        if (head == null || rig.HeadPart == null) return;
        var at = head.position;
        head.localScale = Vector3.one * 0.01f; // (the head and all on it: eyes, teeth, hats)
        _lastHead = head;
        for (var i = 0; i < 3; i++)
            Burst(at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward, i == 0);
        if (rig.HeadPart.Collider != null) SingletonBehaviour<BloodCamera>.Instance.DoExtraSpurts(rig.HeadPart.Collider, 4, 0.25f);
        WallBehind(at, at - VrRig.HeadPosition, 1.2f); // (the way the shot went, near enough)
        if ((at - VrRig.HeadPosition).sqrMagnitude < 2.2f * 2.2f) Body.BloodOnYou.Close(at);
        Body.BloodOnYou.Splash(at, 1.2f);
        _pops++;
    }

    // ------------------------------------------------------------------ explosions

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Enemy), nameof(Enemy.OnDeath))]
    private static void Died(Enemy __instance, Damage damage, Vector3 position)
    {
        if (!Extra || damage.Type != DamageType.Explosion || __instance == null || __instance.Rig == null) return;
        var m = __instance.Rig.GetWorldMaterial();
        if (m != null && !m.SpawnsBlood) return;
        Spray(__instance.transform.position + Vector3.up * 1.1f, 3);
        Holes.Bite(__instance.gameObject, position);
        Body.BloodOnYou.Splash(__instance.transform.position + Vector3.up * 1.1f, 1.5f);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ExplodingCorpsesAbility), nameof(ExplodingCorpsesAbility.CreateExplosion))]
    private static void CorpseBlew(Vector3 position)
    {
        if (Extra) Spray(position + Vector3.up * 0.4f, 5);
    }

    /// <summary>Blood all round a point: bursts, and splats on what's near.</summary>
    private static void Spray(Vector3 at, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var dir = Quaternion.Euler(0f, i * 360f / n + Random.Range(-20f, 20f), 0f) * Vector3.forward;
            Burst(at, dir, i % 2 == 0);
            WallBehind(at, (dir + Vector3.down * Random.Range(0.1f, 0.8f)).normalized, 1.1f);
        }
    }

    // ------------------------------------------------------------------ wounds

    private static readonly Dictionary<Enemy, int> Wounds = new Dictionary<Enemy, int>();

    /// <summary>A wound on the part hit, going with it (at most 3 an enemy).</summary>
    private static void Wound(Enemy enemy, Collider part, Vector3 at, Vector3 dir)
    {
        if (enemy == null || part == null) return;
        Wounds.TryGetValue(enemy, out var n);
        if (n >= 3) return;
        Wounds[enemy] = n + 1;
        Splat(at, dir, 0.3f, part.transform);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(EffectsManager), nameof(EffectsManager.CreateProjectileImpactEffect))]
    private static void Shot(Collider impactCollider, Vector3 impactPosition, Vector3 impactDirection, bool canSpawnBlood)
    {
        if (!Extra || !canSpawnBlood || impactCollider == null) return;
        if (((LayerMasks.EnemyLayers.value | 1 << 31) & 1 << impactCollider.gameObject.layer) == 0) return; // (enemies and bodies)
        var enemy = impactCollider.GetComponentInParent<Enemy>();
        if (enemy == null || enemy.Rig == null || enemy.HasShield) return;
        var m = enemy.Health != null && enemy.Health.IsDead ? enemy.Rig.GetCorpseWorldMaterial() : enemy.Rig.GetWorldMaterial();
        if (m == null || !m.SpawnsBlood) return;
        Wound(enemy, impactCollider, impactPosition, impactDirection);
        Holes.Queue(enemy.gameObject, impactCollider, impactPosition, impactDirection);
        if ((impactPosition - VrRig.HeadPosition).sqrMagnitude < 2.2f * 2.2f) Body.BloodOnYou.Close(impactPosition);
    }

    /// <summary>A level's start: forget the last one's bodies.</summary>
    public static void NewLevel()
    {
        Holes.NewLevel();
        Smears.Clear();
        Wounds.Clear();
        Alive.Clear();
        Pending.Clear();
        Body.BloodOnYou.Clear();
    }

    private static int _pops, _slams, _slamLayers;
    private static float _slamTop;
    private static Transform _lastHead;

    /// <summary>Dev: blood on you as a kick and a close shot just in front of you would leave it.</summary>
    public static void DevOnYou()
    {
        var fwd = VrRig.HeadRotation * Vector3.forward;
        fwd.y = 0f;
        fwd = fwd.sqrMagnitude > 0.0001f ? fwd.normalized : Vector3.forward;
        var foot = Body.HeroBody.FootBone(1);
        if (foot != null) Body.BloodOnYou.Kicked(foot.position + fwd * 0.45f + Vector3.up * 0.3f);
        var hand = Body.HeroBody.HandBone(Input.VrInputProvider.GunHand == UnityEngine.XR.XRNode.LeftHand ? 0 : 1);
        if (hand != null) Body.BloodOnYou.Close(hand.position + fwd * 0.6f);
    }

    /// <summary>Dev: the nearest living enemy, shot in the head or body, kicked, or blown up (as the game does them).</summary>
    public static string DevHit(string how)
    {
        Enemy best = null;
        var bestD = float.MaxValue;
        foreach (var e in Object.FindObjectsOfType<Enemy>())
        {
            if (e == null || e.Health == null || !e.Health.IsAlive || e.Rig is not HumanoidRig) continue;
            var d = (e.transform.position - VrRig.HeadPosition).sqrMagnitude;
            if (d < bestD)
            {
                bestD = d;
                best = e;
            }
        }

        if (best == null) return "no enemies";
        var rig = (HumanoidRig)best.Rig;
        var part = how == "head" ? rig.HeadPart.Collider : best.GetComponentInChildren<Collider>();
        foreach (var c in best.GetComponentsInChildren<Collider>())
            if (how != "head" && c.GetComponentInParent<IEnemyPart>() != null && !c.GetComponentInParent<IEnemyPart>().IsHead())
            {
                part = c;
                break;
            }

        var at = part.bounds.center;
        var dir = (at - VrRig.HeadPosition).normalized;
        switch (how)
        {
            case "head":
            case "body":
                DamageManager.TryDealDamage(part, new Damage(how == "head" ? 100f : 1f, DamageType.Bullet), at, dir, PlayerMeleeAttack.NormalKick); // (a source the game knows: the player)
                SingletonBehaviour<EffectsManager>.Instance.CreateProjectileImpactEffect(null, part, at, dir, -dir, true, false);
                break;
            case "kick":
                var attack = PlayerMeleeAttack.NormalKick;
                if (attack == null) return "no kick here";
                best.OnKicked(attack, at, dir, part);
                break;
            case "explode":
                DamageManager.TryDealDamage(part, new Damage(100f, DamageType.Explosion), at, dir, PlayerMeleeAttack.NormalKick); // (a source the game knows: the player)
                break;
            default:
                return "gorehit head|body|kick|explode";
        }

        return $"{how} {best.name} at {Mathf.Sqrt(bestD):F1} m: alive {best.Health.IsAlive} | {Describe()}";
    }

    /// <summary>Dev: how it stands.</summary>
    public static string Describe() =>
        $"GORE {(VrSettings.Gore != null ? VrSettings.Gore.Value.ToString() : "?")} extra {Extra} colour {(IsRed ? "red" : "pink")} (show blood {GameSettings.ShowBlood}) | extras alive {Alive.Count}/{Budget} pending {Pending.Count} | head pops {_pops}{(_lastHead != null ? $" (last head {_lastHead.lossyScale.x:F3} at {_lastHead.position})" : "")} wounds {Wounds.Count} smeared {Smears.Count} (body hits {_slams}, fastest on walls/floors {_slamTop:F1} m/s, layers {_slamLayers:X}) spurts {_spurting} | {Body.BloodOnYou.Describe()}";
}
