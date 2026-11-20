Shader "S/Cel Ink"
{
    Properties
    {
        _Ink("Ink colour",Color)=(0.11,0.12,0.21,1)
        _Strength("Line opacity",Range(0,1))=0.55
        _Width("Line width at 1080p",Range(0.5,2))=1.05
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Depth contours"
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Ink
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Ink;
            float _Width, _Strength;
            CBUFFER_END
            float Depth(float2 uv)
            {
                float raw = SampleSceneDepth(saturate(uv));
                if (unity_OrthoParams.w > 0.5)
                {
                    #if UNITY_REVERSED_Z
                    raw = 1.0 - raw;
                    #endif
                    return lerp(_ProjectionParams.y,_ProjectionParams.z,raw);
                }
                return LinearEyeDepth(raw,_ZBufferParams);
            }
            half4 Ink(Varyings i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 color = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture,sampler_LinearClamp,i.texcoord,0);
                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                float2 stepUV = max(1.0, _Width * _ScaledScreenParams.y / 1080.0) / _ScaledScreenParams.xy;
                float d = Depth(uv);
                // Second differences reject a smooth depth slope across floors and walls.
                float2 bend = float2(Depth(uv+float2(stepUV.x,0))+Depth(uv-float2(stepUV.x,0)),
                                     Depth(uv+float2(0,stepUV.y))+Depth(uv-float2(0,stepUV.y))) - 2.0*d;
                float contour = smoothstep(0.014,0.060,max(bend.x,bend.y)/max(0.1,d));
                color.rgb = lerp(color.rgb,min(color.rgb,_Ink.rgb),contour*_Strength);
                return color;
            }
            ENDHLSL
        }
    }
}
