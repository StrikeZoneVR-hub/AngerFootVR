using System.Collections.Generic;
using AngerFootVR.Core;
using Framework;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.UI;

/// <summary>
/// The rage room's goon dummy: one of the game's own goons (copied the first time you pause in a level: its
/// pictures, its bones and its ragdoll's bodies and joints, every script taken off before anything of it wakes) held
/// up on a weighted spring stand. Kick it, bat it, throw things at it: it flails and springs back up, grunting in the
/// game's own goon voices. Three good kicks in a row (or a barrel going off) knock it clean off its stand; a fresh
/// one's back a few seconds later. Until a goon's been copied, a cardboard goon wobbles on the stand.
/// </summary>
internal static class Dummy
{
    private static GameObject _template;
    private static Transform _root;
    private static Vector3 _at;
    private static GameObject _live, _stand;
    private static Rigidbody _standBody;
    private static readonly List<Rigidbody> Bones = new List<Rigidbody>();
    private static readonly List<Joint> Springs = new List<Joint>();
    private static float _freedAt = -1f, _lastHit = -10f, _lastVoice = -10f;
    private static int _streak;
    private static bool _cardboard, _hooked;

    /// <summary>In a level (the pause menu): a goon of the game's copied, once a session.</summary>
    public static void TryCapture()
    {
        if (_template != null || !SingletonBehaviour<Player>.HasInstance) return;
        try
        {
            // (a fighter first; the cops some levels have, who are "NPCs" to the game, when there's no one else)
            var all = Object.FindObjectsOfType<Enemy>();
            for (var pass = 0; pass < 2; pass++)
                foreach (var e in all)
                {
                    if (e == null || (e.IsNPC != (pass == 1)) || e.IsBoss || e.Health == null || !e.Health.IsAlive || e.Rig is not HumanoidRig) continue;
                    if (e.GetComponentInChildren<SuicideBomber>() != null || e.Rig is WallClimbingRig) continue;
                    if (e.GetComponentsInChildren<Rigidbody>(true).Length < 6) continue; // (no ragdoll)
                    Capture(e);
                    if (_template != null) return;
                }
        }
        catch (System.Exception ex)
        {
            Plugin.L.LogWarning("Dummy: " + ex.Message);
        }
    }

    private static void Capture(Enemy e)
    {
        var holder = new GameObject("AngerFootVR dummy template");
        holder.SetActive(false);
        Object.DontDestroyOnLoad(holder);
        // (made inside a switched-off holder: nothing of it wakes before its scripts are gone)
        var copy = Object.Instantiate(e.gameObject, holder.transform);
        copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        foreach (var w in copy.GetComponentsInChildren<global::Weapon>(true)) Object.DestroyImmediate(w.gameObject);
        for (var pass = 0; pass < 3; pass++)
            foreach (var c in copy.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c is Transform || c is SkinnedMeshRenderer || c is MeshRenderer || c is MeshFilter || c is Rigidbody || c is Joint) continue;
                if (c is Collider col && !col.isTrigger) continue;
                try
                {
                    Object.DestroyImmediate(c);
                }
                catch
                {
                    // (the next pass)
                }
            }

        foreach (var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = t.GetComponent<Collider>() != null ? FunWorld.PhysLayer : VrUi.Layer;
        foreach (var r in copy.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (r is SkinnedMeshRenderer s) s.updateWhenOffscreen = true;
        }

        var bodies = copy.GetComponentsInChildren<Rigidbody>(true);
        foreach (var b in bodies)
        {
            b.isKinematic = false;
            b.useGravity = true;
            b.interpolation = RigidbodyInterpolation.None;
            b.drag = 0.2f;
            b.angularDrag = 1f;
            b.collisionDetectionMode = CollisionDetectionMode.Discrete;
        }

        copy.name = "AngerFootVR dummy " + e.name;
        _template = copy;
        Plugin.L.LogInfo($"Dummy: copied the goon '{e.name}' ({bodies.Length} ragdoll bodies)");
    }

    public static void Build(Transform root, Vector3 at)
    {
        if (!_hooked)
        {
            _hooked = true;
            FunWorld.Tick += Tick;
        }

        _root = root;
        _at = at;
        _stand = FunWorld.Thing("dummy_stand", at, 0f, 60f, false, true);
        _standBody = _stand != null ? _stand.GetComponent<Rigidbody>() : null;
        Spawn();
    }

    private static void Spawn()
    {
        if (_live != null) Object.Destroy(_live);
        Bones.Clear();
        Springs.Clear();
        _freedAt = -1f;
        _streak = 0;
        if (_root == null || _standBody == null) return;
        if (_template == null)
        {
            Cardboard();
            return;
        }

        _cardboard = false;
        _live = Object.Instantiate(_template, _root);
        _live.SetActive(true);
        _live.transform.localPosition = _at + new Vector3(0f, 0.06f, 0.12f);
        _live.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // (facing the door)
        _live.AddComponent<Body>();
        foreach (var b in _live.GetComponentsInChildren<Rigidbody>())
        {
            Bones.Add(b);
            b.gameObject.AddComponent<Part>();
        }

        // held up by springs at its hips, chest and head, from the stand (its limbs hang free)
        Hold("Root_M", 2600f, 90f);
        Hold("Chest_M", 900f, 50f);
        Hold("Spine1_M", 500f, 40f);
        Hold("Neck_M", 120f, 12f);
        if (Springs.Count == 0 && Bones.Count > 0)
        {
            // (other bone names: the lowest-down body and the highest)
            Bones.Sort((a, b) => a.position.y.CompareTo(b.position.y));
            Spring(Bones[Bones.Count / 3], 2600f, 90f);
            Spring(Bones[Bones.Count * 2 / 3], 900f, 50f);
        }
    }

    private static void Hold(string bone, float spring, float damper)
    {
        foreach (var b in Bones)
            if (b.name == bone)
            {
                Spring(b, spring, damper);
                return;
            }
    }

    private static void Spring(Rigidbody b, float spring, float damper)
    {
        var j = b.gameObject.AddComponent<SpringJoint>();
        j.connectedBody = _standBody;
        j.autoConfigureConnectedAnchor = false;
        j.anchor = Vector3.zero;
        j.connectedAnchor = _standBody.transform.InverseTransformPoint(b.position);
        j.spring = spring;
        j.damper = damper;
        j.minDistance = 0f;
        j.maxDistance = 0.01f;
        j.enableCollision = false;
        Springs.Add(j);
    }

    /// <summary>No goon copied yet: a cardboard one, on a springy hinge.</summary>
    private static void Cardboard()
    {
        _cardboard = true;
        _live = FunWorld.Thing("cutout_goon", _at + new Vector3(0f, 0.86f, 0f), 180f, 3f);
        if (_live == null) return;
        _live.AddComponent<Body>();
        var rb = _live.GetComponent<Rigidbody>();
        rb.WakeUp();
        Bones.Add(rb);
        var h = _live.AddComponent<HingeJoint>();
        h.connectedBody = _standBody;
        h.anchor = Vector3.zero;
        h.axis = Vector3.right;
        h.useSpring = true;
        h.spring = new JointSpring { spring = 220f, damper = 6f, targetPosition = 0f };
        h.useLimits = true;
        h.limits = new JointLimits { min = -75f, max = 75f };
        Springs.Add(h);
    }

    private sealed class Body : MonoBehaviour, FunWorld.IKickable
    {
        public void OnKicked(Vector3 foot, Vector3 facing) => Kicked(foot, facing);
    }

    private sealed class Part : MonoBehaviour
    {
        private void OnCollisionEnter(Collision c)
        {
            var v = c.relativeVelocity.magnitude;
            if (v < 4f || c.collider.attachedRigidbody == null) return;
            if (_live != null && c.collider.transform.IsChildOf(_live.transform)) return; // (its own limbs)
            Struck(c.GetContact(0).point, v);
        }
    }

    private static void Kicked(Vector3 foot, Vector3 facing)
    {
        Rigidbody best = null;
        var bestD = float.MaxValue;
        foreach (var b in Bones)
        {
            if (b == null) continue;
            var d = (b.position - foot).sqrMagnitude;
            if (d < bestD)
            {
                bestD = d;
                best = b;
            }
        }

        if (best == null) return;
        var push = facing * (_cardboard ? 5f : 7f) + Vector3.up * 2f;
        best.AddForce(push, ForceMode.VelocityChange);
        foreach (var b in Bones)
            if (b != null && b != best)
                b.AddForce(push * 0.3f, ForceMode.VelocityChange);
        var now = Time.unscaledTime;
        _streak = now - _lastHit < 2.5f ? _streak + 1 : 1;
        Struck(best.position, 9f);
        if (_streak >= 3 && Springs.Count > 0) Free(push * 1.6f);
    }

    private static void Struck(Vector3 at, float v)
    {
        var now = Time.unscaledTime;
        if (now - _lastHit < 0.25f) return;
        _lastHit = now;
        GameSounds.PlayOr(_cardboard ? "Cardboard box Impacts" : "Body impacts", "thud", at, Mathf.Clamp01(v / 9f));
        if (!_cardboard && now - _lastVoice > 1.2f)
        {
            _lastVoice = now;
            GameSounds.Play(Random.value < 0.5f ? "Voice Growly" : "Voice Deep", at, 0.8f);
        }

        RageMode.DummyHit(at);
    }

    /// <summary>Knocked clean off its stand: it flies, and a fresh one's back after a while.</summary>
    private static void Free(Vector3 push)
    {
        foreach (var j in Springs)
            if (j != null)
                Object.Destroy(j);
        Springs.Clear();
        foreach (var b in Bones)
            if (b != null)
                b.AddForce(push, ForceMode.VelocityChange);
        _freedAt = Time.unscaledTime;
        if (!_cardboard) GameSounds.Play("Gimp Fall Scream", _live != null ? _live.transform.position : FunWorld.World(_at), 0.8f);
    }

    /// <summary>A blast near it (a barrel): knocked off its stand and flung.</summary>
    public static void Blast(Vector3 at, float radius)
    {
        if (_live == null || Bones.Count == 0 || Bones[0] == null) return;
        if ((Bones[0].position - at).magnitude > radius) return;
        if (Springs.Count > 0) Free(Vector3.up * 2f);
        foreach (var b in Bones)
            if (b != null)
                b.AddExplosionForce(9f, at, radius, 0.8f, ForceMode.VelocityChange);
    }

    private static void Tick()
    {
        if (_freedAt > 0f && Time.unscaledTime - _freedAt > 6f)
        {
            // (a puff, and a fresh one on the stand)
            Fx.Smoke(FunWorld.World(_at + Vector3.up), 0.6f, 1.2f);
            Spawn();
            GameSounds.PlayOr("Kick Soft", "step_hard", FunWorld.World(_at + Vector3.up), 0.5f);
        }

        // a copied goon that's turned up since the cardboard one: it takes over
        if (_cardboard && _template != null) Spawn();
    }

    public static void Reset() => Spawn();

    public static void Clear()
    {
        _live = _stand = null;
        _standBody = null;
        _root = null;
        Bones.Clear();
        Springs.Clear();
    }

    public static string Describe() => $"DUMMY template {(_template != null ? _template.name : "none")} live {(_live != null ? (_cardboard ? "cardboard" : _live.name) : "-")} bodies {Bones.Count} springs {Springs.Count}";
}
