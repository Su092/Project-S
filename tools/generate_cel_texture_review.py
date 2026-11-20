from pathlib import Path
import base64
import importlib.util,json,time,concurrent.futures,hashlib,sys,socket,urllib.request,urllib.parse,winreg
from PIL import Image
root=Path(__file__).resolve().parents[1]
hub=Path(sys.argv[1]).resolve()
out=root/"_Inbox/cel-texture-review-v1"
plan=json.loads((out/"generation-plan.json").read_text(encoding="utf-8"))
manifest=hub/"tools/apimart-assets.json"
m=json.loads(manifest.read_text(encoding="utf-8-sig"))
ids={j["id"] for j in plan["jobs"]}
m["assets"]=[j for j in m.get("assets",[]) if j["id"] not in ids]+[{**j,"out":str(root/j["out"])} for j in plan["jobs"]]
manifest.write_text(json.dumps(m,ensure_ascii=False,indent=2),encoding="utf-8")
spec=importlib.util.spec_from_file_location("api",hub/".cursor/skills/apimart-image/scripts/apimart_image.py")
api=importlib.util.module_from_spec(spec);spec.loader.exec_module(api)
key=api.find_key()
if not key:raise RuntimeError("Configured API credential missing")
with winreg.OpenKey(winreg.HKEY_CURRENT_USER,r"Software\Microsoft\Windows\CurrentVersion\Internet Settings") as registry:
    proxy=winreg.QueryValueEx(registry,"ProxyServer")[0]
if ";" in proxy or "=" in proxy:
    options=dict(p.split("=",1) for p in proxy.split(";") if "=" in p);proxy=options.get("https",options.get("http",""))
endpoint=urllib.parse.urlparse(proxy if "://" in proxy else "http://"+proxy)
if not endpoint.hostname or not endpoint.port:raise RuntimeError("System proxy unavailable")
urllib.request.install_opener(urllib.request.build_opener(urllib.request.ProxyHandler({"https":endpoint.geturl(),"http":endpoint.geturl()})))
def connect(self):
    self.set_tunnel(self._ip,443)
    self.sock=socket.create_connection((endpoint.hostname,endpoint.port),self.timeout)
    self._tunnel()
    self.sock=self._context.wrap_socket(self.sock,server_hostname=self._host)
api.PinnedHTTPSConnection.connect=connect
ips=api.resolve_real_ips()
if not ips:raise RuntimeError("No verified API route; no submission")
original=api.http_json
def safe_http(url,token,payload=None,method=None,**kwargs):
    if payload is not None and (method is None or method=="POST"):
        return api.request_via(ips[0],api.HOST,"POST",url[len("https://"+api.HOST):],token,payload,timeout=90)
    return original(url,token,payload,method,**kwargs)
api.http_json=safe_http
def run(job):
    receipt=out/"receipts"/(job["name"]+".json");receipt.parent.mkdir(exist_ok=True)
    state=json.loads(receipt.read_text(encoding="utf-8")) if receipt.exists() else {"id":job["id"],"status":"planned","model":api.MODEL}
    def save():receipt.write_text(json.dumps(state,ensure_ascii=False,indent=2),encoding="utf-8")
    try:
        target=root/job["out"]
        if state["status"]=="completed" and target.exists():return
        if state["status"] in ("submitting","submission_unknown") and not state.get("task_id"):raise RuntimeError("Unknown paid submission; do not resubmit")
        if not state.get("task_id"):
            state.update(status="submitting",submitted_at=time.strftime("%Y-%m-%dT%H:%M:%S%z"));save()
            print(job["name"],"submitting",flush=True)
            try:
                refs=[json.loads((root/job["reference_receipt"]).read_text(encoding="utf-8"))["result_url"]] if job.get("reference_receipt") else None
                if job.get("reference_local"):
                    local_ref="data:image/png;base64,"+base64.b64encode((root/job["reference_local"]).read_bytes()).decode("ascii")
                    refs=[local_ref]+(refs or [])
                state["task_id"]=api.submit(key,job["prompt"],job["size"],job["resolution"],image_urls=refs)
            except Exception:
                state["status"]="submission_unknown";save();raise
            state["status"]="submitted";save();print(job["name"],state["task_id"],flush=True)
        state["result_url"]=api.poll(key,state["task_id"],timeout_s=1200,interval=8)
        state["status"]="downloading";save()
        target.parent.mkdir(parents=True,exist_ok=True);api.download(state["result_url"],str(target))
        with Image.open(target) as im:state.update(width=im.width,height=im.height,mode=im.mode);im.verify()
        state.update(status="completed",out=job["out"],bytes=target.stat().st_size,sha256=hashlib.sha256(target.read_bytes()).hexdigest());save()
        print(job["name"],"COMPLETE",state["width"],state["height"],flush=True)
    except Exception as ex:
        state["error"]=type(ex).__name__+": "+str(ex)[:250];save()
        print(job["name"],"ERROR",state["error"],flush=True)
with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:list(pool.map(run,plan["jobs"]))
states=[json.loads(p.read_text(encoding="utf-8")) for p in sorted((out/"receipts").glob("*.json"))]
(out/"generation-receipt.json").write_text(json.dumps(states,ensure_ascii=False,indent=2),encoding="utf-8")
print("FINISHED",sum(s["status"]=="completed" for s in states),"/",len(plan["jobs"]),flush=True)
