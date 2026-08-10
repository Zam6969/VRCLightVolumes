Shader "Hidden/VRCLV/CookieCrop"
{
    Properties {
        _MainTex("Texture", 2D) = "white" {}
        _CookieCrop("Cookie Crop", Vector) = (0, 0, 1, 1)
        _CookieCropShape("Cookie Crop Shape", Float) = 0
        _CookieCropRotation("Cookie Crop Rotation", Float) = 0
        _CookieCropTriangleA("Cookie Crop Triangle A", Vector) = (0, 0, 0, 0)
        _CookieCropTriangleB("Cookie Crop Triangle B", Vector) = (1, 0, 0, 0)
        _CookieCropTriangleC("Cookie Crop Triangle C", Vector) = (0, 1, 0, 0)
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
            float4 _MainTex_TexelSize;
            float4 _CookieCrop;
            float _CookieCropShape;
            float _CookieCropRotation;
            float4 _CookieCropTriangleA;
            float4 _CookieCropTriangleB;
            float4 _CookieCropTriangleC;

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

            float TriangleSign(float2 p1, float2 p2, float2 p3) {
                return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
            }

            float PointInTriangle(float2 p, float2 a, float2 b, float2 c) {
                if (abs(TriangleSign(a, b, c)) < 0.00001) return 0.0;
                float d1 = TriangleSign(p, a, b);
                float d2 = TriangleSign(p, b, c);
                float d3 = TriangleSign(p, c, a);
                bool hasNegative = d1 < 0.0 || d2 < 0.0 || d3 < 0.0;
                bool hasPositive = d1 > 0.0 || d2 > 0.0 || d3 > 0.0;
                return hasNegative && hasPositive ? 0.0 : 1.0;
            }

            float4 frag(v2f i) : SV_Target {
                float radians = _CookieCropRotation * 0.01745329252;
                float s;
                float c;
                sincos(radians, s, c);
                float2 centeredUv = i.uv - 0.5;
                float absS = abs(s);
                float absC = abs(c);
                float cropPixelAspect = max(abs(_CookieCrop.z) * _MainTex_TexelSize.z, 0.0001) / max(abs(_CookieCrop.w) * _MainTex_TexelSize.w, 0.0001);
                float fitScaleX = cropPixelAspect / max(absC * cropPixelAspect + absS, 0.0001);
                float fitScaleY = 1.0 / max(absS * cropPixelAspect + absC, 0.0001);
                float fitScale = max(min(fitScaleX, fitScaleY), 0.0001);
                centeredUv /= fitScale;
                float2 localUv = float2(centeredUv.x * c + centeredUv.y * s, -centeredUv.x * s + centeredUv.y * c) + 0.5;
                float keep = localUv.x >= 0.0 && localUv.x <= 1.0 && localUv.y >= 0.0 && localUv.y <= 1.0 ? 1.0 : 0.0;
                if (_CookieCropShape > 0.5) {
                    if (_CookieCropShape > 4.5) keep *= PointInTriangle(localUv, _CookieCropTriangleA.xy, _CookieCropTriangleB.xy, _CookieCropTriangleC.xy);
                    else if (_CookieCropShape < 1.5) keep *= localUv.x + localUv.y <= 1.0 ? 1.0 : 0.0;
                    else if (_CookieCropShape < 2.5) keep *= localUv.y <= localUv.x ? 1.0 : 0.0;
                    else if (_CookieCropShape < 3.5) keep *= localUv.y >= localUv.x ? 1.0 : 0.0;
                    else keep *= localUv.x + localUv.y >= 1.0 ? 1.0 : 0.0;
                }
                float2 uv = localUv * _CookieCrop.zw + _CookieCrop.xy;
                return tex2D(_MainTex, uv) * keep;
            }
            ENDCG
        }
    }
}
