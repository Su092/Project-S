"""Build the theatre dressing-room set in metres. Run with Blender in background mode."""
from pathlib import Path
from math import pi, sin, cos
import sys
import bpy
from mathutils import Vector

root = Path(__file__).resolve().parents[1]
out = root / "Assets/_Game/Resources/Backstage"
source = root / "_Inbox/backstage"
out.mkdir(parents=True, exist_ok=True)
source.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = "METRIC"
gold, brass, deep = "D9AA60", "85603C", "132334"
wood, trim, red, bone, light = "593937", "875943", "823446", "F2DDB5", "FFD993"
steel, black = "456477", "23232E"
group = None

def asset(name):
    global group
    group = bpy.data.collections.new(name)
    scene.collection.children.link(group)

def finish(name, color):
    obj = bpy.context.object
    obj.name = "SM_" + name
    for coll in list(obj.users_collection):
        coll.objects.unlink(obj)
    group.objects.link(obj)
    mat = bpy.data.materials.get("MAT_" + color) or bpy.data.materials.new("MAT_" + color)
    mat.diffuse_color = tuple(int(color[i:i+2], 16)/255 for i in (0,2,4)) + (1,)
    obj.data.materials.append(mat)
    return obj

def box(name, pos, size, color, bevel=.025):
    bpy.ops.mesh.primitive_cube_add(size=1, location=pos)
    obj = finish(name, color)
    obj.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        mod = obj.modifiers.new("Edge catchlight","BEVEL")
        mod.width = min(bevel, min(size)*.24)
        mod.segments = 2
        obj.modifiers.new("Face normals","WEIGHTED_NORMAL")
    return obj

def oval(name, pos, scale, color):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=10, location=pos)
    obj = finish(name,color)
    obj.scale = scale
    return obj

def cone(name, pos, base, top, height, color, sides=16):
    bpy.ops.mesh.primitive_cone_add(vertices=sides,radius1=base,radius2=top,depth=height,location=pos)
    return finish(name,color)

def rod(name, a, b, width, color):
    a,b = Vector(a),Vector(b)
    obj = cone(name,(a+b)/2,width,width,(b-a).length,color,12)
    obj.rotation_euler = (b-a).to_track_quat('Z','Y').to_euler()
    return obj

asset("OvertureNote")
for x,z in [(-.43,0),(.42,.16)]:
    oval("Notehead",(x,0,z),(.32,.18,.22),gold).rotation_euler.y=-.32
    rod("Stem",(x+.22,0,z+.08),(x+.22,0,z+1.24),.068,gold)
beam = box("DoubleBeam",(.215,0,1.32),(.98,.14,.25),gold)
beam.rotation_euler.y=-.18
asset("Salon")
box("Parquet",(0,0,-.10),(12,12,.2),wood)
box("Runner",(0,0,.013),(2.25,11.8,.025),red)
for x in [-1.07,1.07]:
    box("Selvedge",(x,0,.031),(.025,11.8,.012),gold)
for i in range(4):
    def wall(name,x,y,z,w,d,h,c):
        a=i*pi/2
        obj=box(name,(x*cos(a)-y*sin(a),x*sin(a)+y*cos(a),z),(w,d,h),c)
        obj.rotation_euler.z=a
    wall("Wall",0,6,2.35,12.2,.18,4.7,deep)
    wall("Wainscot",0,5.88,.52,12,.12,1.04,wood)
    for z in [.12,1.05,3.97,4.38]:
        wall("Cornice",0,5.80,z,12,.13,.065,brass)
    for x in [-5.25,-3.5,-1.75,0,1.75,3.5,5.25]:
        wall("Flute",x,5.83,2.5,.075,.12,2.9,brass)
    for x in [-4.4,4.4]:
        wall("SconcePlate",x,5.70,2.56,.37,.17,.68,brass)
        wall("OpalShade",x,5.53,2.69,.23,.19,.39,light)
# A stepped ceiling rose with suspended opal lights over the central aisle.
for y in [-2.7,2.8]:
    for z,r,h,col in [(4.59,.79,.12,brass),(4.48,.60,.11,wood),(4.39,.46,.085,gold)]:
        cone("CeilingRose",(0,y,z),r,r*.92,h,col,32)
    for j in range(6):
        angle=j*pi/3
        x=.51*cos(angle)
        yy=y+.51*sin(angle)
        rod("Pendant",(x,yy,4.55),(x,yy,4.23),.018,brass)
        oval("OpalDrop",(x,yy,4.18),(.075,.075,.13),light)
for x in [-4.85,-2.65,2.65,4.85]:
    box("FloorBorder",(x,0,.008),(.018,11.6,.012),trim,0)
for side in [-1,1]:
    for row in range(9):
        y=-5.35+row*1.32
        for col in range(3):
            x=side*(1.92+col*1.51)
            box("Marquetry",(x,y+(.43 if col%2 else 0),.009),(1.46,.018,.014),trim,0)
box("Canopy",(0,0,4.74),(12,12,.12),deep)
asset("StageDoor")
box("Portal",(0,0,1.58),(2.48,.32,3.16),brass)
for side in [-1,1]:
    x=side*.55
    box("WalnutLeaf",(x,-.2,1.53),(1.06,.18,2.94),wood)
    for z,h in [(1.95,1.65),(.53,.69)]:
        box("RaisedPanel",(x,-.30,z),(.84,.08,h),trim)
        box("LacquerField",(x,-.36,z),(.71,.045,h-.15),red)
    rod("Pull",(side*.17,-.43,1.15),(side*.17,-.43,1.43),.037,gold)
for i in range(3):
    box("PortalCrown",(0,0,3.15+i*.09),(2.72-i*.30,.44,.12),gold if i==1 else wood)
# Tied velvet legs frame the entrance. Layered folds narrow at the brass tie.
for side in [-1,1]:
    for fold in range(6):
        x=side*(1.40+fold*.105)
        dx=side*(fold-2.5)*.048
        top=rod("VelvetUpper",(x+dx,-.03,3.47),(x-dx,-.09,1.30),.119,
                [red,"6C2F42","9B4557"][fold%3])
        rod("VelvetLower",(x-dx,-.09,1.29),(x,-.04,.09),.102,
            [red,"6C2F42","9B4557"][fold%3])
    box("Tieback",(side*1.66,-.16,1.30),(.42,.12,.077),gold)
    rod("TasselCord",(side*1.62,-.21,1.29),(side*1.59,-.21,1.07),.018,gold)
    cone("Tassel",(side*1.59,-.21,1.04),.045,.022,.10,gold)
for j in range(-3,4):
    blade=box("DoorAnthem",(j*.10,-.12,3.48),(.067,.10,.46-abs(j)*.045),gold)
    blade.rotation_euler.y=j*.18
asset("DressingMirror")
box("Glass",(0,0,1.42),(1.62,.13,2.54),steel)
for x in [-.91,.91]:
    box("GiltStile",(x,0,1.46),(.19,.25,2.8),brass)
    for z in [.34,.85,1.36,1.87,2.38,2.8]:
        oval("VanityBulb",(x,-.15,z),(.072,.06,.072),light)
for z in [.12,2.82]:
    box("GiltRail",(0,0,z),(2.02,.26,.17),gold)
for i in range(-3,4):
    blade=box("Palmette",(i*.118,0,2.97),(.09,.12,.48-abs(i)*.047),gold)
    blade.rotation_euler.y=i*.18
box("MirrorFoot",(0,-.05,.07),(2.18,.68,.14),wood)
asset("ScoreDesk")
box("WritingSurface",(0,0,.99),(2.18,.98,.12),trim)
box("Apron",(0,0,.85),(1.95,.75,.18),wood)
for x in [-.89,.89]:
    for y in [-.32,.32]:
        rod("TurnedLeg",(x,y,.07),(x,y,.89),.066,wood)
        cone("Ferrule",(x,y,.065),.08,.07,.11,brass)
for s in [-1,1]:
    leaf=box("Manuscript",(s*.25,-.10,1.075),(.50,.60,.05),bone)
    leaf.rotation_euler.y=s*.035
    for staff in range(3):
        for j in range(5):
            y=-.32+staff*.17+j*.018
            box("Staff",(s*.25,y,1.111),(.40,.005,.005),brass,0)
        for k in range(4):
            oval("Ink",(s*.25-.14+k*.09,y-.035+(k%2)*.02,1.117),(.016,.012,.005),black)
rod("LampStem",(.8,.22,1.05),(.8,.22,1.44),.035,brass)
cone("LampShade",(.8,.22,1.47),.18,.09,.18,light)

# Hand-cranked gramophone beside the open score.
cx,cy=-.77,.04
box("GramophonePlinth",(cx,cy,1.077),(.59,.54,.054),wood)
box("GramophoneCabinet",(cx,cy,1.17),(.54,.49,.15),"68382B")
box("GramophoneInlay",(cx,cy,1.246),(.56,.51,.016),gold)
box("GramophoneDeck",(cx,cy,1.267),(.58,.53,.032),wood)
box("GramophoneEscutcheon",(cx,cy-.248,1.17),(.16,.015,.05),brass)
cone("Turntable",(cx,cy-.02,1.294),.209,.209,.024,brass,48)
cone("ShellacRecord",(cx,cy-.02,1.313),.204,.204,.013,"191B23",64)
for radius in [.144,.164,.184]:
    bpy.ops.mesh.primitive_torus_add(major_segments=64,minor_segments=6,
        location=(cx,cy-.02,1.319),major_radius=radius,minor_radius=.0013)
    finish("RecordGroove","343746")
cone("RecordLabel",(cx,cy-.02,1.321),.063,.063,.003,red,32)
cone("RecordSpindle",(cx,cy-.02,1.33),.012,.009,.018,gold)
pivot=(cx+.205,cy+.18,1.40)
knuckle=(cx+.188,cy+.04,1.405)
pickup=(cx+.092,cy-.067,1.37)
cone("TonearmSocket",(pivot[0],pivot[1],1.34),.04,.03,.11,brass)
rod("Tonearm",pivot,knuckle,.017,gold)
rod("Tonearm",knuckle,pickup,.016,gold)
oval("Soundbox",pickup,(.034,.025,.035),brass)
rod("Stylus",pickup,(pickup[0]-.012,pickup[1],1.322),.004,black)
rod("WindingCrank",(cx-.24,cy,1.17),(cx-.32,cy,1.17),.012,brass)
rod("WindingCrank",(cx-.32,cy,1.17),(cx-.32,cy-.075,1.115),.012,brass)
rod("CrankGrip",(cx-.32,cy-.075,1.115),(cx-.38,cy-.075,1.115),.019,black)

# Sweep both faces of the bell from the elbow to a ten-petal rolled lip.
profile=[
    ((-.565,.22,1.39),.030), ((-.55,.30,1.43),.033),
    ((-.54,.35,1.52),.038), ((-.55,.35,1.61),.048),
    ((-.58,.31,1.70),.065), ((-.62,.25,1.77),.092),
    ((-.66,.18,1.83),.14), ((-.70,.11,1.89),.216),
    ((-.74,.04,1.95),.32), ((-.78,-.03,2.01),.457)]
centres=[Vector(p) for p,r in profile]
rings=[]
for i,(p,radius) in enumerate(profile):
    tangent=(centres[min(i+1,len(profile)-1)]-centres[max(0,i-1)]).normalized()
    across=tangent.cross(Vector((1,0,0))).normalized()
    rings.append((centres[i],radius,across,tangent.cross(across)))
end,radius,across,up=rings[-1]
axis=across.cross(up)
lip=[(end+axis*.009,radius+.008,across,up),(end+axis*.016,radius+.002,across,up)]
inside=[(p,r-.006,u,v) for p,r,u,v in reversed(rings)]
vertices=[]
faces=[]
sides=80
for p,r,u,v in rings+lip+inside:
    for j in range(sides):
        angle=2*pi*j/sides
        flute=1+.072*(r/radius)**.7*cos(10*angle)
        vertices.append(p+(u*cos(angle)+v*sin(angle))*r*flute)
ring_count=len(vertices)//sides
for i in range(ring_count):
    for j in range(sides):
        k=(j+1)%sides
        next_ring=(i+1)%ring_count
        faces.append((i*sides+j,i*sides+k,next_ring*sides+k,next_ring*sides+j))
mesh=bpy.data.meshes.new("FlutedBrassBell")
mesh.from_pydata(vertices,[],faces)
mesh.update()
bell=bpy.data.objects.new("SM_GramophoneHorn",mesh)
group.objects.link(bell)
for color in ["BA823D","DCA859","EAC170","FFE3A1"]:
    mat=bpy.data.materials.new("MAT_"+color)
    mat.diffuse_color=tuple(int(color[i:i+2],16)/255 for i in (0,2,4))+(1,)
    mat.metallic=.55
    mat.roughness=.28
    mesh.materials.append(mat)
for face in mesh.polygons:
    band,sector=divmod(face.index,sides)
    if len(rings)-1 <= band <= len(rings)+1:
        face.material_index=3
    else:
        face.material_index=(1,2,2,1,0,0,1,1)[sector%8]
    face.use_smooth=True

palettes=["954052","336A71","48648A","796087","938248","4D7463"]
for i,coat in enumerate(palettes):
    asset("Performer"+str(i+1))
    for s in [-1,1]:
        x=s*.115
        rod("Stocking",(x,0,.14),(x,0,.76),.084,black)
        oval("Shoe",(x,-.053,.11),(.115,.162,.094),black)
        rod("Sleeve",(s*.22,0,1.19),(s*.30,-.028,.91),.09,coat)
        oval("Glove",(s*.30,-.038,.83),(.074,.073,.105),bone)
    cone("Tailcoat",(0,0,.91),.29,.19,.46,coat,12).scale.y=.75
    oval("Bodice",(0,0,1.15),(.258,.177,.27),coat)
    for s in [-1,1]:
        rod("Lapel",(s*.14,-.14,1.32),(0,-.182,1.10),.027,gold)
    for z in [.98,1.05,1.12]:
        oval("Button",(0,-.18,z),(.021,.018,.021),gold)
    oval("Face",(0,-.012,1.57),(.204,.165,.235),bone)
    oval("Wig",(0,.054,1.65),(.222,.165,.195),black)
    for s in [-1,1]:
        oval("Domino",(s*.088,-.15,1.6),(.079,.027,.05),black)
        oval("Eye",(s*.086,-.177,1.6),(.018,.01,.021),gold)
    oval("Nose",(0,-.173,1.536),(.032,.031,.042),bone)
    rod("Mouth",(-.031,-.156,1.472),(.031,-.156,1.477),.008,wood)
    for s in [-1,1]:
        oval("Epaulette",(s*.214,0,1.276),(.102,.117,.049),gold)
        for j in range(3):
            rod("Fringe",(s*(.20+j*.029),-.04,1.26),(s*(.20+j*.029),-.04,1.20),.008,brass)
    if i in (0,2):
        cone("Brim",(0,0,1.79),.25,.25,.035,black)
        cone("Crown",(0,0,1.88),.161,.18 if i==0 else .09,.23 if i==0 else .19,coat)
    elif i in (1,4):
        for s in [-1,1]:
            oval("SideCurl",(s*.195,.03,1.56),(.092,.124,.19 if i==1 else .098),coat)
        cone("Diadem",(0,0,1.77),.175,.14,.092,gold)
        for k in range(3 if i==1 else 5):
            cone("TiaraPoint",((k-(1 if i==1 else 2))*.055,-.10,1.87),.038,0,.15,gold)
    else:
        cap=oval("Beret",(0,0,1.79),(.235,.18,.104),coat)
        cap.rotation_euler.y=.20 if i==3 else -.23
        for j in range(1 if i==3 else 3):
            plume=oval("Feather",(.14+j*.03,.015,1.91+j*.02),(.045,.022,.158),gold)
            plume.rotation_euler.y=.48+j*.15

bpy.ops.wm.save_as_mainfile(filepath=str(source/"Backstage.blend"))
export_names = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
for coll in bpy.data.collections:
    if not coll.objects or (export_names and coll.name not in export_names):
        continue
    bpy.ops.object.select_all(action='DESELECT')
    for obj in coll.objects:
        assert obj.data.validate() is False, obj.name
        obj.select_set(True)
    bpy.ops.export_scene.fbx(filepath=str(out/(coll.name+".fbx")),use_selection=True,
                             axis_forward='-Z',axis_up='Y',apply_scale_options='FBX_SCALE_ALL',
                             bake_space_transform=True,add_leaf_bones=False,bake_anim=False)
