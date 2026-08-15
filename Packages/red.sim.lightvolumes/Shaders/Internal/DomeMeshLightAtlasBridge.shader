Shader "Hidden/VRCLV/DomeMeshLightAtlasBridge"
{
    Properties
    {
        _MainTex("Light Volume Atlas", 3D) = "black" {}
        _DynamicTexture0("Realtime Lighting 0", 3D) = "black" {}
        _DynamicTexture1("Realtime Lighting 1", 3D) = "black" {}
        _DynamicTexture2("Realtime Lighting 2", 3D) = "black" {}
        [HideInInspector] _DynamicBounds0("Atlas Bounds 0", Vector) = (0, 0, 0, 0)
        [HideInInspector] _DynamicBounds1("Atlas Bounds 1", Vector) = (0, 0, 0, 0)
        [HideInInspector] _DynamicBounds2("Atlas Bounds 2", Vector) = (0, 0, 0, 0)
        [HideInInspector] _DynamicEnabled("Enabled", Float) = 0
        [HideInInspector] _L0Only("L0 Only", Float) = 0
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
            float4 _DynamicBounds0;
            float4 _DynamicBounds1;
            float4 _DynamicBounds2;
            float _DynamicEnabled;
            float _L0Only;

            bool TryRemap(float3 atlasUvw, float3 regionMin, float3 regionScale, out float3 localUvw)
            {
                localUvw = (atlasUvw - regionMin) / max(regionScale, 1e-6);
                return all(localUvw >= 0.0) && all(localUvw <= 1.0);
            }

            float4 frag(v2f_customrendertexture i) : SV_Target
            {
                float3 atlasUvw = i.localTexcoord.xyz;
                float4 baseValue = tex3Dlod(_MainTex, float4(atlasUvw, 0));
                if (_DynamicEnabled < 0.5) return baseValue;

                float3 regionScale = float3(_DynamicBounds0.w, _DynamicBounds1.w, _DynamicBounds2.w);
                float3 localUvw;
                if (TryRemap(atlasUvw, _DynamicBounds0.xyz, regionScale, localUvw))
                    return tex3Dlod(_DynamicTexture0, float4(saturate(localUvw), 0));
                if (TryRemap(atlasUvw, _DynamicBounds1.xyz, regionScale, localUvw))
                    return _L0Only > 0.5 ? 0 : tex3Dlod(_DynamicTexture1, float4(saturate(localUvw), 0));
                if (TryRemap(atlasUvw, _DynamicBounds2.xyz, regionScale, localUvw))
                    return _L0Only > 0.5 ? 0 : tex3Dlod(_DynamicTexture2, float4(saturate(localUvw), 0));
                return baseValue;
            }
            ENDCG
        }
    }
}
