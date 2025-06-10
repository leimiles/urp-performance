Shader "Hidden/GeometryDensity/Blend"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalRenderPipeline" "RenderType"="Opaque" }
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseTex);
            SAMPLER(sampler_BaseTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.vertex = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return SAMPLE_TEXTURE2D(_BaseTex, sampler_BaseTex, i.uv);
            }
            ENDHLSL
        }
    }
}
