"""Build a local review page from Unity-rendered animation samples."""
from pathlib import Path
from PIL import Image
import json
root=Path(__file__).resolve().parents[1]
shots=root/"Shots/upgrade-ui"
frames=[Image.open(p).convert("RGB") for p in sorted((shots/"frames").glob("frame-*.png"))]
if len(frames)!=json.loads((shots/"recording.json").read_text())["frames"]:raise RuntimeError("Incomplete Unity capture")
frames[0].save(shots/"upgrade-flow.gif",save_all=True,append_images=frames[1:],duration=83,loop=0,optimize=False)
html="""<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>音律升级 · 动效预览</title><style>body{margin:0;background:#121722;color:#eee8d5;font:16px/1.7 system-ui}main{max-width:1280px;margin:auto;padding:30px}h1{font-size:26px}img{width:100%;border:1px solid #504839;border-radius:10px}section{margin:30px 0}.grid{display:grid;grid-template-columns:1fr 1fr;gap:20px}a{color:#efd787}button{padding:10px 24px;cursor:pointer;background:#ddd0ae;border:0;border-radius:6px}small{color:#aab1c1}</style><main><h1>音律升级 · 引擎内动效预览</h1><p>线谱沿固定单曲线左入（约0.93秒，速度140%），图标按弧长跟随；浮动音符放大50%、位于线谱前层，线谱及音符增强独立柔光 → 图标上移、框展开淡入 → 暂停选择 → 框收起 → 选中沿曲线入左上角、其余随线谱右出（出画速度为入画的1.3倍，约0.71秒）。</p><img id="flow" src="upgrade-flow.gif"><button onclick="document.querySelector('#flow').src='upgrade-flow.gif?t='+Date.now()">重播</button><small>按实际动画时间轴采样的 Unity 画面；预览自动选择中间项，游戏内等待玩家点击。</small><section><h2>等待选择</h2><img src="03-choice.png"></section><div class="grid"><section><h2>信息框展开</h2><img src="02-opening.png"></section><section><h2>选择入包</h2><img src="05-flight.png"></section><section><h2>金色子弹框</h2><img src="06-gold.png"></section><section><h2>宽屏布局</h2><img src="03-choice-wide.png"></section></div><p>绿色：乐器；蓝色：默认 / 普通子弹；金色：沿用当前稀有子弹；红色：衍生配件。新配件品质、满包替换页按约定留待后续。</p></main></html>"""
(shots/"index.html").write_text(html,encoding="utf-8")
print("Review page and",len(frames),"frame animation ready")
