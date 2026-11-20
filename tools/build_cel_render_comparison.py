from pathlib import Path
import json,html
from PIL import Image,ImageDraw,ImageFont,ImageChops,ImageStat
ROOT=Path(__file__).resolve().parents[1]
BASE=ROOT/'_Inbox/cel-texture-review-v1'
OUT=BASE/'comparison'
before=json.loads((OUT/'before/snapshot.json').read_text(encoding='utf-8'))
after=json.loads((OUT/'after/snapshot.json').read_text(encoding='utf-8'))
for key in ['cameraPosition','cameraRotation','fieldOfView','gameTime','width','height','models']:assert before[key]==after[key],key
font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',24)
small=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',15)
metrics=[]
for name in ['game','theater']:
    a=Image.open(OUT/'before'/f'{name}.png').convert('RGB');b=Image.open(OUT/'after'/f'{name}.png').convert('RGB')
    assert a.size==b.size==(1920,1080)
    delta=ImageStat.Stat(ImageChops.difference(a,b)).mean
    assert max(delta)>0,name+' rendered no difference'
    metrics.append(dict(view=name,mean_rgb_difference=delta))
    canvas=Image.new('RGB',(1920,1190),(19,26,35));d=ImageDraw.Draw(canvas)
    a.thumbnail((960,540));b.thumbnail((960,540))
    d.text((20,10),'替换前 · 原贴图',font=font,fill='white');d.text((980,10),'替换后 · 赛璐璐重绘',font=font,fill='white')
    canvas.paste(a,(0,55));canvas.paste(b,(960,55))
    # Second row crops the exact same image region, no changes to lighting or color.
    aa=Image.open(OUT/'before'/f'{name}.png');bb=Image.open(OUT/'after'/f'{name}.png')
    box=(480,270,1440,810)
    d.text((20,610),'同一区域原尺寸细节',font=font,fill='white');d.text((980,610),'同一区域原尺寸细节',font=font,fill='white')
    canvas.paste(aa.crop(box),(0,650));canvas.paste(bb.crop(box),(960,650))
    canvas.save(OUT/f'{name}-comparison.jpg',quality=94)
    im=Image.open(OUT/'after'/f'{name}.png');im.thumbnail((1200,675));im.save(OUT/'after'/f'{name}-preview.jpg',quality=92)
models=before['models']
for start in range(0,len(models),8):
    page=Image.new('RGB',(1200,1248),(19,26,35));draw=ImageDraw.Draw(page)
    for j,name in enumerate(models[start:start+8]):
        x=(j%2)*600;y=(j//2)*312
        draw.text((x+8,y+4),name,font=small,fill=(235,231,220))
        for k,label in enumerate(['before','after']):
            im=Image.open(OUT/label/'models'/f'{name}.png').convert('RGB');im.thumbnail((280,280))
            page.paste(im,(x+k*300,y+28));draw.text((x+k*300+8,y+30),label,font=small,fill='white')
    page.save(OUT/f'models-comparison-{start//8+1}.jpg',quality=93)
cards=[]
for name in models:
    a='before/models/'+name+'.png';b='after/models/'+name+'.png'
    cards.append(f'<article><h3>{html.escape(name)}</h3><div class="pair"><figure><img loading="lazy" src="{a}"><figcaption>原贴图</figcaption></figure><figure><img loading="lazy" src="{b}"><figcaption>新贴图</figcaption></figure></div></article>')
doc='''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>贴图替换前后 · 实际渲染对比</title>
<style>*{box-sizing:border-box}body{margin:0;background:#121a25;color:#ece8de;font:16px/1.7 system-ui,"Microsoft YaHei",sans-serif}header,main{max-width:1600px;margin:auto;padding:26px}h1{margin:0;font-size:30px}p{color:#bdc8d7}a{color:#eac885}button,select{font:inherit;border:1px solid #53647a;border-radius:6px;background:#253349;color:#f4ecdf;padding:8px 16px;margin-right:10px}.stage{position:relative;aspect-ratio:16/9;overflow:hidden;background:#000}.stage img{position:absolute;inset:0;width:100%;height:100%}.stage .new{clip-path:inset(0 0 0 50%)}.stage .line{position:absolute;left:50%;top:0;bottom:0;border-left:2px solid #efd6a1}.tag{position:absolute;top:12px;background:#101722c9;padding:4px 10px;border-radius:5px}.tag.left{left:12px}.tag.right{right:12px}.slider{width:100%;margin:20px 0}.grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:20px}article{padding:16px;border:1px solid #3b4c63;border-radius:10px;background:#1b2635}h3{font-size:16px;margin:0 0 10px;overflow-wrap:anywhere}.pair{display:grid;grid-template-columns:1fr 1fr;gap:8px}figure{margin:0}figure img{width:100%;display:block}figcaption{text-align:center;color:#aab9cc;font-size:13px}@media(max-width:800px){.grid{grid-template-columns:1fr}header,main{padding:16px}}</style>
<header><h1>贴图替换前后 · 实际渲染对比</h1><p>33 张已替换 · 35 个模型/特效对照 · 相同镜头、光照、游戏时间和渲染尺寸。Unity 实际渲染，无生成式修图。原尺寸、透明通道与资源 GUID 保留，法线等数据贴图和材质未修改。</p><p><a href="before/game.png" target="_blank">原始游戏截图</a> · <a href="after/game.png" target="_blank">替换后游戏截图</a> · <a href="game-comparison.jpg" target="_blank">静态对比图</a> · <a href="../backup-original/">原贴图备份目录</a></p></header>
<main><select id="view"><option value="game">游戏视角</option><option value="theater">剧院全景</option></select><button onclick="setSplit(100)">只看原图</button><button onclick="setSplit(0)">只看新图</button><button onclick="setSplit(50)">左右对比</button>
<div class="stage"><img id="old" src="before/game.png"><img id="new" class="new" src="after/game.png"><div class="line"></div><span class="tag left">替换前</span><span class="tag right">替换后</span></div><input class="slider" id="slider" type="range" min="0" max="100" value="50"><p>拖动滑杆检查同一区域；下方模型使用原贴图备份与当前贴图，在相同机位、姿态及预览灯光下补拍；场景原始截图保持不变。</p><div class="grid">__CARDS__</div></main>
<script>function setSplit(v){document.querySelector('.new').style.clipPath='inset(0 0 0 '+v+'%)';document.querySelector('.line').style.left=v+'%';document.querySelector('#slider').value=v}document.querySelector('#slider').oninput=e=>setSplit(e.target.value);document.querySelector('#view').onchange=e=>{document.querySelector('#old').src='before/'+e.target.value+'.png';document.querySelector('#new').src='after/'+e.target.value+'.png'}</script></html>'''
(OUT/'index.html').write_text(doc.replace('__CARDS__',''.join(cards)),encoding='utf-8')
(OUT/'render-verification.json').write_text(json.dumps(dict(same_camera=True,same_fov=True,same_game_time=True,models=len(models),views=metrics),ensure_ascii=False,indent=2),encoding='utf-8')
print('Comparison ready:',OUT/'index.html')
