"""Preserve raw art; flood-clear background BEFORE alpha-edge crop."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageChops
import json,hashlib
source=Path('_Inbox/music-combat-vfx-20261119/raw-atlas.png')
raw=Image.open(source).convert('RGB');w,h=raw.size
out=Path('Assets/_Game/Res/Texture/VFX/Music');out.mkdir(parents=True,exist_ok=True)
report={'source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'source_size':[w,h],'assets':[]}
for name,x,y in [('T_MusicWarningRing',0,0),('T_MusicWarningLane',1,0),('T_MusicSpotBeam',0,1),('T_MusicImpact',1,1)]:
 tile=raw.crop((x*w//2,y*h//2,(x+1)*w//2,(y+1)*h//2))
 lum=tile.convert('L')
 # Flood-connected near-black exterior, preserving fine white decoration.
 background=lum.copy();ImageDraw.floodfill(background,(0,0),255,thresh=16)
 floodmask=ImageChops.subtract(background,lum).point(lambda v:255 if v>0 else 0)
 alpha=lum.point(lambda v:0 if v<8 else v)
 alpha.paste(0,(0,0),floodmask)
 # True black enclosed holes are also transparent; do not fill the ring center.
 bbox=alpha.point(lambda v:255 if v>8 else 0).getbbox()
 if not bbox:raise RuntimeError('Empty tile '+name)
 pad=16;box=(max(0,bbox[0]-pad),max(0,bbox[1]-pad),min(tile.width,bbox[2]+pad),min(tile.height,bbox[3]+pad))
 rgba=Image.new('RGBA',tile.size,'white');rgba.putalpha(alpha);rgba=rgba.crop(box)
 if name=='T_MusicSpotBeam':rgba=rgba.transpose(Image.Transpose.ROTATE_90)
 # Lane chevrons point in +V (forward on the ground).
 if name=='T_MusicWarningLane':rgba=rgba.transpose(Image.Transpose.FLIP_TOP_BOTTOM)
 rgba.save(out/(name+'.png'))
 report['assets'].append({'name':name,'cell':[x,y],'alpha_crop':box,'size':list(rgba.size),'flood_then_crop':True})
Path('_Inbox/music-combat-vfx-20261119/processing.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
