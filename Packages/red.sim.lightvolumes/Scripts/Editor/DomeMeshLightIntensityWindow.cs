using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VRCLightVolumes {
    public sealed class DomeMeshLightIntensityWindow : EditorWindow {
        private const string UpdateShaderName = "Hidden/VRCLV/DomeMeshLightVolumeUpdate";
        private const string CubemapShadowLightName = "Realtime Mesh Light - Cubemap Shadows";

        [SerializeField] private LightVolumeManager _manager;
        [SerializeField] private LayerMask _shadowLayerMask = ~0;
        [SerializeField] private float _shadowBias = 0.05f;
        [SerializeField] private bool _includeRenderMeshes = true;
        [SerializeField] private int _screenShadowHorizontalResolution = 64;
        [SerializeField] private int _screenShadowVerticalResolution = 24;
        [SerializeField] private UnityEngine.Object _bakeryVolume;
        [SerializeField] private Texture3D _bakeryShadowMask;
        [SerializeField] private int _bakeryMaskChannel;
        [SerializeField] private Renderer _bakeryScreenRenderer;
        [SerializeField] private Renderer _cubemapScreenRenderer;
        [SerializeField] private LayerMask _cubemapShadowLayers = ~0;
        [SerializeField] private Vector2 _scrollPosition;

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
            using (EditorGUILayout.ScrollViewScope scrollView = new EditorGUILayout.ScrollViewScope(_scrollPosition)) {
                _scrollPosition = scrollView.scrollPosition;
                DrawContent();
            }
        }

        private void DrawContent() {
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
            bool enabled = EditorGUILayout.Toggle(new GUIContent("Enabled", "Turns the shared realtime mesh-light field on or off."), hasStandardBridge ? DomeMeshLightAtlasBridgeUtility.IsBridgeEnabled(bridgeVolume) : _manager.DynamicMeshLightEnabled);
            float intensity = EditorGUILayout.Slider(new GUIContent("Intensity", "Changes all three realtime mesh-light textures together."), materials[0].GetFloat("_Intensity"), 0f, 8f);
            float colorSaturation = EditorGUILayout.Slider(new GUIContent("Screen Color", "0 produces neutral white light; 1 uses the full screen colors."), materials[0].GetFloat("_ColorSaturation"), 0f, 1f);
            Color color = EditorGUILayout.ColorField(new GUIContent("Color Multiplier", "Tints or reduces the final realtime mesh-light contribution."), _manager.DynamicMeshLightColor);
            float panelReach = Mathf.Max(0.1f, EditorGUILayout.FloatField(new GUIContent("Panel Reach", "Maximum distance in meters that each screen section contributes to the lighting field."), materials[0].GetFloat("_ProjectionRange")));
            float edgeFade = EditorGUILayout.Slider(new GUIContent("Edge Fade", "Softens the boundary of the shared 3D lighting field."), currentEdgeFade, 0.05f, Mathf.Max(volumeSize.x * 0.5f, 0.05f));
            float backfaceFade = EditorGUILayout.Slider(new GUIContent("Back Surface Fade", "Blocks light behind the screen mesh and controls how softly it begins on the viewing side."), materials[0].GetFloat("_BackfaceFade"), 0.01f, 1f);
            float floorLightBoost = EditorGUILayout.Slider(new GUIContent("Floor Light Boost", "Strengthens lighting below the screen panels without raising the whole volume."), materials[0].GetFloat("_FloorLightBoost"), 1f, 4f);
            float panelColorSpread = EditorGUILayout.Slider(new GUIContent("Panel Color Spread", "Spreads triangular panel samples away from labels, seams, and isolated dark pixels."), materials[0].GetFloat("_PanelColorSpread"), 0f, 0.04f);

            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Performance", EditorStyles.boldLabel);
            bool performanceMode = EditorGUILayout.Toggle(new GUIContent("VR Performance Mode", "Uses one realtime color-light texture and stops updating the two directional textures. Recommended for volumetric fog and VR."), _manager.DynamicMeshLightL0Only);
            float refreshRate = EditorGUILayout.Slider(new GUIContent("Light Refresh Rate", "How often the lighting follows the video. The screens continue playing at their normal frame rate."), currentRefreshRate, 5f, 60f);
            EditorGUILayout.LabelField("Grid Resolution", $"{outputs[0].width} x {outputs[0].height} x {outputs[0].volumeDepth}");

            if (EditorGUI.EndChangeCheck()) ApplySettings(materials, outputs, enabled, performanceMode, intensity, colorSaturation, color, panelReach, edgeFade, backfaceFade, floorLightBoost, panelColorSpread, refreshRate, volumeSize);

            GUILayout.Space(8f);
            Texture sourceTexture = outputs[0].material.GetTexture("_SourceTex");
            DrawScreenOriginShadows(materials, outputs, volumeMatrix);

            GUILayout.Space(8f);
            DrawAvatarFillLight(sourceTexture, volumeMatrix, volumeSize);

            GUILayout.Space(12f);
            if (GUILayout.Button("Apply Stable VR Preset", GUILayout.Height(28f))) ApplySettings(materials, outputs, enabled, true, intensity, colorSaturation, color, panelReach, edgeFade, backfaceFade, floorLightBoost, panelColorSpread, 10f, volumeSize);
            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Refresh Now")) RefreshOutputs(outputs);
                if (GUILayout.Button("Rebuild / Advanced")) DomeMeshLightVolumeWizard.OpenWindow();
            }
        }

        private void DrawScreenOriginShadows(Material[] materials, CustomRenderTexture[] outputs, Matrix4x4 volumeMatrix) {
            EditorGUILayout.LabelField("Screen-Origin Shadows", EditorStyles.boldLabel);
            bool hasBridge = DomeMeshLightAtlasBridgeUtility.TryGetBridge(_manager, out _, out Material bridgeMaterial, out _);
            Texture3D lightmapperField = hasBridge ? bridgeMaterial.GetTexture("_ScreenBakeL0") as Texture3D : null;
            Texture3D rayField = hasBridge ? bridgeMaterial.GetTexture("_ScreenVisibility") as Texture3D : null;
            bool usingLightmapperField = lightmapperField != null && bridgeMaterial.GetFloat("_UseScreenBake") > 0.5f;
            Texture3D currentMask = lightmapperField != null ? lightmapperField : rayField;
            bool enabled = usingLightmapperField || rayField != null && bridgeMaterial.GetFloat("_UseScreenVisibility") > 0.5f;
            float strength = hasBridge && bridgeMaterial.HasProperty("_ScreenShadowStrength") ? bridgeMaterial.GetFloat("_ScreenShadowStrength") : 1f;
            float contrast = hasBridge && bridgeMaterial.HasProperty("_ScreenShadowContrast") ? bridgeMaterial.GetFloat("_ScreenShadowContrast") : 2f;
            float softness = hasBridge && bridgeMaterial.HasProperty("_ScreenShadowSoftness") ? bridgeMaterial.GetFloat("_ScreenShadowSoftness") : 1f;
            float minimumLight = hasBridge && bridgeMaterial.HasProperty("_ScreenShadowFloor") ? bridgeMaterial.GetFloat("_ScreenShadowFloor") : 0.2f;

            EditorGUILayout.HelpBox("Bakes static light transport from the screen mesh. The screen colors remain realtime and are multiplied by this baked shadow field.", MessageType.None);
            if (!hasBridge) {
                EditorGUILayout.HelpBox("Publish the mesh light to the standard Light Volume atlas before baking detailed screen shadows.", MessageType.Info);
                if (GUILayout.Button("Publish To Standard Light Volumes", GUILayout.Height(26f))) {
                    DomeMeshLightAtlasBridgeUtility.CreateFromExisting(_manager);
                    GUIUtility.ExitGUI();
                }
                return;
            }

            EditorGUI.BeginChangeCheck();
            bool nextEnabled = EditorGUILayout.Toggle("Enabled", enabled);
            float nextStrength = EditorGUILayout.Slider("Shadow Strength", strength, 0f, 1f);
            float nextContrast = EditorGUILayout.Slider("Shadow Contrast", contrast, 0.5f, 8f);
            float nextSoftness = EditorGUILayout.Slider(new GUIContent("Shadow Softness", "Smooths noisy Bakery voxels without another bake."), softness, 0f, 3f);
            float nextMinimumLight = EditorGUILayout.Slider(new GUIContent("Minimum Light", "Stops dim transport from becoming solid black while retaining occlusion."), minimumLight, 0f, 0.75f);
            if (EditorGUI.EndChangeCheck()) DomeMeshLightAtlasBridgeUtility.SetScreenShadowSettings(_manager, nextEnabled, nextStrength, nextContrast, nextSoftness, nextMinimumLight);

            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField(usingLightmapperField ? "Bakery Mesh Field" : "Current Shadow Field", currentMask, typeof(Texture3D), false);
            _screenShadowHorizontalResolution = EditorGUILayout.IntSlider(new GUIContent("Floor Detail", "Horizontal detail for shadows on the floor and walls."), _screenShadowHorizontalResolution, 32, 96);
            _screenShadowVerticalResolution = EditorGUILayout.IntSlider(new GUIContent("Height Detail", "Vertical detail for raised shadow casters."), _screenShadowVerticalResolution, 8, 48);

            GUILayout.Space(4f);
            EditorGUILayout.LabelField("Bakery Mesh Bake", EditorStyles.miniBoldLabel);
            _bakeryScreenRenderer = (Renderer)EditorGUILayout.ObjectField(new GUIContent("Mesh Screen", "The combined dome renderer whose triangles should emit the light."), _bakeryScreenRenderer, typeof(Renderer), true);
            if (!BakeryEditorBridge.IsAvailable) {
                EditorGUILayout.HelpBox("Bakery is not installed or has not finished compiling.", MessageType.Info);
            } else {
                EditorGUILayout.HelpBox("Runs a screen-only Bakery pass using a temporary emissive copy of this mesh, saves its volume field, restores every light and material, then starts your normal Bakery world bake.", MessageType.None);
                bool baking = BakeryEditorBridge.IsBaking || DomeMeshLightLightmapperShadowBaker.IsInProgress;
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || baking || _bakeryScreenRenderer == null)) {
                    string bakeryLabel = lightmapperField == null ? "Bake Mesh Shadows + Restore World Bake" : "Rebake Mesh Shadows + Restore World Bake";
                    if (GUILayout.Button(bakeryLabel, GUILayout.Height(28f))) {
                        Vector3 center = volumeMatrix.MultiplyPoint3x4(Vector3.zero);
                        Vector3 size = new Vector3(volumeMatrix.GetColumn(0).magnitude, volumeMatrix.GetColumn(1).magnitude, volumeMatrix.GetColumn(2).magnitude);
                        Vector3Int resolution = new Vector3Int(_screenShadowHorizontalResolution, _screenShadowVerticalResolution, _screenShadowHorizontalResolution);
                        float cutoff = Mathf.Max(materials[0].GetFloat("_ProjectionRange"), 0.1f);
                        if (!DomeMeshLightLightmapperShadowBaker.Start(_bakeryScreenRenderer, _manager, center, size, resolution, cutoff, nextStrength, nextContrast, out string error)) {
                            EditorUtility.DisplayDialog("Realtime Mesh Light Shadows", error, "OK");
                        }
                        GUIUtility.ExitGUI();
                    }
                }
                if (baking) EditorGUILayout.HelpBox("Bakery is working. Keep this scene open until the screen pass is copied and the normal world bake starts.", MessageType.Info);
            }

            GUILayout.Space(4f);
            EditorGUILayout.LabelField("Fast Raycast Fallback", EditorStyles.miniBoldLabel);
            _shadowLayerMask = DrawLayerMask(new GUIContent("Shadow Layers", "Meshes on these layers block light from the dome screen sections."), _shadowLayerMask);
            _includeRenderMeshes = EditorGUILayout.Toggle(new GUIContent("Include Render Meshes", "Temporarily includes visible shadow-casting meshes without adding permanent colliders."), _includeRenderMeshes);
            _shadowBias = EditorGUILayout.Slider("Shadow Bias", _shadowBias, 0.005f, 0.2f);

            PointLightVolumeInstance legacyCenterLight = FindCubemapShadowLight();
            if (legacyCenterLight != null && legacyCenterLight.gameObject.activeSelf && legacyCenterLight.IsActive) {
                EditorGUILayout.HelpBox("The old center cubemap light is still enabled. It does not represent the dome screens and should be disabled.", MessageType.Warning);
                if (GUILayout.Button("Disable Old Center Shadow Light")) DisableLegacyCenterShadowLight(legacyCenterLight);
            }

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode)) {
                string buttonLabel = rayField == null ? "Bake Fast Screen Shadows" : "Rebake Fast Screen Shadows";
                if (GUILayout.Button(buttonLabel, GUILayout.Height(28f))) {
                    Vector3Int resolution = new Vector3Int(_screenShadowHorizontalResolution, _screenShadowVerticalResolution, _screenShadowHorizontalResolution);
                    if (DomeMeshLightShadowBaker.BakeScreenOriginField(materials, outputs, volumeMatrix, _shadowLayerMask.value, _shadowBias, _includeRenderMeshes, _manager, resolution, nextStrength, nextContrast)) {
                        DisableLegacyCenterShadowLight(legacyCenterLight);
                        EditorSceneManager.MarkSceneDirty(_manager.gameObject.scene);
                    }
                    GUIUtility.ExitGUI();
                }
            }

            using (new EditorGUI.DisabledScope(currentMask == null)) {
                if (GUILayout.Button("Clear Screen-Origin Shadows")) {
                    DomeMeshLightAtlasBridgeUtility.ClearScreenShadowFields(_manager);
                    GUIUtility.ExitGUI();
                }
            }
        }

        private static void DisableLegacyCenterShadowLight(PointLightVolumeInstance shadowLight) {
            if (shadowLight == null) return;
            Undo.RecordObjects(new UnityEngine.Object[] { shadowLight, shadowLight.gameObject }, "Disable Old Center Shadow Light");
            shadowLight.IsActive = false;
            PointLightVolumeEditorUtility.Sync(shadowLight, false, false);
            shadowLight.gameObject.SetActive(false);
            EditorUtility.SetDirty(shadowLight);
        }

        private void DrawShadowMapping(Material[] materials, CustomRenderTexture[] outputs, Matrix4x4 volumeMatrix, Vector3 volumeSize) {
            EditorGUILayout.LabelField("Shadow Mapping", EditorStyles.boldLabel);
            Texture currentMask = materials[0].GetTexture("_BakedOcclusion");
            bool shadowsEnabled = currentMask != null && materials[0].GetFloat("_UseBakedOcclusion") > 0.5f;
            float shadowStrength = materials[0].GetFloat("_BakedShadowStrength");
            float shadowContrast = materials[0].GetFloat("_BakedShadowContrast");

            EditorGUI.BeginChangeCheck();
            bool nextEnabled = EditorGUILayout.Toggle(new GUIContent("Enabled", "Uses the assigned static visibility volume to shadow the changing screen light."), shadowsEnabled);
            if (EditorGUI.EndChangeCheck()) DomeMeshLightShadowBaker.SetEnabled(materials, outputs, nextEnabled);

            EditorGUI.BeginChangeCheck();
            float nextStrength = EditorGUILayout.Slider(new GUIContent("Shadow Strength", "Blends between unshadowed screen light and the baked visibility mask."), shadowStrength, 0f, 1f);
            if (EditorGUI.EndChangeCheck()) DomeMeshLightShadowBaker.SetStrength(materials, outputs, nextStrength);

            EditorGUI.BeginChangeCheck();
            float nextContrast = EditorGUILayout.Slider(new GUIContent("Shadow Contrast", "Darkens partial shadows that are filled by several other dome panels."), shadowContrast, 0.5f, 8f);
            if (EditorGUI.EndChangeCheck()) DomeMeshLightShadowBaker.SetContrast(materials, outputs, nextContrast);

            using (new EditorGUI.DisabledScope(true)) {
                EditorGUILayout.ObjectField("Current Shadow Mask", currentMask, typeof(Texture3D), false);
            }

            GUILayout.Space(4f);
            EditorGUILayout.LabelField("Bake From Scene Geometry", EditorStyles.miniBoldLabel);
            _shadowLayerMask = DrawLayerMask(new GUIContent("Shadow Layers", "Only colliders on these layers block the dome screen light."), _shadowLayerMask);
            _includeRenderMeshes = EditorGUILayout.Toggle(new GUIContent("Include Render Meshes", "Temporarily includes enabled Mesh Renderers and Skinned Mesh Renderers whose Cast Shadows setting is not Off. No permanent colliders are added."), _includeRenderMeshes);
            _shadowBias = EditorGUILayout.Slider(new GUIContent("Shadow Bias", "Moves each visibility ray away from surfaces to avoid false self-shadowing."), _shadowBias, 0.005f, 0.2f);
            EditorGUILayout.HelpBox("This bake uses enabled scene colliders and, when selected, visible render meshes that cast shadows. It works alongside Bakery and does not modify Bakery lightmaps.", MessageType.None);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode)) {
                if (GUILayout.Button(currentMask == null ? "Bake Screen Shadow Map" : "Rebake Screen Shadow Map", GUILayout.Height(26f))) {
                    DomeMeshLightShadowBaker.BakeFromColliders(materials, outputs, volumeMatrix, _shadowLayerMask.value, _shadowBias, _includeRenderMeshes);
                    GUIUtility.ExitGUI();
                }
            }

            GUILayout.Space(5f);
            EditorGUILayout.LabelField("Bakery Volume Shadow Mask", EditorStyles.miniBoldLabel);
            if (!BakeryEditorBridge.IsAvailable) {
                EditorGUILayout.HelpBox("Bakery is not installed or has not finished compiling.", MessageType.Info);
            } else {
                System.Type bakeryVolumeType = BakeryEditorBridge.BakeryVolumeComponentType;
                _bakeryScreenRenderer = (Renderer)EditorGUILayout.ObjectField(new GUIContent("Dome Screens", "The same combined renderer used to create the realtime mesh light."), _bakeryScreenRenderer, typeof(Renderer), true);
                using (new EditorGUI.DisabledScope(!BakeryEditorBridge.SupportsDomeShadowMask || _bakeryScreenRenderer == null)) {
                    if (GUILayout.Button("Prepare Bakery Screen Shadows")) {
                        Vector3 center = volumeMatrix.MultiplyPoint3x4(Vector3.zero);
                        Vector3Int resolution = new Vector3Int(outputs[0].width, outputs[0].height, outputs[0].volumeDepth);
                        float cutoff = materials[0].GetFloat("_ProjectionRange");
                        if (BakeryEditorBridge.TrySetupDomeShadowMask(_bakeryScreenRenderer, _manager, center, volumeSize, resolution, cutoff, out UnityEngine.Object preparedVolume, out string setupMessage)) {
                            _bakeryVolume = preparedVolume;
                            _bakeryShadowMask = BakeryEditorBridge.TryGetVolumeShadowMask(_bakeryVolume, out Texture3D preparedMask) ? preparedMask : null;
                            EditorUtility.DisplayDialog("Realtime Mesh Light Shadows", setupMessage, "Done");
                        } else {
                            EditorUtility.DisplayDialog("Realtime Mesh Light Shadows", setupMessage, "OK");
                        }
                    }
                }
                EditorGUI.BeginChangeCheck();
                _bakeryVolume = EditorGUILayout.ObjectField(new GUIContent("Bakery Volume", "A baked Bakery Volume covering the same dome area."), _bakeryVolume, bakeryVolumeType, true);
                if (EditorGUI.EndChangeCheck()) {
                    _bakeryShadowMask = BakeryEditorBridge.TryGetVolumeShadowMask(_bakeryVolume, out Texture3D selectedMask) ? selectedMask : null;
                }
                _bakeryShadowMask = (Texture3D)EditorGUILayout.ObjectField(new GUIContent("Bakery Mask Texture", "The bakedMask Texture3D generated by Bakery."), _bakeryShadowMask, typeof(Texture3D), false);
                _bakeryMaskChannel = EditorGUILayout.Popup(new GUIContent("Screen Mask Channel", "The Bakery shadowmask channel assigned to the dome screen light."), Mathf.Clamp(_bakeryMaskChannel, 0, 3), new[] { "Red", "Green", "Blue", "Alpha" });

                using (new EditorGUILayout.HorizontalScope()) {
                    if (GUILayout.Button("Find Matching Volume")) {
                        Vector3 center = volumeMatrix.MultiplyPoint3x4(Vector3.zero);
                        if (BakeryEditorBridge.TryFindClosestVolumeShadowMask(center, volumeSize, out UnityEngine.Object foundVolume, out Texture3D foundMask)) {
                            _bakeryVolume = foundVolume;
                            _bakeryShadowMask = foundMask;
                        } else {
                            EditorUtility.DisplayDialog("Realtime Mesh Light Shadows", "No baked Bakery Volume shadow mask overlapping this realtime mesh-light field was found.", "OK");
                        }
                    }
                    using (new EditorGUI.DisabledScope(_bakeryShadowMask == null)) {
                        if (GUILayout.Button("Use Bakery Mask")) {
                            if (BakeryEditorBridge.TryGetDomeShadowMaskChannel(_bakeryScreenRenderer, out int allocatedChannel)) _bakeryMaskChannel = allocatedChannel;
                            DomeMeshLightShadowBaker.ApplyMask(materials, outputs, _bakeryShadowMask, _bakeryMaskChannel, true);
                        }
                    }
                }
                EditorGUILayout.HelpBox("Bake Bakery in Shadowmask mode with the dome screen's Bakery Light Mesh assigned to the selected channel, then import that Bakery Volume mask here.", MessageType.None);
            }

            using (new EditorGUI.DisabledScope(currentMask == null)) {
                if (GUILayout.Button("Clear Screen Shadow Map")) DomeMeshLightShadowBaker.ApplyMask(materials, outputs, null, 0, false);
            }
        }

        private static LayerMask DrawLayerMask(GUIContent label, LayerMask value) {
            string[] layerNames = new string[32];
            for (int i = 0; i < layerNames.Length; i++) {
                string layerName = LayerMask.LayerToName(i);
                layerNames[i] = string.IsNullOrEmpty(layerName) ? "Layer " + i : layerName;
            }
            return EditorGUILayout.MaskField(label, value.value, layerNames);
        }

        private void DrawCubemapFloorShadows(Texture sourceTexture, Matrix4x4 volumeMatrix, Vector3 volumeSize) {
            EditorGUILayout.LabelField("Cubemap Floor Shadows", EditorStyles.boldLabel);
            PointLightVolumeInstance shadowLight = FindCubemapShadowLight();
            if (_cubemapScreenRenderer == null && _bakeryScreenRenderer != null) _cubemapScreenRenderer = _bakeryScreenRenderer;

            if (shadowLight == null) {
                _cubemapScreenRenderer = (Renderer)EditorGUILayout.ObjectField(new GUIContent("Dome Screens", "The combined dome screen renderer. It is excluded so the screen does not block its own shadow cubemap."), _cubemapScreenRenderer, typeof(Renderer), true);
                _cubemapShadowLayers = DrawLayerMask(new GUIContent("Shadow Layers", "Meshes on these layers cast detailed cubemap shadows onto the floor and other receivers."), _cubemapShadowLayers);
                EditorGUILayout.HelpBox("Creates one screen-colored Point Light Volume and a six-face cubemap shadow. The existing mesh field still supplies the directional panel colors.", MessageType.None);
                using (new EditorGUI.DisabledScope(_cubemapScreenRenderer == null || EditorApplication.isPlayingOrWillChangePlaymode)) {
                    if (GUILayout.Button("Create And Bake Cubemap Shadow Light", GUILayout.Height(28f))) {
                        CreateCubemapShadowLight(sourceTexture, volumeMatrix, volumeSize);
                        GUIUtility.ExitGUI();
                    }
                }
                return;
            }

            DomeAvatarFillLight colorDriver = shadowLight.GetComponent<DomeAvatarFillLight>();
            if (colorDriver == null) {
                EditorGUILayout.HelpBox("The cubemap shadow light is missing its live screen-color driver.", MessageType.Warning);
                if (GUILayout.Button("Repair Live Screen Color")) {
                    colorDriver = AddScreenColorDriver(shadowLight.gameObject);
                    ConfigureScreenColorDriver(colorDriver, shadowLight, sourceTexture);
                    LightVolumeManagerEditorBackend.CopyProxyToUdon(colorDriver);
                }
                return;
            }
            UpgradeCubemapShadowSettings(shadowLight, colorDriver);

            if (_cubemapScreenRenderer == null && shadowLight.ExclusionMask != null && shadowLight.ExclusionMask.Length > 0 && shadowLight.ExclusionMask[0] != null)
                _cubemapScreenRenderer = shadowLight.ExclusionMask[0].GetComponent<Renderer>();

            EditorGUI.BeginChangeCheck();
            bool active = EditorGUILayout.Toggle("Enabled", shadowLight.IsActive);
            float intensity = EditorGUILayout.Slider(new GUIContent("Intensity", "Brightness of the single screen-colored Point Light Volume."), shadowLight.Intensity, 0f, 150f);
            float sourceSize = EditorGUILayout.Slider(new GUIContent("Source Size", "Softens the point-light response without changing the baked cubemap detail."), shadowLight.LightSourceSize, 0.05f, 5f);
            Vector3 origin = EditorGUILayout.Vector3Field(new GUIContent("Cubemap Origin", "The point from which all six shadow faces are baked. Normally this is the dome center."), shadowLight.transform.position);
            _cubemapScreenRenderer = (Renderer)EditorGUILayout.ObjectField(new GUIContent("Dome Screens", "Excluded from the cubemap so the emissive screen surface does not block its own light."), _cubemapScreenRenderer, typeof(Renderer), true);
            _cubemapShadowLayers = DrawLayerMask(new GUIContent("Shadow Layers", "Meshes on these layers cast into the cubemap."), shadowLight.LayerMask);
            float bias = EditorGUILayout.Slider("Shadow Bias", shadowLight.Bias, 0.001f, 0.2f);
            float blur = EditorGUILayout.Slider("Shadow Blur", shadowLight.Blur, 0f, 4f);
            float screenColor = EditorGUILayout.Slider("Screen Color", colorDriver.ScreenColor, 0f, 1f);
            float videoBrightness = EditorGUILayout.Slider("Video Brightness", colorDriver.FollowVideoBrightness, 0f, 1f);
            float screenBoost = EditorGUILayout.Slider("Screen Light Boost", colorDriver.ScreenLightBoost, 0.25f, 4f);
            float refreshRate = EditorGUILayout.Slider("Color Refresh Rate", colorDriver.UpdatesPerSecond, 1f, 30f);
            if (EditorGUI.EndChangeCheck()) {
                Undo.RecordObjects(new UnityEngine.Object[] { shadowLight, colorDriver, shadowLight.transform }, "Change Cubemap Screen Shadows");
                shadowLight.IsActive = active;
                shadowLight.Intensity = intensity;
                shadowLight.LightSourceSize = sourceSize;
                shadowLight.transform.position = origin;
                shadowLight.LayerMask = _cubemapShadowLayers.value;
                shadowLight.Bias = bias;
                shadowLight.Blur = blur;
                shadowLight.ExclusionMask = _cubemapScreenRenderer != null ? new[] { _cubemapScreenRenderer.gameObject } : new GameObject[0];
                colorDriver.TargetRenderTexture = sourceTexture;
                colorDriver.TargetPointLightVolume = shadowLight;
                colorDriver.ScreenColor = screenColor;
                colorDriver.FollowVideoBrightness = videoBrightness;
                colorDriver.ScreenLightBoost = screenBoost;
                colorDriver.UpdatesPerSecond = refreshRate;
                PointLightVolumeEditorUtility.Sync(shadowLight, false);
                LightVolumeManagerEditorBackend.CopyProxyToUdon(colorDriver);
                EditorUtility.SetDirty(colorDriver);
                EditorSceneManager.MarkSceneDirty(_manager.gameObject.scene);
            }

            using (new EditorGUI.DisabledScope(true)) {
                EditorGUILayout.ObjectField("Current Cubemap", shadowLight.ShadowMap, typeof(Cubemap), false);
            }
            EditorGUILayout.HelpBox("The cubemap is static, while its light color follows the video at runtime. Rebake after moving the origin or changing shadow-casting meshes.", MessageType.None);
            using (new EditorGUI.DisabledScope(_cubemapScreenRenderer == null || EditorApplication.isPlayingOrWillChangePlaymode)) {
                if (GUILayout.Button(shadowLight.ShadowMap == null ? "Bake Cubemap Shadows" : "Rebake Cubemap Shadows", GUILayout.Height(26f))) {
                    BakeCubemapShadowLight(shadowLight);
                    GUIUtility.ExitGUI();
                }
            }
        }

        private void CreateCubemapShadowLight(Texture sourceTexture, Matrix4x4 volumeMatrix, Vector3 volumeSize) {
            GameObject gameObject = new GameObject(CubemapShadowLightName);
            Undo.RegisterCreatedObjectUndo(gameObject, "Create Cubemap Screen Shadow Light");
            gameObject.transform.position = volumeMatrix.MultiplyPoint3x4(Vector3.zero);
            gameObject.transform.SetParent(_manager.transform, true);
#if UDONSHARP
            PointLightVolumeInstance shadowLight = UdonSharpEditor.UdonSharpUndo.AddComponent<PointLightVolumeInstance>(gameObject);
#else
            PointLightVolumeInstance shadowLight = Undo.AddComponent<PointLightVolumeInstance>(gameObject);
#endif
            shadowLight.LightVolumeManager = _manager;
            shadowLight.LightType = 0;
            shadowLight.IsDynamic = false;
            shadowLight.Color = Color.white;
            shadowLight.Intensity = 75f;
            shadowLight.ShadingStrength = 1f;
            shadowLight.LightSourceSize = 1f;
            shadowLight.Range = Mathf.Max(volumeSize.x, Mathf.Max(volumeSize.y, volumeSize.z));
            shadowLight.Shadows = true;
            shadowLight.RebakeShadows = true;
            shadowLight.ForceCubemapShadows = true;
            shadowLight.WorldSpaceShadows = true;
            shadowLight.LayerMask = _cubemapShadowLayers.value;
            shadowLight.NearClip = 0.05f;
            shadowLight.FarClip = Mathf.Max(volumeSize.x, Mathf.Max(volumeSize.y, volumeSize.z)) * 1.5f;
            shadowLight.Bias = 0.02f;
            shadowLight.Blur = 0.5f;
            shadowLight.ContactHardening = 0f;
            shadowLight.ExclusionMask = new[] { _cubemapScreenRenderer.gameObject };
            shadowLight.RegistryWeight = 999f;

            DomeAvatarFillLight colorDriver = AddScreenColorDriver(gameObject);
            ConfigureScreenColorDriver(colorDriver, shadowLight, sourceTexture);
            LightVolumeManagerEditorBackend.EnsureRegistered(_manager, shadowLight, "Register Cubemap Screen Shadow Light", out _);
            PointLightVolumeEditorUtility.Sync(shadowLight, false);
            LightVolumeManagerEditorBackend.CopyProxyToUdon(colorDriver);
            EditorUtility.SetDirty(shadowLight);
            EditorUtility.SetDirty(colorDriver);
            BakeCubemapShadowLight(shadowLight);
            EditorSceneManager.MarkSceneDirty(_manager.gameObject.scene);
            Selection.activeGameObject = gameObject;
        }

        private static DomeAvatarFillLight AddScreenColorDriver(GameObject gameObject) {
#if UDONSHARP
            return UdonSharpEditor.UdonSharpUndo.AddComponent<DomeAvatarFillLight>(gameObject);
#else
            return Undo.AddComponent<DomeAvatarFillLight>(gameObject);
#endif
        }

        private static void ConfigureScreenColorDriver(DomeAvatarFillLight colorDriver, PointLightVolumeInstance shadowLight, Texture sourceTexture) {
            colorDriver.TargetRenderTexture = sourceTexture;
            colorDriver.TargetLight = null;
            colorDriver.TargetPointLightVolume = shadowLight;
            colorDriver.UpdatesPerSecond = 12f;
            colorDriver.ResponseSpeed = 18f;
            colorDriver.ScreenColor = 1f;
            colorDriver.FollowVideoBrightness = 0.25f;
            colorDriver.ScreenLightBoost = 1f;
            colorDriver.ColorMultiplier = Color.white;
            colorDriver.AntiFlickering = true;
            colorDriver.FollowClosestScreen = false;
            colorDriver.FacingOnlyLighting = false;
            colorDriver.SettingsVersion = 7;
        }

        private void UpgradeCubemapShadowSettings(PointLightVolumeInstance shadowLight, DomeAvatarFillLight colorDriver) {
            if (colorDriver.SettingsVersion >= 7) return;
            Undo.RecordObjects(new UnityEngine.Object[] { shadowLight, colorDriver }, "Upgrade Cubemap Screen Shadows");
            if (shadowLight.Intensity <= 8f) shadowLight.Intensity = 75f;
            if (shadowLight.Color == Color.black) shadowLight.Color = Color.white;
            if (colorDriver.FollowVideoBrightness >= 0.99f) colorDriver.FollowVideoBrightness = 0.25f;
            if (colorDriver.ScreenLightBoost >= 1.99f) colorDriver.ScreenLightBoost = 1f;
            colorDriver.SettingsVersion = 7;
            PointLightVolumeEditorUtility.Sync(shadowLight, false);
            LightVolumeManagerEditorBackend.CopyProxyToUdon(colorDriver);
            EditorUtility.SetDirty(colorDriver);
            EditorSceneManager.MarkSceneDirty(_manager.gameObject.scene);
        }

        private void BakeCubemapShadowLight(PointLightVolumeInstance shadowLight) {
            shadowLight.Shadows = true;
            shadowLight.ForceCubemapShadows = true;
            shadowLight.WorldSpaceShadows = true;
            shadowLight.LayerMask = _cubemapShadowLayers.value;
            shadowLight.ExclusionMask = _cubemapScreenRenderer != null ? new[] { _cubemapScreenRenderer.gameObject } : new GameObject[0];
            PointLightVolumeEditorUtility.Sync(shadowLight, false, false);
            if (!PointLightShadowBaker.BakeShadowMap(shadowLight, "| realtime dome screen cubemap", true)) return;
            PointLightVolumeEditorUtility.Sync(shadowLight, false, false);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(_manager.gameObject.scene);
            EditorGUIUtility.PingObject(shadowLight.ShadowMap);
        }

        private PointLightVolumeInstance FindCubemapShadowLight() {
            PointLightVolumeInstance[] lights = _manager.GetComponentsInChildren<PointLightVolumeInstance>(true);
            for (int i = 0; i < lights.Length; i++) {
                if (lights[i] != null && lights[i].gameObject.name == CubemapShadowLightName) return lights[i];
            }
            return null;
        }

        private void DrawAvatarFillLight(Texture sourceTexture, Matrix4x4 volumeMatrix, Vector3 volumeSize) {
            EditorGUILayout.LabelField("Avatar Lighting", EditorStyles.boldLabel);
            DomeAvatarFillLight avatarFill = FindAvatarFillLight();
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

        private DomeAvatarFillLight FindAvatarFillLight() {
            DomeAvatarFillLight[] fills = _manager.GetComponentsInChildren<DomeAvatarFillLight>(true);
            for (int i = 0; i < fills.Length; i++) {
                DomeAvatarFillLight fill = fills[i];
                if (fill != null && (fill.TargetLight != null || fill.GetComponent<Light>() != null)) return fill;
            }
            return null;
        }

        private void ApplySettings(Material[] materials, CustomRenderTexture[] outputs, bool enabled, bool performanceMode, float intensity, float colorSaturation, Color color, float panelReach, float edgeFade, float backfaceFade, float floorLightBoost, float panelColorSpread, float refreshRate, Vector3 volumeSize) {
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
                materials[i].SetFloat("_BackfaceFade", backfaceFade);
                materials[i].SetFloat("_FloorLightBoost", floorLightBoost);
                materials[i].SetFloat("_PanelColorSpread", panelColorSpread);
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
