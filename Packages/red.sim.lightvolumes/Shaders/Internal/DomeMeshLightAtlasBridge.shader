Shader "Hidden/VRCLV/DomeMeshLightAtlasBridge"
{
    Properties
    {
        _MainTex("Light Volume Atlas", 3D) = "black" {}
        _DynamicTexture0("Realtime Lighting 0", 3D) = "black" {}
        _DynamicTexture1("Realtime Lighting 1", 3D) = "black" {}
        _DynamicTexture2("Realtime Lighting 2", 3D) = "black" {}
        _ScreenVisibility("Screen-Origin Shadow Field", 3D) = "white" {}
        _ScreenBakeL0("Lightmapper Screen Field", 3D) = "white" {}
        [HideInInspector] _DynamicBounds0("Atlas Bounds 0", Vector) = (0, 0, 0, 0)
        [HideInInspector] _DynamicBounds1("Atlas Bounds 1", Vector) = (0, 0, 0, 0)
        [HideInInspector] _DynamicBounds2("Atlas Bounds 2", Vector) = (0, 0, 0, 0)
        [HideInInspector] _AtlasTexelSize("Atlas Texel Size", Vector) = (0, 0, 0, 0)
        [HideInInspector] _DynamicEnabled("Enabled", Float) = 0
        [HideInInspector] _L0Only("L0 Only", Float) = 0
        [HideInInspector] _UseScreenVisibility("Use Screen-Origin Shadows", Float) = 0
        [HideInInspector] _ScreenShadowStrength("Screen Shadow Strength", Range(0, 1)) = 1
        [HideInInspector] _ScreenShadowContrast("Screen Shadow Contrast", Range(0.5, 8)) = 2
        [HideInInspector] _UseScreenBake("Use Lightmapper Screen Field", Float) = 0
        [HideInInspector] _ScreenBakeNormalization("Lightmapper Field Normalization", Float) = 1
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

            sampler3D _MainTex;
            sampler3D _DynamicTexture0;
            sampler3D _DynamicTexture1;
            sampler3D _DynamicTexture2;
            sampler3D _ScreenVisibility;
            sampler3D _ScreenBakeL0;
            float4 _DynamicBounds0;
            float4 _DynamicBounds1;
            float4 _DynamicBounds2;
            float3 _AtlasTexelSize;
            float _DynamicEnabled;
            float _L0Only;
            float _UseScreenVisibility;
            float _ScreenShadowStrength;
            float _ScreenShadowContrast;
            float _UseScreenBake;
            float _ScreenBakeNormalization;

            float ScreenVisibility(float3 localUvw)
            {
                float visibility = 1.0;
                if (_UseScreenBake > 0.5)
                {
                    float3 bakedL0 = tex3Dlod(_ScreenBakeL0, float4(saturate(localUvw), 0)).rgb;
                    // Bakery stores physical irradiance rather than a 0-1 visibility mask. After
                    // normalizing the brightest voxel, recover the useful low end of that HDR
                    // signal so ordinary visible voxels do not collapse to black.
                    float transport = max(bakedL0.r, max(bakedL0.g, bakedL0.b)) * _ScreenBakeNormalization;
                    visibility = saturate(transport * 512.0);
                    visibility = 1.0 - pow(1.0 - visibility, max(_ScreenShadowContrast, 0.5));
                    return lerp(1.0, visibility, saturate(_ScreenShadowStrength));
                }
                else if (_UseScreenVisibility > 0.5)
                {
                    visibility = tex3Dlod(_ScreenVisibility, float4(saturate(localUvw), 0)).r;
                }
                visibility = pow(saturate(visibility), max(_ScreenShadowContrast, 0.5));
                return lerp(1.0, visibility, saturate(_ScreenShadowStrength));
            }

            bool TryRemapPadded(float3 atlasUvw, float3 regionMin, float3 regionScale, out float3 localUvw)
            {
                localUvw = (atlasUvw - regionMin) / max(regionScale, 1e-6);
                float3 localPadding = _AtlasTexelSize / max(regionScale, 1e-6);
                return all(localUvw >= -localPadding) && all(localUvw <= 1.0 + localPadding);
            }

            float4 frag(v2f_customrendertexture i) : SV_Target
            {
                float3 atlasUvw = i.localTexcoord.xyz;
                float4 baseValue = tex3Dlod(_MainTex, float4(atlasUvw, 0));
                if (_DynamicEnabled < 0.5) return baseValue;

                float3 regionScale = float3(_DynamicBounds0.w, _DynamicBounds1.w, _DynamicBounds2.w);
                float3 localUvw;
                // The packed atlas samples the content boundary halfway between the content and its
                // one-voxel padding. Fill that padding with the clamped live edge to prevent white
                // reserved L0 data from bleeding into the spatial fade.
                if (TryRemapPadded(atlasUvw, _DynamicBounds0.xyz, regionScale, localUvw))
                    return tex3Dlod(_DynamicTexture0, float4(saturate(localUvw), 0)) * ScreenVisibility(localUvw);
                if (TryRemapPadded(atlasUvw, _DynamicBounds1.xyz, regionScale, localUvw))
                    return _L0Only > 0.5 ? 0 : tex3Dlod(_DynamicTexture1, float4(saturate(localUvw), 0)) * ScreenVisibility(localUvw);
                if (TryRemapPadded(atlasUvw, _DynamicBounds2.xyz, regionScale, localUvw))
                    return _L0Only > 0.5 ? 0 : tex3Dlod(_DynamicTexture2, float4(saturate(localUvw), 0)) * ScreenVisibility(localUvw);
                return baseValue;
            }
            ENDCG
        }
    }
}
