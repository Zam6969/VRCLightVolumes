using UnityEditor;
using UnityEngine;

namespace VRCLightVolumes {
    public sealed class DomeMeshLightIntensityWindow : EditorWindow {
        private const string UpdateShaderName = "Hidden/VRCLV/DomeMeshLightVolumeUpdate";

        [SerializeField] private LightVolumeManager _manager;

        [MenuItem("Tools/Light Volumes/Realtime Mesh Light Intensity")]
        private static void OpenWindow() {
            DomeMeshLightIntensityWindow window = GetWindow<DomeMeshLightIntensityWindow>();
            window.titleContent = new GUIContent("Mesh Light Intensity");
            window.minSize = new Vector2(380f, 145f);
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

            float currentIntensity = materials[0].GetFloat("_Intensity");
            EditorGUI.BeginChangeCheck();
            float intensity = EditorGUILayout.Slider(new GUIContent("Intensity", "Changes all three realtime mesh-light textures together."), currentIntensity, 0f, 100f);
            if (!EditorGUI.EndChangeCheck()) return;

            Undo.RecordObjects(materials, "Change Realtime Mesh Light Intensity");
            for (int i = 0; i < materials.Length; i++) {
                materials[i].SetFloat("_Intensity", intensity);
                EditorUtility.SetDirty(materials[i]);
                outputs[i].Update();
            }
            AssetDatabase.SaveAssets();
            SceneView.RepaintAll();
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
