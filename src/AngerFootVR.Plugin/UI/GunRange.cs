using System.Collections.Generic;
using AngerFootVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.UI;

/// <summary>
/// The gun range through the movie house's left wall: every gun of the game's on the table behind the benches (its
/// own model and its own shot, from the player's copy), to pick up and try. Grip one (two-handed guns: your other
/// hand on the front), the trigger fires, as the game's own does (the shotgun's spread, the rifle and uzi and
/// minigun automatic, the minigun spinning up first, the crossbow's bolt sticking where it lands, the launcher's
/// grenade going off, the flamethrower's stream, the plunger sticking). Three lanes of paper goons on carriers (the
/// bench's buttons send them out to 20 m and back; holes stay in the paper, head or body called out), steel plates
/// that ring and swing, bottles to burst and red barrels at the far end. TRAINING: seven courses with medals to win
/// (GunRange.Courses.cs). Put a gun down, and a while later it's back on the table.
/// </summary>
internal static partial class GunRange
{
    private static Transform _root;
    private static bool _built;

    public static void Init()
    {
        FunWorld.Built += Build;
        FunWorld.Tick += Tick;
        FunWorld.BeforeStep += Step;
        FunWorld.Clearing += Clear;
    }

    // ================================================================================== the guns

    private sealed class Gun
    {
        public GameWeapons.GunInfo Info;
        public FunWorld.Grabbable Grab;
        public Vector3 Muzzle, Barrel;
        public string Dev; // (how its barrel was found)
        public Pose Home;
        public float NextShot, SpinUp;
        public AudioSource Loop;
        public ParticleSystem Flame;
        public float FlyFrom = -1f;
        public int Shots; // (fired in the throwing course since it was last on the table)
        public Vector3 FlyStart;
        public Quaternion FlyStartRot;
    }

    private static readonly List<Gun> Guns = new List<Gun>();

    private static void Build()
    {
        _root = FunWorld.Root;
        _built = true;
        Guns.Clear();
        Lanes.Clear();
        Plates.Clear();
        Bottles.Clear();
        Poppers.Clear();
        var lo = MovieHouse.RangeMin;
        var hi = MovieHouse.RangeMax;
        var mid = (lo + hi) / 2;
        FunWorld.Solid(new Vector3(mid.x, -0.25f, mid.z), new Vector3(hi.x - lo.x + 1f, 0.5f, hi.z - lo.z + 1f));
        FunWorld.Solid(new Vector3(lo.x - 0.25f, 2f, mid.z), new Vector3(0.5f, 4f, hi.z - lo.z));
        FunWorld.Solid(new Vector3(hi.x + 0.25f, 2f, (MovieHouse.RangeDoorZ1 + hi.z) / 2), new Vector3(0.5f, 4f, hi.z - MovieHouse.RangeDoorZ1));
        FunWorld.Solid(new Vector3(mid.x, 2f, hi.z - 0.3f), new Vector3(hi.x - lo.x, 4f, 0.6f));
        FunWorld.Solid(new Vector3(mid.x, 4.25f, mid.z), new Vector3(hi.x - lo.x, 0.5f, hi.z - lo.z));
        FunWorld.Solid(new Vector3(mid.x, 2f, lo.z - 0.25f), new Vector3(hi.x - lo.x, 4f, 0.5f));

        // the benches across the shooting line
        var bench = new Vector3(mid.x, 0f, MovieHouse.GunRangeLine + 0.25f);
        FunWorld.Picture("concession_counter", _root, bench, Quaternion.identity).transform.localScale = new Vector3((hi.x - lo.x) / 2.4f, 1.02f, 0.7f);
        FunWorld.Solid(bench + new Vector3(0f, 0.48f, 0f), new Vector3(hi.x - lo.x, 0.98f, 0.45f));
        // the gun table behind, along the back wall
        var tableZ = lo.z + 0.5f;
        FunWorld.Picture("concession_counter", _root, new Vector3(mid.x, 0f, tableZ), Quaternion.Euler(0f, 180f, 0f)).transform.localScale = new Vector3(2.35f, 0.98f, 1.15f);
        FunWorld.Solid(new Vector3(mid.x, 0.47f, tableZ), new Vector3(5.6f, 0.94f, 0.72f));
        MovieHouse.Block(new Vector3(mid.x, 0f, tableZ), 2.82f, 0.38f);
        // (the shield's a Glock behind a riot shield: the Glock's there already)
        var prefabs = GameWeapons.All().FindAll(w => (w is ProjectileWeapon || w is HookWeapon) && w.HoldableObjectType != null && w.HoldableObjectType.name != "Shield");
        var x0 = lo.x + 0.75f;
        var step = (hi.x - lo.x - 1.5f) / Mathf.Max(1, prefabs.Count - 1);
        for (var i = 0; i < prefabs.Count; i++)
        {
            var home = new Pose(new Vector3(x0 + i * step, 0.98f, tableZ + 0.05f), Quaternion.Euler(0f, 180f, 0f)); // (barrels to the wall)
            var prefab = prefabs[i];
            FunWorld.Soon("gun " + prefab.name, () =>
            {
                var g = MakeGun(prefab, home);
                if (g == null) return;
                Guns.Add(g);
                var label = MovieHouse.SignFacing(g.Info.Name.ToUpperInvariant(), home.position + new Vector3(0f, 0.02f, 0.33f), 0.06f, new Color(1f, 0.85f, 0.25f), Vector3.forward, _root);
                if (label != null)
                {
                    label.transform.localRotation = Quaternion.Euler(70f, 180f, 0f);
                    label.rectTransform.sizeDelta = new Vector2(step * 0.95f, 0.2f);
                    label.enableWordWrapping = true;
                }
            });
        }

        MovieHouse.SignFacing("PICK ONE UP (GRIP), THE TRIGGER FIRES", new Vector3(mid.x, 1.75f, lo.z + 0.06f), 0.16f, new Color(0.15f, 0.95f, 1f), Vector3.forward, _root);
        FunWorld.Soon("range targets", Targets);
        FunWorld.Soon("range ready", () => Plog($"Gun range: {Guns.Count} guns on the table"));
    }

    private static Gun MakeGun(global::Weapon prefab, Pose home)
    {
        var go = GameWeapons.GunModel(prefab, _root, out var muzzle, out var barrel, out var bounds);
        if (go == null) return null;
        var info = GameWeapons.Info(prefab);
        go.transform.localPosition = home.position;
        go.transform.localRotation = home.rotation;
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 2f;
        rb.isKinematic = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        // its barrel: the way its muzzle point faces, when that's plainly along the gun; else whichever flat axis runs
        // from its middle to its muzzle (the sniper's muzzle point sits half way along it: from its middle, that had
        // pointed it backwards)
        var toMuzzle = muzzle - bounds.center;
        Vector3 b;
        if (Mathf.Abs(barrel.z) > 0.8f) b = new Vector3(0f, 0f, Mathf.Sign(barrel.z));
        else if (Mathf.Abs(barrel.x) > 0.8f) b = new Vector3(Mathf.Sign(barrel.x), 0f, 0f);
        else if (toMuzzle.sqrMagnitude > 1e-4f) b = Mathf.Abs(toMuzzle.x) > Mathf.Abs(toMuzzle.z) ? new Vector3(Mathf.Sign(toMuzzle.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(toMuzzle.z));
        else b = Vector3.forward;
        // where it's held: its grip under the back of its barrel, a two-handed one's front further along
        var along = Vector3.Dot(bounds.extents, new Vector3(Mathf.Abs(b.x), Mathf.Abs(b.y), Mathf.Abs(b.z)));
        var rear = muzzle - b * Mathf.Max(0.08f, Vector3.Dot(muzzle - (bounds.center - b * along), b));
        var len = Mathf.Max(0.1f, (muzzle - rear).magnitude);
        var h = bounds.size.y;
        // (two hands for the long guns; the pistols, the uzi and the plunger in one)
        var nm = info.Name.ToLowerInvariant();
        var two = !(nm.Contains("glock") || nm.Contains("pistol") || nm.Contains("uzzi") || nm.Contains("plunger"));
        var grip = rear + b * (len * (two ? 0.3f : 0.26f)) + Vector3.down * (h * (two ? 0.18f : 0.32f));
        Vector3? fore = two ? rear + b * (len * 0.66f) + Vector3.down * (h * 0.08f) : (Vector3?)null;
        var grab = FunWorld.MakeGrabbable(go, grip, Quaternion.Inverse(Quaternion.LookRotation(b, Vector3.up)), fore);
        grab.Reach = 0.2f;
        var gun = new Gun { Info = info, Grab = grab, Muzzle = muzzle, Barrel = b, Home = home, Dev = $"facing {barrel:F2} bounds {bounds.center:F2}+-{bounds.extents:F2}" };
        grab.OnTrigger = (x, hand) => Pull(gun, hand, true);
        grab.OnTriggerHeld = (x, hand) => Pull(gun, hand, false);
        grab.OnGrabbed = (x, hand) => GameSounds.Play(info.Name + " Pickup", x.transform.position, 0.7f);
        grab.OnReleased = (x, hand) => StopLoops(gun);
        go.AddComponent<ThrowHit>().Gun = gun;
        var gripKey = "range/" + info.Name;
        grab.Adjust = () => Input.GripAdjust.PoseOf(gripKey);
        return gun;
    }

    private static void Pull(Gun g, XRNode hand, bool down)
    {
        var now = Time.unscaledTime;
        var fires = g.Info.Fires;
        if (fires == "flame")
        {
            Flame(g, hand);
            return;
        }

        var minigun = g.Info.Name.ToLowerInvariant().Contains("minigun");
        if (minigun)
        {
            // spinning up before it fires
            if (g.Loop == null) g.Loop = GameSounds.Loop("Minigun Motor Loop", g.Grab.transform, 0.6f);
            g.SpinUp += Time.unscaledDeltaTime;
            if (g.SpinUp < 0.45f) return;
        }

        if (!down && !g.Info.Auto) return;
        if (now < g.NextShot) return;
        g.NextShot = now + Mathf.Max(0.05f, g.Info.Cooldown);
        if (Empty(g)) return;
        Fire(g, hand);
    }

    private static void Fire(Gun g, XRNode hand)
    {
        var t = g.Grab.transform;
        var muzzle = t.TransformPoint(g.Muzzle);
        var dir = t.TransformDirection(g.Barrel);
        var bank = g.Info.FireBank;
        if (string.IsNullOrEmpty(bank) || !GameSounds.Play(bank, muzzle, 0.9f)) GameSounds.PlayOr("Pistol Shoot", "kick", muzzle, 0.9f);
        Fx.MuzzleFlash(muzzle, dir, g.Info.Kick > 1.5f ? 1.6f : 1f);
        g.Grab.Kick = Mathf.Min(1.2f, g.Grab.Kick + 0.35f * g.Info.Kick);
        VrControls.Rumble(hand, Mathf.Clamp01(0.35f + 0.25f * g.Info.Kick), 0.06f);
        if (g.Grab.Fore.HasValue) VrControls.Rumble(g.Grab.Fore.Value, Mathf.Clamp01(0.25f + 0.2f * g.Info.Kick), 0.05f);
        switch (g.Info.Fires)
        {
            case "grenade":
                Grenade(muzzle, dir);
                return;
            case "bolt":
            case "hook":
                Bolt(muzzle, dir, g.Info.Fires == "hook");
                return;
        }

        for (var k = 0; k < g.Info.Projectiles; k++)
        {
            var spread = Quaternion.AngleAxis(Random.Range(0f, 360f), dir) * Quaternion.AngleAxis(Random.Range(0f, g.Info.Spread), Vector3.Cross(dir, Vector3.up).normalized);
            Bullet(muzzle, spread * dir, g.Info.Projectiles > 1 ? 0.5f : 1f);
        }

        Shell(t, muzzle - dir * 0.15f);
    }

    private static readonly Collider[] Near = new Collider[48];

    private static void Bullet(Vector3 from, Vector3 dir, float force)
    {
        if (!FunWorld.Raycast(from, dir, 60f, out var hit)) return;
        Impact(hit, dir, force);
    }

    /// <summary>What a shot does where it lands: a hole (in paper, plates, walls), a ring, a burst bottle, a barrel
    /// going off, a push.</summary>
    private static void Impact(RaycastHit hit, Vector3 dir, float force)
    {
        var c = hit.collider;
        if (ShotGrenade(c)) return;
        if (c.GetComponentInParent<Paper>() is { } paper)
        {
            Fx.Hole(hit.point, hit.normal, paper.transform, 0.03f);
            paper.Hit(hit.point);
            GameSounds.PlayOr("Bullet Impacts Fabric", "cardboard", hit.point, 0.7f);
            return;
        }

        if (c.GetComponentInParent<Plate>() is { } plate)
        {
            plate.Hit(hit.point, dir, force);
            Fx.Sparks(hit.point, hit.normal, 10);
            return;
        }

        if (c.GetComponentInParent<Bottle>() is { } bottle)
        {
            bottle.Burst(dir * 4f * force);
            return;
        }

        if (c.GetComponentInParent<Popper>() is { } pop)
        {
            pop.Hit(hit.point);
            Fx.Hole(hit.point, hit.normal, pop.transform, 0.03f);
            return;
        }

        if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) c.attachedRigidbody.AddForceAtPosition(dir * 3f * force, hit.point, ForceMode.Impulse);
        Fx.Hole(hit.point, hit.normal, c.attachedRigidbody != null ? c.transform : null, 0.03f);
        Fx.Particles(hit.point, Quaternion.LookRotation(hit.normal), 6, 0, 0.05f, 0.4f, 1.2f, 0.03f, new Color(0.5f, 0.48f, 0.45f, 0.7f), 0.6f, 35f, false, false);
        GameSounds.PlayOr("Bullet Impacts Concrete", "thud", hit.point, 0.6f);
    }

    private static void Shell(Transform gun, Vector3 at)
    {
        var go = FunWorld.Thing("can", FunWorld.Local(at), 0f, 0.01f, true);
        if (go == null) return;
        go.transform.localScale = new Vector3(0.18f, 0.22f, 0.18f);
        go.transform.Find("Visual").GetComponent<MeshRenderer>().sharedMaterial = Props.Mat("brass");
        var rb = go.GetComponent<Rigidbody>();
        rb.WakeUp();
        rb.velocity = gun.right * Random.Range(1.5f, 2.5f) + Vector3.up * Random.Range(1f, 2f);
        rb.angularVelocity = Random.insideUnitSphere * 20f;
        FunWorld.AddDebris(go, 6f);
    }

    // the crossbow's bolt and the plunger fly (fast), and stick in what they hit
    private sealed class Flying
    {
        public Transform T;
        public Vector3 Vel;
        public float Born;
        public bool Plunger;
    }

    private static readonly List<Flying> Darts = new List<Flying>();

    private static void Bolt(Vector3 from, Vector3 dir, bool plunger)
    {
        var go = new GameObject(plunger ? "Plunger" : "Bolt") { layer = VrUi.Layer };
        go.transform.SetParent(_root, false);
        go.transform.position = from;
        go.transform.rotation = Quaternion.LookRotation(dir);
        if (plunger)
        {
            var cup = FunWorld.Picture("popcorn_bucket", go.transform, new Vector3(0f, 0f, 0.05f), Quaternion.Euler(-90f, 0f, 0f));
            if (cup != null)
            {
                cup.transform.localScale = new Vector3(0.6f, 0.5f, 0.6f);
                foreach (var r in cup.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = Props.Mat("plastic_red");
            }
        }

        var shaft = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(shaft.GetComponent<Collider>());
        shaft.layer = VrUi.Layer;
        shaft.transform.SetParent(go.transform, false);
        shaft.transform.localScale = plunger ? new Vector3(0.025f, 0.025f, 0.45f) : new Vector3(0.012f, 0.012f, 0.42f);
        shaft.transform.localPosition = new Vector3(0f, 0f, -0.2f);
        shaft.GetComponent<MeshRenderer>().sharedMaterial = Props.Mat(plunger ? "wood_light" : "steel_dark");
        Darts.Add(new Flying { T = go.transform, Vel = dir * (plunger ? 24f : 45f), Born = Time.unscaledTime, Plunger = plunger });
        FunWorld.AddDebris(go, 30f);
    }

    private static void Darting(float dt)
    {
        for (var i = Darts.Count - 1; i >= 0; i--)
        {
            var d = Darts[i];
            if (d.T == null || Time.unscaledTime - d.Born > 4f)
            {
                Darts.RemoveAt(i);
                continue;
            }

            d.Vel += Vector3.down * 3f * dt;
            var move = d.Vel * dt;
            if (FunWorld.Raycast(d.T.position, move.normalized, move.magnitude, out var hit))
            {
                d.T.position = hit.point - move.normalized * 0.05f;
                d.T.SetParent(hit.collider.transform, true);
                GameSounds.PlayOr(d.Plunger ? "Plunger Attach" : "Arrow Impacts", "thud", hit.point, 0.8f);
                Impact(hit, move.normalized, 1.2f);
                Darts.RemoveAt(i);
                continue;
            }

            d.T.position += move;
            d.T.rotation = Quaternion.LookRotation(d.Vel);
        }
    }

    // the launcher's grenade arcs out and goes off where it lands
    private static readonly List<Rigidbody> Grenades = new List<Rigidbody>();

    private static void Grenade(Vector3 from, Vector3 dir)
    {
        var go = FunWorld.Thing("kickball", FunWorld.Local(from + dir * 0.2f), 0f, 0.4f);
        if (go == null) return;
        go.transform.localScale = Vector3.one * 0.35f;
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = Props.Mat("steel_dark");
        var rb = go.GetComponent<Rigidbody>();
        rb.WakeUp();
        rb.velocity = dir * 16f;
        go.AddComponent<Boom>();
        Grenades.Add(rb);
        FunWorld.AddDebris(go, 6f);
    }

    private sealed class Boom : MonoBehaviour
    {
        private float _born;

        private void Start() => _born = Time.unscaledTime;

        private void OnCollisionEnter(Collision c)
        {
            if (Time.unscaledTime - _born > 0.05f) Blow(transform.position);
        }

        private void Update()
        {
            if (Time.unscaledTime - _born > 3f) Blow(transform.position);
        }

        private void Blow(Vector3 at)
        {
            Explode(at, 2.6f);
            Destroy(gameObject);
        }
    }

    private static void Explode(Vector3 at, float radius)
    {
        Fx.Explosion(at, 1f);
        GameSounds.Play("Grenade explosion", at, 1f);
        VrControls.Rumble(XRNode.LeftHand, 0.8f, 0.2f);
        VrControls.Rumble(XRNode.RightHand, 0.8f, 0.2f);
        var n = FunWorld.Overlap(at, radius, Near);
        for (var i = 0; i < n; i++)
        {
            var c = Near[i];
            if (c.GetComponentInParent<Bottle>() is { } b) b.Burst((c.transform.position - at).normalized * 6f);
            else if (c.GetComponentInParent<Plate>() is { } p) p.Hit(c.transform.position, (c.transform.position - at).normalized, 3f);
            else if (c.GetComponentInParent<Paper>() is { } paper) paper.Hit(c.transform.position);
            else if (c.GetComponentInParent<Popper>() is { } pop) pop.Hit(pop.transform.position + Vector3.up, "blast");
            if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) c.attachedRigidbody.AddExplosionForce(12f, at, radius, 0.5f, ForceMode.VelocityChange);
        }
    }

    // the flamethrower: a roaring stream while the trigger's held; what's in it gets scorched
    private static void Flame(Gun g, XRNode hand)
    {
        var t = g.Grab.transform;
        var muzzle = t.TransformPoint(g.Muzzle);
        var dir = t.TransformDirection(g.Barrel);
        if (g.Flame == null)
        {
            g.Flame = Fx.Particles(muzzle, Quaternion.LookRotation(dir), 0, 120, 600f, 0.55f, 9f, 0.18f, new Color(1f, 0.55f, 0.15f), -0.3f, 7f, true, false, float.NaN, t);
            g.Flame.transform.localPosition = g.Muzzle;
            g.Flame.transform.localRotation = Quaternion.LookRotation(g.Barrel);
            g.Loop = GameSounds.Loop("Flamethrower flame loop", t, 0.7f);
        }

        g.SpinUp = Time.unscaledTime; // (held this frame)
        if (Time.unscaledTime < g.NextShot) return;
        g.NextShot = Time.unscaledTime + 0.12f;
        VrControls.Rumble(hand, 0.25f, 0.1f);
        if (!FunWorld.Raycast(muzzle, dir, 6f, out var hit)) return;
        if (hit.collider.GetComponentInParent<Paper>() is { } paper) paper.Burn(hit.point);
        else if (hit.collider.GetComponentInParent<Bottle>() is { } b) b.Burst(dir * 2f);
        else if (hit.collider.GetComponentInParent<Popper>() is { } pop) pop.Hit(hit.point, "flame");
        if (Random.value < 0.3f) Fx.Smoke(hit.point, 0.6f, 0.5f);
    }

    private static void StopLoops(Gun g)
    {
        if (g.Loop != null) Object.Destroy(g.Loop.gameObject);
        g.Loop = null;
        if (g.Flame != null) Object.Destroy(g.Flame.gameObject);
        g.Flame = null;
        g.SpinUp = 0f;
    }

    // ================================================================================== targets

    private sealed class Lane
    {
        public Transform Carrier;
        public Paper Paper;
        public float Distance = 7f, Shown = 7f;
        public TextMeshPro Board;
        public int Head, Body;
        public float X;
    }

    private static readonly List<Lane> Lanes = new List<Lane>();

    /// <summary>A paper goon on a carrier: where a shot lands it's holed, the head or the body called out.</summary>
    private sealed class Paper : MonoBehaviour
    {
        public Lane Lane;
        public float Scale = 1f;

        public void Hit(Vector3 at)
        {
            var local = transform.InverseTransformPoint(at);
            var head = local.y > 1.47f * Scale;
            if (Lane != null)
            {
                if (head) Lane.Head++;
                else Lane.Body++;
                ShowLane(Lane, head ? "HEAD!" : "BODY");
            }
        }

        public void Burn(Vector3 at)
        {
            Fx.Smoke(at, 0.8f, 0.6f);
            foreach (var r in GetComponentsInChildren<MeshRenderer>())
                foreach (var m in r.materials)
                    m.color = Color.Lerp(m.color, new Color(0.12f, 0.08f, 0.06f), 0.06f);
        }
    }

    private sealed class Plate : MonoBehaviour
    {
        public Rigidbody Body;

        public void Hit(Vector3 at, Vector3 dir, float force)
        {
            Body.AddForceAtPosition(dir * 2.5f * force, at, ForceMode.Impulse);
            GameSounds.PlayOr("Bullet Impacts Metal", "glass", at, 1f);
        }
    }

    private sealed class Bottle : MonoBehaviour
    {
        public string Kind;
        public Vector3 Home;
        public bool Gone;

        public void Burst(Vector3 push)
        {
            if (Gone) return;
            Gone = true;
            if (Kind == "barrel") Explode(transform.position + Vector3.up * 0.45f, 3f);
            RageRoom.Shatter(transform, Kind, push);
            gameObject.SetActive(false);
            Respawns.Add((this, Time.unscaledTime + (Kind == "barrel" ? 10f : 6f)));
        }
    }

    private static readonly List<Bottle> Bottles = new List<Bottle>();
    private static readonly List<(Bottle b, float at)> Respawns = new List<(Bottle, float)>();
    private static readonly List<Plate> Plates = new List<Plate>();

    private static void Targets()
    {
        // (a lane a frame: all three at once was 9 ms)
        for (var k = 0; k < 3; k++)
        {
            var lane = k;
            FunWorld.Soon("range lane " + k, () => MakeLane(lane));
        }

        FunWorld.Soon("range plates", Plates_);
        FunWorld.Soon("range bottles", Shelf);
        FunWorld.Soon("range courses", CoursePanel);
    }

    private static void MakeLane(int k)
    {
        var lo = MovieHouse.RangeMin;
        var hi = MovieHouse.RangeMax;
        var w = (hi.x - lo.x) / 3f;
        {
            var x = lo.x + w * (k + 0.5f);
            var lane = new Lane { X = x, Distance = 5f + k * 3f, Shown = 5f + k * 3f };
            var carrier = new GameObject("Carrier " + k) { layer = FunWorld.PhysLayer }.transform;
            carrier.SetParent(_root, false);
            carrier.localPosition = new Vector3(x, 0.15f, MovieHouse.GunRangeLine + lane.Distance);
            lane.Carrier = carrier;
            var paper = FunWorld.Picture(k == 1 ? "cutout_goon2" : "cutout_goon", carrier, Vector3.zero, Quaternion.Euler(0f, 180f, 0f));
            if (paper != null)
            {
                var box = paper.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.9f, 0f);
                box.size = new Vector3(0.62f, 1.8f, 0.04f);
                paper.layer = FunWorld.PhysLayer;
                var p = paper.AddComponent<Paper>();
                p.Lane = lane;
                lane.Paper = p;
                // (its picture on the movie house's layer: a child copy of its renderer)
                var vis = new GameObject("Visual") { layer = VrUi.Layer };
                vis.transform.SetParent(paper.transform, false);
                vis.AddComponent<MeshFilter>().sharedMesh = paper.GetComponent<MeshFilter>().sharedMesh;
                vis.AddComponent<MeshRenderer>().sharedMaterials = paper.GetComponent<MeshRenderer>().sharedMaterials;
                Object.Destroy(paper.GetComponent<MeshRenderer>());
                Object.Destroy(paper.GetComponent<MeshFilter>());
                paper.AddComponent<Rigidbody>().isKinematic = true;
            }

            // the bench's buttons: nearer, further, back (clean paper)
            var panel = new Vector3(x, 1.0f, MovieHouse.GunRangeLine + 0.1f);
            lane.Board = MovieHouse.SignFacing("", panel + new Vector3(0f, 0.4f, 0.1f), 0.11f, new Color(1f, 0.85f, 0.25f), Vector3.back, _root);
            foreach (var (dx, label, act) in new (float, string, System.Action)[]
                     {
                         (-0.4f, "-", () => Move(lane, -2.5f)), (0.4f, "+", () => Move(lane, 2.5f)), (0f, "NEW", () => Fresh(lane)),
                     })
            {
                var b = new GameObject("Lane button " + label) { layer = FunWorld.PhysLayer };
                b.transform.SetParent(_root, false);
                b.transform.localPosition = panel + new Vector3(dx, 0f, 0f);
                b.AddComponent<BoxCollider>().size = new Vector3(0.14f, 0.06f, 0.14f);
                FunWorld.Picture("rage_button_cap", _root, panel + new Vector3(dx, -0.03f, 0f), Quaternion.identity).transform.localScale = Vector3.one * 0.6f;
                MovieHouse.SignFacing(label, panel + new Vector3(dx, 0.12f, 0.05f), 0.1f, new Color(0.15f, 0.95f, 1f), Vector3.back, _root);
                FunWorld.MakePress(b, h => act(), 0.09f);
            }

            Lanes.Add(lane);
            ShowLane(lane, "");
        }
    }

    private static void Plates_()
    {
        var lo = MovieHouse.RangeMin;
        // steel plates at 12 m, swinging on their hinges
        for (var k = 0; k < 5; k++)
        {
            var at = new Vector3(lo.x + 0.9f + k * 1.15f, 0f, MovieHouse.GunRangeLine + 12f);
            var post = FunWorld.Solid(at + new Vector3(0f, 0.55f + (k % 2) * 0.35f, 0.05f), new Vector3(0.05f, 1.1f + (k % 2) * 0.7f, 0.05f));
            var arb = post.AddComponent<Rigidbody>();
            arb.isKinematic = true;
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(plate.GetComponent<Collider>());
            var go = new GameObject("Plate") { layer = FunWorld.PhysLayer };
            go.transform.SetParent(_root, false);
            go.transform.localPosition = at + new Vector3(0f, 1.0f + (k % 2) * 0.7f, 0f);
            plate.layer = VrUi.Layer;
            plate.transform.SetParent(go.transform, false);
            plate.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            plate.transform.localScale = new Vector3(0.3f, 0.01f, 0.3f);
            plate.GetComponent<MeshRenderer>().sharedMaterial = Props.Mat(k % 2 == 0 ? "steel" : "hazard_yellow");
            var cc = go.AddComponent<BoxCollider>();
            cc.size = new Vector3(0.3f, 0.3f, 0.02f);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 2f;
            rb.angularDrag = 0.3f;
            var hj = go.AddComponent<HingeJoint>();
            hj.connectedBody = arb;
            hj.anchor = new Vector3(0f, 0.16f, 0f);
            hj.axis = Vector3.right;
            var pl = go.AddComponent<Plate>();
            pl.Body = rb;
            Plates.Add(pl);
        }
    }

    private static void Shelf()
    {
        var lo = MovieHouse.RangeMin;
        var hi = MovieHouse.RangeMax;
        // bottles on a shelf at 16 m, red barrels at the far end
        var shelf = new Vector3((lo.x + hi.x) / 2, 1.1f, MovieHouse.GunRangeLine + 16f);
        FunWorld.Solid(shelf, new Vector3(hi.x - lo.x - 0.4f, 0.04f, 0.3f));
        FunWorld.Picture("concession_counter", _root, shelf - new Vector3(0f, 1.1f, 0f), Quaternion.identity).transform.localScale = new Vector3((hi.x - lo.x - 0.4f) / 2.4f, 1.12f, 0.45f);
        for (var k = 0; k < 9; k++)
            AddBottle(k % 3 == 2 ? "vase" : k % 2 == 0 ? "bottle_green" : "bottle_brown", shelf + new Vector3(-2.6f + k * 0.65f, 0.025f, 0f));
        foreach (var x in new[] { lo.x + 0.8f, (lo.x + hi.x) / 2 + 0.9f, hi.x - 0.8f })
            AddBottle("barrel", new Vector3(x, 0f, hi.z - 1.2f));
    }

    private static void AddBottle(string kind, Vector3 at)
    {
        var go = FunWorld.Thing(kind, at, Random.Range(0f, 360f), kind == "barrel" ? 30f : 0.4f, true, true);
        if (go == null) return;
        var b = go.AddComponent<Bottle>();
        b.Kind = kind;
        b.Home = at;
        Bottles.Add(b);
    }

    private static void Move(Lane lane, float by)
    {
        lane.Distance = Mathf.Clamp(lane.Distance + by, 2.5f, 20f);
        GameSounds.PlayOr("Trolley Impacts", "thud", FunWorld.World(lane.Carrier.localPosition), 0.5f);
    }

    private static void Fresh(Lane lane)
    {
        lane.Head = lane.Body = 0;
        if (lane.Paper != null)
            foreach (Transform c in lane.Paper.transform)
                if (c.name == "Hole")
                    Object.Destroy(c.gameObject);
        ShowLane(lane, "FRESH PAPER");
    }

    private static void ShowLane(Lane lane, string last)
    {
        if (lane.Board == null) return;
        lane.Board.text = $"{lane.Distance:F1} M   HEAD {lane.Head}  BODY {lane.Body}{(last.Length > 0 ? "\n" + last : "")}";
    }

    // ================================================================================== DRILL: pop-up goons

    private static readonly List<Popper> Poppers = new List<Popper>();

    private static void Pop(Vector3 world, string text, Color c)
    {
        var face = CinemaRig.HeadPosition - world;
        face.y = 0f;
        var t = MovieHouse.SignFacing(text, FunWorld.Local(world + Vector3.up * 0.3f), 0.2f, c, face.sqrMagnitude > 1e-4f ? face.normalized : Vector3.back, _root);
        if (t != null) Object.Destroy(t.gameObject, 1.2f);
    }

    // ================================================================================== every frame

    private static void Step(float dt)
    {
        if (!_built) return;
        Darting(dt);
        foreach (var lane in Lanes)
        {
            lane.Shown = Mathf.MoveTowards(lane.Shown, lane.Distance, dt * 4f);
            if (lane.Carrier != null) lane.Carrier.localPosition = new Vector3(lane.X, 0.15f, MovieHouse.GunRangeLine + lane.Shown);
        }

        foreach (var g in Guns)
        {
            if (g.FlyFrom < 0f || g.Grab == null) continue;
            if (g.Grab.Busy)
            {
                g.FlyFrom = -1f;
                continue;
            }

            var t = Mathf.Clamp01((Time.unscaledTime - g.FlyFrom) / 0.5f);
            var e = t * t * (3f - 2f * t);
            g.Grab.Body.MovePosition(FunWorld.World(Vector3.Lerp(g.FlyStart, g.Home.position, e)));
            g.Grab.Body.MoveRotation(_root.rotation * Quaternion.Slerp(g.FlyStartRot, g.Home.rotation, e));
            if (t >= 1f)
            {
                g.FlyFrom = -1f;
                g.Shots = 0; // (back on the table: loaded again)
            }
        }
    }

    private static void Tick()
    {
        if (!_built) return;
        var now = Time.unscaledTime;
        foreach (var g in Guns)
        {
            if (g.Grab == null) continue;
            if (g.Grab.Busy) Input.GripAdjust.Holding("range/" + g.Info.Name, g.Info.Name.ToUpperInvariant(), g.Grab.Holder.Value, g.Grab.GripWorld, g.Grab.GripWorld + Vector3.up * 0.2f);
            // the flamethrower's stream stops when the trigger's let go; the minigun spins down
            if (g.Flame != null && (!g.Grab.Busy || now - g.SpinUp > 0.08f)) StopLoops(g);
            if (g.Info.Name.ToLowerInvariant().Contains("minigun") && g.Grab.Busy && !VrControls.Held(g.Grab.Holder.Value, VrButton.Trigger))
            {
                g.SpinUp = Mathf.Max(0f, g.SpinUp - Time.unscaledDeltaTime * 2f);
                if (g.SpinUp <= 0f && g.Loop != null) StopLoops(g);
            }

            // put down a while: back to the table
            if (!g.Grab.Busy && g.FlyFrom < 0f && now - g.Grab.LastHeld > 8f && (g.Grab.transform.localPosition - g.Home.position).magnitude > 0.05f)
            {
                g.FlyFrom = now;
                g.FlyStart = g.Grab.transform.localPosition;
                g.FlyStartRot = Quaternion.Inverse(_root.rotation) * g.Grab.transform.rotation;
                g.Grab.Body.isKinematic = true;
            }
        }

        for (var i = Respawns.Count - 1; i >= 0; i--)
        {
            var (b, at) = Respawns[i];
            if (b == null || now < at) continue;
            Respawns.RemoveAt(i);
            b.Gone = false;
            b.transform.localPosition = b.Home;
            b.gameObject.SetActive(true);
        }

        CourseTick();
    }

    private static void Plog(string s) => Plugin.L.LogInfo(s);

    /// <summary>Dev: this gun (by name) into the right hand, or "drop" to let go of it.</summary>
    public static string DevHold(string name)
    {
        if (name == "drop")
        {
            FunWorld.Release(UnityEngine.XR.XRNode.RightHand, false);
            return "dropped";
        }

        var g = Guns.Find(x => x.Info.Name.ToLowerInvariant().Contains(name.ToLowerInvariant()));
        if (g == null) return "no gun like " + name;
        FunWorld.Grab(UnityEngine.XR.XRNode.RightHand, g.Grab, false);
        return "holding " + g.Info.Name;
    }

    public static void Clear()
    {
        _built = false;
        foreach (var g in Guns) StopLoops(g);
        Guns.Clear();
        Lanes.Clear();
        Plates.Clear();
        Bottles.Clear();
        Respawns.Clear();
        Poppers.Clear();
        Darts.Clear();
        Grenades.Clear();
        ClearCourses();
        _root = null;
    }

    public static string Describe()
    {
        var names = new List<string>();
        foreach (var g in Guns) names.Add($"{g.Info.Name}({g.Info.Fires}{(g.Info.Auto ? " auto" : "")} x{g.Info.Projectiles} {g.Info.Cooldown:F2}s {(g.Grab.ForeLocal.HasValue ? "2h" : "1h")} '{g.Info.FireBank}' muzzle {g.Muzzle:F2} barrel {g.Barrel:F1} grip {g.Grab.GripLocal:F2}{(Plugin.DevRun ? " " + g.Dev : "")})");
        return $"RANGE built {_built} guns {Guns.Count}: {string.Join(", ", names)} | lanes {Lanes.Count} plates {Plates.Count} bottles {Bottles.Count} course {Courses[_course].Name} {(_running ? "on" : "off")}";
    }
}
