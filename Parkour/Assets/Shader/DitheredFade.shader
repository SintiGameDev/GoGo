Shader "Custom/DitheredFade"
{
    Properties
    {
        [Header(Surface)]
        _BaseMap        ("Albedo (optional)", 2D) = "white" {}
        _Smoothness     ("Smoothness", Range(0,1)) = 0.5
        _Metallic       ("Metallic", Range(0,1)) = 0.0

        [Header(Gradient)]
        _ColorA         ("Color A (Anfang)", Color) = (0.2, 0.4, 1.0, 1)
        _ColorB         ("Color B (Ende)",   Color) = (1.0, 0.3, 0.5, 1)
        _ColorC         ("Color C (Mitte, optional)", Color) = (0.5, 0.0, 1.0, 1)
        [Toggle] _UseThreeColors ("Use Three Colors", Float) = 0
        [KeywordEnum(Y, X, Z, Radial)] _GradientMode ("Gradient Mode", Float) = 0
        _GradientOffset ("Gradient Offset", Range(-1,1)) = 0.0
        _GradientScale  ("Gradient Scale",  Range(0.1,5)) = 1.0

        [Header(Emission)]
        [Toggle] _UseEmission ("Use Emission", Float) = 0
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,1)
        _EmissionGradient ("Emission folgt Gradient", Range(0,1)) = 0.0

        [Header(Dither Fade)]
        _Alpha          ("Alpha (0=invisible, 1=solid)", Range(0,1)) = 0.8
        _PatternScale   ("Pattern Scale", Range(1,16)) = 4
        _Contrast       ("Contrast", Range(0.5, 4.0)) = 1.5

        [Header(Distance Fade)]
        [Toggle] _UseDistanceFade ("Use Distance Fade", Float) = 0
        _FadeStart      ("Fade Start Distance", Float) = 3.0
        _FadeEnd        ("Fade End Distance", Float) = 8.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "AlphaTest"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"

            // ── Bayer 8×8 ───────────────────────────────────────────────────
            static const float Bayer8[64] =
            {
                 0, 32,  8, 40,  2, 34, 10, 42,
                48, 16, 56, 24, 50, 18, 58, 26,
                12, 44,  4, 36, 14, 46,  6, 38,
                60, 28, 52, 20, 62, 30, 54, 22,
                 3, 35, 11, 43,  1, 33,  9, 41,
                51, 19, 59, 27, 49, 17, 57, 25,
                15, 47,  7, 39, 13, 45,  5, 37,
                63, 31, 55, 23, 61, 29, 53, 21
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _ColorA;
                float4 _ColorB;
                float4 _ColorC;
                float  _UseThreeColors;
                float  _GradientMode;
                float  _GradientOffset;
                float  _GradientScale;
                float4 _EmissionColor;
                float  _UseEmission;
                float  _EmissionGradient;
                float  _Smoothness;
                float  _Metallic;
                float  _Alpha;
                float  _PatternScale;
                float  _Contrast;
                float  _FadeStart;
                float  _FadeEnd;
                float  _UseDistanceFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 positionOS  : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
                float3 viewDirWS   : TEXCOORD3;
                float2 uv          : TEXCOORD4;
                float4 shadowCoord : TEXCOORD5;
            };

            // ── Hilfsfunktionen ─────────────────────────────────────────────
            void DitherClip(float alpha, float4 posCS, float patternScale, float contrast)
            {
                uint scale = max(1u, (uint)patternScale);
                uint px    = (uint)(posCS.x / scale) % 8;
                uint py    = (uint)(posCS.y / scale) % 8;
                float threshold = Bayer8[py * 8 + px] / 64.0;
                threshold = saturate((threshold - 0.5) / contrast + 0.5);
                clip(alpha - threshold);
            }

            float GradientT(float3 posOS)
            {
                float t = 0;
                if      (_GradientMode < 0.5) t = posOS.y + 0.5;
                else if (_GradientMode < 1.5) t = posOS.x + 0.5;
                else if (_GradientMode < 2.5) t = posOS.z + 0.5;
                else                          t = saturate(length(posOS) * 2.0);
                return saturate((t + _GradientOffset) * _GradientScale);
            }

            float3 SampleGradient(float t)
            {
                if (_UseThreeColors > 0.5)
                {
                    float3 ac = lerp(_ColorA.rgb, _ColorC.rgb, saturate(t * 2.0));
                    float3 cb = lerp(_ColorC.rgb, _ColorB.rgb, saturate(t * 2.0 - 1.0));
                    return lerp(ac, cb, step(0.5, t));
                }
                return lerp(_ColorA.rgb, _ColorB.rgb, t);
            }

            // ── Vertex ──────────────────────────────────────────────────────
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   vni = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionCS  = vpi.positionCS;
                OUT.positionWS  = vpi.positionWS;
                OUT.positionOS  = IN.positionOS.xyz;
                OUT.normalWS    = vni.normalWS;
                OUT.viewDirWS   = GetWorldSpaceViewDir(vpi.positionWS);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.shadowCoord = GetShadowCoord(vpi);
                return OUT;
            }

            // ── Fragment ────────────────────────────────────────────────────
            half4 frag(Varyings IN) : SV_Target
            {
                // 1) Dither
                float alpha = _Alpha;
                if (_UseDistanceFade > 0.5)
                {
                    float dist = length(_WorldSpaceCameraPos - IN.positionWS);
                    alpha *= 1.0 - saturate((dist - _FadeStart) / max(_FadeEnd - _FadeStart, 0.001));
                }
                DitherClip(alpha, IN.positionCS, _PatternScale, _Contrast);

                // 2) Gradient → Albedo
                float  t           = GradientT(IN.positionOS);
                float3 gradColor   = SampleGradient(t);
                float4 albedoTex   = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                float3 albedo      = albedoTex.rgb * gradColor;

                // 3) Emission (unabhängig von Lighting)
                float3 emission = 0;
                if (_UseEmission > 0.5)
                    emission = lerp(_EmissionColor.rgb, _EmissionColor.rgb * gradColor, _EmissionGradient);

                // 4) PBR Surface Data → korrekte Trennung von Diffuse / Specular / Emission
                SurfaceData sd = (SurfaceData)0;
                sd.albedo      = albedo;
                sd.metallic    = _Metallic;
                sd.smoothness  = _Smoothness;
                sd.normalTS    = float3(0, 0, 1);
                sd.emission    = emission;
                sd.alpha       = 1.0;

                InputData id   = (InputData)0;
                id.positionWS  = IN.positionWS;
                id.normalWS    = normalize(IN.normalWS);
                id.viewDirectionWS = normalize(IN.viewDirWS);
                id.shadowCoord = IN.shadowCoord;
                id.fogCoord    = 0;
                id.vertexLighting = 0;
                id.bakedGI     = SampleSH(id.normalWS);
                id.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                id.shadowMask  = half4(1,1,1,1);

                return UniversalFragmentPBR(id, sd);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   vertShadow
            #pragma fragment fragShadow

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"

            static const float Bayer8[64] =
            {
                 0, 32,  8, 40,  2, 34, 10, 42,
                48, 16, 56, 24, 50, 18, 58, 26,
                12, 44,  4, 36, 14, 46,  6, 38,
                60, 28, 52, 20, 62, 30, 54, 22,
                 3, 35, 11, 43,  1, 33,  9, 41,
                51, 19, 59, 27, 49, 17, 57, 25,
                15, 47,  7, 39, 13, 45,  5, 37,
                63, 31, 55, 23, 61, 29, 53, 21
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _ColorA;
                float4 _ColorB;
                float4 _ColorC;
                float  _UseThreeColors;
                float  _GradientMode;
                float  _GradientOffset;
                float  _GradientScale;
                float4 _EmissionColor;
                float  _UseEmission;
                float  _EmissionGradient;
                float  _Smoothness;
                float  _Metallic;
                float  _Alpha;
                float  _PatternScale;
                float  _Contrast;
                float  _FadeStart;
                float  _FadeEnd;
                float  _UseDistanceFade;
            CBUFFER_END

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            ShadowVaryings vertShadow(ShadowAttributes IN)
            {
                ShadowVaryings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float3 posWS  = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, normWS, _LightDirection));
                return OUT;
            }

            half4 fragShadow(ShadowVaryings IN) : SV_Target
            {
                uint scale = max(1u, (uint)_PatternScale);
                uint px    = (uint)(IN.positionCS.x / scale) % 8;
                uint py    = (uint)(IN.positionCS.y / scale) % 8;
                float threshold = Bayer8[py * 8 + px] / 64.0;
                threshold = saturate((threshold - 0.5) / _Contrast + 0.5);
                clip(_Alpha - threshold);
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
