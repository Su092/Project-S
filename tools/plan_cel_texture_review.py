from pathlib import Path
import json
root=Path(__file__).resolve().parents[1]
out=root/'_Inbox/cel-texture-review-v1'
inventory=json.loads((out/'source-inventory.json').read_text(encoding='utf-8'))
base="""EDIT THE PROVIDED TEXTURE IMAGE ITSELF. Output one production 2D BASE-COLOR TEXTURE, not a picture of an object. The attached original texture is the exact spatial template. Pixel registration is critical: keep EVERY island silhouette, color-region boundary, swatch, stripe, panel, small part, and unused gap at its identical normalized XY location and identical normalized size. Do not repack, rotate, mirror, move, add, merge or delete UV islands. Maintain the full canvas, no cropping, no padding, no frame, no labels, no text added, no watermark, no checkerboard. Repaint only the existing surfaces toward polished Japanese animation CEL-SHADED visual design: clean deliberate color blocks, limited harmonious palette, clearly separated two or three paint values where surface detail exists, crisp controlled contours, simplified broad stylized material strokes, no photographic grain, no gritty noise, no grunge, no blurry airbrush gradients, no realistic glossy baked lighting, no ambient vignette. Retain original material/color identity and musical-theater palette: warm ivory, readable dark ink navy, burgundy lacquer, warm walnut, restrained brass gold. Improve muddy very-dark painted midtones where appropriate without changing palette assignments. This is albedo; actual light/shadows will be supplied by the existing game shader. Do not draw scene lighting, cast shadows, or a 3D render. """
jobs=[]
for item in inventory:
 name=item['name'];src=item['source'];n=item['number']
 if n<=6:
  detail="""This is a tightly packed instrument UV atlas. Precisely match all intricate parts, long strings and rods, instrument-body silhouettes and decorative curls. Keep the unused black/transparent space and all holes unchanged. Simplify dense wood grain to a few clean, restrained graphic streaks WITHIN the original wooden islands. Use warm readable wood with crisp limited darker/lighter paint accents rather than muddy brown or realistic wavy grain. Gold metal should have two clean graphic gold values, ivory should be clean and creamy. Preserve all strings and fine part boundaries. Do not outline the UV islands with new thick black borders; island borders are seams, not object outlines. """
  group='instruments'
 elif n in (8,11,13):
  detail="""This source is a FOUR-QUADRANT COLOR PALETTE ATLAS. Keep the exact 2x2 four equal rectangles, their dividing lines at 50% width/height and their original positions. Repaint each quadrant as ONE clean uniform cel-animation color close to the reference hue: ivory top-left, brass-gold top-right, burgundy bottom-left, ink-purple bottom-right. No gradients, NO illustrations, NO musical notes, NO texture grain, NO borders, NO shadow bands inside these sampling swatches. Color palette only. """
  group='effects'
 elif n in (7,12):
  detail="""This is a translucent effect color texture. Maintain the exact original effect coverage and original alpha silhouette/falloff, no new design. Retain warm pale gold color in a clean consistent animation fill. Preserve transparent background if supported; never add black outlines, panels, logos, a background scene, extra rings, or extra symbols. Focus on the reference effect itself, not on an illustration of wind or sound. """
  group='effects'
 elif n==10:
  detail="""This is a translucent red curtain effect base-color texture. Preserve its source layout, relative alpha/coverage pattern and red material identity. Use a clean flat cel-animation red/crimson fill with any original value zones expressed clearly. No drawn stage, curtain folds not in source, new ornaments, labels, or decorative borders. """
  group='effects'
 elif n==9:
  detail="""This is a VERTICAL STRIP MATERIAL COLOR ATLAS. Preserve all vertical stripe widths, exact x positions and color order. Use clean flat animation paint colors with restrained richer midtones. NO gradient within color sampling stripes, NO shapes, NO objects, NO extra subdivisions. """
  group='effects'
 elif n in (14,15,17,18,19,20,21,24):
  detail="""This enemy model uses a COLOR-SWATCH UV ATLAS, NOT a conventional picture of the enemy. The large muted field and the small row of rectangular color swatches at the bottom MUST stay in exactly the same places, heights, widths and ordering. The large field must remain uniform. Repaint swatches as clean flat cel-animation colors, keep each material hue identity, brighten muddy brown/brass midtones modestly, retain clear ivory versus ink-dark contrast. NO objects, no creature drawing, no face, no armor plates, no hatching, no gradients or highlights inside sampling swatches, no extra boundaries. Only improve the existing palette for use with a cel-lighting shader. """
  group='enemies'
 elif n==16:
  detail="""This paper-ghost source has printed music staff/note marks above a bottom palette row. Preserve every staff location, all large sheet regions, and exact bottom color-swatch rectangles. Make parchment clean warm ivory with flat subtle paint zones, retain crisp dark-brown printed staff marks in place; do not add text, new notation blocks, perspective or curled-paper illustrations. """
  group='enemies'
 elif n==22:
  detail="""This is a plaid/tartan material atlas with a bottom color-swatch row. Keep every rectangular panel boundary, line direction, diagonal crosshatch region, original pattern scale and bottom swatch boundary precisely. Simplify plaid into crisp flat graphic lines and clean burgundy/navy/gold blocks. Remove fine noisy fabric weave; NO new pattern, NO relocation or rescaling of checks. """
  group='enemies'
 elif n==23:
  detail="""This is a 4x4 tiled material atlas mixing flat palette, printed staff, plaid and wood patches. Keep every 4x4 cell boundary, each cell's assigned content, staff positions and plaid scale EXACT. Repaint within each existing cell in clean cel-animation colors; wood becomes restrained graphic streaks, cloth becomes crisp graphic plaid, uniform palette tiles remain completely flat. """
  group='enemies'
 elif n==25:
  detail="""This is a SPOTLIGHT DEVICE packed atlas with mostly unused black area and a few small narrow colored islands/strips near the lower half. Preserve exact tiny island positions and proportions, all unused black space, and original strip ordering. Repaint ONLY the existing occupied regions with clear cel-animation ivory, brass, burgundy and dark navy colors. Do not fill empty area with an illustration or enlarge small islands. """
  group='devices'
 elif n==31:
  detail="""This is a horizontal architectural TRIM SHEET. Keep all horizontal bands at exactly the original y positions and heights; keep all repeating ornamental lines, trim shapes and their scale/register. Use crisp simplified gold ornament strokes and clean large ivory/navy/burgundy/walnut bands, no mottled noisy background. Preserve horizontal seamless repeat across left/right edges. No new design or swapped strips. """
  group='environment'
 else:
  detail="""This is a SEAMLESS TILEABLE architectural surface BASE-COLOR material. Cover the full canvas edge to edge and ensure opposite edges tile seamlessly in both axes. Preserve original material type, hue family, any original panel/plank/stone seam positions and directions and repeat scale. Simplify mottled grain to broad low-contrast flat paint patches with crisp yet restrained shapes, no noisy stippling. No illustration, no objects, no perspective, no central spotlight, no border, no realistic directional light or baked shadow. """
  if n==28:detail+="This is a uniform warm ivory-gold lamp diffuser: keep it flat and uniform, no new pattern or bulb illustration."
  if n==32:detail+="Retain the exact horizontal wood planks and their seam positions. Replace dense wood grain with a few stylized clean long strokes."
  if n==30:detail+="Retain the exact existing stone tile grid and seams. Broad clean ivory-gray stone values, no realistic pores."
  group='environment'
 jobs.append({'name':name,'id':'s-cel-'+name.lower()+'-20261120-v1','group':group,'size':'1:1','resolution':'2k','out':f'_Inbox/cel-texture-review-v1/generated/{name}.png','reference_local':src,'source_sha256':item['sha256'],'source_size':item['size'],'source_alpha_range':item['alpha_range'],'no_global_style':True,'prompt':base+detail+' Return ONLY the edited texture, full square 2048x2048.'})
plan={'provider':'APIMart','model':'gpt-image-2','review_only':True,'import_authorized':False,'scope':'All current music-theme model base-color textures; data maps and UI unchanged.','jobs':jobs}
(out/'generation-plan.json').write_text(json.dumps(plan,ensure_ascii=False,indent=2),encoding='utf-8')
s=(root/'tools/generate_score_controls_outlined_v3.py').read_text(encoding='utf-8')
s=s.replace('_Inbox/score-controls-outlined-review-v3','_Inbox/cel-texture-review-v1')
(root/'tools/generate_cel_texture_review.py').write_text(s,encoding='utf-8')
print('Prepared',len(jobs),'reference-based 2k review jobs; no changes to Assets.')
