Shader "Hidden/VRCLV/CookieCrop"
{
    Properties {
        _MainTex("Texture", 2D) = "white" {}
        _CookieCrop("Cookie Crop", Vector) = (0, 0, 1, 1)
    }
    SubShader {
        Tags { "RenderType" = "Opaque" }
        Cull Off ZWrite Off ZTest Always

        Pass {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _CookieCrop;

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert(appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target {
                float2 uv = i.uv * _CookieCrop.zw + _CookieCrop.xy;
                return tex2D(_MainTex, uv);
            }
            ENDCG
        }
    }
}
