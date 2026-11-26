"""Export music-themed low-poly props in metres with flat colour material slots."""
from pathlib import Path
from math import pi, sin, cos
import bpy
import sys

root = Path(__file__).resolve().parents[1]
out = root / 'Assets/_Game/Resources/Orchestra'
out.mkdir(parents=True, exist_ok=True)
gold, ivory, wood, dark = 'F9BB55', 'FFF2BC', '843B38', '223247'
bpy.ops.wm.read_factory_settings(use_empty=True)

def finish(name, pos, scale, color):
    o = bpy.context.object
    o.name = name
    o.location = pos
    o.scale = scale
    m = bpy.data.materials.get('Tone_'+color) or bpy.data.materials.new('Tone_'+color)
    m.diffuse_color = tuple(int(color[i:i+2],16)/255 for i in (0,2,4))+(1,)
    o.data.materials.append(m)
    return o

def ell(name,pos,scale,color):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=6)
    return finish(name,pos,scale,color)

def block(name,pos,scale,color):
    bpy.ops.mesh.primitive_cube_add(size=2)
    return finish(name,pos,scale,color)

def cone(name,pos,r1,r2,h,color):
    bpy.ops.mesh.primitive_cone_add(vertices=20,radius1=r1,radius2=r2,depth=h)
    return finish(name,pos,(1,1,1),color)

def rim(name,pos,r,width,color):
    bpy.ops.mesh.primitive_torus_add(major_segments=24,minor_segments=5,major_radius=r,minor_radius=width)
    return finish(name,pos,(1,1,1),color)

def shell(x,z,tilt):
    a=ell('RosewoodShell',(x,0,z),(.32,.47,.13),wood)
    a.rotation_euler.y=tilt
    ell('BrassInlay',(x,-.03,z+.075),(.16,.28,.065),gold)
    rim('Cord',(x,.38,z),.12,.032,dark)

def dish():
    ell('HammeredSteel',(0,0,0),(.56,.56,.17),'398D96')
    rim('BronzeSeam',(0,0,0),.55,.025,gold)
    ell('CentralDome',(0,0,.15),(.14,.14,.07),gold)
    for i in range(7):
        a=2*pi*i/7
        ell('ToneField',(.35*cos(a),.35*sin(a),.115),(.11,.10,.035),'99DAD2')

def bell(x,z):
    cone('FlaredBell',(x,0,z),.27,.075,.34,gold)
    rim('RolledLip',(x,0,z-.17),.27,.026,ivory)
    ell('Clapper',(x,0,z-.19),(.067,.067,.07),dark)
    cone('Handle',(x,0,z+.30),.05,.063,.28,'344578')

def drum():
    cone('PinnedCylinder',(0,0,0),.25,.25,.74,'BA8043').rotation_euler.y=pi/2
    for x in [-.37,.37]:
        cone('EndPlate',(x,0,0),.29,.29,.06,gold).rotation_euler.y=pi/2
    for i in range(12):
        a=i*pi/3
        ell('Pin',(-.30+i*.055,.254*cos(a),.254*sin(a)),(.034,.04,.04),ivory)

def bow(accent,long=False):
    for sign in [-1,1]:
        b=block('BowLimb',(sign*.105,0,0),(.034,.66 if long else .48,.042),accent)
        b.rotation_euler.z=sign*.15
    ell('Frog',(0,-.39,0),(.14,.13,.075),gold)
    block('BrightString',(0,.07,0),(.02,.53,.026),ivory)

def make(id):
    if id=='halfbeat':
        shell(0,0,-.2)
    elif id in ('answer','castanets'):
        shell(-.22,.09,-.28)
        shell(.20,-.03,.30)
    elif id in ('ripple','handpan'): dish()
    elif id in ('bellmark','handbells'):
        bell(-.25 if id=='handbells' else 0,0)
        if id=='handbells': bell(.28,.13)
    elif id in ('barrelroll','musicbox'):
        drum()
        if id=='musicbox':
            block('WalnutCase',(0,0,-.25),(.52,.38,.12),wood)
            block('Lid',(0,.33,.08),(.5,.045,.32),dark).rotation_euler.x=-.22
            block('Crank',(.50,0,-.05),(.035,.18,.03),gold)
            ell('Knob',(.56,-.18,-.05),(.09,.04,.05),ivory)
    elif id in ('std','ap'): bow('63DDE9' if id=='ap' else gold,id=='ap')
    elif id in ('light','reed'):
        ell('Tongue',(0,0,0),(.16,.59,.045),gold)
        block('Ridge',(0,-.08,.044),(.035,.38,.024),ivory)
    elif id=='pellet':
        cone('DrumChip',(0,0,0),.24,.29,.14,wood) 
        rim('Hoop',(0,0,.07),.27,.026,gold)
    elif id=='grenade':
        for i in range(6):
            b=block('Bellows',(0,(i-2.5)*.10,0),(.34 if i%2 else .27,.055,.25 if i%2 else .20),wood if i%2 else gold)
        for y in [-.35,.35]: block('Cap',(0,y,0),(.34,.05,.25),dark)
    elif id=='saw':
        cone('Cymbal',(0,0,0),.52,.11,.14,gold)
        ell('Bell',(0,0,.08),(.12,.12,.09),ivory)
    elif id in ('mine','shock','bouncer'):
        dish()
        rim('Accent',(0,0,.16),.38,.035,{'mine':'F07766','shock':'689FFF','bouncer':'59F1C5'}[id])
    elif id=='missile':
        bow('F37B55')
        for x in [-.18,.18]: block('TuningFork',(x,.35,0),(.043,.34,.05),gold)
    elif id in ('trigger','timer','expire'):
        cone('PhraseScroll',(0,0,0),.20,.20,.72,ivory).rotation_euler.x=pi/2
        for i in range({'trigger':1,'timer':2,'expire':3}[id]):
            rim('Binding',(0,-.19+i*.18,0),.20,.038,'945390').rotation_euler.x=pi/2
    bpy.ops.object.select_all(action='SELECT')
    bpy.context.view_layer.objects.active=bpy.context.selected_objects[0]
    bpy.ops.object.join()
    obj=bpy.context.object
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    obj.name='Orchestra_'+id
    bpy.ops.export_scene.fbx(filepath=str(out/(id+'.fbx')),use_selection=True,add_leaf_bones=False,
                             bake_anim=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True)
    bpy.data.objects.remove(obj,do_unlink=True)

selection = set(sys.argv[sys.argv.index("--")+1:]) if "--" in sys.argv else set()
for key in 'std light pellet ap grenade halfbeat bouncer missile mine shock saw trigger timer expire answer ripple bellmark barrelroll reed castanets handpan handbells musicbox'.split():
    if not selection or key in selection:
        make(key)
