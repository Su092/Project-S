"""One submission with a persistent receipt; reruns only poll the known task."""
import importlib.util,json,sys
from pathlib import Path
hub=Path(sys.argv[1])
spec=importlib.util.spec_from_file_location("api",hub/".cursor/skills/apimart-image/scripts/apimart_image.py")
api=importlib.util.module_from_spec(spec);spec.loader.exec_module(api)
out=Path("_Inbox/music-combat-vfx-20261119");out.mkdir(parents=True,exist_ok=True)
receipt=out/"receipt.json"
item=next(x for x in json.loads((hub/"tools/apimart-assets.json").read_text(encoding="utf-8-sig"))["assets"] if x["id"]=="s-music-combat-vfx-20261119")
key=api.find_key()
if receipt.exists():
 r=json.loads(receipt.read_text())
 if not r.get("task_id"):raise RuntimeError("Previous submission uncertain: inspect receipt, do not resubmit.")
else:
 receipt.write_text(json.dumps({"state":"submitting"}))
 import urllib.request,winreg
 registry=winreg.OpenKey(winreg.HKEY_CURRENT_USER,r'Software\Microsoft\Windows\CurrentVersion\Internet Settings')
 proxy=winreg.QueryValueEx(registry,'ProxyServer')[0]
 opener=urllib.request.build_opener(urllib.request.ProxyHandler({'https':'http://'+proxy,'http':'http://'+proxy}))
 body=json.dumps({"model":"gpt-image-2","prompt":item["prompt"],"size":"1:1","resolution":"2k","n":1}).encode()
 request=urllib.request.Request(api.BASE+'/images/generations',data=body,headers={"Authorization":"Bearer "+key,"Content-Type":"application/json"})
 print('Submitting through configured system proxy',flush=True)
 with opener.open(request,timeout=120) as response:j=json.loads(response.read())
 r={"task_id":j["data"][0]["task_id"],"state":"submitted","model":"gpt-image-2","resolution":"2k"}
 receipt.write_text(json.dumps(r));print("task",r["task_id"],flush=True)
import urllib.request,winreg,time
registry=winreg.OpenKey(winreg.HKEY_CURRENT_USER,r'Software\Microsoft\Windows\CurrentVersion\Internet Settings')
proxy=winreg.QueryValueEx(registry,'ProxyServer')[0]
opener=urllib.request.build_opener(urllib.request.ProxyHandler({'https':'http://'+proxy,'http':'http://'+proxy}))
for i in range(180):
 request=urllib.request.Request(api.BASE+'/tasks/'+r['task_id'],headers={'Authorization':'Bearer '+key})
 try:
  with opener.open(request,timeout=30) as response:data=json.loads(response.read())['data']
 except Exception as e:
  print('poll',type(e).__name__,flush=True);time.sleep(5);continue
 status=data.get('status');print('status',status,flush=True)
 if status=='completed':
  url=data['result']['images'][0]['url'][0];r.update(state='completed',url=url);receipt.write_text(json.dumps(r))
  with opener.open(urllib.request.Request(url,headers={'User-Agent':'S/1.0'}),timeout=90) as response:blob=response.read()
  if len(blob)<10000:raise RuntimeError('Invalid image payload')
  (out/'raw-atlas.png').write_bytes(blob);print('COMPLETE',len(blob),flush=True);break
 if status in ('failed','error'):raise RuntimeError('Generation failed: '+str(data))
 time.sleep(5)
else:raise RuntimeError('Poll timeout; keep receipt and poll only')
