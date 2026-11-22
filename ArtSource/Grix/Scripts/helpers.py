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


def tri_count(root):
    return sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in root.children_recursive if o.type == 'MESH')
