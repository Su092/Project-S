from pathlib import Path
import json,re
root=Path(__file__).resolve().parents[1]
report=json.loads((root/'Logs/legacy-cleanup-audit.json').read_text())
targets={root/p for p in report['deleted']}
# FBX importers may carry external material GUIDs inside .meta files.
guid={}
for p in targets:
 m=Path(str(p)+'.meta')
 if m.exists():
  match=re.search(r'^guid: (\w+)',m.read_text(errors='ignore'),re.M)
  if match:guid[match[1]]=p
protected=set()
for meta in (root/'Assets').rglob('*.meta'):
 asset=Path(str(meta)[:-5])
 if asset in targets:continue
 protected|={guid[g] for g in re.findall(r'guid: ([a-f0-9]{32})',meta.read_text(errors='ignore')) if g in guid}
# Keep dependency closure of any importer-referenced candidate.
while True:
 more=set()
 for p in protected:
  if p.suffix in ('.mat','.prefab','.asset','.controller','.anim'):
   more|={guid[g] for g in re.findall(r'guid: ([a-f0-9]{32})',p.read_text(errors='ignore')) if g in guid}
 if more<=protected:break
 protected|=more
targets-=protected
# Deprecated sci-fi UI backup is outside Assets and never loaded by Unity.
targets.update(p for p in (root/'Backup_UI_PIL').rglob('*') if p.is_file())
entries=[]
for p in sorted(targets):
 entries.append({'path':p.relative_to(root).as_posix(),'bytes':p.stat().st_size})
 p.unlink()
 meta=Path(str(p)+'.meta')
 if meta.exists():meta.unlink()
result={'removed':entries,'count':len(entries),'bytes':sum(e['bytes'] for e in entries),'preserved_importer_dependencies':[p.relative_to(root).as_posix() for p in protected]}
(root/'Logs/legacy-cleanup-deleted.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print('Deleted',result['count'],'assets; bytes',result['bytes'],'protected',len(protected))
