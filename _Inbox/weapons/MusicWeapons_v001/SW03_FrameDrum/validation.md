# SW03_FrameDrum 验证记录

三档LOD实际统计：

[
  {
    "name": "SM_SW03_FrameDrum_All_LOD0",
    "triangles": 9148,
    "budget": 10000,
    "degenerate": 0,
    "loose": 0,
    "weightsNormalized": true,
    "UV": true
  },
  {
    "name": "SM_SW03_FrameDrum_All_LOD1",
    "triangles": 4848,
    "budget": 5000,
    "degenerate": 0,
    "loose": 0,
    "weightsNormalized": true,
    "UV": true
  },
  {
    "name": "SM_SW03_FrameDrum_All_LOD2",
    "triangles": 1746,
    "budget": 1800,
    "degenerate": 0,
    "loose": 0,
    "weightsNormalized": true,
    "UV": true
  }
]

独立UV图集已烘焙BC/ORM/E，2048分辨率、8px扩边。已输出四视图和60度FP待机/射击/换弹预览。Unity回读、全行程自交/壁厚、等密度/岛距量化、21:9与瞄准FOV验收尚待完成。


FBX重新导入验证：pass。逐帧根运动与循环端点：pass。详细记录见 fbx-audit.json。动作预览视频为实际骨骼网格渲染（15fps，工作台贴图显示），非概念视频。


## 最终状态

制作版 / 部分验收。参考图雕花和卷首细节作了简化，当前材质预览偏PBR而非项目赛璐璐着色。横置乐器第一人称演奏面透视压缩明显，尚需调整握持角度。主体尺寸±2%、手风琴最大展开、全行程穿插和多FOV检查未全部验收。

Unity独立测试工程回读：pass。未接入正式游戏Assets。
