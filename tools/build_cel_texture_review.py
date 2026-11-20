from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import json,html,hashlib,csv,collections
root=Path(__file__).resolve().parents[1]
out=root/'_Inbox/cel-texture-review-v1'
plan=json.loads((out/'generation-plan.json').read_text(encoding='utf-8'))
sources=json.loads((out/'source-inventory.json').read_text(encoding='utf-8'))
byname={x['name']:x for x in sources}
labels={'instruments':'乐器','enemies':'敌人','devices':'装置','effects':'特效模型','environment':'剧院场景'}
names={'T_SW01_Violin_BC':'小提琴','T_SW02_Kalimba_BC':'拇指琴','T_SW03_FrameDrum_BC':'手鼓','T_SW04_Cello_BC':'大提琴','T_SW05_Harp_BC':'竖琴','T_SW06_Accordion_BC':'手风琴','Theater_Brass_BaseColor':'黄铜','Theater_Ivory_BaseColor':'象牙白墙面','Theater_Lamp_BaseColor':'灯罩','Theater_Midnight_BaseColor':'深蓝饰面','Theater_Stone_BaseColor':'石材','Theater_Trim_BaseColor':'建筑饰边','Theater_Walnut_BaseColor':'胡桃木','Theater_Wine_BaseColor':'酒红饰面'}
installed=(out/'import-completed.json').exists()
overrides=json.loads((out/'review-overrides.json').read_text(encoding='utf-8')) if (out/'review-overrides.json').exists() else {}
records=[]
cards=[]
for j in plan['jobs']:
 src=byname[j['name']]
 override=overrides.get(j['name'],{})
 target=root/override.get('generated',j['out'])
 receipt=out/override.get('receipt','receipts/'+j['name']+'.json')
 state=json.loads(receipt.read_text(encoding='utf-8')) if receipt.exists() else {}
 complete=state.get('status')=='completed' and target.exists()
 original=(out/'backup-original'/src['source']) if installed else (root/src['source'])
 unchanged=hashlib.sha256(original.read_bytes()).hexdigest()==src['sha256']
 dims=None;mode=None
 if complete:
  with Image.open(target) as im:dims=list(im.size);mode=im.mode;im.verify()
 record={'number':src['number'],'name':j['name'],'category':labels[j['group']],'source':src['source'],'generated':target.relative_to(root).as_posix(),'status':state.get('status','planned'),'source_unchanged':unchanged,'generated_size':dims,'generated_mode':mode,'review_status':'已批准入库并完成固定视角渲染对比' if installed else '待人工审核，未贴模验证','source_alpha_range':src['alpha_range']}
 records.append(record)
 title=names.get(j['name'],j['name'])
 old=('backup-original/' if installed else '../../')+src['source'];new=target.relative_to(out).as_posix()
 n=src['number'];note='含透明通道：入库前需检查/恢复透明覆盖。' if src['alpha_range'] and src['alpha_range'][0]<255 else '入库前需验证 UV / 色板分区 / 平铺接缝。'
 if override.get('note'):note=override['note']
 cards.append(f'''<article data-group="{j['group']}" data-search="{html.escape(title+' '+j['name']+' '+labels[j['group']])}">
 <div class="cardhead"><h2>{n:02} · {html.escape(title)}</h2><span>{labels[j['group']]}</span></div>
 <p class="file">{j['name']}</p><div class="pair">
 <figure><img loading="lazy" src="{old}" alt="原贴图"><figcaption>原贴图 · {'×'.join(map(str,src['size']))}</figcaption></figure>
 <figure><img loading="lazy" src="{new}" alt="APIMart 新贴图" onerror="this.style.opacity=.1"><figcaption>赛璐璐重绘 · {'×'.join(map(str,dims)) if dims else state.get('status','排队中')}</figcaption></figure></div>
 <div class="overlay"><img loading="lazy" src="{old}" alt="原图底层"><img loading="lazy" class="newlayer" src="{new}" alt="新图叠层"></div>
 <label class="blend">新图透明度 <input type="range" min="0" max="100" value="50" oninput="this.closest('article').querySelector('.newlayer').style.opacity=this.value/100"></label>
 <p class="note">{note}</p><div class="links"><a href="{old}" target="_blank">打开原图</a><a href="{new}" target="_blank">打开新图</a></div>
 </article>''')
counts=collections.Counter(r['status'] for r in records)
doc='''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>赛璐璐贴图审核</title>
<style>
*{box-sizing:border-box}body{margin:0;background:#101722;color:#e8e3d7;font:15px/1.6 system-ui,"Microsoft YaHei",sans-serif}header{padding:32px max(24px,5vw) 20px;background:#192331;border-bottom:1px solid #435166}h1{margin:0;font-size:30px}header p{max-width:1000px;color:#b9c5d5;margin:10px 0}.stats{color:#e9c989;font-size:16px}.toolbar{position:sticky;top:0;z-index:5;background:#182231ee;padding:14px max(24px,5vw);display:flex;gap:10px;flex-wrap:wrap;border-bottom:1px solid #38475b}button,input[type=search]{font:inherit;padding:7px 12px;background:#253449;border:1px solid #4c6079;border-radius:6px;color:#f3ead9}button{cursor:pointer}button.active{background:#c5a971;color:#101722}main{padding:24px max(24px,5vw);display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:24px}article{background:#192330;border:1px solid #354457;border-radius:12px;padding:18px;overflow:hidden}.cardhead{display:flex;align-items:center;justify-content:space-between;gap:10px}.cardhead h2{margin:0;font-size:18px;overflow-wrap:anywhere}.cardhead span{white-space:nowrap;color:#e5c581;font-size:13px}.file{font-size:12px;color:#95a6bc;overflow-wrap:anywhere}.pair{display:grid;grid-template-columns:1fr 1fr;gap:12px}figure{margin:0}figure img,.overlay{background:#080a0e;aspect-ratio:1;width:100%;object-fit:contain;border:1px solid #334153}figcaption{font-size:12px;color:#b6c4d6;margin-top:7px}.note{font-size:12px;color:#abb9ca}.links{display:flex;gap:18px}a{color:#dfbd7a}article[hidden]{display:none}.overlay{position:relative;display:none}.overlay img{position:absolute;inset:0;width:100%;height:100%;object-fit:contain}.newlayer{opacity:.5}.blend{display:none;font-size:13px;margin:12px 0}.blend input{width:60%;vertical-align:middle}body.compare .pair{display:none}body.compare .overlay{display:block}body.compare .blend{display:block}@media(max-width:1000px){main{grid-template-columns:1fr}}footer{padding:15px 5vw 40px;color:#9fafc5}
</style>
<header><h1>全模型 · 赛璐璐贴图审核</h1><div class="stats">__STATS__</div><p>每张新图均由 APIMart 使用对应原贴图作为参考生成。这里展示原始生成结果，尚未入库或绑定模型。当前游戏贴图保持原样。</p><p>查看色块、纹理简化和材质配色；切换“叠加检查”可检查布局漂移。图生图结果不能保证逐像素 UV 对齐，最终效果需在批准后贴回模型核对。</p></header>
<div class="toolbar"><button class="active" data-filter="all">全部</button><button data-filter="instruments">乐器</button><button data-filter="enemies">敌人</button><button data-filter="devices">装置</button><button data-filter="effects">特效模型</button><button data-filter="environment">剧院场景</button><input id="search" type="search" placeholder="搜索文件名或类别"><button id="mode">叠加检查</button></div><main>__CARDS__</main><footer>数据贴图（法线、ORM、发光等）未重绘。原图校验与任务回执保存在当前目录；源文件保持不变。审核通过后再处理透明通道、尺寸、接缝与正式绑定。</footer>
<script>
let group='all';function filter(){let q=document.querySelector('#search').value.toLowerCase();document.querySelectorAll('article').forEach(c=>c.hidden=!(group==='all'||c.dataset.group===group)||!c.dataset.search.toLowerCase().includes(q))}
document.querySelectorAll('[data-filter]').forEach(b=>b.onclick=()=>{group=b.dataset.filter;document.querySelectorAll('[data-filter]').forEach(x=>x.classList.toggle('active',x===b));filter()});
document.querySelector('#search').oninput=filter;document.querySelector('#mode').onclick=function(){let on=document.body.classList.toggle('compare');this.textContent=on?'并排对比':'叠加检查';this.classList.toggle('active',on)};
</script></html>'''
doc=doc.replace('__STATS__',f"{counts.get('completed',0)} / {len(records)} 张已生成 · 原图校验 {'全部一致' if all(r['source_unchanged'] for r in records) else '存在变更，需核对'}").replace('__CARDS__','\n'.join(cards))
if installed:
 doc=doc.replace('这里展示原始生成结果，尚未入库或绑定模型。当前游戏贴图保持原样。','已批准入库，游戏贴图已替换；原图展示来自备份。<a href="comparison/index.html">查看实际渲染前后对比</a>。')
 doc=doc.replace('最终效果需在批准后贴回模型核对。','模型渲染对比已另行保存；此页新图仍展示 APIMart 原始输出。')
 doc=doc.replace('源文件保持不变。审核通过后再处理透明通道、尺寸、接缝与正式绑定。','原图已备份。正式入库使用原尺寸，并逐像素保留原透明通道，材质与资源 GUID 未变。')
(out/'review.html').write_text(doc,encoding='utf-8')
(out/'review-status.json').write_text(json.dumps(records,ensure_ascii=False,indent=2),encoding='utf-8')
with (out/'review.csv').open('w',encoding='utf-8-sig',newline='') as f:
 w=csv.writer(f);w.writerow(['序号','类别','文件名','原图','新图','任务状态','审核状态'])
 for r in records:w.writerow([r['number'],r['category'],r['name'],r['source'],r['generated'],r['status'],r['review_status']])
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',16)
for start in range(0,len(records),12):
 sheet=Image.new('RGB',(1200,996),(35,38,46));draw=ImageDraw.Draw(sheet)
 for k,item in enumerate(records[start:start+12]):
  x=(k%4)*300;y=(k//4)*332;p=root/item['generated']
  if p.exists():
   im=Image.open(p).convert('RGB');im.thumbnail((288,288));sheet.paste(im,(x+(300-im.width)//2,y+(288-im.height)//2))
  draw.text((x+6,y+290),f"{item['number']:02} {item['name']}"[:32],font=font,fill=(245,237,214))
  draw.text((x+6,y+310),item['status'],font=font,fill=(160,178,200))
 sheet.save(out/f'generated-overview-{start//12+1}.jpg',quality=90)
print(json.dumps({'counts':dict(counts),'total':len(records),'sources_unchanged':all(r['source_unchanged'] for r in records),'review':'_Inbox/cel-texture-review-v1/review.html'},ensure_ascii=False))
