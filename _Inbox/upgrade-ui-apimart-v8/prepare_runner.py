from pathlib import Path
root=Path(__file__).resolve().parents[2]
s=(root/"tools/generate_upgrade_ui_apimart_v7.py").read_text(encoding="utf-8").replace('out=root/"_Inbox/upgrade-ui-apimart-v7"','out=root/"_Inbox/upgrade-ui-apimart-v8"')
(root/"tools/generate_upgrade_ui_apimart_v8.py").write_text(s,encoding="utf-8")
print("Ready: clean staff supplied as visual reference.")
