from pathlib import Path
from PIL import Image,ImageDraw
import numpy as np
from ui_cutout import extract
import hashlib,json
root=Path(__file__).resolve().parents[1]
source=root/'_Inbox/score-controls-outlined-review-v3'
report=[]
for raw,cat,name,solid in [('close-outlined.png','Icon','score_close',False),('sort-outlined.png','Icon','score_sort',False),('slot-preview-rail.png','Frame','score_preview_rail',True)]:
 p=source/raw
 im=Image.open(p).convert('RGBA')
 if solid:
  # Retain the connected central backplate; discard disconnected canvas-edge artifacts.
  rgba=np.array(im)
  candidate=Image.fromarray((rgba[:,:,:3].max(axis=2)>20).astype('uint8'))
  candidate=candidate.copy()
  seed=(im.width//2,im.height//2)
  assert candidate.getpixel(seed)==1
  ImageDraw.floodfill(candidate,seed,128,thresh=0)
  rgba[np.array(candidate)!=128,3]=0
  im=Image.fromarray(rgba,'RGBA')
 art=extract(im,solid=solid)
 if solid:
  art.thumbnail((2040,500),Image.Resampling.LANCZOS)
  canvas=Image.new('RGBA',(art.width+8,art.height+8));canvas.alpha_composite(art,(4,4))
 else:
  art.thumbnail((500,500),Image.Resampling.LANCZOS)
  canvas=Image.new('RGBA',(512,512))
  canvas.alpha_composite(art,((512-art.width)//2,(512-art.height)//2))
 dest=root/f'Assets/_Game/Res/Texture/UI/{cat}/{name}.png'
 meta=Path(str(dest)+'.meta');before=meta.read_bytes() if meta.exists() else None
 canvas.save(dest)
 assert canvas.getpixel((0,0))[3]==0
 assert before is None or meta.read_bytes()==before
 if name=='score_sort':assert canvas.getpixel((256,256))[3]==0
 report.append({'source':str(p.relative_to(root)),'source_sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'destination':str(dest.relative_to(root)),'approved':True,'size':canvas.size,'meta_preserved':before is not None})
(root/'Logs/score-outlined-approved-import.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False,indent=2))
