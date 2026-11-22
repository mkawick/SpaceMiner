"""Export Grix models for Unity (Y-up file). The axis conversion is put on the root's direct children,
so the root itself comes in with no rotation and nested sockets keep correct local transforms.
Socket convention (Blender local axes): +Y = exhaust (Unity green arrow)."""
import bpy, sys, os, math
args = sys.argv[sys.argv.index('--') + 1:]
blend, outdir, roots = args[0], args[1], args[2:]
bpy.ops.wm.open_mainfile(filepath=blend)
m = bpy.data.materials.get('FlagDecal')
if m:
    for n in [n for n in m.node_tree.nodes if n.type == 'TEX_IMAGE']: m.node_tree.nodes.remove(n)
import re
base = lambda n: re.sub(r'\.\d{3}$', '', n[3:] if n.startswith('zz_') else n)   # 'Engines.001' -> 'Engines'
for o in bpy.data.objects:
    if o.type != 'EMPTY' or not o.parent: continue
    p = base(o.parent.name)
    if p == 'Engines':
        o.rotation_euler = (0, 0, 0)                     # +Y = backwards (Blender +Y is the tail)
        if 'Grix_VilinKonjic' in [a.name for a in [o.parent.parent]]:
            o.location.y = 2.45                          # just behind the dark-blue nozzles
    elif p == 'Thrusters':
        o.rotation_euler = (0, 0, math.radians(-90 if o.location.x > 0 else 90))  # +Y outwards
    elif o.name.startswith('FiringPoint') or p == 'DockPoints':
        o.rotation_euler = (0, 0, 0)
from mathutils import Matrix
# Blender Z-up -> FBX Y-up. Nose = Blender -Y -> Unity +Z.
G = Matrix.Rotation(math.radians(-90), 4, 'X')   # what the exporter applies to the root
C = G   # (no extra half turn: with Bake Axis Conversion off, Unity shows the nose on +Z)
for name in roots:
    root = bpy.data.objects.get(name) or bpy.data.objects['zz_' + name]
    # Free up clean names: park every other object, then strip Blender's .001 suffixes from this asset.
    mine = set([root] + list(root.children_recursive))
    for o in bpy.data.objects:
        if o not in mine and not o.name.startswith('zz_'): o.name = 'zz_' + o.name
    for o in mine: o.name = base(o.name)
    # The exporter multiplies the root by C; cancel that on the root and give it to the children instead.
    for child in root.children:
        child.matrix_basis = C @ child.matrix_basis
    root.matrix_basis = G.inverted() @ root.matrix_basis
    bpy.context.view_layer.update()
    objs = [root] + list(root.children_recursive)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=os.path.join(outdir, name + '.fbx'), use_selection=True,
        object_types={'MESH', 'EMPTY'}, axis_forward='-Z', axis_up='Y', bake_space_transform=False,
        apply_scale_options='FBX_SCALE_UNITS', mesh_smooth_type='FACE', add_leaf_bones=False,
        path_mode='STRIP', embed_textures=False, use_mesh_modifiers=True)
    print('EXPORTED', name)
