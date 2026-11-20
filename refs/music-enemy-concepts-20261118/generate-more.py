import importlib.util,json,time,hashlib,http.client,ssl,sys
from pathlib import Path
from PIL import Image
ROOT=Path('E:/Unity Development')
HERE=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('apimart_image',ROOT/'.cursor/skills/apimart-image/scripts/apimart_image.py')
api=importlib.util.module_from_spec(spec);spec.loader.exec_module(api)
def proxy_json(url,key,payload=None,method=None,timeout=90,retries=1):
    # Use the user's configured local HTTPS proxy; tunnel retains certificate verification.
    c=http.client.HTTPSConnection('127.0.0.1',7890,timeout=timeout,context=ssl.create_default_context())
    c.set_tunnel(api.HOST,443)
    path=url.removeprefix('https://'+api.HOST)
    headers={'Authorization':'Bearer '+key,'Accept':'application/json','Host':api.HOST}
    body=None
    if payload is not None:
        body=json.dumps(payload,ensure_ascii=False).encode('utf-8');headers['Content-Type']='application/json'
    try:
        c.request(method or ('POST' if payload is not None else 'GET'),path,body,headers)
        r=c.getresponse();raw=r.read().decode('utf-8')
        if r.status>=400: raise RuntimeError('HTTP '+str(r.status)+': '+raw[:400])
        return json.loads(raw)
    finally: c.close()
api.http_json=proxy_json
batch=sys.argv[1]
assert batch in ('02','03','04')
job=json.loads((HERE/('generation-plan-'+batch+'.json')).read_text(encoding='utf-8'))
receipt=HERE/('receipt-'+batch+'.json')
if receipt.exists():
    raise RuntimeError('Receipt already exists; inspect before resuming.')
state={'id':job['id'],'model':api.MODEL,'resolution':job['resolution'],'status':'submitting','started_at':time.time()}
def save():
    receipt.write_text(json.dumps(state,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
save()
try:
    key=api.find_key()
    if not key: raise RuntimeError('Configured API key unavailable')

    payload={'model':api.MODEL,'prompt':job['prompt'],'n':1,'size':job['size'],'resolution':job['resolution'],'image_urls':job['image_urls']}
    response=proxy_json(api.BASE+'/images/generations',key,payload,timeout=90)
    data=response.get('data') or []
    if not data or not data[0].get('task_id'):
        state['submit_response']=response
        raise RuntimeError('No task ID returned; do not automatically resubmit')
    tid=data[0]['task_id'];state.update(task_id=tid,status='submitted');save()
    print('task_id='+tid,flush=True)
    url=api.poll(key,tid,timeout_s=900,interval=5)
    state.update(status='generated',url=url);save()
    out=Path(api.download(url,job['out']))
    with Image.open(out) as im:
        im.load();size=im.size;fmt=im.format
    state.update(status='completed',file=str(out),width=size[0],height=size[1],format=fmt,bytes=out.stat().st_size,sha256=hashlib.sha256(out.read_bytes()).hexdigest(),completed_at=time.time())
    save();print('COMPLETED',size,flush=True)
except Exception as ex:
    state['last_error']=str(ex)
    state['status']='needs_status_check' if state.get('task_id') else 'submission_unconfirmed'
    save();print('ERROR:',str(ex),flush=True)
