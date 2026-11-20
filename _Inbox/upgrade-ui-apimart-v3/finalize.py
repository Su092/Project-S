from pathlib import Path
import json
p=Path(__file__).resolve().parent
f=p/"index.html"
f.write_text(f.read_text(encoding="utf-8").replace("03-gold-staff-ornamental.png","03b-gold-staff-ornamental-five.png"),encoding="utf-8")
f=p/"README.md"
s=f.read_text(encoding="utf-8")
s+="\n## 已完成原图\n当前采用 03b-gold-staff-ornamental-five.png（3072×1024），已目视确认五根主线、刻纹高光与外侧卷曲饰纹。首张四线候选仅存档。\n"
f.write_text(s,encoding="utf-8")
manifest=Path(r"E:\Unity Development\tools\apimart-assets.json")
m=json.loads(manifest.read_text(encoding="utf-8-sig"))
m["assets"]=[a for a in m["assets"] if a["id"] not in {"s-upgrade-apimart-v2-staff","s-upgrade-apimart-v3-staff"}]
manifest.write_text(json.dumps(m,ensure_ascii=False,indent=2),encoding="utf-8")
print("Preview and active catalog updated; image bytes unchanged.")
