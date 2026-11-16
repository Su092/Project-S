using UnityEngine;
using S.Core;
using S.View;

namespace S.App
{
    /// <summary>
    /// 唯一的场景入口：所有对象与 UI 都由代码构建（无 Prefab、无手拖引用）。
    /// 这样场景文件改动不会破坏运行时，也便于 Git 审查。
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        GameObject _gunVisual;
        void Awake()
        {
            Application.targetFrameRate = 120;
            QualitySettings.vSyncCount = 1;
            var data = SDataLoader.Get();
            if (data == null)
            {
                Debug.LogError("[S] 配置表（Assets/Resources/Data/Cfg/*.tsv）缺失或校验失败，无法启动");
                return;
            }

            // ---- 玩家 ----
            var playerGo = new GameObject("Player");
            playerGo.transform.position = new Vector3(0f, 1.2f, -6f);

            var cc = playerGo.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.34f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.4f;

            var camGo = new GameObject("MainCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(playerGo.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.62f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 78f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 300f;
            camGo.AddComponent<AudioListener>();

            var pc = playerGo.AddComponent<PlayerController>();
            pc.Cam = cam;
            pc.CameraPivot = camGo.transform;
            pc.Configure(data);

            // 后处理：Bloom + Vignette + 冷阴影暖高光（art-style §1）
            SPostFx.Apply(cam);

            // 枪口（跟随相机）
            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(camGo.transform, false);
            muzzle.transform.localPosition = new Vector3(0.22f, -0.16f, 0.55f);

            // 第一人称枪模：由 PlayerWeapons 按当前枪切换（缺件时回退为方块）
            // 模型在 Blender 里沿 -Y 建模、Z 向上；FBX 导出 -Z forward / Y up，
            // 导入 Unity 后枪管朝 +Z、上方为 +Y，符合第一人称视角，无需额外旋转。
            var gunHolder = new GameObject("GunModel");
            gunHolder.transform.SetParent(muzzle.transform, false);
            gunHolder.transform.localPosition = new Vector3(0f, -0.04f, 0.06f);
            gunHolder.transform.localRotation = Quaternion.identity;

            var weapons = playerGo.AddComponent<PlayerWeapons>();
            weapons.MuzzlePoint = muzzle.transform;
            weapons.Player = pc;
            weapons.GunModelRoot = gunHolder.transform;
            weapons.Init(data);
            weapons.RefreshGunModel();

            if (SAssets.Model("Modle_Gun_Auto") == null)
            {
                // 回退：方块枪
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = "Fallback";
                box.transform.SetParent(gunHolder.transform, false);
                box.transform.localScale = new Vector3(0.09f, 0.11f, 0.62f);
                Destroy(box.GetComponent<Collider>());
                SMat.Apply(box.GetComponent<Renderer>(),
                    SMat.Lit(new Color(0.16f, 0.17f, 0.20f), 0.55f));
            }

            // ---- 总控 ----
            var rootGo = new GameObject("GameRoot");
            var root = rootGo.AddComponent<GameRoot>();
            root.Player = pc;
            root.Weapons = weapons;
            root.Cam = cam;

            // 测试模式里只有 -scheck 需要跳过主菜单（它要立刻跑断言）。
            // -smoke 必须在「正常启动路径」下跑 —— 它断言的正是「菜单有没有正常弹出」，
            // 所以这里不能把它一起关掉（踩过：关掉后冒烟第一条就 FAIL）。
            bool selfCheck = SelfCheck.Requested;
            root.ShowMenuOnStart = !selfCheck;
            root.SelfCheckRunning = selfCheck;

            if (selfCheck) rootGo.AddComponent<SelfCheck>();
            if (SmokeTest.Requested) rootGo.AddComponent<SmokeTest>();
            if (AutoPlay.Requested) rootGo.AddComponent<AutoPlay>();

            // 音效：程序化合成，挂在 GameRoot 上（随场景销毁）
            var sfx = rootGo.AddComponent<SfxBank>();
            sfx.Init();

            // 光标交给 GameRoot 裁决，这里**不要**无条件锁。
            // 若此处锁上，而菜单随后打开（SetPaused(true) 解锁），
            // 谁最后执行谁生效 —— 结果可能是「菜单开着但光标被锁」，玩家点不到按钮。
            root.SyncCursorToState();
        }
    }
}
