from pathlib import Path
root=Path(__file__).resolve().parents[2]
src=(root/"tools/generate_upgrade_ui_apimart.py").read_text(encoding="utf-8")
src=src.replace('out=root/"_Inbox/upgrade-ui-apimart-v1"','out=root/"_Inbox/upgrade-ui-apimart-v2"')
src=src.replace('try:state["task_id"]=api.submit(key,job["prompt"],job["size"],job["resolution"])','try:\n                refs=[json.loads((root/job["reference_receipt"]).read_text(encoding="utf-8"))["result_url"]] if job.get("reference_receipt") else None\n                state["task_id"]=api.submit(key,job["prompt"],job["size"],job["resolution"],image_urls=refs)')
(root/"tools/generate_upgrade_ui_apimart_v2.py").write_text(src,encoding="utf-8")
print("Runner prepared; image processing disabled")
