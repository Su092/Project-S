Shader "S/MusicVfx"
{
 Properties { _BaseMap("Texture",2D)="white"{} _BaseColor("Tint",Color)=(1,1,1,1) }
 SubShader
 {
  Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
  Pass
  {
   Blend SrcAlpha One
   ZWrite Off
   Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
   CBUFFER_START(UnityPerMaterial)
   half4 _BaseColor;float4 _BaseMap_ST;
   CBUFFER_END
   struct A{float4 p:POSITION;half4 c:COLOR;float2 uv:TEXCOORD0;};
   struct V{float4 p:SV_POSITION;half4 c:COLOR;float2 uv:TEXCOORD0;};
   V vert(A i){V o;o.p=TransformObjectToHClip(i.p.xyz);o.c=i.c*_BaseColor;o.uv=TRANSFORM_TEX(i.uv,_BaseMap);return o;}
   half4 frag(V i):SV_Target{return SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv)*i.c;}
   ENDHLSL
  }
 }
}
