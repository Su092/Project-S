using System;
using System.Collections.Generic;
using System.IO;

namespace S.Core
{
    /// <summary>
    /// 直接读磁盘目录的 .tsv。给 dotnet 断言用（不引 UnityEngine）。
    ///
    /// 用法：`new SCfgDirectorySource(@"E:\S\Assets\Resources\Data\Cfg")`
    /// </summary>
    public sealed class SCfgDirectorySource : ISCfgSource
    {
        public SCfgDirectorySource(string folder)
        {
            RootKey = folder ?? string.Empty;
        }

        public string RootKey { get; }

        public bool TryGetText(string table, out string text)
        {
            text = null;
            if (string.IsNullOrEmpty(RootKey) || string.IsNullOrEmpty(table)) return false;

            string path = Path.Combine(RootKey, table + ".tsv");
            if (!File.Exists(path)) return false;

            text = File.ReadAllText(path);
            return true;
        }
    }

    /// <summary>
    /// 内存文本源。给「就地改一段数据做试算」用（不落盘）。
    /// </summary>
    public sealed class SCfgMemorySource : ISCfgSource
    {
        readonly Dictionary<string, string> _texts = new Dictionary<string, string>(StringComparer.Ordinal);

        public SCfgMemorySource(string rootKey = "memory")
        {
            RootKey = rootKey;
        }

        public string RootKey { get; }

        public SCfgMemorySource Set(string table, string text)
        {
            _texts[table] = text ?? string.Empty;
            return this;
        }

        /// <summary>从另一个源整份复制（做「改一张表」的最小改动副本时方便）。</summary>
        public static SCfgMemorySource CopyOf(ISCfgSource src)
        {
            var mem = new SCfgMemorySource(src.RootKey + " (copy)");
            for (int i = 0; i < SCfgAddresses.Required.Length; i++)
            {
                string t = SCfgAddresses.Required[i];
                if (src.TryGetText(t, out string text)) mem.Set(t, text);
            }
            return mem;
        }

        public bool TryGetText(string table, out string text)
        {
            return _texts.TryGetValue(table, out text);
        }
    }
}
