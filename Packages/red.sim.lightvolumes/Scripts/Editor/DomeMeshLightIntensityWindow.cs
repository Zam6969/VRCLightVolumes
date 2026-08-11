using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VRCLightVolumes {
    public sealed class DomeMeshLightIntensityWindow : EditorWindow {
        private const string UpdateShaderName = "Hidden/VRCLV/DomeMeshLightVolumeUpdate";
        private static bool _saveScheduled;

        [SerializeField] private LightVolumeManager _manager;

        [MenuItem("Tools/Light Volumes/Realtime Mesh Light Settings")]
        private static void OpenWindow() {
            DomeMeshLightIntensityWindow window = GetWindow<DomeMeshLightIntensityWindow>();
            window.titleContent = new GUIContent("Mesh Light Settings");
            window.minSize = new Vector2(420f, 300f);
            window.Show();
        }

        private void OnEnable() {
            if (_manager == null) _manager = FindObjectOfType<LightVolumeManager>(true);
        }

        private void OnGUI() {
            EditorGUILayout.LabelField("Realtime Mesh Light", EditorStyles.boldLabel);
            _manager = (LightVolumeManager)EditorGUILayout.ObjectField("Light Volume Manager", _manager, typeof(LightVolumeManager), true);

            if (!TryGetMaterials(out Material[] materials, out CustomRenderTexture[] outputs)) {
                EditorGUILayout.HelpBox("Create or repair a realtime dome mesh light first.", MessageType.Info);
                return;
            }

            Matrix4x4 volumeMatrix = _manager.DynamicMeshLightInvWorldMatrix.inverse;
            Vector3 volumeSize = new Vector3(volumeMatrix.GetColumn(0).magnitude, volumeMatrix.GetColumn(1).magnitude, volumeMatrix.GetColumn(2).magnitude);
            float currentEdgeFade = GetEdgeFade(volumeSize, _manager.DynamicMeshLightInvEdgeSmooth);
            float currentRefreshRate = outputs[0].updatePeriod > 0f ? 1f / outputs[0].updatePeriod : 90f;

            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Lighting", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            bool enabled = EditorGUILayout.Toggle(new GUIContent("Enabled", "Turns the shared realtime mesh-light field on or off."), _manager.DynamicMeshLightEnabled);
            float intensity = EditorGUILayout.Slider(new GUIContent("Intensity", "Changes all three realtime mesh-light textures together."), materials[0].GetFloat("_Intensity"), 0f, 8f);
            Color color = EditorGUILayout.ColorField(new GUIContent("Color Multiplier", "Tints or reduces the final realtime mesh-light contribution."), _manager.DynamicMeshLightColor);
            float panelReach = Mathf.Max(0.1f, EditorGUILayout.FloatField(new GUIContent("Panel Reach", "Maximum distance in meters that each screen section contributes to the lighting field."), materials[0].GetFloat("_ProjectionRange")));
            float edgeFade = EditorGUILayout.Slider(new GUIContent("Edge Fade", "Softens the boundary of the shared 3D lighting field."), currentEdgeFade, 0.05f, Mathf.Max(volumeSize.x * 0.5f, 0.05f));

            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Performance", EditorStyles.boldLabel);
            float refreshRate = EditorGUILayout.Slider(new GUIContent("Light Refresh Rate", "How often the lighting follows the video. The screens continue playing at their normal frame rate."), currentRefreshRate, 5f, 60f);
            EditorGUILayout.LabelField("Grid Resolution", $"{outputs[0].width} x {outputs[0].height} x {outputs[0].volumeDepth}");

            if (EditorGUI.EndChangeCheck()) ApplySettings(materials, outputs, enabled, intensity, color, panelReach, edgeFade, refreshRate, volumeSize);

            GUILayout.Space(12f);
            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Refresh Now")) RefreshOutputs(outputs);
                if (GUILayout.Button("Rebuild / Advanced")) DomeMeshLightVolumeWizard.OpenWindow();
            }
        }

        private void ApplySettings(Material[] materials, CustomRenderTexture[] outputs, bool enabled, float intensity, Color color, float panelReach, float edgeFade, float refreshRate, Vector3 volumeSize) {
            Undo.RecordObject(_manager, "Change Realtime Mesh Light Settings");
            Undo.RecordObjects(materials, "Change Realtime Mesh Light Settings");
            Undo.RecordObjects(outputs, "Change Realtime Mesh Light Settings");

            _manager.DynamicMeshLightEnabled = enabled;
            _manager.DynamicMeshLightColor = color;
            _manager.DynamicMeshLightInvEdgeSmooth = new Vector3(volumeSize.x / edgeFade, volumeSize.y / edgeFade, volumeSize.z / edgeFade);
            EditorUtility.SetDirty(_manager);
            for (int i = 0; i < materials.Length; i++) {
                materials[i].SetFloat("_Intensity", intensity);
                materials[i].SetFloat("_ProjectionRange", panelReach);
                outputs[i].updatePeriod = 1f / refreshRate;
                EditorUtility.SetDirty(materials[i]);
                EditorUtility.SetDirty(outputs[i]);
            }

            LightVolumeManagerEditorBackend.CopyProxyToUdon(_manager);
            _manager._ApplyEditorSettings();
            _manager.UpdateVolumes();
            EditorSceneManager.MarkSceneDirty(_manager.gameObject.scene);
            RefreshOutputs(outputs);
            ScheduleSave();
        }

        private static float GetEdgeFade(Vector3 volumeSize, Vector3 inverseEdgeSmooth) {
            float x = inverseEdgeSmooth.x > 0.0001f ? volumeSize.x / inverseEdgeSmooth.x : 0f;
            float y = inverseEdgeSmooth.y > 0.0001f ? volumeSize.y / inverseEdgeSmooth.y : 0f;
            float z = inverseEdgeSmooth.z > 0.0001f ? volumeSize.z / inverseEdgeSmooth.z : 0f;
            return Mathf.Max((x + y + z) / 3f, 0.05f);
        }

        private static void RefreshOutputs(CustomRenderTexture[] outputs) {
            for (int i = 0; i < outputs.Length; i++) outputs[i].Update();
            SceneView.RepaintAll();
        }

        private static void ScheduleSave() {
            if (_saveScheduled) return;
            _saveScheduled = true;
            EditorApplication.delayCall += SaveAssets;
        }

        private static void SaveAssets() {
            _saveScheduled = false;
            AssetDatabase.SaveAssets();
        }

        private bool TryGetMaterials(out Material[] materials, out CustomRenderTexture[] outputs) {
            materials = null;
            outputs = null;
            if (_manager == null) return false;

            outputs = new[] {
                _manager.DynamicMeshLightTexture0 as CustomRenderTexture,
                _manager.DynamicMeshLightTexture1 as CustomRenderTexture,
                _manager.DynamicMeshLightTexture2 as CustomRenderTexture
            };
            materials = new Material[outputs.Length];
            for (int i = 0; i < outputs.Length; i++) {
                if (outputs[i] == null || outputs[i].material == null || outputs[i].material.shader == null || outputs[i].material.shader.name != UpdateShaderName) return false;
                materials[i] = outputs[i].material;
            }
            return true;
        }
    }
}
