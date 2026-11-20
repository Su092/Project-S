import bpy,sys,json
from pathlib import Path
ROOT=Path(__file__).parent
sys.path.insert(0,str(ROOT))
import build_enemies as b
for i in [2,6]:
 b.build(b.MAN['assets'][i])
# Finalize only the revised family; preserve other assets.
code=(ROOT/'finalize_sources.py').read_text().replace("for name,dims,budget,res,clips in man['assets']:","for name,dims,budget,res,clips in [man['assets'][i] for i in [2,6]]:")
exec(compile(code,'finalize_sources.py','exec'),{'__name__':'__main__'})
import validate_enemies as v
for i in [2,6]:v.check(i)
code=(ROOT/'review_previews.py').read_text().replace("for spec in MAN['assets']:","for spec in [MAN['assets'][i] for i in [2,6]]:")
exec(compile(code,'review_previews.py','exec'),{'__name__':'__main__'})
exec(compile((ROOT/'final_extras.py').read_text(),'final_extras.py','exec'),{'__name__':'__main__'})
(ROOT/'PRESSURE_V002_COMPLETE').write_text('Geometry, actions, exports, Blender roundtrip, previews regenerated. Unity reimport pending.')
