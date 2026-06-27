Shader "Custom/WorldSpaceTexture"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Scale ("Texture Scale", Float) = 1.0
        _Color ("Tint Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        float _Scale;
        fixed4 _Color;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float3 worldPos = IN.worldPos * _Scale;
            float3 blendWeights = abs(IN.worldNormal);
            blendWeights = blendWeights / (blendWeights.x + blendWeights.y + blendWeights.z);

            fixed4 xProjection = tex2D(_MainTex, worldPos.zy);
            fixed4 yProjection = tex2D(_MainTex, worldPos.xz);
            fixed4 zProjection = tex2D(_MainTex, worldPos.xy);

            fixed4 blendedColor = xProjection * blendWeights.x + 
                                  yProjection * blendWeights.y + 
                                  zProjection * blendWeights.z;

            o.Albedo = blendedColor.rgb * _Color.rgb;
            o.Alpha = blendedColor.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}