using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VRCLightVolumes {
    public sealed class DomeMeshLightIntensityWindow : EditorWindow {
        private const string UpdateShaderName = "Hidden/VRCLV/DomeMeshLightVolumeUpdate";

        [SerializeField] private LightVolumeManager _manager;

        [MenuItem("Tools/Light Volumes/Realtime Mesh Light Settings")]
        private static void OpenWindow() {
            DomeMeshLightIntensityWindow window = GetWindow<DomeMeshLightIntensityWindow>();
            window.titleContent = new GUIContent("Mesh Light Settings");
            window.minSize = new Vector2(420f, 520f);
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
            bool hasStandardBridge = DomeMeshLightAtlasBridgeUtility.TryGetBridge(_manager, out LightVolumeInstance bridgeVolume, out _, out _);

            if (!hasStandardBridge) {
                EditorGUILayout.HelpBox("Publish this field through the standard Light Volume atlas so stock compatible avatar shaders can receive it.", MessageType.Info);
                if (GUILayout.Button("Publish To Standard Light Volumes", GUILayout.Height(28f))) {
                    DomeMeshLightAtlasBridgeUtility.CreateFromExisting(_manager);
                    GUIUtility.ExitGUI();
                }
            } else {
                EditorGUILayout.HelpBox("This live field is published as one standard additive Light Volume. No avatar shader patch is required.", MessageType.None);
            }

            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Lighting", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            bool enabled = EditorGUILayout.Toggle(new GUIContent("Enabled", "Turns the shared realtime mesh-light field on or off."), hasStandardBridge ? bridgeVolume.Intensity > 0f : _manager.DynamicMeshLightEnabled);
            float intensity = EditorGUILayout.Slider(new GUIContent("Intensity", "Changes all three realtime mesh-light textures together."), materials[0].GetFloat("_Intensity"), 0f, 8f);
            float colorSaturation = EditorGUILayout.Slider(new GUIContent("Screen Color", "0 produces neutral white light; 1 uses the full screen colors."), materials[0].GetFloat("_ColorSaturation"), 0f, 1f);
            Color color = EditorGUILayout.ColorField(new GUIContent("Color Multiplier", "Tints or reduces the final realtime mesh-light contribution."), _manager.DynamicMeshLightColor);
            float panelReach = Mathf.Max(0.1f, EditorGUILayout.FloatField(new GUIContent("Panel Reach", "Maximum distance in meters that each screen section contributes to the lighting field."), materials[0].GetFloat("_ProjectionRange")));
            float edgeFade = EditorGUILayout.Slider(new GUIContent("Edge Fade", "Softens the boundary of the shared 3D lighting field."), currentEdgeFade, 0.05f, Mathf.Max(volumeSize.x * 0.5f, 0.05f));

            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Performance", EditorStyles.boldLabel);
            bool performanceMode = EditorGUILayout.Toggle(new GUIContent("VR Performance Mode", "Uses one realtime color-light texture and stops updating the two directional textures. Recommended for volumetric fog and VR."), _manager.DynamicMeshLightL0Only);
            float refreshRate = EditorGUILayout.Slider(new GUIContent("Light Refresh Rate", "How often the lighting follows the video. The screens continue playing at their normal frame rate."), currentRefreshRate, 5f, 60f);
            EditorGUILayout.LabelField("Grid Resolution", $"{outputs[0].width} x {outputs[0].height} x {outputs[0].volumeDepth}");

            if (EditorGUI.EndChangeCheck()) ApplySettings(materials, outputs, enabled, performanceMode, intensity, colorSaturation, color, panelReach, edgeFade, refreshRate, volumeSize);

            GUILayout.Space(8f);
            DrawAvatarFillLight(outputs[0].material.GetTexture("_SourceTex"), volumeMatrix, volumeSize);

            GUILayout.Space(12f);
            if (GUILayout.Button("Apply Stable VR Preset", GUILayout.Height(28f))) ApplySettings(materials, outputs, enabled, true, intensity, colorSaturation, color, panelReach, edgeFade, 10f, volumeSize);
            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Refresh Now")) RefreshOutputs(outputs);
                if (GUILayout.Button("Rebuild / Advanced")) DomeMeshLightVolumeWizard.OpenWindow();
            }
        }

        private void DrawAvatarFillLight(Texture sourceTexture, Matrix4x4 volumeMatrix, Vector3 volumeSize) {
            EditorGUILayout.LabelField("Avatar Lighting", EditorStyles.boldLabel);
            DomeAvatarFillLight avatarFill = _manager.GetComponentInChildren<DomeAvatarFillLight>(true);
            if (avatarFill == null) {
                EditorGUILayout.HelpBox("Avatar shaders compiled without this mesh-light extension need one avatar-only fallback light.", MessageType.Info);
                if (GUILayout.Button("Create Avatar Fill Light", GUILayout.Height(26f))) CreateAvatarFillLight(sourceTexture, volumeMatrix, volumeSize);
                return;
            }

            Light targetLight = avatarFill.TargetLight != null ? avatarFill.TargetLight : avatarFill.GetComponent<Light>();
            if (targetLight == null) {
                EditorGUILayout.HelpBox("The avatar fill component is missing its Unity Light.", MessageType.Warning);
                return;
            }
            UpgradeAvatarFillSettings(avatarFill, targetLight, volumeMatrix, volumeSize);

            EditorGUI.BeginChangeCheck();
            bool lightEnabled = EditorGUILayout.Toggle("Enabled", targetLight.enabled);
            float lightIntensity = EditorGUILayout.Slider("Intensity", targetLight.intensity, 0f, 8f);
            float lightRange = Mathf.Max(0.1f, EditorGUILayout.FloatField("Range", targetLight.range));
            float screenColor = EditorGUILayout.Slider(new GUIContent("Screen Color", "0 produces neutral light; 1 follows the video's full color."), avatarFill.ScreenColor, 0f, 1f);
            float videoBrightness = EditorGUILayout.Slider(new GUIContent("Video Brightness", "Controls how strongly dark video frames dim the avatar fill."), avatarFill.FollowVideoBrightness, 0f, 1f);
            float screenLightBoost = EditorGUILayout.Slider(new GUIContent("Screen Light Boost", "Amplifies the sampled screen color on avatars while black frames still turn the light off."), avatarFill.ScreenLightBoost, 0.25f, 4f);
            float updateRate = EditorGUILayout.Slider(new GUIContent("Color Refresh Rate", "How often the video color is sampled for avatar lighting."), avatarFill.UpdatesPerSecond, 1f, 30f);
            Color fillMultiplier = EditorGUILayout.ColorField("Color Multiplier", avatarFill.ColorMultiplier);
            bool antiFlickering = EditorGUILayout.Toggle("Smooth Color Changes", avatarFill.AntiFlickering);
            float responseSpeed = avatarFill.ResponseSpeed;
            using (new EditorGUI.DisabledScope(!antiFlickering)) {
                responseSpeed = EditorGUILayout.Slider(new GUIContent("Color Response", "How quickly smoothed avatar lighting catches up to the sampled video color."), responseSpeed, 1f, 30f);
            }
            bool followClosestScreen = EditorGUILayout.Toggle(new GUIContent("Light From Screen", "Places the avatar light along the direction of the nearest dome screen."), avatarFill.FollowClosestScreen);
            float screenRadius = avatarFill.ScreenRadius;
            float screenInset = avatarFill.ScreenInset;
            float lightDistanceFromAvatar = avatarFill.LightDistanceFromAvatar;
            using (new EditorGUI.DisabledScope(!followClosestScreen)) {
                screenRadius = Mathf.Max(0.1f, EditorGUILayout.FloatField(new GUIContent("Screen Radius", "Distance from the dome center to its screen surface."), screenRadius));
                screenInset = EditorGUILayout.Slider(new GUIContent("Source Inset", "Moves the light slightly inward from the screen surface."), screenInset, 0f, 2f);
                lightDistanceFromAvatar = EditorGUILayout.Slider(new GUIContent("Distance From Avatar", "Keeps the light close enough to be visible while preserving the direction from the nearest screen."), lightDistanceFromAvatar, 0.25f, 5f);
            }
            bool facingOnlyLighting = EditorGUILayout.Toggle(new GUIContent("Screen-Facing Shading", "Uses the direction from the nearest screen so only avatar surfaces facing that screen receive its light contribution."), avatarFill.FacingOnlyLighting);
            if (!EditorGUI.EndChangeCheck()) return;

            Undo.RecordObjects(new Object[] { avatarFill, targetLight }, "Change Avatar Fill Light Settings");
            targetLight.enabled = lightEnabled;
            targetLight.intensity = lightIntensity;
            targetLight.range = lightRange;
            avatarFill.TargetRenderTexture = sourceTexture;
            avatarFill.TargetLight = targetLight;
            avatarFill.ScreenColor = screenColor;
            avatarFill.FollowVideoBrightness = videoBrightness;
            avatarFill.ScreenLightBoost = screenLightBoost;
            avatarFill.UpdatesPerSecond = updateRate;
            avatarFill.ResponseSpeed = responseSpeed;
            avatarFill.ColorMultiplier = fillMultiplier;
            avatarFill.AntiFlickering = antiFlickering;
            avatarFill.FollowClosestScreen = followClosestScreen;
            avatarFill.ScreenCenter = volumeMatrix.MultiplyPoint3x4(Vector3.zero);
            avatarFill.ScreenRadius = screenRadius;
            avatarFill.ScreenInset = screenInset;
            avatarFill.LightDistanceFromAvatar = lightDistanceFromAvatar;
            avatarFill.FacingOnlyLighting = facingOnlyLighting;
            targetLight.type = facingOnlyLighting ? LightType.Directional : LightType.Point;
            avatarFill.SettingsVersion = 6;
            EditorUtility.SetDirty(targetLight);
            EditorUtility.SetDirty(avatarFill);
            EditorSceneManager.MarkSceneDirty(_manager.gameObject.scene);
        }

        private void CreateAvatarFillLight(Texture sourceTexture, Matrix4x4 volumeMatrix, Vector3 volumeSize) {
            GameObject gameObject = new GameObject("Realtime Mesh Light - Avatar Fill");
            Undo.RegisterCreatedObjectUndo(gameObject, "Create Avatar Fill Light");
            gameObject.transform.position = volumeMatrix.GetColumn(3);
            gameObject.transform.SetParent(_manager.transform, true);

            Light targetLight = Undo.AddComponent<Light>(gameObject);
            targetLight.type = LightType.Directional;
            targetLight.lightmapBakeType = LightmapBakeType.Realtime;
            targetLight.shadows = LightShadows.None;
            targetLight.renderMode = LightRenderMode.ForcePixel;
            targetLight.intensity = 1.25f;
            targetLight.range = Mathf.Max(volumeSize.x, Mathf.Max(volumeSize.y, volumeSize.z)) * 0.75f;
            targetLight.color = Color.white;
            targetLight.bounceIntensity = 0f;
            int avatarLayers = LayerMask.GetMask("Player", "PlayerLocal", "MirrorReflection");
            targetLight.cullingMask = avatarLayers != 0 ? avatarLayers : (1 << 9) | (1 << 10);

            DomeAvatarFillLight avatarFill = Undo.AddComponent<DomeAvatarFillLight>(gameObject);
            avatarFill.TargetRenderTexture = sourceTexture;
            avatarFill.TargetLight = targetLight;
            avatarFill.UpdatesPerSecond = 12f;
            avatarFill.ResponseSpeed = 18f;
            avatarFill.ScreenColor = 0.65f;
            avatarFill.FollowVideoBrightness = 0.25f;
            avatarFill.ScreenLightBoost = 2f;
            avatarFill.ColorMultiplier = Color.white;
            avatarFill.AntiFlickering = true;
            avatarFill.FollowClosestScreen = true;
            avatarFill.ScreenCenter = volumeMatrix.MultiplyPoint3x4(Vector3.zero);
            avatarFill.ScreenRadius = Mathf.Max(volumeSize.x, Mathf.Max(volumeSize.y, volumeSize.z)) * 0.5f;
            avatarFill.ScreenInset = 0.15f;
            avatarFill.LightDistanceFromAvatar = 2f;
            avatarFill.FacingOnlyLighting = true;
            avatarFill.SettingsVersion = 6;

            EditorUtility.SetDirty(targetLight);
            EditorUtility.SetDirty(avatarFill);
            EditorSceneManager.MarkSceneDirty(_manager.gameObject.scene);
            Selection.activeGameObject = gameObject;
            EditorGUIUtility.PingObject(gameObject);
        }

        private void UpgradeAvatarFillSettings(DomeAvatarFillLight avatarFill, Light targetLight, Matrix4x4 volumeMatrix, Vector3 volumeSize) {
            if (avatarFill.SettingsVersion >= 6) return;
            Undo.RecordObjects(new Object[] { avatarFill, targetLight }, "Upgrade Avatar Fill Light Settings");
            if (avatarFill.SettingsVersion < 1) {
                if (avatarFill.UpdatesPerSecond <= 5f) avatarFill.UpdatesPerSecond = 12f;
                avatarFill.ResponseSpeed = 18f;
            }
            if (avatarFill.SettingsVersion < 2) {
                avatarFill.FollowClosestScreen = true;
                avatarFill.ScreenCenter = volumeMatrix.MultiplyPoint3x4(Vector3.zero);
                avatarFill.ScreenRadius = Mathf.Max(volumeSize.x, Mathf.Max(volumeSize.y, volumeSize.z)) * 0.5f;
                avatarFill.ScreenInset = 0.15f;
            }
            if (avatarFill.SettingsVersion < 3) avatarFill.LightDistanceFromAvatar = 2f;
            if (avatarFill.SettingsVersion < 4) avatarFill.ScreenLightBoost = 2f;
            if (avatarFill.SettingsVersion < 5) avatarFill.FacingOnlyLighting = true;
            targetLight.type = avatarFill.FacingOnlyLighting ? LightType.Directional : LightType.Point;
            if (avatarFill.FacingOnlyLighting && targetLight.intensity > 2f) targetLight.intensity = 2f;
            avatarFill.SettingsVersion = 6;
            EditorUtility.SetDirty(targetLight);
            EditorUtility.SetDirty(avatarFill);
            EditorSceneManager.MarkSceneDirty(_manager.gameObject.scene);
        }

        private void ApplySettings(Material[] materials, CustomRenderTexture[] outputs, bool enabled, bool performanceMode, float intensity, float colorSaturation, Color color, float panelReach, float edgeFade, float refreshRate, Vector3 volumeSize) {
            Undo.RecordObject(_manager, "Change Realtime Mesh Light Settings");
            Undo.RecordObjects(materials, "Change Realtime Mesh Light Settings");
            Undo.RecordObjects(outputs, "Change Realtime Mesh Light Settings");

            _manager.DynamicMeshLightL0Only = performanceMode;
            _manager.DynamicMeshLightOptimizationVersion = DomeMeshLightVolumeWizard.CurrentOptimizationVersion;
            _manager.DynamicMeshLightColor = color;
            _manager.DynamicMeshLightInvEdgeSmooth = new Vector3(volumeSize.x / edgeFade, volumeSize.y / edgeFade, volumeSize.z / edgeFade);
            bool usesStandardBridge = DomeMeshLightAtlasBridgeUtility.ApplyVolumeSettings(_manager, enabled, color, edgeFade);
            _manager.DynamicMeshLightEnabled = usesStandardBridge ? false : enabled;
            EditorUtility.SetDirty(_manager);
            for (int i = 0; i < materials.Length; i++) {
                materials[i].SetFloat("_Intensity", intensity);
                materials[i].SetFloat("_ColorSaturation", colorSaturation);
                materials[i].SetFloat("_ProjectionRange", panelReach);
                outputs[i].updatePeriod = 1f / refreshRate;
                outputs[i].updateMode = performanceMode && i > 0 ? CustomRenderTextureUpdateMode.OnDemand : CustomRenderTextureUpdateMode.Realtime;
                EditorUtility.SetDirty(materials[i]);
                EditorUtility.SetDirty(outputs[i]);
            }

            LightVolumeManagerEditorBackend.CopyProxyToUdon(_manager);
            _manager._ApplyEditorSettings();
            _manager.UpdateVolumes();
            EditorSceneManager.MarkSceneDirty(_manager.gameObject.scene);
            RefreshOutputs(outputs);
        }

        private static float GetEdgeFade(Vector3 volumeSize, Vector3 inverseEdgeSmooth) {
            float x = inverseEdgeSmooth.x > 0.0001f ? volumeSize.x / inverseEdgeSmooth.x : 0f;
            float y = inverseEdgeSmooth.y > 0.0001f ? volumeSize.y / inverseEdgeSmooth.y : 0f;
            float z = inverseEdgeSmooth.z > 0.0001f ? volumeSize.z / inverseEdgeSmooth.z : 0f;
            return Mathf.Max((x + y + z) / 3f, 0.05f);
        }

        private static void RefreshOutputs(CustomRenderTexture[] outputs) {
            for (int i = 0; i < outputs.Length; i++) {
                if (!outputs[i].IsCreated()) {
                    outputs[i].Create();
                    outputs[i].Initialize();
                }
                outputs[i].Update();
            }
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
