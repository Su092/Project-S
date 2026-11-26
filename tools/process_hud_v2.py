"""Reproducible HUD sheet extraction. Raw downloads stay immutable; existing metas stay intact."""
from pathlib import Path
import json, hashlib
import numpy as np
from PIL import Image, ImageDraw, ImageEnhance
ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/"_Inbox/hud-v2-20261118"
OUT=ROOT/"Assets/_Game/Res/Texture/UI"
STAGE=SOURCE/"processed"
RESAMPLE=Image.Resampling.LANCZOS
PLAN=json.loads((SOURCE/"generation-plan.json").read_text(encoding="utf-8"))
ALIASES={"crosshair":"crosshair","hp_fill":"bar_fill_hp","xp_fill":"bar_fill_xp","boss_fill":"bar_fill_boss"}
FRAMES={"weapon_rail","portrait_frame","minimap_bezel","hp_track_frame","xp_track_frame","boss_track_frame","timer_plate"}
ICONS={"map_player_heading","map_enemy","map_chest","pause_glyph","weapon_swap","read_cursor","operator_portrait","utility_placeholder"}
SOLID={"weapon_rail","attachment_socket","pause_button_base","item_slot","hp_track_frame","xp_track_frame","boss_track_frame","timer_plate","operator_portrait","map_chest"}
SMALL={"map_player_heading","map_enemy","map_chest","pause_glyph","weapon_swap","read_cursor","utility_placeholder"}
from ui_cutout import extract
def fit(im,size,pad=0):
 if isinstance(size,int):size=(size,size)
 im=im.copy();im.thumbnail((size[0]-pad*2,size[1]-pad*2),RESAMPLE)
 canvas=Image.new("RGBA",size);canvas.alpha_composite(im,((size[0]-im.width)//2,(size[1]-im.height)//2));return canvas
def stretch_panel(im,size,edge=18):
 base=im.resize((96,96),RESAMPLE);out=Image.new("RGBA",size)
 sx=[0,edge,96-edge,96];sy=sx;dx=[0,edge,size[0]-edge,size[0]];dy=[0,edge,size[1]-edge,size[1]]
 for y in range(3):
  for x in range(3):
   part=base.crop((sx[x],sy[y],sx[x+1],sy[y+1])).resize((dx[x+1]-dx[x],dy[y+1]-dy[y]),RESAMPLE)
   out.alpha_composite(part,(dx[x],dy[y]))
 return out
def main():
 STAGE.mkdir(exist_ok=True);catalog={};images={};rows=[]
 for job in PLAN["jobs"]:
  source=SOURCE/"raw"/(job["name"]+".png");raw=Image.open(source).convert("RGB");cols,rs=job["grid"]
  for idx,name in enumerate(job["components"]):
   c,r=idx%cols,idx//cols
   cell=raw.crop((round(c*raw.width/cols),round(r*raw.height/rs),round((c+1)*raw.width/cols),round((r+1)*raw.height/rs)))
   im=extract(cell,name in SOLID,job["name"]=="08_modifiers" or name in {"crosshair","reload_spinner"})
   if job["name"]=="08_modifiers":
    glyph=fit(im,256,48);base=images["attachment_socket"].resize((256,256),RESAMPLE);base.alpha_composite(glyph);im=base
    key="item_"+name;cat="Icon/Items"
   else:
    key=ALIASES.get(name,"hud_"+name)
    cat="Frame" if name in FRAMES else "Icon" if name in ICONS or name.startswith("gun_") else "Widget"
    if name in {"hp_track_frame","hp_fill","boss_track_frame","boss_fill"}:im=im.resize((512,40),RESAMPLE)
    elif name in {"xp_track_frame","xp_fill"}:im=im.resize((512,28),RESAMPLE)
    elif name=="weapon_rail":im=im.resize((512,96),RESAMPLE)
    elif name=="timer_plate":im=im.resize((256,80),RESAMPLE)
    elif name.startswith("ammo_arc_"):
     # Normalize both layers onto identical pivot/radii and crop to the right 90 degrees.
     im=im.resize((112,224),RESAMPLE);canvas=Image.new("RGBA",(256,256));canvas.alpha_composite(im,(128,16))
     yy,xx=np.mgrid[:256,:256];radius=np.sqrt((xx-128)**2+(yy-128)**2);angle=np.abs(np.arctan2(yy-128,xx-128))
     lo,hi=(96,112) if name.endswith("track") else (99,109)
     mask=(radius>=lo)&(radius<=hi)&(angle<=np.pi/4)
     # Reuse source texture colour; fill is ivory, track is steel. Exact geometry is shared.
     a=np.array(canvas);color=(125,146,165) if name.endswith("track") else (242,244,246)
     a[:,:,:3]=color;a[:,:,3]=np.uint8(mask)*255;im=Image.fromarray(a,"RGBA")
    else:im=fit(im,512 if name=="operator_portrait" else 128 if name in SMALL or name=="crosshair" else 256,4)
   images[name]=im
   if name in {"attachment_socket","read_cursor","weapon_selected_ring"}:continue
   dest=OUT/cat/(key+".png");dest.parent.mkdir(parents=True,exist_ok=True);im.save(dest);im.save(STAGE/(key+".png"))
   if cat!="Icon/Items":catalog[key]=cat
   rows.append({"name":key,"category":cat,"size":im.size,"source":job["name"],"cell":idx})
 # Generic masks and compatible replacements retain old GUIDs / non-HUD layout.
 mask=Image.new("RGBA",(256,256));ImageDraw.Draw(mask).ellipse((1,1,254,254),fill="white")
 mask.save(OUT/"Widget/hud_map_mask.png");catalog["hud_map_mask"]="Widget"
 replacements={
  "Common/panel":stretch_panel(images["item_slot"],(512,512),20),
  "Frame/card_frame":stretch_panel(images["item_slot"],(256,256),10),
  "Frame/card_inner":stretch_panel(images["pause_button_base"],(256,256),8),
  "Widget/btn_primary":stretch_panel(images["pause_button_base"],(256,96),14),
  "Widget/slot_empty":stretch_panel(images["item_slot"],(128,128),18),
  "Widget/slot_filled":stretch_panel(images["item_slot"],(64,64),6),
  "Icon/icon_frame":images["portrait_frame"].resize((128,128),RESAMPLE)}
 replacements["Widget/btn_primary_hover"]=ImageEnhance.Brightness(replacements["Widget/btn_primary"]).enhance(1.3)
 replacements["Widget/btn_danger"]=replacements["Widget/btn_primary"].copy()
 for name,im in replacements.items():im.save(OUT/(name+".png"))
 (ROOT/"tools/hud-v2-catalog.json").write_text(json.dumps(catalog,indent=2)+"\n",encoding="utf-8")
 for p in OUT.rglob("*.png"):
  if p.name.startswith("hud_") or p.name in [r["name"]+".png" for r in rows]:
   with Image.open(p) as im:
    assert im.mode=="RGBA" and im.getchannel("A").getextrema()[1]>0, p
 (SOURCE/"processing-report.json").write_text(json.dumps({"components":rows,"common_replacements":list(replacements),"raw_hashes":{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (SOURCE/"raw").glob("*.png")}},indent=2)+"\n",encoding="utf-8")
 print("Processed",len(rows),"components + map mask; replaced",len(replacements),"shared textures, metas untouched.")
if __name__=="__main__":main()
