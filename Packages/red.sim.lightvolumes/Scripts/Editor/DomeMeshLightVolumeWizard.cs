using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace VRCLightVolumes {
    public sealed class DomeMeshLightVolumeWizard : EditorWindow {
        private const int MaxEmitterCount = 128;
        private const int CoverageOptimizationVersion = 2;
        private const int BackfaceOptimizationVersion = 3;
        private const int FloorBoostOptimizationVersion = 4;
        internal const int CurrentOptimizationVersion = 5;
        private const float DefaultReceiverPadding = 2f;
        private const float DefaultBackfaceFade = 0.25f;
        private const float DefaultFloorLightBoost = 2f;
        private const float DefaultPanelColorSpread = 0.02f;
        private const string UpdateShaderName = "Hidden/VRCLV/DomeMeshLightVolumeUpdate";
        private const string DefaultOutputFolder = "Assets/LightVolumesDome";
        private static readonly string[] CenterModeNames = { "Fit Dome Sphere", "Renderer Bounds", "Transform Override" };

        [SerializeField] private Renderer _domeRenderer;
        [SerializeField] private Texture _liveAtlas;
        [SerializeField] private LightVolumeManager _manager;
        [SerializeField] private Transform _centerOverride;
        [SerializeField] private int _centerMode;
        [SerializeField] private Vector3 _centerOffset;
        [SerializeField] private int _volumeResolution = 32;
        [SerializeField] private float _volumeScale = 1f;
        [SerializeField] private float _receiverPadding = DefaultReceiverPadding;
        [SerializeField] private float _backfaceFade = DefaultBackfaceFade;
        [SerializeField] private float _floorLightBoost = DefaultFloorLightBoost;
        [SerializeField] private float _panelColorSpread = DefaultPanelColorSpread;
        [SerializeField] private float _projectionRangeScale = 2f;
        [SerializeField] private float _intensity = 5f;
        [SerializeField] private float _colorSaturation = 1f;
        [SerializeField] private float _edgeFade = 0.5f;
        [SerializeField] private float _updatesPerSecond = 15f;
        [SerializeField] private bool _performanceMode = true;
        [SerializeField] private bool _disableExistingLights;
        [SerializeField] private string _outputFolder = DefaultOutputFolder;

        private struct Emitter {
            public Vector3 Position;
            public Vector3 Normal;
            public float Area;
            public Vector2 Uv0;
            public Vector2 Uv1;
            public Vector2 Uv2;
        }

        private struct UvSample {
            public float Area;
            public Vector2 Uv;
        }

        private struct VertexKey : IEquatable<VertexKey> {
            private readonly int _x;
            private readonly int _y;
            private readonly int _z;
            private readonly int _u;
            private readonly int _v;

            public VertexKey(Vector3 position, Vector2 uv) {
                const float positionScale = 100000f;
                const float uvScale = 1000000f;
                _x = Mathf.RoundToInt(position.x * positionScale);
                _y = Mathf.RoundToInt(position.y * positionScale);
                _z = Mathf.RoundToInt(position.z * positionScale);
                _u = Mathf.RoundToInt(uv.x * uvScale);
                _v = Mathf.RoundToInt(uv.y * uvScale);
            }

            public bool Equals(VertexKey other) {
                return _x == other._x && _y == other._y && _z == other._z && _u == other._u && _v == other._v;
            }

            public override bool Equals(object obj) {
                return obj is VertexKey other && Equals(other);
            }

            public override int GetHashCode() {
                unchecked {
                    int hash = _x;
                    hash = hash * 397 ^ _y;
                    hash = hash * 397 ^ _z;
                    hash = hash * 397 ^ _u;
                    return hash * 397 ^ _v;
                }
            }
        }

        private sealed class EmitterAccumulator {
            public float Area;
            public Vector3 WeightedPosition;
            public Vector3 WeightedNormal;
            public readonly UvSample[] Samples = new UvSample[3];

            public void AddSample(float area, Vector2 uv) {
                if (area <= Samples[2].Area) return;
                if (area > Samples[0].Area) {
                    Samples[2] = Samples[1];
                    Samples[1] = Samples[0];
                    Samples[0] = new UvSample { Area = area, Uv = uv };
                } else if (area > Samples[1].Area) {
                    Samples[2] = Samples[1];
                    Samples[1] = new UvSample { Area = area, Uv = uv };
                } else {
                    Samples[2] = new UvSample { Area = area, Uv = uv };
                }
            }
        }

        private sealed class UnionFind {
            private readonly int[] _parent;

            public UnionFind(int count) {
                _parent = new int[count];
                for (int i = 0; i < count; i++) _parent[i] = i;
            }

            public int Find(int value) {
                int root = value;
                while (_parent[root] != root) root = _parent[root];
                while (_parent[value] != value) {
                    int next = _parent[value];
                    _parent[value] = root;
                    value = next;
                }
                return root;
            }

            public void Union(int a, int b) {
                int rootA = Find(a);
                int rootB = Find(b);
                if (rootA != rootB) _parent[rootB] = rootA;
            }
        }

        [MenuItem("Tools/Light Volumes/Create Realtime Dome Mesh Light")]
        public static void OpenWindow() {
            DomeMeshLightVolumeWizard window = GetWindow<DomeMeshLightVolumeWizard>();
            window.titleContent = new GUIContent("Dome Mesh Light");
            window.minSize = new Vector2(430f, 540f);
            window.Show();
        }

        [InitializeOnLoadMethod]
        private static void ScheduleGeneratedMaterialRepair() {
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorApplication.delayCall += RepairGeneratedMaterialsDelayed;
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode) {
            EditorApplication.delayCall += RepairGeneratedMaterialsDelayed;
        }

        private static void RepairGeneratedMaterialsDelayed() {
            RepairGeneratedMaterials();
        }

        [MenuItem("Tools/Light Volumes/Repair Realtime Dome Mesh Light")]
        private static void RepairGeneratedMaterialsMenu() {
            if (RepairGeneratedMaterials()) EditorUtility.DisplayDialog("Realtime Dome Mesh Light", "The existing realtime dome lighting textures were repaired and refreshed.", "Done");
            else EditorUtility.DisplayDialog("Realtime Dome Mesh Light", "No realtime dome materials needed repair in the open scene.", "OK");
        }

        // Repairs assets made before the internal volume parameters became serialized shader properties.
        private static bool RepairGeneratedMaterials() {
            LightVolumeManager[] managers = Resources.FindObjectsOfTypeAll<LightVolumeManager>();
            bool repairedAny = false;
            for (int managerIndex = 0; managerIndex < managers.Length; managerIndex++) {
                LightVolumeManager manager = managers[managerIndex];
                if (manager == null || !manager.gameObject.scene.IsValid()) continue;
                CustomRenderTexture[] outputs = {
                    manager.DynamicMeshLightTexture0 as CustomRenderTexture,
                    manager.DynamicMeshLightTexture1 as CustomRenderTexture,
                    manager.DynamicMeshLightTexture2 as CustomRenderTexture
                };
                if (outputs[0] == null || outputs[1] == null || outputs[2] == null) continue;

                bool migrateToStableVr = manager.DynamicMeshLightOptimizationVersion < 1;
                if (migrateToStableVr) {
                    manager.DynamicMeshLightL0Only = true;
                    manager.DynamicMeshLightOptimizationVersion = 1;
                    EditorUtility.SetDirty(manager);
                    LightVolumeManagerEditorBackend.CopyProxyToUdon(manager);
                    EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
                    repairedAny = true;
                }

                Matrix4x4 volumeMatrix = manager.DynamicMeshLightInvWorldMatrix.inverse;
                Vector3 center = volumeMatrix.MultiplyPoint3x4(Vector3.zero);
                Vector3 size = new Vector3(volumeMatrix.GetColumn(0).magnitude, volumeMatrix.GetColumn(1).magnitude, volumeMatrix.GetColumn(2).magnitude);
                float currentEdgeFade = GetEdgeFade(size, manager.DynamicMeshLightInvEdgeSmooth);
                bool migrateCoverage = manager.DynamicMeshLightOptimizationVersion < CoverageOptimizationVersion;
                bool migrateBackfaceBlocking = manager.DynamicMeshLightOptimizationVersion < BackfaceOptimizationVersion;
                bool migrateFloorBoost = manager.DynamicMeshLightOptimizationVersion < FloorBoostOptimizationVersion;
                bool migratePanelSampling = manager.DynamicMeshLightOptimizationVersion < CurrentOptimizationVersion;
                if (migrateCoverage) {
                    Vector3 bridgeSize = DomeMeshLightAtlasBridgeUtility.GetWorldSize(manager);
                    size = Vector3.Max(size, bridgeSize);
                    if (TryCalculateExpandedCoverage(outputs[0].material, center, size, DefaultReceiverPadding, out Vector3 expandedSize)) {
                        for (int outputIndex = 0; outputIndex < outputs.Length; outputIndex++) {
                            Material outputMaterial = outputs[outputIndex].material;
                            if (outputMaterial == null || outputMaterial.shader == null || outputMaterial.shader.name != UpdateShaderName) continue;
                            outputMaterial.SetVector("_VolumeCenter", center);
                            outputMaterial.SetVector("_VolumeSize", expandedSize);
                            EditorUtility.SetDirty(outputMaterial);
                            outputs[outputIndex].Update();
                        }
                        manager.DynamicMeshLightInvWorldMatrix = Matrix4x4.TRS(center, Quaternion.identity, expandedSize).inverse;
                        manager.DynamicMeshLightInvEdgeSmooth = new Vector3(expandedSize.x / currentEdgeFade, expandedSize.y / currentEdgeFade, expandedSize.z / currentEdgeFade);
                        DomeMeshLightAtlasBridgeUtility.SetWorldCoverage(manager, center, expandedSize);
                        LightVolumeManagerEditorBackend.GenerateAtlas(manager);
                        DomeMeshLightAtlasBridgeUtility.SyncAtlasMaterial(manager);
                        size = expandedSize;
                        repairedAny = true;
                    }
                }
                if (migrateBackfaceBlocking) {
                    for (int outputIndex = 0; outputIndex < outputs.Length; outputIndex++) {
                        Material outputMaterial = outputs[outputIndex].material;
                        if (outputMaterial == null || outputMaterial.shader == null || outputMaterial.shader.name != UpdateShaderName) continue;
                        outputMaterial.SetFloat("_BackfaceFade", DefaultBackfaceFade);
                        EditorUtility.SetDirty(outputMaterial);
                        outputs[outputIndex].Update();
                    }
                    repairedAny = true;
                }
                if (migrateFloorBoost) {
                    for (int outputIndex = 0; outputIndex < outputs.Length; outputIndex++) {
                        Material outputMaterial = outputs[outputIndex].material;
                        if (outputMaterial == null || outputMaterial.shader == null || outputMaterial.shader.name != UpdateShaderName) continue;
                        outputMaterial.SetFloat("_FloorLightBoost", DefaultFloorLightBoost);
                        EditorUtility.SetDirty(outputMaterial);
                        outputs[outputIndex].Update();
                    }
                    repairedAny = true;
                }
                if (migratePanelSampling) {
                    for (int outputIndex = 0; outputIndex < outputs.Length; outputIndex++) {
                        Material outputMaterial = outputs[outputIndex].material;
                        if (outputMaterial == null || outputMaterial.shader == null || outputMaterial.shader.name != UpdateShaderName) continue;
                        outputMaterial.SetFloat("_PanelColorSpread", DefaultPanelColorSpread);
                        EditorUtility.SetDirty(outputMaterial);
                        outputs[outputIndex].Update();
                    }
                    repairedAny = true;
                }
                if (migrateBackfaceBlocking || migrateFloorBoost || migratePanelSampling) {
                    manager.DynamicMeshLightOptimizationVersion = CurrentOptimizationVersion;
                    EditorUtility.SetDirty(manager);
                    LightVolumeManagerEditorBackend.CopyProxyToUdon(manager);
                    EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
                }
                for (int outputIndex = 0; outputIndex < outputs.Length; outputIndex++) {
                    CustomRenderTexture output = outputs[outputIndex];
                    Material material = output.material;
                    if (material == null || material.shader == null || material.shader.name != UpdateShaderName || material.GetFloat("_EmitterCount") > 0f) continue;
                    Texture2D positionArea = material.GetTexture("_EmitterPositionArea") as Texture2D;
                    if (positionArea == null) continue;

                    Color[] emitterData = positionArea.GetPixels();
                    int emitterCount = 0;
                    for (int i = 0; i < emitterData.Length; i++) if (emitterData[i].a > 0.000001f) emitterCount++;
                    if (emitterCount == 0) continue;

                    material.SetVector("_VolumeCenter", center);
                    material.SetVector("_VolumeSize", size);
                    material.SetFloat("_EmitterCount", emitterCount);
                    material.SetFloat("_EmitterTexelSize", 1f / positionArea.width);
                    EditorUtility.SetDirty(material);
                    output.Update();
                    repairedAny = true;
                }
                for (int outputIndex = 0; outputIndex < outputs.Length; outputIndex++) {
                    CustomRenderTextureUpdateMode updateMode = manager.DynamicMeshLightL0Only && outputIndex > 0 ? CustomRenderTextureUpdateMode.OnDemand : CustomRenderTextureUpdateMode.Realtime;
                    bool outputChanged = outputs[outputIndex].updateMode != updateMode;
                    outputs[outputIndex].updateMode = updateMode;
                    if (migrateToStableVr && Mathf.Abs(outputs[outputIndex].updatePeriod - 0.1f) > 0.0001f) {
                        outputs[outputIndex].updatePeriod = 0.1f;
                        outputChanged = true;
                    }
                    if (outputChanged) {
                        EditorUtility.SetDirty(outputs[outputIndex]);
                        repairedAny = true;
                    }
                }
            }
            if (!repairedAny) return false;
            AssetDatabase.SaveAssets();
            Debug.Log("[LightVolumes] Repaired realtime dome mesh-light materials and refreshed their 3D lighting textures.");
            return true;
        }

        private void OnEnable() {
            if (_manager == null) _manager = FindObjectOfType<LightVolumeManager>(true);
            if (_domeRenderer == null && Selection.activeGameObject != null) _domeRenderer = Selection.activeGameObject.GetComponent<Renderer>();
            TryFindAtlasFromRenderer();
        }

        private void OnGUI() {
            EditorGUILayout.LabelField("Realtime Dome Mesh Light", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("The mesh positions and UVs are prepared once. VideoTXL's current render texture is sampled every frame to rebuild one shared 3D Light Volume.", MessageType.Info);

            Renderer previousRenderer = _domeRenderer;
            _domeRenderer = (Renderer)EditorGUILayout.ObjectField(new GUIContent("Dome Screens", "The combined dome renderer whose UV0 reads the live video atlas."), _domeRenderer, typeof(Renderer), true);
            if (_domeRenderer != previousRenderer) {
                _liveAtlas = null;
                TryFindAtlasFromRenderer();
            }
            _liveAtlas = (Texture)EditorGUILayout.ObjectField(new GUIContent("Live Video Atlas", "The VideoTXL Custom Render Texture used by the dome screen material."), _liveAtlas, typeof(Texture), false);
            _manager = (LightVolumeManager)EditorGUILayout.ObjectField("Light Volume Manager", _manager, typeof(LightVolumeManager), true);

            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Lighting Volume", EditorStyles.boldLabel);
            _centerMode = EditorGUILayout.Popup("Dome Center", Mathf.Clamp(_centerMode, 0, CenterModeNames.Length - 1), CenterModeNames);
            if (_centerMode == 2) _centerOverride = (Transform)EditorGUILayout.ObjectField("Center Transform", _centerOverride, typeof(Transform), true);
            _centerOffset = EditorGUILayout.Vector3Field("Center Offset", _centerOffset);
            _volumeScale = Mathf.Max(0.1f, EditorGUILayout.FloatField(new GUIContent("Volume Size Scale", "1 covers the fitted dome diameter. Increase this only if receivers lie outside it."), _volumeScale));
            _receiverPadding = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Nearby Avatar Coverage", "Extra space beyond every screen edge so avatars standing beside an outer panel remain inside the live lighting field."), _receiverPadding));

            if (TryResolveMesh(_domeRenderer, out Mesh mesh, out Matrix4x4 matrix, out _)) {
                Vector3 center = ResolveCenter(mesh, matrix, out float radius);
                EditorGUILayout.LabelField("Resolved Center", center.ToString("F3"));
                EditorGUILayout.LabelField("Estimated Radius", radius.ToString("F3") + " m");
            }

            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Realtime Quality", EditorStyles.boldLabel);
            _volumeResolution = EditorGUILayout.IntPopup("Grid Resolution", _volumeResolution, new[] { "16 x 16 x 16", "24 x 24 x 24", "32 x 32 x 32", "48 x 48 x 48" }, new[] { 16, 24, 32, 48 });
            _projectionRangeScale = Mathf.Max(0.1f, EditorGUILayout.FloatField(new GUIContent("Panel Reach", "Maximum panel-to-voxel distance as a multiple of the dome radius."), _projectionRangeScale));
            _intensity = Mathf.Max(0f, EditorGUILayout.FloatField("Light Intensity", _intensity));
            _colorSaturation = EditorGUILayout.Slider(new GUIContent("Screen Color", "0 produces neutral white light; 1 uses the full screen colors."), _colorSaturation, 0f, 1f);
            _edgeFade = Mathf.Max(0.001f, EditorGUILayout.FloatField("Edge Fade", _edgeFade));
            _backfaceFade = EditorGUILayout.Slider(new GUIContent("Back Surface Fade", "Stops screen light behind the mesh while softly beginning the light on its viewing side."), _backfaceFade, 0.01f, 1f);
            _floorLightBoost = EditorGUILayout.Slider(new GUIContent("Floor Light Boost", "Strengthens the screen contribution below each panel without increasing the entire lighting volume."), _floorLightBoost, 1f, 4f);
            _panelColorSpread = EditorGUILayout.Slider(new GUIContent("Panel Color Spread", "Spreads triangular panel color samples away from labels, seams, and isolated dark pixels."), _panelColorSpread, 0f, 0.04f);
            _performanceMode = EditorGUILayout.Toggle(new GUIContent("VR Performance Mode", "Keeps realtime screen colors but uses one non-directional lighting field instead of three directional fields."), _performanceMode);
            _updatesPerSecond = EditorGUILayout.Slider(new GUIContent("Light Refresh Rate", "How often the shared 3D lighting field reads the current video frame. Lower values save GPU time while the screen itself remains full frame rate."), _updatesPerSecond, 5f, 90f);

            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            _outputFolder = EditorGUILayout.TextField("Asset Folder", string.IsNullOrWhiteSpace(_outputFolder) ? DefaultOutputFolder : _outputFolder);
            _disableExistingLights = EditorGUILayout.ToggleLeft(new GUIContent("Disable other registered light GameObjects", "Preserves every existing light and setting, but deactivates their GameObjects for the performance test. Undo restores them."), _disableExistingLights);
            if (_disableExistingLights) EditorGUILayout.HelpBox("The 37 lights are not deleted. Their GameObjects are only switched off.", MessageType.Warning);

            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(!CanCreate())) {
                if (GUILayout.Button("Create Realtime Mesh Light Volume", GUILayout.Height(34f))) CreateRealtimeMeshLight();
            }
        }

        private bool CanCreate() {
            if (_domeRenderer == null || _liveAtlas == null || _manager == null) return false;
            if (_centerMode == 2 && _centerOverride == null) return false;
            return TryResolveMesh(_domeRenderer, out _, out _, out _);
        }

        private void TryFindAtlasFromRenderer() {
            if (_domeRenderer == null || _liveAtlas != null) return;
            Material[] materials = _domeRenderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++) {
                Material material = materials[i];
                if (material == null) continue;
                Texture texture = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
                if (texture == null && material.HasProperty("_EmissionMap")) texture = material.GetTexture("_EmissionMap");
                if (texture == null) continue;
                _liveAtlas = texture;
                return;
            }
        }

        private void CreateRealtimeMeshLight() {
            if (!TryResolveMesh(_domeRenderer, out Mesh mesh, out Matrix4x4 localToWorld, out string error)) {
                EditorUtility.DisplayDialog("Realtime Dome Mesh Light", error, "OK");
                return;
            }
            Shader updateShader = Shader.Find(UpdateShaderName);
            if (updateShader == null) {
                EditorUtility.DisplayDialog("Realtime Dome Mesh Light", "The realtime mesh-light shader is still importing. Wait for Unity to finish compiling and try again.", "OK");
                return;
            }

            Vector3 center = ResolveCenter(mesh, localToWorld, out float radius);
            List<Emitter> emitters = BuildEmitters(mesh, localToWorld, center);
            if (emitters.Count == 0) {
                EditorUtility.DisplayDialog("Realtime Dome Mesh Light", "No usable screen triangles were found in the selected mesh.", "OK");
                return;
            }
            if (emitters.Count > MaxEmitterCount) {
                emitters.Sort((a, b) => b.Area.CompareTo(a.Area));
                emitters.RemoveRange(MaxEmitterCount, emitters.Count - MaxEmitterCount);
                Debug.LogWarning($"[LightVolumes] Dome mesh contained more than {MaxEmitterCount} disconnected emitters. The largest {MaxEmitterCount} were used.", _domeRenderer);
            }

            string outputFolder = NormalizeAssetFolder(_outputFolder);
            EnsureAssetFolder(outputFolder);
            Vector3 baseVolumeSize = Vector3.one * Mathf.Max(radius * 2f * _volumeScale, 0.01f);
            Vector3 volumeSize = Vector3.Max(baseVolumeSize, CalculateMeshCoverageSize(mesh, localToWorld, center, _volumeScale, _receiverPadding));

            try {
                EditorUtility.DisplayProgressBar("Realtime Dome Mesh Light", $"Preparing {emitters.Count} screen emitters", 0.2f);
                int dataWidth = Mathf.NextPowerOfTwo(Mathf.Max(emitters.Count, 1));
                Texture2D positionArea = CreateEmitterTexture(dataWidth, emitters, 0, "DomeEmitterPositionArea");
                Texture2D normal = CreateEmitterTexture(dataWidth, emitters, 1, "DomeEmitterNormal");
                Texture2D uv01 = CreateEmitterTexture(dataWidth, emitters, 2, "DomeEmitterUv01");
                Texture2D uv2 = CreateEmitterTexture(dataWidth, emitters, 3, "DomeEmitterUv2");
                AssetDatabase.CreateAsset(positionArea, AssetDatabase.GenerateUniqueAssetPath(outputFolder + "/DomeEmitterPositionArea.asset"));
                AssetDatabase.CreateAsset(normal, AssetDatabase.GenerateUniqueAssetPath(outputFolder + "/DomeEmitterNormal.asset"));
                AssetDatabase.CreateAsset(uv01, AssetDatabase.GenerateUniqueAssetPath(outputFolder + "/DomeEmitterUv01.asset"));
                AssetDatabase.CreateAsset(uv2, AssetDatabase.GenerateUniqueAssetPath(outputFolder + "/DomeEmitterUv2.asset"));

                EditorUtility.DisplayProgressBar("Realtime Dome Mesh Light", "Creating live 3D lighting textures", 0.55f);
                CustomRenderTexture[] outputs = new CustomRenderTexture[3];
                for (int channel = 0; channel < 3; channel++) {
                    Material material = new Material(updateShader) { name = $"DomeMeshLightUpdate{channel}" };
                    material.SetTexture("_SourceTex", _liveAtlas);
                    material.SetTexture("_EmitterPositionArea", positionArea);
                    material.SetTexture("_EmitterNormal", normal);
                    material.SetTexture("_EmitterUv01", uv01);
                    material.SetTexture("_EmitterUv2", uv2);
                    material.SetVector("_VolumeCenter", center);
                    material.SetVector("_VolumeSize", volumeSize);
                    material.SetFloat("_EmitterCount", emitters.Count);
                    material.SetFloat("_EmitterTexelSize", 1f / dataWidth);
                    material.SetFloat("_Intensity", _intensity);
                    material.SetFloat("_ColorSaturation", _colorSaturation);
                    material.SetFloat("_ProjectionRange", radius * _projectionRangeScale);
                    material.SetFloat("_BackfaceFade", _backfaceFade);
                    material.SetFloat("_FloorLightBoost", _floorLightBoost);
                    material.SetFloat("_PanelColorSpread", _panelColorSpread);
                    material.SetFloat("_UseBakedOcclusion", 0f);
                    material.SetFloat("_BakedShadowStrength", 1f);
                    material.SetFloat("_BakedShadowContrast", 2f);
                    material.SetVector("_BakedOcclusionChannel", new Vector4(1f, 0f, 0f, 0f));
                    material.SetInt("_OutputChannel", channel);
                    AssetDatabase.CreateAsset(material, AssetDatabase.GenerateUniqueAssetPath(outputFolder + $"/DomeMeshLightUpdate{channel}.mat"));

                    CustomRenderTexture output = new CustomRenderTexture(_volumeResolution, _volumeResolution, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) {
                        name = $"DomeMeshLightVolume{channel}",
                        dimension = TextureDimension.Tex3D,
                        volumeDepth = _volumeResolution,
                        material = material,
                        updateMode = _performanceMode && channel > 0 ? CustomRenderTextureUpdateMode.OnDemand : CustomRenderTextureUpdateMode.Realtime,
                        updatePeriod = 1f / Mathf.Max(_updatesPerSecond, 1f),
                        initializationColor = Color.clear,
                        useMipMap = false,
                        autoGenerateMips = false,
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp
                    };
                    AssetDatabase.CreateAsset(output, AssetDatabase.GenerateUniqueAssetPath(outputFolder + $"/DomeMeshLightVolume{channel}.asset"));
                    outputs[channel] = output;
                }

                EditorUtility.DisplayProgressBar("Realtime Dome Mesh Light", "Connecting the Light Volume Manager", 0.85f);
                Undo.RecordObject(_manager, "Create Realtime Dome Mesh Light");
                _manager.DynamicMeshLightEnabled = true;
                _manager.DynamicMeshLightL0Only = _performanceMode;
                _manager.DynamicMeshLightOptimizationVersion = CurrentOptimizationVersion;
                _manager.DynamicMeshLightTexture0 = outputs[0];
                _manager.DynamicMeshLightTexture1 = outputs[1];
                _manager.DynamicMeshLightTexture2 = outputs[2];
                _manager.DynamicMeshLightInvWorldMatrix = Matrix4x4.TRS(center, Quaternion.identity, volumeSize).inverse;
                _manager.DynamicMeshLightInvEdgeSmooth = new Vector3(volumeSize.x / _edgeFade, volumeSize.y / _edgeFade, volumeSize.z / _edgeFade);
                _manager.DynamicMeshLightColor = Color.white;
                EditorUtility.SetDirty(_manager);
                LightVolumeManagerEditorBackend.CopyProxyToUdon(_manager);

                DomeMeshLightAtlasBridgeUtility.Create(_manager, center, volumeSize, _volumeResolution, _edgeFade, _updatesPerSecond, outputFolder);

                if (_disableExistingLights) DisableExistingPointLights();
                _manager.UpdateVolumes();
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(_manager.gameObject.scene);
                Selection.activeGameObject = _manager.gameObject;
                EditorGUIUtility.PingObject(_manager);
                EditorUtility.DisplayDialog("Realtime Dome Mesh Light", $"Created one realtime mesh Light Volume from {emitters.Count} dome screen sections. It now publishes through the standard Light Volume atlas for compatible world and avatar shaders.", "Done");
            } catch (Exception exception) {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Realtime Dome Mesh Light", "The realtime mesh Light Volume could not be created. See the Console for details.", "OK");
            } finally {
                EditorUtility.ClearProgressBar();
            }
        }

        private void DisableExistingPointLights() {
            PointLightVolumeInstance[] lights = _manager.PointLightVolumeInstances;
            if (lights == null) return;
            for (int i = 0; i < lights.Length; i++) {
                PointLightVolumeInstance light = lights[i];
                if (light == null || !light.gameObject.activeSelf) continue;
                Undo.RecordObject(light.gameObject, "Disable Original Dome Lights");
                light.gameObject.SetActive(false);
            }
        }

        private Vector3 ResolveCenter(Mesh mesh, Matrix4x4 localToWorld, out float radius) {
            Vector3 center;
            if (_centerMode == 2 && _centerOverride != null) center = _centerOverride.position;
            else if (_centerMode == 0 && TryFitSphere(mesh, localToWorld, out Vector3 fittedCenter)) center = fittedCenter;
            else center = TransformBounds(mesh.bounds, localToWorld).center;
            center += _centerOffset;
            radius = EstimateRadius(mesh, localToWorld, center);
            return center;
        }

        private static List<Emitter> BuildEmitters(Mesh mesh, Matrix4x4 localToWorld, Vector3 domeCenter) {
            Vector3[] vertices = mesh.vertices;
            Vector2[] uv = mesh.uv;
            List<int> triangles = new List<int>();
            UnionFind unionFind = new UnionFind(vertices.Length);
            Dictionary<VertexKey, int> duplicateVertices = new Dictionary<VertexKey, int>();
            for (int i = 0; i < vertices.Length; i++) {
                VertexKey key = new VertexKey(vertices[i], uv[i]);
                if (duplicateVertices.TryGetValue(key, out int original)) unionFind.Union(original, i);
                else duplicateVertices.Add(key, i);
            }
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++) {
                int[] subMeshTriangles = mesh.GetTriangles(subMesh);
                for (int i = 0; i + 2 < subMeshTriangles.Length; i += 3) {
                    int a = subMeshTriangles[i];
                    int b = subMeshTriangles[i + 1];
                    int c = subMeshTriangles[i + 2];
                    unionFind.Union(a, b);
                    unionFind.Union(a, c);
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(c);
                }
            }

            Dictionary<int, EmitterAccumulator> groups = new Dictionary<int, EmitterAccumulator>();
            for (int i = 0; i + 2 < triangles.Count; i += 3) {
                int ia = triangles[i];
                int ib = triangles[i + 1];
                int ic = triangles[i + 2];
                Vector3 a = localToWorld.MultiplyPoint3x4(vertices[ia]);
                Vector3 b = localToWorld.MultiplyPoint3x4(vertices[ib]);
                Vector3 c = localToWorld.MultiplyPoint3x4(vertices[ic]);
                Vector3 cross = Vector3.Cross(b - a, c - a);
                float area = cross.magnitude * 0.5f;
                if (area <= 0.000001f) continue;

                int root = unionFind.Find(ia);
                if (!groups.TryGetValue(root, out EmitterAccumulator group)) {
                    group = new EmitterAccumulator();
                    groups.Add(root, group);
                }
                Vector3 centroid = (a + b + c) / 3f;
                group.Area += area;
                group.WeightedPosition += centroid * area;
                group.WeightedNormal += cross * 0.5f;
                Vector2 uvA = uv[ia];
                Vector2 uvB = uv[ib];
                Vector2 uvC = uv[ic];
                group.AddSample(area, uvA * 0.6f + uvB * 0.2f + uvC * 0.2f);
                group.AddSample(area, uvA * 0.2f + uvB * 0.6f + uvC * 0.2f);
                group.AddSample(area, uvA * 0.2f + uvB * 0.2f + uvC * 0.6f);
            }

            List<Emitter> emitters = new List<Emitter>(groups.Count);
            foreach (EmitterAccumulator group in groups.Values) {
                if (group.Area <= 0.000001f) continue;
                Vector3 position = group.WeightedPosition / group.Area;
                Vector3 normal = group.WeightedNormal.sqrMagnitude > 0.000001f ? group.WeightedNormal.normalized : (domeCenter - position).normalized;
                if (Vector3.Dot(normal, domeCenter - position) < 0f) normal = -normal;
                Vector2 fallbackUv = group.Samples[0].Uv;
                emitters.Add(new Emitter {
                    Position = position,
                    Normal = normal,
                    Area = group.Area,
                    Uv0 = fallbackUv,
                    Uv1 = group.Samples[1].Area > 0f ? group.Samples[1].Uv : fallbackUv,
                    Uv2 = group.Samples[2].Area > 0f ? group.Samples[2].Uv : fallbackUv
                });
            }
            return emitters;
        }

        private static Texture2D CreateEmitterTexture(int width, List<Emitter> emitters, int dataType, string name) {
            Texture2D texture = new Texture2D(width, 1, TextureFormat.RGBAFloat, false, true) {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            Color[] pixels = new Color[width];
            for (int i = 0; i < emitters.Count; i++) {
                Emitter emitter = emitters[i];
                if (dataType == 0) pixels[i] = new Color(emitter.Position.x, emitter.Position.y, emitter.Position.z, emitter.Area);
                else if (dataType == 1) pixels[i] = new Color(emitter.Normal.x, emitter.Normal.y, emitter.Normal.z, 1f);
                else if (dataType == 2) pixels[i] = new Color(emitter.Uv0.x, emitter.Uv0.y, emitter.Uv1.x, emitter.Uv1.y);
                else pixels[i] = new Color(emitter.Uv2.x, emitter.Uv2.y, 0f, 0f);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static bool TryResolveMesh(Renderer renderer, out Mesh mesh, out Matrix4x4 localToWorld, out string error) {
            mesh = null;
            localToWorld = Matrix4x4.identity;
            error = null;
            if (renderer == null) {
                error = "Choose the combined dome-screen renderer.";
                return false;
            }
            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter != null) mesh = meshFilter.sharedMesh;
            if (mesh == null && renderer is SkinnedMeshRenderer skinnedRenderer) mesh = skinnedRenderer.sharedMesh;
            if (mesh == null) {
                error = "The selected renderer does not contain a usable mesh.";
                return false;
            }
            if (!mesh.isReadable) {
                error = "Enable Read/Write in this model's import settings so the dome positions and UVs can be prepared.";
                return false;
            }
            Vector2[] uv = mesh.uv;
            if (mesh.vertexCount == 0 || uv == null || uv.Length != mesh.vertexCount) {
                error = "The selected mesh needs UV0 coordinates for every vertex.";
                return false;
            }
            localToWorld = renderer.localToWorldMatrix;
            return true;
        }

        private static bool TryFitSphere(Mesh mesh, Matrix4x4 localToWorld, out Vector3 center) {
            center = Vector3.zero;
            Vector3[] vertices = mesh.vertices;
            if (vertices == null || vertices.Length < 4) return false;
            int step = Mathf.Max(1, vertices.Length / 20000);
            Vector3 p0 = localToWorld.MultiplyPoint3x4(vertices[0]);
            double a00 = 0, a01 = 0, a02 = 0, a11 = 0, a12 = 0, a22 = 0;
            double b0 = 0, b1 = 0, b2 = 0;
            double p0Sq = p0.sqrMagnitude;
            int samples = 0;
            for (int i = step; i < vertices.Length; i += step) {
                Vector3 p = localToWorld.MultiplyPoint3x4(vertices[i]);
                double x = 2.0 * (p.x - p0.x);
                double y = 2.0 * (p.y - p0.y);
                double z = 2.0 * (p.z - p0.z);
                double rhs = p.sqrMagnitude - p0Sq;
                a00 += x * x; a01 += x * y; a02 += x * z;
                a11 += y * y; a12 += y * z; a22 += z * z;
                b0 += x * rhs; b1 += y * rhs; b2 += z * rhs;
                samples++;
            }
            if (samples < 3 || !SolveSymmetric3x3(a00, a01, a02, a11, a12, a22, b0, b1, b2, out double cx, out double cy, out double cz)) return false;
            center = new Vector3((float)cx, (float)cy, (float)cz);
            return IsFinite(center.x) && IsFinite(center.y) && IsFinite(center.z);
        }

        private static bool SolveSymmetric3x3(double a00, double a01, double a02, double a11, double a12, double a22, double b0, double b1, double b2, out double x, out double y, out double z) {
            double determinant = a00 * (a11 * a22 - a12 * a12) - a01 * (a01 * a22 - a12 * a02) + a02 * (a01 * a12 - a11 * a02);
            if (Math.Abs(determinant) < 1e-12) {
                x = y = z = 0;
                return false;
            }
            x = (b0 * (a11 * a22 - a12 * a12) - a01 * (b1 * a22 - a12 * b2) + a02 * (b1 * a12 - a11 * b2)) / determinant;
            y = (a00 * (b1 * a22 - a12 * b2) - b0 * (a01 * a22 - a12 * a02) + a02 * (a01 * b2 - b1 * a02)) / determinant;
            z = (a00 * (a11 * b2 - b1 * a12) - a01 * (a01 * b2 - b1 * a02) + b0 * (a01 * a12 - a11 * a02)) / determinant;
            return true;
        }

        private static float EstimateRadius(Mesh mesh, Matrix4x4 localToWorld, Vector3 center) {
            Vector3[] vertices = mesh.vertices;
            int step = Mathf.Max(1, vertices.Length / 20000);
            double sum = 0;
            int samples = 0;
            for (int i = 0; i < vertices.Length; i += step) {
                sum += Vector3.Distance(localToWorld.MultiplyPoint3x4(vertices[i]), center);
                samples++;
            }
            return samples > 0 ? Mathf.Max((float)(sum / samples), 0.0001f) : 1f;
        }

        private static float GetEdgeFade(Vector3 volumeSize, Vector3 inverseEdgeSmooth) {
            float x = inverseEdgeSmooth.x > 0.0001f ? volumeSize.x / inverseEdgeSmooth.x : 0f;
            float y = inverseEdgeSmooth.y > 0.0001f ? volumeSize.y / inverseEdgeSmooth.y : 0f;
            float z = inverseEdgeSmooth.z > 0.0001f ? volumeSize.z / inverseEdgeSmooth.z : 0f;
            return Mathf.Max((x + y + z) / 3f, 0.05f);
        }

        private static Vector3 CalculateMeshCoverageSize(Mesh mesh, Matrix4x4 localToWorld, Vector3 center, float scale, float receiverPadding) {
            Vector3[] vertices = mesh.vertices;
            Vector3 halfSize = Vector3.zero;
            for (int i = 0; i < vertices.Length; i++) {
                Vector3 offset = localToWorld.MultiplyPoint3x4(vertices[i]) - center;
                halfSize.x = Mathf.Max(halfSize.x, Mathf.Abs(offset.x));
                halfSize.y = Mathf.Max(halfSize.y, Mathf.Abs(offset.y));
                halfSize.z = Mathf.Max(halfSize.z, Mathf.Abs(offset.z));
            }
            halfSize = halfSize * Mathf.Max(scale, 0.1f) + Vector3.one * Mathf.Max(receiverPadding, 0f);
            return Vector3.Max(halfSize * 2f, Vector3.one * 0.01f);
        }

        internal static bool TryCalculateExpandedCoverage(Material material, Vector3 center, Vector3 currentSize, float receiverPadding, out Vector3 expandedSize) {
            expandedSize = currentSize;
            if (material == null) return false;
            Texture2D positionArea = material.GetTexture("_EmitterPositionArea") as Texture2D;
            if (positionArea == null || !positionArea.isReadable) return false;

            Color[] emitters = positionArea.GetPixels();
            int emitterCount = Mathf.Min(Mathf.RoundToInt(material.GetFloat("_EmitterCount")), emitters.Length);
            if (emitterCount <= 0) return false;
            Vector3 halfSize = currentSize * 0.5f;
            float padding = Mathf.Max(receiverPadding, 0f);
            for (int i = 0; i < emitterCount; i++) {
                Color emitter = emitters[i];
                if (emitter.a <= 0.000001f) continue;
                float footprint = Mathf.Sqrt(emitter.a / Mathf.PI);
                halfSize.x = Mathf.Max(halfSize.x, Mathf.Abs(emitter.r - center.x) + footprint + padding);
                halfSize.y = Mathf.Max(halfSize.y, Mathf.Abs(emitter.g - center.y) + footprint + padding);
                halfSize.z = Mathf.Max(halfSize.z, Mathf.Abs(emitter.b - center.z) + footprint + padding);
            }
            expandedSize = halfSize * 2f;
            return true;
        }

        private static Bounds TransformBounds(Bounds localBounds, Matrix4x4 matrix) {
            Vector3 center = matrix.MultiplyPoint3x4(localBounds.center);
            Vector3 extents = localBounds.extents;
            Vector3 axisX = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
            Vector3 axisY = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
            Vector3 axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));
            extents = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
            return new Bounds(center, extents * 2f);
        }

        private static bool IsFinite(float value) {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string NormalizeAssetFolder(string folder) {
            folder = string.IsNullOrWhiteSpace(folder) ? DefaultOutputFolder : folder.Trim().Replace('\\', '/').TrimEnd('/');
            if (folder != "Assets" && !folder.StartsWith("Assets/", StringComparison.Ordinal)) folder = "Assets/" + folder.TrimStart('/');
            return folder;
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
