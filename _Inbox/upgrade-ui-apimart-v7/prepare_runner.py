from pathlib import Path
root=Path(__file__).resolve().parents[2]
s=(root/"tools/generate_upgrade_ui_apimart_v6.py").read_text(encoding="utf-8").replace('out=root/"_Inbox/upgrade-ui-apimart-v6"','out=root/"_Inbox/upgrade-ui-apimart-v7"')
(root/"tools/generate_upgrade_ui_apimart_v7.py").write_text(s,encoding="utf-8")
print("Ready: source image supplied directly; no post-processing.")
