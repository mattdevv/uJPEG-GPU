Shader "Unlit/ImageDiff"
{
    Properties
    {
        [MainTexture] _MainTex ("Main Texture", 2D) = "white" {}
        _DiffTex ("Difference Texture", 2D) = "black" {}
        _Scalar ("Scalar", Range(1, 10)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            sampler2D _DiffTex;
            float _Scalar;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            
            // Converts a color from linear light gamma to sRGB gamma
            float4 fromLinear(float4 linearRGB)
            {
                float3 cutoff = step(linearRGB.rgb, 0.0031308);
                float3 higher = 1.055 * pow(linearRGB.rgb, 1.0/2.4) - 0.055;
                float3 lower = linearRGB.rgb * 12.92;

                return float4(lerp(higher, lower, cutoff), linearRGB.a);
            }

            float4 frag (v2f i) : SV_Target
            {                
                // sample the texture
                float4 srgb1 = (tex2D(_MainTex, i.uv));
                float4 srgb2 = (tex2D(_DiffTex, i.uv));
                float4 delta = srgb1 - srgb2;
                
                return abs(delta) * _Scalar;
            }
            ENDHLSL
        }
    }
}
