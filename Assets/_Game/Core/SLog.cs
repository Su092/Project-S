using System;

namespace S.Core
{
    /// <summary>
    /// Core 里的日志出口。Core 不许引 UnityEngine，但又必须能把「配置有警告」
    /// 之类的消息发出去 —— 所以在这里留一个可替换的钩子。
    ///
    /// Unity 侧在 <c>SDataLoader</c> 里挂上 Debug.Log，
    /// dotnet 断言里默认走 Console，两边都不必改业务代码。
    /// </summary>
    public static class SLog
    {
        /// <summary>默认实现：写到标准输出。Unity 侧会被替换掉。</summary>
        public static Action<string> Warn = s => Console.WriteLine(s);
        public static Action<string> Error = s => Console.WriteLine(s);

        public static void W(string msg) => Warn?.Invoke(msg);
        public static void E(string msg) => Error?.Invoke(msg);
    }
}
