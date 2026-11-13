using UnityEngine;

namespace S.Core
{
    /// <summary>
    /// Unity 侧的桥：从 Resources 读 sdata.json 文本并交给 SData.Parse。
    /// Core 本身不依赖 UnityEngine，只有这个文件把两边接起来。
    /// </summary>
    public static class SDataLoader
    {
        public const string ResourcePath = "Data/sdata";

        static SData s_cache;

        public static SData Get()
        {
            if (s_cache != null) return s_cache;
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogError($"[S] 找不到 Resources/{ResourcePath}.json —— 配件系统无法启动");
                return null;
            }
            s_cache = SData.Parse(asset.text);
            return s_cache;
        }

        /// <summary>编辑器/自检用：强制重读。</summary>
        public static SData Reload()
        {
            s_cache = null;
            return Get();
        }
    }
}
