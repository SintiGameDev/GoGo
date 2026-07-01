Shader "Custom/CurvedWorld"
{
    Properties
    {
        [Header(Surface)]
        _BaseColor   ("Base Color", Color) = (0.6, 0.6, 0.7, 1)
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        _Smoothness  ("Smoothness", Range(0,1)) = 0.4
        _Metallic    ("Metallic", Range(0,1)) = 0.0
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,1)

        [Header(Curve)]
        _CurveStrength ("Curve Strength", Float) = 0.001   // wie stark der Boden wegbiegt
        _CurveAxis     ("Curve Axis (1=radial, 0=nur vorne)", Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "DisableBatching"="True" }
        LOD 200

        // ---------------------------------------------------------------
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _BaseMap_ST;
            float  _Smoothness;
            float  _Metallic;
            float4 _EmissionColor;
            float  _CurveStrength;
            float  _CurveAxis;
        CBUFFER_END

        // --- Kern des Effekts: World-Y abhaengig vom horizontalen Kameraabstand absenken ---
        // dist^2 => quadratischer Falloff: nah flach, fern biegt es stark weg.
        float3 CurveWorld(float3 positionWS)
        {
            float2 d = positionWS.xz - _WorldSpaceCameraPos.xz;
            // _CurveAxis=1: radial (klassischer Mini-Planet). =0: nur Z-Tiefe (Horizont vorne).
            d.x *= _CurveAxis;
            float dist2 = dot(d, d);
            positionWS.y -= dist2 * _CurveStrength;
            return positionWS;
        }
        ENDHLSL

        // =============================================================== Forward
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_SCREEN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                posWS = CurveWorld(posWS);                      // <-- Bend
                OUT.positionWS = posWS;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                SurfaceData s = (SurfaceData)0;
                s.albedo     = albedo.rgb;
                s.metallic   = _Metallic;
                s.smoothness = _Smoothness;
                s.emission   = _EmissionColor.rgb;
                s.alpha      = 1;
                s.occlusion  = 1;

                InputData inp = (InputData)0;
                inp.positionWS          = IN.positionWS;
                inp.normalWS            = normalize(IN.normalWS);
                inp.viewDirectionWS     = normalize(GetWorldSpaceViewDir(IN.positionWS));
                inp.shadowCoord         = TransformWorldToShadowCoord(IN.positionWS);
                inp.fogCoord            = ComputeFogFactor(IN.positionCS.z);
                inp.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);

                half4 color = UniversalFragmentPBR(inp, s);
                color.rgb = MixFog(color.rgb, inp.fogCoord);
                return color;
            }
            ENDHLSL
        }

        // =============================================================== ShadowCaster
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                posWS = CurveWorld(posWS);                      // <-- gleicher Bend
                float3 nrmWS = TransformObjectToWorldNormal(IN.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionCS = cs;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // =============================================================== DepthOnly
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                posWS = CurveWorld(posWS);                      // <-- gleicher Bend
                OUT.positionCS = TransformWorldToHClip(posWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack "Hide/InternalErrorShader"
}
