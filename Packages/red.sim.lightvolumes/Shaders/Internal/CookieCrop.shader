Shader "Hidden/VRCLV/CookieCrop"
{
    Properties {
        _MainTex("Texture", 2D) = "white" {}
        _CookieCrop("Cookie Crop", Vector) = (0, 0, 1, 1)
        _CookieCropShape("Cookie Crop Shape", Float) = 0
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
            float _CookieCropShape;

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
                float keep = 1.0;
                if (_CookieCropShape > 0.5) {
                    if (_CookieCropShape < 1.5) keep = i.uv.x + i.uv.y <= 1.0 ? 1.0 : 0.0;
                    else if (_CookieCropShape < 2.5) keep = i.uv.y <= i.uv.x ? 1.0 : 0.0;
                    else if (_CookieCropShape < 3.5) keep = i.uv.y >= i.uv.x ? 1.0 : 0.0;
                    else keep = i.uv.x + i.uv.y >= 1.0 ? 1.0 : 0.0;
                }
                float2 uv = i.uv * _CookieCrop.zw + _CookieCrop.xy;
                return tex2D(_MainTex, uv) * keep;
            }
            ENDCG
        }
    }
}
