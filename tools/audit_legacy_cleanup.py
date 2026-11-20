from pathlib import Path
import re,json
root=Path(__file__).resolve().parents[1]
assets=list((root/'Assets').rglob('*'));files=[p for p in assets if p.is_file() and p.suffix!='.meta']
def rel(p):return p.relative_to(root).as_posix()
oldnames=('Boss_CrucibleSovereign','FurnaceWarden','PressureRunner','RingSentry','SlagHound')
candidates={p for p in files if '/AbandonedFoundry/' in rel(p) or '/Characters/Foundry/' in rel(p) or (p.suffix=='.prefab' and p.stem.startswith(oldnames)) or p.name=='FoundryArena.prefab'}
# Legacy UI is only removable when neither code nor serialized assets depend on it.
candidates.update(p for p in files if '/Texture/UI/' in rel(p) and p.suffix in ('.png','.jpg') and not p.stem.startswith(('hud_','offer_','item_')))
guid={}
for p in files:
 meta=Path(str(p)+'.meta')
 if meta.exists():
  m=re.search(r'^guid: (\w+)',meta.read_text(errors='ignore'),re.M)
  if m:guid[m[1]]=p
texts={}
for p in files:
 if p.suffix in ('.prefab','.unity','.asset','.mat','.controller','.anim','.cs','.tsv','.shader'):
  if 'AddressableAssetsData' not in rel(p) and '/Editor/' not in rel(p):texts[p]=p.read_text(errors='ignore')
code='\n'.join(t for p,t in texts.items() if p.suffix in ('.cs','.tsv'))
# AssetName is the retired editor authoring constant; ActiveAssetName is the gameplay load.
code=code.replace('public const string AssetName = "FoundryArena";','').replace('public const string AssetPath = "Assets/_Game/Res/Prefab/Maps/FoundryArena.prefab";','')
# Catalog entries alone register names, but actual accessors below remain roots.
code=re.sub(r'\{ "[^"]+", "(?:Common|Frame|Widget|Tag|Icon|Items)" \},?','',code)
code='\n'.join(re.findall(r'"([^"\n]*)"',code))+'\n'+'\n'.join(t for p,t in texts.items() if p.suffix=='.tsv')
retained=set(files)-candidates
for p in candidates:
 if p.stem in code.splitlines() or any('/'+p.name in line for line in code.splitlines()):retained.add(p)
while True:
 deps={guid[g] for p in retained for g in re.findall(r'guid: ([a-f0-9]{32})',texts.get(p,'')) if g in guid}
 new=deps-retained
 if not new:break
 retained|=new
deleted=sorted(candidates-retained)
report={'deleted':[rel(p) for p in deleted],'retained_candidates':[rel(p) for p in sorted(candidates&retained)],'bytes':sum(p.stat().st_size+Path(str(p)+'.meta').stat().st_size for p in deleted)}
(root/'Logs/legacy-cleanup-audit.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('Safe candidates',len(deleted),'bytes',report['bytes'],'retained',len(candidates&retained))
print('\n'.join(report['deleted'][:18]))
