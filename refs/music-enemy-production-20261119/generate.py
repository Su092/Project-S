import importlib.util
import json
import time
import ssl
import http.client
import hashlib
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
from PIL import Image

HUB = Path('E:/Unity Development')
HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('apimart', HUB/'.cursor/skills/apimart-image/scripts/apimart_image.py')
api = importlib.util.module_from_spec(spec)
spec.loader.exec_module(api)
# Existing project route, TLS tunnel to the API hostname with certificate validation.
def request(url, key, payload=None, method=None, timeout=90, retries=1):
    conn = http.client.HTTPSConnection('127.0.0.1',7890,timeout=timeout,context=ssl.create_default_context())
    conn.set_tunnel(api.HOST,443)
    headers={'Authorization':'Bearer '+key,'Accept':'application/json','Host':api.HOST}
    body=None
    if payload is not None:
        body=json.dumps(payload,ensure_ascii=False).encode('utf-8')
        headers['Content-Type']='application/json'
    try:
        conn.request(method or ('POST' if payload is not None else 'GET'),url.removeprefix('https://'+api.HOST),body,headers)
        r=conn.getresponse()
        raw=r.read().decode('utf-8')
        if r.status>=400: raise RuntimeError('HTTP '+str(r.status)+': '+raw[:300])
        return json.loads(raw)
    finally:
        conn.close()
api.http_json=request

def generate(job, key):
    receipt=HERE/(job['id']+'-receipt.json')
    def save():
        receipt.write_text(json.dumps(state,ensure_ascii=False,indent=2),encoding='utf-8')
    state=json.loads(receipt.read_text()) if receipt.exists() else {'id':job['id'],'status':'new','resolution':'1k'}
    if state['status']=='completed': return state['url']
    try:
        if not state.get('task_id'):
            if state['status']!='new': raise RuntimeError('Unconfirmed submission requires read-only audit.')
            state.update(status='submitting',started=time.time());save()
            payload={k:job[k] for k in ['prompt','size','resolution']}
            payload.update(model=api.MODEL,n=1,image_urls=job['image_urls'])
            result=request(api.BASE+'/images/generations',key,payload)
            state['task_id']=result['data'][0]['task_id']
            state['status']='submitted';save()
            print(job['id'],state['task_id'],flush=True)
        if not state.get('url'):
            state['url']=api.poll(key,state['task_id'],timeout_s=1200,interval=8)
            state['status']='generated';save()
        out=Path(api.download(state['url'],str(HERE/(job['id']+'.png'))))
        with Image.open(out) as im:
            im.load();width,height=im.size
        state.update(status='completed',width=width,height=height,bytes=out.stat().st_size,sha256=hashlib.sha256(out.read_bytes()).hexdigest())
        save();print('COMPLETED',job['id'],width,height,flush=True)
        return state['url']
    except Exception as e:
        state['last_error']=str(e)
        save();print('ERROR',job['id'],str(e),flush=True)
        return None

def main():
    jobs=json.loads((HERE/'plan.json').read_text(encoding='utf-8'))
    old=HERE.parent/'music-enemy-concepts-20261118'
    urls={i:json.loads((old/('receipt.json' if i==1 else 'receipt-%02d.json'%i)).read_text())['url'] for i in range(1,5)}
    for job in jobs:
        job['image_urls']=[urls[i] for i in job['refs']]
        job.update(out='Project/S/unity/refs/'+HERE.name+'/'+job['id']+'.png',no_global_style=True)
    manifest=HUB/'tools/apimart-assets.json'
    m=json.loads(manifest.read_text(encoding='utf-8-sig'))
    for j in jobs:
        full_id='s-music-production-20261119-'+j['id']
        if not any(a['id']==full_id for a in m['assets']):
            m['assets'].append(dict(j,id=full_id))
    manifest.write_text(json.dumps(m,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    key=api.find_key()
    if not key:raise RuntimeError('API credential not found')
    with ThreadPoolExecutor(max_workers=3) as pool:
        results=list(pool.map(lambda j:generate(j,key),jobs[:6]))
    if results[5]:
        jobs[6]['image_urls']=[results[5]]
        (HERE/'turret-reference.json').write_text(json.dumps({'image_urls':jobs[6]['image_urls']},indent=2))
        # Preserve any unrelated manifest edits since initial submission.
        m=json.loads(manifest.read_text(encoding='utf-8-sig'))
        for a in m['assets']:
            if a['id']=='s-music-production-20261119-07_turret':a['image_urls']=jobs[6]['image_urls']
        manifest.write_text(json.dumps(m,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
        generate(jobs[6],key)
    print('BATCH FINISHED',flush=True)

if __name__=='__main__':main()
