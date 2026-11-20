from pathlib import Path
p=Path(__file__).with_name('build_enemies.py')
s=p.read_text().replace("-.36 if i==0 else -.22","-.43 if i==0 else -.22").replace("-.386 if i==0 else -.246","-.456 if i==0 else -.246").replace("-.396 if i==0 else -.256","-.466 if i==0 else -.256")
p.write_text(s)
