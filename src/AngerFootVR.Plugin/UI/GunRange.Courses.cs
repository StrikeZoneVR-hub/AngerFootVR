using System.Collections.Generic;
using System.IO;
using AngerFootVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.UI;

/// <summary>
/// THE RANGE's TRAINING: seven short courses, each teaching something the levels ask of you, each with a bronze, a
/// silver and a gold to win: FIRST SHOTS (hit each goon as it pops up), HEADS UP (only headshots count), ON THE MOVE
/// (goons running across), NOT GRANNY (goons and grannies popping up fast), EMPTY? THROW IT (three shots a gun, then
/// throw it), SWAT BACK (goons lob grenades: swat them back, or shoot them in the air) and THE GAUNTLET (all of it). The
/// bench's &lt; &gt; pick one, START runs it (again: stops it); the board above says what to do and what each medal
/// takes, the wall beside you shows the medals you've won. Your best for each is kept (BepInEx/AngerFootVR/
/// range-medals.txt; not on a dev run).
/// </summary>
internal static partial class GunRange
{
    private sealed class Course
    {
        public string Name, Tip;
        public bool Timed; // (a time to beat, lower is better; else a score)
        public float Bronze, Silver, Gold, Limit;
    }

    private static readonly Course[] Courses =
    {
        new Course { Name = "FIRST SHOTS", Tip = "PICK UP A GUN AND HIT EACH GOON AS IT POPS UP. AS FAST AS YOU CAN.", Timed = true, Gold = 9f, Silver = 15f, Bronze = 30f, Limit = 60f },
        new Course { Name = "HEADS UP", Tip = "ONLY HEADSHOTS COUNT. IN THE LEVELS ONE TO THE HEAD DROPS ANY GOON.", Gold = 7f, Silver = 5f, Bronze = 3f, Limit = 40f },
        new Course { Name = "ON THE MOVE", Tip = "GOONS RUN ACROSS. AIM A LITTLE AHEAD OF THEM, WHERE THEY'RE GOING.", Gold = 7f, Silver = 5f, Bronze = 3f, Limit = 30f },
        new Course { Name = "NOT GRANNY", Tip = "GOONS AND GRANNIES POP UP FAST. SHOOT GOONS, NEVER GRANNIES. LOOK FIRST.", Gold = 1500f, Silver = 1000f, Bronze = 500f, Limit = 25f },
        new Course { Name = "EMPTY? THROW IT", Tip = "THREE SHOTS A GUN. OUT OF AMMO? THROW THE GUN AT A GOON TO KNOCK HIM DOWN.", Timed = true, Gold = 16f, Silver = 26f, Bronze = 45f, Limit = 60f },
        new Course { Name = "SWAT BACK", Tip = "GOONS LOB GRENADES. SWING YOUR HAND AT ONE TO SEND IT BACK (OR SHOOT IT IN THE AIR).", Gold = 5f, Silver = 3f, Bronze = 1f, Limit = 40f },
        new Course { Name = "THE GAUNTLET", Tip = "ALL OF IT AT ONCE: POP-UPS, RUNNERS, GRANNIES, GRENADES. 45 SECONDS.", Gold = 3000f, Silver = 2000f, Bronze = 1000f, Limit = 45f },
    };

    private const int FirstShots = 0, HeadsUp = 1, OnTheMove = 2, NotGranny = 3, ThrowIt = 4, SwatBack = 5, Gauntlet = 6;
    private const int ShotsPerGun = 3;
    private const float HeadLine = 1.47f; // (on the cutout, model units: above it is the head)

    private static int _course, _score, _sent;
    private static bool _running;
    private static float _start = -1f, _nextEvent, _result;
    private static string _last = "";
    private static TextMeshPro _courseBoard, _courseName, _medalWall, _gripLabel;
    private static readonly Dictionary<string, float> CourseBest = new Dictionary<string, float>();
    private static readonly float[] LaneWas = new float[3];
    private static string MedalFile => Path.Combine(BepInEx.Paths.BepInExRootPath, "AngerFootVR", "range-medals.txt");

    // ================================================================================== the cutouts

    /// <summary>A cutout goon (or granny) that pops up, can run across the range, can lob a grenade.</summary>
    private sealed class Popper : MonoBehaviour
    {
        public bool Granny;
        public float UpAt = -1f, HitAt = -1f;
        public float StayFor = -1f; // (seconds up before it drops by itself; -1: until it's hit)
        public Vector3 From, To;
        public float MoveFor; // (a runner: From to To while it's up)
        public float LobAt = -1f; // (when it throws its grenade)
        public MeshFilter Look;
        public MeshRenderer LookR;

        public bool Up => UpAt > 0f && HitAt < 0f;

        /// <summary>Hit: by a shot (where), a thrown gun or a blast.</summary>
        public void Hit(Vector3 at, string how = "shot")
        {
            if (UpAt < 0f || HitAt > 0f) return;
            HitAt = Time.unscaledTime;
            var head = how == "shot" && transform.InverseTransformPoint(at).y > HeadLine;
            GameSounds.PlayOr(Granny ? "Voice female 2" : "Body impacts", "thud", at, 0.8f);
            Scored(this, at, how, head);
        }
    }

    private static Mesh _goonMesh, _goon2Mesh, _grannyMesh;
    private static Material[] _goonMats, _goon2Mats, _grannyMats;

    private static void Cutouts()
    {
        var goon = Props.Get("cutout_goon");
        var granny = Props.Get("cutout_granny");
        var goon2 = Props.Get("cutout_goon2") ?? goon;
        if (goon == null || granny == null) return;
        _goon2Mesh = goon2.Mesh;
        _goon2Mats = Props.MaterialsOf(goon2);
        _goonMesh = goon.Mesh;
        _goonMats = Props.MaterialsOf(goon);
        _grannyMesh = granny.Mesh;
        _grannyMats = Props.MaterialsOf(granny);
        for (var k = 0; k < 8; k++)
        {
            var go = new GameObject("Popper " + k) { layer = FunWorld.PhysLayer };
            go.transform.SetParent(_root, false);
            go.transform.localScale = Vector3.one * 0.8f;
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.9f, 0f);
            box.size = new Vector3(0.62f, 1.8f, 0.04f);
            go.AddComponent<Rigidbody>().isKinematic = true;
            var vis = new GameObject("Visual") { layer = VrUi.Layer };
            vis.transform.SetParent(go.transform, false);
            var p = go.AddComponent<Popper>();
            p.Look = vis.AddComponent<MeshFilter>();
            p.LookR = vis.AddComponent<MeshRenderer>();
            Poppers.Add(p);
            Dress(p, false);
            Park(p, k);
        }
    }

    private static void Dress(Popper p, bool granny)
    {
        p.Granny = granny;
        var two = Poppers.IndexOf(p) % 2 == 1; // (pink goons and green ones)
        p.Look.sharedMesh = granny ? _grannyMesh : two ? _goon2Mesh : _goonMesh;
        p.LookR.sharedMaterials = granny ? _grannyMats : two ? _goon2Mats : _goonMats;
    }

    /// <summary>Down, flat, at its place when no course is on (spread down the range).</summary>
    private static void Park(Popper p, int k)
    {
        var lo = MovieHouse.RangeMin;
        var hi = MovieHouse.RangeMax;
        p.UpAt = p.HitAt = p.LobAt = -1f;
        p.MoveFor = 0f;
        p.transform.localPosition = new Vector3(Mathf.Lerp(lo.x + 0.6f, hi.x - 0.6f, (k * 0.37f) % 1f), 0f, MovieHouse.GunRangeLine + 4f + k * 1.3f);
    }

    /// <summary>Up it comes, here (range local), for this long (-1: till hit).</summary>
    private static Popper PopUp(Vector3 at, float stay, bool granny = false)
    {
        var down = Poppers.FindAll(p => p.UpAt < 0f);
        if (down.Count == 0) return null;
        var p = down[Random.Range(0, down.Count)];
        Dress(p, granny);
        p.transform.localPosition = at;
        p.UpAt = Time.unscaledTime;
        p.HitAt = p.LobAt = -1f;
        p.StayFor = stay;
        p.MoveFor = 0f;
        GameSounds.Play("Door Impacts", FunWorld.World(at), 0.4f);
        return p;
    }

    /// <summary>A spot down the range this far from the line (x across it at random, clear of its walls).</summary>
    private static Vector3 Spot(float near, float far)
    {
        var lo = MovieHouse.RangeMin;
        var hi = MovieHouse.RangeMax;
        return new Vector3(Random.Range(lo.x + 0.7f, hi.x - 0.7f), 0f, MovieHouse.GunRangeLine + Random.Range(near, far));
    }

    private static Popper Runner(float dist, float time)
    {
        var lo = MovieHouse.RangeMin;
        var hi = MovieHouse.RangeMax;
        var left = Random.value < 0.5f;
        var z = MovieHouse.GunRangeLine + dist;
        var from = new Vector3(left ? lo.x + 0.4f : hi.x - 0.4f, 0f, z);
        var p = PopUp(from, time);
        if (p == null) return null;
        p.From = from;
        p.To = new Vector3(left ? hi.x - 0.4f : lo.x + 0.4f, 0f, z);
        p.MoveFor = time;
        return p;
    }

    private static void Animate(float now)
    {
        foreach (var p in Poppers)
        {
            var up = 0f;
            if (p.UpAt > 0f)
            {
                var age = now - p.UpAt;
                if (p.HitAt < 0f && p.StayFor > 0f && age > p.StayFor)
                {
                    p.HitAt = now; // (gone again, missed)
                    if (_running && !p.Granny && _course != FirstShots && _course != ThrowIt) _last = "MISSED ONE";
                }

                up = p.HitAt > 0f ? 1f - Mathf.Clamp01((now - p.HitAt) * 6f) : Mathf.Clamp01(age * 6f);
                if (p.HitAt > 0f && now - p.HitAt > 0.3f) p.UpAt = -1f;
                if (p.MoveFor > 0f && p.HitAt < 0f) p.transform.localPosition = Vector3.Lerp(p.From, p.To, Mathf.Clamp01(age / p.MoveFor));
                if (p.LobAt > 0f && p.HitAt < 0f && now >= p.LobAt)
                {
                    p.LobAt = -1f;
                    Lob(p);
                }
            }

            p.transform.localRotation = Quaternion.Euler(-90f * (1f - up), 180f, 0f);
        }
    }

    // ================================================================================== grenades lobbed at you

    private sealed class Lobbed
    {
        public Transform T;
        public Vector3 From, Vel; // (range local: its path from where it was thrown)
        public float Born, Fuse, Flight;
        public bool Swatted;
        public Popper By;
    }

    private static readonly List<Lobbed> Lobs = new List<Lobbed>();
    private static readonly Vector3 Gravity = new Vector3(0f, -9.81f, 0f);

    /// <summary>The goon throws: an arc over the bench that would land on your chest.</summary>
    private static void Lob(Popper by)
    {
        var from = by.transform.localPosition + new Vector3(0f, 1.6f, 0f);
        var head = FunWorld.Local(CinemaRig.HeadPosition);
        var flat = from - head;
        flat.y = 0f;
        var target = head + new Vector3(0f, -0.4f, 0f) + flat.normalized * 0.5f;
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Lobbed grenade";
        go.layer = FunWorld.PhysLayer; // (a shot can hit it)
        go.transform.SetParent(_root, false);
        go.transform.localPosition = from;
        go.transform.localScale = Vector3.one * 0.13f;
        go.GetComponent<MeshRenderer>().sharedMaterial = Props.Mat("steel_dark");
        go.GetComponent<Collider>().isTrigger = true; // (shots find it; hands and things pass through)
        var vis = FunWorld.Picture("kickball", go.transform, Vector3.zero, Quaternion.identity);
        if (vis != null)
        {
            go.GetComponent<MeshRenderer>().enabled = false;
            foreach (var r in vis.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = Props.Mat("steel_dark");
            vis.transform.localScale = Vector3.one * 2.6f;
        }

        // its fuse: a red light on top, so you see it coming
        var light = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(light.GetComponent<Collider>());
        light.name = "Fuse";
        light.layer = VrUi.Layer;
        light.transform.SetParent(go.transform, false);
        light.transform.localPosition = new Vector3(0f, 0.55f, 0f);
        light.transform.localScale = Vector3.one * 0.45f;
        light.GetComponent<MeshRenderer>().sharedMaterial = Props.Mat("endo_eye");

        const float flight = 1.3f;
        Lobs.Add(new Lobbed { T = go.transform, From = from, Vel = (target - from) / flight - 0.5f * Gravity * flight, Born = Time.unscaledTime, Fuse = 2.6f, Flight = flight, By = by });
        GameSounds.PlayOr("Grenade Throw", "kick", FunWorld.World(from), 0.7f);
    }

    private static void Lobbing(float now)
    {
        for (var i = Lobs.Count - 1; i >= 0; i--)
        {
            var g = Lobs[i];
            if (g.T == null)
            {
                Lobs.RemoveAt(i);
                continue;
            }

            var t = now - g.Born;
            var at = g.From + g.Vel * t + 0.5f * Gravity * t * t;
            if (at.y < 0.07f) at.y = 0.07f; // (it lands and lies there, fizzing)
            g.T.localPosition = at;
            g.T.Rotate(400f * Time.unscaledDeltaTime, 260f * Time.unscaledDeltaTime, 0f, Space.Self);
            if (!g.Swatted)
                foreach (var hand in new[] { XRNode.LeftHand, XRNode.RightHand })
                {
                    var pose = VrHands.Get(hand);
                    if (!pose.Tracked || pose.Velocity.magnitude < 1.6f || (pose.Position - g.T.position).magnitude > 0.32f) continue;
                    Swat(g, now, hand);
                    break;
                }

            if (t >= g.Fuse) Blow(g, i);
        }
    }

    /// <summary>Hit back: away to the goon that threw it (or the nearest one up), going off when it gets there.</summary>
    private static void Swat(Lobbed g, float now, XRNode? hand)
    {
        var to = g.By != null && g.By.Up ? g.By : Poppers.Find(p => p.Up && !p.Granny);
        var aim = to != null ? to.transform.localPosition + new Vector3(0f, 0.9f, 0f) : g.T.localPosition + new Vector3(0f, 1.5f, 9f);
        const float flight = 0.75f;
        g.Swatted = true;
        g.From = g.T.localPosition;
        g.Born = now;
        g.Vel = (aim - g.From) / flight - 0.5f * Gravity * flight;
        g.Fuse = flight;
        if (hand.HasValue) VrControls.Rumble(hand.Value, 0.9f, 0.12f);
        GameSounds.PlayOr("Kick Impacts", "kick", g.T.position, 0.9f);
        Pop(g.T.position, "SWAT!", new Color(0.15f, 0.95f, 1f));
    }

    private static void Blow(Lobbed g, int i)
    {
        Lobs.RemoveAt(i);
        var at = g.T.position;
        Object.Destroy(g.T.gameObject);
        if (!g.Swatted && g.By != null && g.By.Up) g.By.HitAt = Time.unscaledTime; // (its job done: down it goes, no points)
        if (!g.Swatted && (at - CinemaRig.HeadPosition).magnitude < 3f)
        {
            // (no harm done here, but in a level that was you)
            Fx.Explosion(at, 0.8f);
            GameSounds.Play("Grenade explosion", at, 0.8f);
            VrControls.Rumble(XRNode.LeftHand, 0.8f, 0.25f);
            VrControls.Rumble(XRNode.RightHand, 0.8f, 0.25f);
            Pop(CinemaRig.HeadPosition + CinemaRig.HeadRotation * Vector3.forward * 1.5f, "BOOM! TOO SLOW", new Color(1f, 0.2f, 0.3f));
            if (_running) _last = "SWAT IT BEFORE IT GOES OFF";
            return;
        }

        Explode(at, 2.2f);
    }

    /// <summary>A shot that hit a lobbed grenade: it goes off right there.</summary>
    private static bool ShotGrenade(Collider c)
    {
        for (var i = 0; i < Lobs.Count; i++)
        {
            if (Lobs[i].T == null || c.transform != Lobs[i].T) continue;
            Lobs[i].Swatted = true; // (away from you: it counts)
            Pop(Lobs[i].T.position, "SHOT IT!", new Color(0.15f, 0.95f, 1f));
            Blow(Lobs[i], i);
            return true;
        }

        return false;
    }

    /// <summary>A thrown gun that hits a cutout knocks it down.</summary>
    private sealed class ThrowHit : MonoBehaviour
    {
        public Gun Gun;

        private void OnCollisionEnter(Collision c)
        {
            if (Gun == null || Gun.Grab == null || Gun.Grab.Busy || c.relativeVelocity.magnitude < 2.2f) return;
            if (c.collider.GetComponentInParent<Popper>() is { } p) p.Hit(c.contacts.Length > 0 ? c.contacts[0].point : p.transform.position, "thrown");
        }
    }

    // ================================================================================== the courses

    private static void CoursePanel()
    {
        var lo = MovieHouse.RangeMin;
        var hi = MovieHouse.RangeMax;
        var line = MovieHouse.GunRangeLine;
        Cutouts();

        // on the bench at the left: < the course > and START
        var panel = new Vector3((lo.x + hi.x) / 2 - 1.05f, 1.0f, line + 0.1f);
        var pink = new Color(1f, 0.16f, 0.62f);
        Button("Course start", panel, "START", pink, () => StartCourse());
        foreach (var dir in new[] { -1, 1 })
        {
            // (facing down the range, +x is to your right)
            var at = panel + new Vector3(dir * 0.3f, 0f, 0f);
            var d = dir;
            Button(dir < 0 ? "Course back" : "Course next", at, dir < 0 ? "<" : ">", pink, () => Choose(d));
        }

        // GUN GRIP ADJUST on and off, between the second and third lanes' buttons
        _gripLabel = Button("Grip adjust", new Vector3((lo.x + hi.x) / 2 + 1.07f, 1.0f, line + 0.1f), "GRIP ADJUST", new Color(0.15f, 0.95f, 1f), () =>
        {
            if (Plugin.DevRun) Input.GripAdjust.DevOn = !Input.GripAdjust.DevOn; // (a dev run leaves the settings file alone)
            else if (VrSettings.GripAdjust != null) VrSettings.GripAdjust.Value = !VrSettings.GripAdjust.Value;
            GameSounds.PlayOr("UI Click", "step", CinemaRig.HeadPosition, 0.6f);
            ShowCourse();
        });
        if (_gripLabel != null) _gripLabel.rectTransform.sizeDelta = new Vector2(0.62f, 0.2f);

        _courseName = MovieHouse.SignFacing("", panel + new Vector3(0f, 0.62f, 0.05f), 0.08f, new Color(1f, 0.85f, 0.25f), Vector3.back, _root);
        if (_courseName != null) _courseName.rectTransform.sizeDelta = new Vector2(1.0f, 0.2f);

        // the board over the range: what to do, what each medal takes, how it's going
        _courseBoard = MovieHouse.SignFacing("", new Vector3((lo.x + hi.x) / 2, 2.75f, line + 0.6f), 0.12f, pink, Vector3.back, _root);
        if (_courseBoard != null)
        {
            _courseBoard.rectTransform.sizeDelta = new Vector2(5.6f, 1.3f);
            _courseBoard.enableWordWrapping = true;
        }

        // the medals you've won, on the wall at your left
        _medalWall = MovieHouse.SignFacing("", new Vector3(lo.x + 0.06f, 1.75f, -3.9f), 0.075f, new Color(0.9f, 0.9f, 0.9f), Vector3.right, _root);
        if (_medalWall != null)
        {
            _medalWall.rectTransform.sizeDelta = new Vector2(1.9f, 1.4f);
            _medalWall.alignment = TextAlignmentOptions.Left;
        }

        LoadMedals();
        ShowCourse();
    }

    private static TextMeshPro Button(string name, Vector3 at, string label, Color c, System.Action press)
    {
        var b = new GameObject(name) { layer = FunWorld.PhysLayer };
        b.transform.SetParent(_root, false);
        b.transform.localPosition = at;
        b.AddComponent<BoxCollider>().size = new Vector3(0.14f, 0.06f, 0.14f);
        var cap = FunWorld.Picture("rage_button_cap", _root, at - new Vector3(0f, 0.03f, 0f), Quaternion.identity);
        if (cap != null) cap.transform.localScale = Vector3.one * (label.Length > 1 ? 0.7f : 0.5f);
        var text = MovieHouse.SignFacing(label, at + new Vector3(0f, 0.12f, 0.05f), label.Length > 1 ? 0.08f : 0.12f, c, Vector3.back, _root);
        FunWorld.MakePress(b, h => press(), 0.09f);
        return text;
    }

    private static void Choose(int dir)
    {
        if (_running) return;
        _course = (_course + dir + Courses.Length) % Courses.Length;
        _last = "";
        GameSounds.PlayOr("UI Click", "step", CinemaRig.HeadPosition, 0.6f);
        ShowCourse();
    }

    private static void StartCourse()
    {
        if (_running)
        {
            Finish(false, "STOPPED");
            return;
        }

        _running = true;
        _start = Time.unscaledTime + 1.5f; // (READY... then GO)
        _nextEvent = _start;
        _score = _sent = 0;
        _result = 0f;
        _last = "";
        foreach (var p in Poppers)
        {
            p.UpAt = p.HitAt = p.LobAt = -1f;
            p.MoveFor = 0f;
        }

        foreach (var g in Guns) g.Shots = 0;
        // the paper lanes run back out of the way
        for (var k = 0; k < Lanes.Count && k < LaneWas.Length; k++)
        {
            LaneWas[k] = Lanes[k].Distance;
            Lanes[k].Distance = 20f;
            ShowLane(Lanes[k], "");
        }

        if (_course == ThrowIt)
        {
            // all eight up at once, close (for throwing)
            var lo = MovieHouse.RangeMin;
            var hi = MovieHouse.RangeMax;
            for (var k = 0; k < 8; k++)
            {
                var p = Poppers[k];
                Dress(p, false);
                p.transform.localPosition = new Vector3(Mathf.Lerp(lo.x + 0.8f, hi.x - 0.8f, k / 7f), 0f, MovieHouse.GunRangeLine + 2.6f + (k % 3) * 1.4f);
            }
        }

        GameSounds.PlayOr("World Map Start Level", "door_bang", CinemaRig.HeadPosition, 0.8f);
        Plog($"Range course {Courses[_course].Name} started");
    }

    private static void Finish(bool done, string why = "")
    {
        if (!_running) return;
        _running = false;
        var c = Courses[_course];
        var t = Time.unscaledTime - _start;
        if (c.Timed) _result = done ? t : -1f;
        else _result = Mathf.Max(0, _score);
        foreach (var p in Poppers)
            if (p.UpAt > 0f && p.HitAt < 0f)
                p.HitAt = Time.unscaledTime;
        for (var k = 0; k < Lanes.Count && k < LaneWas.Length; k++)
        {
            Lanes[k].Distance = LaneWas[k];
            ShowLane(Lanes[k], "");
        }
        var medal = Medal(c, _result);
        var key = c.Name;
        var better = why != "STOPPED" && _result >= 0f && (!CourseBest.TryGetValue(key, out var best) || (c.Timed ? _result < best : _result > best));
        if (better && (c.Timed ? _result <= c.Bronze : _result >= c.Bronze))
        {
            CourseBest[key] = _result;
            SaveMedals();
        }

        _last = why.Length > 0 ? why : medal != null ? $"{medal}! {Shown(c, _result)}" + (better ? " (YOUR BEST)" : "") : _result < 0f ? "OUT OF TIME" : $"{Shown(c, _result)}: NO MEDAL YET";
        Pop(CinemaRig.HeadPosition + CinemaRig.HeadRotation * Vector3.forward * 2f, medal ?? (why.Length > 0 ? why : "TRY AGAIN"), MedalColour(medal));
        if (medal != null) GameSounds.PlayOr("UI Star Goal Completed HUD", "door_bang", CinemaRig.HeadPosition, 1f);
        Plog($"Range course {c.Name}: {(_result < 0f ? "out of time" : Shown(c, _result))} {medal ?? "no medal"}{(why.Length > 0 ? " (" + why + ")" : "")}");
        ShowCourse();
    }

    private static string Medal(Course c, float r)
    {
        if (r < 0f) return null;
        if (c.Timed) return r <= c.Gold ? "GOLD" : r <= c.Silver ? "SILVER" : r <= c.Bronze ? "BRONZE" : null;
        return r >= c.Gold ? "GOLD" : r >= c.Silver ? "SILVER" : r >= c.Bronze ? "BRONZE" : null;
    }

    private static Color MedalColour(string medal) => medal switch
    {
        "GOLD" => new Color(1f, 0.8f, 0.2f),
        "SILVER" => new Color(0.82f, 0.85f, 0.9f),
        "BRONZE" => new Color(0.85f, 0.5f, 0.25f),
        _ => new Color(1f, 0.2f, 0.3f),
    };

    private static string Shown(Course c, float r) => c.Timed ? $"{r:F1} S" : c.Gold >= 100f ? $"{r:F0} POINTS" : $"{r:F0}";

    /// <summary>A cutout hit while a course is on: the course's own scoring.</summary>
    private static void Scored(Popper p, Vector3 at, string how, bool head)
    {
        if (!_running || Time.unscaledTime < _start) return;
        var gold = new Color(1f, 0.85f, 0.25f);
        var red = new Color(1f, 0.2f, 0.3f);
        switch (_course)
        {
            case HeadsUp:
                if (head)
                {
                    _score++;
                    Pop(at, "HEADSHOT!", gold);
                }
                else Pop(at, "BODY: AIM HIGHER", red);

                break;
            case NotGranny:
            case Gauntlet:
                var pts = p.Granny ? -300 : how == "thrown" ? 300 : how == "blast" ? 250 : head ? 200 : p.MoveFor > 0f ? 150 : 100;
                _score += pts;
                Pop(at, p.Granny ? "NOT GRANNY! -300" : how == "thrown" ? $"THROWN! +{pts}" : how == "blast" ? $"BLOWN UP +{pts}" : head ? $"HEADSHOT +{pts}" : $"+{pts}", p.Granny ? red : gold);
                break;
            case SwatBack:
                if (how == "blast")
                {
                    _score++;
                    Pop(at, "RIGHT BACK AT HIM!", gold);
                }
                else Pop(at, "SWAT THE GRENADE BACK AT HIM", red);

                break;
            case ThrowIt:
                _score++;
                Pop(at, how == "thrown" ? "THROWN! DOWN" : "DOWN", gold);
                break;
            default:
                _score++;
                Pop(at, head ? "HEADSHOT!" : "HIT!", gold);
                break;
        }

        if (_course == FirstShots) _nextEvent = Time.unscaledTime + 0.35f;
    }

    private static void CourseTick()
    {
        var now = Time.unscaledTime;
        Animate(now);
        Lobbing(now);
        if (!_running)
        {
            if (Time.frameCount % 30 == 0) ShowCourse();
            return;
        }

        var c = Courses[_course];
        var t = now - _start;
        if (t >= 0f)
        {
            var anyUp = Poppers.Exists(p => p.UpAt > 0f);
            switch (_course)
            {
                case FirstShots:
                    // one at a time, further each time; it stays till you hit it
                    if (_score >= 6) Finish(true);
                    else if (!anyUp && _sent < 6 && now >= _nextEvent)
                    {
                        PopUp(Spot(3.5f + _sent * 1.6f, 4.5f + _sent * 1.6f), -1f);
                        _sent++;
                    }

                    break;
                case HeadsUp:
                    if (!anyUp && _sent >= 8) Finish(true);
                    else if (!anyUp && _sent < 8 && now >= _nextEvent)
                    {
                        PopUp(Spot(4f, 11f), 2.4f);
                        _sent++;
                        _nextEvent = now + 0.9f;
                    }

                    break;
                case OnTheMove:
                    if (!anyUp && _sent >= 8) Finish(true);
                    else if (_sent < 8 && now >= _nextEvent)
                    {
                        Runner(Random.Range(5f, 12f), 3.4f - _sent * 0.12f);
                        _sent++;
                        _nextEvent = now + 1.6f;
                    }

                    break;
                case NotGranny:
                    if (now >= _nextEvent)
                    {
                        PopUp(Spot(4f, 12f), 1.3f, Random.value < 0.3f);
                        _nextEvent = now + Random.Range(0.7f, 1.1f);
                    }

                    break;
                case ThrowIt:
                    if (_sent == 0)
                    {
                        foreach (var p in Poppers)
                        {
                            p.UpAt = now;
                            p.HitAt = -1f;
                            p.StayFor = -1f;
                        }

                        _sent = 8;
                    }
                    else if (_score >= 8) Finish(true);

                    break;
                case SwatBack:
                    if (_sent >= 6 && !anyUp && Lobs.Count == 0) Finish(true);
                    else if (_sent < 6 && now >= _nextEvent && Lobs.Count == 0)
                    {
                        var p = PopUp(Spot(6f, 10f), 7f);
                        if (p != null) p.LobAt = now + 0.7f;
                        _sent++;
                        _nextEvent = now + 3.2f;
                    }

                    break;
                case Gauntlet:
                    if (now >= _nextEvent)
                    {
                        var roll = Random.value;
                        if (roll < 0.42f) PopUp(Spot(4f, 12f), 1.7f);
                        else if (roll < 0.6f) PopUp(Spot(4f, 12f), 1.5f, true);
                        else if (roll < 0.85f) Runner(Random.Range(5f, 12f), 3f);
                        else if (Lobs.Count == 0)
                        {
                            var p = PopUp(Spot(6f, 10f), 5f);
                            if (p != null) p.LobAt = now + 0.7f;
                        }

                        _nextEvent = now + Random.Range(0.8f, 1.2f);
                    }

                    break;
            }
        }

        if (_running && t >= c.Limit) Finish(!c.Timed);
        if (Time.frameCount % 6 == 0) ShowCourse();
    }

    /// <summary>The empty gun's click (the throwing course): true when it can't fire.</summary>
    private static bool Empty(Gun g)
    {
        if (!_running || _course != ThrowIt || Time.unscaledTime < _start) return false;
        if (g.Shots < ShotsPerGun)
        {
            g.Shots++;
            return false;
        }

        GameSounds.PlayOr("UI Back", "step", g.Grab.transform.position, 0.5f);
        if (Time.frameCount % 2 == 0) Pop(g.Grab.transform.position, "EMPTY: THROW IT!", new Color(1f, 0.2f, 0.3f));
        return true;
    }

    private static void ShowCourse()
    {
        if (_gripLabel != null) _gripLabel.text = Input.GripAdjust.On ? "GRIP ADJUST: ON" : "GRIP ADJUST: OFF";
        if (_courseBoard == null) return;
        var c = Courses[_course];
        if (_courseName != null) _courseName.text = $"{_course + 1}/{Courses.Length}  {c.Name}";
        var goals = c.Timed ? $"GOLD {c.Gold:F0} S   SILVER {c.Silver:F0} S   BRONZE {c.Bronze:F0} S" : $"GOLD {c.Gold:F0}   SILVER {c.Silver:F0}   BRONZE {c.Bronze:F0}";
        var best = CourseBest.TryGetValue(c.Name, out var b) ? $"YOUR BEST {Shown(c, b)} ({Medal(c, b)})" : "NO MEDAL YET";
        if (_running)
        {
            var t = Time.unscaledTime - _start;
            var shown = c.Timed ? $"TIME {Mathf.Max(0f, t):F1}" : $"TIME {Mathf.CeilToInt(Mathf.Max(0f, c.Limit - Mathf.Max(0f, t)))}";
            var score = _course switch { FirstShots => $"HIT {_score}/6", ThrowIt => $"DOWN {_score}/8", HeadsUp => $"HEADS {_score}", OnTheMove => $"HITS {_score}", SwatBack => $"BLOWN BACK {_score}", _ => $"SCORE {_score}" };
            _courseBoard.text = t < 0f ? $"{c.Name}\nREADY..." : $"{c.Name}   {shown}   {score}\n{(_last.Length > 0 ? _last : c.Tip)}";
        }
        else _courseBoard.text = $"TRAINING: {c.Name}\n{c.Tip}\n{goals}\n{(_last.Length > 0 ? _last : best)}";
    }

    private static void ShowMedals()
    {
        if (_medalWall == null) return;
        var lines = new List<string> { "<color=#FF29A0>TRAINING MEDALS</color>" };
        foreach (var c in Courses)
        {
            var m = CourseBest.TryGetValue(c.Name, out var b) ? Medal(c, b) : null;
            var col = m != null ? "#" + ColorUtility.ToHtmlStringRGB(MedalColour(m)) : "#777777";
            lines.Add($"<color={col}>{(m ?? "-")}</color>  {c.Name}{(m != null ? "  " + Shown(c, b) : "")}");
        }

        _medalWall.text = string.Join("\n", lines);
    }

    private static void LoadMedals()
    {
        CourseBest.Clear();
        try
        {
            if (File.Exists(MedalFile))
                foreach (var line in File.ReadAllLines(MedalFile))
                {
                    var i = line.LastIndexOf('=');
                    if (i > 0 && float.TryParse(line.Substring(i + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) CourseBest[line.Substring(0, i)] = v;
                }
        }
        catch (System.Exception e)
        {
            Plugin.L.LogWarning("Range medals: " + e.Message);
        }

        ShowMedals();
    }

    private static void SaveMedals()
    {
        ShowMedals();
        if (Plugin.DevRun) return; // (a dev run keeps them for now only)
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MedalFile));
            var lines = new List<string>();
            foreach (var kv in CourseBest) lines.Add(kv.Key + "=" + kv.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            File.WriteAllLines(MedalFile, lines);
        }
        catch (System.Exception e)
        {
            Plugin.L.LogWarning("Range medals: " + e.Message);
        }
    }

    private static void ClearCourses()
    {
        _running = false;
        _start = -1f;
        _courseBoard = _courseName = _medalWall = _gripLabel = null;
        Lobs.Clear();
    }

    /// <summary>Dev: run a course (by number), or hit every cutout that's up ("hit"), or swat the grenades in the air.</summary>
    public static string DevCourse(string arg)
    {
        if (arg == "hit")
        {
            foreach (var p in Poppers)
                if (p.Up && !p.Granny)
                    p.Hit(p.transform.TransformPoint(new Vector3(0f, 1.6f, 0f)));
        }
        else if (arg == "swat")
        {
            foreach (var g in Lobs)
                if (!g.Swatted)
                    Swat(g, Time.unscaledTime, null);
        }
        else if (int.TryParse(arg, out var k) && k >= 1 && k <= Courses.Length)
        {
            if (_running) Finish(false, "STOPPED");
            _course = k - 1;
            StartCourse();
        }

        return $"COURSE {Courses[_course].Name} running {_running} t {Time.unscaledTime - _start:F1} score {_score} sent {_sent} up {Poppers.FindAll(p => p.Up).Count} lobs {Lobs.Count} last '{_last}' | best: {string.Join(", ", new List<string>(System.Linq.Enumerable.Select(CourseBest, kv => kv.Key + "=" + kv.Value)).ToArray())}";
    }
}
