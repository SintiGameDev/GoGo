Shader "Custom/TraumweltDeform_NORMALTEST"
{
    Properties
    {
        [Header(Surface)]
        _BaseColor   ("Base Color", Color) = (0.5, 0.5, 0.6, 1)
        _Smoothness  ("Smoothness", Range(0,1)) = 0.5
        _Metallic    ("Metallic", Range(0,1)) = 0.0
        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)

        [Header(Displacement)]
        _NoiseScale  ("Noise Scale", Float) = 2.0
        _NoiseAmp    ("Displacement Amount", Float) = 0.2
        _NoiseSpeed  ("Flow Speed", Float) = 0.5
        _NormalEps   ("Normal Sample Offset", Range(0.001, 0.2)) = 0.02
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "DisableBatching"="True" }
        LOD 300

        // ---------------------------------------------------------------
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float  _Smoothness;
            float  _Metallic;
            float4 _EmissionColor;
            float  _NoiseScale;
            float  _NoiseAmp;
            float  _NoiseSpeed;
            float  _NormalEps;
        CBUFFER_END

        // --- 3D Simplex Noise (Ashima / Stefan Gustavson, public domain) ---
        float3 mod289(float3 x) { return x - floor(x * (1.0/289.0)) * 289.0; }
        float4 mod289(float4 x) { return x - floor(x * (1.0/289.0)) * 289.0; }
        float4 permute(float4 x) { return mod289(((x*34.0)+1.0)*x); }
        float4 taylorInvSqrt(float4 r) { return 1.79284291400159 - 0.85373472095314 * r; }

        float snoise(float3 v)
        {
            const float2 C = float2(1.0/6.0, 1.0/3.0);
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
            float4 sh = -step(h, float4(0,0,0,0));

            float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
            float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;

            float3 p0 = float3(a0.xy, h.x);
            float3 p1 = float3(a0.zw, h.y);
            float3 p2 = float3(a1.xy, h.z);
            float3 p3 = float3(a1.zw, h.w);

            float4 norm = taylorInvSqrt(float4(dot(p0,p0), dot(p1,p1), dot(p2,p2), dot(p3,p3)));
            p0 *= norm.x; p1 *= norm.y; p2 *= norm.z; p3 *= norm.w;

            float4 m = max(0.6 - float4(dot(x0,x0), dot(x1,x1), dot(x2,x2), dot(x3,x3)), 0.0);
            m = m * m;
            return 42.0 * dot(m*m, float4(dot(p0,x0), dot(p1,x1), dot(p2,x2), dot(p3,x3)));
        }

        // Verschiebung entlang der Normale; an einer Stelle ausgewertet.
        float DisplacementAt(float3 posOS, float3 normalOS)
        {
            float3 samplePos = posOS * _NoiseScale + _Time.y * _NoiseSpeed;
            return snoise(samplePos) * _NoiseAmp;
        }

        // Deformierte Position + neu berechnete Normale (finite differences).
        void Deform(float3 posOS, float3 normalOS, out float3 outPos, out float3 outNormal)
        {
            float d = DisplacementAt(posOS, normalOS);
            outPos = posOS + normalOS * d;

            // === TEST: Normalen-Neuberechnung uebersprungen ===
            outNormal = normalOS;
            return;

            // --- Ab hier unerreichbar (Originalcode, fuer spaeteres Zurueckwechseln) ---
            // Tangentenbasis um die Normale herum aufbauen
            float3 tangent = normalize(cross(normalOS, float3(0,1,0) + float3(0.001,0,0)));
            float3 bitangent = normalize(cross(normalOS, tangent));

            float eps = _NormalEps;
            float3 pT = posOS + tangent   * eps;
            float3 pB = posOS + bitangent * eps;

            float3 nT = normalize(normalOS); // Normale am Nachbarpunkt ~ Originalnormale
            float3 nB = normalize(normalOS);

            float dT = DisplacementAt(pT, nT);
            float dB = DisplacementAt(pB, nB);

            float3 displacedT = pT + nT * dT;
            float3 displacedB = pB + nB * dB;

            float3 newT = displacedT - outPos;
            float3 newB = displacedB - outPos;

            outNormal = normalize(cross(newT, newB));
            // Orientierung sicherstellen (nicht nach innen klappen)
            if (dot(outNormal, normalOS) < 0) outNormal = -outNormal;
        }
        ENDHLSL

        // ============================ FORWARD =============================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_SCREEN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float4 screenPos   : TEXCOORD2;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;

                float3 pos, nrm;
                Deform(IN.positionOS.xyz, normalize(IN.normalOS), pos, nrm);

                VertexPositionInputs posInputs = GetVertexPositionInputs(pos);
                OUT.positionCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS   = TransformObjectToWorldNormal(nrm);
                OUT.screenPos  = ComputeScreenPos(posInputs.positionCS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS   = normalize(IN.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord = 0;
                inputData.vertexLighting = half3(0, 0, 0);
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = IN.screenPos.xy / IN.screenPos.w;
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo     = _BaseColor.rgb;
                surfaceData.metallic   = _Metallic;
                surfaceData.smoothness = _Smoothness;
                surfaceData.alpha      = 1.0;
                surfaceData.occlusion  = 1.0;
                surfaceData.specular   = half3(0, 0, 0);
                surfaceData.clearCoatMask = 0;
                surfaceData.clearCoatSmoothness = 0;
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.emission = _EmissionColor.rgb;

                return UniversalFragmentPBR(inputData, surfaceData);
            }
            ENDHLSL
        }

        // ========================= SHADOW CASTER ==========================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma target 3.0

            // Lighting.hlsl laedt Core + Shadows in korrekter Reihenfolge
            // (liefert ApplyShadowBias UND LerpWhiteTo).
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vertShadow (Attributes IN)
            {
                Varyings OUT;

                float3 pos, nrm;
                Deform(IN.positionOS.xyz, normalize(IN.normalOS), pos, nrm);

                float3 positionWS = TransformObjectToWorld(pos);
                float3 normalWS   = TransformObjectToWorldNormal(nrm);

                float4 clip = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, _LightDirection));

                #if UNITY_REVERSED_Z
                    clip.z = min(clip.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    clip.z = max(clip.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                OUT.positionCS = clip;
                return OUT;
            }

            half4 fragShadow (Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // ============================ DEPTH ===============================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }

            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma target 3.0

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vertDepth (Attributes IN)
            {
                Varyings OUT;
                float3 pos, nrm;
                Deform(IN.positionOS.xyz, normalize(IN.normalOS), pos, nrm);
                OUT.positionCS = TransformObjectToHClip(pos);
                return OUT;
            }

            half4 fragDepth (Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack "Hide/InternalErrorShader"
}
