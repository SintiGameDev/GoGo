// TraumweltGradientRamp.shader
// URP Unlit Gradient/Palette-Mapping Shader (Vaporwave)
// Mappt die Luminanz der Albedo-Textur auf einen gebackenen Gradienten (_RampTex).
// Features: Posterize-Stufen, animiertes Scrolling, Contrast/Gamma, Blend mit Original, Saturation, Alpha-Blend.
// Unity 6 / URP. Ramp-Textur wird per GradientRampBaker.cs erzeugt.

Shader "Traumwelt/GradientRamp"
{
    Properties
    {
        [Header(Base)]
        _BaseMap   ("Albedo", 2D) = "white" {}
        _Tint      ("Tint", Color) = (1,1,1,1)
        _RampTex   ("Gradient Ramp (baked)", 2D) = "white" {}

        [Header(Mapping)]
        _Steps     ("Posterize Steps", Range(2,16)) = 5
        _Contrast  ("Contrast", Range(0,3)) = 1
        _Gamma     ("Gamma", Range(0.1,3)) = 1

        [Header(Look)]
        _Blend     ("Gradient to Original (0=Gradient 1=Original)", Range(0,1)) = 0
        _Saturation("Saturation", Range(0,2)) = 1

        [Header(Animation)]
        _ScrollSpeed ("Scroll Speed (+/- = Richtung)", Float) = 0

        [Header(Transparency)]
        _Alpha     ("Alpha", Range(0,1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
        }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);   SAMPLER(sampler_BaseMap);
            TEXTURE2D(_RampTex);   SAMPLER(sampler_RampTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _Tint;
                float  _Steps;
                float  _Contrast;
                float  _Gamma;
                float  _Blend;
                float  _Saturation;
                float  _ScrollSpeed;
                float  _Alpha;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half3 ApplySaturation(half3 col, half sat)
            {
                half gray = dot(col, half3(0.2126, 0.7152, 0.0722));
                return lerp(gray.xxx, col, sat);
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _Tint;

                // Luminanz als Lookup-Index
                half lum = dot(albedo.rgb, half3(0.2126, 0.7152, 0.0722));

                // Contrast um 0.5 zentriert
                lum = saturate((lum - 0.5) * _Contrast + 0.5);

                // Gamma
                lum = pow(max(lum, 1e-5), _Gamma);

                // Posterize auf N Stufen (volle 0..1 Bandbreite)
                lum = floor(lum * _Steps) / max(_Steps - 1.0, 1.0);
                lum = saturate(lum);

                // Scroll-Animation auf der Ramp-Koordinate
                float u = frac(lum + _Time.y * _ScrollSpeed);

                half3 gradColor = SAMPLE_TEXTURE2D(_RampTex, sampler_RampTex, float2(u, 0.5)).rgb;
                gradColor = ApplySaturation(gradColor, _Saturation);

                half3 finalRGB = lerp(gradColor, albedo.rgb, _Blend);
                half  finalA   = albedo.a * _Alpha;

                return half4(finalRGB, finalA);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
