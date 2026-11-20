from pathlib import Path
root=Path(__file__).resolve().parents[2]
s=(root/"tools/generate_upgrade_ui_apimart_v4.py").read_text(encoding="utf-8").replace('out=root/"_Inbox/upgrade-ui-apimart-v4"','out=root/"_Inbox/upgrade-ui-apimart-v5"')
(root/"tools/generate_upgrade_ui_apimart_v5.py").write_text(s,encoding="utf-8")
print("Ready")
