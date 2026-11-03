using System;
using UnityEngine;

namespace AngerFootVR.Body;

/// <summary>
/// The invisible skeleton the body is posed from: InkVR's (HenryBody's posing, tested there for months), in Henry's
/// metres (eyes 1.62 m up), scaled to you by k. From your head and hands, every frame:
/// - the body faces the way your head does, letting it turn 35 degrees on its own, a little towards where your hands
///   are working;
/// - the neck sits behind and below your eyes; looking down tips the head forward over it, so the chest drops back
///   out of your view and your legs and feet come into it, the hips easing back while the feet stay planted;
/// - crouching, the knees take most of it and the back stays fairly upright;
/// - the feet plant on the ground (stairs and ramps too) and step, one at a time, when they fall behind, the body
///   turns or the ground changes; legs bend forward over the feet, which stay flat.
/// Anger Foot's own on top: past a jog the feet stride with your speed, a kick swings the leg out and back, and in the
/// air the legs hang tucked under you. HeroSkin puts the game's model on it.
/// </summary>
internal sealed class Skeleton
{
    public const float EyeHeight = 1.62f;

    // ---- rest skeleton (Henry's metres) ----
    private static readonly Vector3 HipC = new Vector3(0, 0.95f, 0), NeckB = new Vector3(0, 1.45f, -0.02f);
    private static Vector3 HipJ(float s) => new Vector3(0.095f * s, 0.93f, 0);
    private static Vector3 Knee(float s) => new Vector3(0.095f * s, 0.5f, 0.015f);
    private static Vector3 Ankle(float s) => new Vector3(0.1f * s, 0.085f, -0.01f);
    private static Vector3 Toe(float s) => Ankle(s) + new Vector3(0, -0.04f, 0.17f);
    private static readonly float ThighLen = (HipJ(1) - Knee(1)).magnitude, ShinLen = (Knee(1) - Ankle(1)).magnitude;

    /// <summary>The solved pose, world space (what HeroSkin reads).</summary>
    public struct Pose
    {
        public Vector3 Eye;
        public Quaternion HeadRot;
        public float K; // world units per metre of Henry
        public Quaternion Pelvis, Spine, Chest, Neck; // torso frames: z along the bone, y forward
        public Vector3 NeckBase;
        public Vector3 HipL, HipR, KneeL, KneeR, AnkleL, AnkleR;
        public Quaternion FootL, FootR; // z towards the toes, y up
        public float GroundL, GroundR;
        public float HenryLeg; // hip to ankle, standing (world)
        public Vector3 HenryAnkleOffset; // ankle minus hip, standing, in Henry's own frame (metres)
        public bool Low; // the head down near the floor: no body under it
    }

    private struct Foot
    {
        public Vector3 Planted, From, To;
        public float Yaw, FromYaw, ToYaw, T, Duration;
        public bool Stepping;
    }

    private readonly Foot[] _feet = new Foot[2];
    private bool _init;
    private float _yaw, _phase;
    private int _nextFoot;

    /// <summary>How a leg kicks (the game's kicks, as its first-person legs do them).</summary>
    public enum KickStyle { High, Sweep, Stomp, Low }

    // each leg's kick: its time (0..1, 1: none), its wait before it starts (s), its kind
    private readonly float[] _kickT = { 1f, 1f };
    private readonly float[] _kickWait = { 0f, 0f };
    private readonly KickStyle[] _kickStyle = { KickStyle.High, KickStyle.High };
    private Vector3 _lastPelvis, _velocity;
    private bool _running;

    public Pose Last;

    /// <summary>How fast the floor under you moves (world, per second): the feet keep up with it instead of sliding.</summary>
    public Vector3 GroundVelocity;

    private const float KickTime = 0.42f;

    // the kick's height above the hip and its reach (of the leg's length): tunable from the dev console (kick lift reach).
    // The game's own first-person kick puts the foot out level with your eyes; a body's leg kicks head-high: the foot
    // ends about 15 cm under your eyes, the leg nearly straight (0.55 up, 0.68 of the leg out)
    public static float KickLift = 0.55f, KickReach = 0.68f;

    /// <summary>Dev: the kick held at full stretch (for looking at it).</summary>
    public static bool DevKickFreeze;

    private static float YawOf(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
    private static float Smooth(float t) => t * t * (3f - 2f * t);

    /// <summary>Forget the feet (a level load, a teleport, the movie house): they plant afresh under you.</summary>
    public void Reset() => _init = false;

    /// <summary>The game kicked: the left leg swings out and back, as the game's own leg does (the right if the left is
    /// mid-step).</summary>
    public void Kick() => Kick(_feet[0].Stepping ? 1 : 0, KickStyle.High, 0f);

    /// <summary>A leg kicks (0 left, 1 right), after a wait (the flying kick's second leg).</summary>
    public void Kick(int leg, KickStyle style, float wait)
    {
        _kickT[leg] = 0f;
        _kickWait[leg] = wait;
        _kickStyle[leg] = style;
    }

    /// <param name="eye">Your eyes (world).</param>
    /// <param name="headRot">Your head (world).</param>
    /// <param name="k">World units per metre of Henry (your standing eye height / 1.62).</param>
    /// <param name="groundAt">The ground's height (world y) under a world point.</param>
    /// <param name="airborne">Off the ground (a jump, a fall): the legs hang tucked.</param>
    public Pose Solve(Vector3 eye, Quaternion headRot, float k, Vector3? handLeft, Vector3? handRight, Func<Vector3, float> groundAt, bool airborne, float dt)
    {
        var p = new Pose { Eye = eye, HeadRot = headRot, K = k };

        // --- facing: follow the head, a little towards where the hands are working ---
        var headFwd = headRot * Vector3.forward;
        var flat = new Vector3(headFwd.x, 0, headFwd.z);
        if (flat.sqrMagnitude < 1e-4f) flat = headRot * Vector3.up; // looking straight down: the top of the head says
        var headYaw = YawOf(flat);
        var target = headYaw;
        if (handLeft.HasValue && handRight.HasValue)
        {
            var mid = (handLeft.Value + handRight.Value) * 0.5f - eye;
            mid.y = 0;
            if (mid.magnitude > 0.15f * k)
            {
                var d = Mathf.DeltaAngle(headYaw, YawOf(mid));
                if (Mathf.Abs(d) < 75f) target = headYaw + d * 0.4f;
            }
        }

        var rel = _velocity - GroundVelocity;
        var speed = new Vector3(rel.x, 0, rel.z).magnitude / Mathf.Max(k, 1e-3f);
        if (!_init) _yaw = target;
        if (dt > 0)
        {
            var off = Mathf.DeltaAngle(_yaw, target);
            if (Mathf.Abs(off) > 35f) _yaw += off - Mathf.Sign(off) * 35f;
            _yaw = Mathf.MoveTowardsAngle(_yaw, target, dt * (speed > 0.3f ? 220f : 45f));
        }

        var yawRot = Quaternion.Euler(0, _yaw, 0);
        var f = yawRot * Vector3.forward;
        var up = Vector3.up;

        // --- the neck behind and below your eyes; looking down tips the head forward over it ---
        var pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(-headFwd.y, -1f, 1f)) * Mathf.Rad2Deg, -20f, 80f) * Mathf.Deg2Rad;
        var lean = 0.18f * k;
        var neckBase = eye + up * -(0.18f * k - lean * (1f - Mathf.Cos(pitch))) - f * (0.13f * k + lean * Mathf.Sin(Mathf.Max(0f, pitch)));
        // the hips ease back a little too, the feet staying planted, so the legs angle forward into view
        var neckStill = eye + up * -(0.18f * k - lean * (1f - Mathf.Cos(pitch))) - f * (0.13f * k + 0.12f * k * Mathf.Sin(Mathf.Max(0f, pitch)));
        var neckTop = neckBase + (eye - neckBase).normalized * (0.08f * k);
        var floorY = groundAt(new Vector3(neckStill.x, eye.y, neckStill.z));
        // the head near the floor (lying down, tracking not started): no body can stand under it
        p.Low = eye.y - floorY < 0.62f * k;

        var spineLen = (NeckB - HipC).magnitude * k;
        var stand = floorY + HipC.y * k;
        var ideal = neckBase.y - spineLen;
        Vector3 pelvis;
        if (ideal >= stand)
        {
            pelvis = neckStill - f * (0.02f * k);
            pelvis.y = Mathf.Min(ideal, stand + 0.04f * k);
        }
        else
        {
            var drop = stand - ideal;
            var py = Mathf.Max(stand - drop * 0.9f, floorY + 0.3f * k); // the knees take most of a crouch
            var vert = neckBase.y - py;
            var horiz = Mathf.Sqrt(Mathf.Max(0, spineLen * spineLen - vert * vert));
            pelvis = neckStill - f * (horiz + 0.02f * k);
            pelvis.y = py;
        }

        if (dt > 0)
        {
            if (_init && (pelvis - _lastPelvis).magnitude > 1.5f * k) _init = false; // teleported
            _velocity = _init ? Vector3.Lerp(_velocity, (pelvis - _lastPelvis) / dt, 1f - Mathf.Exp(-dt * 8f)) : Vector3.zero;
            _lastPelvis = pelvis;
        }

        var tu = (neckBase - pelvis).normalized;
        var ft = Vector3.ProjectOnPlane(f, tu).normalized;
        var rt = Vector3.Cross(tu, ft);
        var waist = Vector3.Lerp(pelvis, neckBase, 0.3f);
        var chest = Vector3.Lerp(pelvis, neckBase, 0.66f) + ft * (0.01f * k);
        p.Pelvis = Frame(waist - pelvis, ft);
        p.Spine = Frame(chest - waist, ft);
        p.Chest = Frame(neckBase - chest, ft);
        p.Neck = Frame(neckTop - neckBase, Vector3.ProjectOnPlane(headFwd, (neckTop - neckBase).normalized));
        p.NeckBase = neckBase;

        // --- feet ---
        var rFlat = yawRot * Vector3.right;
        if (!_init)
        {
            for (var i = 0; i < 2; i++)
            {
                var s = i == 0 ? -1f : 1f;
                var at = new Vector3(pelvis.x, 0, pelvis.z) + rFlat * (0.1f * s * k);
                at.y = groundAt(at + up * (eye.y - at.y));
                _feet[i] = new Foot { Planted = at, Yaw = _yaw };
            }

            _init = true;
        }

        // past a jog the feet stride with your speed (Anger Foot is fast); slower, they plant and step
        var wasRunning = _running;
        _running = speed > (_running ? 1.3f : 1.8f) && !airborne;
        if (wasRunning && !_running)
            for (var i = 0; i < 2; i++)
            {
                _feet[i].Planted = RunFoot(i, pelvis, rFlat, rel, k, groundAt, eye.y);
                _feet[i].Stepping = false;
                _feet[i].Yaw = _yaw;
            }

        if (dt > 0)
        {
            if (_running)
            {
                var stride = Mathf.Clamp(0.45f + speed * 0.12f, 0.6f, 1.4f);
                _phase = Mathf.Repeat(_phase + dt * speed / stride * Mathf.PI, Mathf.PI * 2f);
            }
            else Step(pelvis, rFlat, eye.y, k, speed, groundAt, dt);

            for (var leg = 0; leg < 2; leg++)
            {
                if (_kickWait[leg] > 0f)
                {
                    _kickWait[leg] -= dt;
                    continue;
                }

                _kickT[leg] = Mathf.Min(1f, _kickT[leg] + dt / KickTime);
                if (DevKickFreeze && _kickT[leg] > 0.4f && _kickT[leg] < 1f) _kickT[leg] = 0.4f;
            }
        }

        for (var i = 0; i < 2; i++)
        {
            var s = i == 0 ? -1f : 1f;
            var ground = _running ? RunFoot(i, pelvis, rFlat, rel, k, groundAt, eye.y) : FootPos(i, k);
            var footYaw = _running ? YawOf(new Vector3(rel.x, 0, rel.z)) : _feet[i].Stepping ? Mathf.LerpAngle(_feet[i].FromYaw, _feet[i].ToYaw, Smooth(_feet[i].T)) : _feet[i].Yaw;
            if (_running && Mathf.Abs(Mathf.DeltaAngle(footYaw, _yaw)) > 100f) footYaw = _yaw; // backing up: the feet still face ahead
            var footRot = Quaternion.Euler(0, footYaw + 7f * s, 0);
            var hip = pelvis + rt * (0.095f * s * k) - tu * (0.02f * k);
            var ankleTarget = ground + up * (Ankle(1).y * k);
            var footFwd = footRot * Vector3.forward;
            var sole = footRot;

            if (airborne)
            {
                // in the air: the legs hang under you, knees a little up
                ankleTarget = hip - tu * ((ThighLen + ShinLen) * 0.82f * k) - ft * (0.08f * k) + rt * (0.03f * s * k);
                sole = Quaternion.LookRotation(Quaternion.AngleAxis(25f, rt) * ft, up);
                ground = ankleTarget - up * (Ankle(1).y * k);
            }

            if (_kickT[i] < 1f && _kickWait[i] <= 0f)
            {
                // the kick: snapped out in front, head-high (where you can see it), sole first, held a moment, and back
                var t = _kickT[i];
                var w = t < 0.28f ? Smooth(t / 0.28f) : t < 0.55f ? 1f : 1f - Smooth((t - 0.55f) / 0.45f);
                var legLen = (ThighLen + ShinLen) * k;
                var dir = ft;
                var kickAt = hip + ft * (legLen * KickReach) + tu * (KickLift * k);
                switch (_kickStyle[i])
                {
                    case KickStyle.Sweep:
                    {
                        // the roundhouse: out in front, then swept round to the kicking leg's side and down
                        var sweep = Smooth(Mathf.Clamp01((t - 0.22f) / 0.5f));
                        dir = Quaternion.AngleAxis(s * 60f * sweep, up) * ft;
                        kickAt = hip + dir * (legLen * Mathf.Lerp(KickReach, 0.8f, sweep)) + tu * (Mathf.Lerp(KickLift, 0.15f, sweep) * k);
                        break;
                    }
                    case KickStyle.Stomp:
                        // looking down: the foot stamps the ground in front
                        kickAt = new Vector3(hip.x, ground.y, hip.z) + ft * (legLen * 0.38f) + up * (Ankle(1).y * k);
                        break;
                    case KickStyle.Low:
                        // the slide: the leg out low, along the ground
                        kickAt = hip + ft * (legLen * 0.92f) - tu * (0.12f * k);
                        break;
                }

                ankleTarget = Vector3.Lerp(ankleTarget, kickAt, w);
                var soleWay = _kickStyle[i] == KickStyle.Stomp ? ft : Vector3.Slerp(dir, tu, 0.65f);
                sole = Quaternion.Slerp(sole, Quaternion.LookRotation(soleWay, _kickStyle[i] == KickStyle.Stomp ? up : -dir), w);
                footFwd = Vector3.Slerp(footFwd, dir, w);
            }

            var knee = SolveLeg(hip, ankleTarget, footFwd, k, out var ankle);
            if (i == 0)
            {
                p.HipL = hip;
                p.KneeL = knee;
                p.AnkleL = ankle;
                p.FootL = sole;
                p.GroundL = ground.y;
            }
            else
            {
                p.HipR = hip;
                p.KneeR = knee;
                p.AnkleR = ankle;
                p.FootR = sole;
                p.GroundR = ground.y;
            }
        }

        p.HenryLeg = (ThighLen + ShinLen) * k;
        p.HenryAnkleOffset = Ankle(1) - HipJ(1);
        Last = p;
        return p;
    }

    private static Quaternion Frame(Vector3 along, Vector3 up) =>
        along.sqrMagnitude < 1e-10f ? Quaternion.identity : Quaternion.LookRotation(along, up.sqrMagnitude > 1e-6f ? up : Vector3.forward);

    private Vector3 FootPos(int i, float k)
    {
        var foot = _feet[i];
        if (!foot.Stepping) return foot.Planted;
        var t = Smooth(foot.T);
        var lift = Mathf.Sin(foot.T * Mathf.PI) * 0.08f * k;
        return Vector3.Lerp(foot.From, foot.To, t) + Vector3.up * lift;
    }

    /// <summary>A running foot: along the way you're going, half a stride cycle from the other, lifting as it comes through.</summary>
    private Vector3 RunFoot(int i, Vector3 pelvis, Vector3 rFlat, Vector3 rel, float k, Func<Vector3, float> groundAt, float eyeY)
    {
        var s = i == 0 ? -1f : 1f;
        var dir = new Vector3(rel.x, 0, rel.z);
        dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
        var speed = new Vector3(rel.x, 0, rel.z).magnitude / Mathf.Max(k, 1e-3f);
        var stride = Mathf.Clamp(0.45f + speed * 0.12f, 0.6f, 1.4f) * k;
        var ph = _phase + (i == 0 ? 0f : Mathf.PI);
        var at = new Vector3(pelvis.x, 0, pelvis.z) + rFlat * (0.09f * s * k) + dir * (Mathf.Cos(ph) * stride * 0.5f);
        at.y = groundAt(new Vector3(at.x, eyeY, at.z));
        return at + Vector3.up * (Mathf.Max(0f, Mathf.Sin(ph)) * 0.2f * k);
    }

    /// <summary>Plant each foot under the hip; step (one foot at a time) when it falls behind or the body turns.</summary>
    private void Step(Vector3 pelvis, Vector3 rFlat, float eyeY, float k, float speed, Func<Vector3, float> groundAt, float dt)
    {
        for (var i = 0; i < 2; i++)
        {
            if (!_feet[i].Stepping) continue;
            _feet[i].T += dt / _feet[i].Duration;
            if (_feet[i].T < 1f) continue;
            _feet[i].Stepping = false;
            _feet[i].Planted = _feet[i].To;
            _feet[i].Yaw = _feet[i].ToYaw;
        }

        if (_feet[0].Stepping || _feet[1].Stepping) return;

        var rel = _velocity - GroundVelocity;
        var lead = new Vector3(rel.x, 0, rel.z) * 0.3f;
        var stride = 0.2f + 0.1f * Mathf.Min(speed, 2.5f);
        var best = -1;
        var bestNeed = 0f;
        var targets = new Vector3[2];
        for (var i = 0; i < 2; i++)
        {
            var s = i == 0 ? -1f : 1f;
            var at = new Vector3(pelvis.x, 0, pelvis.z) + rFlat * (0.1f * s * k) + lead;
            at.y = groundAt(new Vector3(at.x, eyeY, at.z));
            targets[i] = at;
            var flat = new Vector3(at.x - _feet[i].Planted.x, 0, at.z - _feet[i].Planted.z).magnitude / k;
            var turn = Mathf.Abs(Mathf.DeltaAngle(_feet[i].Yaw, _yaw)) / 40f;
            var height = Mathf.Abs(at.y - _feet[i].Planted.y) / k / 0.2f;
            var need = Mathf.Max(flat / stride, Mathf.Max(turn, height));
            if (i == _nextFoot) need *= 1.15f; // alternate feet when both want to go
            if (need > 1f && need > bestNeed)
            {
                bestNeed = need;
                best = i;
            }
        }

        if (best < 0) return;
        _feet[best].From = _feet[best].Planted;
        _feet[best].To = targets[best];
        _feet[best].FromYaw = _feet[best].Yaw;
        _feet[best].ToYaw = _yaw;
        _feet[best].T = 0;
        _feet[best].Duration = Mathf.Clamp(0.32f - speed * 0.05f, 0.2f, 0.32f);
        _feet[best].Stepping = true;
        _nextFoot = 1 - best;
    }

    /// <summary>Two-bone leg: hip, knee, ankle, the knee bending forward over the foot. Out: the reachable ankle.</summary>
    private static Vector3 SolveLeg(Vector3 hip, Vector3 ankleTarget, Vector3 forward, float k, out Vector3 ankle)
    {
        var a = ThighLen * k;
        var b = ShinLen * k;
        var to = ankleTarget - hip;
        var dist = to.magnitude;
        var dir = dist > 1e-5f ? to / dist : Vector3.down;
        var d = Mathf.Clamp(dist, Mathf.Abs(a - b) + 1e-4f, (a + b) * 0.999f);
        ankle = hip + dir * d;
        var cosA = (a * a + d * d - b * b) / (2 * a * d);
        var sinA = Mathf.Sqrt(Mathf.Max(0, 1 - cosA * cosA));
        var bend = Vector3.ProjectOnPlane(forward, dir).normalized;
        if (bend.sqrMagnitude < 1e-6f) bend = forward;
        return hip + dir * (a * cosA) + bend * (a * sinA);
    }

    public string Describe() =>
        $"SKELETON yaw {_yaw:F0} speed {(_velocity - GroundVelocity).magnitude:F2} running {_running} feet L {Last.AnkleL:F2} R {Last.AnkleR:F2} knees L {Last.KneeL:F2} R {Last.KneeR:F2} low {Last.Low}";
}
