"""Grix faction test assets: Rudar (worker bee), Prijevoznik (transport bee), Skladiste hive hub.
Blender convention: nose faces -Y, up is +Z. Exported FBX (forward -Z, up Y, baked) -> Unity forward +Z, up +Y.
Engine sockets: local +Z in Blender == Unity local +Y (green arrow) == exhaust direction.
"""
import bpy, bmesh, math, random, sys, os
from mathutils import Vector, Matrix, Euler

OUT = sys.argv[sys.argv.index('--') + 1] if '--' in sys.argv else '/tmp/grix'
MODE = sys.argv[sys.argv.index('--') + 2] if '--' in sys.argv and len(sys.argv) > sys.argv.index('--') + 2 else 'all'
TEX = os.path.join(os.path.dirname(OUT), 'tex')
random.seed(7)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

# ---------------------------------------------------------------- materials
def lin(h):
    h = h.lstrip('#'); c = [int(h[i:i+2], 16) / 255 for i in (0, 2, 4)]
    return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c) + (1,)

def mat(name, col, metal=0.0, rough=0.4, emit=None, estr=0.0, alpha=1.0, coat=0.0, tex=None):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = lin(col) if isinstance(col, str) else col
    b.inputs['Metallic'].default_value = metal
    b.inputs['Roughness'].default_value = rough
    b.inputs['Coat Weight'].default_value = coat
    if emit:
        b.inputs['Emission Color'].default_value = lin(emit); b.inputs['Emission Strength'].default_value = estr
    if alpha < 1:
        b.inputs['Alpha'].default_value = alpha
        try: m.surface_render_method = 'BLENDED'
        except Exception: pass
    if tex:
        t = m.node_tree.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(tex)
        m.node_tree.links.new(t.outputs['Color'], b.inputs['Base Color'])
    m.diffuse_color = lin(col) if isinstance(col, str) else col
    return m

M = {
    'chitin': mat('Grix_Chitin', '#120810', metal=0.35, rough=0.22, coat=0.8),
    'chitin2': mat('Grix_ChitinPlum', '#3a0f2f', metal=0.3, rough=0.3, coat=0.6),
    'red': mat('Grix_Red', '#C8102E', rough=0.35, coat=0.4),
    'white': mat('Grix_White', '#F2F0EE', rough=0.45),
    'blue': mat('Grix_Blue', '#171796', metal=0.2, rough=0.3, coat=0.4),
    'wing': mat('Grix_Wing', '#7d88e0', rough=0.05, alpha=0.26, emit='#2a3aa0', estr=0.25),
    'glow': mat('Grix_Glow', '#E17633', emit='#E17633', estr=2.2, rough=0.3),
    'eye': mat('Grix_Eye', '#0b0b30', metal=0.6, rough=0.08, emit='#2a2aa8', estr=1.2, coat=1.0),
    'hole': mat('Grix_Hole', '#030203', rough=0.9),
    'team': mat('TeamColour', '#CC8CC8', rough=0.35, coat=0.3),
    'flag': mat('FlagDecal', '#ffffff', rough=0.6, tex=os.path.join(TEX, 'Grix_Flag.png')),
}

# ---------------------------------------------------------------- helpers
def link(o, coll):
    coll.objects.link(o); return o

def new_coll(name):
    c = bpy.data.collections.new(name); scene.collection.children.link(c); return c

def from_bm(bm, name, material, coll, smooth=False):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    me.materials.append(material)
    for p in me.polygons: p.use_smooth = smooth
    return link(bpy.data.objects.new(name, me), coll)

def ellipsoid(name, center, scale, material, coll, seg=18, rings=12, rot=(0, 0, 0), smooth=True, axis_y=False):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=1.0)
    if axis_y:
        bmesh.ops.rotate(bm, verts=bm.verts, matrix=Matrix.Rotation(math.radians(90), 3, 'X'))
    bmesh.ops.scale(bm, vec=scale, verts=bm.verts)
    bmesh.ops.rotate(bm, verts=bm.verts, matrix=Euler([math.radians(a) for a in rot]).to_matrix())
    bmesh.ops.translate(bm, vec=center, verts=bm.verts)
    return from_bm(bm, name, material, coll, smooth)

def segment(name, a, b, r1, r2, material, coll, verts=10, smooth=True):
    a, b = Vector(a), Vector(b); d = b - a
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=verts, radius1=r1, radius2=r2, depth=d.length)
    q = d.to_track_quat('Z', 'Y')
    bmesh.ops.rotate(bm, verts=bm.verts, matrix=q.to_matrix())
    bmesh.ops.translate(bm, vec=(a + b) / 2, verts=bm.verts)
    return from_bm(bm, name, material, coll, smooth)

def limb(name, pts, radii, material, coll, joints=True, verts=8):
    objs = []
    for i in range(len(pts) - 1):
        objs.append(segment(f'{name}_{i}', pts[i], pts[i+1], radii[i], radii[i+1], material, coll, verts))
        if joints and 0 < i + 1 < len(pts) - 1:
            r = radii[i+1] * 1.15
            objs.append(ellipsoid(f'{name}_j{i}', pts[i+1], (r, r, r), material, coll, 10, 6))
    return objs

def hexprism(name, center, r, h, material, coll, z0=0.0, rot=0.0):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=r, radius2=r, depth=h)
    bmesh.ops.rotate(bm, verts=bm.verts, matrix=Matrix.Rotation(rot, 3, 'Z'))
    bmesh.ops.translate(bm, vec=(center[0], center[1], z0 + h / 2), verts=bm.verts)
    return from_bm(bm, name, material, coll, False)

def disc(name, center, sx, sy, material, coll, rot, verts=24, thick=0.012):
    bm = bmesh.new()
    bmesh.ops.create_circle(bm, cap_ends=True, segments=verts, radius=1.0)
    bmesh.ops.scale(bm, vec=(sx, sy, 1), verts=bm.verts)
    ext = bmesh.ops.extrude_face_region(bm, geom=bm.faces[:])
    bmesh.ops.translate(bm, vec=(0, 0, thick), verts=[v for v in ext['geom'] if isinstance(v, bmesh.types.BMVert)])
    bmesh.ops.rotate(bm, verts=bm.verts, matrix=Euler([math.radians(a) for a in rot]).to_matrix())
    bmesh.ops.translate(bm, vec=center, verts=bm.verts)
    return from_bm(bm, name, material, coll, True)

def quad(name, center, size, material, coll):
    bm = bmesh.new(); s = size / 2
    vs = [bm.verts.new((center[0] + x, center[1] + y, center[2])) for x, y in ((-s, -s), (s, -s), (s, s), (-s, s))]
    f = bm.faces.new(vs); uv = bm.loops.layers.uv.new('UVMap')
    for loop, (u, v) in zip(f.loops, ((0, 0), (1, 0), (1, 1), (0, 1))): loop[uv].uv = (u, v)
    return from_bm(bm, name, material, coll, False)

def join(objs, name):
    objs = [o for o in objs if o]
    with bpy.context.temp_override(active_object=objs[0], selected_editable_objects=objs, selected_objects=objs):
        bpy.ops.object.join()
    o = objs[0]; o.name = name; o.data.name = name
    return o

def empty(name, loc, coll, parent=None, rot=(0, 0, 0), size=0.25, kind='ARROWS'):
    e = bpy.data.objects.new(name, None); e.empty_display_type = kind; e.empty_display_size = size
    e.location = loc; e.rotation_euler = [math.radians(a) for a in rot]
    link(e, coll)
    if parent: e.parent = parent
    return e

def mirror_x(p): return (-p[0], p[1], p[2])


def engines(root, coll, pts):
    eng = empty('Engines', (0, 0, 0), coll, root, size=0.1, kind='PLAIN_AXES')
    for name, p in pts:
        empty(name, p, coll, eng, rot=(-90, 0, 0), size=0.3, kind='SINGLE_ARROW')  # local +Z -> backwards (+Y)
    return eng


# ---------------------------------------------------------------- Vilin Konjic: Grix player ship (short, chunky dragonfly)
def build_dragonfly(coll):
    root = empty('Grix_VilinKonjic', (0, 0, 0), coll, size=0.8, kind='PLAIN_AXES')
    parts = []
    # head: two huge wraparound compound eyes meeting on top, small face plate + mandible guns
    for sx in (1, -1):
        parts.append(ellipsoid('eye', (0.34 * sx, -2.15, 0.22), (0.42, 0.42, 0.4), M['eye'], coll, 20, 14))
        parts += limb('gun', [(0.24 * sx, -2.35, -0.25), (0.34 * sx, -2.75, -0.3), (0.2 * sx, -3.05, -0.3)],
                      [0.11, 0.08, 0.05], M['white'], coll, verts=8)
        parts.append(segment('barrel', (0.2 * sx, -2.95, -0.3), (0.2 * sx, -3.25, -0.3), 0.045, 0.035, M['chitin2'], coll, 8))
    parts.append(ellipsoid('face', (0, -2.45, -0.02), (0.3, 0.2, 0.3), M['chitin'], coll))
    parts.append(ellipsoid('brow', (0, -2.05, 0.5), (0.12, 0.38, 0.06), M['chitin2'], coll, 12, 8))
    # thorax: big armoured block, team shield on top, red/white collar
    parts.append(ellipsoid('collar', (0, -1.75, 0.08), (0.48, 0.14, 0.42), M['red'], coll, 18, 8))
    parts.append(ellipsoid('thorax', (0, -0.95, 0.08), (0.78, 0.95, 0.62), M['chitin'], coll, 22, 14))
    parts.append(ellipsoid('shield', (0, -0.95, 0.58), (0.52, 0.7, 0.16), M['team'], coll))
    parts.append(ellipsoid('ridge', (0, -0.95, 0.72), (0.08, 0.6, 0.06), M['chitin2'], coll, 10, 6))
    for sx in (1, -1):
        parts.append(ellipsoid('pauldron', (0.62 * sx, -1.15, 0.28), (0.3, 0.42, 0.22), M['chitin2'], coll, 14, 8, rot=(0, 20 * sx, 0)))
        parts.append(segment('spike', (0.5 * sx, -0.6, 0.45), (0.62 * sx, -0.2, 0.85), 0.09, 0.0, M['chitin2'], coll, 6))
    # wings: four short, broad, swept stubs (well under body length) with blue veins
    for sx in (1, -1):
        for nm, root_, ln, wd, ang, z in (('wingF', (0.55, -1.25), 0.85, 0.26, 72, 0.5), ('wingH', (0.55, -0.7), 0.75, 0.24, 52, 0.45)):
            a = math.radians(ang); d = Vector((math.sin(a) * sx, math.cos(a), 0.06))
            rootp = Vector((root_[0] * sx, root_[1], z))
            w = disc(nm, rootp + d * ln, ln, wd, M['wing'], coll, (3 * sx, 4, 90 - ang * sx), verts=20, thick=0.01)
            for pl in w.data.polygons: pl.use_smooth = False
            parts.append(w)
            parts.append(segment('vein', tuple(rootp), tuple(rootp + d * ln * 1.92), 0.035, 0.01, M['blue'], coll, 6))
            parts.append(segment('vein2', tuple(rootp + Vector((0, 0.08, 0))), tuple(rootp + d * ln * 1.6 + Vector((0, 0.12, 0))), 0.02, 0.008, M['blue'], coll, 6))
        parts.append(ellipsoid('wroot', (0.5 * sx, -0.98, 0.5), (0.12,) * 3, M['blue'], coll, 10, 6))
    # legs: six short folded legs under the thorax (landing gear)
    for sx in (1, -1):
        for i, y in enumerate((-1.45, -1.0, -0.55)):
            parts += limb('leg', [(0.45 * sx, y, -0.35), (0.85 * sx, y + 0.1, -0.3), (0.75 * sx, y + 0.35, -0.75)],
                          [0.07, 0.055, 0.02], M['chitin'], coll, verts=6)
    # abdomen: short (a lot shorter than a real dragonfly), segmented, Croatian bands, twin engine pods at the tail
    cols = ['red', 'white', 'blue', 'red', 'white']
    for i in range(5):
        t = i / 4
        r = 0.5 - 0.22 * t
        y = -0.05 + i * 0.42; z = 0.05 - 0.04 * i
        parts.append(ellipsoid('seg', (0, y, z), (r, 0.3, r * 0.9), M['chitin'], coll, 18, 10))
        parts.append(ellipsoid('rim', (0, y + 0.2, z), (r * 1.03, 0.06, r * 0.93), M[cols[i]], coll, 18, 6))
    for sx in (1, -1):
        parts.append(ellipsoid('pod', (0.36 * sx, 1.85, -0.05), (0.2, 0.42, 0.2), M['chitin2'], coll, 14, 10))
        parts.append(segment('nozzle', (0.36 * sx, 2.15, -0.05), (0.36 * sx, 2.38, -0.05), 0.16, 0.2, M['blue'], coll, 12))
        parts.append(ellipsoid('glowring', (0.36 * sx, 2.37, -0.05), (0.15, 0.03, 0.15), M['glow'], coll, 12, 4))
    parts.append(segment('tailspike', (0, 1.9, -0.1), (0, 2.5, -0.18), 0.12, 0.0, M['chitin2'], coll, 8))
    flag = quad('Flag', (0, 0.4, 0.52), 0.42, M['flag'], coll)
    body = join(parts, 'Body')
    body.parent = root; flag.parent = root
    # sockets: Engines exhaust backwards (+Y in Blender = Unity -Z), thrusters on the thorax sides
    engines(root, coll, [('Engine_L', (0.36, 2.4, -0.05)), ('Engine_R', (-0.36, 2.4, -0.05))])
    thr = empty('Thrusters', (0, 0, 0), coll, root, size=0.1, kind='PLAIN_AXES')
    for nm, p, rot in (('Thruster_FL', (0.8, -1.6, 0.0), (0, 0, 90)), ('Thruster_FR', (-0.8, -1.6, 0.0), (0, 0, -90)),
                       ('Thruster_BL', (0.6, 0.5, 0.0), (0, 0, 90)), ('Thruster_BR', (-0.6, 0.5, 0.0), (0, 0, -90))):
        # local +Z points sideways (outwards) -> Unity +Y (green arrow) = exhaust direction
        e = empty(nm, p, coll, thr, size=0.25, kind='SINGLE_ARROW')
        e.rotation_euler = (0, math.radians(90 if p[0] > 0 else -90), 0)
    for nm, x in (('FiringPoint_L', 0.2), ('FiringPoint_R', -0.2)):
        empty(nm, (x, -3.3, -0.3), coll, root, size=0.25, kind='SINGLE_ARROW', rot=(90, 0, 0))
    return root

c = new_coll('Dragonfly'); df = build_dragonfly(c)
print('TRIS', tri_count_fn(df) if 'tri_count_fn' in dir() else sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in df.children_recursive if o.type=='MESH'))
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'Grix_Ship.blend'))
print('DONE')
