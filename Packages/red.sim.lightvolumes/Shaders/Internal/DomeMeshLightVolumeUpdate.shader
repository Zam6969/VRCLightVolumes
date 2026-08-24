Shader "Hidden/VRCLV/DomeMeshLightVolumeUpdate"
{
    Properties
    {
        _SourceTex("Live Atlas", 2D) = "black" {}
        _EmitterPositionArea("Emitter Position and Area", 2D) = "black" {}
        _EmitterNormal("Emitter Normal", 2D) = "black" {}
        _EmitterUv01("Emitter UV 0 and 1", 2D) = "black" {}
        _EmitterUv2("Emitter UV 2", 2D) = "black" {}
        _BakedOcclusion("Baked Screen Shadow Mask", 3D) = "white" {}
        [HideInInspector] _VolumeCenter("Volume Center", Vector) = (0, 0, 0, 0)
        [HideInInspector] _VolumeSize("Volume Size", Vector) = (1, 1, 1, 0)
        [HideInInspector] _EmitterCount("Emitter Count", Float) = 0
        [HideInInspector] _EmitterTexelSize("Emitter Texel Size", Float) = 1
        [HideInInspector] _UseBakedOcclusion("Use Baked Screen Shadows", Float) = 0
        [HideInInspector] _BakedOcclusionChannel("Baked Shadow Channel", Vector) = (1, 0, 0, 0)
        _BakedShadowStrength("Baked Shadow Strength", Range(0, 1)) = 1
        _BakedShadowContrast("Baked Shadow Contrast", Range(0.5, 8)) = 2
        _Intensity("Intensity", Float) = 1
        _ColorSaturation("Screen Color", Range(0, 1)) = 1
        _ProjectionRange("Projection Range", Float) = 10
        _BackfaceFade("Back Surface Fade", Range(0.01, 1)) = 0.25
        _FloorLightBoost("Floor Light Boost", Range(1, 4)) = 2
        _PanelColorSpread("Panel Color Spread", Range(0, 0.04)) = 0.02
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
            sampler3D _BakedOcclusion;
            float3 _VolumeCenter;
            float3 _VolumeSize;
            float _EmitterCount;
            float _EmitterTexelSize;
            float _UseBakedOcclusion;
            float4 _BakedOcclusionChannel;
            float _BakedShadowStrength;
            float _BakedShadowContrast;
            float _Intensity;
            float _ColorSaturation;
            float _ProjectionRange;
            float _BackfaceFade;
            float _FloorLightBoost;
            float _PanelColorSpread;
            int _OutputChannel;

            float4 frag(v2f_customrendertexture i) : SV_Target
            {
                float3 worldPos = _VolumeCenter + (i.localTexcoord.xyz - 0.5) * _VolumeSize;
                float3 l0 = 0;
                float3 l1r = 0;
                float3 l1g = 0;
                float3 l1b = 0;
                float invRangeSq = rcp(max(_ProjectionRange * _ProjectionRange, 1e-4));
                float nearestFrontDistSq = 1e30;
                float nearestBackDistSq = 1e30;

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
                    float signedDistance = dot(emitterNormal, receiverFromEmitter);
                    if (signedDistance >= 0) nearestFrontDistSq = min(nearestFrontDistSq, distSq);
                    else nearestBackDistSq = min(nearestBackDistSq, distSq);
                    float invDist = rsqrt(distSq);
                    float3 emitterToReceiver = receiverFromEmitter * invDist;
                    float emitterFacing = saturate(signedDistance * invDist);
                    float rangeMask = saturate(1.0 - distSq * invRangeSq);
                    if (emitterFacing <= 0 || rangeMask <= 0) continue;

                    float2 sampleUv0 = uv01.xy;
                    float2 sampleUv1 = uv01.zw;
                    float2 sampleUv2 = uv2;
                    float duplicateSpanSq = dot(sampleUv0 - sampleUv1, sampleUv0 - sampleUv1) + dot(sampleUv0 - sampleUv2, sampleUv0 - sampleUv2);
                    if (duplicateSpanSq < 1e-10)
                    {
                        sampleUv0 += float2(0.0, 1.0) * _PanelColorSpread;
                        sampleUv1 += float2(-0.8660254, -0.5) * _PanelColorSpread;
                        sampleUv2 += float2(0.8660254, -0.5) * _PanelColorSpread;
                    }
                    float4 sample0 = tex2Dlod(_SourceTex, float4(saturate(sampleUv0), 0, 0));
                    float4 sample1 = tex2Dlod(_SourceTex, float4(saturate(sampleUv1), 0, 0));
                    float4 sample2 = tex2Dlod(_SourceTex, float4(saturate(sampleUv2), 0, 0));
                    float3 emission = (sample0.rgb + sample1.rgb + sample2.rgb) * 0.3333333333;
                    float neutralEmission = dot(emission, float3(0.2126, 0.7152, 0.0722));
                    emission = lerp(neutralEmission.xxx, emission, _ColorSaturation);
                    float sourceRadiusSq = max(positionArea.w * VRCLV_INV_PI, 1e-4);
                    float floorMask = smoothstep(0.0, max(_ProjectionRange * 0.25, 0.5), positionArea.y - worldPos.y);
                    float floorBoost = lerp(1.0, _FloorLightBoost, floorMask);
                    float weight = _Intensity * floorBoost * positionArea.w * emitterFacing * VRCLV_INV_PI * rcp(distSq + sourceRadiusSq) * rangeMask * rangeMask;
                    float3 contribution = emission * weight;
                    float3 receiverToEmitter = -emitterToReceiver;

                    l0 += contribution;
                    l1r += receiverToEmitter * contribution.r;
                    l1g += receiverToEmitter * contribution.g;
                    l1b += receiverToEmitter * contribution.b;
                }

                // Compare the nearest front and back surfaces instead of allowing one irregular
                // panel centroid to erase valid light from another nearby front-facing panel.
                float nearestFrontDistance = sqrt(nearestFrontDistSq);
                float nearestBackDistance = sqrt(nearestBackDistSq);
                float shellBias = max(_BackfaceFade * 4.0, 0.5);
                float shellSide = nearestBackDistance - nearestFrontDistance + shellBias;
                float shellMask = smoothstep(-_BackfaceFade, _BackfaceFade, shellSide);
                l0 *= shellMask;
                l1r *= shellMask;
                l1g *= shellMask;
                l1b *= shellMask;

                float4 bakedOcclusionSample = tex3Dlod(_BakedOcclusion, float4(saturate(i.localTexcoord.xyz), 0));
                float bakedVisibility = saturate(dot(bakedOcclusionSample, _BakedOcclusionChannel));
                bakedVisibility = pow(bakedVisibility, max(_BakedShadowContrast, 0.5));
                float bakedShadow = lerp(1.0, bakedVisibility, saturate(_UseBakedOcclusion) * saturate(_BakedShadowStrength));
                l0 *= bakedShadow;
                l1r *= bakedShadow;
                l1g *= bakedShadow;
                l1b *= bakedShadow;

                if (_OutputChannel == 0) return float4(l0, l1r.z);
                if (_OutputChannel == 1) return float4(l1r.x, l1g.x, l1b.x, l1g.z);
                return float4(l1r.y, l1g.y, l1b.y, l1b.z);
            }
            ENDCG
        }
    }
}
