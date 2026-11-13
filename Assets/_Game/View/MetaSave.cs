using System.IO;
using UnityEngine;
using S.Core;

namespace S.View
{
    /// <summary>
    /// 局外存档的读写桥。Core/MetaProgress 负责结构与逻辑，这里只管落盘。
    ///
    /// 落盘位置：Application.persistentDataPath/meta.txt
    /// 为什么不用 PlayerPrefs：文本文件便于玩家排查与手动备份，
    /// 且序列化逻辑已经在 Core 里可断言，不需要 PlayerPrefs 的键值包装。
    /// </summary>
    public static class MetaSave
    {
        const string FileName = "meta.txt";
        const string BackupSuffix = ".bak";

        static MetaProgress s_cached;

        public static string PathOf =>
            System.IO.Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>读取存档。文件不存在或损坏时返回全新进度（不抛异常）。</summary>
        public static MetaProgress Load()
        {
            if (s_cached != null) return s_cached;

            var p = PathOf;
            try
            {
                if (File.Exists(p))
                {
                    var text = File.ReadAllText(p);
                    s_cached = MetaProgress.Deserialize(text);
                    return s_cached;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[S] 读取局外存档失败，将使用新进度：{e.Message}");
            }

            s_cached = new MetaProgress();
            return s_cached;
        }

        /// <summary>
        /// 保存。先写临时文件再替换，避免写一半崩溃把存档写坏。
        /// 旧档留一份 .bak，玩家误操作还能救。
        /// </summary>
        public static bool Save(MetaProgress m)
        {
            if (m == null) return false;
            s_cached = m;

            var p = PathOf;
            var tmp = p + ".tmp";
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p));
                File.WriteAllText(tmp, m.Serialize());

                if (File.Exists(p)) File.Copy(p, p + BackupSuffix, true);
                if (File.Exists(p)) File.Delete(p);
                File.Move(tmp, p);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[S] 保存局外存档失败：{e.Message}");
                return false;
            }
        }

        /// <summary>自检用：强制重读（清缓存）。</summary>
        public static MetaProgress Reload()
        {
            s_cached = null;
            return Load();
        }
    }
}
