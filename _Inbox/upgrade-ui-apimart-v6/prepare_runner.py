from pathlib import Path
import json,shutil
root=Path(__file__).resolve().parents[2]
p=Path(__file__).resolve().parent
plan=json.loads((p/"generation-plan.json").read_text(encoding="utf-8"))
shutil.copyfile(plan["referenceSource"],p/"reference-user.png")
s=(root/"tools/generate_upgrade_ui_apimart_v5.py").read_text(encoding="utf-8").replace('out=root/"_Inbox/upgrade-ui-apimart-v5"','out=root/"_Inbox/upgrade-ui-apimart-v6"')
s=s.replace('from pathlib import Path','from pathlib import Path\nimport base64')
needle='state["task_id"]=api.submit(key,job["prompt"],job["size"],job["resolution"],image_urls=refs)'
replacement='if job.get("reference_local"):\n                    local_ref="data:image/png;base64,"+base64.b64encode((root/job["reference_local"]).read_bytes()).decode("ascii")\n                    refs=[local_ref]+(refs or [])\n                '+needle
assert needle in s
s=s.replace(needle,replacement)
(root/"tools/generate_upgrade_ui_apimart_v6.py").write_text(s,encoding="utf-8")
print("Local image reference copied unchanged; reference input enabled.")
