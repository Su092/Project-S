# 墙段闪烁修复 — 导入与绘制链路
发现原始 Wall_Straight4.fbx 导入后有 3 个 MeshRenderer（LOD0、LOD1、COL），没有 LODGroup。直接放置 FBX 会同时绘制共面的不同网格。上一版仅配置了生成的 Prefab，未覆盖原始模型直接使用的路径。

修复：
1. FoundryWallImportSafety.cs 显式处理此 FBX：碰撞代理删除 Renderer，保留精确 MeshCollider；创建唯一 LODGroup，禁用交叉淡化，分配两级渲染器。
2. Prefab 更新前删除原有/继承的所有 LODGroup，重建唯一控制组；移除重复碰撞组件。
3. 在 Blender 中显式三角化 Boolean 输出，固定凹多边形/薄面三角化，不交由导入器重新选择。
4. FBX、UnityMeshes、Prefab 原地更新，保留 GUID。修改后的独立源、模块源、当前布局源及布局 FBX 已同步。
5. 生成器优先读取 Wall_Straight4_Revised.blend，以后重新生成不会用旧壳体覆盖编辑版。

验证：
- 原始 FBX 与 Prefab：各 1 个 LODGroup、2 个可见 Renderer、0 个碰撞 Renderer。
- 实际导入后的三角面正面积共面重叠：LOD0=0，LOD1=0。
- 96 组离屏渲染：两种资源 × 强制LOD0/强制LOD1/自动 × 4距离 × 4方向。
- 使用红/绿无光照替换材质，逐帧读回像素识别实际绘制的 LOD。96/96 样本只绘制一级，错误数0。
- URP 下初始 OnWillRenderObject 回调法没有返回记录，已改为像素读回验证，最终报告只采用像素结果。
- 碰撞与可见面 2400 条射线抽样，超过1mm偏差的样本数0。

范围：此墙段全部导入/源文件路径；没有重建生产 Main 场景或改动战斗逻辑。
检查结果：WallRenderValidation.txt、ImportedTriangleOverlap.json、WallRevision_Unity.txt。
普通材质检查图：WallRevision_Unity.png。红/绿图为LOD专项诊断，不是最终材质。
