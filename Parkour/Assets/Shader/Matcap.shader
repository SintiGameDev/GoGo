// =============================================================
//  Matcap.shader  (URP / Unity 6000.0.25f1)
//  Material-Capture: Beleuchtung kommt NICHT aus der Szene,
//  sondern aus einer einzigen Kugel-Textur ("Matcap").
//  -> Ton, Chrom, Wachs, Clay-Look ohne ein einziges Licht.
// =============================================================
Shader "Traumwelt/Matcap"
{
    Properties
    {
        [Header(Matcap)]
        _MatcapTex   ("Matcap (Kugel-Textur)", 2D) = "white" {}
        _Tint        ("Tint", Color) = (1,1,1,1)

        [Header(Optionale Base Textur)]
        [Toggle(_USE_BASEMAP)] _UseBaseMap ("Base Textur benutzen", Float) = 0
        _BaseMap     ("Base Map", 2D) = "white" {}

        [Header(Zweiter additiver Matcap fuer Glow Rim)]
        [Toggle(_USE_ADDMATCAP)] _UseAddMatcap ("Add-Matcap benutzen", Float) = 0
        _AddMatcapTex   ("Add-Matcap", 2D) = "black" {}
        _AddStrength    ("Add-Staerke", Range(0,4)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            Name "MatcapUnlit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma shader_feature_local _USE_BASEMAP
            #pragma shader_feature_local _USE_ADDMATCAP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // --- Texturen ---
            TEXTURE2D(_MatcapTex);    SAMPLER(sampler_MatcapTex);
            TEXTURE2D(_AddMatcapTex); SAMPLER(sampler_AddMatcapTex);
            TEXTURE2D(_BaseMap);      SAMPLER(sampler_BaseMap);

            // --- SRP-Batcher-kompatibler CBUFFER ---
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _Tint;
                float  _AddStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                // Normale im VIEW-Space -> das ist der ganze Trick
                float3 normalVS    : TEXCOORD1;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrm = GetVertexNormalInputs(IN.normalOS);

                OUT.positionHCS = pos.positionCS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);

                // World-Normale -> View-Space.
                // UNITY_MATRIX_V dreht die Normale relativ zur Kamera.
                // Genau das brauchen wir, damit der Matcap "der Kamera folgt".
                OUT.normalVS = mul((float3x3)UNITY_MATRIX_V, nrm.normalWS);

                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // Normale kann durch Interpolation leicht ihre Laenge verlieren
                float3 n = normalize(IN.normalVS);

                // KERN: View-Space-Normale.xy [-1..1] -> UV [0..1]
                // n.z (Richtung Kamera) ignorieren wir; die Kugel ist 2D projiziert.
                float2 matcapUV = n.xy * 0.5 + 0.5;

                half3 col = SAMPLE_TEXTURE2D(_MatcapTex, sampler_MatcapTex, matcapUV).rgb;
                col *= _Tint.rgb;

                #ifdef _USE_BASEMAP
                    half3 baseCol = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb;
                    col *= baseCol;     // Albedo moduliert den Matcap-Look
                #endif

                #ifdef _USE_ADDMATCAP
                    // Zweiter Matcap wird ADDIERT -> ideal fuer Rim-Glow,
                    // Emission-Faelle, Sci-Fi-Schimmer. Schwarz = unsichtbar.
                    half3 addCol = SAMPLE_TEXTURE2D(_AddMatcapTex, sampler_AddMatcapTex, matcapUV).rgb;
                    col += addCol * _AddStrength;
                #endif

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
