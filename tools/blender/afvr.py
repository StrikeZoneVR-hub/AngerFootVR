"""
Anger Foot VR's Blender toolkit (run inside Blender: blender -b --factory-startup --python tools/blender/build_props.py).

Shapes are made in code, baked with soft ambient occlusion into their vertex colours (the mod draws them with Unity's
Particles/Standard Surface, which multiplies by vertex colour), and written to one .afvp file the mod loads at runtime:

    "AFVP" u32 version=1, u32 count, then per model:
      str name, str group ("" or the breakable it is a piece of), f32x3 offset (Unity space, from the group's origin)
      u32 vertices: f32x3 position, f32x3 normal, f32x2 uv, u8x4 colour (each)
      u32 submeshes: str material, u32 indices, u32 index[...]
    str = u16 length + UTF-8

Unity is y-up and left-handed: Blender (x, y, z) becomes (x, z, y), and the triangles turn round. Models face +y in
Blender (+z in Unity, the way the mod's spaces face).
"""
import bpy, bmesh, math, random, struct
from mathutils import Vector, Matrix

# ---------------------------------------------------------------- scene

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def mat(name):
    """A material slot by name (the mod gives each name its look)."""
    m = bpy.data.materials.get(name)
    return m if m else bpy.data.materials.new(name)

def obj_from_bmesh(name, bm, material=None):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    if material:
        me.materials.append(mat(material))
    return ob

def set_material(ob, name):
    ob.data.materials.clear()
    ob.data.materials.append(mat(name))

def add_material(ob, name):
    """Adds a slot (or finds it) and returns its index."""
    for i, m in enumerate(ob.data.materials):
        if m and m.name == name:
            return i
    ob.data.materials.append(mat(name))
    return len(ob.data.materials) - 1

def apply_modifiers(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
    old = ob.data
    ob.modifiers.clear()
    ob.data = me
    bpy.data.meshes.remove(old)
    return ob

def bevel(ob, width, segments=3, angle=40):
    m = ob.modifiers.new("bevel", 'BEVEL')
    m.width = width
    m.segments = segments
    m.limit_method = 'ANGLE'
    m.angle_limit = math.radians(angle)
    m.harden_normals = False
    return apply_modifiers(ob)

def subdivide(ob, levels=1):
    m = ob.modifiers.new("subsurf", 'SUBSURF')
    m.levels = levels
    m.render_levels = levels
    return apply_modifiers(ob)

def solidify(ob, thickness, offset=0.0):
    m = ob.modifiers.new("solid", 'SOLIDIFY')
    m.thickness = thickness
    m.offset = offset
    return apply_modifiers(ob)

def transform(ob, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1)):
    mtx = Matrix.Translation(Vector(loc)) @ Matrix.Rotation(rot[2], 4, 'Z') @ Matrix.Rotation(rot[1], 4, 'Y') @ Matrix.Rotation(rot[0], 4, 'X') @ Matrix.Diagonal(Vector((*scale, 1)))
    ob.data.transform(mtx)
    ob.data.update()
    return ob

def join(name, obs):
    """Joins objects (their materials kept) into one, named."""
    obs = [o for o in obs if o]
    bpy.ops.object.select_all(action='DESELECT')
    for o in obs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    if len(obs) > 1:
        bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = name
    ob.data.name = name
    return ob

def smooth(ob, angle=35):
    for p in ob.data.polygons:
        p.use_smooth = True
    ob.data.set_sharp_from_angle(angle=math.radians(angle))
    return ob

# ---------------------------------------------------------------- shapes

def box(name, size, loc=(0, 0, 0), material=None, round_=0.0, segments=3):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2])) + Vector(loc)
    ob = obj_from_bmesh(name, bm, material)
    if round_ > 0:
        bevel(ob, round_, segments, angle=60)
    return smooth(ob)

def cylinder(name, radius, depth, loc=(0, 0, 0), material=None, segments=24, axis='Z', cap=True, round_=0.0):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=cap, cap_tris=False, segments=segments, radius1=radius, radius2=radius, depth=depth)
    rot = {'Z': Matrix.Identity(3), 'X': Matrix.Rotation(math.pi / 2, 3, 'Y'), 'Y': Matrix.Rotation(math.pi / 2, 3, 'X')}[axis]
    for v in bm.verts:
        v.co = rot @ v.co + Vector(loc)
    ob = obj_from_bmesh(name, bm, material)
    if round_ > 0:
        bevel(ob, round_, 2, angle=60)
    return smooth(ob, 50)

def lathe(name, profile, segments=32, loc=(0, 0, 0), material=None):
    """A solid of revolution round z from (radius, height) points, bottom to top; closed at both ends."""
    bm = bmesh.new()
    rings = []
    for (r, z) in profile:
        ring = []
        for i in range(segments):
            a = 2 * math.pi * i / segments
            ring.append(bm.verts.new(Vector((math.cos(a) * r, math.sin(a) * r, z)) + Vector(loc)))
        rings.append(ring)
    for k in range(len(rings) - 1):
        for i in range(segments):
            j = (i + 1) % segments
            bm.faces.new((rings[k][i], rings[k][j], rings[k + 1][j], rings[k + 1][i]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = obj_from_bmesh(name, bm, material)
    return smooth(ob, 50)

def tube_along(name, points, radius, material=None, sides=10, closed=False):
    """A round tube along a polyline (neon tubes, rails)."""
    bm = bmesh.new()
    rings = []
    n = len(points)
    for i, p in enumerate(points):
        p = Vector(p)
        a = Vector(points[max(i - 1, 0)]) if not closed else Vector(points[(i - 1) % n])
        b = Vector(points[min(i + 1, n - 1)]) if not closed else Vector(points[(i + 1) % n])
        t = (b - a).normalized()
        up = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
        x = t.cross(up).normalized()
        y = x.cross(t).normalized()
        ring = [bm.verts.new(p + (x * math.cos(2 * math.pi * k / sides) + y * math.sin(2 * math.pi * k / sides)) * radius) for k in range(sides)]
        rings.append(ring)
    count = n if closed else n - 1
    for i in range(count):
        r0, r1 = rings[i], rings[(i + 1) % n]
        for k in range(sides):
            bm.faces.new((r0[k], r0[(k + 1) % sides], r1[(k + 1) % sides], r1[k]))
    if not closed:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return smooth(obj_from_bmesh(name, bm, material), 60)

# ---------------------------------------------------------------- breaking

def fracture(ob, pieces, seed, inner_material, shrink=0.85):
    """Voronoi cells: the shape cut between random seed points inside it, each cut capped with the inner material.
    Returns the pieces (each its own object, origin at its middle). The shape should be closed and solid."""
    rnd = random.Random(seed)
    me = ob.data
    lo = Vector((min(v.co.x for v in me.vertices), min(v.co.y for v in me.vertices), min(v.co.z for v in me.vertices)))
    hi = Vector((max(v.co.x for v in me.vertices), max(v.co.y for v in me.vertices), max(v.co.z for v in me.vertices)))
    mid = (lo + hi) / 2
    half = (hi - lo) / 2 * shrink
    seeds = [mid + Vector((rnd.uniform(-1, 1) * half.x, rnd.uniform(-1, 1) * half.y, rnd.uniform(-1, 1) * half.z)) for _ in range(pieces)]
    inner = add_material(ob, inner_material)
    out = []
    for i, s in enumerate(seeds):
        bm = bmesh.new()
        bm.from_mesh(me)
        ok = True
        for j, t in enumerate(seeds):
            if i == j:
                continue
            n = (t - s).normalized()
            geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
            res = bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-5, plane_co=(s + t) / 2, plane_no=n, clear_outer=True)
            cut = [e for e in res['geom_cut'] if isinstance(e, bmesh.types.BMEdge)]
            if cut:
                filled = bmesh.ops.holes_fill(bm, edges=cut, sides=0)
                for f in filled.get('faces', []):
                    f.material_index = inner
                    f.smooth = False
            if len(bm.verts) < 4:
                ok = False
                break
        if not ok or len(bm.faces) < 4:
            bm.free()
            continue
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        piece_me = bpy.data.meshes.new(f"{ob.name}.piece{len(out):02d}")
        bm.to_mesh(piece_me)
        bm.free()
        for m in me.materials:
            piece_me.materials.append(m)
        p = bpy.data.objects.new(piece_me.name, piece_me)
        bpy.context.scene.collection.objects.link(p)
        out.append(p)
    return out

def pieces_from_parts(ob_parts):
    """A breakable made of parts (planks, legs): each part is a piece."""
    return list(ob_parts)

# ---------------------------------------------------------------- baking

def densify(obs, max_len=0.1):
    """Long edges cut until none is longer than max_len: the occlusion is kept at the vertices, and a slat or plank
    whose only vertices are its ends (buried in the posts that hold it) would go dark all along."""
    for ob in obs:
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        for _ in range(6):
            long_edges = [e for e in bm.edges if e.calc_length() > max_len]
            if not long_edges:
                break
            bmesh.ops.subdivide_edges(bm, edges=long_edges, cuts=1, use_grid_fill=True)
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
        bm.to_mesh(ob.data)
        bm.free()
        ob.data.update()

def bake_ao(obs, samples=128, distance=0.25):
    """Soft ambient occlusion into each model's vertex colours (white where open, darker in creases)."""
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = samples
    if scene.world is None:
        scene.world = bpy.data.worlds.new("World")
    scene.world.light_settings.distance = distance
    for ob in obs:
        if not ob.data.color_attributes.get("AO"):
            ob.data.color_attributes.new(name="AO", type='BYTE_COLOR', domain='CORNER')
        ob.data.color_attributes.active_color = ob.data.color_attributes["AO"]
        # (a material per slot that uses the attribute is needed for the baker to have something to write into)
        # each model on its own (they're all made at the origin: together they'd shade each other black)
        for o in bpy.context.scene.collection.objects:
            o.hide_render = o != ob
        bpy.ops.object.select_all(action='DESELECT')
        ob.select_set(True)
        bpy.context.view_layer.objects.active = ob
        try:
            bpy.ops.object.bake(type='AO', target='VERTEX_COLORS')
        except Exception as e:
            print("AO bake failed for", ob.name, e)
    for o in bpy.context.scene.collection.objects:
        o.hide_render = False

# ---------------------------------------------------------------- export

def _str(f, s):
    b = s.encode('utf-8')
    f.write(struct.pack('<H', len(b)))
    f.write(b)

def _mesh_data(ob):
    me = ob.data
    me.calc_loop_triangles()
    normals = me.corner_normals
    uv = me.uv_layers.active.data if me.uv_layers.active else None
    col = me.color_attributes.get("AO")
    verts, index = [], {}
    sub = {}
    for tri in me.loop_triangles:
        idx = []
        for li in tri.loops:
            loop = me.loops[li]
            co = me.vertices[loop.vertex_index].co
            n = normals[li].vector
            u = tuple(uv[li].uv) if uv else (0.0, 0.0)
            c = tuple(col.data[li].color) if col and col.domain == 'CORNER' else (1.0, 1.0, 1.0, 1.0)
            # (the occlusion softened: creases darker, never black)
            # (and thin parts, slats and flaps, that the rays see both sides of, kept from going black)
            c = tuple(1.0 - (1.0 - max(x, 0.35)) * 0.7 for x in c[:3]) + (1.0,)
            # Blender (x, y, z) -> Unity (x, z, y)
            key = (round(co.x, 5), round(co.z, 5), round(co.y, 5), round(n.x, 3), round(n.z, 3), round(n.y, 3), round(u[0], 4), round(u[1], 4), round(c[0], 3))
            i = index.get(key)
            if i is None:
                i = len(verts)
                index[key] = i
                verts.append(((co.x, co.z, co.y), (n.x, n.z, n.y), u, tuple(int(max(0, min(1, x)) * 255) for x in c)))
            idx.append(i)
        name = me.materials[tri.material_index].name if me.materials and tri.material_index < len(me.materials) and me.materials[tri.material_index] else "default"
        sub.setdefault(name, []).extend((idx[0], idx[2], idx[1]))  # turned round for the left-handed y-up space
    return verts, sub

def export(path, models):
    """models: list of (object, group name, offset in Blender space)."""
    with open(path, 'wb') as f:
        f.write(b'AFVP')
        f.write(struct.pack('<II', 1, len(models)))
        for ob, group, off in models:
            verts, sub = _mesh_data(ob)
            _str(f, ob.name)
            _str(f, group)
            f.write(struct.pack('<3f', off[0], off[2], off[1]))
            f.write(struct.pack('<I', len(verts)))
            for (p, n, u, c) in verts:
                f.write(struct.pack('<3f3f2f4B', *p, *n, *u, *c))
            f.write(struct.pack('<I', len(sub)))
            for name, idx in sub.items():
                _str(f, name)
                f.write(struct.pack('<I', len(idx)))
                f.write(struct.pack(f'<{len(idx)}I', *idx))
    print("AFVP written:", path, len(models), "models")

def center_origin(ob):
    """Moves the mesh so its middle is at the origin; returns that middle (Blender space)."""
    me = ob.data
    c = sum((v.co for v in me.vertices), Vector()) / max(1, len(me.vertices))
    me.transform(Matrix.Translation(-c))
    me.update()
    return c

# ---------------------------------------------------------------- previews

def preview(path, obs, view=(1.5, -2.2, 1.4), target=(0, 0, 0.5), lens=40):
    """A quick render (Workbench, matcap-ish) of some objects, for checking shapes."""
    scene = bpy.context.scene
    for o in scene.collection.objects:
        o.hide_render = o not in obs
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'VERTEX' if all(o.data.color_attributes.get("AO") for o in obs) else 'MATERIAL'
    scene.render.resolution_x = 800
    scene.render.resolution_y = 600
    cam_data = bpy.data.cameras.new("cam")
    cam_data.lens = lens
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    cam.location = Vector(view)
    d = Vector(target) - cam.location
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    scene.camera = cam
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)
    for o in scene.collection.objects:
        o.hide_render = False
