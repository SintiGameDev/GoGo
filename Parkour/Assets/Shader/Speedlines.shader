Shader "Hidden/Speedlines"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "SpeedlinesPass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // Wird global per Shader.SetGlobalFloat von SpeedlinesController gesetzt
            float _Intensity;       // 0 = aus, 1 = volle Staerke
            float _LineCount;       // Anzahl der Streaks
            float _LineSpeed;       // Scroll-Geschwindigkeit der Streaks nach aussen
            float _InnerRadius;     // ab welchem Radius (0-1, Bildschirmmitte) die Lines beginnen
            float4 _LineColor;      // Farbe/Alpha der Lines

            // Einfacher Hash fuer Pseudo-Noise je Linie (Laenge/Dicke-Variation)
            float Hash(float n)
            {
                return frac(sin(n * 127.1) * 43758.5453123);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                if (_Intensity <= 0.001)
                {
                    return col;
                }

                // Koordinaten relativ zur Bildschirmmitte, Aspect-korrigiert
                float2 centered = uv - 0.5;
                centered.x *= _ScreenParams.x / _ScreenParams.y;

                float radius = length(centered);
                float angle = atan2(centered.y, centered.x); // -PI..PI

                // Bildschirm in _LineCount Sektoren aufteilen
                float sector = angle / (2 * PI) * _LineCount;
                float sectorId = floor(sector);
                float sectorFrac = frac(sector);

                // Pro Sektor zufaellige Linienbreite und Laenge
                float randWidth = lerp(0.15, 0.5, Hash(sectorId));
                float randLength = lerp(0.6, 1.0, Hash(sectorId + 17.0));
                float randSpeed = lerp(0.7, 1.3, Hash(sectorId + 31.0));

                // Linie als duenner Streifen im Sektor (mittig, Breite = randWidth)
                float distFromCenter = abs(sectorFrac - 0.5);
                float lineMask = 1.0 - smoothstep(randWidth * 0.5, randWidth * 0.5 + 0.05, distFromCenter);

                // Streaks scrollen nach aussen (Radius waechst mit der Zeit), Intensitaet steuert Reichweite
                float scroll = frac(_Time.y * _LineSpeed * randSpeed);
                float outerEdge = _InnerRadius + (randLength * _Intensity);
                float headRadius = lerp(_InnerRadius, outerEdge, scroll);
                float tailRadius = headRadius - (0.08 * randLength);

                float radialMask = smoothstep(tailRadius, headRadius, radius) *
                                    (1.0 - smoothstep(headRadius, headRadius + 0.02, radius));

                // Nur ausserhalb von _InnerRadius sichtbar (Mitte bleibt frei)
                float innerCutoff = smoothstep(_InnerRadius * 0.5, _InnerRadius, radius);

                float mask = lineMask * radialMask * innerCutoff * _Intensity;

                col.rgb = lerp(col.rgb, _LineColor.rgb, mask * _LineColor.a);
                return col;
            }
            ENDHLSL
        }
    }
}
