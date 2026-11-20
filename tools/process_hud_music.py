"""APIMart music HUD atlas extraction. Preserve existing .meta GUIDs and non-HUD art.
Raw sheets are immutable. Run after all eleven receipts are completed.
"""
from pathlib import Path
import json,hashlib,shutil,argparse
import numpy as np
from PIL import Image,ImageDraw
from process_hud_v2 import fit,stretch_panel,RESAMPLE
from ui_cutout import extract
R=Path(__file__).resolve().parents[1]
SOURCE=R/"_Inbox/hud-music-20261118"
OUT=R/"Assets/_Game/Res/Texture/UI"
STAGE=SOURCE/"processed"
FRAMES={"weapon_rail","portrait_frame","minimap_bezel","hp_track_frame","xp_track_frame","boss_track_frame","timer_plate"}
ICONS={"map_player_heading","map_enemy","map_chest","pause_glyph","weapon_swap","read_cursor","operator_portrait","utility_placeholder"}
SOLID={"weapon_rail","attachment_socket","pause_button_base","item_slot","hp_track_frame","xp_track_frame","boss_track_frame","timer_plate","operator_portrait","map_chest"}
SMALL={"map_player_heading","map_enemy","map_chest","pause_glyph","weapon_swap","read_cursor","utility_placeholder"}
ALIASES={n:"hud_music_"+n for n in ["hp_fill","xp_fill","boss_fill","crosshair"]}
def strip(im,size,end,source_fraction=.12):
 """Compress authored ornamental endcaps into the existing nine-slice border."""
 w,h=im.size;s=max(1,round(w*source_fraction));out=Image.new("RGBA",size)
 for a,b,c,d in [(0,s,0,end),(s,w-s,end,size[0]-end),(w-s,w,size[0]-end,size[0])]:
  out.alpha_composite(im.crop((a,0,b,h)).resize((d-c,size[1]),RESAMPLE),(c,0))
 return out
def arc(fill):
 """Register both generated arc layers to the current right-side 90 degree meter."""
 scale=4;n=256*scale;y,x=np.mgrid[:n,:n]/scale
 rad=np.hypot(x-128,y-128);angle=np.abs(np.arctan2(y-128,x-128))
 a=np.zeros((n,n,4),dtype=np.uint8)
 if fill:
  mask=(rad>=99)&(rad<=109)&(angle<=np.pi/4)
  a[:,:,:3]=(92,217,217);a[rad>106,:3]=(170,242,230);a[:,:,3]=mask*255
 else:
  mask=(rad>=96)&(rad<=112)&(angle<=np.pi/4)
  a[:,:,:3]=(244,212,156);inner=(rad>=99)&(rad<=109)
  a[inner,:3]=(38,51,71);a[:,:,3]=mask*255
 return Image.fromarray(a,"RGBA").resize((256,256),RESAMPLE)
def crop_cell(raw,job,i):
 cols,rows=job["grid"];c=i%cols;r=i//cols
 bounds=(round(c*raw.width/cols),round(r*raw.height/rows),round((c+1)*raw.width/cols),round((r+1)*raw.height/rows))
 # Reviewed art slightly crosses its mathematical cell, but never another component.
 if job["name"]=="01_frames" and r==0:
  edges=[0,720,1365,2048];bounds=(edges[c],0,edges[c+1],1024)
 if job["name"]=="05_instruments":
  edges=[0,730,1365,2048];bounds=(edges[c],r*1024,edges[c+1],(r+1)*1024)
 if job["name"]=="07_states" and r==0:
  bounds=(0,0,1080,1024) if c==0 else (1080,0,2048,1024)
 return raw.crop(bounds),bounds
def main():
 parser=argparse.ArgumentParser();parser.add_argument("--only",nargs="+");args=parser.parse_args()
 selected=set(args.only or [])
 overrides=SOURCE/"user-overrides"
 plan=json.loads((SOURCE/"generation-plan.json").read_text(encoding="utf-8"))
 for j in plan["jobs"]:
  state=json.loads((SOURCE/"receipts"/(j["name"]+".json")).read_text(encoding="utf-8"))
  p=SOURCE/"raw"/(j["name"]+".png")
  assert state["status"]=="completed" and hashlib.sha256(p.read_bytes()).hexdigest()==state["sha256"],j["name"]
 STAGE.mkdir(exist_ok=True)
 previous=json.loads((SOURCE/"processing-report.json").read_text(encoding="utf-8")) if selected else {"components":[]}
 rows=[];images={};catalog=json.loads((R/"tools/hud-v2-catalog.json").read_text())
 def save(key,category,im,source,cell,bounds=None):
  if key=="hud_attachment_socket":return
  if selected and key not in selected:return
  assert im.mode=="RGBA" and im.getchannel("A").getextrema()[1]==255,key
  dest=OUT/category/(key+".png")
  override=overrides/(key+".png")
  if override.exists():
   shutil.copy2(override,dest);shutil.copy2(override,STAGE/(key+".png"));source="user-overrides/"+override.name;cell=None
  else:im.save(dest);im.save(STAGE/(key+".png"))
  if category!="Icon/Items":catalog[key]=category
  rows.append(dict(name=key,category=category,size=im.size,source=source,cell=cell,crop=bounds,sha256=hashlib.sha256(dest.read_bytes()).hexdigest()))
 for job in plan["jobs"]:
  raw=Image.open(SOURCE/"raw"/(job["name"]+".png")).convert("RGB")
  for i,name in enumerate(job["components"]):
   cell,bounds=crop_cell(raw,job,i);im=extract(cell,name in SOLID)
   if job["name"].endswith("_notes"):
    base=images["attachment_socket"].copy()
    # Readability at 62px: large musical silhouette, leave lower space for existing slot number.
    glyph=fit(im,256,48)
    base.alpha_composite(glyph,(0,-9))
    im=base;key=name;cat="Icon/Items"
   else:
    key=ALIASES.get(name,"hud_"+name)
    cat="Frame" if name in FRAMES else "Icon" if name in ICONS or name.startswith("gun_") else "Widget"
    if (overrides/(key+".png")).exists():im=Image.open(overrides/(key+".png")).convert("RGBA")
    elif name in {"portrait_frame","minimap_bezel"}:im=fit(im,256,4)
    elif name=="operator_portrait":im=fit(im,512,44)
    elif name.endswith("track_frame"):pass # Full native ornament width; slice scaling is set in Unity.
    elif name in ["hp_fill","xp_fill","boss_fill"]:
     # Bake at the actual fill aspect so Filled mode does not stretch rounded endcaps.
     width,height,source_h,insets={"hp_fill":(470,28,85,(84,15,84,14)),
       "xp_fill":(430,22,80,(61,13,61,14)),"boss_fill":(530,28,91,(93,14,93,14))}[name]
     scale=height/source_h;l,b,r,t=insets
     size=(round((width-(l+r)*scale)*4),round((height-(t+b)*scale)*4))
     im=strip(im,size,size[1]//2,im.height/(2*im.width))
    elif name=="weapon_rail":im=strip(im,(512,96),46,.13)
    elif name=="timer_plate":im=strip(im,(256,80),24,.18)
    elif name.startswith("ammo_arc_"):im=arc(name.endswith("fill"))
    else:im=fit(im,512 if name=="operator_portrait" else 128 if name in SMALL or name=="crosshair" else 256,4)
   images[name]=im;save(key,cat,im,job["name"],i,bounds)
 # Dedicated badge mask follows the inner portrait ring; the minimap mask stays unchanged.
 badge=Image.new("RGBA",(1024,1024))
 ImageDraw.Draw(badge).ellipse(((127.5985-95)*4,(128.0855-95)*4,(127.5985+95)*4,(128.0855+95)*4),fill="white")
 badge=badge.resize((256,256),RESAMPLE)
 save("hud_music_badge_mask","Widget",badge,"derived-inner-circle",None)
 # HUD-exclusive overlays; leave shared menu/inventory backgrounds unchanged.
 for key,size,edge,cat in [("hud_music_resume",(256,96),14,"Widget"),("hud_music_banner",(512,256),20,"Frame")]:
  im=stretch_panel(images["item_slot"],size,edge)
  save(key,cat,im,"01_frames",5)
 (R/"tools/hud-v2-catalog.json").write_text(json.dumps(catalog,indent=2)+"\n",encoding="utf-8")
 if selected:
  updates={r["name"]:r for r in rows}
  rows=[updates.pop(r["name"],r) for r in previous["components"]]+list(updates.values())
 rows=[r for r in rows if r["name"]!="hud_attachment_socket"]
 # Integrity + transparency checks catch black rectangular backgrounds before import.
 for row in rows:
  p=STAGE/(row["name"]+".png");im=Image.open(p)
  assert im.getchannel("A").getbbox(),p
  if row["category"]=="Icon/Items":
   a=np.array(im)[:,:,3];assert a[0,0]==0 and a[-1,-1]==0 and a[128,128]>250,p
 for name in ["hud_portrait_frame","hud_minimap_bezel","hud_attachment_read_ring"]:
  im=Image.open(STAGE/(name+".png"));assert im.getpixel((im.width//2,im.height//2))[3]==0,name
 report=dict(components=rows,raw_hashes={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (SOURCE/"raw").glob("*.png")},notes=["Existing GUIDs retained; user replacement aspect/circle alignment and bar content insets updated","Ammo arc geometrically registered at 256x256 pivot128 radius96..112; fill99..109","New HUD-only fills, reticle and overlays; other shared panels unchanged"])
 (SOURCE/"processing-report.json").write_text(json.dumps(report,indent=2)+"\n",encoding="utf-8")
 # Contact sheet for actual processed alpha inspection.
 tiles=rows;w=1024;h=((len(tiles)+7)//8)*150
 sheet=Image.new("RGB",(w,h),(26,36,50));d=ImageDraw.Draw(sheet)
 for i,row in enumerate(tiles):
  im=Image.open(STAGE/(row["name"]+".png"));im.thumbnail((118,118),RESAMPLE)
  x=(i%8)*128;y=(i//8)*150;sheet.paste(im,(x+(128-im.width)//2,y+(120-im.height)//2),im)
  d.text((x+3,y+123),row["name"].removeprefix("hud_")[:20],fill=(245,218,173))
 sheet.save(SOURCE/"processed-contact.png")
 print("Processed",len(rows),"sprites; raw hashes and transparency verified. Existing metas untouched.")
if __name__=="__main__":main()
