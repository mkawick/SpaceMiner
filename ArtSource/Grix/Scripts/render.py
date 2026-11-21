import bpy, math, sys, os
from mathutils import Vector
OUT = sys.argv[sys.argv.index('--') + 1]
SAMPLES = int(sys.argv[sys.argv.index('--') + 2]) if len(sys.argv) > sys.argv.index('--') + 2 else 48
bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT, 'Grix_Assets.blend'))
sc = bpy.context.scene
sc.render.engine = 'CYCLES'; sc.cycles.samples = SAMPLES; sc.cycles.device = 'CPU'
sc.cycles.use_denoising = True
sc.render.resolution_x, sc.render.resolution_y = 1400, 900
sc.view_settings.view_transform = 'Standard'
sc.view_settings.look = 'None'
w = bpy.data.worlds.new('W'); sc.world = w; w.use_nodes = True
w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.012, 0.014, 0.022, 1)
w.node_tree.nodes['Background'].inputs['Strength'].default_value = 1.0

def light(name, kind, loc, rot, energy, color=(1, 1, 1), size=1.0):
    d = bpy.data.lights.new(name, kind); d.energy = energy; d.color = color
    if kind == 'AREA': d.size = size
    o = bpy.data.objects.new(name, d); sc.collection.objects.link(o)
    o.location = loc; o.rotation_euler = [math.radians(a) for a in rot]
    return o
light('Key', 'SUN', (0, 0, 10), (40, 10, -35), 4.0, (1.0, 0.95, 0.88))
light('Rim', 'SUN', (0, 0, 10), (-55, 0, 160), 2.5, (0.55, 0.65, 1.0))
light('Fill', 'SUN', (0, 0, 10), (70, 0, 90), 0.6, (0.9, 0.8, 1.0))

cam_d = bpy.data.cameras.new('Cam'); cam = bpy.data.objects.new('Cam', cam_d); sc.collection.objects.link(cam); sc.camera = cam

def look(target, dist, yaw, pitch, lens=50):
    t = Vector(target); y, p = math.radians(yaw), math.radians(pitch)
    cam.location = t + Vector((math.sin(y) * math.cos(p), -math.cos(y) * math.cos(p), math.sin(p))) * dist
    cam.rotation_euler = (t - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam_d.lens = lens

def show(names):
    for c in bpy.data.collections:
        c.hide_render = c.name not in names

def team_copy(root_name, colour_hex, offset, coll):
    """Duplicate an asset hierarchy with its TeamColour slot swapped, for colour demos."""
    def lin(h):
        h = h.lstrip('#'); c = [int(h[i:i+2], 16) / 255 for i in (0, 2, 4)]
        return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c) + (1,)
    tm = bpy.data.materials['TeamColour'].copy(); tm.name = 'Team_' + colour_hex
    tm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = lin(colour_hex)
    src = bpy.data.objects[root_name]; mp = {}
    for o in [src] + list(src.children_recursive):
        n = o.copy()
        if o.type == 'MESH':
            n.data = o.data.copy()
            for i, m in enumerate(n.data.materials):
                if m and m.name == 'TeamColour': n.data.materials[i] = tm
        coll.objects.link(n); mp[o] = n
    for o, n in mp.items():
        if o.parent in mp: n.parent = mp[o.parent]
    mp[src].location = offset
    return mp[src]

def render(name):
    sc.render.filepath = os.path.join(OUT, name); bpy.ops.render.render(write_still=True)

which = sys.argv[sys.argv.index('--') + 3] if len(sys.argv) > sys.argv.index('--') + 3 else 'all'
if which in ('all', 'bees'):
    show({'Rudar'}); look((0, 0.05, 0), 6.0, 38, 30, 60); render('rudar_3q.png')
    show({'Transport'}); look((0, 0.3, 0), 7.4, 38, 32, 60); render('transport_3q.png')
if which in ('all', 'hub'):
    show({'Hub'}); look((0, 0, 1.5), 34, 150, 38, 50); render('hub_3q.png')
if which in ('all', 'lineup'):
    demo = bpy.data.collections.new('Demo'); sc.collection.children.link(demo)
    for i, col in enumerate(('#CCE8CC', '#E17633', '#F6EFEE')):
        team_copy('Grix_Rudar', col, (-3.2 + 3.2 * i, 0, 0), demo)
    show({'Demo'}); look((0, 0.2, 0), 13.5, 180, 62, 50); render('rudar_team_colours.png')
if which in ('all', 'scene'):
    demo = bpy.data.collections.new('Scene'); sc.collection.children.link(demo)
    h = team_copy('Grix_Skladiste', '#CCE8CC', (0, 0, 0), demo)
    import random; random.seed(3)
    for i in range(6):
        a = math.radians(60 * i + 15); r = 11 + random.uniform(-1, 2)
        b = team_copy('Grix_Rudar' if i % 3 else 'Grix_Prijevoznik', '#CCE8CC', (math.cos(a) * r, math.sin(a) * r, 1.5), demo)
        b.rotation_euler = (0, 0, a - math.radians(90))
        b.scale = (1.4, 1.4, 1.4)
    show({'Scene'}); look((0, 0, 0), 46, 160, 58, 45); render('hub_scene.png')
