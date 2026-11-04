using AngerFootVR.Core;
using Framework;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.Input;

/// <summary>
/// GUN SPIN (1.2): a pistol, the Glock or the Uzi (each of the dual pistols too) spins round your trigger finger, cowboy
/// style. Loosen your grip (the grip not squeezed: a held grip is a gun ready to throw) and flick your wrist, the muzzle
/// up and over or down and under: it goes round as fast as you flicked it and slows, and another flick keeps it going.
/// Squeeze the grip to catch it: it finishes its turn into your hand. Your hand stays where it is, the index hooked in
/// the trigger guard, the gun turning round it. No shots while it spins. The flick is the wrist's own (fast round its
/// side, the hand itself nearly still), so aiming about or throwing never spins it.
/// </summary>
internal static class GunSpin
{
    public static bool On => VrSettings.GunSpin == null || VrSettings.GunSpin.Value;

    private const float Flick = 13f; // rad/s round the wrist's side: a flick
    private const float Settle = 9f; // rad/s: slower, it comes round into the hand
    private const float Friction = 1.1f; // (a spin of 25 rad/s goes round about three times)

    private sealed class Hand
    {
        public bool Active, Catching;
        public float Angle, Speed, Started, EndedAt = -10f;
        public int Turns;
        public Quaternion Last;
        public bool HasLast;
        public Vector3 Omega; // rad/s, in the controller's own frame (smoothed)
        public float DevOmega;
        public int DevFrames;
    }

    private static readonly Hand[] Hands = { new Hand(), new Hand() };
    private static int Side(XRNode h) => h == XRNode.LeftHand ? 0 : 1;

    /// <summary>The frame a spin was caught (that grip squeeze is the catch, not a throw).</summary>
    public static int CaughtFrame = -10;

    public static int Spins;
    public static string Last = "none yet";

    /// <summary>This hand's gun is spinning.</summary>
    public static bool Spinning(XRNode hand) => Hands[Side(hand)].Active;

    public static bool Any => Hands[0].Active || Hands[1].Active;

    /// <summary>How far round it is now (degrees, about the controller's side).</summary>
    public static float AngleOf(XRNode hand) => Hands[Side(hand)].Angle * Mathf.Rad2Deg;

    private static bool Spinnable(HoldableObjectType t) => t == HoldableObjectType.Glock || t == HoldableObjectType.Uzzi || t == HoldableObjectType.DualPistols;

    /// <summary>Which hands have a gun that can spin this frame.</summary>
    private static bool CanSpin(XRNode hand)
    {
        if (!On || !XrManager.IsRunning || CinemaRig.Active || !SingletonBehaviour<Player>.HasInstance || !GunHold.Holding) return false;
        var p = SingletonBehaviour<Player>.Instance;
        if (p.Weapon == null || !Spinnable(p.Weapon.HoldableObjectType) || GripAdjust.Adjusting) return false;
        if (hand == GunHold.Hand) return !Arsenal.TwoHands && !Arsenal.Supported;
        return Arsenal.Dual && hand == XRNode.LeftHand; // (the left of the pair)
    }

    /// <summary>Every frame, after the controllers are read and before the throws (a squeeze that catches a spin isn't a
    /// throw).</summary>
    public static void Update()
    {
        var dt = Mathf.Clamp(Time.unscaledDeltaTime, 1e-4f, 0.05f);
        for (var s = 0; s < 2; s++)
        {
            var node = s == 0 ? XRNode.LeftHand : XRNode.RightHand;
            var h = Hands[s];
            var pose = VrHands.GetRaw(node);
            Measure(h, pose, dt);
            if (!CanSpin(node) || !pose.Tracked)
            {
                if (h.Active) End(h, node, "put away");
                continue;
            }

            var dev = h.DevFrames > 0;
            var wx = dev ? h.DevOmega : h.Omega.x;
            if (dev) h.DevFrames--;
            var side = Mathf.Abs(wx);
            var mostlySide = dev || side >= 0.7f * h.Omega.magnitude;
            var still = pose.Velocity.magnitude < 2f;
            var loose = !VrControls.Held(node, VrButton.Grip) && !VrControls.Held(node, VrButton.Trigger);

            if (!h.Active)
            {
                if (side >= Flick && mostlySide && still && loose && Time.unscaledTime - h.EndedAt > 0.3f)
                {
                    h.Active = true;
                    h.Catching = false;
                    h.Angle = 0f;
                    h.Turns = 0;
                    h.Speed = Mathf.Sign(wx) * Mathf.Clamp(side * 1.6f, 18f, 32f);
                    h.Started = Time.unscaledTime;
                    Spins++;
                    Last = $"{(s == 0 ? "left" : "right")} hand flicked at {side:F0} rad/s: spinning at {Mathf.Abs(h.Speed):F0} rad/s";
                    VrControls.Rumble(node, 0.2f, 0.02f);
                }

                continue;
            }

            // spinning: another flick the same way keeps it going; a squeeze catches it
            if (!h.Catching && side >= Flick * 0.8f && Mathf.Sign(wx) == Mathf.Sign(h.Speed)) h.Speed = Mathf.Sign(h.Speed) * Mathf.Max(Mathf.Abs(h.Speed), Mathf.Clamp(side * 1.6f, 18f, 32f));
            if (!h.Catching && VrControls.Down(node, VrButton.Grip))
            {
                h.Catching = true;
                CaughtFrame = Time.frameCount;
            }

            var dir = Mathf.Sign(h.Speed);
            var speed = Mathf.Abs(h.Speed);
            if (h.Catching) speed = Mathf.Max(speed, 26f); // (caught: it comes round into the hand quick)
            else speed *= Mathf.Exp(-Friction * dt);
            var settling = h.Catching || speed < Settle || Time.unscaledTime - h.Started > 4f;
            if (settling) speed = Mathf.Max(speed, Settle);
            h.Angle += dir * speed * dt;
            h.Speed = dir * speed;

            // a full turn: a tap in the hand as the grip comes past the palm
            var turnsNow = Mathf.FloorToInt(Mathf.Abs(h.Angle) / (Mathf.PI * 2f));
            if (turnsNow > h.Turns)
            {
                h.Turns = turnsNow;
                if (settling)
                {
                    End(h, node, h.Catching ? "caught" : "came round into the hand");
                    VrControls.Rumble(node, 0.45f, 0.03f);
                    continue;
                }

                VrControls.Rumble(node, 0.18f, 0.015f);
            }
        }
    }

    private static void End(Hand h, XRNode node, string why)
    {
        h.Active = false;
        h.Catching = false;
        h.Angle = 0f;
        h.Speed = 0f;
        h.EndedAt = Time.unscaledTime;
        Last = $"{(node == XRNode.LeftHand ? "left" : "right")} hand's spin {why} after {h.Turns} turn{(h.Turns == 1 ? "" : "s")}";
        if (Dev.ActionLog.Enabled) Plugin.L.LogInfo("ACTION gun spin " + Last);
    }

    /// <summary>The wrist's turning speed, in the controller's own frame (smoothed over a few frames).</summary>
    private static void Measure(Hand h, VrHands.Pose pose, float dt)
    {
        if (!pose.Tracked)
        {
            h.HasLast = false;
            h.Omega = Vector3.zero;
            return;
        }

        if (h.HasLast)
        {
            (Quaternion.Inverse(h.Last) * pose.Rotation).ToAngleAxis(out var deg, out var axis);
            if (deg > 180f) deg -= 360f;
            var w = float.IsNaN(axis.x) ? Vector3.zero : axis * (deg * Mathf.Deg2Rad / dt);
            h.Omega = Vector3.Lerp(h.Omega, w, 0.6f);
        }

        h.Last = pose.Rotation;
        h.HasLast = true;
    }

    /// <summary>From GunHold, once the gun and the hand holding it are placed: the gun turned round the trigger finger.</summary>
    public static void Apply(Transform rig, Vector3 pivot, XRNode hand)
    {
        var h = Hands[Side(hand)];
        if (!h.Active) return;
        var axis = VrHands.GetRaw(hand).Rotation * Vector3.right;
        rig.RotateAround(pivot, axis, h.Angle * Mathf.Rad2Deg);
    }

    /// <summary>From the dual pistols: the left one turned round its trigger.</summary>
    public static void ApplyLeft(Transform gun, VrHands.Pose left)
    {
        var h = Hands[0];
        if (!h.Active || gun == null) return;
        var pivot = left.Position + left.Aim * new Vector3(0f, 0.02f, 0.06f);
        gun.RotateAround(pivot, left.Rotation * Vector3.right, h.Angle * Mathf.Rad2Deg);
    }

    /// <summary>Dev: a flick of this speed (rad/s round the wrist's side) on a hand for a few frames (the test runtime's
    /// hands don't turn).</summary>
    public static string DevFlick(XRNode hand, float omega)
    {
        VrControls.DevPresent();
        var h = Hands[Side(hand)];
        h.DevOmega = omega;
        h.DevFrames = 3;
        return $"flick {omega:F0} rad/s on the {(hand == XRNode.LeftHand ? "left" : "right")} (can spin: {CanSpin(hand)})";
    }

    public static string Describe() =>
        $"GUN SPIN on {On} | left {(Hands[0].Active ? $"spinning {Hands[0].Angle * Mathf.Rad2Deg:F0} deg at {Hands[0].Speed:F0} rad/s" : "still")} wrist {Hands[0].Omega.x:F1} | right {(Hands[1].Active ? $"spinning {Hands[1].Angle * Mathf.Rad2Deg:F0} deg at {Hands[1].Speed:F0} rad/s" : "still")} wrist {Hands[1].Omega.x:F1} | spins {Spins} | last: {Last}";
}
