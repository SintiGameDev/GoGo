// =====================================================================
// TraumweltDeform.shader
// Control (Remedy)-artiger Vertex-Noise-Deformation Shader fuer URP
// 
// Verschiebt Vertices entlang ihrer Normalen-Richtung mit 3D Simplex
// Noise + Zeit-Offset (pseudo-4D). Funktioniert auf beliebigen Meshes
// (Sphere, Cube, organische Meshes, etc.) - voll parametrisierbar
// ueber Material-Inspector, kein Code-Wissen noetig zur Nutzung.
//
// PBR-Lit Beleuchtung (URP Lit-kompatibel: Albedo, Normal, Metallic,
// Smoothness, Emission) + optionale World-Space Noise-Synchronisation
// fuer mehrere Objekte im selben "Feld".
// =====================================================================

Shader "Traumwelt/VertexNoiseDeform"
{
    Properties
    {
        [Header(Surface)]
        _BaseMap("Albedo", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)
        _NormalMap("Normal Map", 2D) = "bump" {}
        _NormalStrength("Normal Strength", Range(0,2)) = 1.0
        _Metallic("Metallic", Range(0,1)) = 0.0
        _Smoothness("Smoothness", Range(0,1)) = 0.5
        [HDR]_EmissionColor("Emission Color", Color) = (0,0,0,1)
        _EmissionMap("Emission Map", 2D) = "white" {}

        [Header(Noise Displacement)]
        _NoiseScale("Noise Scale", Float) = 1.0
        _NoiseSpeed("Noise Speed (Time Offset)", Float) = 0.5
        _DisplaceStrength("Displacement Strength", Float) = 0.3
        _DisplaceClampMin("Displacement Clamp Min", Float) = -1.0
        _DisplaceClampMax("Displacement Clamp Max", Float) = 1.0

        [Header(Fractal Octaves)]
        [IntRange]_Octaves("Octaves (1-3)", Range(1,3)) = 1
        _OctaveLacunarity("Octave Lacunarity (Frequenz-Multiplikator)", Float) = 2.0
        _OctaveGain("Octave Gain (Amplituden-Multiplikator)", Float) = 0.5

        [Header(Space Mode)]
        [Toggle]_WorldSpaceNoise("World Space Noise (0=Object, 1=World)", Float) = 0

        [Header(Recalculate Normals)]
        [Toggle]_RecalcNormals("Normals per Differenzen neu berechnen", Float) = 1
        _NormalRecalcOffset("Normal Recalc Sample Offset", Float) = 0.01

        [HideInInspector]_Cull("__cull", Float) = 2.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "Queue" = "Geometry"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // URP Keywords fuer Licht/Schatten
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // -----------------------------------------------------------
            // Ashima Arts Simplex Noise 3D (MIT-Lizenz, freie Nutzung)
            // https://github.com/ashima/webgl-noise
            // -----------------------------------------------------------
            float3 mod289(float3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
            float4 mod289(float4 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
            float4 permute(float4 x) { return mod289(((x * 34.0) + 1.0) * x); }
            float4 taylorInvSqrt(float4 r) { return 1.79284291400159 - 0.85373472095314 * r; }

            float SimplexNoise3D(float3 v)
            {
                const float2 C = float2(1.0 / 6.0, 1.0 / 3.0);
                const float4 D = float4(0.0, 0.5, 1.0, 2.0);

                float3 i  = floor(v + dot(v, C.yyy));
                float3 x0 = v - i + dot(i, C.xxx);

                float3 g = step(x0.yzx, x0.xyz);
                float3 l = 1.0 - g;
                float3 i1 = min(g.xyz, l.zxy);
                float3 i2 = max(g.xyz, l.zxy);

                float3 x1 = x0 - i1 + C.xxx;
                float3 x2 = x0 - i2 + C.yyy;
                float3 x3 = x0 - D.yyy;

                i = mod289(i);
                float4 p = permute(permute(permute(
                    i.z + float4(0.0, i1.z, i2.z, 1.0))
                    + i.y + float4(0.0, i1.y, i2.y, 1.0))
                    + i.x + float4(0.0, i1.x, i2.x, 1.0));

                float n_ = 0.142857142857;
                float3 ns = n_ * D.wyz - D.xzx;

                float4 j = p - 49.0 * floor(p * ns.z * ns.z);

                float4 x_ = floor(j * ns.z);
                float4 y_ = floor(j - 7.0 * x_);

                float4 x = x_ * ns.x + ns.yyyy;
                float4 y = y_ * ns.x + ns.yyyy;
                float4 h = 1.0 - abs(x) - abs(y);

                float4 b0 = float4(x.xy, y.xy);
                float4 b1 = float4(x.zw, y.zw);

                float4 s0 = floor(b0) * 2.0 + 1.0;
                float4 s1 = floor(b1) * 2.0 + 1.0;
                float4 sh = -step(h, 0.0);

                float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
                float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;

                float3 p0 = float3(a0.xy, h.x);
                float3 p1 = float3(a0.zw, h.y);
                float3 p2 = float3(a1.xy, h.z);
                float3 p3 = float3(a1.zw, h.w);

                float4 norm = taylorInvSqrt(float4(dot(p0, p0), dot(p1, p1), dot(p2, p2), dot(p3, p3)));
                p0 *= norm.x;
                p1 *= norm.y;
                p2 *= norm.z;
                p3 *= norm.w;

                float4 m = max(0.6 - float4(dot(x0, x0), dot(x1, x1), dot(x2, x2), dot(x3, x3)), 0.0);
                m = m * m;
                return 42.0 * dot(m * m, float4(dot(p0, x0), dot(p1, x1), dot(p2, x2), dot(p3, x3)));
            }

            // -----------------------------------------------------------
            // Material Properties
            // -----------------------------------------------------------
            TEXTURE2D(_BaseMap);       SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap);     SAMPLER(sampler_NormalMap);
            TEXTURE2D(_EmissionMap);   SAMPLER(sampler_EmissionMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _NormalStrength;
                float _Metallic;
                float _Smoothness;
                float4 _EmissionColor;

                float _NoiseScale;
                float _NoiseSpeed;
                float _DisplaceStrength;
                float _DisplaceClampMin;
                float _DisplaceClampMax;

                float _Octaves;
                float _OctaveLacunarity;
                float _OctaveGain;

                float _WorldSpaceNoise;
                float _RecalcNormals;
                float _NormalRecalcOffset;
            CBUFFER_END

            // -----------------------------------------------------------
            // Fractal Noise Sample (1-3 Octaves) - gemeinsam genutzt fuer
            // Vertex-Position und Normal-Neuberechnung
            // -----------------------------------------------------------
            float SampleFractalNoise(float3 pos, float timeOffset)
            {
                float total = 0.0;
                float frequency = _NoiseScale;
                float amplitude = 1.0;
                float maxAmp = 0.0;

                int oct = (int)_Octaves;
                for (int i = 0; i < 3; i++)
                {
                    if (i < oct)
                    {
                        float3 samplePos = pos * frequency + float3(timeOffset, timeOffset * 0.85, timeOffset * 1.15);
                        total += SimplexNoise3D(samplePos) * amplitude;
                        maxAmp += amplitude;
                        frequency *= _OctaveLacunarity;
                        amplitude *= _OctaveGain;
                    }
                }
                return (maxAmp > 0.0) ? (total / maxAmp) : 0.0;
            }

            float3 DisplacedPosition(float3 objectPos, float3 normalDir, float3 worldPosForNoise)
            {
                float timeOffset = _Time.y * _NoiseSpeed;
                float3 noiseBasis = (_WorldSpaceNoise > 0.5) ? worldPosForNoise : objectPos;

                float n = SampleFractalNoise(noiseBasis, timeOffset);
                n = clamp(n * _DisplaceStrength, _DisplaceClampMin, _DisplaceClampMax);

                return objectPos + normalDir * n;
            }

            // -----------------------------------------------------------
            // Vertex Input / Output
            // -----------------------------------------------------------
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS      : SV_POSITION;
                float2 uv              : TEXCOORD0;
                float3 positionWS      : TEXCOORD1;
                float3 normalWS        : TEXCOORD2;
                float4 tangentWS       : TEXCOORD3;
                float3 viewDirWS       : TEXCOORD4;
                float4 shadowCoord     : TEXCOORD5;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 6);
                float fogCoord         : TEXCOORD7;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;

                // --- Welt-Position fuer World-Space-Noise-Modus vorab berechnen ---
                float3 worldPosUndisplaced = TransformObjectToWorld(IN.positionOS.xyz);

                // --- Displacement entlang der Normalen ---
                float3 displacedOS = DisplacedPosition(IN.positionOS.xyz, normalize(IN.normalOS), worldPosUndisplaced);

                float3 newNormalOS = normalize(IN.normalOS);

                if (_RecalcNormals > 0.5)
                {
                    // Normalen per finiter Differenzen neu berechnen, da das
                    // Displacement die Oberflaeche verformt (Tangent-Sample-Trick).
                    //
                    // FIX (Flackern bei mehreren Octaves):
                    // (a) Sample-Offset wird an die hoechste Octave-Frequenz
                    //     gekoppelt, damit die Differenz den echten Gradienten
                    //     misst statt hochfrequentes Aliasing.
                    // (b) Zentrale Differenzen (+e/-e) -> stabiler als Vorwaerts-Diff.
                    // (c) KEIN harter Fallback mehr bei degeneriertem cross(): das
                    //     binaere Umschalten zwischen Recalc- und Original-Normale
                    //     war pro Frame zwischen zwei Zustaenden gesprungen
                    //     (= das kamera-/lichtunabhaengige Flackern). Stattdessen
                    //     weiches Blending ueber eine skalenunabhaengige "confidence"
                    //     (= |sin(Winkel)| zwischen den beiden Flaechen-Tangenten).
                    int octForE = (int)_Octaves;
                    float highestFreq = _NoiseScale * pow(max(_OctaveLacunarity, 1.0), max(octForE - 1, 0));
                    float e = _NormalRecalcOffset / max(1.0, highestFreq);

                    float3 nOS = normalize(IN.normalOS);
                    float3 tangentApprox = normalize(abs(nOS.y) < 0.99
                        ? cross(nOS, float3(0, 1, 0))
                        : cross(nOS, float3(1, 0, 0)));
                    float3 bitangentApprox = normalize(cross(nOS, tangentApprox));

                    float3 posTp = IN.positionOS.xyz + tangentApprox * e;
                    float3 posTm = IN.positionOS.xyz - tangentApprox * e;
                    float3 posBp = IN.positionOS.xyz + bitangentApprox * e;
                    float3 posBm = IN.positionOS.xyz - bitangentApprox * e;

                    float3 dispTp = DisplacedPosition(posTp, nOS, TransformObjectToWorld(posTp));
                    float3 dispTm = DisplacedPosition(posTm, nOS, TransformObjectToWorld(posTm));
                    float3 dispBp = DisplacedPosition(posBp, nOS, TransformObjectToWorld(posBp));
                    float3 dispBm = DisplacedPosition(posBm, nOS, TransformObjectToWorld(posBm));

                    // Tangenten der verformten Oberflaeche (zentrale Differenz)
                    float3 dA = dispTp - dispTm;
                    float3 dB = dispBp - dispBm;

                    float3 crossResult = cross(dA, dB);
                    float crossLen = length(crossResult);
                    float denom = length(dA) * length(dB);

                    // confidence in [0,1]: 0 = degeneriert/parallel, 1 = sauber orthogonal
                    float confidence = (denom > 1e-20) ? saturate(crossLen / denom) : 0.0;

                    float3 recalcN = (crossLen > 1e-20) ? (crossResult / crossLen) : nOS;
                    if (dot(recalcN, nOS) < 0.0)
                        recalcN = -recalcN;

                    // Weiches Blending statt hartem Sprung -> kein Flackern mehr
                    newNormalOS = normalize(lerp(nOS, recalcN, confidence));
                }

                VertexPositionInputs vertexInput = GetVertexPositionInputs(displacedOS);
                VertexNormalInputs normalInput = GetVertexNormalInputs(newNormalOS, IN.tangentOS);

                OUT.positionCS = vertexInput.positionCS;
                OUT.positionWS = vertexInput.positionWS;
                OUT.normalWS = normalInput.normalWS;
                OUT.tangentWS = float4(normalInput.tangentWS, IN.tangentOS.w);
                OUT.viewDirWS = GetWorldSpaceViewDir(vertexInput.positionWS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);

                OUT.shadowCoord = GetShadowCoord(vertexInput);

                OUTPUT_LIGHTMAP_UV(IN.lightmapUV, unity_LightmapST, OUT.lightmapUV);
                OUTPUT_SH(OUT.normalWS, OUT.vertexSH);

                OUT.fogCoord = ComputeFogFactor(vertexInput.positionCS.z);

                return OUT;
            }

            // -----------------------------------------------------------
            // Fragment
            // -----------------------------------------------------------
            half4 frag(Varyings IN) : SV_Target
            {
                half4 albedoTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half3 albedo = albedoTex.rgb * _BaseColor.rgb;

                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, IN.uv), _NormalStrength);

                float sgn = IN.tangentWS.w;
                float3 normalWS_raw = normalize(IN.normalWS.xyz);
                float3 tangentWS_raw = normalize(IN.tangentWS.xyz);
                float3 bitangent = sgn * cross(normalWS_raw, tangentWS_raw);

                // DIAGNOSE: Schutz gegen degenerierte Tangent-Basis (NaN-Quelle testen)
                float bitangentLenSq = dot(bitangent, bitangent);
                if (bitangentLenSq < 1e-8)
                {
                    bitangent = cross(normalWS_raw, float3(0,1,0));
                    if (dot(bitangent, bitangent) < 1e-8)
                        bitangent = cross(normalWS_raw, float3(1,0,0));
                }
                bitangent = normalize(bitangent);

                half3x3 tangentToWorld = half3x3(tangentWS_raw, bitangent.xyz, normalWS_raw);
                half3 normalWS = normalize(TransformTangentToWorld(normalTS, tangentToWorld));

                half3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, IN.uv).rgb * _EmissionColor.rgb;

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = normalize(IN.viewDirWS);
                inputData.shadowCoord = IN.shadowCoord;
                inputData.fogCoord = IN.fogCoord;
                inputData.bakedGI = SAMPLE_GI(IN.lightmapUV, IN.vertexSH, inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.metallic = _Metallic;
                surfaceData.smoothness = _Smoothness;
                surfaceData.normalTS = normalTS;
                surfaceData.emission = emission;
                surfaceData.occlusion = 1.0;
                surfaceData.alpha = 1.0;
                surfaceData.specular = 0.0;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0;

                return color;
            }

            ENDHLSL
        }

        // --- Shadow Caster Pass: Displacement muss auch hier angewendet
        //     werden, damit Schatten zur verformten Geometrie passen ---
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 mod289_s(float3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
            float4 mod289_s(float4 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
            float4 permute_s(float4 x) { return mod289_s(((x * 34.0) + 1.0) * x); }
            float4 taylorInvSqrt_s(float4 r) { return 1.79284291400159 - 0.85373472095314 * r; }

            float SimplexNoise3D_S(float3 v)
            {
                const float2 C = float2(1.0 / 6.0, 1.0 / 3.0);
                const float4 D = float4(0.0, 0.5, 1.0, 2.0);

                float3 i  = floor(v + dot(v, C.yyy));
                float3 x0 = v - i + dot(i, C.xxx);

                float3 g = step(x0.yzx, x0.xyz);
                float3 l = 1.0 - g;
                float3 i1 = min(g.xyz, l.zxy);
                float3 i2 = max(g.xyz, l.zxy);

                float3 x1 = x0 - i1 + C.xxx;
                float3 x2 = x0 - i2 + C.yyy;
                float3 x3 = x0 - D.yyy;

                i = mod289_s(i);
                float4 p = permute_s(permute_s(permute_s(
                    i.z + float4(0.0, i1.z, i2.z, 1.0))
                    + i.y + float4(0.0, i1.y, i2.y, 1.0))
                    + i.x + float4(0.0, i1.x, i2.x, 1.0));

                float n_ = 0.142857142857;
                float3 ns = n_ * D.wyz - D.xzx;

                float4 j = p - 49.0 * floor(p * ns.z * ns.z);

                float4 x_ = floor(j * ns.z);
                float4 y_ = floor(j - 7.0 * x_);

                float4 x = x_ * ns.x + ns.yyyy;
                float4 y = y_ * ns.x + ns.yyyy;
                float4 h = 1.0 - abs(x) - abs(y);

                float4 b0 = float4(x.xy, y.xy);
                float4 b1 = float4(x.zw, y.zw);

                float4 s0 = floor(b0) * 2.0 + 1.0;
                float4 s1 = floor(b1) * 2.0 + 1.0;
                float4 sh = -step(h, 0.0);

                float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
                float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;

                float3 p0 = float3(a0.xy, h.x);
                float3 p1 = float3(a0.zw, h.y);
                float3 p2 = float3(a1.xy, h.z);
                float3 p3 = float3(a1.zw, h.w);

                float4 norm = taylorInvSqrt_s(float4(dot(p0, p0), dot(p1, p1), dot(p2, p2), dot(p3, p3)));
                p0 *= norm.x;
                p1 *= norm.y;
                p2 *= norm.z;
                p3 *= norm.w;

                float4 m = max(0.6 - float4(dot(x0, x0), dot(x1, x1), dot(x2, x2), dot(x3, x3)), 0.0);
                m = m * m;
                return 42.0 * dot(m * m, float4(dot(p0, x0), dot(p1, x1), dot(p2, x2), dot(p3, x3)));
            }

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _NormalStrength;
                float _Metallic;
                float _Smoothness;
                float4 _EmissionColor;

                float _NoiseScale;
                float _NoiseSpeed;
                float _DisplaceStrength;
                float _DisplaceClampMin;
                float _DisplaceClampMax;

                float _Octaves;
                float _OctaveLacunarity;
                float _OctaveGain;

                float _WorldSpaceNoise;
                float _RecalcNormals;
                float _NormalRecalcOffset;
            CBUFFER_END

            float SampleFractalNoise_S(float3 pos, float timeOffset)
            {
                float total = 0.0;
                float frequency = _NoiseScale;
                float amplitude = 1.0;
                float maxAmp = 0.0;

                int oct = (int)_Octaves;
                for (int i = 0; i < 3; i++)
                {
                    if (i < oct)
                    {
                        float3 samplePos = pos * frequency + float3(timeOffset, timeOffset * 0.85, timeOffset * 1.15);
                        total += SimplexNoise3D_S(samplePos) * amplitude;
                        maxAmp += amplitude;
                        frequency *= _OctaveLacunarity;
                        amplitude *= _OctaveGain;
                    }
                }
                return (maxAmp > 0.0) ? (total / maxAmp) : 0.0;
            }

            float3 _LightDirection;

            float4 GetShadowPositionHClip(float3 positionOS, float3 normalOS)
            {
                float3 worldPosUndisplaced = TransformObjectToWorld(positionOS);
                float timeOffset = _Time.y * _NoiseSpeed;
                float3 noiseBasis = (_WorldSpaceNoise > 0.5) ? worldPosUndisplaced : positionOS;

                float n = SampleFractalNoise_S(noiseBasis, timeOffset);
                n = clamp(n * _DisplaceStrength, _DisplaceClampMin, _DisplaceClampMax);

                float3 displacedOS = positionOS + normalize(normalOS) * n;

                float3 positionWS = TransformObjectToWorld(displacedOS);
                float3 normalWS = normalize(TransformObjectToWorldNormal(normalOS));

                // Manuelle, versionsunabhaengige Shadow-Bias-Berechnung
                // (ersetzt ApplyShadowBias, da Signatur je nach URP-Version variiert)
                float invNdotL = 1.0 - saturate(dot(_LightDirection, normalWS));
                float scale = invNdotL * _ShadowBias.y;
                positionWS = _LightDirection * _ShadowBias.xxx + positionWS;
                positionWS = normalWS * scale.xxx + positionWS;

                float4 positionCS = TransformWorldToHClip(positionWS);

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                return positionCS;
            }

            struct AttributesShadow
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct VaryingsShadow
            {
                float4 positionCS : SV_POSITION;
            };

            VaryingsShadow vertShadow(AttributesShadow IN)
            {
                VaryingsShadow OUT;
                OUT.positionCS = GetShadowPositionHClip(IN.positionOS.xyz, IN.normalOS);
                return OUT;
            }

            half4 fragShadow(VaryingsShadow IN) : SV_Target
            {
                return 0;
            }

            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
