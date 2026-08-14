using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace VRCLightVolumes {
    [InitializeOnLoad]
    internal static class DomeMeshLightShaderIntegrationUpdater {
        private const string MochieIncludePath = "Assets/Mochie/Common/LightVolumes.cginc";
        private const string PoiyomiTemplatePath = "Assets/_PoiyomiShaders/ModularShader/Editor/Poi_FeatureModules/Toon/Poi_PerPassIncludes/VRLT_LightVolumesInclude.poiTemplate";
        private const string PoiyomiShadersPath = "Assets/_PoiyomiShaders/Shaders";
        private const string PoiyomiOptimizedShadersPath = "Assets/OptimizedShaders";
        private const string Marker = "// VRCLV_DYNAMIC_MESH_LIGHT_INTEGRATION";

        static DomeMeshLightShaderIntegrationUpdater() {
            EditorApplication.delayCall += UpdateKnownIntegrationsDelayed;
        }

        private static void UpdateKnownIntegrationsDelayed() {
            UpdateKnownIntegrations();
        }

        [MenuItem("Tools/Light Volumes/Update Mesh Light Shader Integrations")]
        private static void UpdateKnownIntegrationsMenu() {
            bool changed = UpdateKnownIntegrations();
            EditorUtility.DisplayDialog("Mesh Light Shader Integrations", changed ? "Mochie and Poiyomi shader integrations were updated to receive the realtime mesh light. Existing material settings were not changed." : "The known shader integrations are already current or are not installed.", "OK");
        }

        private static bool UpdateKnownIntegrations() {
            bool changed = UpdateMochieIntegration();
            changed |= UpdatePoiyomiIntegrations();
            return changed;
        }

        private static bool UpdateMochieIntegration() {
            if (!File.Exists(MochieIncludePath)) return false;
            string originalSource = File.ReadAllText(MochieIncludePath);
            string lineEnding = originalSource.Contains("\r\n") ? "\r\n" : "\n";
            string source = originalSource.Replace("\r\n", "\n");
            if (source.Contains(Marker) || source.Contains("_UdonDynamicMeshLightEnabled")) return false;

            const string textureAnchor = "uniform SamplerState sampler_UdonLightVolume;\n";
            const string boundsAnchor = "float LV_BoundsMask(float3 localUVW, float3 invLocalEdgeSmooth) {\n    float3 fade = saturate((0.5 - abs(localUVW)) * invLocalEdgeSmooth);\n    return fade.x * fade.y * fade.z;\n}\n";
            const string additiveAnchor = "void LV_LightVolumeAdditiveSH(float3 worldPos, inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b) {\n";
            if (!source.Contains(textureAnchor) || !source.Contains(boundsAnchor) || !source.Contains(additiveAnchor)) {
                Debug.LogWarning("[LightVolumes] The installed Mochie Light Volumes include has an unknown layout, so the realtime mesh-light integration was not modified.");
                return false;
            }

            string declarations = textureAnchor + Marker + @"
uniform float _UdonDynamicMeshLightEnabled;
uniform float _UdonDynamicMeshLightL0Only;
uniform Texture3D _UdonDynamicMeshLightTexture0;
uniform Texture3D _UdonDynamicMeshLightTexture1;
uniform Texture3D _UdonDynamicMeshLightTexture2;
uniform SamplerState sampler_UdonDynamicMeshLightTexture0;
uniform float4x4 _UdonDynamicMeshLightInvWorldMatrix[1];
uniform float3 _UdonDynamicMeshLightInvEdgeSmooth;
uniform float4 _UdonDynamicMeshLightColor;
";
            string sampler = boundsAnchor + @"
void LV_SampleDynamicMeshLight(float3 worldPos, inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b) {
    [branch] if (_UdonDynamicMeshLightEnabled == 0) return;
    float3 localUVW = mul(_UdonDynamicMeshLightInvWorldMatrix[0], float4(worldPos, 1)).xyz;
    [branch] if (!LV_PointLocalAABB(localUVW)) return;
    float mask = LV_BoundsMask(localUVW, _UdonDynamicMeshLightInvEdgeSmooth);
    float3 uvw = saturate(localUVW + 0.5);
    float4 tex0 = _UdonDynamicMeshLightTexture0.SampleLevel(sampler_UdonDynamicMeshLightTexture0, uvw, 0);
    float3 color = _UdonDynamicMeshLightColor.rgb * mask;
    L0 += tex0.rgb * color;
    [branch] if (_UdonDynamicMeshLightL0Only != 0) return;
    float4 tex1 = _UdonDynamicMeshLightTexture1.SampleLevel(sampler_UdonDynamicMeshLightTexture0, uvw, 0);
    float4 tex2 = _UdonDynamicMeshLightTexture2.SampleLevel(sampler_UdonDynamicMeshLightTexture0, uvw, 0);
    L1r += float3(tex1.r, tex2.r, tex0.a) * color.r;
    L1g += float3(tex1.g, tex2.g, tex1.a) * color.g;
    L1b += float3(tex1.b, tex2.b, tex2.a) * color.b;
}
";
            source = ReplaceFirst(source, textureAnchor, declarations);
            source = ReplaceFirst(source, boundsAnchor, sampler);
            source = ReplaceFirst(source, additiveAnchor, additiveAnchor + "    LV_SampleDynamicMeshLight(worldPos, L0, L1r, L1g, L1b);\n");
            if (lineEnding == "\r\n") source = source.Replace("\n", "\r\n");
            File.WriteAllText(MochieIncludePath, source, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(MochieIncludePath, ImportAssetOptions.ForceUpdate);
            Debug.Log("[LightVolumes] Updated Mochie shaders to receive the realtime dome mesh light.");
            return true;
        }

        private static bool UpdatePoiyomiIntegrations() {
            bool changed = UpdatePoiyomiFile(PoiyomiTemplatePath);
            changed |= UpdatePoiyomiShadersInDirectory(PoiyomiShadersPath);
            changed |= UpdatePoiyomiShadersInDirectory(PoiyomiOptimizedShadersPath);
            if (changed) Debug.Log("[LightVolumes] Updated Poiyomi shaders to receive the realtime dome mesh light. Enable Light Volumes on the avatar materials before locking or uploading them.");
            return changed;
        }

        private static bool UpdatePoiyomiShadersInDirectory(string directory) {
            if (!Directory.Exists(directory)) return false;
            bool changed = false;
            string[] shaderPaths = Directory.GetFiles(directory, "*.shader", SearchOption.AllDirectories);
            for (int i = 0; i < shaderPaths.Length; i++) {
                changed |= UpdatePoiyomiFile(shaderPaths[i].Replace('\\', '/'));
            }
            return changed;
        }

        private static bool UpdatePoiyomiFile(string path) {
            if (!File.Exists(path)) return false;
            string originalSource = File.ReadAllText(path);
            if (originalSource.Contains(Marker) || originalSource.Contains("_UdonDynamicMeshLightEnabled")) return false;

            string lineEnding = originalSource.Contains("\r\n") ? "\r\n" : "\n";
            string source = originalSource.Replace("\r\n", "\n");
            const string samplerPattern = @"(?m)^(?<indent>[ \t]*)uniform SamplerState sampler_UdonLightVolume;\n";
            const string boundsPattern = @"(?ms)^(?<indent>[ \t]*)float LV_BoundsMask\(float3 localUVW, float3 invLocalEdgeSmooth\) \{.*?^\k<indent>\}\n";
            const string baseSamplingPattern = @"(?m)(^[ \t]*LV_LightVolumeSH\(worldPos \+ worldPosOffset, L0, L1r, L1g, L1b, occlusion\);\n)(?<point>^[ \t]*LV_PointLightVolumeSH\(worldPos, occlusion, L0, L1r, L1g, L1b\);\n)";
            if (!Regex.IsMatch(source, samplerPattern) || !Regex.IsMatch(source, boundsPattern) || !Regex.IsMatch(source, baseSamplingPattern)) return false;

            source = Regex.Replace(source, samplerPattern, delegate(Match match) {
                string indent = match.Groups["indent"].Value;
                return match.Value + IndentBlock(DynamicDeclarations, indent);
            });
            source = Regex.Replace(source, boundsPattern, delegate(Match match) {
                string indent = match.Groups["indent"].Value;
                return match.Value + "\n" + IndentBlock(DynamicSampler, indent);
            });
            source = Regex.Replace(source, baseSamplingPattern, delegate(Match match) {
                string pointLine = match.Groups["point"].Value;
                int contentLength = pointLine.Length - pointLine.TrimStart(' ', '\t').Length;
                string indent = pointLine.Substring(0, contentLength);
                return match.Groups[1].Value + pointLine + indent + "LV_SampleDynamicMeshLight(worldPos + worldPosOffset, L0, L1r, L1g, L1b);\n";
            });

            if (lineEnding == "\r\n") source = source.Replace("\n", "\r\n");
            File.WriteAllText(path, source, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return true;
        }

        private const string DynamicDeclarations = Marker + @"
uniform float _UdonDynamicMeshLightEnabled;
uniform float _UdonDynamicMeshLightL0Only;
uniform Texture3D _UdonDynamicMeshLightTexture0;
uniform Texture3D _UdonDynamicMeshLightTexture1;
uniform Texture3D _UdonDynamicMeshLightTexture2;
uniform SamplerState sampler_UdonDynamicMeshLightTexture0;
uniform float4x4 _UdonDynamicMeshLightInvWorldMatrix[1];
uniform float3 _UdonDynamicMeshLightInvEdgeSmooth;
uniform float4 _UdonDynamicMeshLightColor;
";

        private const string DynamicSampler = @"void LV_SampleDynamicMeshLight(float3 worldPos, inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b) {
#ifndef SHADER_TARGET_SURFACE_ANALYSIS
    [branch] if (_UdonDynamicMeshLightEnabled == 0) return;
    float3 localUVW = mul(_UdonDynamicMeshLightInvWorldMatrix[0], float4(worldPos, 1)).xyz;
    [branch] if (!LV_PointLocalAABB(localUVW)) return;
    float mask = LV_BoundsMask(localUVW, _UdonDynamicMeshLightInvEdgeSmooth);
    float3 uvw = saturate(localUVW + 0.5);
    float4 tex0 = _UdonDynamicMeshLightTexture0.SampleLevel(sampler_UdonDynamicMeshLightTexture0, uvw, 0);
    float3 color = _UdonDynamicMeshLightColor.rgb * mask;
    L0 += tex0.rgb * color;
    [branch] if (_UdonDynamicMeshLightL0Only != 0) return;
    float4 tex1 = _UdonDynamicMeshLightTexture1.SampleLevel(sampler_UdonDynamicMeshLightTexture0, uvw, 0);
    float4 tex2 = _UdonDynamicMeshLightTexture2.SampleLevel(sampler_UdonDynamicMeshLightTexture0, uvw, 0);
    L1r += float3(tex1.r, tex2.r, tex0.a) * color.r;
    L1g += float3(tex1.g, tex2.g, tex1.a) * color.g;
    L1b += float3(tex1.b, tex2.b, tex2.a) * color.b;
#endif
}
";

        private static string IndentBlock(string block, string indent) {
            return indent + block.Replace("\n", "\n" + indent).TrimEnd(' ', '\t') + "\n";
        }

        private static string ReplaceFirst(string source, string oldValue, string newValue) {
            int index = source.IndexOf(oldValue, System.StringComparison.Ordinal);
            return index < 0 ? source : source.Substring(0, index) + newValue + source.Substring(index + oldValue.Length);
        }
    }
}
