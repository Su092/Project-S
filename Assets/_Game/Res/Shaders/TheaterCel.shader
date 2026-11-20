Shader "S/Theater Cel"
{
    Properties
    {
        [MainTexture] _BaseMap("Paint / trim", 2D) = "white" {}
        [MainColor] _BaseColor("Paint tint", Color) = (1,1,1,1)
        _MetallicGlossMap("Metal (R), occlusion (G)", 2D) = "white" {}
        _Metallic("Metal accent", Range(0,1)) = 1
        [HDR] _EmissionColor("Lamp emission", Color) = (0,0,0,0)
        [HideInInspector] _Cull("Cull", Float) = 2
        [HideInInspector] _Cutoff("Cutoff", Float) = 0.5
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "Painted Light"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex CelVertex
            #pragma fragment CelFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"

            half _MusicStageDim; // Global cue, zero outside follow-spot sequences.
            struct Mesh { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Pixel
            {
                float4 positionCS:SV_POSITION;
                float3 positionWS:TEXCOORD0;
                half3 normalWS:TEXCOORD1;
                float2 uv:TEXCOORD2;
                half3 vertexLight:TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Pixel CelVertex(Mesh v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Pixel o = (Pixel)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.vertexLight = VertexLighting(o.positionWS, o.normalWS);
                return o;
            }
            half3 Irradiance(Light light, half3 n, half3 view, inout half glint)
            {
                half incidence = saturate(dot(n,light.direction));
                half power = Luminance(light.color) * light.distanceAttenuation * light.shadowAttenuation;
                half spec = dot(n,SafeNormalize(view+light.direction));
                glint = max(glint, smoothstep(0.91h,0.95h,spec) * saturate(power) * saturate(incidence*4));
                return light.color * (incidence * light.distanceAttenuation * light.shadowAttenuation);
            }
            half4 CelFragment(Pixel i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                #ifdef LOD_FADE_CROSSFADE
                LODFadeCrossFade(i.positionCS);
                #endif
                half3 n = normalize(i.normalWS);
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                shadowCoord = ComputeScreenPos(TransformWorldToHClip(i.positionWS));
                #endif
                half3 view = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half lampGlint = 0;
                half3 direct = Irradiance(GetMainLight(shadowCoord), n, view, lampGlint) + i.vertexLight;
                #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                #if USE_CLUSTER_LIGHT_LOOP
                for (uint k=0; k<min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); ++k)
                    direct += Irradiance(GetAdditionalLight(k,i.positionWS,half4(1,1,1,1)),n,view,lampGlint);
                #endif
                LIGHT_LOOP_BEGIN(count)
                    direct += Irradiance(GetAdditionalLight(lightIndex,i.positionWS,half4(1,1,1,1)),n,view,lampGlint);
                LIGHT_LOOP_END
                #endif

                // Restore the original painted fill outside an active follow-spot cue.
                half3 key = half3(-0.52,0.72,-0.46);
                half facing = dot(n,key);
                half lum = Luminance(direct);
                half pool = lum / (0.48h + lum);
                half cue = saturate(_MusicStageDim);
                half value = lerp(0.26h + 0.38h*facing + 0.62h*pool, 0.08h + 0.10h*facing + 0.95h*pool, cue);
                half feather = clamp(fwidth(value)*1.4h,0.025h,0.085h);
                half mid = smoothstep(0.32h-feather,0.32h+feather,value);
                half sun = smoothstep(0.69h-feather,0.69h+feather,value);
                half3 shade = lerp(lerp(half3(0.20,0.31,0.55),half3(0.045,0.065,0.12),cue),lerp(half3(0.55,0.62,0.82),half3(0.22,0.28,0.40),cue),mid);
                shade = lerp(shade,half3(1.28,1.03,0.84),sun);
                half3 paint = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb * _BaseColor.rgb;
                half2 mask = SAMPLE_TEXTURE2D(_MetallicGlossMap,sampler_MetallicGlossMap,i.uv).rg;
                half3 color = paint * shade * lerp(0.76h,1.0h,mask.g);
                color += lerp(half3(0.020,0.027,0.046),half3(0.005,0.008,0.014),cue)*(1.0h-0.65h*sun);
                color += paint * 0.17h * direct / (1.0h + lum);
                half glint = smoothstep(0.93h,0.965h,dot(n,SafeNormalize(view+key)));
                color += half3(0.65,0.44,0.20)*max(glint*lerp(1.0h,0.12h,cue),lampGlint)*mask.r*_Metallic;
                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(inputData.normalizedScreenSpaceUV);
                color *= lerp(0.52h,1.0h,ao.indirectAmbientOcclusion);
                color += _EmissionColor.rgb * paint;
                return half4(color,1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
        UsePass "Universal Render Pipeline/Lit/Meta"
    }
}
