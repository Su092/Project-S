import json
from pathlib import Path
from generate import generate,api,HUB,HERE
receipt=json.loads((HERE/'06_boss29-receipt.json').read_text())
job={'id':'06_boss29_corrected','size':'16:9','resolution':'1k','image_urls':[receipt['url']],'prompt':"""Edit the supplied finished WANDERING CONDUCTOR concept board. Preserve its exact layout, typography, fullbody conductor, warm cel art, railing-mounted brass note turret design, summon spotlight transfer, all numerical labels and repair diagram. Make ONE localized correction in bottom-right ALL THREE (FINALE) illustration and the two staggered-shot examples: the turrets are enemies targeting the PLAYER, not monsters. Replace ALL beast/monster silhouettes used as targets with ONE neutral slim HUMAN PLAYER silhouette. In the ALL THREE diagram show three turrets shooting three continuous red laser beams toward the SAME SINGLE human player, with the three bright beam endpoints slightly trailing behind that human's side-step motion. No beast targets, no monster victims, no multiple human targets. Keep the same antique musical turret design, no guns held by the human. The summon panel still shows the single approved triangular metronome guard; do NOT remove it. Improve the main left human scale comparison: conductor H1.3 and neutral human H1.0 must share floor baseline, human silhouette must be 1/1.3 the actual height of the conductor. Keep sufficient separation. No other redesign. Landscape16:9,1k."""}
manifest=HUB/'tools/apimart-assets.json'
m=json.loads(manifest.read_text(encoding='utf-8-sig'))
full=dict(job,id='s-music-production-20261119-06_boss29_corrected',out='Project/S/unity/refs/'+HERE.name+'/06_boss29_corrected.png',no_global_style=True)
if not any(a['id']==full['id'] for a in m['assets']):
    m['assets'].append(full)
    manifest.write_text(json.dumps(m,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
generate(job,api.find_key())
