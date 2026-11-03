"""
The rage room's breakables, made in Blender: each as a whole model and its pieces (group = its name). Wood comes apart
along its own planks and legs; glass, china, the TV and the gnome break into Voronoi chunks, the cuts showing their
inside material.
"""
import math, random
import bpy, bmesh
from mathutils import Vector, Matrix
import afvr as A


def at(ob, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1)):
    return A.transform(ob, loc, tuple(math.radians(r) for r in rot), scale)


def copy(ob, name):
    c = ob.copy()
    c.data = ob.data.copy()
    c.name = name
    bpy.context.scene.collection.objects.link(c)
    return c


def emit(keep, name, whole_parts, pieces):
    """The whole model (its parts joined, a copy) and each piece (origin at its middle, its offset from the whole's)."""
    whole = A.join(name, [copy(p, p.name + "_w") for p in whole_parts]) if len(whole_parts) > 1 else copy(whole_parts[0], name)
    whole.name = name
    keep(whole)
    for i, p in enumerate(pieces):
        p.name = f"{name}.piece{i:02d}"
        off = A.center_origin(p)
        A.smooth(p, 40)
        keep(p, name, tuple(off))
    for p in whole_parts:
        if p.name in bpy.data.objects and p not in pieces:
            bpy.data.objects.remove(p)


def crate(keep):
    """A wooden crate: slatted sides on corner posts; it comes apart plank by plank."""
    s = 0.62
    parts = []
    rnd = random.Random(3)
    for side in range(4):
        rot = side * 90
        for k in range(4):
            z = 0.06 + k * 0.155
            p = A.box("slat", (s - 0.06, 0.022, 0.13), material="wood_crate", round_=0.004, segments=1)
            at(p, (0, s / 2 - 0.011, z + 0.065))
            at(p, (0, 0, 0), (0, 0, rot))  # (turned round the crate's middle to its side)
            for v in p.data.vertices:
                v.co += Vector((0, 0, rnd.uniform(-0.002, 0.002)))
            parts.append(p)
    for (x, y) in ((1, 1), (1, -1), (-1, 1), (-1, -1)):
        post = A.box("post", (0.05, 0.05, s), material="wood_crate_dark", round_=0.004, segments=1)
        at(post, (x * (s / 2 - 0.035), y * (s / 2 - 0.035), s / 2))
        parts.append(post)
    for k in range(4):
        top = A.box("lid", (s - 0.02, 0.145, 0.022), material="wood_crate", round_=0.004, segments=1)
        at(top, (0, -s / 2 + 0.08 + k * 0.152, s - 0.011))
        parts.append(top)
    bottom = A.box("floor", (s - 0.02, s - 0.02, 0.022), material="wood_crate_dark", round_=0.004, segments=1)
    at(bottom, (0, 0, 0.011))
    parts.append(bottom)
    emit(keep, "crate", parts, [copy(p, "p") for p in parts])


def tv(keep):
    """An old CRT television on little feet: its case breaks into chunks, its screen shatters."""
    body = A.box("case", (0.58, 0.42, 0.46), material="tv_plastic", round_=0.035, segments=3)
    for v in body.data.vertices:  # the tube's back tapers away
        if v.co.y < 0:
            f = 1 + v.co.y * 0.9
            v.co.x *= f
            v.co.z = (v.co.z) * f
    at(body, (0, -0.05, 0.26))
    bezel_cut = A.box("screen", (0.44, 0.04, 0.33), material="tv_screen", round_=0.03, segments=3)
    for v in bezel_cut.data.vertices:  # a curved tube face
        v.co.y += 0.03 * (1 - (v.co.x / 0.22) ** 2) * (1 - (v.co.z / 0.165) ** 2)
    at(bezel_cut, (-0.04, 0.17, 0.27))
    knobs = [A.cylinder(f"knob{i}", 0.022, 0.03, (0.235, 0.17, 0.33 - i * 0.08), "tv_knob", 16, axis='Y', round_=0.005) for i in range(2)]
    feet = [A.box(f"foot{i}", (0.06, 0.06, 0.03), material="tv_knob", round_=0.008) for i in range(4)]
    for i, (x, y) in enumerate(((1, 1), (1, -1), (-1, 1), (-1, -1))):
        at(feet[i], (x * 0.2, y * 0.12 - 0.05, 0.015))
    ant = [A.cylinder(f"ant{i}", 0.006, 0.42, (0, 0, 0.21), "chrome", 8) for i in range(2)]
    at(ant[0], (0, 0, 0), (0, 25, 0))
    at(ant[1], (0, 0, 0), (0, -25, 0))
    for a in ant:
        at(a, (0, -0.1, 0.49))
    shell = A.join("tv_shell", [body] + feet)
    case_pieces = A.fracture(shell, 9, 11, "tv_inside")
    glass_pieces = A.fracture(bezel_cut, 8, 12, "glass_shard")
    whole = [copy(shell, "s"), copy(bezel_cut, "g")] + [copy(k, "k") for k in knobs] + [copy(a, "a") for a in ant]
    emit(keep, "tv", whole, case_pieces + glass_pieces + knobs + ant)
    for o in (shell, bezel_cut):
        try:
            bpy.data.objects.remove(o)
        except ReferenceError:
            pass


def bottle(keep, name, material, seed):
    prof = [(0.0, 0.0), (0.031, 0.0), (0.033, 0.006), (0.033, 0.15), (0.03, 0.17), (0.017, 0.2), (0.012, 0.225), (0.012, 0.245), (0.014, 0.25), (0.0, 0.252)]
    b = A.lathe(name + "_src", prof, 20, (0, 0, 0), material)
    label = A.lathe(name + "_label", [(0.0336, 0.06), (0.0336, 0.12)], 20, (0, 0, 0), "bottle_label")
    whole = A.join(name + "_j", [b, label])
    pieces = A.fracture(whole, 7, seed, material)  # (the broken edges the bottle's own glass)
    emit(keep, name, [whole], pieces)


def vase(keep):
    prof = [(0.0, 0.0), (0.07, 0.0), (0.075, 0.01), (0.12, 0.09), (0.13, 0.16), (0.1, 0.26), (0.065, 0.33), (0.06, 0.37), (0.08, 0.41), (0.075, 0.42), (0.0, 0.42)]
    v = A.lathe("vase_src", prof, 32, (0, 0, 0), "china_blue")
    band = A.lathe("vase_band", [(0.1305, 0.13), (0.1305, 0.19)], 32, (0, 0, 0), "china_white")
    for vert in band.data.vertices:
        r = math.hypot(vert.co.x, vert.co.y)
        z = vert.co.z
        rr = 0.12 + (0.13 - 0.12) * (z - 0.09) / 0.07 if z < 0.16 else 0.13 - (0.13 - 0.1) * (z - 0.16) / 0.1
        if r > 0:
            vert.co.x *= (rr + 0.002) / r
            vert.co.y *= (rr + 0.002) / r
    whole = A.join("vase_j", [v, band])
    emit(keep, "vase", [whole], A.fracture(whole, 10, 21, "china_inside"))


def chair(keep):
    """A wooden chair: legs, seat (it splits), back posts and slats all come apart."""
    parts = []
    seat_l = A.box("seat_l", (0.22, 0.42, 0.04), material="wood_light", round_=0.006)
    at(seat_l, (-0.11, 0, 0.45))
    seat_r = A.box("seat_r", (0.22, 0.42, 0.04), material="wood_light", round_=0.006)
    at(seat_r, (0.11, 0, 0.45))
    parts += [seat_l, seat_r]
    for (x, y) in ((1, 1), (-1, 1)):
        leg = A.box("leg", (0.04, 0.04, 0.43), material="wood_dark", round_=0.006)
        at(leg, (x * 0.18, y * 0.18, 0.215))
        parts.append(leg)
    for x in (1, -1):
        post = A.box("post", (0.04, 0.04, 0.92), material="wood_dark", round_=0.006)
        at(post, (x * 0.18, -0.18, 0.46))
        parts.append(post)
    for k in range(3):
        slat = A.box("slat", (0.36, 0.025, 0.06), material="wood_light", round_=0.005)
        at(slat, (0, -0.18, 0.6 + k * 0.1))
        parts.append(slat)
    emit(keep, "chair", parts, [copy(p, "p") for p in parts])


def table(keep):
    parts = []
    for k in range(3):
        plank = A.box("plank", (0.9, 0.2, 0.035), material="wood_light", round_=0.006)
        at(plank, (0, -0.2 + k * 0.2, 0.74))
        parts.append(plank)
    for (x, y) in ((1, 1), (1, -1), (-1, 1), (-1, -1)):
        leg = A.box("leg", (0.05, 0.05, 0.72), material="wood_dark", round_=0.006)
        at(leg, (x * 0.4, y * 0.25, 0.36))
        parts.append(leg)
    for (sx, sy, x, y) in ((0.75, 0.025, 0, 0.25), (0.75, 0.025, 0, -0.25), (0.025, 0.45, 0.4, 0), (0.025, 0.45, -0.4, 0)):
        rail = A.box("rail", (sx, sy, 0.07), material="wood_dark", round_=0.004)
        at(rail, (x, y, 0.68))
        parts.append(rail)
    emit(keep, "table", parts, [copy(p, "p") for p in parts])


def box_(keep):
    """A cardboard box, taped: its sides and flaps come apart."""
    w, d, h, t = 0.46, 0.36, 0.32, 0.006
    parts = []
    for (sx, sy, sz, x, y, z) in ((w, t, h, 0, d / 2, h / 2), (w, t, h, 0, -d / 2, h / 2), (t, d, h, w / 2, 0, h / 2), (t, d, h, -w / 2, 0, h / 2), (w, d, t, 0, 0, t / 2)):
        p = A.box("side", (sx, sy, sz), material="cardboard")
        at(p, (x, y, z))
        parts.append(p)
    for y in (1, -1):
        flap = A.box("flap", (w, d / 2, t), material="cardboard")
        at(flap, (0, y * d / 4, h - t / 2))
        parts.append(flap)
    tape = A.box("tape", (0.05, d + 0.004, 0.002), material="tape")
    at(tape, (0, 0, h + 0.001))
    whole = parts + [tape]
    emit(keep, "box", whole, [copy(p, "p") for p in parts])  # (emit tidies the tape away with the other parts)


def gnome(keep):
    """A garden gnome (plaster): red hat, white beard, blue coat. It breaks into chunks."""
    body = A.lathe("g_body", [(0.0, 0.0), (0.11, 0.0), (0.12, 0.03), (0.11, 0.16), (0.08, 0.24), (0.0, 0.25)], 24, (0, 0, 0), "gnome_coat")
    head = A.lathe("g_head", [(0.0, 0.22), (0.06, 0.23), (0.075, 0.27), (0.07, 0.31), (0.0, 0.33)], 24, (0, 0, 0), "gnome_skin")
    beard = A.lathe("g_beard", [(0.0, 0.16), (0.07, 0.19), (0.075, 0.24), (0.0, 0.27)], 20, (0, 0.035, 0), "gnome_beard")
    hat = A.lathe("g_hat", [(0.0, 0.29), (0.08, 0.3), (0.06, 0.38), (0.03, 0.45), (0.0, 0.5)], 24, (0, 0, 0), "gnome_hat")
    for v in hat.data.vertices:  # the tip flops forward
        if v.co.z > 0.38:
            v.co.y += (v.co.z - 0.38) * 0.6
    nose = A.lathe("g_nose", [(0.0, 0.0), (0.02, 0.005), (0.018, 0.03), (0.0, 0.035)], 12, (0, 0, 0), "gnome_skin")
    at(nose, (0, 0, 0), (-80, 0, 0))
    at(nose, (0, 0.075, 0.275))
    whole = A.join("gnome_j", [body, head, beard, hat, nose])
    emit(keep, "gnome", [whole], A.fracture(whole, 10, 31, "plaster"))


def build(models, keep):
    crate(keep)
    tv(keep)
    bottle(keep, "bottle_green", "glass_green", 41)
    bottle(keep, "bottle_brown", "glass_brown", 42)
    vase(keep)
    chair(keep)
    table(keep)
    box_(keep)
    gnome(keep)
