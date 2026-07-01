Shader "Traumwelt/InteriorMapping"
{
    // -------------------------------------------------------------------------
    // INTERIOR MAPPING
    // Erzeugt die Illusion von echten 3D-Raeumen HINTER einer flachen Wand,
    // komplett ohne zusaetzliche Geometrie. Nur Mathe im Fragment-Shader.
    // (Bekannt aus den Wolkenkratzern in Spider-Man.)
    //
    // Funktioniert sofort prozedural (Farben pro Wand/Boden/Decke).
    // Optional: eine Cubemap zuweisen + "Use Cubemap" anhaken.
    // -------------------------------------------------------------------------
    Properties
    {
        [Header(Raum Layout)]
        _Rooms      ("Raeume pro Achse (Tiling)", Float) = 4
        _RoomDepth  ("Raumtiefe", Range(0.2, 4)) = 1

        [Header(Prozedurale Farben)]
        _BackColor  ("Rueckwand", Color)  = (0.45, 0.42, 0.38, 1)
        _WallColor  ("Seitenwand", Color) = (0.30, 0.28, 0.26, 1)
        _FloorColor ("Boden", Color)      = (0.18, 0.16, 0.15, 1)
        _CeilColor  ("Decke", Color)      = (0.85, 0.83, 0.78, 1)
        _BackDim    ("Tiefen Abdunklung", Range(0,1)) = 0.55

        [Header(Stadt Look)]
        _DarkRoomAmount ("Anteil dunkler Raeume", Range(0,1)) = 0.3
        _DarkRoomDim    ("Wie dunkel", Range(0,1)) = 0.12

        [Header(Cubemap optional)]
        [Toggle(USE_CUBEMAP)] _UseCubemap ("Cubemap benutzen", Float) = 0
        [NoScaleOffset] _RoomCube ("Raum Cubemap", Cube) = "" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local USE_CUBEMAP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // -------- Uniforms --------
            CBUFFER_START(UnityPerMaterial)
                float  _Rooms;
                float  _RoomDepth;
                float4 _BackColor;
                float4 _WallColor;
                float4 _FloorColor;
                float4 _CeilColor;
                float  _BackDim;
                float  _DarkRoomAmount;
                float  _DarkRoomDim;
            CBUFFER_END

            TEXTURECUBE(_RoomCube);
            SAMPLER(sampler_RoomCube);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 viewDirTS   : TEXCOORD1; // Blickrichtung im Tangentenraum
            };

            // kleiner Hash fuer "Zufalls"-Raeume (Licht an/aus)
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrm = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionHCS = pos.positionCS;
                OUT.uv = IN.uv;

                // Blickrichtung (Oberflaeche -> Kamera) in den Tangentenraum drehen,
                // sodass die Wand-Normale = +Z zeigt.
                float3 viewWS = GetWorldSpaceViewDir(pos.positionWS);
                OUT.viewDirTS = float3(
                    dot(viewWS, nrm.tangentWS),
                    dot(viewWS, nrm.bitangentWS),
                    dot(viewWS, nrm.normalWS)
                );
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // --- 1. UV in einzelne Raeume kacheln, lokal auf -1..1 bringen ---
                float2 roomUV = frac(IN.uv * _Rooms) * 2.0 - 1.0;
                float2 roomId = floor(IN.uv * _Rooms); // welcher Raum (fuer Zufall)

                // --- 2. Strahl ins Zimmer: V zeigt zur Kamera (+z), D = ins Zimmer ---
                float3 V = normalize(IN.viewDirTS);
                float3 D = -V;
                D.z /= _RoomDepth; // groesser = tiefere Raeume

                // --- 3. Ray-Box-Schnitt. Box: x,y,z in [-1,1].
                //        Start O liegt auf der Fensterflaeche (z = 1). ---
                float3 O = float3(roomUV, 1.0);
                float3 invD = 1.0 / D;
                float3 t1 = (-1.0 - O) * invD;
                float3 t2 = ( 1.0 - O) * invD;
                float3 tmax = max(t1, t2);      // Austritts-Zeit pro Achse
                float  t = min(min(tmax.x, tmax.y), tmax.z); // erste getroffene Wand
                float3 hit = O + D * t;          // Trefferpunkt in der Box

                // --- 4. Welche Flaeche wurde getroffen? ---
                half3 col;
            #ifdef USE_CUBEMAP
                // Trefferrichtung als Cubemap-Sample
                col = SAMPLE_TEXTURECUBE(_RoomCube, sampler_RoomCube, hit).rgb;
            #else
                if (tmax.z <= tmax.x && tmax.z <= tmax.y)
                    col = _BackColor.rgb;                       // Rueckwand
                else if (tmax.x <= tmax.y)
                    col = _WallColor.rgb;                       // Seitenwand
                else
                    col = (hit.y < 0) ? _FloorColor.rgb : _CeilColor.rgb; // Boden/Decke
            #endif

                // --- 5. Fake-Tiefe: weiter hinten = dunkler ---
                float depthFade = saturate((hit.z + 1.0) * 0.5); // 0 hinten .. 1 vorne
                col *= lerp(_BackDim, 1.0, depthFade);

                // --- 6. Stadt-Look: ein Teil der Raeume ist "unbeleuchtet" ---
                float lit = hash21(roomId);
                if (lit < _DarkRoomAmount)
                    col *= _DarkRoomDim;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
