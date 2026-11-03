using System.Collections.Generic;
using System.IO;
using AngerFootVR.Core;
using Framework;
using TMPro;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.UI;

/// <summary>
/// The movie house's lobby, to do things in between levels:
/// - the concession stand: grab a popcorn bucket and eat (hold it to your mouth), hold a cup under the soda
///   fountain to fill it and drink; kick the popcorn cart and popcorn flies;
/// - the seats: walk down the aisle and grip by a row to sit (the stick gets you up);
/// - NOW SHOWING: posters of the levels you've played, their stars under them; point at one and pull the trigger
///   (twice: to be sure) to play it; the arrows page through them;
/// - the shoe wall: every pair you've unlocked; point at a pair and pull the trigger (or grip it) to wear it, through
///   the game's own shoe choice (as its shoe menu does; in a level, from your next one);
/// - the photo kiosk: press its button, smile at the lens: 3, 2, 1, flash, and a photo strip drops out of its slot
///   (it's saved in Pictures\Anger Foot VR too);
/// - the house lights' knife switch by your spot (pull it down for the lights), the curtain's pull cord by the
///   stage; a cardboard audience in the seats, and two cardboard goons in the lobby to kick about.
/// </summary>
internal static class Lobby
{
    private static Transform _root;
    private static bool _built;

    public static void Init()
    {
        FunWorld.Built += Build;
        FunWorld.Tick += Tick;
        FunWorld.Clearing += Clear;
    }

    private static void Build()
    {
        _root = FunWorld.Root;
        _built = true;
        _seatRow = -1;
        FunWorld.Soon("concessions", Concessions);
        FunWorld.Soon("posters", Posters);
        FunWorld.Soon("shoes", Shoes);
        FunWorld.Soon("photo kiosk", Kiosk);
        FunWorld.Soon("switch", Switch);
        FunWorld.Soon("cord", Cord);
        FunWorld.Soon("audience", Audience);
    }

    private static void Tick()
    {
        if (!_built || _root == null) return;
        Refill();
        Soda();
        Seats();
        Countdown();
        PosterPress();
    }

    // ================================================================================== concessions

    private static readonly List<(string kind, Vector3 at, GameObject live, float goneAt)> Stock = new List<(string, Vector3, GameObject, float)>();
    private static readonly Vector3[] Nozzles = new Vector3[3];
    private static readonly Dictionary<GameObject, float> Filling = new Dictionary<GameObject, float>();
    private static readonly HashSet<GameObject> Full = new HashSet<GameObject>();

    private static void Concessions()
    {
        Stock.Clear();
        Filling.Clear();
        Full.Clear();
        var counterAt = new Vector3(-6.62f, 0f, -1.0f);
        FunWorld.Picture("concession_counter", _root, counterAt, Quaternion.Euler(0f, 90f, 0f));
        FunWorld.Solid(counterAt + new Vector3(0f, 0.48f, 0f), new Vector3(2.46f, 0.96f, 0.66f), 90f);
        MovieHouse.Block(counterAt, 0.33f, 1.23f);
        var popcornAt = new Vector3(-6.45f, 0f, 1.0f);
        var cart = FunWorld.Thing("popcorn_machine", popcornAt, 90f, 80f, false, true);
        if (cart != null) cart.AddComponent<Cart>();
        MovieHouse.Block(popcornAt, 0.3f, 0.45f);
        var fountain = FunWorld.Picture("soda_fountain", _root, new Vector3(-6.72f, 0.96f, -1.75f), Quaternion.Euler(0f, 90f, 0f));
        FunWorld.Solid(new Vector3(-6.72f, 0.96f + 0.28f, -1.75f), new Vector3(0.62f, 0.56f, 0.42f), 90f);
        if (fountain != null)
            for (var k = 0; k < 3; k++)
                Nozzles[k] = fountain.transform.TransformPoint(new Vector3(-0.2f + k * 0.2f, 0.215f, 0.17f));
        MovieHouse.SignFacing("CONCESSIONS", new Vector3(-6.93f, 2.55f, -0.4f), 0.36f, new Color(1f, 0.85f, 0.25f), Vector3.right, _root);
        foreach (var z in new[] { -0.55f, -0.15f, 0.3f }) Stock.Add(("popcorn_bucket", new Vector3(-6.48f, 0.965f, z), null, -1f));
        foreach (var z in new[] { -1.3f, -1.1f }) Stock.Add(("soda_cup", new Vector3(-6.45f, 0.965f, z), null, -1f));
        for (var i = 0; i < Stock.Count; i++) Stock[i] = (Stock[i].kind, Stock[i].at, Make(Stock[i].kind, Stock[i].at), -1f);
    }

    private static GameObject Make(string kind, Vector3 at)
    {
        var go = FunWorld.Thing(kind, at, Random.Range(-20f, 20f), kind == "popcorn_bucket" ? 0.3f : 0.2f, true);
        if (go == null) return null;
        var b = go.GetComponent<Collider>().bounds;
        var g = FunWorld.MakeGrabbable(go, go.transform.InverseTransformPoint(b.center), Quaternion.identity);
        g.Reach = 0.16f;
        if (kind == "popcorn_bucket")
        {
            var d = Drinks.MakeDrinkable(g, "Chew popcorn", 8);
            go.AddComponent<Spill>();
            d.Sipped = x => Kernels(FunWorld.Mouth, 3, 0.6f);
        }
        else
        {
            var d = Drinks.MakeDrinkable(g, "Player drink bottle", 5);
            d.Ready = () => Full.Contains(go);
        }

        return go;
    }

    /// <summary>Taken stock comes back to the counter a while after it's gone from its place.</summary>
    private static void Refill()
    {
        for (var i = 0; i < Stock.Count; i++)
        {
            var (kind, at, live, goneAt) = Stock[i];
            var there = live != null && (live.transform.localPosition - at).magnitude < 0.25f;
            if (there)
            {
                if (goneAt > 0f) Stock[i] = (kind, at, live, -1f);
                continue;
            }

            if (goneAt < 0f)
            {
                Stock[i] = (kind, at, live, Time.unscaledTime);
                if (live != null) FunWorld.AddDebris(live, 90f); // (the old one's yours a while, then tidied away)
                continue;
            }

            if (Time.unscaledTime - goneAt < 6f) continue;
            Stock[i] = (kind, at, Make(kind, at), -1f);
        }
    }

    /// <summary>A cup held under a nozzle fills (a second and a half); then it's a drink.</summary>
    private static void Soda()
    {
        foreach (var (kind, _, live, _) in Stock)
        {
            if (kind != "soda_cup" || live == null || Full.Contains(live)) continue;
            var top = live.transform.TransformPoint(new Vector3(0f, 0.17f, 0f));
            var under = false;
            foreach (var n in Nozzles)
                if (new Vector2(top.x - n.x, top.z - n.z).magnitude < 0.07f && n.y - top.y > -0.02f && n.y - top.y < 0.2f)
                    under = true;
            if (!under)
            {
                Filling.Remove(live);
                continue;
            }

            if (!Filling.ContainsKey(live))
            {
                Filling[live] = Time.unscaledTime;
                Fx.Particles(top + Vector3.up * 0.1f, Quaternion.LookRotation(Vector3.down), 0, 60, 1.5f, 0.3f, 0.6f, 0.012f, new Color(0.35f, 0.15f, 0.08f, 0.8f), 1f, 3f, false, true, top.y - 0.02f);
                GameSounds.Play("bucket slosh", top, 0.5f, 1.4f);
            }
            else if (Time.unscaledTime - Filling[live] > 1.5f)
            {
                Full.Add(live);
                Filling.Remove(live);
                GameSounds.PlayOr("UI Star Goal Completed HUD", "step", top, 0.5f);
            }
        }
    }

    /// <summary>The popcorn cart: kick it and popcorn flies out of its kettle.</summary>
    private sealed class Cart : MonoBehaviour, FunWorld.IKickable
    {
        public void OnKicked(Vector3 foot, Vector3 facing)
        {
            GameSounds.PlayOr("Kick Medium Metal", "thud", transform.position + Vector3.up, 1f);
            Kernels(transform.TransformPoint(new Vector3(0f, 1.3f, 0f)), 16, 2.5f);
            gameObject.AddComponent<Wobble>();
        }
    }

    /// <summary>A bucket thrown hard spills popcorn where it lands.</summary>
    private sealed class Spill : MonoBehaviour
    {
        private void OnCollisionEnter(Collision c)
        {
            if (c.relativeVelocity.magnitude < 3.5f) return;
            Kernels(c.GetContact(0).point + Vector3.up * 0.1f, 10, 1.6f);
            GameSounds.Play("popcorn impacts", transform.position, 0.8f);
        }
    }

    private static int _kernels;

    private static void Kernels(Vector3 at, int n, float speed)
    {
        for (var k = 0; k < n && _kernels < 120; k++)
        {
            var go = FunWorld.Thing("popcorn_kernel", FunWorld.Local(at + Random.insideUnitSphere * 0.08f), Random.Range(0f, 360f), 0.005f, true);
            if (go == null) return;
            var rb = go.GetComponent<Rigidbody>();
            rb.WakeUp();
            rb.velocity = Random.insideUnitSphere * speed + Vector3.up * speed * 0.6f;
            rb.drag = 0.6f;
            _kernels++;
            go.AddComponent<Counted>();
            FunWorld.AddDebris(go, 25f);
        }

        if (n > 3) GameSounds.Play("popcorn impacts", at, 0.7f);
    }

    private sealed class Counted : MonoBehaviour
    {
        private void OnDestroy() => _kernels--;
    }

    private sealed class Wobble : MonoBehaviour
    {
        private float _born;
        private Transform _vis;

        private void Start()
        {
            _born = Time.unscaledTime;
            _vis = transform.Find("Visual");
        }

        private void Update()
        {
            var t = Time.unscaledTime - _born;
            if (_vis != null) _vis.localRotation = Quaternion.Euler(Mathf.Sin(t * 30f) * 4f * Mathf.Exp(-t * 5f), 0f, 0f);
            if (t > 1f)
            {
                if (_vis != null) _vis.localRotation = Quaternion.identity;
                Destroy(this);
            }
        }
    }

    // ================================================================================== the seats

    private static int _seatRow = -1;
    private static float _seatSide;

    /// <summary>In the aisle, by a row: grip and you sit in its seat by the aisle.</summary>
    private static void Seats()
    {
        if (CinemaRig.Seated) return;
        var head = MovieHouse.Root.InverseTransformPoint(CinemaRig.HeadPosition);
        if (Mathf.Abs(head.x) > 0.95f || head.z < 3.0f || head.z > 12.6f) return;
        foreach (var hand in new[] { XRNode.LeftHand, XRNode.RightHand })
        {
            if (!VrControls.Down(hand, VrButton.Grip) || FunWorld.Holding(hand)) continue;
            var p = MovieHouse.Root.InverseTransformPoint(VrHands.Get(hand).Position);
            var side = Mathf.Abs(p.x - head.x) > 0.05f ? Mathf.Sign(p.x - head.x) : (hand == XRNode.LeftHand ? -1f : 1f);
            var row = Mathf.Clamp(Mathf.RoundToInt((MovieHouse.FrontRowZ - (head.z + 0.1f)) / MovieHouse.RowDepth), 0, MovieHouse.Rows - 1);
            var seat = new Vector3(side * (MovieHouse.AisleHalf + 0.38f), row * MovieHouse.RowRise, MovieHouse.FrontRowZ - row * MovieHouse.RowDepth - 0.1f);
            _seatRow = row;
            _seatSide = side;
            CinemaRig.Sit(seat, row * MovieHouse.RowRise);
            GameSounds.PlayOr("Kick Soft", "step", MovieHouse.Root.TransformPoint(seat), 0.4f, 0.8f);
            Plugin.L.LogInfo($"Lobby: sat in row {row + 1}");
            return;
        }
    }

    /// <summary>Up out of the seat (the stick), into the aisle beside it.</summary>
    public static void GetUp()
    {
        var row = Mathf.Max(0, _seatRow);
        CinemaRig.Stand(new Vector3(_seatSide * 0.3f, 0f, MovieHouse.FrontRowZ - row * MovieHouse.RowDepth - 0.1f));
    }

    // ================================================================================== NOW SHOWING

    private sealed class Poster
    {
        public Renderer Picture;
        public TextMeshPro Title;
        public Level Level;
        public GameObject Box;
    }

    private static readonly List<Poster> Posters_ = new List<Poster>();
    private static readonly List<Level> Played = new List<Level>();
    private static int _page;
    private static Poster _armed;
    private static float _armedAt;
    private static TextMeshPro _hint;

    private static void Posters()
    {
        Posters_.Clear();
        _page = 0;
        _armed = null;
        var face = Vector3.left;
        var zs = new[] { -1.15f, 0.45f, 2.05f };
        foreach (var z in zs)
        {
            var at = new Vector3(6.93f, 1.85f, z);
            FunWorld.Picture("poster_frame", _root, at, Quaternion.Euler(0f, -90f, 0f));
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(q.GetComponent<Collider>());
            q.layer = VrUi.Layer;
            q.transform.SetParent(_root, false);
            q.transform.localPosition = at + new Vector3(-0.045f, 0f, 0f);
            q.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            q.transform.localScale = new Vector3(0.94f, 1.4f, 1f);
            var m = new Material(Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default")) { color = Color.white };
            q.GetComponent<MeshRenderer>().sharedMaterial = m;
            var box = FunWorld.Solid(at + new Vector3(-0.05f, 0f, 0f), new Vector3(1.1f, 1.6f, 0.1f), -90f);
            var poster = new Poster { Picture = q.GetComponent<MeshRenderer>(), Box = box };
            poster.Title = MovieHouse.SignFacing("", at + new Vector3(-0.06f, -1.02f, 0f), 0.1f, new Color(1f, 0.85f, 0.25f), face, _root);
            if (poster.Title != null)
            {
                poster.Title.rectTransform.sizeDelta = new Vector2(1.05f, 0.5f);
                poster.Title.enableWordWrapping = true;
            }
            FunWorld.MakePress(box, h => PressPoster(poster), 0.01f);
            Posters_.Add(poster);
        }

        MovieHouse.SignFacing("NOW SHOWING", new Vector3(6.93f, 3.05f, 0.45f), 0.42f, new Color(1f, 0.2f, 0.3f), face, _root);
        _hint = MovieHouse.SignFacing("POINT AT A POSTER AND PULL THE TRIGGER TO PLAY IT", new Vector3(6.93f, 2.72f, 0.45f), 0.11f, new Color(0.15f, 0.95f, 1f), face, _root);
        foreach (var (z, dir) in new[] { (-2.1f, -1), (3.0f, 1) })
        {
            var at = new Vector3(6.93f, 1.85f, z);
            MovieHouse.SignFacing(dir < 0 ? "<" : ">", at, 0.6f, new Color(1f, 0.16f, 0.62f), face, _root);
            var b = FunWorld.Solid(at + new Vector3(-0.05f, 0f, 0f), new Vector3(0.35f, 0.45f, 0.1f), -90f);
            var d = dir;
            FunWorld.MakePress(b, h => Page(d), 0.18f);
        }

        Played.Clear();
        var save = GameState.CurrentSave;
        var levels = ScriptableObjectSingleton<GameConfig>.Instance != null ? ScriptableObjectSingleton<GameConfig>.Instance.GameplayLevels : null;
        if (save != null && levels != null)
            foreach (var l in levels)
                if (l != null && l.PreviewScreenshot != null && save.GetLevelSave(l) is { HasCompletedLevel: true })
                    Played.Add(l);
        ShowPage();
    }

    private static void Page(int dir)
    {
        if (Played.Count == 0) return;
        var pages = (Played.Count + 2) / 3;
        _page = (_page + dir + pages) % pages;
        _armed = null;
        ShowPage();
        GameSounds.PlayOr("World Map Mapbook Turn Page", "step", CinemaRig.HeadPosition, 0.6f);
    }

    private static void ShowPage()
    {
        var save = GameState.CurrentSave;
        for (var i = 0; i < Posters_.Count; i++)
        {
            var p = Posters_[i];
            var k = _page * 3 + i;
            p.Level = k < Played.Count ? Played[k] : null;
            p.Picture.sharedMaterial.mainTexture = p.Level != null ? p.Level.PreviewScreenshot : null;
            p.Picture.sharedMaterial.color = p.Level != null ? Color.white : new Color(0.05f, 0.04f, 0.06f);
            if (p.Title == null) continue;
            if (p.Level == null)
            {
                p.Title.text = Played.Count == 0 && i == 1 ? "PLAY A LEVEL AND IT'S SHOWING HERE" : "";
                continue;
            }

            var ls = save?.GetLevelSave(p.Level);
            var stars = ls != null ? ls.CompletedStarGoals.Count : 0;
            var goals = p.Level.StarGoals != null ? p.Level.StarGoals.Length : 3;
            p.Title.text = $"{Name(p.Level)}\n{new string('*', stars)}{new string('-', Mathf.Max(0, goals - stars))}";
        }
    }

    private static string Name(Level l)
    {
        try
        {
            var t = l.LevelName != null ? l.LevelName.GetTranslation() : null;
            return string.IsNullOrEmpty(t) ? l.name.ToUpperInvariant() : t.ToUpperInvariant();
        }
        catch
        {
            return l.name.ToUpperInvariant();
        }
    }

    private static void PressPoster(Poster p)
    {
        if (p.Level == null) return;
        if (_armed != p || Time.unscaledTime - _armedAt > 3f)
        {
            _armed = p;
            _armedAt = Time.unscaledTime;
            if (_hint != null) _hint.text = $"AGAIN TO PLAY {Name(p.Level)}";
            GameSounds.PlayOr("World Map Hover over building", "step", CinemaRig.HeadPosition, 0.6f);
            return;
        }

        _armed = null;
        GameSounds.PlayOr("World Map Start Level", "door_bang", CinemaRig.HeadPosition, 1f);
        Plugin.L.LogInfo("Lobby: playing " + p.Level.name + " from its poster");
        if (GameState.IsGamePausedOrNotFocused) GameState.ResumeGame();
        GameState.LoadLevel(p.Level, LoadingTransition.FadeToAndFromBlack);
    }

    private static void PosterPress()
    {
        if (_armed != null && Time.unscaledTime - _armedAt > 3f)
        {
            _armed = null;
            if (_hint != null) _hint.text = "POINT AT A POSTER AND PULL THE TRIGGER TO PLAY IT";
        }
    }

    // ================================================================================== the shoe wall

    private static TextMeshPro _shoeNote;

    private static void Shoes()
    {
        var at = new Vector3(6.72f, 0f, -3.6f);
        FunWorld.Picture("shoe_shelf", _root, at, Quaternion.Euler(0f, -90f, 0f));
        MovieHouse.Block(at, 0.22f, 0.92f);
        FunWorld.Solid(at + new Vector3(0f, 1f, 0f), new Vector3(1.84f, 2f, 0.42f), -90f);
        MovieHouse.SignFacing("SHOES", new Vector3(6.93f, 2.45f, -3.6f), 0.4f, new Color(1f, 0.16f, 0.62f), Vector3.left, _root);
        _shoeNote = MovieHouse.SignFacing("POINT AND PULL THE TRIGGER (OR GRIP) TO WEAR A PAIR", new Vector3(6.55f, 2.17f, -3.6f), 0.1f, new Color(0.15f, 0.95f, 1f), Vector3.left, _root);
        var save = GameState.CurrentSave;
        var config = ScriptableObjectSingleton<GameConfig>.Instance;
        var template = Body.HeroModel.Template;
        if (save == null || config == null || config.Shoes == null || template == null) return;
        var slot = 0;
        foreach (var shoe in config.Shoes)
        {
            if (shoe == null || !save.IsShoeUnlocked(shoe)) continue;
            if (!Body.HeroModel.ShoeMeshes.TryGetValue(shoe, out var meshName)) continue;
            Mesh mesh = null;
            Material[] mats = null;
            foreach (var r in template.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.name == meshName)
                {
                    mesh = r.sharedMesh;
                    mats = r.sharedMaterials;
                }

            if (mesh == null || slot >= 24) continue;
            var shelf = slot / 6;
            var col = slot % 6;
            slot++;
            // (the pair as the model has them, at its feet: their middle on the board, turned to face you)
            var spot = new Vector3(6.68f, 0.49f + shelf * 0.45f + 0.02f, -3.6f - 0.75f + col * 0.3f);
            var go = new GameObject("Shoes " + shoe.name) { layer = FunWorld.PhysLayer };
            go.transform.SetParent(_root, false);
            go.transform.localPosition = spot;
            go.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            var vis = new GameObject("Visual") { layer = VrUi.Layer };
            vis.transform.SetParent(go.transform, false);
            var b = mesh.bounds;
            var scale = 0.26f / Mathf.Max(0.05f, Mathf.Max(b.size.x, b.size.z));
            vis.transform.localScale = Vector3.one * scale;
            vis.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z) * scale;
            vis.AddComponent<MeshFilter>().sharedMesh = mesh;
            vis.AddComponent<MeshRenderer>().sharedMaterials = mats;
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, b.size.y * scale / 2, 0f);
            box.size = new Vector3(0.28f, Mathf.Max(0.08f, b.size.y * scale), 0.3f);
            var s = shoe;
            FunWorld.MakePress(go, h => Wear(s), 0.01f);
            FunWorld.Handles.Add(new FunWorld.Handle { Where = () => go.transform.position + Vector3.up * 0.06f, Reach = 0.16f, Begin = (h, p) => Wear(s) });
        }
    }

    private static void Wear(ShoeType shoe)
    {
        var save = GameState.CurrentSave;
        if (save == null || shoe == null) return;
        save.SetRandomShoeOptionEnabled(false);
        save.SetSelectedShoeType(shoe);
        save.WriteToDisk();
        var name = shoe.name;
        try
        {
            if (shoe.ShoeName != null) name = shoe.ShoeName.GetTranslation();
        }
        catch
        {
            // (its own name, then)
        }

        if (_shoeNote != null) _shoeNote.text = $"WEARING {name.ToUpperInvariant()}{(SingletonBehaviour<Player>.HasInstance ? " (FROM YOUR NEXT LEVEL)" : "")}";
        GameSounds.PlayOr("Shoe Short shoe", "step", CinemaRig.HeadPosition, 0.8f);
        Plugin.L.LogInfo("Lobby: shoes " + shoe.name);
    }

    // ================================================================================== the photo kiosk

    private static Transform _kiosk;
    private static TextMeshPro _countdown;
    private static float _shotAt = -1f;

    private static void Kiosk()
    {
        var at = new Vector3(6.6f, 0f, -5.4f);
        var go = FunWorld.Picture("photo_kiosk", _root, at, Quaternion.Euler(0f, -90f, 0f));
        _kiosk = go != null ? go.transform : null;
        FunWorld.Solid(at + new Vector3(0f, 0.98f, 0f), new Vector3(0.72f, 1.95f, 0.52f), -90f);
        MovieHouse.Block(at, 0.28f, 0.38f);
        if (_kiosk == null) return;
        MovieHouse.SignFacing("PHOTO", MovieHouse.Root.InverseTransformPoint(_kiosk.TransformPoint(new Vector3(0f, 2.05f, 0.27f))), 0.3f, new Color(1f, 0.85f, 0.25f), Vector3.left, _root);
        _countdown = MovieHouse.SignFacing("PRESS", MovieHouse.Root.InverseTransformPoint(_kiosk.TransformPoint(new Vector3(0f, 1.3f, 0.29f))), 0.12f, new Color(0.15f, 0.95f, 1f), Vector3.left, _root);
        var button = new GameObject("Photo button") { layer = FunWorld.PhysLayer };
        button.transform.SetParent(_root, false);
        button.transform.position = _kiosk.TransformPoint(new Vector3(-0.18f, 1.05f, 0.29f));
        button.AddComponent<BoxCollider>().size = Vector3.one * 0.12f;
        FunWorld.MakePress(button, h => Smile(), 0.1f);
    }

    private static void Smile()
    {
        if (_shotAt > 0f) return;
        _shotAt = Time.unscaledTime + 3f;
        GameSounds.PlayOr("UI Button Click", "step", _kiosk.position + Vector3.up, 0.8f);
    }

    private static void Countdown()
    {
        if (_countdown == null) return;
        if (_shotAt < 0f)
        {
            _countdown.text = "PRESS\nTHE BUTTON";
            return;
        }

        var left = _shotAt - Time.unscaledTime;
        if (left > 0f)
        {
            _countdown.text = Mathf.CeilToInt(left).ToString();
            return;
        }

        _shotAt = -1f;
        Snap();
    }

    private static void Snap()
    {
        var lens = _kiosk.TransformPoint(new Vector3(0f, 1.6f, 0.3f));
        var look = _kiosk.TransformDirection(Vector3.forward);
        Fx.MuzzleFlash(lens + look * 0.05f, look, 2f);
        GameSounds.PlayOr("Collectible Pickup", "step", lens, 0.8f);
        var tex = Photo.Take(lens, look);
        if (tex == null) return;
        // the strip drops out of its slot
        var slot = _kiosk.TransformPoint(new Vector3(0.18f, 0.95f, 0.3f));
        var strip = FunWorld.Thing("photo_strip", FunWorld.Local(slot), 0f, 0.02f);
        if (strip == null) return;
        strip.transform.rotation = Quaternion.LookRotation(look) * Quaternion.Euler(0f, 180f, 0f);
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.Destroy(q.GetComponent<Collider>());
        q.layer = VrUi.Layer;
        q.transform.SetParent(strip.transform, false);
        q.transform.localPosition = new Vector3(0f, 0.105f, 0.0025f);
        q.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        q.transform.localScale = new Vector3(0.064f, 0.08f, 1f);
        q.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default")) { mainTexture = tex };
        var rb = strip.GetComponent<Rigidbody>();
        rb.WakeUp();
        rb.velocity = look * 0.5f;
        FunWorld.MakeGrabbable(strip, new Vector3(0f, 0.1f, 0f), Quaternion.identity).Reach = 0.14f;
        FunWorld.AddDebris(strip, 300f);
    }

    // ================================================================================== the switch, the cord

    private static Transform _blade;
    private static float _switchStartY;

    private static void Switch()
    {
        var at = new Vector3(-1.35f, 1.35f, -5.98f);
        FunWorld.Picture("knife_switch", _root, at, Quaternion.identity);
        var blade = FunWorld.Picture("knife_switch_blade", _root, at + new Vector3(0f, -0.12f, 0.06f), Quaternion.Euler(MovieHouse.LightsOn ? 0f : 160f, 0f, 0f));
        _blade = blade != null ? blade.transform : null;
        MovieHouse.SignFacing("HOUSE LIGHTS", at + new Vector3(0f, 0.3f, 0.02f), 0.12f, new Color(1f, 0.85f, 0.25f), Vector3.forward, _root);
        FunWorld.Handles.Add(new FunWorld.Handle
        {
            Where = () => _blade != null ? _blade.TransformPoint(new Vector3(0f, 0.22f, 0.06f)) : Vector3.zero,
            Reach = 0.15f,
            Begin = (h, p) => _switchStartY = p.y,
            Drag = (h, p) =>
            {
                var d = p.y - _switchStartY;
                if (MovieHouse.LightsOn && d < -0.15f) Throw(false, h);
                else if (!MovieHouse.LightsOn && d > 0.15f) Throw(true, h);
            },
        });
    }

    private static void Throw(bool on, XRNode hand)
    {
        MovieHouse.HouseLights(on);
        if (_blade != null) _blade.localRotation = Quaternion.Euler(on ? 0f : 160f, 0f, 0f);
        GameSounds.PlayOr("Kick Door Lever", "thud", _blade != null ? _blade.position : CinemaRig.HeadPosition, 0.9f);
        VrControls.Rumble(hand, 0.6f, 0.08f);
        _switchStartY = VrHands.Get(hand).Position.y;
    }

    private static Transform _cord;
    private static float _cordStartY, _cordPulled;

    private static void Cord()
    {
        var at = new Vector3(-1.3f, 3.35f, 12.9f);
        var go = FunWorld.Picture("pull_cord", _root, at, Quaternion.identity);
        _cord = go != null ? go.transform : null;
        FunWorld.Handles.Add(new FunWorld.Handle
        {
            Where = () => _cord != null ? _cord.TransformPoint(new Vector3(0f, -1.72f, 0f)) : Vector3.zero,
            Reach = 0.2f,
            Begin = (h, p) =>
            {
                _cordStartY = p.y;
                _cordPulled = 0f;
            },
            Drag = (h, p) =>
            {
                var d = Mathf.Clamp(_cordStartY - p.y, 0f, 0.3f);
                if (_cord != null) _cord.localPosition = at + Vector3.down * d;
                if (d > 0.22f && _cordPulled < 0.22f)
                {
                    MovieHouse.Curtains(!MovieHouse.CurtainsClosed);
                    GameSounds.PlayOr("Kick Soft", "cardboard", _cord.position, 0.7f, 0.7f);
                    VrControls.Rumble(h, 0.5f, 0.08f);
                }

                _cordPulled = d;
            },
            End = h =>
            {
                if (_cord != null) _cord.localPosition = at;
            },
        });
    }

    // ================================================================================== the cardboard audience

    private static void Audience()
    {
        var rnd = new System.Random(11);
        string[] kinds = { "cutout_goon", "cutout_goon2", "cutout_granny" };
        foreach (var (row, seat, side) in new[] { (1, 2, -1), (2, 5, 1), (3, 1, 1), (4, 4, -1), (0, 6, 1), (5, 3, -1) })
        {
            var x = side * (MovieHouse.AisleHalf + 0.38f + seat * MovieHouse.SeatPitch);
            var z = MovieHouse.FrontRowZ - row * MovieHouse.RowDepth - 0.1f;
            var vis = FunWorld.Picture(kinds[rnd.Next(3)], _root, new Vector3(x, row * MovieHouse.RowRise + 0.42f, z), Quaternion.Euler(0f, rnd.Next(-8, 8), 0f));
            if (vis != null) vis.transform.localScale = Vector3.one * 0.62f;
        }

        foreach (var (x, z, yaw, kind) in new[] { (2.5f, -4.9f, 10f, "cutout_goon"), (-3.5f, -1.4f, 70f, "cutout_goon2") })
        {
            var go = FunWorld.Thing(kind, new Vector3(x, 0f, z), yaw, 2f);
            if (go == null) continue;
            var rb = go.GetComponent<Rigidbody>();
            rb.WakeUp();
            var anchor = new GameObject("Cutout base") { layer = FunWorld.PhysLayer };
            anchor.transform.SetParent(_root, false);
            anchor.transform.localPosition = new Vector3(x, 0f, z);
            var arb = anchor.AddComponent<Rigidbody>();
            arb.isKinematic = true;
            var h = go.AddComponent<HingeJoint>();
            h.connectedBody = arb;
            h.anchor = Vector3.zero;
            h.axis = Vector3.right;
            h.useSpring = true;
            h.spring = new JointSpring { spring = 160f, damper = 4f, targetPosition = 0f };
            h.useLimits = true;
            h.limits = new JointLimits { min = -80f, max = 80f };
            go.AddComponent<Kicked>().Body = rb;
            MovieHouse.Block(new Vector3(x, 0f, z), 0.2f, 0.2f);
        }
    }

    private sealed class Kicked : MonoBehaviour, FunWorld.IKickable
    {
        public Rigidbody Body;

        public void OnKicked(Vector3 foot, Vector3 facing)
        {
            Body.AddForceAtPosition(facing * 9f, transform.position + Vector3.up * 1.2f, ForceMode.VelocityChange);
            GameSounds.PlayOr("Cardboard box Impacts", "cardboard", transform.position + Vector3.up, 1f);
        }
    }

    public static void Clear()
    {
        _built = false;
        _root = null;
        Stock.Clear();
        Filling.Clear();
        Full.Clear();
        Posters_.Clear();
        _kiosk = null;
        _countdown = null;
        _blade = _cord = null;
        _shotAt = -1f;
        _kernels = 0;
        MovieHouse.Obstacles.Clear();
    }

    public static string Describe() => $"LOBBY built {_built} stock {Stock.Count} played {Played.Count} page {_page} seated {CinemaRig.Seated}";
}

/// <summary>The photo kiosk's camera: you as the mirror shows you (head and all), the movie house behind.</summary>
internal static class Photo
{
    public static Texture2D Take(Vector3 at, Vector3 look)
    {
        try
        {
            var go = new GameObject("AngerFootVR photo camera");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.transform.SetPositionAndRotation(at, Quaternion.LookRotation(look));
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 40f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = (1 << VrUi.Layer) | (1 << Body.VrBody.MirrorLayer);
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            var rt = RenderTexture.GetTemporary(640, 800, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            // (your body without its head is the headset's; the mirror's has the head: only that one in the picture)
            var hidden = new List<Renderer>();
            if (Body.VrBody.Root != null)
                foreach (var r in Body.VrBody.Root.GetComponentsInChildren<Renderer>())
                    if (r.enabled)
                    {
                        r.enabled = false;
                        hidden.Add(r);
                    }

            cam.Render();
            foreach (var r in hidden) r.enabled = true;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Object.Destroy(go);
            Save(tex);
            return tex;
        }
        catch (System.Exception e)
        {
            Plugin.L.LogWarning("Photo: " + e.Message);
            return null;
        }
    }

    private static void Save(Texture2D tex)
    {
        try
        {
            var dir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyPictures), "Anger Foot VR");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "Anger Foot VR " + System.DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss") + ".png");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Plugin.L.LogInfo("Photo saved: " + file);
        }
        catch (System.Exception e)
        {
            Plugin.L.LogWarning("Photo not saved: " + e.Message);
        }
    }
}
