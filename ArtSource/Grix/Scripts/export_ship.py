import bpy, sys, os
OUT=sys.argv[sys.argv.index('--')+1]
bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT,'Grix_Ship.blend'))
m=bpy.data.materials['FlagDecal']
for n in [n for n in m.node_tree.nodes if n.type=='TEX_IMAGE']: m.node_tree.nodes.remove(n)
root=bpy.data.objects['Grix_VilinKonjic']
objs=[root]+list(root.children_recursive)
bpy.ops.object.select_all(action='DESELECT')
for o in objs: o.select_set(True)
bpy.context.view_layer.objects.active=root
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT,'Grix_VilinKonjic.fbx'),use_selection=True,object_types={'MESH','EMPTY'},
    axis_forward='-Z',axis_up='Y',bake_space_transform=True,apply_scale_options='FBX_SCALE_UNITS',mesh_smooth_type='FACE',
    add_leaf_bones=False,path_mode='STRIP',embed_textures=False,use_mesh_modifiers=True)
print('DONE', [o.name for o in objs if o.type=='EMPTY'])
