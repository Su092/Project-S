from pathlib import Path
import json
root=Path(__file__).resolve().parents[1]
parent=root/'_Inbox/cel-texture-review-v1'
out=parent/'fx-color-revision'
out.mkdir(exist_ok=True)
sources=json.loads((parent/'source-inventory.json').read_text(encoding='utf-8'))
jobs=[]
for item in sources:
 if item['number'] not in (7,12):continue
 name=item['name']
 jobs.append({'name':name,'id':'s-cel-'+name.lower()+'-rgb-revision-20261120-v2','size':'1:1','resolution':'2k','reference_local':item['source'],'out':f'_Inbox/cel-texture-review-v1/fx-color-revision/generated/{name}.png','no_global_style':True,'source_sha256':item['sha256'],'prompt':"""Create a MODEL EFFECT RGB BASE-COLOR TEXTURE using the attached original texture as its color reference. IMPORTANT: this original texture has a wave-shaped alpha mask but its RGB COLOR CHANNELS are a uniform light golden-peach paint. We will preserve its original alpha mask separately in the game. Your output must therefore be ONLY the RGB PAINT FIELD, covering 100 percent of the entire square canvas with the same flat uniform warm light cel-animation gold/ivory #F7CD86. IGNORE the wave-shaped transparency when painting. Do NOT draw the wave, do NOT draw any symbol, do NOT include transparency, no checkerboard, no black background, no white background, no borders. All 2048x2048 pixels are clean uniform warm gold paint matching the source RGB, no gradient, no texture, no light bloom, no text. This output is a uniform material color channel, not a VFX illustration. The alpha shape and falloff belong to the original separate channel and must NOT be baked into this RGB painting."""})
(out/'generation-plan.json').write_text(json.dumps({'provider':'APIMart','model':'gpt-image-2','review_only':True,'import_authorized':False,'reason':'Correct alpha/checkerboard baked into RGB on first generation; preserve original alpha separately after approval.','jobs':jobs},ensure_ascii=False,indent=2),encoding='utf-8')
s=(root/'tools/generate_score_controls_outlined_v3.py').read_text(encoding='utf-8').replace('_Inbox/score-controls-outlined-review-v3','_Inbox/cel-texture-review-v1/fx-color-revision')
(root/'tools/generate_cel_fx_revision.py').write_text(s,encoding='utf-8')
(parent/'review-overrides.json').write_text(json.dumps({j['name']:{'generated':j['out'],'receipt':f"fx-color-revision/receipts/{j['name']}.json",'note':'特效仅重绘 RGB 色场，批准入库时必须恢复原始透明通道；首版透明棋盘/轮廓烘焙问题已另行修正。'} for j in jobs},ensure_ascii=False,indent=2),encoding='utf-8')
print('Prepared two known-completed FX color revisions, no duplicate task submission.')
