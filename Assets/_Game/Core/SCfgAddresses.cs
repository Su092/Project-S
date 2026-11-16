namespace S.Core
{
    /// <summary>
    /// 配置表的**唯一真相**：表名与文件名。
    ///
    /// 为什么集中：表名原先散在加载器的十几个 `Read(source, "Xxx.tsv")` 里，
    /// 改名要 grep 全文；更糟的是「表名拼错」的表现是启动期抛异常，
    /// 而不是编译错误 —— 集中成常量后，至少改名时 IDE 会提示引用点。
    /// </summary>
    public static class SCfgAddresses
    {
        /// <summary>Resources 下的目录（相对 `Assets/Resources/`，不带扩展名）。</summary>
        public const string ResourceFolder = "Data/Cfg";

        public const string Player = "Player";
        public const string Gun = "Gun";
        public const string Bullet = "Bullet";
        public const string Modifier = "Modifier";
        public const string GunModifier = "GunModifier";
        public const string RarityWeight = "RarityWeight";
        public const string Arena = "Arena";
        public const string Timeline = "Timeline";
        public const string Elite = "Elite";
        public const string Boss = "Boss";
        public const string Enemy = "Enemy";

        /// <summary>
        /// 全部必需表。SCfgLoader 会逐张加载，缺任何一张都抛异常 ——
        /// 半套数据比没有数据更危险（跑起来才发现敌人全 0 血）。
        /// </summary>
        public static readonly string[] Required =
        {
            Player, Gun, Bullet, Modifier, GunModifier, RarityWeight,
            Arena, Timeline, Elite, Boss, Enemy,
        };

        /// <summary>Resources 路径（不含扩展名），Unity 侧加载用。</summary>
        public static string ResourcePath(string table) => ResourceFolder + "/" + table;

        /// <summary>相对工程根的资产路径，报错时给人看用。</summary>
        public static string AssetPath(string table) =>
            "Assets/Resources/" + ResourceFolder + "/" + table + ".tsv";
    }
}
