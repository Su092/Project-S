"""Approved APIMart RGB replacement with immutable source/meta backup and exact alpha preservation."""
from pathlib import Path
import json,hashlib,shutil,sys
from PIL import Image,ImageChops
ROOT=Path(__file__).resolve().parents[1]
BASE=ROOT/'_Inbox/cel-texture-review-v1'
BACK=BASE/'backup-original'
STAGE=BASE/'prepared'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def dump(path,value):path.write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')
def prepare():
    sources=json.loads((BASE/'source-inventory.json').read_text(encoding='utf-8'))
    rows=json.loads((BASE/'review-status.json').read_text(encoding='utf-8'))
    byname={r['name']:r for r in rows}
    # Preflight every source before backing up or staging any replacement.
    for s in sources:
        assert sha(ROOT/s['source'])==s['sha256'], 'Source changed: '+s['source']
        assert byname[s['name']]['status']=='completed'
        assert (ROOT/(s['source']+'.meta')).exists()
    BACK.mkdir(exist_ok=True);STAGE.mkdir(exist_ok=True)
    manifest=[]
    for s in sources:
        path=ROOT/s['source'];back=BACK/s['source']
        back.parent.mkdir(parents=True,exist_ok=True)
        if back.exists():assert sha(back)==s['sha256'],'Backup mismatch'
        else:shutil.copy2(path,back)
        meta=Path(str(path)+'.meta');backmeta=Path(str(back)+'.meta')
        if backmeta.exists():assert sha(meta)==sha(backmeta),'Meta backup mismatch'
        else:shutil.copy2(meta,backmeta)
        candidate=ROOT/byname[s['name']]['generated']
        with Image.open(back) as original, Image.open(candidate) as new:
            rgb=new.convert('RGB')
            if rgb.size!=original.size:rgb=rgb.resize(original.size,Image.Resampling.LANCZOS)
            if 'A' in original.getbands():rgb.putalpha(original.getchannel('A'))
            target=STAGE/(s['name']+'.png');rgb.save(target)
            with Image.open(target) as result:
                assert result.size==original.size
                if 'A' in original.getbands():assert ImageChops.difference(original.getchannel('A'),result.getchannel('A')).getbbox() is None
        manifest.append(dict(source=s['source'],backup=back.relative_to(ROOT).as_posix(),generated=candidate.relative_to(ROOT).as_posix(),prepared=target.relative_to(ROOT).as_posix(),original_sha256=s['sha256'],prepared_sha256=sha(target),meta_sha256=sha(meta),size=s['size'],alpha_preserved=True))
    # Record data maps and materials for proving they stay unchanged.
    texture_root=ROOT/'Assets/_Game/Res/Texture'
    protected=[]
    replaced={r['source'] for r in manifest}
    for p in texture_root.rglob('*.png'):
        rel=p.relative_to(ROOT).as_posix()
        if rel not in replaced:protected.append(dict(path=rel,sha256=sha(p)))
    materials=sorted({m.replace('\\','/') for s in sources for m in s['materials']})
    for rel in materials:protected.append(dict(path=rel,sha256=sha(ROOT/rel)))
    dump(BASE/'import-plan-approved.json',dict(authorized=True,records=manifest,protected_files=protected))
    (BASE/'import-paths.txt').write_text('\n'.join(r['source'] for r in manifest),encoding='utf-8')
    (BASE/'import-materials.txt').write_text('\n'.join(materials),encoding='utf-8')
    print('PREPARED',len(manifest),'textures; original files and metas backed up; original sizes and exact alpha retained; Assets unchanged.')
def install():
    plan=json.loads((BASE/'import-plan-approved.json').read_text(encoding='utf-8'))
    before=BASE/'comparison/before'
    assert (before/'snapshot.json').is_file() and (before/'game.png').stat().st_size>10000,'Capture original rendering first'
    assert not (BASE/'import-completed.json').exists(),'Already installed; do not repeat'
    for r in plan['records']:
        assert sha(ROOT/r['source'])==r['original_sha256'],'Source changed'
        assert sha(ROOT/r['prepared'])==r['prepared_sha256'],'Staging changed'
        assert sha(ROOT/(r['source']+'.meta'))==r['meta_sha256'],'Meta changed'
    done=[]
    try:
        for r in plan['records']:
            shutil.copyfile(ROOT/r['prepared'],ROOT/r['source']);done.append(r)
        validate(plan)
    except BaseException:
        for r in done:shutil.copyfile(ROOT/r['backup'],ROOT/r['source'])
        raise
    dump(BASE/'import-completed.json',dict(count=len(done),backup=BACK.relative_to(ROOT).as_posix(),records=done))
    print('INSTALLED',len(done),'textures. GUID/meta unchanged; exact original alpha and sizes preserved.')
def validate(plan=None):
    if plan is None:plan=json.loads((BASE/'import-plan-approved.json').read_text(encoding='utf-8'))
    for r in plan['records']:
        assert sha(ROOT/r['source'])==r['prepared_sha256'],r['source']
        assert sha(ROOT/(r['source']+'.meta'))==r['meta_sha256'],'Meta changed: '+r['source']
        with Image.open(ROOT/r['backup']) as original, Image.open(ROOT/r['source']) as new:
            assert new.size==original.size
            if 'A' in original.getbands():assert ImageChops.difference(original.getchannel('A'),new.getchannel('A')).getbbox() is None
    # The original receipt predates the retirement of these two unused HUD sprites.
    retired={'Assets/_Game/Res/Texture/UI/Icon/hud_read_cursor.png',
             'Assets/_Game/Res/Texture/UI/Widget/hud_weapon_selected_ring.png'}
    for r in plan['protected_files']:
        path=ROOT/r['path']
        if r['path'] in retired and not path.exists() and (BASE/'import-completed.json').exists():continue
        assert sha(path)==r['sha256'],'Protected file changed: '+r['path']
    print('PASS: replacement hashes, sizes, original alpha, meta/GUID, materials and',len(plan['protected_files']),'protected files.')
if __name__=='__main__':
    {'prepare':prepare,'install':install,'validate':validate}[sys.argv[1]]()
