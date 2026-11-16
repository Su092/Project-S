using System.Text;
using UnityEditor;
using UnityEngine;
using S.Core;

namespace S.EditorTools
{
    /// <summary>
    /// 配置表**能不能作为 TextAsset 被读到**的编辑器侧断言。
    ///
    /// 为什么单独做这一步：`.tsv` 不在 Unity 默认的文本扩展名清单里，
    /// 没有 <see cref="STsvImporter"/> 时导入结果**根本不是 TextAsset**，
    /// 而**编译期完全看不出来** —— 打包也成功，只是运行时「缺表」。
    /// 这里用 AssetDatabase 按资产路径断言（资产已搬到 `Assets/_Game/Res/Cfg/`，
    /// 不走 Resources），把「导入器没生效」和「路径写错」分开。
    ///
    /// ⚠ 这里刻意不用 `SRes`：SRes 依赖 Addressables 分组已构建，
    /// 而本断言的职责是「文件本身导对没有」。两者分开，分组没建时也能定位问题。
    /// </summary>
    public static class SCfgImportVerify
    {
        [MenuItem("S/诊断/校验配置表导入")]
        public static void VerifyMenu() => Verify(false);

        public static void VerifyBatch() => Verify(true);

        static void Verify(bool exitOnFail)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 配置表导入契约（.tsv → TextAsset）===");

            int pass = 0, fail = 0;
            for (int i = 0; i < SCfgAddresses.Required.Length; i++)
            {
                string table = SCfgAddresses.Required[i];
                string path = SCfgAddresses.AssetPath(table);
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);

                if (asset == null)
                {
                    sb.AppendLine($"FAIL {path,-44} AssetDatabase 取不到 TextAsset（.tsv 导入器未生效？）");
                    fail++;
                    continue;
                }

                if (string.IsNullOrEmpty(asset.text))
                {
                    sb.AppendLine($"FAIL {path,-44} 导入成功但文本为空");
                    fail++;
                    continue;
                }

                // 三行表头是硬约定，这里顺带查一眼，避免「导入对了但文件是旧的纯文本」
                int lines = asset.text.Split('\n').Length;
                if (lines < 4)
                {
                    sb.AppendLine($"FAIL {path,-44} 只有 {lines} 行，连三行表头都不够");
                    fail++;
                    continue;
                }

                sb.AppendLine($"PASS {path,-44} {asset.text.Length} 字节 / {lines} 行");
                pass++;
            }

            // 最后真跑一次装配：导入器对但表内容坏，同样起不来
            var data = SDataLoader.Reload();
            if (data == null)
            {
                sb.AppendLine("FAIL SCfgLoader 装配失败（见上面的 Cfg 报错）");
                fail++;
            }
            else
            {
                sb.AppendLine($"PASS 装配成功 枪 {data.GunOrder.Count} / 子弹 {data.BulletOrder.Count} / " +
                              $"衍生 {data.ModifierOrder.Count} / 敌人 {data.EnemyOrder.Count}");
                pass++;
            }

            sb.AppendLine($"RESULT 配置导入 {pass}/{pass + fail} passed");
            Debug.Log(sb.ToString());

            if (fail > 0 && exitOnFail)
            {
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }
    }
}
