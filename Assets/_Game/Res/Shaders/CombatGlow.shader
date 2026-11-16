Shader "S/CombatGlow"
{
 Properties {
  _BaseColor ("Tint", Color) = (1,1,1,1)
  _Soft ("Soft billboard", Float) = 0
  [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination blend", Float) = 1
 }
 SubShader {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
  Pass {
   Blend SrcAlpha [_DstBlend]
   ZWrite Off
   Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct Attributes { float4 positionOS:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; };
   struct Varyings { float4 positionCS:SV_POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; };
   CBUFFER_START(UnityPerMaterial)
    half4 _BaseColor;
    float _Soft;
   CBUFFER_END
   Varyings vert(Attributes v) {
    Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz);
    o.color=v.color*_BaseColor; o.uv=v.uv; return o;
   }
   half4 frag(Varyings i):SV_Target {
    float r=length(i.uv*2-1);
    float falloff=pow(saturate(1-r*r),2);
    return half4(i.color.rgb, i.color.a*lerp(1,falloff,_Soft));
   }
   ENDHLSL
  }
 }
}
