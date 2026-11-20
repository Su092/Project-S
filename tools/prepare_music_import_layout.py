"""One-time migration recipe. Source delivery remains outside Assets."""
from pathlib import Path
import json, collections
ROOT=Path(__file__).resolve().parents[1]
OLD='Assets/_Game/Res/Characters/Music'
EDITOR='Assets/_Game/Editor/MusicImport'
RES='Assets/_Game/Res'
def category(kind,id): return f'{RES}/{kind}/Enemies/Music/{id}'
def main():
    plan=json.loads((ROOT/OLD/'import-plan.json').read_text(encoding='utf-8-sig'))
    moves={}; inputs={}; contracts={}
    def move(old,new):
        if old in moves and moves[old]!=new: raise ValueError((old,new,moves[old]))
        moves[old]=new
        return new
    def selected(old,owner,kind):
        relative=old[len(OLD)+1:]
        if relative.startswith(('FXM_S14_NoteGate/','SM_MusicFollowSpot/')): relative='SkillSupplements/'+relative
        target=(f'{EDITOR}/Sources/{owner}/{kind}/' if kind!='Textures' else category('Texture',owner)+'/')+Path(old).name
        move(old,target)
        if relative in inputs and inputs[relative]!=target: raise ValueError(relative)
        inputs[relative]=target
        return target
    for a in plan['assets']:
        old=a['model'];owner=a['family'];directory=str(Path(old).parent).replace('\\','/')
        a['model']=selected(old,owner,'Models')
        a['collision']=''
        for clip in a['clips']:
            clip['file']=selected(clip['file'],owner,'Animations')
            move(directory+'/Animations/'+owner+'_'+clip['state']+'.anim',category('Animation',owner)+'/'+owner+'_'+clip['state']+'.anim')
        for i in range(3):move(directory+'/SM_'+a['id']+'_LOD'+str(i)+'.asset',category('Model',a['id'])+'/SM_'+a['id']+'_LOD'+str(i)+'.asset')
        move(directory+'/'+a['id']+'.controller',category('Animation',a['id'])+'/'+a['id']+'.controller')
        move(directory+'/MAT_'+owner+'.mat',category('Material',owner)+'/MAT_'+owner+'.mat')
        oldtex=a['textureRoot']
        for suffix in ['_BC.png','_ORM.png','_E.png']:selected(oldtex+suffix,owner,'Textures')
        a['textureRoot']=category('Texture',owner)+'/'+Path(oldtex).name
        # Turret is a nested delivery but has its own contract.
        rel=old[len(OLD)+1:]; parent=rel.split('/Exports/')[0]
        contracts[owner]={'source':parent+'/manifest.json','id':owner,'height':a['height']}
    for fx in plan['effects']:
        old=fx['model'];directory=str(Path(old).parent).replace('\\','/')
        fx['model']=selected(old,fx['id'],'Models')
        fx['texture']=selected(fx['texture'],'Shared','Textures')
        move(directory+'/MAT_'+fx['id']+'.mat',category('Material',fx['id'])+'/MAT_'+fx['id']+'.mat')
    for id in ['FXM_S14_NoteGate','SM_MusicFollowSpot']:
        directory=OLD+'/'+id
        selected(directory+'/Model/'+id+'.fbx',id,'Models')
        for suffix in ['BC','ORM','E']+(['Curtain'] if id=='FXM_S14_NoteGate' else []):
            selected(directory+'/Textures/T_'+id+'_'+suffix+'.png',id,'Textures')
        for mat in ['MAT_'+id,'MAT_'+id+'_Accent']:
            move(directory+'/'+mat+'.mat',category('Material',id)+'/'+mat+'.mat')
        contracts[id]={'source':'SkillSupplements/'+id+'/manifest.json','id':id,'height':0}
    id='SM_MusicFollowSpot'
    move(OLD+'/'+id+'/Textures/T_'+id+'_LensEmission.asset',category('Texture',id)+'/T_'+id+'_LensEmission.asset')
    move(OLD+'/'+id+'/'+id+'.controller',category('Animation',id)+'/'+id+'.controller')
    for state in ['Idle','FocusIn','FocusOut','RigTest']:
        selected(OLD+'/'+id+'/Animations/'+id+'_'+state+'.fbx',id,'Animations')
        move(OLD+'/'+id+'/Animations/'+state+'.anim',category('Animation',id)+'/'+state+'.anim')
    for p in (ROOT/OLD/'Native').rglob('*.asset'):
        id=p.parent.name
        kind='Model' if p.name.startswith('Mesh_') else 'Animation' if p.name.startswith(('Avatar_','AnimationClip_')) else 'Material' if p.name.startswith('Material_') else 'Texture'
        move(p.relative_to(ROOT).as_posix(),category(kind,id)+'/'+p.name)
    assert len(set(moves.values()))==len(moves),'target conflict'
    for old in moves:
        assert (ROOT/old).is_file(),old
    delivery=ROOT/'_Inbox/chr/S_Music_Enemies/Reexport_20261119_135947'
    for source in inputs: assert (delivery/source).is_file(),source
    for c in contracts.values():
        j=json.loads((delivery/c['source']).read_text(encoding='utf-8-sig'))
        assert j['assetId']==c['id'],c
    archive=[p.relative_to(ROOT).as_posix() for p in (ROOT/OLD).rglob('*') if p.is_file() and p.suffix!='.meta' and p.relative_to(ROOT).as_posix() not in moves]
    out=ROOT/EDITOR;out.mkdir(parents=True,exist_ok=True)
    (out/'import-plan.json').write_text(json.dumps(plan,ensure_ascii=False,indent=2),encoding='utf-8')
    (out/'delivery-files.json').write_text(json.dumps({'schemaVersion':1,'files':[{'source':s,'target':t} for s,t in sorted(inputs.items())],'contracts':list(contracts.values())},ensure_ascii=False,indent=2),encoding='utf-8')
    (out/'migration.json').write_text(json.dumps({'moves':[{'source':s,'target':t} for s,t in moves.items()],'archive':archive},ensure_ascii=False,indent=2),encoding='utf-8')
    print('moves',len(moves),'selected inputs',len(inputs),'archive',len(archive),'contracts',len(contracts))
if __name__=='__main__':main()
