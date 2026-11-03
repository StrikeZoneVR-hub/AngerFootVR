"""
Anger Foot VR's models, made in Blender from code:
  blender -b --factory-startup --python tools/blender/build_props.py -- <out.afvp> <preview folder>
Movie house: the cinema seat (and the row's end), velvet curtains, the swagged valance, the art deco proscenium round
the screen (and its neon), the projection booth, the lobby doors, step lights and sconces.
Rage room: things to kick and break, each with its pieces (pre-broken: Voronoi cells, or its own planks and legs).
"""
import sys, os, math, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy, bmesh
from mathutils import Vector, Matrix
import afvr as A

args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = args[0] if args else os.path.join(os.path.dirname(__file__), 'props.afvp')
PREVIEW = args[1] if len(args) > 1 else None

A.reset()
models = []  # (object, group, offset)


def keep(ob, group="", offset=(0, 0, 0)):
    models.append((ob, group, offset))
    return ob


def at(ob, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1)):
    return A.transform(ob, loc, tuple(math.radians(r) for r in rot), scale)


# ======================================================================================== movie house

def seat():
    """A cinema seat facing +y: a deep cushion, a curved back tipped back, its shell, the left armrest and standard."""
    parts = []
    cushion = A.box("cushion", (0.50, 0.50, 0.12), material="velvet", round_=0.045, segments=4)
    at(cushion, (0, 0.04, 0.45), (-5, 0, 0))
    parts.append(cushion)
    back = A.box("back", (0.52, 0.10, 0.64), material="velvet", round_=0.045, segments=4)
    for v in back.data.vertices:  # curved round you
        v.co.y += 0.35 * v.co.x * v.co.x
    at(back, (0, 0, 0.32), (14, 0, 0))
    at(back, (0, -0.25, 0.47))
    parts.append(back)
    shell = A.box("shell", (0.55, 0.05, 0.68), material="seat_plastic", round_=0.02)
    for v in shell.data.vertices:
        v.co.y += 0.35 * v.co.x * v.co.x
    at(shell, (0, -0.075, 0.34), (14, 0, 0))
    at(shell, (0, -0.25, 0.44))
    parts.append(shell)
    parts += armrest(-0.31)
    s = A.join("seat", parts)
    return A.smooth(s, 40)


def armrest(x):
    """A standard (the side panel) with an armrest and a cup holder, at x."""
    std = A.box("standard", (0.05, 0.52, 0.64), material="metal_dark", round_=0.012)
    at(std, (x, -0.04, 0.32))
    pad = A.box("pad", (0.075, 0.50, 0.05), material="armrest", round_=0.02)
    at(pad, (x, -0.03, 0.665))
    ring = A.tube_along("cup", [(x + 0.045 * math.cos(a), 0.25 + 0.045 * math.sin(a), 0.675) for a in [2 * math.pi * i / 16 for i in range(16)]], 0.008, "chrome", 6, closed=True)
    return [std, pad, ring]


def seat_end():
    return A.smooth(A.join("seat_end", armrest(0.31)), 40)


def curtain(name, width=2.4, height=7.2, folds=9):
    """A heavy velvet curtain: deep folds, gathered at the top, falling a little wider at the floor."""
    bm = bmesh.new()
    cols, rows = folds * 10, 28
    grid = []
    for r in range(rows + 1):
        t = r / rows  # 0 at the floor, 1 at the top
        row = []
        for c in range(cols + 1):
            u = c / cols
            amp = 0.07 + 0.06 * (1 - t)
            x = (u - 0.5) * width * (0.92 + 0.08 * (1 - t))
            y = amp * math.sin(u * folds * 2 * math.pi) + 0.02 * math.sin(u * folds * 4 * math.pi + 1.3)
            z = t * height + (0.04 * math.sin(u * 17.0) if r == 0 else 0)
            row.append(bm.verts.new((x, y, z)))
        grid.append(row)
    for r in range(rows):
        for c in range(cols):
            bm.faces.new((grid[r][c], grid[r][c + 1], grid[r + 1][c + 1], grid[r + 1][c]))
    ob = A.obj_from_bmesh(name, bm, "velvet_curtain")
    A.solidify(ob, 0.025)
    return A.smooth(ob, 70)


def valance(width=14.4, swags=7, drop=1.0):
    """The swagged valance across the top of the stage, with a gold fringe along its scallops."""
    bm = bmesh.new()
    cols, rows = swags * 18, 10
    grid = []
    for r in range(rows + 1):
        t = r / rows
        row = []
        for c in range(cols + 1):
            u = c / cols
            local = (u * swags) % 1.0
            sag = drop * (0.35 + 0.65 * (1 - (2 * local - 1) ** 2))  # each swag dips in its middle
            x = (u - 0.5) * width
            z = -sag * (1 - t)
            y = 0.06 * math.sin(t * math.pi * 3 + local * 2) * (1 - t) + 0.05 * math.sin(local * math.pi) * (1 - t)
            row.append(bm.verts.new((x, y, z)))
        grid.append(row)
    for r in range(rows):
        for c in range(cols):
            bm.faces.new((grid[r][c], grid[r][c + 1], grid[r + 1][c + 1], grid[r + 1][c]))
    ob = A.obj_from_bmesh("valance_cloth", bm, "velvet_curtain")
    A.solidify(ob, 0.03)
    A.smooth(ob, 70)
    edge = []
    for c in range(cols + 1):
        u = c / cols
        local = (u * swags) % 1.0
        sag = drop * (0.35 + 0.65 * (1 - (2 * local - 1) ** 2))
        edge.append(((u - 0.5) * width, 0.04, -sag - 0.02))
    fringe = A.tube_along("fringe", edge, 0.035, "gold", 8)
    tassels = []
    for k in range(swags + 1):
        x = (k / swags - 0.5) * width
        t = A.lathe(f"tassel{k}", [(0.0, -0.75), (0.07, -0.68), (0.05, -0.45), (0.02, -0.3), (0.035, -0.22), (0.0, -0.12)], 12, (x, 0.08, 0), "gold")
        tassels.append(t)
    return A.join("valance", [ob, fringe] + tassels)


def proscenium(w=11.8, h=6.6):
    """The art deco frame round the screen: three stepped bands, fluted pilasters up the sides, a sunburst crown."""
    parts = []
    bands = [(0.75, 0.25, "deco_dark"), (0.42, 0.42, "deco_gold"), (0.18, 0.58, "deco_dark")]
    for k, (bw, depth, m) in enumerate(bands):
        ow, oh = w + 2 * bw, h + 2 * bw
        for (sx, sz, cx, cz) in [(ow, bw, 0, h / 2 + bw / 2), (ow, bw, 0, -h / 2 - bw / 2), (bw, h, -w / 2 - bw / 2, 0), (bw, h, w / 2 + bw / 2, 0)]:
            b = A.box(f"band{k}", (sx, depth, sz), material=m, round_=0.02)
            at(b, (cx, -depth / 2, cz + h / 2 + 0.9))
            parts.append(b)
    for sx in (-1, 1):
        x = sx * (w / 2 + 1.05)
        col = A.box("pilaster", (0.5, 0.3, h + 2.2), material="deco_dark", round_=0.02)
        at(col, (x, -0.15, (h + 2.2) / 2))
        parts.append(col)
        for f in range(-2, 3):
            flute = A.box("flute", (0.05, 0.06, h + 1.4), material="deco_gold", round_=0.015)
            at(flute, (x + f * 0.09, 0.02, (h + 1.4) / 2 + 0.4))
            parts.append(flute)
        cap = A.box("cap", (0.62, 0.38, 0.22), material="deco_gold", round_=0.03)
        at(cap, (x, -0.12, h + 2.3))
        parts.append(cap)
    # the sunburst over the middle
    top = h + 0.9 + 0.75
    fan = []
    for i in range(13):
        a = math.pi * (i + 0.5) / 13
        r0, r1 = 0.35, 1.5 if i % 2 == 0 else 1.15
        ray = A.box(f"ray{i}", (0.09, 0.08, r1 - r0), material="deco_gold", round_=0.015)
        at(ray, (0, 0, (r0 + r1) / 2), (0, 90 - math.degrees(a), 0))
        at(ray, (0, 0.04, top))
        fan.append(ray)
    hub = A.cylinder("hub", 0.36, 0.12, (0, 0.05, top), "deco_gold", 32, axis='Y')
    return A.join("proscenium", parts + fan + [hub])


def proscenium_neon(w=11.8, h=6.6):
    """The neon tube round the screen's opening."""
    m = 0.12
    pts = [(-w / 2 + m, 0.05, 0.9 + m), (w / 2 - m, 0.05, 0.9 + m), (w / 2 - m, 0.05, 0.9 + h - m), (-w / 2 + m, 0.05, 0.9 + h - m)]
    full = []
    for i in range(4):
        a, b = Vector(pts[i]), Vector(pts[(i + 1) % 4])
        for k in range(8):
            full.append(tuple(a.lerp(b, k / 8)))
    return A.tube_along("proscenium_neon", full, 0.025, "neon_pink", 8, closed=True)


def booth():
    """The projection booth's window in the back wall, the projector behind it, its lens to the screen (+y)."""
    parts = []
    for (sx, sz, cx, cz) in [(2.0, 0.12, 0, 0.56), (2.0, 0.16, 0, -0.58), (0.14, 1.2, -1.0, 0), (0.14, 1.2, 1.0, 0)]:
        b = A.box("bframe", (sx, 0.3, sz), material="deco_dark", round_=0.02)
        at(b, (cx, 0, cz))
        parts.append(b)
    inside = A.box("binside", (1.9, 0.6, 1.0), material="booth_inside")
    at(inside, (0, -0.42, 0))
    parts.append(inside)
    body = A.box("projector", (0.45, 0.7, 0.42), material="metal_dark", round_=0.04)
    at(body, (0.15, -0.35, -0.05))
    parts.append(body)
    lens = A.cylinder("lens", 0.09, 0.35, (0.15, 0.12, 0.0), "chrome", 24, axis='Y', round_=0.01)
    parts.append(lens)
    glass = A.cylinder("lensglass", 0.07, 0.02, (0.15, 0.3, 0.0), "lens_glow", 24, axis='Y')
    parts.append(glass)
    for dz, dy in ((0.42, -0.2), (0.42, -0.55)):
        reel = A.cylinder("reel", 0.26, 0.05, (-0.05, dy, dz), "chrome", 32, axis='X', round_=0.01)
        parts.append(reel)
    return A.join("booth", parts)


def door_leaf(name, hinge_left):
    """One leaf of the padded lobby doors, its origin at its hinge (so it swings), opening into the room behind (-y)."""
    w, h, t = 0.95, 2.25, 0.07
    s = 1 if hinge_left else -1
    cx = s * w / 2
    parts = []
    panel = A.box("panel", (w, t, h), material="door_leather", round_=0.03, segments=3)
    at(panel, (cx, 0, h / 2))
    parts.append(panel)
    # the padding's buttons in a diamond pattern
    for r in range(7):
        for c in range(4):
            x = cx + (c - 1.5 + (0.5 if r % 2 else 0)) * 0.2
            z = 0.35 + r * 0.24
            if abs(x - cx) > w / 2 - 0.08 or 1.15 < z < 1.75 and abs(x - cx) < 0.2:
                continue
            b = A.cylinder("button", 0.018, 0.03, (x, 0.04, z), "chrome", 10, axis='Y')
            parts.append(b)
    ring = A.tube_along("porthole", [(cx + 0.17 * math.cos(a), 0.045, 1.45 + 0.17 * math.sin(a)) for a in [2 * math.pi * i / 24 for i in range(24)]], 0.022, "chrome", 8, closed=True)
    parts.append(ring)
    glass = A.cylinder("glass", 0.16, 0.02, (cx, 0.0, 1.45), "glass_dark", 24, axis='Y')
    parts.append(glass)
    bar = A.cylinder("pushbar", 0.02, w * 0.7, (cx, 0.1, 1.05), "chrome", 12, axis='X', round_=0.005)
    parts.append(bar)
    for k in (-1, 1):
        post = A.cylinder("barpost", 0.015, 0.07, (cx + k * w * 0.32, 0.065, 1.05), "chrome", 8, axis='Y')
        parts.append(post)
    plate = A.box("kickplate", (w - 0.08, 0.01, 0.28), material="chrome", round_=0.004)
    at(plate, (cx, 0.04, 0.16))
    parts.append(plate)
    return A.join(name, parts)


def door_frame():
    parts = []
    for x in (-1.0, 1.0):
        j = A.box("jamb", (0.12, 0.3, 2.42), material="door_frame", round_=0.02)
        at(j, (x, 0, 1.21))
        parts.append(j)
    head = A.box("header", (2.24, 0.3, 0.16), material="door_frame", round_=0.02)
    at(head, (0, 0, 2.38))
    parts.append(head)
    return A.join("door_frame", parts)


def step_light():
    housing = A.box("housing", (0.14, 0.05, 0.06), material="metal_dark", round_=0.008)
    strip = A.box("strip", (0.11, 0.01, 0.025), material="step_glow")
    at(strip, (0, 0.026, 0))
    return A.join("step_light", [housing, strip])


def sconce():
    """An art deco wall light: a fan of glass over a gold backplate."""
    plate = A.box("plate", (0.16, 0.04, 0.5), material="deco_gold", round_=0.015)
    at(plate, (0, -0.02, 0))
    fan = A.lathe("fan", [(0.0, -0.05), (0.2, 0.0), (0.22, 0.02), (0.0, 0.03)], 32, (0, 0, 0), "sconce_glass")
    for v in fan.data.vertices:  # half a disc, standing off the wall
        v.co.y = abs(v.co.y)
    at(fan, (0, 0, 0), (90, 0, 0))
    at(fan, (0, 0.0, 0.1))
    return A.join("sconce", [plate, fan])


# ======================================================================================== build

mh = [seat(), seat_end(), curtain("curtain"), valance(), proscenium(), proscenium_neon(), booth(),
      door_leaf("door_left", True), door_leaf("door_right", False), door_frame(), step_light(), sconce()]
for ob in mh:
    keep(ob)

if '--no-rage' not in sys.argv:
    try:
        import build_rage
        build_rage.build(models, keep)
    except ImportError:
        pass

# (the rage room's wooden and cardboard things are long planks and flaps held at their ends)
A.densify([ob for ob, g, o in models if ob.name in ("chair", "crate", "box", "table")])
A.bake_ao([ob for ob, g, o in models if not ob.name.endswith('neon') and 'glow' not in ob.name])
A.export(OUT, models)

if PREVIEW:
    os.makedirs(PREVIEW, exist_ok=True)
    def show(name, view, target, lens=40):
        ob = bpy.data.objects.get(name)
        if ob:
            A.preview(os.path.join(PREVIEW, name + ".png"), [ob], view, target, lens)
    show("seat", (1.2, 1.6, 1.3), (0, 0, 0.5))
    show("curtain", (3.0, 4.0, 3.6), (0, 0, 3.4), 30)
    show("valance", (3.0, 9.0, 0.5), (0, 0, -0.5), 30)
    show("proscenium", (5.0, 16.0, 5.0), (0, 0, 4.4), 30)
    show("booth", (1.5, 2.4, 0.6), (0, 0, 0), 35)
    show("door_left", (0.8, 2.4, 1.4), (0.47, 0, 1.1), 35)
    show("sconce", (0.5, 0.8, 0.3), (0, 0, 0.0), 35)
    for n, v, t in (("crate", (1.2, 1.4, 1.0), (0, 0, 0.3)), ("tv", (0.9, 1.1, 0.7), (0, 0, 0.3)), ("bottle_green", (0.3, 0.45, 0.3), (0, 0, 0.12)),
                    ("vase", (0.6, 0.8, 0.5), (0, 0, 0.2)), ("chair", (1.2, 1.5, 1.1), (0, 0, 0.45)), ("table", (1.4, 1.8, 1.2), (0, 0, 0.4)),
                    ("box", (0.8, 1.0, 0.7), (0, 0, 0.16)), ("gnome", (0.5, 0.75, 0.45), (0, 0, 0.25))):
        show(n, v, t)
    # the pieces of a few, spread out
    for n in ("tv", "gnome", "crate"):
        ps = [o for o in bpy.data.objects if o.name.startswith(n + ".piece")]
        for i, p in enumerate(ps):
            p.location = Vector((0, 0, 0))
        if ps:
            A.preview(os.path.join(PREVIEW, n + "_pieces.png"), ps, (1.0, 1.4, 1.0), (0, 0, 0), 35)
