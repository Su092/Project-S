from pathlib import Path
import json
p=Path(__file__).resolve().parent
f=p/"generation-plan.json"
m=json.loads(f.read_text(encoding="utf-8"))
j=json.loads((p/"correction-job.json").read_text(encoding="utf-8"))
m["jobs"]=[x for x in m["jobs"] if x["id"]!=j["id"]]+[j]
f.write_text(json.dumps(m,ensure_ascii=False,indent=2),encoding="utf-8")
print("Correction planned")
