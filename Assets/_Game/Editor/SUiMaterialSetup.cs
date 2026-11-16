using UnityEditor;
using UnityEngine;

namespace S.EditorTools
{
    /// <summary>
    /// 生成 UI 图标用的加色材质。
    ///
    /// 为什么需要：APIMart 出的图标是「纯黑底 + 白色图形」。
    /// 用普通 Alpha 混合放在深色 HUD 上会出现一个可见的黑方块；
    /// 用加色（Blend One One）则黑色天然透明、亮部发光，正是 HUD 图标的观感。
    /// </summary>
    public static class SUiMaterialSetup
    {
        static readonly string MatPath = S.View.SRes.Root + "Shaders/mat_ui_additive.mat";
        static readonly string ShaderPath = S.View.SRes.Root + "Shaders/UiAdditive.shader";

        [MenuItem("S/生成 UI 加色材质")]
        public static void Create()
        {
            System.IO.Directory.CreateDirectory(S.View.SRes.Root + "Shaders");

            // 优先用工程自带的加色 shader；没有就先写一个最小的
            var shader = Shader.Find("S/UiAdditive");
            if (shader == null)
            {
                WriteShader();
                AssetDatabase.Refresh();
                shader = Shader.Find("S/UiAdditive");
            }

            if (shader == null)
            {
                Debug.LogError("[S] UiAdditive shader 创建失败");
                return;
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, MatPath);
            }
            else
            {
                mat.shader = shader;
            }

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 写进 Always Included，防止打包被剥离（与 URP 材质同一个坑）
            ShaderSetup.Apply();

            Debug.Log("[S] UI 加色材质已生成: " + MatPath);
        }

        static void WriteShader()
        {
            var src = @"Shader ""S/UiAdditive""
{
    Properties
    {
        [PerRendererData] _MainTex (""Sprite Texture"", 2D) = ""white"" {}
        _Color (""Tint"", Color) = (1,1,1,1)

        _StencilComp (""Stencil Comparison"", Float) = 8
        _Stencil (""Stencil ID"", Float) = 0
        _StencilOp (""Stencil Operation"", Float) = 0
        _StencilWriteMask (""Stencil Write Mask"", Float) = 255
        _StencilReadMask (""Stencil Read Mask"", Float) = 255
        _ColorMask (""Color Mask"", Float) = 15
    }

    SubShader
    {
        Tags
        {
            ""Queue"" = ""Transparent""
            ""IgnoreProjector"" = ""True""
            ""RenderType"" = ""Transparent""
            ""PreviewType"" = ""Plane""
            ""CanUseSpriteAtlas"" = ""True""
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        // 关键：加色混合。黑底乘 0 不贡献任何亮度，天然透明。
        Blend One One
        ColorMask [_ColorMask]

        Pass
        {
            Name ""UI_ADDITIVE""
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.texcoord) * i.color;
                // 预乘亮度，使白色图形更亮、灰边更淡
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
";
            System.IO.File.WriteAllText(ShaderPath, src, new System.Text.UTF8Encoding(false));
        }

        public static void CreateBatch()
        {
            Create();
        }
    }
}
