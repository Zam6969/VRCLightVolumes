using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VRCLightVolumes.Editor;

namespace VRCLightVolumes {
    internal static class DomeMeshLightAtlasBridgeUtility {
        internal const string BridgeShaderName = "Hidden/VRCLV/DomeMeshLightAtlasBridge";
        internal const string BridgeVolumeName = "Realtime Mesh Light - Standard Light Volume";
        private const string DefaultOutputFolder = "Assets/LightVolumesDome";

        internal static bool TryGetBridge(LightVolumeManager manager, out LightVolumeInstance volume, out Material material, out CustomRenderTexture output) {
            volume = null;
            material = null;
            output = null;
            if (manager == null) return false;

            LightVolumeInstance[] volumes = manager.LightVolumeInstances;
            if (volumes != null) {
                for (int i = 0; i < volumes.Length; i++) {
                    LightVolumeInstance candidate = volumes[i];
                    if (candidate == null || candidate.Bake || !candidate.ReserveUVSpace || candidate.gameObject.name != BridgeVolumeName) continue;
                    volume = candidate;
                    break;
                }
            }

            Material[] materials = manager.AtlasPostProcessorMaterials;
            RenderTexture[] targets = manager.AtlasPostProcessorTargets;
            if (materials != null && targets != null) {
                int count = Mathf.Min(materials.Length, targets.Length);
                for (int i = 0; i < count; i++) {
                    Material candidate = materials[i];
                    if (candidate == null || candidate.shader == null || candidate.shader.name != BridgeShaderName) continue;
                    material = candidate;
                    output = targets[i] as CustomRenderTexture;
                    break;
                }
            }
            return volume != null && material != null && output != null;
        }

        internal static bool CreateFromExisting(LightVolumeManager manager) {
            if (manager == null || manager.DynamicMeshLightTexture0 == null) return false;
            Matrix4x4 volumeMatrix = manager.DynamicMeshLightInvWorldMatrix.inverse;
            Vector3 center = volumeMatrix.MultiplyPoint3x4(Vector3.zero);
            Vector3 size = new Vector3(volumeMatrix.GetColumn(0).magnitude, volumeMatrix.GetColumn(1).magnitude, volumeMatrix.GetColumn(2).magnitude);
            int resolution = Mathf.Max(manager.DynamicMeshLightTexture0.width, 4);
            float edgeFade = GetEdgeFade(size, manager.DynamicMeshLightInvEdgeSmooth);
            float updatesPerSecond = GetUpdatesPerSecond(manager.DynamicMeshLightTexture0 as CustomRenderTexture);
            string outputFolder = GetOutputFolder(manager.DynamicMeshLightTexture0);
            return Create(manager, center, size, resolution, edgeFade, updatesPerSecond, outputFolder);
        }

        internal static bool Create(LightVolumeManager manager, Vector3 center, Vector3 size, int resolution, float edgeFade, float updatesPerSecond, string outputFolder) {
            if (manager == null) return false;
            if (TryGetBridge(manager, out LightVolumeInstance existingVolume, out _, out _)) {
                ConfigureVolume(existingVolume, manager, center, size, resolution, edgeFade);
                manager.DynamicMeshLightEnabled = false;
                SyncAtlasMaterial(manager);
                LightVolumeManagerEditorBackend.GenerateAtlas(manager);
                return true;
            }

            Shader shader = Shader.Find(BridgeShaderName);
            if (shader == null) throw new System.InvalidOperationException("The standard Light Volume atlas bridge shader is still importing.");
            outputFolder = NormalizeAssetFolder(outputFolder);
            EnsureAssetFolder(outputFolder);

            GameObject bridgeObject = new GameObject(BridgeVolumeName);
            Undo.RegisterCreatedObjectUndo(bridgeObject, "Create Standard Realtime Light Volume");
            bridgeObject.transform.position = center;
            bridgeObject.transform.rotation = Quaternion.identity;
            bridgeObject.transform.localScale = size;
            bridgeObject.transform.SetParent(manager.transform, true);
            LightVolumeInstance volume = Undo.AddComponent<LightVolumeInstance>(bridgeObject);
            ConfigureVolume(volume, manager, center, size, resolution, edgeFade);
            manager.InitializeLightVolume(volume);

            Material material = new Material(shader) { name = "DomeMeshLightAtlasBridge" };
            SetSourceTextures(material, manager);
            AssetDatabase.CreateAsset(material, AssetDatabase.GenerateUniqueAssetPath(outputFolder + "/DomeMeshLightAtlasBridge.mat"));

            CustomRenderTexture output = new CustomRenderTexture(4, 4, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) {
                name = "DomeMeshLightStandardAtlas",
                dimension = TextureDimension.Tex3D,
                volumeDepth = 4,
                material = material,
                updateMode = CustomRenderTextureUpdateMode.Realtime,
                updatePeriod = 1f / Mathf.Max(updatesPerSecond, 1f),
                initializationColor = Color.clear,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            AssetDatabase.CreateAsset(output, AssetDatabase.GenerateUniqueAssetPath(outputFolder + "/DomeMeshLightStandardAtlas.asset"));

            Undo.RecordObject(manager, "Publish Realtime Mesh Light Through Standard Atlas");
            manager.DynamicMeshLightEnabled = false;
            manager.Editor.RegisterPostProcessor(output);
            SyncAtlasMaterial(manager);
            LightVolumeManagerEditorBackend.CopyProxyToUdon(volume);
            LightVolumeManagerEditorBackend.CopyProxyToUdon(manager);
            LightVolumeManagerEditorBackend.GenerateAtlas(manager);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            AssetDatabase.SaveAssets();
            return true;
        }

        internal static bool SyncAtlasMaterial(LightVolumeManager manager) {
            if (!TryGetBridge(manager, out LightVolumeInstance volume, out Material material, out CustomRenderTexture output)) return false;
            if (volume.SmoothBlending <= 0.0001f) {
                volume.SmoothBlending = 0.5f;
                volume.UpdateTransform();
                EditorUtility.SetDirty(volume);
                LightVolumeManagerEditorBackend.CopyProxyToUdon(volume);
            }
            SetSourceTextures(material, manager);
            material.SetVector("_DynamicBounds0", volume.BoundsUvwMin0);
            material.SetVector("_DynamicBounds1", volume.BoundsUvwMin1);
            material.SetVector("_DynamicBounds2", volume.BoundsUvwMin2);
            material.SetVector("_AtlasTexelSize", new Vector4(
                1f / Mathf.Max(output.width, 1),
                1f / Mathf.Max(output.height, 1),
                1f / Mathf.Max(output.volumeDepth, 1),
                0f));
            bool hasTextures = manager.DynamicMeshLightTexture0 != null && (manager.DynamicMeshLightL0Only || manager.DynamicMeshLightTexture1 != null && manager.DynamicMeshLightTexture2 != null);
            bool bridgeEnabled = IsBridgeEnabled(volume);
            float previousEnabled = material.GetFloat("_DynamicEnabled");
            material.SetFloat("_DynamicEnabled", hasTextures && bridgeEnabled ? 1f : 0f);
            material.SetFloat("_L0Only", manager.DynamicMeshLightL0Only ? 1f : 0f);
            CustomRenderTexture source = manager.DynamicMeshLightTexture0 as CustomRenderTexture;
            if (source != null) output.updatePeriod = source.updatePeriod;
            EditorUtility.SetDirty(material);
            EditorUtility.SetDirty(output);
            if (!Mathf.Approximately(previousEnabled, material.GetFloat("_DynamicEnabled")) && output.IsCreated()) output.Update();
            return true;
        }

        internal static bool IsBridgeEnabled(LightVolumeInstance volume) {
            return volume != null && volume.isActiveAndEnabled && volume.IsActive && volume.Intensity > 0f && volume.Color != Color.black;
        }

        internal static Vector3 GetWorldSize(LightVolumeManager manager) {
            if (!TryGetBridge(manager, out LightVolumeInstance volume, out _, out _)) return Vector3.zero;
            Matrix4x4 matrix = volume.transform.localToWorldMatrix;
            return new Vector3(matrix.GetColumn(0).magnitude, matrix.GetColumn(1).magnitude, matrix.GetColumn(2).magnitude);
        }

        internal static bool SetWorldCoverage(LightVolumeManager manager, Vector3 center, Vector3 size) {
            if (!TryGetBridge(manager, out LightVolumeInstance volume, out _, out _)) return false;
            Transform transform = volume.transform;
            Transform parent = transform.parent;
            transform.SetParent(null, true);
            transform.position = center;
            transform.rotation = Quaternion.identity;
            transform.localScale = size;
            transform.SetParent(parent, true);
            volume.UpdateTransform();
            EditorUtility.SetDirty(volume);
            LightVolumeManagerEditorBackend.CopyProxyToUdon(volume);
            return true;
        }

        internal static bool ApplyVolumeSettings(LightVolumeManager manager, bool enabled, Color color, float edgeFade) {
            if (!TryGetBridge(manager, out LightVolumeInstance volume, out _, out _)) return false;
            Undo.RecordObjects(new UnityEngine.Object[] { volume, volume.gameObject }, "Change Standard Realtime Light Volume Settings");
            if (enabled && !volume.gameObject.activeSelf) volume.gameObject.SetActive(true);
            manager.DynamicMeshLightEnabled = false;
            volume.Color = color;
            volume.Intensity = enabled ? 1f : 0f;
            volume.IsActive = enabled && volume.isActiveAndEnabled && color != Color.black;
            volume.SetSmoothBlending(edgeFade);
            volume.UpdateTransform();
            EditorUtility.SetDirty(volume);
            LightVolumeManagerEditorBackend.CopyProxyToUdon(volume);
            SyncAtlasMaterial(manager);
            return true;
        }

        internal static bool ApplyScreenShadowField(LightVolumeManager manager, Texture3D texture, Vector3Int resolution, float strength, float contrast) {
            if (!TryGetBridge(manager, out LightVolumeInstance volume, out Material material, out _)) return false;
            Undo.RecordObjects(new UnityEngine.Object[] { volume, material }, "Apply Screen-Origin Shadow Field");
            material.SetTexture("_ScreenVisibility", texture);
            material.SetFloat("_UseScreenVisibility", texture != null ? 1f : 0f);
            if (texture != null) material.SetFloat("_UseScreenBake", 0f);
            material.SetFloat("_ScreenShadowStrength", Mathf.Clamp01(strength));
            material.SetFloat("_ScreenShadowContrast", Mathf.Clamp(contrast, 0.5f, 8f));
            if (texture != null) volume.Resolution = new Vector3Int(
                Mathf.Max(resolution.x, 4),
                Mathf.Max(resolution.y, 4),
                Mathf.Max(resolution.z, 4));
            volume.UpdateTransform();
            EditorUtility.SetDirty(volume);
            EditorUtility.SetDirty(material);
            LightVolumeManagerEditorBackend.CopyProxyToUdon(volume);
            LightVolumeManagerEditorBackend.GenerateAtlas(manager);
            return true;
        }

        internal static bool ApplyLightmapperScreenField(LightVolumeManager manager, Texture3D texture, Vector3Int resolution, float normalization, float strength, float contrast) {
            if (!TryGetBridge(manager, out LightVolumeInstance volume, out Material material, out _)) return false;
            Undo.RecordObjects(new UnityEngine.Object[] { volume, material }, "Apply Lightmapper Screen Shadow Field");
            material.SetTexture("_ScreenBakeL0", texture);
            material.SetFloat("_UseScreenBake", texture != null ? 1f : 0f);
            if (texture != null) material.SetFloat("_UseScreenVisibility", 0f);
            material.SetFloat("_ScreenBakeNormalization", Mathf.Max(normalization, 0.0001f));
            material.SetVector("_ScreenBakeTexelSize", new Vector4(
                1f / Mathf.Max(resolution.x, 1),
                1f / Mathf.Max(resolution.y, 1),
                1f / Mathf.Max(resolution.z, 1),
                0f));
            material.SetFloat("_ScreenShadowStrength", Mathf.Clamp01(strength));
            material.SetFloat("_ScreenShadowContrast", Mathf.Clamp(contrast, 0.5f, 8f));
            if (texture != null) volume.Resolution = new Vector3Int(
                Mathf.Max(resolution.x, 4),
                Mathf.Max(resolution.y, 4),
                Mathf.Max(resolution.z, 4));
            volume.UpdateTransform();
            EditorUtility.SetDirty(volume);
            EditorUtility.SetDirty(material);
            LightVolumeManagerEditorBackend.CopyProxyToUdon(volume);
            LightVolumeManagerEditorBackend.GenerateAtlas(manager);
            return true;
        }

        internal static bool SetScreenShadowSettings(LightVolumeManager manager, bool enabled, float strength, float contrast, float softness, float minimumLight) {
            if (!TryGetBridge(manager, out _, out Material material, out CustomRenderTexture output)) return false;
            Undo.RecordObject(material, "Change Screen-Origin Shadow Settings");
            bool hasLightmapperField = material.GetTexture("_ScreenBakeL0") != null;
            bool hasRayField = material.GetTexture("_ScreenVisibility") != null;
            material.SetFloat("_UseScreenBake", enabled && hasLightmapperField ? 1f : 0f);
            material.SetFloat("_UseScreenVisibility", enabled && !hasLightmapperField && hasRayField ? 1f : 0f);
            material.SetFloat("_ScreenShadowStrength", Mathf.Clamp01(strength));
            material.SetFloat("_ScreenShadowContrast", Mathf.Clamp(contrast, 0.5f, 8f));
            material.SetFloat("_ScreenShadowSoftness", Mathf.Clamp(softness, 0f, 3f));
            material.SetFloat("_ScreenShadowFloor", Mathf.Clamp01(minimumLight));
            EditorUtility.SetDirty(material);
            if (!output.IsCreated()) output.Create();
            output.Update();
            SceneView.RepaintAll();
            return true;
        }

        internal static bool ClearScreenShadowFields(LightVolumeManager manager) {
            if (!TryGetBridge(manager, out _, out Material material, out CustomRenderTexture output)) return false;
            Undo.RecordObject(material, "Clear Screen-Origin Shadow Fields");
            material.SetTexture("_ScreenVisibility", null);
            material.SetTexture("_ScreenBakeL0", null);
            material.SetFloat("_UseScreenVisibility", 0f);
            material.SetFloat("_UseScreenBake", 0f);
            EditorUtility.SetDirty(material);
            if (!output.IsCreated()) output.Create();
            output.Update();
            SceneView.RepaintAll();
            return true;
        }

        private static void ConfigureVolume(LightVolumeInstance volume, LightVolumeManager manager, Vector3 center, Vector3 size, int resolution, float edgeFade) {
            Transform transform = volume.transform;
            transform.position = center;
            transform.rotation = Quaternion.identity;
            if (transform.parent != null) transform.SetParent(null, true);
            transform.localScale = size;
            transform.SetParent(manager.transform, true);
            volume.LightVolumeManager = manager;
            volume.IsDynamic = false;
            volume.IsAdditive = true;
            volume.Color = manager.DynamicMeshLightColor;
            volume.Intensity = 1f;
            volume.Bake = false;
            volume.ReserveUVSpace = true;
            volume.AdaptiveResolution = false;
            bool hasDetailedShadowField = false;
            if (TryGetBridge(manager, out LightVolumeInstance existingVolume, out Material bridgeMaterial, out _) && existingVolume == volume)
                hasDetailedShadowField = bridgeMaterial.GetTexture("_ScreenVisibility") != null || bridgeMaterial.GetTexture("_ScreenBakeL0") != null;
            if (!hasDetailedShadowField) volume.Resolution = Vector3Int.one * Mathf.Max(resolution, 4);
            volume.RegistryWeight = 1000f;
            volume.InvBakedRotation = Quaternion.identity;
            volume.SmoothBlending = Mathf.Max(edgeFade, 0.05f);
            volume.UpdateTransform();
            EditorUtility.SetDirty(volume);
        }

        private static void SetSourceTextures(Material material, LightVolumeManager manager) {
            material.SetTexture("_DynamicTexture0", manager.DynamicMeshLightTexture0);
            material.SetTexture("_DynamicTexture1", manager.DynamicMeshLightTexture1);
            material.SetTexture("_DynamicTexture2", manager.DynamicMeshLightTexture2);
        }

        private static float GetEdgeFade(Vector3 size, Vector3 inverseEdgeSmooth) {
            float x = inverseEdgeSmooth.x > 0.0001f ? size.x / inverseEdgeSmooth.x : 0f;
            float y = inverseEdgeSmooth.y > 0.0001f ? size.y / inverseEdgeSmooth.y : 0f;
            float z = inverseEdgeSmooth.z > 0.0001f ? size.z / inverseEdgeSmooth.z : 0f;
            return Mathf.Max((x + y + z) / 3f, 0.05f);
        }

        private static float GetUpdatesPerSecond(CustomRenderTexture texture) {
            return texture != null && texture.updatePeriod > 0f ? 1f / texture.updatePeriod : 15f;
        }

        private static string GetOutputFolder(Texture texture) {
            string path = AssetDatabase.GetAssetPath(texture);
            if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/")) return Path.GetDirectoryName(path).Replace('\\', '/');
            return DefaultOutputFolder;
        }

        private static string NormalizeAssetFolder(string folder) {
            if (string.IsNullOrWhiteSpace(folder) || !folder.StartsWith("Assets")) return DefaultOutputFolder;
            return folder.TrimEnd('/', '\\').Replace('\\', '/');
        }

        private static void EnsureAssetFolder(string folder) {
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++) {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
