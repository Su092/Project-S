from pathlib import Path
import importlib.util, json, time, concurrent.futures, hashlib, traceback
H=Path("E:/Unity Development")
ROOT=Path.cwd().resolve()/"refs/theme-style-concepts-20261118"
spec=importlib.util.spec_from_file_location("apimart_skill",H/".cursor/skills/apimart-image/scripts/apimart_image.py")
api=importlib.util.module_from_spec(spec);spec.loader.exec_module(api)
# Honor the existing Windows proxy without changing OS settings.
import socket, urllib.request
PROXY_HOST,PROXY_PORT="127.0.0.1",7890
urllib.request.install_opener(urllib.request.build_opener(urllib.request.ProxyHandler({"https":"http://127.0.0.1:7890","http":"http://127.0.0.1:7890"})))
def proxy_connect(self):
    self.set_tunnel(self._ip,443)
    self.sock=socket.create_connection((PROXY_HOST,PROXY_PORT),self.timeout)
    self._tunnel()
    self.sock=self._context.wrap_socket(self.sock,server_hostname=self._host)
api.PinnedHTTPSConnection.connect=proxy_connect
key=api.find_key()
if not key: raise RuntimeError("Configured APIMart credential missing.")
# Use the skill's DoH, pinned IP, real SNI and verified TLS. POST is submitted once;
# unknown submission outcomes are recorded and never automatically resubmitted.
ips=api.resolve_real_ips()
if not ips: raise RuntimeError("No verified DoH route; stopping before paid submissions.")
get_json=api.http_json
def safe_http(url, token, payload=None, method=None, **kwargs):
 if payload is not None and (method is None or method=="POST"):
  return api.request_via(ips[0],api.HOST,"POST",url[len("https://"+api.HOST):],token,payload,timeout=90)
 return get_json(url,token,payload,method,**kwargs)
api.http_json=safe_http
jobs=json.loads((ROOT/"generation-plan.json").read_text(encoding="utf-8"))["jobs"]
(ROOT/"receipts").mkdir(exist_ok=True)
def run(j):
 path=ROOT/"receipts"/(j["name"]+".json")
 state=json.loads(path.read_text(encoding="utf-8")) if path.exists() else {"id":j["id"],"status":"planned","model":api.MODEL,"size":j["size"],"resolution":j["resolution"],"out":j["out"]}
 def save(): path.write_text(json.dumps(state,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
 try:
  if state["status"]=="completed" and (H/j["out"]).exists(): return
  if state["status"] in ("submitting","submission_unknown") and not state.get("task_id"):
   print(j["name"],"unknown submission; no automatic retry",flush=True);return
  if not state.get("task_id"):
   state.update(status="submitting",submitted_at=time.strftime("%Y-%m-%dT%H:%M:%S%z"));save()
   print(j["name"],"submitting",flush=True)
   try:state["task_id"]=api.submit(key,j["prompt"],j["size"],j["resolution"])
   except Exception as ex:
    state.update(status="submission_unknown",error=type(ex).__name__+": "+str(ex)[:400]);save();raise
   state["status"]="queued";save()
   print(j["name"],"task",state["task_id"],flush=True)
  state["result_url"]=api.poll(key,state["task_id"],timeout_s=1200,interval=8);state["status"]="downloading";save()
  out=Path(api.download(state["result_url"],j["out"]))
  from PIL import Image
  with Image.open(out) as im:
   state.update(width=im.width,height=im.height,format=im.format,mode=im.mode);im.verify()
  state.update(status="completed",bytes=out.stat().st_size,sha256=hashlib.sha256(out.read_bytes()).hexdigest(),completed_at=time.strftime("%Y-%m-%dT%H:%M:%S%z"));save()
  print(j["name"],"completed",state["width"],state["height"],state["bytes"],flush=True)
 except Exception as ex:
  if state["status"]!="submission_unknown":state["status"]="poll_or_download_error"
  state["error"]=type(ex).__name__+": "+str(ex)[:400];save()
  print(j["name"],state["status"],state["error"],flush=True)
with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
 list(pool.map(run,jobs))
summary=[json.loads(p.read_text(encoding="utf-8")) for p in sorted((ROOT/"receipts").glob("*.json"))]
(ROOT/"generation-receipt.json").write_text(json.dumps(summary,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
print("FINISHED",sum(x["status"]=="completed" for x in summary),"/",len(jobs),flush=True)
