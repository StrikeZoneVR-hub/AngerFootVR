"""
Anger Foot VR 1.2's models (The Rage Room Update), made in Blender from code like the rest:
- the rage room's new things: the vending machine (its glass front shatters) and its cans, the water cooler and its
  jug, a dish rack and plates, the pink boot piñata and its candy, the explosive barrel, the weapon rack with a bat,
  a lead pipe, a sledgehammer and a golf club, the RAGE MODE button, the RESET lever, the goon dummy's spring stand;
- the lobby: the popcorn machine (bucket, kernels), the soda fountain and its cup, the concession counter, poster
  frames, the shoe shelf, the photo kiosk and its photo strip, the house lights' knife switch, the curtain's pull cord,
  cardboard goons and a cardboard granny;
- the arcade: DOOR KICKER (its doors), the shooting gallery (counter, rails, targets, the light gun), the KICK-O-METER
  (its pad, its lamps), SHOE TOSS (the lane), GOON BOWLING (the lane, the goon pins, the kickball), the high score board.
Front is +y, up +z, origins at the floor under the middle unless said.
"""
import math, random
import bpy, bmesh
from mathutils import Vector
import afvr as A
from build_rage import at, copy, emit


def sphere(name, r, material, segments=16, rings=10, squash=1.0):
    prof = [(r * math.sin(math.pi * k / rings), r * squash * (1 - math.cos(math.pi * k / rings))) for k in range(rings + 1)]
    prof[0] = (0.0, 0.0)
    prof[-1] = (0.0, prof[-1][1])
    ob = A.lathe(name, prof, segments, (0, 0, 0), material)
    return at(ob, (0, 0, -r * squash))  # (centred on its middle)


def flat(name, pts, thick, material, y=0.0):
    """A flat shape: (x, z) outline points round its front (+y), thick along y."""
    bm = bmesh.new()
    front = [bm.verts.new(Vector((x, y + thick / 2, z))) for x, z in pts]
    back = [bm.verts.new(Vector((x, y - thick / 2, z))) for x, z in pts]
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((front[i], front[j], back[j], back[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return A.smooth(A.obj_from_bmesh(name, bm, material), 30)


def circle(cx, cz, r, n=20, a0=0.0, a1=2 * math.pi):
    return [(cx + r * math.cos(a0 + (a1 - a0) * k / n), cz + r * math.sin(a0 + (a1 - a0) * k / n)) for k in range(n)]


def helix(name, r, h, turns, wire, material):
    pts = [(r * math.cos(2 * math.pi * turns * k / 80), r * math.sin(2 * math.pi * turns * k / 80), h * k / 80) for k in range(81)]
    return A.tube_along(name, pts, wire, material, 8)


def whole(keep, name, parts):
    ob = A.join(name, parts) if len(parts) > 1 else parts[0]
    ob.name = name
    keep(ob)
    return ob


# ======================================================================================== the rage room

def vending(keep):
    """A soda machine: red cabinet, cans on shelves behind its glass front, the coin panel, the dispenser flap, a
    glowing header. The cabinet takes kicks (a can drops); the glass front is its own model and shatters."""
    w, d, h = 0.95, 0.8, 1.9
    parts = [at(A.box("cab", (w, d, h), material="vending_red", round_=0.025, segments=2), (0, 0, h / 2))]
    parts.append(at(A.box("inside", (0.62, 0.06, 1.3), material="booth_inside"), (-0.1, d / 2 - 0.02, 1.08)))
    cols = ("can_red", "can_blue", "can_green", "can_red", "can_blue")
    for row in range(4):
        z = 0.5 + row * 0.31
        parts.append(at(A.box(f"shelf{row}", (0.6, 0.2, 0.012), material="chrome"), (-0.1, d / 2 - 0.12, z - 0.005)))
        for c in range(5):
            parts.append(at(A.cylinder(f"c{row}{c}", 0.033, 0.12, (0, 0, 0), cols[(c + row) % 5], 12), (-0.33 + c * 0.115, d / 2 - 0.09, z + 0.06)))
    parts.append(at(A.box("coin", (0.2, 0.025, 0.5), material="metal_dark", round_=0.006), (0.34, d / 2 + 0.005, 1.15)))
    for k in range(5):
        parts.append(at(A.box(f"btn{k}", (0.12, 0.02, 0.045), material="neon_cyan" if k % 2 else "neon_pink"), (0.34, d / 2 + 0.02, 1.32 - k * 0.07)))
    parts.append(at(A.box("slot", (0.03, 0.02, 0.07), material="booth_inside"), (0.34, d / 2 + 0.02, 0.95)))
    parts.append(at(A.box("tray", (0.6, 0.06, 0.18), material="booth_inside"), (-0.1, d / 2 - 0.01, 0.24)))
    parts.append(at(A.box("flap", (0.58, 0.012, 0.15), material="chrome", round_=0.004), (-0.1, d / 2 + 0.025, 0.25)))
    parts.append(at(A.box("logo", (0.9, 0.025, 0.24), material="vending_logo", round_=0.01), (0, d / 2 + 0.005, 1.74)))
    whole(keep, "vending", parts)
    glass = at(A.box("pane", (0.64, 0.014, 1.33), material="glass_aqua"), (-0.1, d / 2 + 0.01, 1.08))
    pieces = A.fracture(glass, 9, 71, "glass_shard")
    emit(keep, "vending_glass", [glass], pieces)


def cans(keep):
    for name, mat in (("can", "can_red"), ("can_blue", "can_blue"), ("can_green", "can_green")):
        body = A.lathe(name, [(0.0, 0.0), (0.026, 0.0), (0.033, 0.009), (0.033, 0.111), (0.027, 0.121), (0.0, 0.121)], 18, (0, 0, 0), mat)
        top = A.cylinder("top", 0.0265, 0.004, (0, 0, 0.122), "chrome", 18)
        whole(keep, name, [body, top])


def cooler(keep):
    """An office water cooler: a white cabinet with two taps and a drip tray; its blue jug sits on top, its own model
    (it comes off and glugs out over the floor)."""
    parts = [at(A.box("cab", (0.32, 0.32, 0.96), material="plastic_white", round_=0.03, segments=2), (0, 0, 0.48))]
    parts.append(at(A.box("panel", (0.24, 0.02, 0.22), material="metal_dark", round_=0.01), (0, 0.165, 0.78)))
    parts.append(at(A.box("tap_hot", (0.04, 0.05, 0.035), material="plastic_red", round_=0.008), (-0.05, 0.19, 0.76)))
    parts.append(at(A.box("tap_cold", (0.04, 0.05, 0.035), material="can_blue", round_=0.008), (0.05, 0.19, 0.76)))
    parts.append(at(A.box("tray", (0.22, 0.09, 0.02), material="metal_dark"), (0, 0.2, 0.6)))
    parts.append(at(A.cylinder("collar", 0.1, 0.05, (0, 0, 0), "plastic_white", 24), (0, 0, 0.985)))
    whole(keep, "cooler", parts)
    jug = A.lathe("jug", [(0.0, 0.96), (0.03, 0.96), (0.035, 1.02), (0.13, 1.08), (0.14, 1.12), (0.14, 1.36), (0.12, 1.42), (0.0, 1.43)], 24, (0, 0, 0), "water_jug")
    water = A.lathe("water", [(0.0, 1.0), (0.025, 1.0), (0.12, 1.09), (0.128, 1.13), (0.128, 1.3), (0.0, 1.3)], 20, (0, 0, 0), "water_blue")
    whole(keep, "cooler_jug", [jug, water])


def plates(keep):
    """A dinner plate (it shatters) and the wall rack the plates stand in."""
    p = A.lathe("plate", [(0.0, 0.0), (0.07, 0.0), (0.076, 0.004), (0.118, 0.013), (0.13, 0.021), (0.128, 0.025),
                          (0.116, 0.018), (0.072, 0.009), (0.0, 0.009)], 32, (0, 0, 0), "china_white")
    band = A.lathe("band", [(0.115, 0.0175), (0.126, 0.022), (0.124, 0.0245), (0.113, 0.0195)], 32, (0, 0, 0), "china_blue")
    plate = A.join("plate_whole", [p, band])
    at(plate, (0, 0, 0), (90, 0, 0))  # (stood on its edge: it leans in the rack, face to the room)
    at(plate, (0, 0, 0.13))
    pieces = A.fracture(plate, 5, 81, "china_inside")
    emit(keep, "plate", [plate], pieces)
    parts = [at(A.box("shelf", (1.3, 0.22, 0.03), material="wood_dark", round_=0.006), (0, 0, 0.015))]
    parts.append(at(A.box("back", (1.3, 0.025, 0.36), material="wood_dark", round_=0.006), (0, -0.1, 0.18)))
    for k in range(13):
        x = -0.6 + k * 0.1
        parts.append(at(A.cylinder(f"peg{k}", 0.006, 0.12, (0, 0, 0), "wood_light", 8), (x, 0.06, 0.09)))
    whole(keep, "dish_rack", parts)


def pinata(keep):
    """A pink piñata in the shape of a kicking boot (Anger Foot's own): fringed paper stripes, a yellow sole; it
    bursts into chunks and candy."""
    stripes = ("pinata_pink", "pinata_cyan", "pinata_yellow", "pinata_pink")
    parts = []
    shaft = A.lathe("shaft", [(0.0, 0.18), (0.13, 0.18), (0.135, 0.3), (0.125, 0.52), (0.13, 0.55), (0.0, 0.56)], 20, (0, 0, 0), "pinata_pink")
    parts.append(shaft)
    foot = A.box("foot", (0.28, 0.5, 0.2), material="pinata_pink", round_=0.08, segments=3)
    at(foot, (0, 0.12, 0.13))
    parts.append(foot)
    sole = A.box("sole", (0.3, 0.54, 0.05), material="pinata_yellow", round_=0.02, segments=2)
    at(sole, (0, 0.13, 0.025))
    parts.append(sole)
    for k, z in enumerate((0.26, 0.34, 0.42, 0.5)):
        band = A.lathe(f"fringe{k}", [(0.128, z), (0.15, z + 0.005), (0.145, z + 0.04), (0.126, z + 0.04)], 20, (0, 0, 0), stripes[k])
        parts.append(band)
    cuff = A.lathe("cuff", [(0.12, 0.55), (0.15, 0.555), (0.15, 0.6), (0.0, 0.6)], 20, (0, 0, 0), "pinata_cyan")
    parts.append(cuff)
    body = A.join("pinata_body", [copy(p, "pp") for p in parts])
    pieces = A.fracture(body, 9, 91, "cardboard")
    emit(keep, "pinata", parts, pieces)
    try:
        bpy.data.objects.remove(body)
    except ReferenceError:
        pass
    for name, mat in (("candy_a", "candy_red"), ("candy_b", "candy_yellow"), ("candy_c", "candy_blue")):
        c = A.box("c", (0.036, 0.024, 0.024), material=mat, round_=0.01, segments=2)
        l = A.cylinder("l", 0.012, 0.018, (0, 0, 0), mat, 8, axis='X')
        at(l, (-0.026, 0, 0))
        r = A.cylinder("r", 0.012, 0.018, (0, 0, 0), mat, 8, axis='X')
        at(r, (0.026, 0, 0))
        whole(keep, name, [c, l, r])


def barrel(keep):
    """A red explosive barrel: ribbed steel, a yellow hazard band; it bursts into shards when it blows."""
    body = A.cylinder("body", 0.28, 0.88, (0, 0, 0.44), "barrel_red", 28, round_=0.01)
    parts = [body]
    for z in (0.02, 0.3, 0.58, 0.86):
        parts.append(A.cylinder(f"rib{z}", 0.292, 0.035, (0, 0, z), "metal_dark", 28))
    parts.append(A.cylinder("band", 0.284, 0.16, (0, 0, 0.44), "hazard_yellow", 28))
    parts.append(A.cylinder("cap", 0.04, 0.03, (0.14, 0, 0.885), "metal_dark", 12))
    solid = A.join("barrel_solid", [copy(p, "bp") for p in parts])
    pieces = A.fracture(solid, 8, 101, "metal_dark")
    emit(keep, "barrel", parts, pieces)
    try:
        bpy.data.objects.remove(solid)
    except ReferenceError:
        pass


def weapons(keep):
    """The weapon rack on the wall and what's on it. Each weapon's origin is at its handle's end, along +z; held
    10-12 cm up from there."""
    parts = [at(A.box("board", (1.5, 0.04, 1.05), material="wood_dark", round_=0.01), (0, -0.02, 0.525))]
    parts.append(at(A.box("head", (1.6, 0.08, 0.12), material="wood_crate_dark", round_=0.01), (0, 0.0, 1.08)))
    for x in (-0.55, -0.18, 0.18, 0.55):
        for z in (0.28, 0.78):
            parts.append(at(A.cylinder(f"peg{x}{z}", 0.012, 0.1, (0, 0, 0), "chrome", 10, axis='Y'), (x - 0.06, 0.05, z)))
            parts.append(at(A.cylinder(f"peh{x}{z}", 0.012, 0.1, (0, 0, 0), "chrome", 10, axis='Y'), (x + 0.06, 0.05, z)))
    whole(keep, "weapon_rack", parts)

    bat = A.lathe("bat", [(0.0, 0.0), (0.024, 0.0), (0.024, 0.012), (0.014, 0.022), (0.0135, 0.3), (0.019, 0.44), (0.032, 0.62),
                          (0.036, 0.8), (0.033, 0.84), (0.0, 0.85)], 20, (0, 0, 0), "wood_light")
    tape = A.cylinder("tape", 0.0152, 0.2, (0, 0, 0.12), "rubber_black", 16)
    whole(keep, "bat", [bat, tape])

    pipe = A.cylinder("pipe", 0.017, 0.62, (0, 0, 0.31), "steel_dark", 14)
    ring = A.cylinder("ring", 0.021, 0.04, (0, 0, 0.02), "steel", 14)
    elbow_pts = [(0, 0, 0.62)] + [(0.06 * (1 - math.cos(a)), 0, 0.62 + 0.06 * math.sin(a)) for a in [math.pi / 2 * k / 6 for k in range(1, 7)]] + [(0.12, 0, 0.68)]
    elbow = A.tube_along("elbow", elbow_pts, 0.02, "steel_dark", 12)
    whole(keep, "pipe", [pipe, ring, elbow])

    handle = A.cylinder("handle", 0.017, 0.86, (0, 0, 0.43), "wood_light", 14)
    grip = A.cylinder("grip", 0.019, 0.22, (0, 0, 0.12), "rubber_black", 14)
    head = A.box("head", (0.26, 0.085, 0.085), material="steel_dark", round_=0.012, segments=2)
    at(head, (0, 0, 0.86))
    whole(keep, "sledge", [handle, grip, head])

    shaft = A.cylinder("shaft", 0.0075, 0.98, (0, 0, 0.49), "chrome", 10)
    cgrip = A.cylinder("cgrip", 0.013, 0.26, (0, 0, 0.13), "rubber_black", 12)
    chead = A.box("chead", (0.1, 0.03, 0.05), material="steel", round_=0.008, segments=2)
    at(chead, (0.04, 0, 0.99))
    whole(keep, "golfclub", [shaft, cgrip, chead])


def controls(keep):
    """The RAGE MODE button on its pedestal (the cap its own model, pressed down), the RESET lever (base and handle,
    the handle's origin at its pivot), the goon dummy's weighted spring stand."""
    parts = [A.cylinder("ped", 0.16, 0.92, (0, 0, 0.46), "metal_dark", 24, round_=0.01)]
    parts.append(A.cylinder("foot", 0.24, 0.05, (0, 0, 0.025), "metal_dark", 24, round_=0.01))
    parts.append(A.cylinder("top", 0.2, 0.04, (0, 0, 0.94), "hazard_yellow", 28, round_=0.008))
    parts.append(A.cylinder("bezel", 0.13, 0.03, (0, 0, 0.975), "chrome", 28))
    whole(keep, "rage_button", parts)
    cap = A.cylinder("cap", 0.105, 0.06, (0, 0, 0.03), "button_red", 28, round_=0.02)
    whole(keep, "rage_button_cap", [cap])

    base = [at(A.box("box", (0.22, 0.12, 0.42), material="metal_dark", round_=0.012), (0, 0.06, 0))]
    base.append(at(A.box("slot", (0.035, 0.02, 0.32), material="booth_inside"), (0, 0.12, 0)))
    base.append(at(A.box("stripe_t", (0.2, 0.01, 0.04), material="hazard_yellow"), (0, 0.122, 0.17)))
    base.append(at(A.box("stripe_b", (0.2, 0.01, 0.04), material="hazard_yellow"), (0, 0.122, -0.17)))
    whole(keep, "lever_base", base)
    rod = A.cylinder("rod", 0.012, 0.3, (0, 0, 0.15), "chrome", 10)
    knob = sphere("knob", 0.04, "button_red")
    at(knob, (0, 0, 0.32))
    hub = A.cylinder("hub", 0.03, 0.05, (0, 0, 0), "chrome", 14, axis='X')
    whole(keep, "lever_handle", [rod, knob, hub])

    stand = [A.lathe("weight", [(0.0, 0.0), (0.34, 0.0), (0.36, 0.04), (0.3, 0.12), (0.0, 0.13)], 32, (0, 0, 0), "plastic_red")]
    stand.append(at(helix("spring", 0.06, 0.22, 5, 0.012, "chrome"), (0, 0, 0.12)))
    stand.append(A.cylinder("post", 0.035, 0.46, (0, 0, 0.57), "metal_dark", 14))
    stand.append(A.cylinder("plate", 0.12, 0.03, (0, 0, 0.8), "metal_dark", 20))
    whole(keep, "dummy_stand", stand)


# ======================================================================================== the lobby

def popcorn(keep):
    """A popcorn cart: red cabinet on wheels, a chrome and glass kettle box heaped with popcorn, a red roof, the
    sign panel. Its striped bucket, heaped full, and a single kernel (they spill)."""
    parts = [at(A.box("cab", (0.82, 0.56, 0.86), material="popcorn_red", round_=0.02, segments=2), (0, 0, 0.5))]
    for x in (-0.33, 0.33):
        for y in (-0.2, 0.2):
            parts.append(at(A.cylinder(f"w{x}{y}", 0.07, 0.04, (0, 0, 0), "rubber_black", 16, axis='X'), (x, y, 0.07)))
    parts.append(at(A.box("deck", (0.84, 0.58, 0.03), material="chrome"), (0, 0, 0.945)))
    for x in (-0.38, 0.38):
        for y in (-0.25, 0.25):
            parts.append(at(A.box(f"post{x}{y}", (0.025, 0.025, 0.66), material="chrome"), (x, y, 1.29)))
    for (sx, sy, x, y) in ((0.74, 0.008, 0, 0.255), (0.74, 0.008, 0, -0.255), (0.008, 0.48, 0.385, 0), (0.008, 0.48, -0.385, 0)):
        parts.append(at(A.box("pane", (sx, sy, 0.62), material="glass_aqua"), (x, y, 1.27)))
    rnd = random.Random(5)
    heap = A.box("heap", (0.72, 0.46, 0.22), material="popcorn", round_=0.05, segments=2)
    for v in heap.data.vertices:
        if v.co.z > 0:
            v.co.z += rnd.uniform(-0.02, 0.05)
        v.co.x += rnd.uniform(-0.006, 0.006)
    at(heap, (0, 0, 1.07))
    parts.append(heap)
    parts.append(at(A.cylinder("kettle", 0.12, 0.14, (0, 0, 0), "steel", 20), (0, 0, 1.45)))
    roof = A.box("roof", (0.9, 0.64, 0.08), material="popcorn_red", round_=0.03, segments=2)
    at(roof, (0, 0, 1.64))
    parts.append(roof)
    parts.append(at(A.box("sign", (0.7, 0.03, 0.2), material="jukebox_glow", round_=0.01), (0, 0.3, 1.8)))
    whole(keep, "popcorn_machine", parts)

    bucket = A.lathe("bucket", [(0.0, 0.0), (0.065, 0.0), (0.095, 0.2), (0.09, 0.2), (0.06, 0.012), (0.0, 0.012)], 24, (0, 0, 0), "stripe_white")
    bparts = [bucket]
    for k in range(8):
        a = 2 * math.pi * k / 8
        s = A.box(f"s{k}", (0.026, 0.006, 0.2), material="popcorn_red")
        at(s, (0, 0, 0.1))
        # (leaned out with the bucket's taper, round it)
        at(s, (0, 0, 0), (-8.5, 0, 0))
        at(s, (0, 0.068, 0))
        at(s, (0, 0, 0), (0, 0, math.degrees(a)))
        bparts.append(s)
    top = A.lathe("top", [(0.092, 0.195), (0.09, 0.23), (0.06, 0.26), (0.0, 0.27)], 24, (0, 0, 0), "popcorn")
    for v in top.data.vertices:
        if v.co.z > 0.2:
            v.co.z += rnd.uniform(-0.012, 0.012)
    bparts.append(top)
    whole(keep, "popcorn_bucket", bparts)
    k = sphere("kernel", 0.011, "popcorn", 8, 6)
    for v in k.data.vertices:
        v.co *= rnd.uniform(0.8, 1.25)
    whole(keep, "popcorn_kernel", [k])


def soda(keep):
    """A countertop soda fountain (three nozzles over a drip tray, lit flavour panels) and its cup: lid and straw."""
    parts = [at(A.box("body", (0.62, 0.42, 0.56), material="chrome", round_=0.02, segments=2), (0, 0, 0.28))]
    for k, mat in enumerate(("can_red", "can_blue", "can_green")):
        x = -0.2 + k * 0.2
        parts.append(at(A.box(f"panel{k}", (0.15, 0.02, 0.18), material=mat, round_=0.008), (x, 0.215, 0.42)))
        parts.append(at(A.cylinder(f"noz{k}", 0.018, 0.05, (0, 0, 0), "steel_dark", 12), (x, 0.17, 0.24)))
        parts.append(at(A.box(f"lev{k}", (0.03, 0.06, 0.1), material="plastic_white", round_=0.008), (x, 0.24, 0.27)))
    parts.append(at(A.box("tray", (0.6, 0.18, 0.03), material="metal_dark"), (0, 0.16, 0.015)))
    whole(keep, "soda_fountain", parts)
    cup = A.lathe("cup", [(0.0, 0.0), (0.038, 0.0), (0.05, 0.16), (0.046, 0.16), (0.034, 0.008), (0.0, 0.008)], 20, (0, 0, 0), "cup_white")
    band = A.lathe("band", [(0.041, 0.05), (0.046, 0.12), (0.0465, 0.12), (0.0415, 0.05)], 20, (0, 0, 0), "popcorn_red")
    lid = A.cylinder("lid", 0.052, 0.012, (0, 0, 0.166), "plastic_red", 20)
    straw = A.cylinder("straw", 0.004, 0.14, (0.012, 0, 0.22), "stripe_white", 8)
    whole(keep, "soda_cup", [cup, band, lid, straw])


def counter(keep):
    """The concession counter: dark wood, a lit glass case of candy boxes, gold trim."""
    w = 2.4
    parts = [at(A.box("base", (w, 0.62, 0.92), material="wood_dark", round_=0.015), (0, 0, 0.46))]
    parts.append(at(A.box("top", (w + 0.06, 0.68, 0.04), material="deco_gold", round_=0.01), (0, 0, 0.94)))
    parts.append(at(A.box("case", (w - 0.1, 0.02, 0.5), material="glass_aqua"), (0, 0.31, 0.5)))
    parts.append(at(A.box("shelf", (w - 0.12, 0.4, 0.015), material="chrome"), (0, 0.12, 0.42)))
    rnd = random.Random(9)
    mats = ("candy_red", "candy_yellow", "candy_blue", "popcorn_red", "pinata_pink")
    for k in range(14):
        x = -w / 2 + 0.16 + k * ((w - 0.32) / 13)
        parts.append(at(A.box(f"b{k}", (0.11, 0.05, rnd.uniform(0.13, 0.18)), material=mats[k % 5], round_=0.006), (x, 0.2, 0.5)))
    parts.append(at(A.box("kick", (w, 0.02, 0.08), material="deco_gold"), (0, 0.31, 0.06)))
    whole(keep, "concession_counter", parts)


def lobby_bits(keep):
    """Poster frames ringed with bulbs, the shoe shelf, the photo kiosk and its strip, the house lights' knife switch,
    the curtain's pull cord."""
    fw, fh = 1.1, 1.6
    frame = [at(A.box("back", (fw, 0.04, fh), material="deco_dark"), (0, 0.02, 0))]
    for (sx, sz, x, z) in ((fw, 0.06, 0, fh / 2 - 0.03), (fw, 0.06, 0, -fh / 2 + 0.03), (0.06, fh, fw / 2 - 0.03, 0), (0.06, fh, -fw / 2 + 0.03, 0)):
        frame.append(at(A.box("edge", (sx, 0.07, sz), material="deco_gold", round_=0.008), (x, 0.035, z)))
    for k in range(7):
        for side in (-1, 1):
            frame.append(at(sphere("bulb", 0.018, "sconce_glass", 8, 6), (side * (fw / 2 - 0.03), 0.08, -fh / 2 + 0.15 + k * (fh - 0.3) / 6)))
    whole(keep, "poster_frame", frame)

    sw, sd, sh = 1.8, 0.4, 2.0
    shelf = [at(A.box("side_l", (0.04, sd, sh), material="wood_dark"), (-sw / 2, 0, sh / 2)), at(A.box("side_r", (0.04, sd, sh), material="wood_dark"), (sw / 2, 0, sh / 2))]
    shelf.append(at(A.box("back", (sw, 0.02, sh), material="deco_dark"), (0, -sd / 2, sh / 2)))
    for k in range(5):
        z = 0.04 + k * 0.45
        shelf.append(at(A.box(f"board{k}", (sw, sd, 0.03), material="wood_dark"), (0, 0, z)))
        shelf.append(at(A.box(f"neon{k}", (sw - 0.04, 0.012, 0.012), material="neon_pink" if k % 2 else "neon_cyan"), (0, sd / 2 - 0.004, z + 0.02)))
    whole(keep, "shoe_shelf", shelf)

    kiosk = [at(A.box("cab", (0.72, 0.52, 1.95), material="deco_dark", round_=0.02), (0, 0, 0.975))]
    for z in (0.02, 1.93):
        kiosk.append(at(A.box("trim", (0.74, 0.54, 0.04), material="deco_gold", round_=0.008), (0, 0, z)))
    kiosk.append(at(A.box("screen", (0.4, 0.02, 0.28), material="tv_screen", round_=0.01), (0, 0.262, 1.3)))
    kiosk.append(at(A.cylinder("lensring", 0.06, 0.04, (0, 0, 0), "chrome", 20, axis='Y'), (0, 0.27, 1.6)))
    kiosk.append(at(A.cylinder("lens", 0.045, 0.045, (0, 0, 0), "glass_dark", 20, axis='Y'), (0, 0.275, 1.6)))
    kiosk.append(at(A.box("flash", (0.5, 0.03, 0.1), material="sconce_glass", round_=0.01), (0, 0.265, 1.8)))
    kiosk.append(at(A.box("slot", (0.18, 0.03, 0.025), material="booth_inside"), (0.18, 0.265, 0.95)))
    kiosk.append(at(A.box("btn", (0.08, 0.03, 0.08), material="button_red", round_=0.02), (-0.18, 0.27, 1.05)))
    whole(keep, "photo_kiosk", kiosk)
    strip = A.box("strip", (0.07, 0.003, 0.21), material="cup_white")
    whole(keep, "photo_strip", [strip])

    sb = [at(A.box("plate", (0.26, 0.03, 0.36), material="brass", round_=0.012), (0, 0.015, 0))]
    sb.append(at(A.box("insul", (0.2, 0.03, 0.06), material="rubber_black"), (0, 0.04, -0.12)))
    for x in (-0.05, 0.05):
        sb.append(at(A.box(f"clip{x}", (0.012, 0.05, 0.05), material="steel"), (x, 0.06, 0.12)))
    whole(keep, "knife_switch", sb)
    blade = [at(A.box("b1", (0.01, 0.012, 0.22), material="steel"), (-0.05, 0, 0.11)), at(A.box("b2", (0.01, 0.012, 0.22), material="steel"), (0.05, 0, 0.11))]
    blade.append(at(A.box("bar", (0.13, 0.016, 0.016), material="steel"), (0, 0, 0.22)))
    blade.append(at(A.cylinder("handle", 0.016, 0.1, (0, 0, 0), "rubber_black", 12), (0, 0.06, 0.22)))
    whole(keep, "knife_switch_blade", blade)

    pts = [(0.012 * math.sin(k * 0.7), 0.012 * math.cos(k * 0.7), -k * 0.08) for k in range(21)]
    cord = A.tube_along("cord", pts, 0.012, "deco_gold", 8)
    tassel = A.lathe("tassel", [(0.0, -1.6), (0.035, -1.62), (0.05, -1.78), (0.02, -1.82), (0.0, -1.83)], 16, (0, 0, 0), "deco_gold")
    whole(keep, "pull_cord", [cord, tassel])


def cutouts(keep):
    """Cardboard stand-ups: two goons in tracksuits (pink, green) with shades, and a granny in her cardigan. Flat
    painted layers on a board, a prop leg at the back."""
    def person(name, suit, skin, hair, granny=False):
        parts = []
        outline = [(-0.27, 0.0), (0.27, 0.0), (0.3, 0.85), (0.27, 1.25), (0.16, 1.4), (0.13, 1.48)] + circle(0, 1.62, 0.15, 14, -0.9, math.pi + 0.9) + [(-0.13, 1.48), (-0.16, 1.4), (-0.27, 1.25), (-0.3, 0.85)]
        parts.append(flat("board", outline, 0.008, "cardboard"))
        parts.append(flat("legs", [(-0.25, 0.0), (0.25, 0.0), (0.24, 0.78), (-0.24, 0.78)], 0.004, "jeans_blue" if granny else suit, 0.006))
        parts.append(flat("gap", [(-0.012, 0.0), (0.012, 0.0), (0.012, 0.7), (-0.012, 0.7)], 0.004, "cardboard", 0.0085))
        torso = [(-0.28, 0.75), (0.28, 0.75), (0.3, 0.85), (0.27, 1.25), (0.16, 1.4), (-0.16, 1.4), (-0.27, 1.25), (-0.3, 0.85)]
        parts.append(flat("torso", torso, 0.004, "granny_purple" if granny else suit, 0.007))
        if not granny:
            for x in (-0.24, 0.24):
                parts.append(flat("stripe", [(x - 0.015, 0.0), (x + 0.015, 0.0), (x + 0.015, 1.3), (x - 0.015, 1.3)], 0.003, "stripe_white", 0.0095))
            parts.append(flat("zip", [(-0.006, 0.78), (0.006, 0.78), (0.006, 1.38), (-0.006, 1.38)], 0.003, "chrome", 0.0095))
        parts.append(flat("neck", [(-0.06, 1.36), (0.06, 1.36), (0.06, 1.5), (-0.06, 1.5)], 0.004, skin, 0.0075))
        parts.append(flat("face", circle(0, 1.62, 0.135, 16), 0.004, skin, 0.009))
        parts.append(flat("hair", circle(0, 1.66, 0.14, 16, 0.25, math.pi - 0.25), 0.004, hair, 0.0105))
        if granny:
            parts.append(flat("bun", circle(0, 1.8, 0.07, 12), 0.004, hair, 0.0105))
            for x in (-0.05, 0.05):
                parts.append(flat("glass", circle(x, 1.61, 0.032, 10), 0.003, "glass_dark", 0.0115))
            parts.append(flat("smile", [(-0.04, 1.54), (0.04, 1.54), (0.035, 1.53), (-0.035, 1.53)], 0.003, "candy_red", 0.0115))
        else:
            parts.append(flat("shades", [(-0.11, 1.6), (0.11, 1.6), (0.1, 1.65), (-0.1, 1.65)], 0.003, "rubber_black", 0.0115))
            parts.append(flat("mouth", [(-0.05, 1.53), (0.05, 1.53), (0.05, 1.545), (-0.05, 1.545)], 0.003, "hair_black", 0.0115))
        leg = A.box("prop", (0.04, 0.4, 0.008), material="cardboard")
        at(leg, (0, 0, 0), (-60, 0, 0))
        at(leg, (0, -0.15, 0.17))
        parts.append(leg)
        whole(keep, name, parts)

    person("cutout_goon", "goon_pink", "goon_skin", "hair_black")
    person("cutout_goon2", "goon_green", "goon_skin", "hair_black")
    person("cutout_granny", "granny_purple", "goon_skin", "hair_white", granny=True)


# ======================================================================================== the arcade

def door_kicker(keep):
    """DOOR KICKER: a facade of five doorways (kick height), a marquee over them; each door its own model, its
    origin at its hinge (left edge, bottom)."""
    n, dw, dh, gap, sill = 5, 0.62, 1.2, 0.18, 0.22
    width = n * dw + (n + 1) * gap
    parts = []
    parts.append(at(A.box("top", (width, 0.4, 2.6 - sill - dh), material="deco_dark"), (0, 0, sill + dh + (2.6 - sill - dh) / 2)))
    parts.append(at(A.box("sill", (width, 0.4, sill), material="deco_dark"), (0, 0, sill / 2)))
    for k in range(n + 1):
        x = -width / 2 + gap / 2 + k * (dw + gap)
        parts.append(at(A.box(f"jamb{k}", (gap, 0.4, dh), material="deco_dark"), (x, 0, sill + dh / 2)))
    for k in range(n):
        x = -width / 2 + gap + dw / 2 + k * (dw + gap)
        parts.append(at(A.box(f"inside{k}", (dw, 0.02, dh), material="booth_inside"), (x, -0.19, sill + dh / 2)))
        for (sx, sz, ox, oz) in ((dw + 0.06, 0.04, 0, dh / 2 + 0.02), (0.04, dh, -dw / 2 - 0.02, 0), (0.04, dh, dw / 2 + 0.02, 0)):
            parts.append(at(A.box("trim", (sx, 0.05, sz), material="deco_gold"), (x + ox, 0.2, sill + dh / 2 + oz)))
    parts.append(at(A.box("marquee", (width - 0.2, 0.06, 0.4), material="deco_gold", round_=0.01), (0, 0.22, 2.3)))
    whole(keep, "door_kicker", parts)
    door = [at(A.box("leaf", (dw - 0.02, 0.04, dh - 0.02), material="wood_dark", round_=0.006), ((dw - 0.02) / 2, 0, (dh - 0.02) / 2))]
    door.append(at(A.box("panel1", (dw - 0.14, 0.012, 0.42), material="wood_light", round_=0.004), ((dw - 0.02) / 2, 0.024, 0.32)))
    door.append(at(A.box("panel2", (dw - 0.14, 0.012, 0.42), material="wood_light", round_=0.004), ((dw - 0.02) / 2, 0.024, 0.84)))
    door.append(at(sphere("knob", 0.022, "brass", 10, 6), (dw - 0.08, 0.04, 0.6)))
    whole(keep, "dk_door", door)


def gallery(keep):
    """The shooting gallery: a counter under a striped awning, three chrome rails across a velvet back wall, goon
    targets on sticks (pizza slices for bonus), the light gun on its cable."""
    w = 3.2
    parts = [at(A.box("counter", (w, 0.6, 1.02), material="deco_dark", round_=0.012), (0, 0, 0.51))]
    parts.append(at(A.box("ctop", (w + 0.06, 0.66, 0.04), material="deco_gold", round_=0.008), (0, 0, 1.04)))
    parts.append(at(A.box("back", (w, 0.08, 2.6), material="velvet"), (0, -2.6, 1.3)))
    for side in (-1, 1):
        parts.append(at(A.box(f"side{side}", (0.08, 2.6, 2.6), material="deco_dark"), (side * (w / 2 + 0.04), -1.3, 1.3)))
    for k, z in enumerate((1.25, 1.65, 2.05)):
        parts.append(at(A.cylinder(f"rail{k}", 0.015, w, (0, 0, 0), "chrome", 10, axis='X'), (0, -2.1 + k * 0.15, z)))
    for k in range(12):
        x = -w / 2 + w / 24 + k * w / 12
        s = A.box(f"awn{k}", (w / 12, 0.8, 0.02), material="stripe_red" if k % 2 else "stripe_white")
        at(s, (0, 0, 0), (-20, 0, 0))
        at(s, (x, -0.15, 2.75))
        parts.append(s)
    parts.append(at(A.box("valance", (w + 0.1, 0.03, 0.25), material="deco_gold", round_=0.008), (0, 0.24, 2.62)))
    whole(keep, "gallery", parts)

    def target(name, mats):
        p = [at(A.box("stick", (0.02, 0.02, 0.32), material="chrome"), (0, 0, 0.16))]
        p += mats
        whole(keep, name, p)

    goon = []
    goon.append(flat("body", [(-0.11, 0.3), (0.11, 0.3), (0.12, 0.47), (0.06, 0.53), (-0.06, 0.53), (-0.12, 0.47)], 0.01, "goon_pink"))
    goon.append(flat("head", circle(0, 0.6, 0.075, 14), 0.01, "goon_skin", 0.002))
    goon.append(flat("shades", [(-0.06, 0.6), (0.06, 0.6), (0.055, 0.625), (-0.055, 0.625)], 0.004, "rubber_black", 0.0075))
    goon.append(flat("ring", circle(0, 0.6, 0.09, 14), 0.006, "stripe_white", -0.004))
    target("gallery_goon", goon)
    pizza = [flat("slice", [(-0.1, 0.62), (0.1, 0.62), (0.0, 0.32)], 0.012, "pizza_cheese")]
    pizza.append(flat("crust", [(-0.11, 0.6), (0.11, 0.6), (0.11, 0.65), (-0.11, 0.65)], 0.016, "pizza_crust"))
    for (x, z) in ((-0.03, 0.55), (0.035, 0.5), (0.0, 0.42)):
        pizza.append(flat("pep", circle(x, z, 0.022, 10), 0.004, "pepperoni", 0.008))
    target("gallery_pizza", pizza)

    gun = [at(A.box("body", (0.045, 0.2, 0.06), material="plastic_orange", round_=0.012, segments=2), (0, 0.06, 0.1))]
    grip = A.box("grip", (0.04, 0.06, 0.12), material="plastic_orange", round_=0.012, segments=2)
    at(grip, (0, 0, 0), (15, 0, 0))
    at(grip, (0, -0.02, 0.04))
    gun.append(grip)
    gun.append(at(A.cylinder("muzzle", 0.018, 0.03, (0, 0, 0), "neon_cyan", 14, axis='Y'), (0, 0.17, 0.11)))
    gun.append(at(A.box("trig", (0.012, 0.02, 0.03), material="rubber_black"), (0, 0.025, 0.06)))
    whole(keep, "lightgun", gun)


def kickometer(keep):
    """The KICK-O-METER: a tall red cabinet with its column of lamps (the lamps are placed by the game, one model),
    a padded target on a swinging arm at hip height (its origin at the arm's pivot)."""
    parts = [at(A.box("cab", (0.82, 0.6, 2.5), material="plastic_red", round_=0.03, segments=2), (0, -0.1, 1.25))]
    parts.append(at(A.box("face", (0.56, 0.03, 1.5), material="deco_dark", round_=0.01), (0, 0.205, 1.6)))
    parts.append(at(A.box("crown", (0.9, 0.66, 0.12), material="deco_gold", round_=0.02), (0, -0.1, 2.56)))
    parts.append(at(A.box("arm_box", (0.26, 0.2, 0.26), material="metal_dark", round_=0.02), (0, 0.28, 0.62)))
    whole(keep, "kickometer", parts)
    pad = [at(A.cylinder("arm", 0.025, 0.32, (0, 0, 0), "chrome", 12, axis='Y'), (0, 0.16, 0))]
    pad.append(at(A.cylinder("pad", 0.19, 0.1, (0, 0, 0), "door_leather", 28, axis='Y', round_=0.03), (0, 0.34, 0)))
    pad.append(at(A.cylinder("ring", 0.2, 0.03, (0, 0, 0), "chrome", 28, axis='Y'), (0, 0.29, 0)))
    pad.append(at(A.cylinder("dot", 0.07, 0.11, (0, 0, 0), "hazard_yellow", 20, axis='Y'), (0, 0.345, 0)))
    whole(keep, "meter_pad", pad)
    lamp = A.cylinder("lamp", 0.07, 0.03, (0, 0, 0), "lamp", 20, axis='Y')
    whole(keep, "meter_lamp", [lamp])


def shoe_toss(keep):
    """SHOE TOSS: a skee-ball lane. A felt ramp rising to a backboard with five rings round their holes (10 to 100),
    chrome rails along its sides. Origin at the near end."""
    L, W = 3.2, 0.9
    parts = [at(A.box("lane", (W, L, 0.06), material="felt_green"), (0, L / 2, 0.3))]
    for side in (-1, 1):
        parts.append(at(A.box(f"rail{side}", (0.06, L, 0.3), material="chrome", round_=0.01), (side * (W / 2 + 0.03), L / 2, 0.45)))
        parts.append(at(A.box(f"legs{side}", (0.05, L, 0.3), material="deco_dark"), (side * (W / 2 - 0.05), L / 2, 0.15)))
    ramp = A.box("ramp", (W, 0.6, 0.05), material="felt_green")
    at(ramp, (0, 0, 0), (25, 0, 0))
    at(ramp, (0, L + 0.25, 0.42))
    parts.append(ramp)
    parts.append(at(A.box("board", (W + 0.12, 0.1, 1.5), material="deco_dark"), (0, L + 0.95, 1.05)))
    holes = ((0, 0.75, 0.16, "neon_cyan"), (-0.26, 1.15, 0.11, "neon_pink"), (0.26, 1.15, 0.11, "neon_pink"), (0, 1.5, 0.09, "hazard_yellow"), (0, 1.8, 0.075, "candy_red"))
    shelf = A.box("shelf", (W, 0.9, 0.04), material="deco_dark")
    at(shelf, (0, 0, 0), (-12, 0, 0))
    at(shelf, (0, L + 0.5, 1.0))
    parts.append(shelf)
    for k, (x, z, r, mat) in enumerate(holes):
        ring = A.tube_along(f"ring{k}", [(x + r * math.cos(2 * math.pi * i / 24), L + 0.89, z + r * math.sin(2 * math.pi * i / 24)) for i in range(24)], 0.014, mat, 8, closed=True)
        parts.append(ring)
        parts.append(at(A.cylinder(f"hole{k}", r - 0.01, 0.02, (0, 0, 0), "booth_inside", 20, axis='Y'), (x, L + 0.9, z)))
    parts.append(at(A.box("marquee", (W + 0.1, 0.06, 0.3), material="deco_gold", round_=0.01), (0, L + 0.9, 1.95)))
    whole(keep, "shoe_toss", parts)


def bowling(keep):
    """GOON BOWLING: a polished lane between gutters to a pin deck with a dark pit and a backstop; the goon pins (a
    pin in a pink tracksuit band with shades) and the red kickball. Lane origin at the foul line."""
    L, W = 7.0, 1.05
    parts = [at(A.box("lane", (W, L, 0.05), material="lane_wood"), (0, L / 2, 0.025))]
    for side in (-1, 1):
        parts.append(at(A.box(f"gut{side}", (0.2, L, 0.03), material="metal_dark"), (side * (W / 2 + 0.1), L / 2, 0.015)))
        parts.append(at(A.box(f"wall{side}", (0.06, L + 0.8, 0.45), material="deco_dark"), (side * (W / 2 + 0.23), L / 2 + 0.4, 0.225)))
        parts.append(at(A.box(f"neon{side}", (0.012, L + 0.8, 0.012), material="neon_pink" if side < 0 else "neon_cyan"), (side * (W / 2 + 0.2), L / 2 + 0.4, 0.46)))
    parts.append(at(A.box("pit", (W + 0.4, 0.6, 0.02), material="booth_inside"), (0, L + 0.3, 0.0)))
    parts.append(at(A.box("back", (W + 0.52, 0.1, 1.2), material="deco_dark"), (0, L + 0.65, 0.6)))
    parts.append(at(A.box("foul", (W, 0.03, 0.006), material="neon_pink"), (0, 0, 0.053)))
    for k in range(5):
        parts.append(at(A.box(f"arrow{k}", (0.03, 0.1, 0.004), material="neon_cyan"), (-0.36 + k * 0.18, 2.0, 0.052)))
    whole(keep, "bowling_lane", parts)
    pin = A.lathe("pin", [(0.0, 0.0), (0.04, 0.0), (0.06, 0.08), (0.06, 0.13), (0.035, 0.24), (0.025, 0.27), (0.035, 0.32), (0.034, 0.36), (0.0, 0.38)], 20, (0, 0, 0), "plastic_white")
    band = A.lathe("band", [(0.062, 0.09), (0.062, 0.13), (0.06, 0.135), (0.059, 0.085)], 20, (0, 0, 0), "goon_pink")
    shades = A.box("shades", (0.06, 0.02, 0.016), material="rubber_black", round_=0.004)
    at(shades, (0, 0.032, 0.33))
    whole(keep, "goon_pin", [pin, band, shades])
    ball = sphere("kickball", 0.11, "ball_red", 20, 12)
    seam = A.lathe("seam", [(0.1105, -0.004), (0.1105, 0.004), (0.109, 0.004), (0.109, -0.004)], 24, (0, 0, 0), "stripe_white")
    whole(keep, "kickball", [ball, seam])


def boards(keep):
    """The high score board (the scores are drawn on it) and the arcade's marquee frames."""
    parts = [at(A.box("back", (1.6, 0.06, 1.1), material="deco_dark"), (0, 0, 0))]
    for (sx, sz, x, z) in ((1.6, 0.04, 0, 0.53), (1.6, 0.04, 0, -0.53), (0.04, 1.1, 0.78, 0), (0.04, 1.1, -0.78, 0)):
        parts.append(at(A.box("edge", (sx, 0.08, sz), material="neon_cyan"), (x, 0.01, z)))
    whole(keep, "score_board", parts)


def build(models, keep):
    vending(keep)
    cans(keep)
    cooler(keep)
    plates(keep)
    pinata(keep)
    barrel(keep)
    weapons(keep)
    controls(keep)
    popcorn(keep)
    soda(keep)
    counter(keep)
    lobby_bits(keep)
    cutouts(keep)
    door_kicker(keep)
    gallery(keep)
    kickometer(keep)
    shoe_toss(keep)
    bowling(keep)
    boards(keep)
