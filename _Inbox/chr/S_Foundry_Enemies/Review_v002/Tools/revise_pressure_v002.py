import shutil
from pathlib import Path
root=Path(__file__).parent
backup=root/'_RevisionBackups'/'pre_pressure_v002'
backup.mkdir(parents=True,exist_ok=True)
for name in ['PressureRunner','PressureRunner_Elite_TriplePressure']:
 if not (backup/name).exists():shutil.copytree(root/name,backup/name)
p=root/'build_enemies.py'
if not (backup/p.name).exists():shutil.copy2(p,backup/p.name)
s=p.read_text()
old="  b.part('PearVessel',(0,0,.83),(.43,.43,1),1,typ='lathe',profile=[(-.50,.18),(-.40,.28),(-.20,.40),(.14,.46),(.35,.40),(.46,.23)])"
new="""  # Load-bearing liner and three independently opening upper ceramic panels.
  b.part('PearVessel',(0,0,.83),(.43,.43,1),0,typ='lathe',profile=[(-.50,.18),(-.40,.27),(-.20,.38),(.14,.435),(.35,.375),(.46,.205)])
  b.part('LowerRefractory',(0,0,.83),(1,1,1),1,typ='lathe',profile=[(-.49,.18),(-.40,.28),(-.20,.40),(.015,.45)])
  for i in range(3):
   a=-math.pi/2+i*TAU/3
   bn=b.bone('pressure_panel_'+str(i),(.42*math.cos(a),.42*math.sin(a),.90))
   b.part('PressurePanel_'+str(i),(0,0,.83),(1,1,1),1,bn,'sector',start=a-TAU/6+.018,end=a+TAU/6-.018,profile=[(.035,.455),(.14,.46),(.35,.40),(.46,.23)],thick=.025)
  # Gauges are protected instruments, no barrel-like projection toward player.
  for i in range(3 if e else 1):
   a=(-math.pi/2 if i==0 else -math.pi/2+(-.55 if i==1 else .55))
   x=0 if i==0 else (-.49 if i==1 else .49)
   z=1.15 if i==0 else 1.08
   b.cyl('GaugeCase_'+str(i),(x,-.36 if i==0 else -.22,z),.082,.045,0,axis='Y',rot_z=a+math.pi/2)
   b.cyl('GaugeDial_'+str(i),(x,-.386 if i==0 else -.246,z),.066,.012,1,axis='Y',rot_z=a+math.pi/2)
   needle=b.box('GaugeNeedle_'+str(i),(x,-.396 if i==0 else -.256,z+.012),(.012,.012,.075),2)
   needle['rot_y']=-.5+i*.35
"""
assert old in s
s=s.replace(old,new)
old="    arm.pose.bones['valve'].location.z=.10*hold"
new="""    arm.pose.bones['valve'].location.z=.10*hold
    for i in range(3):
     bn='pressure_panel_'+str(i)
     if bn in arm.pose.bones:
      a=-math.pi/2+i*TAU/3
      opening=max(0,min((t-.18-i*.06)*2.5,1))
      arm.pose.bones[bn].location=(.045*math.cos(a)*opening,.045*math.sin(a)*opening,.025*opening)
    # Plant tripod feet wider during arming; this is a pose change, not root motion.
    for j,(n,hip,knee,foot) in enumerate(b.legs):
     arm.pose.bones[n+'_upper'].rotation_euler.y=.09*math.cos(TAU*j/3-math.pi/2)*hold"""
assert old in s;s=s.replace(old,new)
p.write_text(s)
print('Pressure v002 design and animation updated; backups:',backup)
