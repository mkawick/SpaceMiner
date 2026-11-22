import bpy, math, sys, os
from mathutils import Vector
OUT=sys.argv[sys.argv.index('--')+1]; S=int(sys.argv[sys.argv.index('--')+2])
bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT,'Grix_Ship.blend'))
sc=bpy.context.scene; sc.render.engine='CYCLES'; sc.cycles.samples=S; sc.cycles.device='CPU'; sc.cycles.use_denoising=True
sc.render.resolution_x,sc.render.resolution_y=1400,900; sc.view_settings.view_transform='Standard'
w=bpy.data.worlds.new('W'); sc.world=w; w.use_nodes=True
w.node_tree.nodes['Background'].inputs['Color'].default_value=(0.012,0.014,0.022,1)
def light(n,rot,e,c):
    d=bpy.data.lights.new(n,'SUN'); d.energy=e; d.color=c; o=bpy.data.objects.new(n,d); sc.collection.objects.link(o); o.rotation_euler=[math.radians(a) for a in rot]
light('Key',(40,10,-35),4.0,(1,.95,.88)); light('Rim',(-55,0,160),2.5,(.55,.65,1)); light('Fill',(70,0,90),.6,(.9,.8,1))
cd=bpy.data.cameras.new('C'); cam=bpy.data.objects.new('C',cd); sc.collection.objects.link(cam); sc.camera=cam
def look(t,dist,yaw,pitch,lens):
    t=Vector(t); y,p=math.radians(yaw),math.radians(pitch)
    cam.location=t+Vector((math.sin(y)*math.cos(p),-math.cos(y)*math.cos(p),math.sin(p)))*dist
    cam.rotation_euler=(t-cam.location).to_track_quat('-Z','Y').to_euler(); cd.lens=lens
for name,(yaw,pitch,dist) in {'ship_3q.png':(35,26,8.6),'ship_top.png':(180,70,12.5),'ship_rear.png':(160,22,11)}.items():
    look((0,-0.3,0),dist,yaw,pitch,50); sc.render.filepath=os.path.join(OUT,name); bpy.ops.render.render(write_still=True)
