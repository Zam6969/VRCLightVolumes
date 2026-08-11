Shader "Hidden/VRCLV/DomeMeshLightVolumeUpdate"
{
    Properties
    {
        _SourceTex("Live Atlas", 2D) = "black" {}
        _EmitterPositionArea("Emitter Position and Area", 2D) = "black" {}
        _EmitterNormal("Emitter Normal", 2D) = "black" {}
        _EmitterUv01("Emitter UV 0 and 1", 2D) = "black" {}
        _EmitterUv2("Emitter UV 2", 2D) = "black" {}
        [HideInInspector] _VolumeCenter("Volume Center", Vector) = (0, 0, 0, 0)
        [HideInInspector] _VolumeSize("Volume Size", Vector) = (1, 1, 1, 0)
        [HideInInspector] _EmitterCount("Emitter Count", Float) = 0
        [HideInInspector] _EmitterTexelSize("Emitter Texel Size", Float) = 1
        _Intensity("Intensity", Float) = 1
        _ColorSaturation("Screen Color", Range(0, 1)) = 1
        _ProjectionRange("Projection Range", Float) = 10
        _OutputChannel("Output Channel", Int) = 0
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex CustomRenderTextureVertexShader
            #pragma fragment frag
            #include "UnityCustomRenderTexture.cginc"

            #define VRCLV_DOME_MAX_EMITTERS 128
            #define VRCLV_INV_PI 0.31830988618

            sampler2D _SourceTex;
            sampler2D _EmitterPositionArea;
            sampler2D _EmitterNormal;
            sampler2D _EmitterUv01;
            sampler2D _EmitterUv2;
            float3 _VolumeCenter;
            float3 _VolumeSize;
            float _EmitterCount;
            float _EmitterTexelSize;
            float _Intensity;
            float _ColorSaturation;
            float _ProjectionRange;
            int _OutputChannel;

            float4 frag(v2f_customrendertexture i) : SV_Target
            {
                float3 worldPos = _VolumeCenter + (i.localTexcoord.xyz - 0.5) * _VolumeSize;
                float3 l0 = 0;
                float3 l1r = 0;
                float3 l1g = 0;
                float3 l1b = 0;
                float invRangeSq = rcp(max(_ProjectionRange * _ProjectionRange, 1e-4));

                [loop] for (int emitterIndex = 0; emitterIndex < VRCLV_DOME_MAX_EMITTERS; emitterIndex++)
                {
                    if (emitterIndex >= (int)_EmitterCount) break;
                    float emitterU = (emitterIndex + 0.5) * _EmitterTexelSize;
                    float4 positionArea = tex2Dlod(_EmitterPositionArea, float4(emitterU, 0.5, 0, 0));
                    float3 emitterNormal = tex2Dlod(_EmitterNormal, float4(emitterU, 0.5, 0, 0)).xyz;
                    float4 uv01 = tex2Dlod(_EmitterUv01, float4(emitterU, 0.5, 0, 0));
                    float2 uv2 = tex2Dlod(_EmitterUv2, float4(emitterU, 0.5, 0, 0)).xy;

                    float3 receiverFromEmitter = worldPos - positionArea.xyz;
                    float distSq = max(dot(receiverFromEmitter, receiverFromEmitter), 1e-4);
                    float invDist = rsqrt(distSq);
                    float3 emitterToReceiver = receiverFromEmitter * invDist;
                    float emitterFacing = saturate(dot(emitterNormal, emitterToReceiver));
                    float rangeMask = saturate(1.0 - distSq * invRangeSq);
                    if (emitterFacing <= 0 || rangeMask <= 0) continue;

                    float4 sample0 = tex2Dlod(_SourceTex, float4(uv01.xy, 0, 0));
                    float4 sample1 = tex2Dlod(_SourceTex, float4(uv01.zw, 0, 0));
                    float4 sample2 = tex2Dlod(_SourceTex, float4(uv2, 0, 0));
                    float3 emission = (sample0.rgb + sample1.rgb + sample2.rgb) * 0.3333333333;
                    float neutralEmission = dot(emission, float3(0.2126, 0.7152, 0.0722));
                    emission = lerp(neutralEmission.xxx, emission, _ColorSaturation);
                    float sourceRadiusSq = max(positionArea.w * VRCLV_INV_PI, 1e-4);
                    float weight = _Intensity * positionArea.w * emitterFacing * VRCLV_INV_PI * rcp(distSq + sourceRadiusSq) * rangeMask * rangeMask;
                    float3 contribution = emission * weight;
                    float3 receiverToEmitter = -emitterToReceiver;

                    l0 += contribution;
                    l1r += receiverToEmitter * contribution.r;
                    l1g += receiverToEmitter * contribution.g;
                    l1b += receiverToEmitter * contribution.b;
                }

                if (_OutputChannel == 0) return float4(l0, l1r.z);
                if (_OutputChannel == 1) return float4(l1r.x, l1g.x, l1b.x, l1g.z);
                return float4(l1r.y, l1g.y, l1b.y, l1b.z);
            }
            ENDCG
        }
    }
}
