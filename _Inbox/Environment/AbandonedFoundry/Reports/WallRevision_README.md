# Wall_Straight4 用户编辑修订
已采用当前 Blender 中 ENV_Foundry_Wall_Straight4_LOD0 的修改外形，不重新生成旧墙体。
- 精确布尔合并 6 个相交封闭壳体，保留外轮廓并清除内部重叠面。
- LOD0 242 tris；LOD1 120 tris（49.6%）。两级均无退化面、非流形边、孤立顶点或正面积共面重叠。
- 尺寸约 4 × 1.046 × 4m（Blender XYZ）；4m 拼接宽/高保留。装饰厚度保留用户修改。
- UV0 材质，UV1 独立打包；移除多余基础体 UVMap。
- 静态 MeshCollider 与当前可见外轮廓一致，替换旧版大盒碰撞；渲染器移除。
- Unity 2,400 条六向抽样射线，可见/碰撞表面相差超过 1mm 的样本为0。
- 原 FBX、Prefab 和 UnityMeshes 资源原地更新，保留 .meta GUID，已有引用自动更新。未重建主场景。
- 独立检查场景：Foundry_WallRevision_Check.unity；截图：Previews/WallRevision_Unity.png。
- 新源文件：Source/Wall_Straight4_Revised.blend；编辑中的 Foundry_Layout.blend 也已保存墙段修正。
- 备份：Source/WallRevision_Backup.blend（含编辑时状态），Wall_Straight4_PreRevision.fbx。
最新闪烁修复已同步 Foundry_Modules.blend 和 Foundry_Layout.fbx；生成器会优先读取 Revised 源。导入规则与验证见 WallFlickerFix.md。
