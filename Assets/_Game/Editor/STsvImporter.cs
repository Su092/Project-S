using System.IO;
using System.Text;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace S.EditorTools
{
    /// <summary>
    /// 把 `.tsv` 导入成 TextAsset。
    ///
    /// **为什么必须有这个导入器**：Unity 的默认文本扩展名清单里**没有** `.tsv`
    /// （只有 txt/html/htm/xml/bytes/json/csv/yaml/fnt）。没有导入器时：
    ///   - 编辑器里 `Resources.Load` 返回 null；
    ///   - 打包时该文件被**静默丢弃**，几万行配置一个字节都不进包；
    ///   - 表现是编辑器里能跑、打出来的 exe 一启动就「缺表」黑屏。
    /// 这类故障不会在编译期报错，只能靠「构建产物里有没有这些文件」来断言
    /// （见 scripts/check.ps1 的 build 任务）。
    ///
    /// 换成 `.txt` 后缀也能绕开，但那样表名会变成 `Player.tsv.txt`，
    /// 每次改表都得解释一遍后缀。给 `.tsv` 一个导入器更直接。
    /// </summary>
    [ScriptedImporter(1, "tsv")]
    public sealed class STsvImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            // 配置表一律 UTF-8（可能带 BOM，STsv 里会剥掉）。用 ReadAllText 让 BOM 交给解码器处理。
            string text = File.ReadAllText(ctx.assetPath, Encoding.UTF8);

            var asset = new TextAsset(text);
            ctx.AddObjectToAsset("text", asset);
            ctx.SetMainObject(asset);
        }
    }
}
