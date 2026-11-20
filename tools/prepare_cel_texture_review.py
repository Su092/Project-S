from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import json,hashlib,collections
root=Path(__file__).resolve().parents[1]
out=root/'_Inbox/cel-texture-review-v1'
rows=json.loads((out/'source-material-inventory.json').read_text(encoding='utf-8'))
sources=list(dict.fromkeys(r['texture'] for r in rows if r['texture']))
inventory=[]
for i,src in enumerate(sources):
 p=root/src
 with Image.open(p) as im:
  alpha=im.getchannel('A').getextrema() if im.mode=='RGBA' else None
  inventory.append({'number':i+1,'source':p.relative_to(root).as_posix(),'name':p.stem,'size':list(im.size),'mode':im.mode,'alpha_range':alpha,'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'materials':[r['material'].replace('\\','/') for r in rows if r['texture']==src]})
(out/'source-inventory.json').write_text(json.dumps(inventory,ensure_ascii=False,indent=2),encoding='utf-8')
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',16)
for start in range(0,len(inventory),12):
 sheet=Image.new('RGB',(1200,996),(35,38,46));draw=ImageDraw.Draw(sheet)
 for k,item in enumerate(inventory[start:start+12]):
  im=Image.open(root/item['source']).convert('RGB');im.thumbnail((288,288))
  x=(k%4)*300;y=(k//4)*332
  sheet.paste(im,(x+(300-im.width)//2,y+(288-im.height)//2))
  label=f"{item['number']:02} {item['name']}"
  draw.text((x+6,y+290),label[:32],font=font,fill=(245,237,214))
  draw.text((x+6,y+310),str(tuple(item['size'])),font=font,fill=(160,178,200))
 sheet.save(out/f'sources-{start//12+1}.jpg',quality=90)
print(json.dumps([{'number':i['number'],'name':i['name'],'size':i['size'],'alpha':i['alpha_range']} for i in inventory],indent=2))
