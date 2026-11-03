using System.Collections.Generic;
using System.IO;
using AngerFootVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.UI;

/// <summary>
/// The arcade behind the movie house's back wall: five games made for Anger Foot, each with its high score on the
/// board by the door (kept in the mod's own folder, never your save).
/// - DOOR KICKER: five doors; one knocks, then bursts open on a goon. Kick a knocking door before it opens (DOOR
///   KICK!), or kick (punch, bat) the goon behind an open one. Never the granny. 45 seconds.
/// - GOON GALLERY: a carnival shooting gallery. The light gun's on its cable at the counter (grip it, the trigger
///   shoots); goons ride the rails, pizza slices are worth more. Shoot the START bell (or press the counter's
///   button) for a 40 second round.
/// - KICK-O-METER: how angry are you? A light runs up and down the column: kick the pad when it's at the top (or
///   punch it for real: as hard as you can). MILDLY ANNOYED to ANGER FOOT!!
/// - SHOE TOSS: skee-ball with shoes (pairs, laced together, from the game's own): throw them up the lane into the
///   rings, 10 to 100 a hole, nine throws a game.
/// - GOON BOWLING: kick the ball (or throw it) down the lane at ten goon pins. Five frames, strikes and spares.
/// </summary>
internal static class Arcade
{
    private static Transform _root;
    private static bool _built;
    private static readonly Dictionary<string, int> Best = new Dictionary<string, int>();
    private static TextMeshPro _scores;
    private static string File => Path.Combine(BepInEx.Paths.BepInExRootPath, "AngerFootVR", "arcade-scores.txt");

    public static void Init()
    {
        FunWorld.Built += Build;
        FunWorld.Tick += Tick;
        FunWorld.BeforeStep += Step;
        FunWorld.Kicked += Kicked;
        FunWorld.Clearing += Clear;
    }

    private static void Build()
    {
        _root = FunWorld.Root;
        _built = true;
        LoadScores();
        var lo = MovieHouse.ArcadeMin;
        var hi = MovieHouse.ArcadeMax;
        var mid = (lo + hi) / 2;
        FunWorld.Solid(new Vector3(mid.x, -0.25f, mid.z), new Vector3(hi.x - lo.x + 1f, 0.5f, hi.z - lo.z + 1f));
        FunWorld.Solid(new Vector3(lo.x - 0.25f, 2.25f, mid.z), new Vector3(0.5f, 4.5f, hi.z - lo.z));
        FunWorld.Solid(new Vector3(hi.x + 0.25f, 2.25f, mid.z), new Vector3(0.5f, 4.5f, hi.z - lo.z));
        FunWorld.Solid(new Vector3(mid.x, 2.25f, lo.z - 0.25f), new Vector3(hi.x - lo.x, 4.5f, 0.5f));
        FunWorld.Solid(new Vector3(mid.x, 4.75f, mid.z), new Vector3(hi.x - lo.x, 0.5f, hi.z - lo.z));
        FunWorld.Solid(new Vector3((lo.x + MovieHouse.ArcadeDoorX0) / 2, 2.25f, hi.z + 0.2f), new Vector3(MovieHouse.ArcadeDoorX0 - lo.x, 4.5f, 0.4f));
        FunWorld.Solid(new Vector3((MovieHouse.ArcadeDoorX1 + hi.x) / 2, 2.25f, hi.z + 0.2f), new Vector3(hi.x - MovieHouse.ArcadeDoorX1, 4.5f, 0.4f));
        _scores = MovieHouse.SignFacing("", new Vector3(-9.3f, 2.5f, hi.z - 0.1f), 0.2f, new Color(1f, 0.85f, 0.25f), Vector3.back, _root);
        if (_scores != null) _scores.rectTransform.sizeDelta = new Vector2(3.4f, 2f);
        FunWorld.Picture("score_board", _root, new Vector3(-9.3f, 2.5f, hi.z - 0.04f), Quaternion.Euler(0f, 180f, 0f));
        MovieHouse.SignFacing("HIGH SCORES", new Vector3(-9.3f, 3.3f, hi.z - 0.1f), 0.32f, new Color(1f, 0.16f, 0.62f), Vector3.back, _root);
        FunWorld.Soon("door kicker", DoorKicker.Build);
        FunWorld.Soon("gallery", Gallery.Build);
        FunWorld.Soon("kick-o-meter", Meter.Build);
        FunWorld.Soon("shoe toss", ShoeToss.Build);
        FunWorld.Soon("bowling", Bowling.Build);
        ShowScores();
    }

    private static void Tick()
    {
        if (!_built) return;
        Hands();
        DoorKicker.Tick();
        Gallery.Tick();
        Meter.Tick();
        ShoeToss.Tick();
        Bowling.Tick();
    }

    private static void Step(float dt)
    {
        if (!_built) return;
        Gallery.Step(dt);
    }

    private static void Kicked(Vector3 foot, Vector3 facing)
    {
        if (!_built) return;
        DoorKicker.Kick(foot, facing);
        Meter.Kick(foot, facing);
        Bowling.Kick(foot, facing);
    }

    // ---------------------------------------------------------------------------------- shared bits

    private static Vector3 L(Vector3 world) => _root.InverseTransformPoint(world);

    private static TextMeshPro Marquee(Vector3 local, Vector3 facing, float size, Color c, string text = "")
    {
        var t = MovieHouse.SignFacing(text, local, size, c, facing, _root);
        if (t != null) t.rectTransform.sizeDelta = new Vector2(3.2f, 1f);
        return t;
    }

    private static void Score(string game, int score)
    {
        if (!Best.TryGetValue(game, out var b) || score > b)
        {
            Best[game] = score;
            SaveScores();
            GameSounds.PlayOr("UI Star Goal Completed HUD", "door_bang", CinemaRig.HeadPosition, 1f);
        }

        ShowScores();
    }

    private static void ShowScores()
    {
        if (_scores == null) return;
        string Line(string game) => $"{game}  {(Best.TryGetValue(game, out var b) ? b.ToString() : "-")}";
        _scores.text = string.Join("\n", Line("DOOR KICKER"), Line("GOON GALLERY"), Line("KICK-O-METER"), Line("SHOE TOSS"), Line("GOON BOWLING"));
    }

    private static void LoadScores()
    {
        Best.Clear();
        try
        {
            if (!System.IO.File.Exists(File)) return;
            foreach (var line in System.IO.File.ReadAllLines(File))
            {
                var i = line.LastIndexOf('=');
                if (i > 0 && int.TryParse(line.Substring(i + 1), out var v)) Best[line.Substring(0, i)] = v;
            }
        }
        catch (System.Exception e)
        {
            Plugin.L.LogWarning("Arcade scores: " + e.Message);
        }
    }

    private static void SaveScores()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File));
            var lines = new List<string>();
            foreach (var kv in Best) lines.Add(kv.Key + "=" + kv.Value);
            System.IO.File.WriteAllLines(File, lines);
        }
        catch (System.Exception e)
        {
            Plugin.L.LogWarning("Arcade scores: " + e.Message);
        }
    }

    private static void Pop(Vector3 world, string text, Color c)
    {
        var face = CinemaRig.HeadPosition - world;
        face.y = 0f;
        var t = MovieHouse.SignFacing(text, L(world), 0.22f, c, face.sqrMagnitude > 1e-4f ? face.normalized : Vector3.back, _root);
        if (t != null) t.gameObject.AddComponent<Rise>();
    }

    private sealed class Rise : MonoBehaviour
    {
        private float _born;
        private Vector3 _at;

        private void Start()
        {
            _born = Time.unscaledTime;
            _at = transform.localPosition;
        }

        private void Update()
        {
            var age = Time.unscaledTime - _born;
            transform.localPosition = _at + Vector3.up * age * 0.45f;
            var t = GetComponent<TextMeshPro>();
            if (t != null) t.alpha = Mathf.Clamp01((1.1f - age) / 0.4f);
            if (age > 1.1f) Destroy(gameObject);
        }
    }

    private static GameObject Button(Vector3 local, System.Action pressed)
    {
        FunWorld.Picture("rage_button", _root, local, Quaternion.identity).transform.localScale = new Vector3(0.7f, 0.85f, 0.7f);
        var cap = FunWorld.Thing("rage_button_cap", local + new Vector3(0f, 0.82f, 0f), 0f, 1f, false, true);
        if (cap == null) return null;
        cap.transform.localScale = Vector3.one * 0.7f;
        FunWorld.MakePress(cap, h => pressed(), 0.12f, true).Where = () => cap.transform.position + Vector3.up * 0.03f;
        FunWorld.Solid(local + new Vector3(0f, 0.4f, 0f), new Vector3(0.24f, 0.8f, 0.24f));
        MovieHouse.Block(local, 0.15f, 0.15f);
        return cap;
    }

    // ================================================================================== DOOR KICKER

    private static class DoorKicker
    {
        private sealed class Door
        {
            public Transform Hinge;
            public GameObject Goon, Granny;
            public Vector3 Centre; // (world, at kick height)
            public int State; // 0 shut, 1 knocking, 2 open
            public float Since;
            public bool Grandma;
            public float Angle;
        }

        private static readonly List<Door> Doors = new List<Door>();
        private static TextMeshPro _board;
        private static float _endsAt = -1f, _next;
        private static int _score;
        private const float Length = 45f;

        public static void Build()
        {
            Doors.Clear();
            _endsAt = -1f;
            var at = new Vector3(-6.4f, 0f, -15.85f);
            FunWorld.Picture("door_kicker", _root, at, Quaternion.identity);
            FunWorld.Solid(at + new Vector3(0f, 1.3f, -0.05f), new Vector3(4.2f, 2.6f, 0.3f));
            MovieHouse.Block(at + new Vector3(0f, 0f, 0.1f), 2.12f, 0.32f);
            const int n = 5;
            const float dw = 0.62f, gap = 0.18f, sill = 0.22f;
            var width = n * dw + (n + 1) * gap;
            for (var k = 0; k < n; k++)
            {
                var x = -width / 2 + gap + k * (dw + gap);
                var hinge = new GameObject("Door hinge") { layer = VrUi.Layer }.transform;
                hinge.SetParent(_root, false);
                hinge.localPosition = at + new Vector3(x, sill + 0.01f, 0.2f);
                FunWorld.Picture("dk_door", hinge, Vector3.zero, Quaternion.identity);
                var goon = FunWorld.Picture(k % 2 == 0 ? "cutout_goon" : "cutout_goon2", _root, at + new Vector3(x + dw / 2, sill - 0.2f, 0.0f), Quaternion.identity);
                var granny = FunWorld.Picture("cutout_granny", _root, at + new Vector3(x + dw / 2, sill - 0.2f, 0.0f), Quaternion.identity);
                foreach (var c in new[] { goon, granny })
                    if (c != null)
                    {
                        c.transform.localScale = Vector3.one * 0.72f;
                        c.SetActive(false);
                    }

                Doors.Add(new Door { Hinge = hinge, Goon = goon, Granny = granny, Centre = FunWorld.World(at + new Vector3(x + dw / 2, 0.75f, 0.25f)) });
            }

            Marquee(at + new Vector3(0f, 2.3f, 0.26f), Vector3.forward, 0.32f, new Color(1f, 0.85f, 0.25f), "DOOR KICKER");
            _board = Marquee(at + new Vector3(0f, 1.72f, 0.26f), Vector3.forward, 0.16f, new Color(0.15f, 0.95f, 1f));
            Button(at + new Vector3(2.6f, 0f, 0.9f), Start);
            Show();
        }

        private static void Start()
        {
            if (_endsAt > 0f) return;
            _score = 0;
            _endsAt = Time.unscaledTime + Length;
            _next = Time.unscaledTime + 1f;
            foreach (var d in Doors) Shut(d);
            GameSounds.PlayOr("World Map Start Level", "door_bang", CinemaRig.HeadPosition, 0.8f);
        }

        private static void Shut(Door d)
        {
            d.State = 0;
            d.Since = Time.unscaledTime;
            if (d.Goon != null) d.Goon.SetActive(false);
            if (d.Granny != null) d.Granny.SetActive(false);
        }

        public static void Tick()
        {
            var now = Time.unscaledTime;
            var running = _endsAt > 0f;
            if (running && now >= _endsAt)
            {
                _endsAt = -1f;
                running = false;
                foreach (var d in Doors) Shut(d);
                Score("DOOR KICKER", _score);
                Pop(Doors[2].Centre + Vector3.up * 1.4f, $"SCORE {_score}", new Color(0.15f, 0.95f, 1f));
            }

            if (running && now >= _next)
            {
                // faster as the round goes on
                var t = 1f - (_endsAt - now) / Length;
                _next = now + Mathf.Lerp(1.5f, 0.65f, t) + Random.Range(0f, 0.3f);
                var shut = Doors.FindAll(x => x.State == 0);
                if (shut.Count > 0)
                {
                    var d = shut[Random.Range(0, shut.Count)];
                    d.State = 1;
                    d.Since = now;
                    d.Grandma = Random.value < 0.18f;
                }
            }

            foreach (var d in Doors)
            {
                var age = now - d.Since;
                if (d.State == 1)
                {
                    // knocking: it rattles in its frame
                    d.Angle = Mathf.Sin(age * 50f) * 3f;
                    if (Mathf.Repeat(age, 0.35f) < Time.unscaledDeltaTime) GameSounds.PlayOr("Door Impacts", "thud", d.Centre, 0.5f);
                    if (age > Mathf.Lerp(1.1f, 0.7f, running ? 1f - (_endsAt - now) / Length : 0f)) Open(d, false);
                }
                else if (d.State == 2)
                {
                    d.Angle = Mathf.Lerp(d.Angle, 105f, Time.unscaledDeltaTime * 18f);
                    if (age > 1.4f) Shut(d);
                }
                else d.Angle = Mathf.Lerp(d.Angle, 0f, Time.unscaledDeltaTime * 12f);

                d.Hinge.localRotation = Quaternion.Euler(0f, -d.Angle, 0f);
            }

            if (running || Time.frameCount % 20 == 0) Show();
        }

        private static void Open(Door d, bool kicked)
        {
            d.State = 2;
            d.Since = Time.unscaledTime;
            if (d.Goon != null) d.Goon.SetActive(!d.Grandma);
            if (d.Granny != null) d.Granny.SetActive(d.Grandma);
            GameSounds.PlayOr(kicked ? "Light Wood Door Smash Open" : "Door Smash Open", "door_bang", d.Centre, kicked ? 1f : 0.6f);
        }

        public static void Kick(Vector3 foot, Vector3 facing)
        {
            if (_endsAt < 0f) return;
            foreach (var d in Doors)
            {
                if ((d.Centre - foot).magnitude > 0.8f) continue;
                if (d.State == 1)
                {
                    _score += d.Grandma ? -200 : 150;
                    Pop(d.Centre + Vector3.up * 0.9f, d.Grandma ? "NOT GRANNY!" : "DOOR KICK! +150", d.Grandma ? new Color(1f, 0.2f, 0.3f) : new Color(1f, 0.85f, 0.25f));
                    Open(d, true);
                    if (!d.Grandma) GameSounds.Play(Random.value < 0.5f ? "Voice Growly" : "Voice Deep", d.Centre, 0.8f);
                }
                else if (d.State == 2)
                {
                    _score += d.Grandma ? -200 : 100;
                    Pop(d.Centre + Vector3.up * 0.9f, d.Grandma ? "NOT GRANNY!" : "KICKED! +100", d.Grandma ? new Color(1f, 0.2f, 0.3f) : new Color(1f, 0.85f, 0.25f));
                    GameSounds.PlayOr("Body impacts", "thud", d.Centre, 1f);
                    Shut(d);
                }

                return;
            }
        }

        /// <summary>A hand (or what it holds) through an open doorway: a punch.</summary>
        public static void Punch(Vector3 at, float speed)
        {
            if (_endsAt < 0f || speed < 2.5f) return;
            foreach (var d in Doors)
                if (d.State == 2 && (d.Centre + Vector3.up * 0.5f - at).magnitude < 0.45f)
                {
                    _score += d.Grandma ? -200 : 75;
                    Pop(d.Centre + Vector3.up * 0.9f, d.Grandma ? "NOT GRANNY!" : "PUNCHED! +75", d.Grandma ? new Color(1f, 0.2f, 0.3f) : new Color(1f, 0.85f, 0.25f));
                    GameSounds.PlayOr("Body impacts", "thud", at, 1f);
                    Shut(d);
                    return;
                }
        }

        private static void Show()
        {
            if (_board == null) return;
            _board.text = _endsAt > 0f ? $"TIME {Mathf.CeilToInt(_endsAt - Time.unscaledTime)}   SCORE {_score}" : "PRESS THE BUTTON: KICK THE DOORS BEFORE THEY OPEN";
        }

        public static void Clear() => Doors.Clear();
    }

    // ================================================================================== GOON GALLERY

    private static class Gallery
    {
        private sealed class Target
        {
            public Transform T;
            public int Rail;
            public float Pos, Speed, Down = -1f;
            public bool Pizza;
            public GameObject Box;
        }

        private static readonly List<Target> Targets = new List<Target>();
        private static Transform _frame;
        private static FunWorld.Grabbable _gun;
        private static Vector3 _holster;
        private static Quaternion _holsterRot;
        private static LineRenderer _cable;
        private static TextMeshPro _board;
        private static float _endsAt = -1f, _lastShot;
        private static int _score, _shots, _hits;
        private const float Length = 40f, Width = 3.0f;

        public static void Build()
        {
            Targets.Clear();
            _endsAt = -1f;
            var at = new Vector3(-3.95f, 0f, -12.0f);
            var frame = FunWorld.Picture("gallery", _root, at, Quaternion.Euler(0f, -90f, 0f));
            _frame = frame != null ? frame.transform : null;
            if (_frame == null) return;
            FunWorld.Solid(at + new Vector3(0f, 0.51f, 0f), new Vector3(3.2f, 1.02f, 0.6f), -90f);
            MovieHouse.Block(at + new Vector3(1.4f, 0f, 0f), 1.75f, 1.62f);
            var rnd = new System.Random(3);
            for (var rail = 0; rail < 3; rail++)
                for (var k = 0; k < 5; k++)
                {
                    var pizza = rnd.NextDouble() < 0.18;
                    var vis = FunWorld.Picture(pizza ? "gallery_pizza" : "gallery_goon", _frame, Vector3.zero, Quaternion.identity);
                    if (vis == null) continue;
                    var box = new GameObject("Target") { layer = FunWorld.PhysLayer };
                    box.transform.SetParent(_root, false);
                    var c = box.AddComponent<BoxCollider>();
                    c.size = new Vector3(0.26f, 0.3f, 0.26f);
                    var rb = box.AddComponent<Rigidbody>();
                    rb.isKinematic = true;
                    Targets.Add(new Target { T = vis.transform, Rail = rail, Pos = k / 5f, Speed = (rail % 2 == 0 ? 1f : -1f) * (0.12f + rail * 0.04f), Pizza = pizza, Box = box });
                }

            // the light gun on its cable at the counter
            _holster = at + new Vector3(0.1f, 1.06f, 0.2f);
            _holsterRot = Quaternion.Euler(0f, 90f, 0f);
            var gun = FunWorld.Thing("lightgun", _holster, 90f, 0.6f, false, true);
            if (gun != null)
            {
                _gun = FunWorld.MakeGrabbable(gun, new Vector3(0f, 0.04f, -0.02f), Quaternion.Euler(-15f, 0f, 0f));
                _gun.Reach = 0.18f;
                _gun.OnTrigger = (g, h) => Shoot(h);
                var line = new GameObject("Cable") { layer = VrUi.Layer };
                line.transform.SetParent(_root, false);
                _cable = line.AddComponent<LineRenderer>();
                _cable.positionCount = 8;
                _cable.widthMultiplier = 0.012f;
                _cable.sharedMaterial = Props.Mat("rubber_black");
            }

            Marquee(at + new Vector3(-0.4f, 2.95f, 0f), Vector3.left, 0.3f, new Color(1f, 0.16f, 0.62f), "GOON GALLERY");
            _board = Marquee(at + new Vector3(-0.45f, 2.45f, 0f), Vector3.left, 0.15f, new Color(1f, 0.85f, 0.25f));
            var start = new GameObject("Gallery start") { layer = FunWorld.PhysLayer };
            start.transform.SetParent(_root, false);
            start.transform.localPosition = at + new Vector3(-0.32f, 1.08f, -1.2f);
            start.AddComponent<BoxCollider>().size = new Vector3(0.12f, 0.06f, 0.12f);
            FunWorld.Picture("rage_button_cap", _root, start.transform.localPosition - new Vector3(0f, 0.03f, 0f), Quaternion.identity).transform.localScale = Vector3.one * 0.6f;
            FunWorld.MakePress(start, h => Start(), 0.1f);
            Show();
        }

        private static void Start()
        {
            if (_endsAt > 0f) return;
            _score = _shots = _hits = 0;
            _endsAt = Time.unscaledTime + Length;
            foreach (var t in Targets) t.Down = -1f;
            GameSounds.PlayOr("World Map Start Level", "door_bang", CinemaRig.HeadPosition, 0.8f);
        }

        public static void Step(float dt)
        {
            if (_frame == null) return;
            var speedUp = _endsAt > 0f ? 1.6f : 0.6f;
            foreach (var t in Targets)
            {
                t.Pos = Mathf.Repeat(t.Pos + t.Speed * dt * speedUp / Width * 3f, 1f);
                var x = (t.Pos - 0.5f) * Width;
                var y = 1.25f + t.Rail * 0.4f - 0.3f;
                var z = -2.1f + t.Rail * 0.15f + 0.03f;
                var down = t.Down > 0f ? Mathf.Clamp01((Time.unscaledTime - t.Down) * 6f) : 0f;
                if (t.Down > 0f && Time.unscaledTime - t.Down > 2.5f) t.Down = -1f;
                t.T.localPosition = new Vector3(x, y, z);
                t.T.localRotation = Quaternion.Euler(-85f * down, 0f, 0f);
                t.Box.transform.position = t.T.TransformPoint(new Vector3(0f, 0.5f, 0f));
                t.Box.transform.rotation = t.T.rotation;
            }
        }

        private static void Shoot(XRNode hand)
        {
            if (Time.unscaledTime - _lastShot < 0.18f || _gun == null) return;
            _lastShot = Time.unscaledTime;
            var muzzle = _gun.transform.TransformPoint(new Vector3(0f, 0.11f, 0.17f));
            var dir = _gun.transform.TransformDirection(Vector3.forward);
            Fx.MuzzleFlash(muzzle, dir, 0.6f);
            GameSounds.PlayOr("Pistol Shoot", "kick", muzzle, 0.7f, 1.3f);
            VrControls.Rumble(hand, 0.5f, 0.05f);
            if (_endsAt > 0f) _shots++;
            if (!FunWorld.Raycast(muzzle, dir, 12f, out var hit)) return;
            foreach (var t in Targets)
            {
                if (hit.collider.gameObject != t.Box || t.Down > 0f) continue;
                t.Down = Time.unscaledTime;
                GameSounds.PlayOr("Bullet Impacts Metal", "glass", hit.point, 0.9f);
                if (_endsAt > 0f)
                {
                    _hits++;
                    var pts = t.Pizza ? 250 : 100;
                    _score += pts;
                    Pop(hit.point + Vector3.up * 0.2f, t.Pizza ? $"PIZZA! +{pts}" : $"+{pts}", t.Pizza ? new Color(1f, 0.85f, 0.25f) : new Color(0.15f, 0.95f, 1f));
                }

                return;
            }

            Fx.Sparks(hit.point, hit.normal, 8);
        }

        public static void Tick()
        {
            if (_frame == null) return;
            if (_endsAt > 0f && Time.unscaledTime >= _endsAt)
            {
                _endsAt = -1f;
                var acc = _shots > 0 ? Mathf.RoundToInt(100f * _hits / _shots) : 0;
                var total = _score + acc * 5;
                Score("GOON GALLERY", total);
                Pop(_frame.TransformPoint(new Vector3(0f, 1.8f, 0.4f)), $"SCORE {total} ({acc}% HITS)", new Color(0.15f, 0.95f, 1f));
            }

            // the gun goes back to the counter when it's let go
            if (_gun != null && !_gun.Busy && Time.unscaledTime - _gun.LastHeld > 1f)
            {
                var want = FunWorld.World(_holster);
                if ((_gun.transform.position - want).magnitude > 0.02f)
                {
                    _gun.Body.isKinematic = true;
                    _gun.transform.position = Vector3.Lerp(_gun.transform.position, want, Time.unscaledDeltaTime * 8f);
                    _gun.transform.rotation = Quaternion.Slerp(_gun.transform.rotation, _root.rotation * _holsterRot, Time.unscaledDeltaTime * 8f);
                }
            }

            if (_cable != null && _gun != null)
            {
                var a = FunWorld.World(_holster + new Vector3(0.15f, -0.04f, 0f));
                var b = _gun.transform.TransformPoint(new Vector3(0f, -0.02f, -0.06f));
                for (var i = 0; i < 8; i++)
                {
                    var t = i / 7f;
                    _cable.SetPosition(i, Vector3.Lerp(a, b, t) + Vector3.down * Mathf.Sin(t * Mathf.PI) * 0.15f);
                }
            }

            if (_endsAt > 0f || Time.frameCount % 20 == 0) Show();
        }

        private static void Show()
        {
            if (_board == null) return;
            _board.text = _endsAt > 0f ? $"TIME {Mathf.CeilToInt(_endsAt - Time.unscaledTime)}   SCORE {_score}" : "GRIP THE GUN, PRESS THE BUTTON, SHOOT THE GOONS";
        }

        public static void Clear()
        {
            Targets.Clear();
            _frame = null;
            _gun = null;
            _cable = null;
        }
    }

    // ================================================================================== KICK-O-METER

    private static class Meter
    {
        private static readonly List<Renderer> Lamps = new List<Renderer>();
        private static Transform _pad;
        private static Vector3 _padAt;
        private static TextMeshPro _rating;
        private static float _hitAt = -10f, _power, _swing;
        private static Material _on, _off;
        private const int Count = 12;

        public static void Build()
        {
            Lamps.Clear();
            var at = new Vector3(-2.1f, 0f, -7.6f);
            FunWorld.Picture("kickometer", _root, at, Quaternion.Euler(0f, -90f, 0f));
            FunWorld.Solid(at + new Vector3(0.1f, 1.25f, 0f), new Vector3(0.82f, 2.5f, 0.6f), -90f);
            MovieHouse.Block(at + new Vector3(0.1f, 0f, 0f), 0.32f, 0.42f);
            _on = Props.Mat("lamp_on");
            _off = Props.Mat("lamp");
            var face = Quaternion.Euler(0f, -90f, 0f);
            for (var k = 0; k < Count; k++)
            {
                var lamp = FunWorld.Picture("meter_lamp", _root, at + face * new Vector3(0f, 0.95f + k * 0.115f, 0.225f), face);
                if (lamp != null) Lamps.Add(lamp.GetComponent<Renderer>());
            }

            var pad = FunWorld.Picture("meter_pad", _root, at + face * new Vector3(0f, 0.62f, 0.28f), face);
            _pad = pad != null ? pad.transform : null;
            _padAt = FunWorld.World(at + face * new Vector3(0f, 0.62f, 0.62f));
            var hit = new GameObject("Meter pad") { layer = FunWorld.PhysLayer };
            hit.transform.SetParent(_root, false);
            hit.transform.position = _padAt;
            hit.AddComponent<SphereCollider>().radius = 0.2f;
            hit.AddComponent<Rigidbody>().isKinematic = true;
            hit.AddComponent<PadHit>();
            FunWorld.MakePress(hit, h => Hit(Mathf.Clamp01(Needle * 1.04f), true), 0.01f); // (pointed at and the trigger: as a kick)
            Marquee(at + new Vector3(-0.32f, 2.75f, 0f), Vector3.left, 0.26f, new Color(1f, 0.85f, 0.25f), "KICK-O-METER");
            _rating = Marquee(at + new Vector3(-0.33f, 0.62f + 1.65f, 0f), Vector3.left, 0.13f, new Color(0.15f, 0.95f, 1f), "HOW ANGRY ARE YOU?");
        }

        private sealed class PadHit : MonoBehaviour
        {
            private void OnCollisionEnter(Collision c)
            {
                if (c.collider.gameObject.name.StartsWith("Hand") || c.collider.GetComponentInParent<FunWorld.Grabbable>() is { Busy: true })
                    Hit(Mathf.Clamp01((c.relativeVelocity.magnitude - 1.5f) / 8f), false);
            }
        }

        // (the light running up and down the column: kick when it's at the top)
        private static float Needle => Mathf.PingPong(Time.unscaledTime * 1.7f, 1f);

        public static void Kick(Vector3 foot, Vector3 facing)
        {
            if ((foot - _padAt).magnitude > 0.85f) return;
            Hit(Mathf.Clamp01(Needle * 1.04f + Random.Range(-0.04f, 0.03f)), true);
        }

        private static void Hit(float power, bool kick)
        {
            if (Time.unscaledTime - _hitAt < 2.2f) return;
            _hitAt = Time.unscaledTime;
            _power = power;
            _swing = 1f;
            GameSounds.PlayOr(kick ? "Kick Impacts" : "Body impacts", "thud", _padAt, 1f);
            var (label, c) = power switch
            {
                >= 0.92f => ("ANGER FOOT!!", new Color(1f, 0.2f, 0.3f)),
                >= 0.75f => ("LIVID", new Color(1f, 0.45f, 0.2f)),
                >= 0.55f => ("FURIOUS", new Color(1f, 0.85f, 0.25f)),
                >= 0.3f => ("CRANKY", new Color(0.15f, 0.95f, 1f)),
                _ => ("MILDLY ANNOYED", new Color(0.6f, 0.9f, 1f)),
            };
            if (_rating != null)
            {
                _rating.text = $"{label}\n{Mathf.RoundToInt(power * 1000)}";
                _rating.color = c;
            }

            if (power >= 0.92f)
            {
                Fx.Confetti(_padAt + Vector3.up * 1.6f);
                GameSounds.PlayOr("Level Complete", "door_bang", _padAt, 1f);
            }

            Score("KICK-O-METER", Mathf.RoundToInt(power * 1000));
        }

        public static void Tick()
        {
            if (Lamps.Count == 0) return;
            var since = Time.unscaledTime - _hitAt;
            int lit;
            if (since < 2.2f)
            {
                // the lamps climb to the hit's power
                lit = Mathf.RoundToInt(Mathf.Min(since / 0.9f, 1f) * _power * Count);
                if (since < 0.9f && Time.frameCount % 4 == 0) GameSounds.Play("UI Button MouseOver", _padAt + Vector3.up * (0.95f + lit * 0.115f), 0.25f, 0.8f + lit * 0.08f);
            }
            else
            {
                // the running light
                lit = -1;
                var at = Mathf.RoundToInt(Needle * (Count - 1));
                for (var k = 0; k < Lamps.Count; k++)
                    if (Lamps[k] != null)
                        Lamps[k].sharedMaterial = k == at ? _on : _off;
            }

            if (lit >= 0)
                for (var k = 0; k < Lamps.Count; k++)
                    if (Lamps[k] != null)
                        Lamps[k].sharedMaterial = k < lit ? _on : _off;
            foreach (var hand in new[] { XRNode.LeftHand, XRNode.RightHand })
            {
                var hp = VrHands.Get(hand);
                if (hp.Tracked && (hp.Position - _padAt).magnitude < 0.24f && hp.Velocity.magnitude > 2f) Hit(Mathf.Clamp01((hp.Velocity.magnitude - 1.5f) / 8f), false);
            }

            _swing = Mathf.MoveTowards(_swing, 0f, Time.unscaledDeltaTime * 2.5f);
            if (_pad != null) _pad.localRotation = Quaternion.Euler(-70f * Mathf.Sin(_swing * Mathf.PI * 0.5f), -90f, 0f);
        }

        public static void Clear()
        {
            Lamps.Clear();
            _pad = null;
            _rating = null;
        }
    }

    // ================================================================================== SHOE TOSS

    private static class ShoeToss
    {
        private sealed class Hole
        {
            public Vector3 Centre; // (world)
            public float Radius;
            public int Points;
        }

        private static readonly List<Hole> Holes = new List<Hole>();
        private static readonly List<(FunWorld.Grabbable g, Vector3 home)> Pairs = new List<(FunWorld.Grabbable, Vector3)>();
        private static readonly Dictionary<FunWorld.Grabbable, Vector3> Last = new Dictionary<FunWorld.Grabbable, Vector3>();
        private static TextMeshPro _board;
        private static Transform _lane;
        private static int _score, _thrown;
        private static bool _playing;
        private static float _doneAt = -1f;

        public static void Build()
        {
            Holes.Clear();
            Pairs.Clear();
            Last.Clear();
            _playing = false;
            var at = new Vector3(-10.75f, 0f, -8.1f);
            var lane = FunWorld.Picture("shoe_toss", _root, at, Quaternion.Euler(0f, 180f, 0f));
            _lane = lane != null ? lane.transform : null;
            if (_lane == null) return;
            const float len = 3.2f;
            // the lane's felt, its rails, its backboard and the shelf the holes are in
            FunWorld.Solid(at + Rot(new Vector3(0f, 0.3f, len / 2)), new Vector3(0.9f, 0.06f, len), 180f);
            FunWorld.Solid(at + Rot(new Vector3(0.48f, 0.45f, len / 2)), new Vector3(0.06f, 0.3f, len), 180f);
            FunWorld.Solid(at + Rot(new Vector3(-0.48f, 0.45f, len / 2)), new Vector3(0.06f, 0.3f, len), 180f);
            FunWorld.Solid(at + Rot(new Vector3(0f, 0.42f, len + 0.25f)), new Vector3(0.9f, 0.05f, 0.62f), 180f, -25f);
            FunWorld.Solid(at + Rot(new Vector3(0f, 1.05f, len + 0.97f)), new Vector3(1.02f, 1.5f, 0.1f), 180f);
            MovieHouse.Block(at + Rot(new Vector3(0f, 0f, (len + 1f) / 2)), 0.55f, (len + 1.1f) / 2);
            foreach (var (x, z, r, p) in new[] { (0f, 0.75f, 0.16f, 10), (-0.26f, 1.15f, 0.11f, 30), (0.26f, 1.15f, 0.11f, 30), (0f, 1.5f, 0.09f, 50), (0f, 1.8f, 0.075f, 100) })
                Holes.Add(new Hole { Centre = FunWorld.World(at + Rot(new Vector3(x, z, len + 0.88f))), Radius = r + 0.04f, Points = p });
            Marquee(at + Rot(new Vector3(0f, 2.2f, len + 0.86f)), Vector3.forward, 0.3f, new Color(0.15f, 0.95f, 1f), "SHOE TOSS");
            _board = Marquee(at + Rot(new Vector3(0f, 1.98f, len + 0.84f)), Vector3.forward, 0.13f, new Color(1f, 0.85f, 0.25f));
            // the shoes in their tray by the near end
            var meshes = ShoeMeshes();
            for (var k = 0; k < 9; k++)
            {
                var home = at + new Vector3(0.3f + (k % 3 - 1) * 0.3f, 0.9f, 0.45f + (k / 3 - 1) * 0.24f);
                var pair = Pair(meshes.Count > 0 ? meshes[k % meshes.Count] : default, home);
                if (pair != null) Pairs.Add((pair, home));
            }

            FunWorld.Solid(at + new Vector3(0.3f, 0.4f, 0.45f), new Vector3(1.0f, 0.8f, 0.8f));
            MovieHouse.Block(at + new Vector3(0.3f, 0f, 0.45f), 0.5f, 0.4f);
            FunWorld.Picture("concession_counter", _root, at + new Vector3(0.3f, -0.15f, 0.45f), Quaternion.identity).transform.localScale = new Vector3(0.42f, 1f, 1.3f);
            Show();
        }

        private static Vector3 Rot(Vector3 v) => Quaternion.Euler(0f, 180f, 0f) * v;

        private static List<(Mesh mesh, Material[] mats)> ShoeMeshes()
        {
            var list = new List<(Mesh, Material[])>();
            var template = Body.HeroModel.Template;
            if (template == null) return list;
            foreach (var r in template.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.sharedMesh != null && Body.HeroModel.ShoeMeshes.ContainsValue(r.name) && r.name != "Old_Kicks")
                    list.Add((r.sharedMesh, r.sharedMaterials));
            return list;
        }

        private static FunWorld.Grabbable Pair((Mesh mesh, Material[] mats) shoes, Vector3 home)
        {
            var go = new GameObject("Shoe pair") { layer = FunWorld.PhysLayer };
            go.transform.SetParent(_root, false);
            go.transform.localPosition = home;
            if (shoes.mesh != null)
            {
                var vis = new GameObject("Visual") { layer = VrUi.Layer };
                vis.transform.SetParent(go.transform, false);
                var b = shoes.mesh.bounds;
                var s = 0.26f / Mathf.Max(0.05f, Mathf.Max(b.size.x, b.size.z));
                vis.transform.localScale = Vector3.one * s;
                vis.transform.localPosition = -b.center * s;
                vis.AddComponent<MeshFilter>().sharedMesh = shoes.mesh;
                vis.AddComponent<MeshRenderer>().sharedMaterials = shoes.mats;
            }
            else FunWorld.Picture("kickball", go.transform, Vector3.zero, Quaternion.identity).transform.localScale = Vector3.one * 0.6f;

            var c = go.AddComponent<BoxCollider>();
            c.size = new Vector3(0.26f, 0.1f, 0.2f);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.5f;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.Sleep();
            var g = FunWorld.MakeGrabbable(go, Vector3.zero, Quaternion.identity);
            g.Reach = 0.18f;
            g.OnGrabbed = (x, h) =>
            {
                if (!_playing)
                {
                    _playing = true;
                    _score = 0;
                    _thrown = 0;
                }
            };
            g.OnReleased = (x, h) =>
            {
                if (!_playing) return;
                _thrown++;
                if (_thrown >= 9) _doneAt = Time.unscaledTime + 3f;
            };
            return g;
        }

        public static void Tick()
        {
            foreach (var (g, home) in Pairs)
            {
                if (g == null) continue;
                var p = g.transform.position;
                if (!g.Busy && Last.TryGetValue(g, out var was) && g.Body != null && !g.Body.isKinematic)
                    foreach (var h in Holes)
                    {
                        // through a hole: in its ring, moving into the board
                        var d = new Vector2(p.x - h.Centre.x, p.y - h.Centre.y).magnitude;
                        var crossed = (was.z - h.Centre.z) * (p.z - h.Centre.z) <= 0f || Mathf.Abs(p.z - h.Centre.z) < 0.08f;
                        if (d < h.Radius && crossed)
                        {
                            In(g, h);
                            break;
                        }
                    }

                Last[g] = p;
                // a pair that's landed somewhere and been left: back to the tray
                if (!g.Busy && Time.unscaledTime - g.LastHeld > 5f && (g.transform.localPosition - home).magnitude > 0.3f && (g.Body == null || g.Body.velocity.magnitude < 0.2f || g.transform.position.y < _root.position.y - 1f))
                {
                    g.Body.velocity = Vector3.zero;
                    g.transform.localPosition = home;
                    g.transform.localRotation = Quaternion.identity;
                    g.Body.Sleep();
                }
            }

            if (_doneAt > 0f && Time.unscaledTime > _doneAt)
            {
                _doneAt = -1f;
                Done();
            }

            if (Time.frameCount % 15 == 0) Show();
        }

        private static void In(FunWorld.Grabbable g, Hole h)
        {
            _score += h.Points;
            Pop(h.Centre + Vector3.up * 0.3f, $"+{h.Points}", h.Points >= 50 ? new Color(1f, 0.2f, 0.3f) : new Color(1f, 0.85f, 0.25f));
            GameSounds.PlayOr("Soccer ball impacts", "thud", h.Centre, 0.8f);
            if (h.Points >= 100) Fx.Confetti(h.Centre);
            // (into the hole: out of play until it's back in the tray)
            g.Body.velocity = Vector3.zero;
            g.transform.position = h.Centre + Vector3.down * 0.4f;
            g.LastHeld = Time.unscaledTime - 4f;
        }

        private static void Done()
        {
            _playing = false;
            Score("SHOE TOSS", _score);
            Pop(Holes[3].Centre + Vector3.up * 0.8f, $"SCORE {_score}", new Color(0.15f, 0.95f, 1f));
        }

        private static void Show()
        {
            if (_board == null) return;
            _board.text = _playing ? $"SHOES IN {_thrown}/9   SCORE {_score}" : "THROW THE SHOES INTO THE RINGS";
        }

        public static void Clear()
        {
            Holes.Clear();
            Pairs.Clear();
            Last.Clear();
            _lane = null;
        }
    }

    // ================================================================================== GOON BOWLING

    private static class Bowling
    {
        private static readonly List<(Rigidbody pin, Vector3 home)> Pins = new List<(Rigidbody, Vector3)>();
        private static Rigidbody _ball;
        private static Vector3 _ballHome;
        private static TextMeshPro _board;
        private static float _rolledAt = -1f;
        private static int _frame, _roll, _standing = 10, _total, _first;
        private static readonly List<string> Marks = new List<string>();
        private const int Frames = 5;

        public static void Build()
        {
            Pins.Clear();
            Marks.Clear();
            _frame = 0;
            _roll = 0;
            _total = 0;
            var at = new Vector3(-12.55f, 0f, -7.65f);
            FunWorld.Picture("bowling_lane", _root, at, Quaternion.Euler(0f, 180f, 0f));
            const float len = 7f;
            Vector3 R(Vector3 v) => at + Quaternion.Euler(0f, 180f, 0f) * v;
            FunWorld.Solid(R(new Vector3(0f, 0.025f, len / 2)), new Vector3(1.05f, 0.05f, len), 180f);
            FunWorld.Solid(R(new Vector3(0f, -0.02f, len / 2)), new Vector3(1.6f, 0.04f, len), 180f);
            foreach (var side in new[] { -1f, 1f })
                FunWorld.Solid(R(new Vector3(side * 0.755f, 0.225f, len / 2 + 0.4f)), new Vector3(0.06f, 0.45f, len + 0.8f), 180f);
            FunWorld.Solid(R(new Vector3(0f, 0.6f, len + 0.65f)), new Vector3(1.6f, 1.2f, 0.1f), 180f);
            MovieHouse.Block(R(new Vector3(0f, 0f, len / 2 + 0.4f)), 0.82f, len / 2 + 0.45f);
            // ten goon pins in their triangle at the far end
            var deck = len - 0.55f;
            for (var row = 0; row < 4; row++)
                for (var k = 0; k <= row; k++)
                {
                    var p = R(new Vector3((k - row / 2f) * 0.22f, 0.06f, deck + row * 0.19f));
                    var pin = FunWorld.Thing("goon_pin", p, 180f, 1.4f, true);
                    if (pin == null) continue;
                    var rb = pin.GetComponent<Rigidbody>();
                    rb.WakeUp();
                    Pins.Add((rb, p));
                }

            _ballHome = R(new Vector3(0f, 0.12f, -0.35f));
            var ball = FunWorld.Thing("kickball", _ballHome, 0f, 1.6f);
            if (ball != null)
            {
                Object.Destroy(ball.GetComponent<BoxCollider>());
                ball.AddComponent<SphereCollider>().radius = 0.11f;
                _ball = ball.GetComponent<Rigidbody>();
                _ball.angularDrag = 0.2f;
                var g = FunWorld.MakeGrabbable(ball, Vector3.zero, Quaternion.identity);
                g.Reach = 0.2f;
                g.OnReleased = (x, h) => Rolled();
            }

            Marquee(R(new Vector3(0f, 1.6f, len + 0.55f)), Vector3.forward, 0.3f, new Color(1f, 0.16f, 0.62f), "GOON BOWLING");
            _board = Marquee(R(new Vector3(0f, 1.3f, len + 0.55f)), Vector3.forward, 0.13f, new Color(1f, 0.85f, 0.25f));
            Show();
        }

        public static void Kick(Vector3 foot, Vector3 facing)
        {
            if (_ball == null || (_ball.position - foot).magnitude > 0.95f) return;
            _ball.isKinematic = false;
            _ball.WakeUp();
            // (a straight kick runs down the lane; you aim with where you face)
            _ball.velocity = facing * 9.5f + Vector3.up * 0.4f;
            _ball.angularVelocity = Vector3.Cross(Vector3.up, facing) * 40f;
            GameSounds.PlayOr("Soccer ball impacts", "thud", _ball.position, 1f);
            Rolled();
        }

        private static void Rolled()
        {
            if (_rolledAt < 0f) _rolledAt = Time.unscaledTime;
        }

        public static void Tick()
        {
            if (_ball == null) return;
            if (_rolledAt > 0f)
            {
                var t = Time.unscaledTime - _rolledAt;
                var still = _ball.velocity.magnitude < 0.15f && t > 1.5f;
                var past = FunWorld.Local(_ball.position).z < -13.9f;
                if (t > 7f || (still && t > 2f) || (past && t > 3f)) Count();
            }

            if (Time.frameCount % 15 == 0) Show();
        }

        private static int Down()
        {
            var n = 0;
            foreach (var (pin, home) in Pins)
                if (pin == null || Vector3.Angle(pin.transform.up, Vector3.up) > 35f || (FunWorld.Local(pin.position) - home).magnitude > 0.18f)
                    n++;
            return n;
        }

        private static void Count()
        {
            _rolledAt = -1f;
            var down = Down();
            var now = down - (10 - _standing);
            if (_roll == 0)
            {
                _first = down;
                if (down == 10)
                {
                    Marks.Add("X");
                    _total += 30;
                    Pop(FunWorld.World(Pins[0].home) + Vector3.up * 1f, "STRIKE!", new Color(1f, 0.2f, 0.3f));
                    GameSounds.PlayOr("Level Complete", "door_bang", CinemaRig.HeadPosition, 1f);
                    NextFrame();
                }
                else
                {
                    _standing = 10 - down;
                    _roll = 1;
                    Pop(FunWorld.World(Pins[0].home) + Vector3.up * 1f, $"{down}", new Color(1f, 0.85f, 0.25f));
                    Sweep(false);
                }
            }
            else
            {
                if (down == 10)
                {
                    Marks.Add($"{_first}/");
                    _total += 15;
                    Pop(FunWorld.World(Pins[0].home) + Vector3.up * 1f, "SPARE!", new Color(0.15f, 0.95f, 1f));
                }
                else
                {
                    Marks.Add($"{_first}{now}");
                    _total += down;
                }

                NextFrame();
            }

            if (now > 0 && Random.value < 0.6f) GameSounds.Play(Random.value < 0.5f ? "Voice Growly" : "Voice Nasally", FunWorld.World(Pins[0].home), 0.6f);
            BallBack();
        }

        private static void NextFrame()
        {
            _frame++;
            _roll = 0;
            _standing = 10;
            Sweep(true);
            if (_frame >= Frames)
            {
                Score("GOON BOWLING", _total);
                Pop(FunWorld.World(Pins[0].home) + Vector3.up * 1.4f, $"GAME {_total}", new Color(0.15f, 0.95f, 1f));
                _frame = 0;
                _total = 0;
                Marks.Clear();
            }
        }

        /// <summary>The pins set up again (all of them: a new frame; else only the standing ones stay).</summary>
        private static void Sweep(bool all)
        {
            for (var i = 0; i < Pins.Count; i++)
            {
                var (pin, home) = Pins[i];
                if (pin == null) continue;
                var fallen = Vector3.Angle(pin.transform.up, Vector3.up) > 35f || (FunWorld.Local(pin.position) - home).magnitude > 0.18f;
                if (!all && !fallen) continue;
                pin.velocity = Vector3.zero;
                pin.angularVelocity = Vector3.zero;
                if (all)
                {
                    pin.gameObject.SetActive(true);
                    pin.transform.localPosition = home;
                    pin.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    pin.Sleep();
                }
                else pin.gameObject.SetActive(false); // (knocked down: cleared off the deck)
            }

            GameSounds.Play("Trolley Impacts", FunWorld.World(Pins[0].home), 0.5f);
        }

        private static void BallBack()
        {
            if (_ball == null) return;
            var g = _ball.GetComponent<FunWorld.Grabbable>();
            if (g != null && g.Busy) return;
            _ball.velocity = Vector3.zero;
            _ball.angularVelocity = Vector3.zero;
            _ball.transform.localPosition = _ballHome;
            _ball.Sleep();
        }

        private static void Show()
        {
            if (_board == null) return;
            _board.text = $"FRAME {Mathf.Min(_frame + 1, Frames)}/{Frames}  ROLL {_roll + 1}   {string.Join(" ", Marks)}   TOTAL {_total}\nKICK (OR THROW) THE BALL AT THE GOONS";
        }

        public static void Clear()
        {
            Pins.Clear();
            _ball = null;
            _board = null;
            _rolledAt = -1f;
        }
    }

    /// <summary>A hand through Door Kicker's doorways (the punches), every frame.</summary>
    public static void Hands()
    {
        if (!_built) return;
        foreach (var hand in new[] { XRNode.LeftHand, XRNode.RightHand })
        {
            var p = VrHands.Get(hand);
            if (p.Tracked) DoorKicker.Punch(p.Position, p.Velocity.magnitude);
        }
    }

    public static void Clear()
    {
        _built = false;
        _root = null;
        _scores = null;
        DoorKicker.Clear();
        Gallery.Clear();
        Meter.Clear();
        ShoeToss.Clear();
        Bowling.Clear();
    }

    public static string Describe() => $"ARCADE built {_built} best {string.Join(", ", Best)}";
}
