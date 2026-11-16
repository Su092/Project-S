using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace S.View
{
    /// <summary>
    /// 运行时材质创建的唯一入口。
    ///
    /// 为什么需要它：URP 的 Lit 着色器在打包时可能被剥离（场景里没有任何材质引用它时尤其如此），
    /// 于是 `new Material(Shader.Find("Universal Render Pipeline/Lit"))` 在编辑器里正常、
    /// 在播放器里抛 ArgumentNullException（Parameter name: shader），整个 Awake 断掉。
    /// 这里按「URP Lit → URP Unlit → Sprites/Default → 内置 Standard」顺序兜底。
    ///
    /// **材质缓存**：所有 Lit/Unlit 材质按 (shader, 颜色, smoothness) 缓存共享。
    /// 起因是弹丸、命中火花、爆炸球这些瞬态对象每次生成都 new 一个材质，
    /// 连发 9 发/秒 × 3 发分裂时材质实例会无限累积（实测几十秒涨几百个）。
    /// 缓存后同一视觉规格只创建一次，实例数恒定。
    /// </summary>
    public static class SMat
    {
        static Shader s_lit;
        static Shader s_unlit;
        static bool s_resolved;

        static readonly Dictionary<string, Material> s_cache = new Dictionary<string, Material>();
        static readonly List<Material> s_owned = new List<Material>();

        static void Resolve()
        {
            if (s_resolved) return;
            s_resolved = true;

            s_lit = Shader.Find("Universal Render Pipeline/Lit");
            if (s_lit == null) s_lit = Shader.Find("Universal Render Pipeline/Unlit");
            if (s_lit == null) s_lit = Shader.Find("Standard");
            if (s_lit == null) s_lit = Shader.Find("Sprites/Default");

            s_unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (s_unlit == null) s_unlit = Shader.Find("Sprites/Default");
            if (s_unlit == null) s_unlit = s_lit;
        }

        static string Key(char kind, Color c, float smooth) =>
            $"{kind}|{c.r:F3},{c.g:F3},{c.b:F3},{c.a:F3}|{smooth:F2}";

        /// <summary>受光材质（共享缓存）。着色器全缺失时返回 null，调用方需判空。</summary>
        public static Material Lit(Color c, float smoothness = 0.2f)
        {
            Resolve();
            if (s_lit == null) return null;

            var key = Key('L', c, smoothness);
            if (s_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var m = new Material(s_lit);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);

            s_cache[key] = m;
            s_owned.Add(m);
            return m;
        }

        /// <summary>自发光/纯色材质（共享缓存）。</summary>
        public static Material Unlit(Color c)
        {
            Resolve();
            if (s_unlit == null) return null;

            var key = Key('U', c, 0f);
            if (s_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var m = new Material(s_unlit);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;

            s_cache[key] = m;
            s_owned.Add(m);
            return m;
        }

        /// <summary>共享粒子/拖尾材质：仅三种固定变体，颜色由顶点传入。</summary>
        public static Material Combat(bool soft, bool smoke = false)
        {
            string key = "Combat|" + soft + "|" + smoke;
            if (s_cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var shader = Shader.Find("S/CombatGlow");
            if (shader == null) return Unlit(Color.white);
            var mat = new Material(shader);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Soft", soft ? 1f : 0f);
            mat.SetFloat("_DstBlend", smoke ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.One);
            s_cache[key] = mat; s_owned.Add(mat);
            return mat;
        }

        /// <summary>安全赋值：材质为 null 时保留 Renderer 原有材质，不抛异常。</summary>
        public static void Apply(Renderer r, Material m)
        {
            if (r != null && m != null) r.sharedMaterial = m;
        }

        /// <summary>自检用：当前缓存的材质数量（应随场景稳定，不随开火增长）。</summary>
        public static int CachedCount => s_cache.Count;

        /// <summary>自检用：本类创建的材质总数。</summary>
        public static int CreatedCount => s_owned.Count;

        /// <summary>
        /// 新建一个**不共享**的 Lit 材质实例。
        /// 用于需要独立贴图/平铺参数的场合（地面、墙板），调用方负责自己缓存。
        /// 这些实例不计入共享缓存，但会登记以便自检统计。
        /// </summary>
        public static Material NewLitInstance(float smoothness = 0.2f)
        {
            Resolve();
            if (s_lit == null) return null;

            var m = new Material(s_lit);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            m.color = Color.white;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);

            s_owned.Add(m);
            return m;
        }
    }
}
