"""Deterministic cutout/import of approved APIMart upgrade UI sheets. Originals remain in _Inbox."""
from pathlib import Path
from PIL import Image, ImageFilter
import numpy as np
import json
root=Path(__file__).resolve().parents[1]
dst=root/"Assets/_Game/Res/Texture/UI"
catalog={}
def cut(im,threshold=12):
    a=np.array(im.convert("RGBA")).astype(float)
    peak=a[:,:,:3].max(2)
    alpha=np.clip((peak-threshold)/32,0,1)*a[:,:,3]/255
    a[:,:,3]=alpha*255
    out=Image.fromarray(a.astype("uint8"))
    box=out.getchannel("A").point(lambda x:255 if x>25 else 0).getbbox()
    return out.crop(box)
def save(im,name,cat):
    p=dst/("Icon/Items" if cat=="Items" else cat)/(name+".png")
    p.parent.mkdir(parents=True,exist_ok=True);im.save(p);catalog[name]=cat
sheet=Image.open(root/"_Inbox/upgrade-ui-apimart-v2/01-frames-four-colors.png")
w,h=sheet.size
for i,color in enumerate(["green","blue","gold","red"]):
    x=i%2*w//2;y=i//2*h//2
    im=cut(sheet.crop((x+12,y+12,x+w//2-12,y+h//2-12)))
    im=im.resize((280,512),Image.Resampling.LANCZOS)
    save(im,"offer_frame_"+color,"Frame")
sheet=Image.open(root/"_Inbox/upgrade-ui-apimart-v8/02-gold-notes-clean.png")
w,h=sheet.size
for i in range(6):
    x=i%3*w//3;y=i//3*h//2
    im=cut(sheet.crop((x+16,y+16,x+w//3-16,y+h//2-16)))
    im.thumbnail((128,160),Image.Resampling.LANCZOS)
    canvas=Image.new("RGBA",(160,192));canvas.alpha_composite(im,((160-im.width)//2,(192-im.height)//2))
    save(canvas,"offer_note_"+str(i),"Icon")
    halo=Image.new("RGBA",canvas.size,(255,205,85,0))
    a=canvas.getchannel("A")
    strength=np.asarray(a.filter(ImageFilter.GaussianBlur(3)),dtype=float)*.65+np.asarray(a.filter(ImageFilter.GaussianBlur(8)),dtype=float)*1.25
    halo.putalpha(Image.fromarray(np.clip(strength,0,255).astype("uint8")))
    save(halo,"offer_note_glow_"+str(i),"Icon")
im=Image.open(root/"_Inbox/upgrade-ui-apimart-v7/03-gold-staff-clean-core.png")
im=cut(im.crop((12,12,im.width-12,im.height-12)),8)
im.thumbnail((2048,700),Image.Resampling.LANCZOS)
canvas=Image.new("RGBA",(im.width+32,im.height+32));canvas.alpha_composite(im,(16,16))
save(canvas,"offer_staff","Widget")
glow=Image.new("RGBA",canvas.size,(255,196,64,0))
a=canvas.getchannel("A")
strength=np.asarray(a.filter(ImageFilter.GaussianBlur(5)),dtype=float)*1.1+np.asarray(a.filter(ImageFilter.GaussianBlur(16)),dtype=float)*1.8
glow.putalpha(Image.fromarray(np.clip(strength,0,255).astype("uint8")))
save(glow,"offer_staff_glow","Widget")
from music_staff_profile import write_profile
write_profile(canvas,root/"Assets/_Game/View/MusicOfferArt.cs")
for p in sorted((dst/"Icon/Items").glob("item_*.png")):
    im=Image.open(p).convert("RGBA");a=np.array(im).astype(float);h,w=a.shape[:2]
    yy,xx=np.mgrid[:h,:w];r=np.sqrt(((xx-(w-1)/2)/w)**2+((yy-(h-1)/2)/h)**2)
    peak=a[:,:,:3].max(2)
    a[:,:,3]*=np.clip((peak-80)/55,0,1)*np.clip((.415-r)*100,0,1)
    # Preserve common canvas/scale; remove baked circular plate and perimeter.
    save(Image.fromarray(a.astype("uint8")),"offer_"+p.stem,"Items")
path=root/"tools/hud-v2-catalog.json";d=json.loads(path.read_text(encoding="utf-8-sig"));d.update(catalog);path.write_text(json.dumps(d,indent=2)+"\n",encoding="utf-8")
path=root/"Assets/_Game/View/UiAssets.cs";s=path.read_text(encoding="utf-8-sig")
entries="\n".join('            { "'+k+'", "'+v+'" },' for k,v in catalog.items() if '{ "'+k+'",' not in s)
s=s.replace('            // Music HUD:',entries+'\n            // Music HUD:');path.write_text(s,encoding="utf-8")
manifest={"sources":["upgrade-ui-apimart-v2","upgrade-ui-apimart-v7","upgrade-ui-apimart-v8"],"outputs":catalog,"staffSize":canvas.size}
(root/"Logs/music-upgrade-assets.json").write_text(json.dumps(manifest,indent=2),encoding="utf-8")
print("Processed",len(catalog),"sprites; staff",canvas.size)
