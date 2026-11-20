"""Read-only music HUD preservation/asset provenance audit (no Unity YAML mutations)."""
from pathlib import Path
import json,hashlib,re
from audit_ui_slices import parse
R=Path(__file__).resolve().parents[1];D=R/"_Inbox/hud-music-20261118"
B=Path((D/"backup-location.txt").read_text(encoding="utf-8"))
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def layout(p):
 names,rts,_=parse(p)
 def path(i):
  p=rts[i]["father_go"]
  return (path(p)+"/" if p in rts else "")+names[i]
 return {path(i):{k:v for k,v in rt.items() if k!="father_go"} for i,rt in rts.items()}
report={}
pf="Assets/_Game/Res/UIPrefabs/HUD.prefab"
before,after=layout(B/pf),layout(R/pf)
allowed={"HUD/Health/Fill","HUD/Experience/Fill","HUD/BossBar/Fill","HUD/Minimap/Bezel","HUD/Minimap/North"}
changed={p for p in set(before)|set(after) if before.get(p)!=after.get(p)}
assert changed==allowed,changed
report["unchanged_rect_transforms"]=len(after)-len(changed)
report["authorized_geometry_updates"]=sorted(changed)
hashes=json.loads((D/"before-hashes.json").read_text())
stable={k:v for k,v in hashes.items() if k!=pf}
assert all((R/k).exists() and digest(R/k)==v for k,v in stable.items()),[k for k,v in stable.items() if not (R/k).exists() or digest(R/k)!=v]
report["unchanged_scene_config_and_other_prefab_files"]=len(stable)
report["unchanged_other_prefabs"]=[Path(k).name for k in stable if k.endswith(".prefab")]
base=B/"Assets/_Game/Res/Texture/UI";current=R/"Assets/_Game/Res/Texture/UI"
guids={}
for p in base.rglob("*.meta"):
 q=current/p.relative_to(base)
 assert q.exists(),q
 a=re.search(r"^guid: (\w+)",p.read_text(),re.M).group(1)
 b=re.search(r"^guid: (\w+)",q.read_text(),re.M).group(1)
 assert a==b,q;guids[p.relative_to(base).as_posix()]=a
report["preserved_existing_texture_guids"]=len(guids)
processing=json.loads((D/"processing-report.json").read_text())
changed={row["category"]+"/"+row["name"]+".png" for row in processing["components"]}
untouched=[]
for p in base.rglob("*.png"):
 rel=p.relative_to(base).as_posix()
 if rel not in changed:
  assert digest(p)==digest(current/rel),rel
  untouched.append(rel)
report["unchanged_other_textures"]=len(untouched)
assert len(processing["components"])==87
for row in processing["components"]:assert digest(current/row["category"]/(row["name"]+".png"))==row["sha256"],row["name"]
report["verified_processed_sprites"]=87
receipts=[json.loads(p.read_text()) for p in (D/"receipts").glob("*.json")]
assert len(receipts)==11 and all(x["status"]=="completed" for x in receipts)
assert len({x["task_id"] for x in receipts})==11
for name,sha in processing["raw_hashes"].items():assert digest(D/"raw"/name)==sha,name
report["verified_raw_sheets"]=11
report["result"]="PASS"
(D/"preservation-report.json").write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
print(json.dumps(report,ensure_ascii=False,indent=2))
