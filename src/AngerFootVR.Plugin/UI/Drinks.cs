using System;
using UnityEngine;

namespace AngerFootVR.UI;

/// <summary>
/// Drink (or eat) for real in the movie house's rooms: a can from the vending machine, a soda from the fountain,
/// the popcorn bucket. Hold it to your mouth: a sip (a bite) every moment you keep it there, the game's own sound,
/// a buzz in your hand, until it's empty.
/// </summary>
internal sealed class Drinks : MonoBehaviour
{
    public FunWorld.Grabbable Grab;
    public string Bank;
    public int Left = 4;
    public Func<bool> Ready; // (the soda: only once it's filled)
    public Action<Drinks> Sipped;
    private float _near = -1f, _next;

    public static Drinks MakeDrinkable(FunWorld.Grabbable g, string bank, int sips = 4)
    {
        var d = g.gameObject.GetComponent<Drinks>() ?? g.gameObject.AddComponent<Drinks>();
        d.Grab = g;
        d.Bank = bank;
        d.Left = sips;
        return d;
    }

    private void Update()
    {
        if (Grab == null || !Grab.Busy || Left <= 0 || (Ready != null && !Ready()))
        {
            _near = -1f;
            return;
        }

        // (its top: its body's bounds, up from its middle)
        var b = GetComponent<Collider>()?.bounds ?? new Bounds(transform.position, Vector3.one * 0.1f);
        var top = b.center + transform.up * b.extents.y * 0.8f;
        var close = (top - FunWorld.Mouth).magnitude < 0.13f || (b.center - FunWorld.Mouth).magnitude < 0.12f;
        if (!close)
        {
            _near = -1f;
            return;
        }

        var now = Time.unscaledTime;
        if (_near < 0f) _near = now;
        if (now - _near < 0.3f || now < _next) return;
        _next = now + 0.9f;
        Left--;
        if (!GameSounds.Play(Bank, FunWorld.Mouth, 0.8f)) Sounds.Play("step", FunWorld.Mouth, 0.3f, 1.6f);
        if (Grab.Holder.HasValue) Core.VrControls.Rumble(Grab.Holder.Value, 0.25f, 0.12f);
        Sipped?.Invoke(this);
        if (Left == 0 && Grab.Body != null) Grab.Body.mass *= 0.4f; // (empty: light)
    }
}
