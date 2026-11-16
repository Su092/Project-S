using System.IO;
using UnityEditor;
using UnityEngine;

namespace S.EditorTools
{
    /// <summary>
    /// 资产导入规则。只影响本工程的 Art 与 UI 目录。
    ///
    /// 为什么需要：APIMart 产出的贴图默认按 sRGB + 压缩导入，
    /// 但平铺地面/墙面需要 Repeat 包裹与足够高的采样上限，否则平铺会出现接缝或发糊。
    /// </summary>
    public sealed class SAssetPostprocessor : AssetPostprocessor
    {
        static readonly string TexRoot = S.View.SRes.Root + "Texture/";
        static readonly string ModelRoot = S.View.SRes.Root + "Model/";
        static readonly string UiRoot = S.View.SRes.Root + "Texture/UI/";

        void OnPreprocessTexture()
        {
            string p = assetPath.Replace('\\', '/');

            // UI 素材优先判断：它就在 Texture/ 下面，若先命中 TexRoot 会被当成平铺贴图
            // 导成 Default 类型，sprite 取不到、9-slice 边距也写不进去（顺带说，原先
            // 这两条规则匹配的目录是 Resources/UI 与 _Game/Art/Textures，搬目录后必须同步，
            // 否则规则静默失效 —— 编译打包全绿，只是圆角全坏）。
            if (p.Contains(UiRoot))
            {
                var ti = (TextureImporter)assetImporter;
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear;
                ti.maxTextureSize = 2048;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.spritePixelsPerUnit = 100f;

                // 9-slice 边距：按钮/条/槽必须按名字给边距，否则拉伸时圆角与内线会被拉变形。
                // 这里集中配置，新增素材只要命名匹配就自动获得边距，不必手工改 meta（踩过）。
                // ⚠ 必须显式写 zero 而不是"有值才写"：早期素材的 meta 里有按百分比算的旧边距
                // （crosshair 6、keycap 10），不覆盖的话会一直留着 —— 一旦哪天控件改成
                // Sliced，就会用上这份没人维护的边距，圆角被切成方块。
                var border = BorderFor(Path.GetFileNameWithoutExtension(p));
                ti.spriteBorder = border ?? Vector4.zero;
                return;
            }

            if (p.Contains(TexRoot))
            {
                var ti = (TextureImporter)assetImporter;
                ti.textureType = TextureImporterType.Default;
                ti.wrapMode = TextureWrapMode.Repeat;      // 平铺
                ti.filterMode = FilterMode.Bilinear;
                ti.mipmapEnabled = true;
                ti.anisoLevel = 4;
                ti.maxTextureSize = 2048;
                ti.sRGBTexture = true;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                return;
            }
        }

        /// <summary>
        /// 按素材名给出 9-slice 边距（left,bottom,right,top，单位像素）。
        ///
        /// ⚠ 判据是「控件真实像素」，不是贴图尺寸。
        /// 我吃过一次亏：贴图 512×96 看着"可拉伸区 464×48 很安全"，
        /// 但控件实际只有 360×14 —— 上下边距 24+24=48 早就超过 14，
        /// Unity 会把整块边距压扁，圆角塌成死平、描边糊成一条。
        /// 贴图尺寸只决定"能不能放下边距"，控件尺寸才决定"是否真的变形"。
        ///
        /// 所以这里每个数值都按**最小控件尺寸**倒推，并在
        /// tools/audit_ui_slices.py 里用 prefab 的真实 RectTransform 复核。
        /// 校验命令：python tools/audit_ui_slices.py（有问题的会列出来）
        /// </summary>
        static Vector4? BorderFor(string name)
        {
            switch (name)
            {
                // 按钮：最小控件 108×40（背包 BuyButton），其次 340×70。
                // 圆角 12 ⇒ 边距必须 ≥12 才能包住圆角，同时 40 高要留出可拉伸区。
                // 取 14：12 圆角 + 2px 余量，40-28=12px 可拉伸区。原来的 22 直接吃光 40 高。
                case "btn_primary":
                case "btn_primary_hover":
                case "btn_danger":
                    return new Vector4(16f, 14f, 16f, 14f);

                // 条槽：真实高度 14 / 26 / 30（横条）与 320（竖向威胁条）。
                // 最小是 HUD 的 XP 条 360×14 —— 上下边距各 3 才能留下 8px 可拉伸区。
                // 代价：圆角只能是 3，14px 高的条看起来接近直角。
                // 这才是"一张 512×96 药丸图服务 14~320 全部高度"的真实限制，
                // 想恢复圆角必须给矮条单独出一张薄图（见 audit 输出）。
                case "bar_bg":
                    return new Vector4(3f, 3f, 3f, 3f);

                // 槽位：slot_empty 用在 ChainRow.Tail 60×84，几何能看全。
                case "slot_empty":
                    return new Vector4(18f, 18f, 18f, 18f);

                // slot_filled 实际被当 20×20 的小徽章底（Menu.BadgeBack）。
                // 20 是硬约束：边距合计必须 ≤14 才留 6px，取 6。
                // 圆角随之只能 6 —— 20px 的方框上，6 已经是能看出来的极限。
                case "slot_filled":
                    return new Vector4(6f, 6f, 6f, 6f);

                // 稀有度标签：OfferCard.TagTop 272×16，ItemCard.Tag 只有 6×84（细竖条）。
                // 6px 宽是硬约束，边距合计必须 ≤2 才有可拉伸区，取 1。
                // 代价最大的一处：标签在两处都不是"药丸"，圆角退化为 1~2px 的微倒角。
                case "tag_starter":
                case "tag_common":
                case "tag_rare":
                case "tag_epic":
                    return new Vector4(1f, 1f, 1f, 1f);

                // 面板：VolumePanel 最小 380×228，圆角 12 ⇒ 20 绰绰有余。
                // ⚠ 这里 20 是按贴图 512 时的圆角(约 12)给的余量；预处理量得
                // 圆角半径只有 2（原图本身的圆角就极克制，缩到 512 后更小），
                // 边距 >= 圆角即可包住弧，所以 20 仍然成立且更稳。
                case "panel":
                    return new Vector4(20f, 20f, 20f, 20f);

                // ---- 老素材（纳入流水线后统一在这里给边距）----
                // 它们的共同约束：预处理产物已被裁到内容框，贴图边缘就是板边，
                // 所以边距必须 ≤ 贴图尺寸的一半，且 ≤ 最小控件的一半（留可拉伸区）。
                //
                // card_frame：最小控件 ItemCard 150×84。产物 198×256，圆角 6。
                // 取 10：包住 6px 圆角，且 10+10=20 < 84 高留 64px 可拉伸区。
                case "card_frame":
                    return new Vector4(10f, 10f, 10f, 10f);

                // card_inner：卡片内部的暗色内衬，贴图 256²、圆角 5。
                // 最小控件是 ItemCard 内缩 4 后的 142×76，边距 8 留 60px 可拉伸区。
                case "card_inner":
                    return new Vector4(8f, 8f, 8f, 8f);

                // banner：HUD BannerPlate 720×88。产物 512×84，圆角 22。
                // ⚠ 22 是硬下限（<22 会把弧切开），但 22+22=44 已占掉 84 高的一半；
                // 88 高的控件还能留 44px 可拉伸区，成立。取 24 留 2px 余量。
                case "banner":
                    return new Vector4(24f, 24f, 24f, 24f);

                // bar_frame：无预制体引用。产物 512×48，圆角 15。
                // 若将来给 14 高的细条用，15+15=30 会吃光高度 —— 这时必须重新出一张
                // 薄图（和 bar_bg 同样的取舍）。当前取 16，包住圆角，服务 >=40 高的控件。
                case "bar_frame":
                    return new Vector4(16f, 16f, 16f, 16f);

                default:
                    return null;   // 图标、晕影、分隔线、准星等不需要边距
            }
        }

        void OnPreprocessModel()
        {
            string p = assetPath.Replace('\\', '/');
            if (!p.Contains(ModelRoot)) return;

            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importBlendShapes = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            mi.weldVertices = true;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.None;   // 不用法线贴图，省内存

            // ⚠ 不要把 bakeAxisConversion 打开。
            // 关着的时候，Unity 在根节点写一个 X≈-90° 的旋转来完成 Blender(Z 上)
            // → Unity(Y 上) 的轴转换，运行时只要「保留这个旋转」就得到 上=+Y、前=+Z。
            // 打开烘焙则会把旋转烘进顶点、根节点变成 identity —— 两种都可行，
            // 但前提是运行时代码与之一致。本工程统一走「保留导入旋转」这条，
            // 契约断言见 SModelContract（它会验证旋转确实存在）。
            mi.bakeAxisConversion = false;
        }
    }
}
