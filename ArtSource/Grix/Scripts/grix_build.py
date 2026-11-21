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

# ---------------------------------------------------------------- bee body (head, thorax, wings, legs)
def bee_body(prefix, coll, s=1.0):
    P = lambda x, y, z: (x * s, y * s, z * s)
    parts = []
    # head: wedge-ish skull with brow crests
    parts.append(ellipsoid('head', P(0, -0.9, 0.04), (0.32 * s, 0.36 * s, 0.26 * s), M['chitin'], coll, rot=(-12, 0, 0)))
    for sx in (1, -1):
        parts.append(ellipsoid('eye', P(0.22 * sx, -0.95, 0.1), (0.12 * s, 0.2 * s, 0.17 * s), M['eye'], coll, 14, 10,
                               rot=(0, 0, -18 * sx)))
        parts.append(ellipsoid('crest', P(0.15 * sx, -0.9, 0.26), (0.1 * s, 0.3 * s, 0.05 * s), M['chitin2'], coll, 12, 8,
                               rot=(10, 0, -15 * sx)))
        parts += limb('ant', [P(0.08 * sx, -1.1, 0.2), P(0.18 * sx, -1.3, 0.46), P(0.34 * sx, -1.6, 0.44)],
                      [0.025 * s, 0.02 * s, 0.014 * s], M['chitin'], coll, verts=6)
        parts.append(ellipsoid('antTip', P(0.34 * sx, -1.6, 0.44), (0.035 * s,) * 3, M['glow'], coll, 8, 6))
        # mining mandibles
        parts += limb('mand', [P(0.13 * sx, -1.12, -0.08), P(0.22 * sx, -1.32, -0.11), P(0.06 * sx, -1.5, -0.12)],
                      [0.07 * s, 0.045 * s, 0.01 * s], M['white'], coll, verts=6)
    # thorax: armoured, team-colour shield on top, two dorsal spikes
    parts.append(ellipsoid('thorax', P(0, -0.3, 0.02), (0.42 * s, 0.46 * s, 0.36 * s), M['chitin'], coll))
    parts.append(ellipsoid('shield', P(0, -0.3, 0.33), (0.3 * s, 0.34 * s, 0.1 * s), M['team'], coll))
    parts.append(ellipsoid('collar', P(0, -0.68, 0.03), (0.3 * s, 0.1 * s, 0.26 * s), M['red'], coll, 14, 8))
    for sx in (1, -1):
        parts.append(segment('spike', P(0.2 * sx, -0.12, 0.3), P(0.3 * sx, 0.08, 0.62), 0.06 * s, 0.0, M['chitin2'], coll, 6))
    # wings: long, narrow, veined
    for sx in (1, -1):
        for nm, root_, ln, wd, ang, z in (('wingF', (0.24, -0.3), 0.8, 0.17, 34, 0.46), ('wingH', (0.22, -0.05), 0.55, 0.12, 22, 0.42)):
            a = math.radians(ang); d = Vector((math.sin(a) * sx, math.cos(a), 0.12))
            rootp = Vector(P(root_[0] * sx, root_[1], z))
            ctr = rootp + d * ln * s
            w = disc(nm, ctr, ln * s, wd * s, M['wing'], coll, (4 * sx, 6, 90 - ang * sx), verts=20, thick=0.008)
            for pl in w.data.polygons: pl.use_smooth = False
            parts.append(w)
            tip = rootp + d * ln * 1.9 * s
            parts.append(segment('vein', tuple(rootp), tuple(tip), 0.022 * s, 0.006 * s, M['blue'], coll, 6))
        parts.append(ellipsoid('wroot', P(0.24 * sx, -0.25, 0.44), (0.065 * s,) * 3, M['blue'], coll, 8, 6))
    # six legs, folded, spiked feet
    legs = [(-0.52, 0), (-0.32, 1), (-0.1, 2)]
    for sx in (1, -1):
        for y, i in legs:
            hip = P(0.24 * sx, y, -0.18)
            knee = P(0.6 * sx, y + 0.04 + i * 0.08, -0.08)
            foot = P(0.52 * sx, y + 0.28 + i * 0.16, -0.5)
            parts += limb('leg', [hip, knee, foot], [0.05 * s, 0.04 * s, 0.012 * s], M['chitin'], coll, verts=6)
    return parts

def abdomen(coll, y0, s=1.0, segs=5):
    """Segmented wasp-like abdomen: overlapping chitin rings with thin red/white/blue rims."""
    parts = []
    cols = ['red', 'white', 'red', 'white', 'blue', 'blue']
    for i in range(segs):
        t = i / (segs - 1)
        rx = (0.44 - 0.2 * t * t) * s; rz = (0.4 - 0.18 * t * t) * s
        y = (y0 + i * 0.2) * s; z = (-0.04 - 0.05 * i) * s
        parts.append(ellipsoid('seg', (0, y, z), (rx, 0.2 * s, rz), M['chitin'], coll, 18, 10))
        parts.append(ellipsoid('rim', (0, y + 0.1 * s, z), (rx * 1.02, 0.05 * s, rz * 1.02), M[cols[i]], coll, 18, 6))
    ytip = (y0 + segs * 0.2) * s; ztip = (-0.04 - 0.05 * segs) * s
    parts.append(segment('stinger', (0, ytip - 0.12 * s, ztip), (0, ytip + 0.18 * s, ztip - 0.06 * s), 0.07 * s, 0.0, M['blue'], coll, 8))
    return parts, ytip

def engines(root, coll, pts):
    eng = empty('Engines', (0, 0, 0), coll, root, size=0.1, kind='PLAIN_AXES')
    for name, p in pts:
        empty(name, p, coll, eng, rot=(-90, 0, 0), size=0.3, kind='SINGLE_ARROW')  # local +Z -> backwards (+Y)
    return eng

# ---------------------------------------------------------------- Rudar (worker / miner bee)
def build_rudar(coll, team=None):
    root = empty('Grix_Rudar', (0, 0, 0), coll, size=0.5, kind='PLAIN_AXES')
    body = join(bee_body('r', coll), 'Body')
    ab, ytip = abdomen(coll, 0.28)
    baskets = [ellipsoid('basket', (0.46 * sx, 0.36, -0.44), (0.07, 0.1, 0.07), M['glow'], coll, 10, 6) for sx in (1, -1)]
    cargo = join(ab + baskets, 'Cargo')
    for o in (body, cargo): o.parent = root
    engines(root, coll, [('Engine_L', (0.16, 0.95, 0.12)), ('Engine_R', (-0.16, 0.95, 0.12))])
    empty('FiringPoint', (0, -1.5, -0.12), coll, root, size=0.2, kind='SINGLE_ARROW', rot=(90, 0, 0))
    return root

# ---------------------------------------------------------------- Prijevoznik (transport bee: same body, honeycomb cargo)
def build_transport(coll):
    s = 1.15
    root = empty('Grix_Prijevoznik', (0, 0, 0), coll, size=0.5, kind='PLAIN_AXES')
    body = join(bee_body('t', coll, s), 'Body')
    parts = []
    # cargo frame: cradle arms holding a honeycomb pod of 7 cells
    cy, cz = 1.05, -0.05
    r = 0.24
    cells = [(0, 0)] + [(math.cos(math.radians(60 * i + 30)) * r * 1.8, math.sin(math.radians(60 * i + 30)) * r * 1.8) for i in range(6)]
    for i, (x, y) in enumerate(cells):
        h = 0.5 if i else 0.62
        parts.append(hexprism('cell', (x, cy + y), r * 0.98, h, M['chitin2'], coll, z0=cz - h / 2, rot=math.radians(0)))
        cap = M['glow'] if i in (0, 2, 5) else (M['white'] if i % 2 else M['red'])
        parts.append(hexprism('cap', (x, cy + y), r * 0.78, 0.06, cap, coll, z0=cz + h / 2))
    for sx in (1, -1):
        parts += limb('cradle', [(0.22 * sx, 0.1, -0.05), (0.62 * sx, 0.55, 0.05), (0.62 * sx, 1.45, -0.02)],
                      [0.07, 0.06, 0.05], M['chitin'], coll)
    parts.append(segment('spine', (0, 0.12, 0.0), (0, 1.6, -0.05), 0.11, 0.07, M['blue'], coll))
    flag = quad('Flag', (0, cy, cz + 0.42), 0.4, M['flag'], coll)
    cargo = join(parts, 'Cargo')
    for o in (body, cargo, flag): o.parent = root
    engines(root, coll, [('Engine_L', (0.62, 1.55, 0.0)), ('Engine_R', (-0.62, 1.55, 0.0))])
    empty('FiringPoint', (0, -1.72, -0.14), coll, root, size=0.2, kind='SINGLE_ARROW', rot=(90, 0, 0))
    return root

# ---------------------------------------------------------------- Skladiste hive hub
def build_hub(coll):
    root = empty('Grix_Skladiste', (0, 0, 0), coll, size=1.5, kind='PLAIN_AXES')
    parts = []
    R = 1.25; gap = 1.06
    def axial(q, r):
        return (R * gap * 1.5 * q, R * gap * math.sqrt(3) * (r + q / 2))
    coords = []
    for q in range(-3, 4):
        for r in range(-3, 4):
            if abs(q + r) <= 3: coords.append((q, r))
    corners = {(3, 0), (-3, 0), (0, 3), (0, -3), (3, -3), (-3, 3)}
    docks = []
    for q, r in coords:
        x, y = axial(q, r); dist = max(abs(q), abs(r), abs(q + r))
        if dist == 0: continue
        if dist == 3 and (q, r) not in corners and random.random() < 0.28: continue
        if (q, r) in corners:
            h = 1.1
            parts.append(hexprism('port', (x, y), R, h, M['chitin'], coll))
            parts.append(hexprism('portRim', (x, y), R * 0.86, 0.08, M['team'], coll, z0=h))
            parts.append(hexprism('portHole', (x, y), R * 0.6, 0.1, M['hole'], coll, z0=h + 0.02))
            docks.append((x * 1.18, y * 1.18, 1.0))
            continue
        h = max(0.9, 4.6 - dist * 1.05 + random.uniform(-0.35, 0.35))
        parts.append(hexprism('cell', (x, y), R, h, M['chitin'] if dist > 1 else M['chitin2'], coll))
        roll = random.random()
        if roll < 0.32:
            parts.append(hexprism('ore', (x, y), R * 0.8, 0.1, M['glow'], coll, z0=h - 0.05))
        elif roll < 0.75:
            parts.append(hexprism('wax', (x, y), R * 0.84, 0.16, M['white'], coll, z0=h))
        else:
            parts.append(hexprism('capR', (x, y), R * 0.84, 0.12, M['red'], coll, z0=h))
    # central spire: stacked hex rings with Croatian bands, team ring, flag on top
    z = 0.0
    tiers = [(1.25, 2.2, 'chitin'), (1.05, 0.5, 'red'), (1.0, 0.5, 'white'), (0.95, 0.5, 'blue'),
             (0.9, 1.6, 'chitin'), (1.02, 0.28, 'team'), (0.85, 1.2, 'chitin2')]
    for rr, hh, mk in tiers:
        parts.append(hexprism('spire', (0, 0), rr, hh, M[mk], coll, z0=z)); z += hh
    parts.append(hexprism('crown', (0, 0), 1.0, 0.18, M['white'], coll, z0=z))
    flag = quad('Flag', (0, 0, z + 0.19), 1.3, M['flag'], coll)
    # chitin ribs arching from the rim to the spire (insect carapace)
    for i in range(6):
        a = math.radians(60 * i + 30)
        c, s_ = math.cos(a), math.sin(a)
        pts = [(c * 7.4, s_ * 7.4, 0.2), (c * 6.4, s_ * 6.4, 3.0), (c * 4.0, s_ * 4.0, 5.4), (c * 1.3, s_ * 1.3, 6.2)]
        parts += limb('rib', pts, [0.34, 0.26, 0.2, 0.14], M['chitin'], coll, verts=8)
        parts.append(ellipsoid('ribNode', pts[1], (0.3, 0.3, 0.3), M['glow'], coll, 10, 6))
    # base slab and six folded legs (for later relocation)
    parts.append(hexprism('slab', (0, 0), 7.9, 0.6, M['chitin2'], coll, z0=-0.6, rot=math.radians(30)))
    for i in range(6):
        a = math.radians(60 * i)
        c, s_ = math.cos(a), math.sin(a)
        pts = [(c * 6.6, s_ * 6.6, -0.5), (c * 8.4, s_ * 8.4, 0.7), (c * 9.0, s_ * 9.0, -2.0), (c * 9.3, s_ * 9.3, -2.6)]
        parts += limb('leg', pts, [0.42, 0.34, 0.2, 0.08], M['chitin'], coll, verts=8)
    hub = join(parts, 'Hive')
    hub.parent = root; flag.parent = root
    dp = empty('DockPoints', (0, 0, 0), coll, root, size=0.5, kind='PLAIN_AXES')
    for i, d in enumerate(docks):
        empty(f'DockPoint_{i}', d, coll, dp, size=0.6, kind='SPHERE')
    return root

# ---------------------------------------------------------------- export
def export(root, path):
    objs = [root] + list(root.children_recursive)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH', 'EMPTY'},
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                             apply_scale_options='FBX_SCALE_UNITS', mesh_smooth_type='FACE',
                             add_leaf_bones=False, path_mode='STRIP', use_mesh_modifiers=True)

def tri_count(root):
    n = 0
    for o in root.children_recursive:
        if o.type == 'MESH':
            n += sum(len(p.vertices) - 2 for p in o.data.polygons)
    return n

cR = new_coll('Rudar'); cT = new_coll('Transport'); cH = new_coll('Hub')
rud = build_rudar(cR); tra = build_transport(cT); hub = build_hub(cH)
os.makedirs(OUT, exist_ok=True)
for root, fn in ((rud, 'Grix_Rudar.fbx'), (tra, 'Grix_Prijevoznik.fbx'), (hub, 'Grix_Skladiste.fbx')):
    export(root, os.path.join(OUT, fn))
    print('TRIS', root.name, tri_count(root))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'Grix_Assets.blend'))
print('DONE')
