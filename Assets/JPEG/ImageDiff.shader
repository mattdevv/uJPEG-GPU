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

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col = tex2D(_MainTex, i.uv);
                fixed4 col2 = tex2D(_DiffTex, i.uv);
                
                return _Scalar * abs(col - col2);
            }
            ENDHLSL
        }
    }
}
