import bpy, math, sys, os
from mathutils import Vector
a=sys.argv[sys.argv.index('--')+1:]; OUT=a[0]; S=int(a[1]); which=a[2] if len(a)>2 else 'all'
bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT,'Grix_Buildings.blend'))
sc=bpy.context.scene; sc.render.engine='CYCLES'; sc.cycles.samples=S; sc.cycles.device='CPU'; sc.cycles.use_denoising=True
sc.render.resolution_x,sc.render.resolution_y=1400,900; sc.view_settings.view_transform='Standard'
w=bpy.data.worlds.new('W'); sc.world=w; w.use_nodes=True
w.node_tree.nodes['Background'].inputs['Color'].default_value=(0.012,0.014,0.022,1)
def light(n,rot,e,c):
    d=bpy.data.lights.new(n,'SUN'); d.energy=e; d.color=c; o=bpy.data.objects.new(n,d); sc.collection.objects.link(o); o.rotation_euler=[math.radians(x) for x in rot]
light('Key',(40,10,-35),4.0,(1,.95,.88)); light('Rim',(-55,0,160),2.5,(.55,.65,1)); light('Fill',(70,0,90),.6,(.9,.8,1))
cd=bpy.data.cameras.new('C'); cam=bpy.data.objects.new('C',cd); sc.collection.objects.link(cam); sc.camera=cam
def look(t,dist,yaw,pitch,lens=50):
    t=Vector(t); y,p=math.radians(yaw),math.radians(pitch)
    cam.location=t+Vector((math.sin(y)*math.cos(p),-math.cos(y)*math.cos(p),math.sin(p)))*dist
    cam.rotation_euler=(t-cam.location).to_track_quat('-Z','Y').to_euler(); cd.lens=lens
def show(name):
    for c in bpy.data.collections: c.hide_render = c.name != name
shots={'Rafinerija':[('raf_3q.png',(0,-1.5,5),35,28,34),('raf_side.png',(0,-1.5,5),90,10,34),('raf_top.png',(0,-1.5,3),20,62,38)],
       'Tvornica':[('tvo_3q.png',(0,-1,4),35,28,36),('tvo_top.png',(0,-1,3),20,62,40),('tvo_close.png',(0,0,3),50,24,24)]}
for coll,lst in shots.items():
    if which not in ('all',coll): continue
    show(coll)
    for fn,t,yaw,pitch,dist in lst:
        look(t,dist,yaw,pitch); sc.render.filepath=os.path.join(OUT,fn); bpy.ops.render.render(write_still=True)
