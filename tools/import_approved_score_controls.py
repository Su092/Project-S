from pathlib import Path
from PIL import Image
from ui_cutout import extract
import hashlib,json
root=Path(__file__).resolve().parents[1]
source=root/'_Inbox/score-hud-apimart-review-v2'
report=[]
for raw,cat,name in [('empty-slot-ivory.png','Frame','score_slot_empty'),('sort-recycle.png','Icon','score_sort'),('close-ivory.png','Icon','score_close')]:
 p=source/raw
 art=extract(Image.open(p),solid=False)
 art.thumbnail((500,500),Image.Resampling.LANCZOS)
 canvas=Image.new('RGBA',(512,512))
 canvas.alpha_composite(art,((512-art.width)//2,(512-art.height)//2))
 dest=root/f'Assets/_Game/Res/Texture/UI/{cat}/{name}.png'
 canvas.save(dest)
 assert canvas.getpixel((0,0))[3]==0
 if name=='score_slot_empty':assert canvas.getpixel((256,256))[3]==0
 report.append({'source':str(p.relative_to(root)),'source_sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'destination':str(dest.relative_to(root)),'approved':True,'size':[512,512]})
(root/'Logs/score-controls-approved-import.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False,indent=2))
