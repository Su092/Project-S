from pathlib import Path
root=Path(__file__).resolve().parents[2]
s=(root/"tools/generate_upgrade_ui_apimart_v3.py").read_text(encoding="utf-8").replace('out=root/"_Inbox/upgrade-ui-apimart-v3"','out=root/"_Inbox/upgrade-ui-apimart-v4"')
(root/"tools/generate_upgrade_ui_apimart_v4.py").write_text(s,encoding="utf-8")
print("Ready")
