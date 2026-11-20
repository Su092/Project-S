from pathlib import Path
import re,shutil,json
root=Path(__file__).resolve().parents[1];backup=root/'_Inbox/console-fix-backup'
changes=[]
def save(p,s):
    old=p.read_text(encoding='utf-8-sig')
    if old==s:return
    dest=backup/p.relative_to(root);dest.parent.mkdir(parents=True,exist_ok=True)
    if not dest.exists():shutil.copy2(p,dest)
    p.write_text(s,encoding='utf-8');changes.append(p.relative_to(root).as_posix())
# Remove only imported child LODGroup; preserve root ownership, all meshes and IDs.
for p in (root/'Assets/_Game/Res/Prefab/Weapons').glob('SW*.prefab'):
    s=p.read_text(encoding='utf-8-sig')
    blocks=re.findall(r'--- !u!\d+ &\d+[^\n]*\n.*?(?=--- !u!|\Z)',s,re.S)
    lods=[b for b in blocks if b.startswith('--- !u!205 ')]
    assert len(lods)==2,(p,len(lods))
    models=[b for b in blocks if b.startswith('--- !u!1 ') and '\n  m_Name: Model\n' in b]
    assert len(models)==1
    modelid=re.match(r'--- !u!1 &(\d+)',models[0])[1]
    child=next(b for b in lods if 'm_GameObject: {fileID: '+modelid+'}' in b)
    parent=next(b for b in lods if b!=child)
    assert re.findall(r'renderer: \{fileID: (\d+)\}',child)==re.findall(r'renderer: \{fileID: (\d+)\}',parent)
    component=re.match(r'--- !u!205 &(\d+)',child)[1]
    s=s.replace(child,'').replace('  - component: {fileID: '+component+'}\n','')
    assert s.count('--- !u!205 ')==1
    save(p,s)
p=root/'Assets/_Game/Editor/SMusicWeaponImporter.cs';s=p.read_text(encoding='utf-8-sig')
needle='                var lod = root.AddComponent<LODGroup>();var lods = new LOD[3];'
assert needle in s
s=s.replace(needle,'                // FBX already contains an LODGroup; the runtime root owns these renderers.\n                foreach(var inherited in modelGo.GetComponentsInChildren<LODGroup>(true))\n                    UnityEngine.Object.DestroyImmediate(inherited);\n'+needle)
s=s.replace('actual.Length==3&&instance.GetComponent<LODGroup>()','actual.Length==3&&instance.GetComponentsInChildren<LODGroup>(true).Length==1&&instance.GetComponent<LODGroup>()')
save(p,s)
# Unity 6000.6 deprecated the old unsorted overload. All calls here used None.
for p in (root/'Assets').rglob('*.cs'):
    s=p.read_text(encoding='utf-8-sig')
    if 'FindObjectsSortMode.None' not in s:continue
    s=re.sub(r'(FindObjectsByType<[^>]+>\(FindObjectsInactive\.(?:Include|Exclude))\s*,\s*FindObjectsSortMode.None\)',r'\1)',s)
    s=re.sub(r'(FindObjectsByType<[^>]+>\()FindObjectsSortMode.None\)',r'\1)',s)
    assert 'FindObjectsSortMode.None' not in s,p
    save(p,s)
(backup/'changes.json').write_text(json.dumps(changes,ensure_ascii=False,indent=2),encoding='utf-8')
print('Updated',len(changes),'files:')
print('\n'.join(changes))
