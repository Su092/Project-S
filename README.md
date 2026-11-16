# ProjectS

第一人称射击与局内配件构筑。设计、规格和任务统一保存在 <HUB>/Project/S，机器实际路径见枢纽 machine/profile.local.json。

## 打开与运行

使用 Unity 6000.6.4f1 打开当前工程，打开 Assets/Scenes/Main.unity 并 Play。等待资源预加载完成，在菜单从全部武器中选择一把再开始。
环境：URP 17.6.0、Input System 1.20.0、Addressables 2.11.2。

| 操作 | 按键 |
|---|---|
| 移动 / 视角 | WASD / 鼠标 |
| 射击 / 装填 | 左键 / R |
| 冲刺 / 跳跃 | Shift / 空格 |
| 切枪 | 1 / 2 |
| 背包 / 暂停 | Tab / Esc |

## 目录

Assets/_Game/App 为启动及实机检查，Core 为规则与配置，View 为表现及交互，Editor 为编辑器工具。
Assets/_Game/Res 包含 Cfg、Model、Texture、UIPrefabs、Font、Shaders 和 Prefab；Assets/AddressableAssetsData 为资源分组。
_Inbox 保存原料；Shots / Logs / Build / Library 为本机产物，不提交。

## 验证

```powershell
powershell -ExecutionPolicy Bypass -File scripts/check.ps1 -Task tests
dotnet run --project tools/loadout-tests -c Release
```

统一入口支持 all / tests / setup / assets / ui / build / smoke / shots。Unity 相关任务先确认工程编辑器与数据库锁未占用，再连续执行需要的独占步骤。
tests 包含纯规则和 UI 素材分类两项，规则通过不代表整个任务通过。本轮缺十二个新配件独立图标，详情见枢纽 memory/CURRENT.md。
项目文件 tools/loadout-tests/loadout-tests.csproj 已纳入 Git，依赖 .NET SDK。
专项检查在 Editor/S*Check 中，日志写 Logs，截图写 Shots。完整构建与实机自检的实际完成状态以枢纽 CURRENT 为准。
