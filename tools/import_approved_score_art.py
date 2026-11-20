from pathlib import Path
from PIL import Image
from ui_cutout import extract
import hashlib,json
root=Path(__file__).resolve().parents[1]
source=root/"_Inbox/score-workbench-apimart-review"
report=[]
for name,target,size in [
 ("score-bag-icon.png","Assets/_Game/Res/Texture/UI/Icon/score_bag_icon.png",512),
 ("score-page.png","Assets/_Game/Res/Texture/UI/Frame/score_page.png",2048)]:
 p=source/name
 art=extract(Image.open(p),solid=True)
 if size==512:
  art.thumbnail((488,488),Image.Resampling.LANCZOS)
  canvas=Image.new("RGBA",(512,512));canvas.alpha_composite(art,((512-art.width)//2,(512-art.height)//2));art=canvas
 else:art.thumbnail((2048,2048),Image.Resampling.LANCZOS)
 dest=root/target;art.save(dest)
 report.append({"source":name,"source_sha256":hashlib.sha256(p.read_bytes()).hexdigest(),"destination":target,"size":art.size,"approved":True})
(root/"Logs/score-approved-import.json").write_text(json.dumps(report,indent=2),encoding="utf-8")
print(json.dumps(report,indent=2))
