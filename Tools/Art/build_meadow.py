"""Original Meadow Village meshes. Run with Blender --background --python this_file.

The source stays outside Assets; exported FBX is the Unity interchange format.
All distances are metres. No downloaded assets or texture dependencies.
"""
import bpy
import math
import random
import json
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'My project/Assets/AIFarm/Art/Models/Meadow'
SOURCE = ROOT / 'ArtSource'
OUT.mkdir(parents=True, exist_ok=True)
SOURCE.mkdir(parents=True, exist_ok=True)
random.seed(142)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
PALETTE = {
    'Grass': '#86a764', 'GrassLight': '#a3bc78', 'GrassDark': '#66894d',
    'Earth': '#997354', 'EarthDark': '#795540', 'Sand': '#d2b88b',
    'Stone': '#acac98', 'StoneLight': '#c9c4ac', 'Water': '#4dabb0',
    'WaterLight': '#94d6cf', 'Wood': '#895c3e', 'WoodLight': '#c29a63',
    'WoodDark': '#4b4239', 'Cream': '#f3dfb1', 'Plaster': '#e4c996',
    'RoofRust': '#b96048', 'RoofLight': '#d18459', 'RoofTeal': '#467b79',
    'RoofBlue': '#576e88', 'RoofGold': '#b99954', 'Glass': '#547a79',
    'Glow': '#ffda8a', 'Leaf': '#537849', 'LeafLight': '#719954',
    'LeafGold': '#a5b567', 'Bark': '#69503b', 'FlowerPink': '#d98694',
    'FlowerWhite': '#fff1cf', 'FlowerGold': '#ecc55c', 'Fruit': '#d67b4e',
    'Metal': '#4b6264', 'Lavender': '#a49ab8', 'Red': '#ad5950',
}
MATS = {}
for name, hx in PALETTE.items():
    rgb = [int(hx[i:i+2], 16) / 255 for i in (1, 3, 5)]
    mat = bpy.data.materials.new('Meadow_' + name)
    mat.diffuse_color = (*rgb, 1)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = (*rgb, 1)
    bsdf.inputs['Roughness'].default_value = .78
    MATS[name] = mat

# Use architectural coordinates (x, north, height) throughout this source.
def finish(obj, name, material, smooth=False):
    obj.name = name
    obj.data.materials.append(MATS[material])
    if smooth:
        for face in obj.data.polygons:
            face.use_smooth = True
    return obj

def box(name, p, size, mat, bevel=.06, rotation=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=p)
    o = bpy.context.object
    o.dimensions = size
    o.rotation_euler[2] = rotation
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        m = o.modifiers.new('Soft crafted edges', 'BEVEL')
        m.width = bevel
        m.segments = 2
        bpy.ops.object.modifier_apply(modifier=m.name)
    return finish(o, name, mat)

def ellipsoid(name, p, scale, mat, ico=False):
    if ico:
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=1, location=p)
    else:
        bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=1, location=p)
    o=bpy.context.object
    o.scale=scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(o,name,mat,not ico)

def cylinder(name,p,radius,depth,mat,vertices=12,radius2=None):
    bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=radius,
        radius2=radius if radius2 is None else radius2, depth=depth, location=p)
    return finish(bpy.context.object,name,mat)

def beam(name, a, b, radius, mat):
    mid=(Vector(a)+Vector(b))*.5
    o=cylinder(name,mid,radius,(Vector(b)-Vector(a)).length,mat,8)
    o.rotation_euler=(Vector(b)-Vector(a)).to_track_quat('Z','Y').to_euler()
    return o

def mesh(name,verts,faces,mat):
    data=bpy.data.meshes.new(name)
    data.from_pydata(verts,[],faces)
    data.update()
    o=bpy.data.objects.new(name,data)
    bpy.context.collection.objects.link(o)
    return finish(o,name,mat)

def ribbon(name,points,width,mat,height=.014):
    verts=[]
    for i,p in enumerate(points):
        tangent=Vector(points[min(i+1,len(points)-1)])-Vector(points[max(i-1,0)])
        tangent.normalize()
        normal=Vector((-tangent.y,tangent.x))*width*.5
        verts.extend([(p[0]+normal.x,p[1]+normal.y,height),(p[0]-normal.x,p[1]-normal.y,height)])
    return mesh(name,verts,[(i*2,i*2+1,i*2+3,i*2+2) for i in range(len(points)-1)],mat)

def roof(name,x,y,w,d,eave,rise,mat):
    verts=[(x-w/2,y-d/2,eave),(x+w/2,y-d/2,eave),(x,y-d/2,eave+rise),
           (x-w/2,y+d/2,eave),(x+w/2,y+d/2,eave),(x,y+d/2,eave+rise)]
    mesh(name,verts,[(1,2,0),(5,4,3),(2,5,3,0),(1,4,5,2),(3,4,1,0)],mat)
    for front in [-1,1]:
        beam('Roof fascia',(x-w/2,y+front*d/2,eave),(x,y+front*d/2,eave+rise),.065,'Cream')
        beam('Roof fascia',(x,y+front*d/2,eave+rise),(x+w/2,y+front*d/2,eave),.065,'Cream')
    for j in range(1,6):
        yy=y-d/2+j*d/6
        beam('Tile seams',(x-w/2,yy,eave+.016),(x,yy,eave+rise+.016),.018,'RoofLight' if mat=='RoofRust' else mat)
        beam('Tile seams',(x,yy,eave+rise+.016),(x+w/2,yy,eave+.016),.018,'RoofLight' if mat=='RoofRust' else mat)
    beam('Ridge cap',(x,y-d/2-.06,eave+rise),(x,y+d/2+.06,eave+rise),.095,mat)

def window(x,y,z,w=.5):
    box('Window frame',(x,y,z),(w+.14,.10,.75),'Cream',.03)
    box('Blue window glass',(x,y-.06,z),(w,.035,.6),'Glass',.01)
    box('Window mullion',(x,y-.09,z),(.038,.03,.63),'Cream',.008)
    box('Window transom',(x,y-.09,z),(w,.03,.035),'Cream',.008)
    box('Window sill',(x,y-.08,z-.4),(w+.22,.22,.08),'WoodLight',.025)

def planter(x,y,width=.8):
    box('Flower box',(x,y,.42),(width,.35,.3),'Wood',.04)
    for i in range(5):
        xx=x+(i-2)*width/5
        beam('Flower stem',(xx,y,.55),(xx,y,.82),.012,'Leaf')
        ellipsoid('Petal cluster',(xx,y,.83),(.10,.09,.065),'FlowerPink' if i%2 else 'FlowerWhite')

def house(name,x,y,w=2.65,d=2.1,h=1.85,roofmat='RoofRust',shop=False):
    box(name+' foundation',(x,y,.12),(w+.2,d+.2,.3),'Stone',.09)
    box(name+' plaster',(x,y,h/2+.2),(w,d,h),'Plaster',.075)
    box('Skirting',(x,y,.42),(w+.025,d+.025,.16),'Wood',.02)
    for dx in [-w/2+.07,w/2-.07]:
        for dy in [-d/2+.04,d/2-.04]:
            box('Timber post',(x+dx,y+dy,1.05),(.12,.12,h),'Wood',.02)
    roof(name+' roof',x,y,w+.48,d+.5,h+.22,.83,roofmat)
    front=y-d/2-.055
    box('Door frame',(x,front,.88),(.70,.14,1.48),'Cream',.07)
    box('Painted door',(x,front-.09,.83),(.53,.06,1.32),roofmat,.06)
    box('Door top window',(x,front-.135,1.15),(.33,.025,.36),'Glass',.03)
    ellipsoid('Brass door knob',(x+.18,front-.15,.7),(.035,.03,.035),'Glow')
    box('Door step',(x,front-.19,.1),(.86,.48,.16),'StoneLight',.04)
    window(x-w*.31,front,1.2,w*.2)
    window(x+w*.31,front,1.2,w*.2)
    planter(x-w*.31,front-.22,w*.24)
    # Gable medallion, chimney masonry, chimney cap.
    o=cylinder('Attic round frame',(x,y-d/2-.06,h+.54),.23,.10,'Cream',16)
    o.rotation_euler[0]=math.pi/2
    o=cylinder('Attic glass',(x,y-d/2-.12,h+.54),.16,.015,'Glass',16)
    o.rotation_euler[0]=math.pi/2
    box('Chimney',(x+w*.29,y+.42,h+.55),(.36,.40,1.1),'Stone',.025)
    box('Chimney cap',(x+w*.29,y+.42,h+1.08),(.48,.5,.14),'StoneLight',.04)
    if shop:
        for j in range(6):
            stripe=box('Striped canvas awning',(x+(j-2.5)*(w+.2)/6,front-.55,1.83),((w+.2)/6,.96,.1),roofmat if j%2 else 'Cream',.025)
            stripe.rotation_euler[0]=.12
        for dx in [-w*.49,w*.49]:
            beam('Awning upright',(x+dx,front-.96,.07),(x+dx,front-.96,1.82),.035,'WoodDark')

def tree(x,y,s=1,variant=0):
    cylinder('Tree trunk',(x,y,1.1*s),.17*s,2.2*s,'Bark',9,radius2=.12*s)
    for dx,dy,dz in [(-.65,0,2),(.6,.15,2.4),(0,-.5,2.15),(0,0,2.9),(-.4,.45,2.65)]:
        beam('Branch',(x,y,1.3*s),(x+dx*s,y+dy*s,dz*s),.065*s,'Bark')
        ellipsoid('Soft tree crown',(x+dx*s,y+dy*s,dz*s),(.83*s,.78*s,.8*s),['Leaf','LeafLight','LeafGold'][variant%3],ico=True)

def fence_segment(a,b):
    length=(Vector(b)-Vector(a)).length
    n=max(1,round(length/1.25))
    for i in range(n+1):
        p=Vector(a).lerp(Vector(b),i/n)
        box('Fence picket',(p.x,p.y,.46),(.12,.12,.97),'WoodLight',.025)
        ellipsoid('Post cap',(p.x,p.y,.96),(.095,.095,.05),'Cream')
    for height in [.33,.7]:
        beam('Fence rail',(*a,height),(*b,height),.045,'WoodLight')

def barrel(x,y):
    cylinder('Oak barrel',(x,y,.4),.29,.8,'Wood',12,radius2=.27)
    for z in [.15,.64]:
        cylinder('Barrel iron hoop',(x,y,z),.304,.05,'Metal',12)
    cylinder('Barrel lid',(x,y,.81),.26,.035,'WoodLight',12)

# Broad island with softened edges and stratified banks, now 56 x 44 metres.
box('Meadow foundation',(0,2,-.86),(56,44,1.65),'EarthDark',1.3)
box('Meadow soil rim',(0,2,-.40),(55.8,43.8,.72),'Earth',.8)
box('Meadow turf',(0,2,-.13),(55.7,43.7,.20),'Grass',.7)

# Looped paths invite exploration around the original functional town core.
ribbon('Village west lane',[(-5,-9),(-5,-6),(-5,-2),(-5,3),(-5,5),(-9,8),(-12,9),(-17,12)],1.7,'Sand')
ribbon('Village east lane',[(5,-9),(5,-5),(5,0),(5,4),(5,5),(10,8),(15,10),(17,12)],1.7,'Sand')
ribbon('Cottage lane',[(-10,4.3),(-5,4.3),(0,4.3),(5,4.3),(10,4.3)],1.5,'Sand')
ribbon('Well approach',[(-6,-4.5),(-3,-4.6),(0,-4.7),(4,-4.8),(9,-5.4),(13,-5)],1.45,'Sand')
ribbon('Entrance avenue',[(0,-17),(0,-13),(0,-10),(-2,-8),(-4,-6)],2.3,'Sand')
ribbon('Orchard ramble',[(-5,-9),(-9,-10),(-14,-10),(-19,-6),(-20,0),(-19,6),(-15,9),(-11,8)],1.65,'Sand')
ribbon('Lake promenade',[(0,-13),(7,-12),(12,-11),(18,-11),(23,-7),(23,1),(20,6),(15,10)],1.65,'Sand')

for x,mat,name in [(-7.2,'RoofTeal','Yaya cottage'),(-2.4,'RoofRust','Amu cottage'),(2.4,'RoofGold','Xiaosui cottage'),(7.2,'RoofBlue','Momo cottage')]:
    house(name,x,6.2,2.55,1.9,1.75,mat)
house('Workshop',-7.5,2.4,2.75,2.25,2.05,'RoofRust',True)
house('Cafeteria',-7.5,-2.4,2.75,2.25,2.05,'RoofTeal',True)
house('Library',7.5,2.4,2.75,2.25,2.05,'RoofBlue',True)

# Detailed central well, hollow masonry ring and pulley.
for layer in range(3):
    for i in range(12):
        a=(i+layer*.5)*math.tau/12
        box('Well stone',(math.cos(a)*.60,-6.2+math.sin(a)*.60,.17+layer*.22),(.32,.26,.21),'StoneLight' if i%3 else 'Stone',.025,a+math.pi/2)
cylinder('Well water',(0,-6.2,.17),.48,.025,'Water',24)
for x in [-.82,.82]:
    box('Well upright',(x,-6.2,.99),(.13,.17,1.98),'Wood',.025)
beam('Well crossbar',(-.91,-6.2,1.61),(.91,-6.2,1.61),.07,'WoodDark')
beam('Well rope',(0,-6.2,1.65),(0,-6.2,.55),.016,'Cream')
roof('Well shelter',0,-6.2,2.15,1.6,1.96,.54,'RoofRust')
barrel(1.05,-6.6)

# Plaza paving, benches, lanterns, produce stall.
cylinder('Plaza stone disk',(7.1,-2.8,.016),2.0,.055,'Stone',32)
cylinder('Plaza inset',(7.1,-2.8,.05),1.8,.026,'Sand',32)
for i in range(20):
    a=i*math.tau/20
    box('Plaza setts',(7.1+1.9*math.cos(a),-2.8+1.9*math.sin(a),.07),(.31,.18,.07),'StoneLight',.025,a+math.pi/2)
for x,y in [(9.3,-1.7),(9.3,-4.2),(-10.7,-2.2)]:
    for dx in [-.6,.6]:
        box('Bench legs',(x+dx,y,.25),(.13,.43,.5),'Metal',.03)
    for yy in [-.15,.02,.19]:
        box('Bench seat slat',(x,y+yy,.51),(1.6,.13,.1),'WoodLight',.025)
    box('Bench back',(x,y+.26,.84),(1.6,.09,.39),'WoodLight',.04)
for x,y in [(-4.5,-7.7),(4.6,-7.7),(-10,4),(10,4),(-4.5,8.5),(4.5,8.5),(11,-9),(-16,7)]:
    cylinder('Lantern footing',(x,y,.12),.22,.23,'Stone',12)
    cylinder('Lantern post',(x,y,1.27),.055,2.3,'Metal',10)
    box('Lantern glow',(x,y,2.4),(.29,.29,.38),'Glow',.035)
    for dx in [-.16,.16]:
        for dy in [-.16,.16]:
            beam('Lantern cage',(x+dx,y+dy,2.18),(x+dx,y+dy,2.6),.016,'Metal')
    cylinder('Lantern top',(x,y,2.66),.27,.21,'Metal',4,radius2=0)

# Farm border & furrows are decoration: nine numbered playable plots stay in Unity.
for px in [-2.35,0,2.35]:
    for py in [-2.35,0,2.35]:
        for d in [-1.02,1.02]:
            box('Raised bed rim',(px+d,py,.115),(.055,2.07,.13),'WoodLight',.014)
            box('Raised bed rim',(px,py+d,.115),(2.07,.055,.13),'WoodLight',.014)
        for d in [-.65,-.22,.22,.65]:
            box('Soil furrow',(px+d,py,.151),(.045,1.75,.035),'EarthDark',.015)

# Orchard, perimeter trees, rocks and low wildflowers.
for x in [-17,-13.8]:
    for y in [-5.5,-1.6,2.3]:
        tree(x,y,.92,1)
        for dx,dy,dz in [(-.6,-.35,2.3),(.55,-.35,2.5),(.2,.4,2.8)]:
            ellipsoid('Orchard peaches',(x+dx,y+dy,dz*.92),(.14,.14,.15),'Fruit')
for i in range(56):
    x=random.uniform(-25,25)
    y=random.uniform(-17,21)
    if abs(x)>23 or y>16 or (x< -22 and y> -10) or (y< -14 and abs(x)>8):
        tree(x,y,random.uniform(.8,1.5),random.choice([0,0,1,2]))
for x,y in [(-12,13),(-21,11),(21,15),(-9,18),(8,20),(24,8),(-24,-10)]:
    tree(x,y,1.4,0)
for i in range(85):
    x=random.uniform(-26,26)
    y=random.uniform(-18,22)
    if abs(x)<11 and -9<y<10: continue
    if 11<x<22 and -10<y<4: continue
    ellipsoid('Meadow stone',(x,y,.15),(random.uniform(.15,.48),random.uniform(.17,.4),random.uniform(.15,.32)),'Stone' if i%2 else 'StoneLight',True)
for i in range(185):
    x=random.uniform(-25,25)
    y=random.uniform(-17,21)
    if abs(x)<11 and -10<y<10: continue
    if 10<x<23 and -11<y<5: continue
    for j in range(3):
        xx=x+random.uniform(-.25,.25)
        yy=y+random.uniform(-.25,.25)
        h=random.uniform(.18,.34)
        beam('Wildflower stem',(xx,yy,0),(xx,yy,h),.012,'GrassDark')
        ellipsoid('Wildflower bloom',(xx,yy,h),(.07,.07,.04),['FlowerGold','FlowerWhite','FlowerPink'][i%3])

# Organic pond; pebble beach, reeds, lily pads and timber dock.
pond=[]
for i in range(48):
    a=i*math.tau/48
    r=1+.065*math.sin(a*3)+.035*math.sin(a*5)
    pond.append((17+5*r*math.cos(a),-3+5.7*r*math.sin(a),.032))
mesh('Pond shoreline',[(17,-3,.026)]+[(17+(x-17)*1.10,-3+(y+3)*1.08,.026) for x,y,z in pond],[(0,i+1,(i+1)%48+1) for i in range(48)],'Sand')
mesh('Pond water',[(17,-3,.042)]+[(x,y,.042) for x,y,z in pond],[(0,i+1,(i+1)%48+1) for i in range(48)],'Water')
for i in range(24):
    x,y,z=pond[i*2]
    ellipsoid('Shore pebble',(x,y,.14),(.28,.20,.17),'StoneLight',True)
    if i%3==0:
        for j in range(5):
            xx=x+random.uniform(-.25,.25); yy=y+random.uniform(-.25,.25)
            beam('Reed',(xx,yy,.03),(xx+.1,yy,.75),.018,'LeafGold')
            cylinder('Cattail',(xx+.1,yy,.79),.045,.24,'EarthDark',8)
for i in range(8):
    x=17+random.uniform(-3,3);y=-3+random.uniform(-3,3)
    cylinder('Lily pad',(x,y,.065),random.uniform(.16,.26),.018,'LeafLight',12)
    if i%2: ellipsoid('Water lily',(x,y,.10),(.11,.11,.05),'FlowerPink')
for y in [-5.3,-3.7]:
    for x in [12.1,15.3]:
        cylinder('Dock post',(x,y,.23),.08,.8,'Wood',10)
for i in range(13):
    box('Dock plank',(12.1+i*.26,-4.5,.27),(.23,1.8,.14),'WoodLight' if i%3 else 'Wood',.028)
for k in range(7):
    ribbon('Water glint',[(15+k*.41,-.7+k*.47),(15.9+k*.41,-.7+k*.47)],.033,'WaterLight',.057)

# Landmark windmill and barn in the expanded north quarter.
wx,wy=-16,12
cylinder('Windmill stone tower',(wx,wy,2.1),1.2,4.2,'Cream',12,radius2=.85)
cylinder('Windmill roof',(wx,wy,4.65),1.3,1.45,'RoofRust',12,radius2=.06)
box('Windmill doorway',(wx,wy-1.13,.79),(.68,.13,1.5),'Wood',.1)
hub=(wx,wy-1.06,3.7)
ellipsoid('Windmill axle',hub,(.22,.22,.22),'WoodDark')
for i in range(4):
    a=math.pi/4+i*math.pi/2
    end=(wx+math.cos(a)*2.65,wy-1.10,3.7+math.sin(a)*2.65)
    beam('Windmill sail spar',hub,end,.065,'WoodDark')
    for j in range(5):
        r=1.05+j*.31
        cx=wx+math.cos(a)*r;cz=3.7+math.sin(a)*r
        beam('Windmill sail slat',(cx,wy-1.13,cz),(cx-.56*math.sin(a),wy-1.13,cz+.56*math.cos(a)),.07,'Cream')
house('Harvest barn',16,12.6,5.2,3.6,3.1,'RoofRust')
box('Barn double door',(16,10.72,1.23),(1.8,.18,2.2),'Red',.08)
for dx in [-.45,.45]:
    beam('Barn door brace',(16+dx-.37,10.60,.3),(16+dx+.37,10.60,2.05),.045,'Cream')
    beam('Barn door brace',(16+dx+.37,10.59,.3),(16+dx-.37,10.59,2.05),.045,'Cream')
for x,y in [(12.4,12),(12.8,13),(19.3,12.7),(-10.4,1.6)]:
    barrel(x,y)
    box('Supply crate',(x+.65,y,.33),(.6,.6,.63),'WoodLight',.035)
    for z in [.13,.53]:box('Crate band',(x+.65,y-.32,z),(.66,.07,.05),'Wood',.008)

# Entrance arbor, fence gardens, stone threshold and welcome sign.
for x in [-1.65,1.65]:
    box('Entry post',(x,-12.6,1.3),(.22,.24,2.6),'Wood',.045)
beam('Entry lintel',(-1.9,-12.6,2.6),(1.9,-12.6,2.6),.13,'Wood')
box('Welcome sign',(0,-12.64,2.32),(2.5,.14,.46),'Cream',.07)
for x in [-1.7,1.7]:
    for j in range(5):
        ellipsoid('Arbor ivy',(x+math.sin(j)*.14,-12.6,1.5+j*.26),(.25,.23,.22),'LeafLight',True)
fence_segment((-2.5,-12.5),(-9,-12.5))
fence_segment((2.5,-12.5),(8,-12.5))
fence_segment((-19.8,-8),(-19.8,5))
fence_segment((-10,9.3),(10,9.3))

# Consolidate nearby geometry by material: manageable Unity hierarchy/draw calls.
groups={}
for o in list(bpy.context.scene.objects):
    if o.type!='MESH':continue
    key=(o.data.materials[0].name,int((o.location.x+30)//10),int((o.location.y+25)//10))
    groups.setdefault(key,[]).append(o)
for (mat,gx,gy),objects in groups.items():
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:o.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    if len(objects)>1:
        bpy.ops.object.join()
    bpy.context.object.name=f'{mat}_sector_{gx}_{gy}'
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
bpy.ops.export_scene.fbx(filepath=str(OUT/'MeadowVillage.fbx'),use_selection=True,
    axis_forward='-Z',axis_up='Y',use_space_transform=True,bake_space_transform=False,
    apply_unit_scale=True,object_types={'MESH'},bake_anim=False,add_leaf_bones=False)
(OUT/'palette.json').write_text(json.dumps({'materials':[{'name':'Meadow_'+n,'color':[*[int(h[i:i+2],16)/255 for i in (1,3,5)],1]} for n,h in PALETTE.items()]},indent=2),encoding='utf-8')

# Save a genuinely editable Blender scene with a useful overview camera.
bpy.ops.object.camera_add(location=(35,-46,38))
cam=bpy.context.object
cam.name='Meadow overview'
cam.rotation_euler=(Vector((0,2,0))-cam.location).to_track_quat('-Z','Y').to_euler()
cam.data.type='ORTHO';cam.data.ortho_scale=68
bpy.context.scene.camera=cam
bpy.ops.object.light_add(type='AREA',location=(-10,-15,30))
bpy.context.object.data.energy=6500
bpy.context.object.data.shape='DISK';bpy.context.object.data.size=25
bpy.context.scene.world.color=(.3,.35,.4)
bpy.context.scene.render.engine='CYCLES'
bpy.context.scene.cycles.samples=32
bpy.context.scene.render.resolution_x=1600;bpy.context.scene.render.resolution_y=1000
bpy.context.scene.render.resolution_percentage=100
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'MeadowVillage.blend'))
print(json.dumps({'objects':len(groups),'vertices':sum(len(o.data.vertices) for o in bpy.context.scene.objects if o.type=='MESH'),'fbx':str(OUT/'MeadowVillage.fbx')}))
