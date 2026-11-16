namespace S.Core
{
    /// <summary>
    /// 配置文本的来源。抽出来是为了让「读哪张表」与「表从哪来」解耦：
    ///
    ///   - Unity 运行时：Resources 里的 TextAsset（SCfgResourcesSource）
    ///   - dotnet 断言：直接读工程目录的 .tsv 文件（SCfgDirectorySource）
    ///   - 修数据时的试算：内存里塞一段文本（SCfgMemorySource）
    ///
    /// 三条通路喂给同一个 SCfgLoader，所以「测试过了」与「游戏跑得起来」是同一份逻辑。
    /// </summary>
    public interface ISCfgSource
    {
        /// <summary>给人看的来源标识（报错前缀用），如 `Assets/Resources/Data/Cfg`。</summary>
        string RootKey { get; }

        /// <summary>取某张表的原文。表名见 <see cref="SCfgAddresses"/>。取不到返回 false。</summary>
        bool TryGetText(string table, out string text);
    }
}
