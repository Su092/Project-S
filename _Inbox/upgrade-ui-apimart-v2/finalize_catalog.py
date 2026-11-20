from pathlib import Path
import json
root=Path(__file__).resolve().parents[2]
hub=Path(r"E:\Unity Development")
p=hub/"tools/apimart-assets.json"
m=json.loads(p.read_text(encoding="utf-8-sig"))
retired={"s-upgrade-apimart-v1-frames","s-upgrade-apimart-v1-staff","s-upgrade-apimart-v1-staff-corrected"}
m["assets"]=[a for a in m["assets"] if a["id"] not in retired]
p.write_text(json.dumps(m,ensure_ascii=False,indent=2),encoding="utf-8")
old=root/"_Inbox/upgrade-ui-apimart-v1/README.md"
notice="> SUPERSEDED: current four-color design is ../upgrade-ui-apimart-v2/README.md. White/purple variants below are archived history only.\n\n"
s=old.read_text(encoding="utf-8")
if not s.startswith("> SUPERSEDED:"):old.write_text(notice+s,encoding="utf-8")
print("Current catalog updated; retired six-color sheet removed from active entries; raw images unchanged.")
