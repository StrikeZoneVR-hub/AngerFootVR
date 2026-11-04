"""
Anger Foot VR's wound and gore art (1.2): run inside Blender,
    blender -b --factory-startup --python tools/blender/build_gore.py -- <textures dir> <gore.afvp> <preview dir>

Textures (seamless: every pattern is 4D noise on a torus, so it tiles both ways), baked from procedural shaders:
- gore_flesh.png  the inside of a wound: wet muscle (fibres, creases, vessels, a few fat globules), 512x512;
- gore_bone.png   bone (rib, skull): off-white, porous, stained, 256x256;
- gore_noise.png  the wound shaders' noise: R fine, G medium, B cells (Voronoi), A cell edges (the torn rims), 256x256;
- gore_skin.png   the torn skin edge and bruise: R the tear's ragged profile, G bruise mottling, B abrasion grain, A drip
                  lumps, 256x256.
Models (gore.afvp, the props format): flesh chunks, bone shards, a rib piece, skin flaps, flying out of big wounds.
Previews: a contact sheet of the textures and a render of a sample wound (a skin-covered arm with a bullet hole cut
through it, its layers, its inside), for checking the look.
"""
import bpy, bmesh, math, os, random, sys
import numpy as np
from mathutils import Vector, Matrix, noise

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import afvr

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
TEX = args[0] if len(args) > 0 else os.path.abspath("analysis/gore-tex")
AFVP = args[1] if len(args) > 1 else os.path.abspath("analysis/gore.afvp")
PREV = args[2] if len(args) > 2 else os.path.abspath("analysis/gore-preview")
os.makedirs(TEX, exist_ok=True)
os.makedirs(PREV, exist_ok=True)

afvr.reset()
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 1
scene.cycles.use_denoising = False

# ---------------------------------------------------------------- seamless procedural textures, baked


def torus_coords(nt, scale_u, scale_v):
    """A node giving (vector, w): the UV square wrapped onto a 4D torus (seamless noise both ways)."""
    tc = nt.nodes.new('ShaderNodeTexCoord')
    sep = nt.nodes.new('ShaderNodeSeparateXYZ')
    nt.links.new(tc.outputs['UV'], sep.inputs[0])

    def trig(inp, fn, r):
        m = nt.nodes.new('ShaderNodeMath')
        m.operation = 'MULTIPLY'
        m.inputs[1].default_value = 2 * math.pi
        nt.links.new(inp, m.inputs[0])
        t = nt.nodes.new('ShaderNodeMath')
        t.operation = fn
        nt.links.new(m.outputs[0], t.inputs[0])
        s = nt.nodes.new('ShaderNodeMath')
        s.operation = 'MULTIPLY'
        s.inputs[1].default_value = r
        nt.links.new(t.outputs[0], s.inputs[0])
        return s.outputs[0]

    cx = trig(sep.outputs['X'], 'COSINE', scale_u)
    sx = trig(sep.outputs['X'], 'SINE', scale_u)
    cy = trig(sep.outputs['Y'], 'COSINE', scale_v)
    sy = trig(sep.outputs['Y'], 'SINE', scale_v)
    comb = nt.nodes.new('ShaderNodeCombineXYZ')
    nt.links.new(cx, comb.inputs[0])
    nt.links.new(sx, comb.inputs[1])
    nt.links.new(cy, comb.inputs[2])
    return comb.outputs[0], sy


def noise4(nt, su, sv, detail=4.0, rough=0.55, distortion=0.0, offset=0.0):
    v, w = torus_coords(nt, su, sv)
    n = nt.nodes.new('ShaderNodeTexNoise')
    n.noise_dimensions = '4D'
    n.inputs['Scale'].default_value = 1.0
    n.inputs['Detail'].default_value = detail
    n.inputs['Roughness'].default_value = rough
    n.inputs['Distortion'].default_value = distortion
    if offset:
        a = nt.nodes.new('ShaderNodeMath')
        a.operation = 'ADD'
        a.inputs[1].default_value = offset
        nt.links.new(w, a.inputs[0])
        w = a.outputs[0]
    nt.links.new(v, n.inputs['Vector'])
    nt.links.new(w, n.inputs['W'])
    return n.outputs['Fac']


def voronoi4(nt, su, sv, feature='F1', offset=0.0):
    v, w = torus_coords(nt, su, sv)
    n = nt.nodes.new('ShaderNodeTexVoronoi')
    n.voronoi_dimensions = '4D'
    n.feature = feature
    n.inputs['Scale'].default_value = 1.0
    if offset:
        a = nt.nodes.new('ShaderNodeMath')
        a.operation = 'ADD'
        a.inputs[1].default_value = offset
        nt.links.new(w, a.inputs[0])
        w = a.outputs[0]
    nt.links.new(v, n.inputs['Vector'])
    nt.links.new(w, n.inputs['W'])
    return n.outputs['Distance']


def ramp(nt, fac, stops):
    r = nt.nodes.new('ShaderNodeValToRGB')
    els = r.color_ramp.elements
    while len(els) > 1:
        els.remove(els[-1])
    els[0].position = stops[0][0]
    els[0].color = stops[0][1]
    for pos, col in stops[1:]:
        e = els.new(pos)
        e.color = col
    nt.links.new(fac, r.inputs['Fac'])
    return r.outputs['Color']


def mix(nt, a, b, fac, blend='MIX'):
    m = nt.nodes.new('ShaderNodeMix')
    m.data_type = 'RGBA'
    m.blend_type = blend
    fac_in = next(i for i in m.inputs if i.name == 'Factor' and i.type == 'VALUE')
    a_in = next(i for i in m.inputs if i.name == 'A' and i.type == 'RGBA')
    b_in = next(i for i in m.inputs if i.name == 'B' and i.type == 'RGBA')
    out = next(o for o in m.outputs if o.type == 'RGBA')
    if isinstance(fac, float):
        fac_in.default_value = fac
    else:
        nt.links.new(fac, fac_in)
    nt.links.new(a, a_in)
    nt.links.new(b, b_in)
    return out


def math_node(nt, op, a, b=None, clamp=False):
    m = nt.nodes.new('ShaderNodeMath')
    m.operation = op
    m.use_clamp = clamp
    if isinstance(a, float):
        m.inputs[0].default_value = a
    else:
        nt.links.new(a, m.inputs[0])
    if b is not None:
        if isinstance(b, float):
            m.inputs[1].default_value = b
        else:
            nt.links.new(b, m.inputs[1])
    return m.outputs[0]


PLANE = None


def bake(name, size, build, path, srgb=True):
    """Bakes a procedural colour (build(nt) -> colour socket) into a PNG."""
    global PLANE
    if PLANE is None:
        bpy.ops.mesh.primitive_plane_add(size=1)
        PLANE = bpy.context.active_object
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    em = nt.nodes.new('ShaderNodeEmission')
    nt.links.new(em.outputs[0], out.inputs['Surface'])
    nt.links.new(build(nt), em.inputs['Color'])
    img = bpy.data.images.new(name, size, size, alpha=False, float_buffer=True)
    img.colorspace_settings.name = 'Linear Rec.709' if not srgb else 'sRGB'
    tex = nt.nodes.new('ShaderNodeTexImage')
    tex.image = img
    nt.nodes.active = tex
    PLANE.data.materials.clear()
    PLANE.data.materials.append(m)
    bpy.ops.object.select_all(action='DESELECT')
    PLANE.select_set(True)
    bpy.context.view_layer.objects.active = PLANE
    bpy.ops.object.bake(type='EMIT', margin=0)
    px = np.array(img.pixels[:], dtype=np.float32).reshape(size, size, 4)
    return px


def to_srgb(x):
    x = np.clip(x, 0, 1)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1 / 2.4) - 0.055)


def save_png(px, path, alpha=None, colour=False):
    size = px.shape[0]
    if colour:
        px = px.copy()
        px[:, :, :3] = to_srgb(px[:, :, :3])
    out = bpy.data.images.new(os.path.basename(path), size, size, alpha=True)
    data = px.copy()
    if alpha is not None:
        data[:, :, 3] = alpha
    else:
        data[:, :, 3] = 1.0
    out.pixels[:] = np.clip(data, 0, 1).ravel()
    out.filepath_raw = path
    out.file_format = 'PNG'
    out.save()
    print("TEXTURE", path)


def srgb(c):
    """sRGB 0-255 -> linear 0-1 (the emission bake writes linear: these are what Blender stores as the colour)."""
    def f(x):
        x = x / 255.0
        return x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4
    return (f(c[0]), f(c[1]), f(c[2]), 1.0)


# the inside of a wound: wet muscle
def flesh(nt):
    fibres = noise4(nt, 0.7, 14.0, detail=7.0, rough=0.62, distortion=0.25)  # long fibres across v
    fibres2 = noise4(nt, 1.6, 16.0, detail=3.0, rough=0.5, offset=3.1)
    creases = voronoi4(nt, 1.4, 1.4, 'DISTANCE_TO_EDGE')
    blobs = noise4(nt, 2.5, 2.5, detail=2.0, rough=0.5, offset=7.3)
    fat = voronoi4(nt, 2.6, 2.6, 'F1', offset=11.0)
    vessels = noise4(nt, 1.2, 1.2, detail=1.0, rough=0.3, distortion=2.2, offset=19.0)
    muscle = ramp(nt, math_node(nt, 'ADD', math_node(nt, 'MULTIPLY', fibres, 0.8), math_node(nt, 'MULTIPLY', fibres2, 0.3)), [
        (0.3, srgb((60, 6, 12))), (0.42, srgb((112, 16, 24))), (0.52, srgb((158, 32, 38))), (0.62, srgb((190, 58, 60))), (0.75, srgb((214, 92, 90))), (0.88, srgb((232, 130, 124)))])
    # the creases between bundles darker, the wet bulges lighter
    crease = ramp(nt, creases, [(0.0, (0.42, 0.42, 0.42, 1)), (0.035, (0.75, 0.75, 0.75, 1)), (0.1, (1, 1, 1, 1))])
    c = mix(nt, muscle, crease, 1.0, 'MULTIPLY')
    bulge = ramp(nt, blobs, [(0.35, (0.8, 0.8, 0.8, 1)), (0.7, (1.15, 1.1, 1.1, 1))])
    c = mix(nt, c, bulge, 1.0, 'MULTIPLY')
    # fat globules: yellow-white specks here and there
    fatmask = math_node(nt, 'LESS_THAN', fat, 0.13)
    fatcol = ramp(nt, blobs, [(0.3, srgb((222, 190, 120))), (0.7, srgb((245, 222, 160)))])
    c = mix(nt, c, fatcol, math_node(nt, 'MULTIPLY', fatmask, 0.85))
    # dark vessels winding through
    vmask = ramp(nt, vessels, [(0.47, (0, 0, 0, 1)), (0.495, (1, 1, 1, 1)), (0.505, (1, 1, 1, 1)), (0.53, (0, 0, 0, 1))])
    sep = nt.nodes.new('ShaderNodeSeparateColor')
    nt.links.new(vmask, sep.inputs[0])
    c = mix(nt, c, ramp(nt, blobs, [(0.0, srgb((60, 4, 14))), (1.0, srgb((90, 10, 30)))]), math_node(nt, 'MULTIPLY', sep.outputs[0], 0.7))
    return c


def bone(nt):
    pores = voronoi4(nt, 14.0, 14.0, 'F1')
    grain = noise4(nt, 6.0, 6.0, detail=6.0, rough=0.6)
    stain = noise4(nt, 1.5, 1.5, detail=3.0, rough=0.5, offset=5.0)
    base = ramp(nt, grain, [(0.3, srgb((198, 184, 150))), (0.55, srgb((228, 218, 192))), (0.8, srgb((244, 238, 220)))])
    pore = ramp(nt, pores, [(0.0, (0.55, 0.5, 0.45, 1)), (0.12, (1, 1, 1, 1))])
    c = mix(nt, base, pore, 1.0, 'MULTIPLY')
    blood = ramp(nt, stain, [(0.55, (1, 1, 1, 1)), (0.72, srgb((222, 170, 150)))])
    return mix(nt, c, blood, 1.0, 'MULTIPLY')


def grey(fn):
    def build(nt):
        v = fn(nt)
        comb = nt.nodes.new('ShaderNodeCombineColor')
        for i in range(3):
            nt.links.new(v, comb.inputs[i])
        return comb.outputs[0]
    return build


print("BAKING textures into", TEX)
fl = bake("gore_flesh", 512, flesh, None)
save_png(fl, os.path.join(TEX, "gore_flesh.png"), colour=True)
bo = bake("gore_bone", 256, bone, None)
save_png(bo, os.path.join(TEX, "gore_bone.png"), colour=True)
# the wound shaders' noise (linear data)
r = bake("n_r", 256, grey(lambda nt: noise4(nt, 8.0, 8.0, detail=4.0, rough=0.5)), None, srgb=False)[:, :, 0]
g = bake("n_g", 256, grey(lambda nt: noise4(nt, 3.0, 3.0, detail=3.0, rough=0.5, offset=4.0)), None, srgb=False)[:, :, 0]
b = bake("n_b", 256, grey(lambda nt: math_node(nt, 'MULTIPLY', voronoi4(nt, 6.0, 6.0, 'F1', offset=9.0), 1.6)), None, srgb=False)[:, :, 0]
a = bake("n_a", 256, grey(lambda nt: math_node(nt, 'MULTIPLY', voronoi4(nt, 10.0, 10.0, 'DISTANCE_TO_EDGE', offset=13.0), 3.0)), None, srgb=False)[:, :, 0]
save_png(np.stack([r, g, b, a], axis=2), os.path.join(TEX, "gore_noise.png"), alpha=a)
# the skin's tear and bruise (linear data)
tr = bake("s_r", 256, grey(lambda nt: noise4(nt, 2.0, 0.5, detail=8.0, rough=0.7, distortion=0.6, offset=21.0)), None, srgb=False)[:, :, 0]
br = bake("s_g", 256, grey(lambda nt: noise4(nt, 4.0, 4.0, detail=5.0, rough=0.65, offset=25.0)), None, srgb=False)[:, :, 0]
ab = bake("s_b", 256, grey(lambda nt: noise4(nt, 24.0, 24.0, detail=2.0, rough=0.5, offset=29.0)), None, srgb=False)[:, :, 0]
dl = bake("s_a", 256, grey(lambda nt: noise4(nt, 1.0, 6.0, detail=2.0, rough=0.4, offset=33.0)), None, srgb=False)[:, :, 0]
save_png(np.stack([tr, br, ab, dl], axis=2), os.path.join(TEX, "gore_skin.png"), alpha=dl)

# ---------------------------------------------------------------- gore models: chunks, shards, a rib, flaps

random.seed(12)
models = []


def chunk(name, radius, seed, flat=0.7, lumps=0.45):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=4, radius=radius)
    off = Vector((seed * 1.7, seed * 0.3, seed * 2.9))
    for v in bm.verts:
        d = v.co.normalized()
        # (soft lumps, a few folds; one side flatter, where it tore off)
        n = noise.noise(d * 1.3 + off) * lumps * 0.75 + noise.noise(d * 3.2 + off * 2) * 0.1 + noise.noise(d * 7.0 + off * 3) * 0.025
        torn = max(0.0, d.x) ** 2 * 0.3
        v.co = d * radius * (1.0 + n - torn)
        v.co.z *= flat
    ob = afvr.obj_from_bmesh(name, bm, "gore_flesh")
    for f in ob.data.polygons:
        f.use_smooth = True
    return ob


def shard(name, length, seed):
    bm = bmesh.new()
    rnd = random.Random(seed)
    sides = 6
    rings = 5
    verts = []
    for i in range(rings):
        t = i / (rings - 1)
        r = length * 0.11 * (1.0 - 0.75 * t) * (0.8 + rnd.random() * 0.4)
        ring = []
        for k in range(sides):
            a = k / sides * math.tau + rnd.random() * 0.4
            ring.append(bm.verts.new((math.cos(a) * r * (1.2 if k % 2 else 0.8), math.sin(a) * r * 0.7, t * length + (rnd.random() - 0.5) * 0.004)))
        verts.append(ring)
    for i in range(rings - 1):
        for k in range(sides):
            bm.faces.new((verts[i][k], verts[i][(k + 1) % sides], verts[i + 1][(k + 1) % sides], verts[i + 1][k]))
    bm.faces.new(list(reversed(verts[0])))
    tip = bm.verts.new((rnd.random() * 0.004, 0, length * 1.08))
    for k in range(sides):
        bm.faces.new((verts[-1][k], verts[-1][(k + 1) % sides], tip))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = afvr.obj_from_bmesh(name, bm, "gore_bone")
    afvr.smooth(ob, 50)
    return ob


def rib(name):
    pts = [Vector((math.cos(a) * 0.09, math.sin(a) * 0.05, 0)) for a in np.linspace(-0.2, 1.4, 9)]
    ob = afvr.tube_along(name, pts, 0.0065, material="gore_bone", sides=8)
    return ob


def flap(name, size, seed):
    bm = bmesh.new()
    rnd = random.Random(seed)
    n = 7
    grid = []
    for i in range(n):
        row = []
        for j in range(n):
            u = i / (n - 1) - 0.5
            v = j / (n - 1)
            edge = 1.0 if v < 0.85 else 0.6 + rnd.random() * 0.4
            x = u * size * (1.0 - 0.35 * v) * edge
            y = v * size
            z = -0.25 * size * (v ** 2) - 0.08 * size * (u * 2) ** 2
            row.append(bm.verts.new((x, y, z)))
        grid.append(row)
    for i in range(n - 1):
        for j in range(n - 1):
            bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
    ob = afvr.obj_from_bmesh(name, bm, "gore_skin")
    afvr.solidify(ob, 0.003)
    afvr.smooth(ob, 50)
    # the inner side raw: its own material slot
    me = ob.data
    raw = afvr.add_material(ob, "gore_flesh")
    for p in me.polygons:
        if p.normal.z < -0.2:
            p.material_index = raw
    return ob


for i, (r, f) in enumerate([(0.022, 0.75), (0.016, 0.6), (0.03, 0.8), (0.012, 0.7), (0.026, 0.55)]):
    models.append(chunk(f"gib_flesh_{i}", r, i + 1, flat=f))
for i, l in enumerate([0.05, 0.035, 0.065]):
    models.append(shard(f"gib_bone_{i}", l, 40 + i))
models.append(rib("gib_rib_0"))
for i, s in enumerate([0.035, 0.05]):
    models.append(flap(f"gib_flap_{i}", s, 70 + i))

for ob in models:
    afvr.center_origin(ob)
afvr.densify(models, 0.02)
afvr.bake_ao(models, samples=64, distance=0.03)
afvr.export(AFVP, [(ob, "", (0, 0, 0)) for ob in models])

# ---------------------------------------------------------------- previews: the textures on the models, a cut-open sample

def tex_material(name, png, kind):
    """The look-dev material: the baked texture box-mapped (the models have no UVs: the game's shader maps it so too)."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes.get('Principled BSDF')
    tc = nt.nodes.new('ShaderNodeTexCoord')
    mp = nt.nodes.new('ShaderNodeMapping')
    mp.inputs['Scale'].default_value = (14, 14, 14)
    nt.links.new(tc.outputs['Object'], mp.inputs['Vector'])
    t = nt.nodes.new('ShaderNodeTexImage')
    t.image = bpy.data.images.load(png)
    t.projection = 'BOX'
    t.projection_blend = 0.35
    nt.links.new(mp.outputs['Vector'], t.inputs['Vector'])
    nt.links.new(t.outputs['Color'], bsdf.inputs['Base Color'])
    if kind == 'flesh':
        bsdf.inputs['Roughness'].default_value = 0.18
        bsdf.inputs['Specular IOR Level'].default_value = 0.8
        bsdf.inputs['Subsurface Weight'].default_value = 0.35
        bsdf.inputs['Subsurface Radius'].default_value = (0.5, 0.1, 0.06)
        bsdf.inputs['Subsurface Scale'].default_value = 0.01
        bsdf.inputs['Coat Weight'].default_value = 0.35
        bsdf.inputs['Coat Roughness'].default_value = 0.05
    elif kind == 'bone':
        bsdf.inputs['Roughness'].default_value = 0.5
        bsdf.inputs['Subsurface Weight'].default_value = 0.1
    return m


def plain(name, rgb, rough, coat=0.0, sss=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get('Principled BSDF')
    b.inputs['Base Color'].default_value = srgb(rgb)
    b.inputs['Roughness'].default_value = rough
    b.inputs['Coat Weight'].default_value = coat
    b.inputs['Subsurface Weight'].default_value = sss
    b.inputs['Subsurface Radius'].default_value = (0.6, 0.2, 0.1)
    b.inputs['Subsurface Scale'].default_value = 0.01
    return m


fl_mat = tex_material("prev_flesh", os.path.join(TEX, "gore_flesh.png"), 'flesh')
bo_mat = tex_material("prev_bone", os.path.join(TEX, "gore_bone.png"), 'bone')
skin_mat = plain("prev_skin", (96, 160, 92), 0.45, sss=0.15)   # (the goons' green)
fat_mat = plain("prev_fat", (236, 204, 128), 0.3, coat=0.3, sss=0.3)
blood_mat = plain("prev_blood", (92, 3, 8), 0.05, coat=0.8)
floor_mat = plain("prev_floor", (28, 28, 30), 0.65)

PLANE.hide_render = True
for ob in models:
    ob.hide_render = True


def studio(target, cam_loc, lens):
    scene.world = bpy.data.worlds.new("w")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.006, 0.006, 0.007, 1)
    for o in [o for o in scene.collection.objects if o.type in ('LIGHT', 'CAMERA')]:
        bpy.data.objects.remove(o)
    for loc, energy, col, size in [((0.35, -0.45, 0.55), 5.0, (1, 0.96, 0.92), 0.3), ((-0.55, -0.25, 0.3), 1.4, (0.75, 0.82, 1.0), 0.5), ((0.05, 0.55, 0.3), 2.2, (1, 0.85, 0.75), 0.25)]:
        ld = bpy.data.lights.new("l", 'AREA')
        ld.energy = energy
        ld.color = col
        ld.size = size
        lo = bpy.data.objects.new("l", ld)
        lo.location = loc
        lo.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
        scene.collection.objects.link(lo)
    cd = bpy.data.cameras.new("cam")
    cd.lens = lens
    cd.dof.use_dof = True
    cd.dof.focus_distance = (Vector(target) - Vector(cam_loc)).length
    cd.dof.aperture_fstop = 11.0
    cam = bpy.data.objects.new("cam", cd)
    cam.location = cam_loc
    cam.rotation_euler = (Vector(target) - Vector(cam_loc)).to_track_quat('-Z', 'Y').to_euler()
    scene.collection.objects.link(cam)
    scene.camera = cam


bpy.ops.mesh.primitive_plane_add(size=3, location=(0, 0, 0))
floor = bpy.context.active_object
floor.data.materials.append(floor_mat)

# 1) the gore models, wet, on a dark floor
shown = []
x = -0.25
for ob in models:
    c = ob.copy()
    c.data = ob.data.copy()
    scene.collection.objects.link(c)
    c.hide_render = False
    dims = c.dimensions
    c.location = (x, 0.02 * math.sin(x * 40), max(dims.z, 0.01) * 0.45)
    if "bone" in ob.name:
        c.rotation_euler = (math.radians(70), math.radians(15), math.radians(20 + x * 90))
        c.location.z = 0.012
    x += max(dims.x, dims.y) * 0.75 + 0.012
    c.data.materials.clear()
    c.data.materials.append(bo_mat if ("bone" in ob.name or "rib" in ob.name) else fl_mat)
    if "flap" in ob.name:
        c.data.materials.clear()
        c.data.materials.append(skin_mat)
        c.data.materials.append(fl_mat)
    shown.append(c)
# a little blood under them
for i in range(5):
    bpy.ops.mesh.primitive_cylinder_add(vertices=40, radius=0.025 + 0.02 * (i % 3), depth=0.0008, location=(-0.2 + i * 0.1, 0.01, 0.0005))
    pl = bpy.context.active_object
    pl.scale = (1.5, 0.8, 1)
    pl.data.materials.append(blood_mat)
    shown.append(pl)
studio((0.0, 0, 0.02), (0.02, -0.5, 0.2), 60)
scene.cycles.samples = 128
scene.cycles.use_denoising = True
scene.render.resolution_x = 1600
scene.render.resolution_y = 900
scene.render.filepath = os.path.join(PREV, "gore_models.png")
scene.render.image_settings.file_format = 'PNG'
bpy.ops.render.render(write_still=True)
print("PREVIEW", scene.render.filepath)
for o in shown:
    o.hide_render = True

print("GORE DONE")
