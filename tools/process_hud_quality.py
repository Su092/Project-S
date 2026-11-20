from pathlib import Path
from PIL import Image
from ui_cutout import extract
root=Path(__file__).resolve().parents[1]
im=Image.open(root/'_Inbox/hud-quality-apimart/attachment-frames-thin.png')
for i,name in enumerate(['blue','gold','red']):
 frame=extract(im.crop((round(i*im.width/3),0,round((i+1)*im.width/3),im.height)))
 frame.thumbnail((248,248),Image.Resampling.LANCZOS)
 out=Image.new('RGBA',(256,256));out.alpha_composite(frame,((256-frame.width)//2,(256-frame.height)//2))
 out.save(root/('Assets/_Game/Res/Texture/UI/Frame/hud_quality_'+name+'.png'))
 print(name,frame.size)
# Preserve the reading-ring GUID and retain this replacement through full HUD rebuilds.
ring=(root/'Assets/_Game/Res/Texture/UI/Frame/hud_quality_gold.png').read_bytes()
(root/'Assets/_Game/Res/Texture/UI/Widget/hud_attachment_read_ring.png').write_bytes(ring)
override=root/'_Inbox/hud-music-20261118/user-overrides/hud_attachment_read_ring.png'
override.parent.mkdir(parents=True,exist_ok=True)
override.write_bytes(ring)
