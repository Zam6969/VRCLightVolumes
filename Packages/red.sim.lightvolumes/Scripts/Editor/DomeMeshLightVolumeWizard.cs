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
        [SerializeField] private float _projectionRangeScale = 2f;
        [SerializeField] private float _intensity = 5f;
        [SerializeField] private float _edgeFade = 0.5f;
        [SerializeField] private float _updatesPerSecond = 30f;
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

                Matrix4x4 volumeMatrix = manager.DynamicMeshLightInvWorldMatrix.inverse;
                Vector3 center = volumeMatrix.MultiplyPoint3x4(Vector3.zero);
                Vector3 size = new Vector3(volumeMatrix.GetColumn(0).magnitude, volumeMatrix.GetColumn(1).magnitude, volumeMatrix.GetColumn(2).magnitude);
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
            _edgeFade = Mathf.Max(0.001f, EditorGUILayout.FloatField("Edge Fade", _edgeFade));
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
            Vector3 volumeSize = Vector3.one * Mathf.Max(radius * 2f * _volumeScale, 0.01f);

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
                    material.SetFloat("_ProjectionRange", radius * _projectionRangeScale);
                    material.SetInt("_OutputChannel", channel);
                    AssetDatabase.CreateAsset(material, AssetDatabase.GenerateUniqueAssetPath(outputFolder + $"/DomeMeshLightUpdate{channel}.mat"));

                    CustomRenderTexture output = new CustomRenderTexture(_volumeResolution, _volumeResolution, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) {
                        name = $"DomeMeshLightVolume{channel}",
                        dimension = TextureDimension.Tex3D,
                        volumeDepth = _volumeResolution,
                        material = material,
                        updateMode = CustomRenderTextureUpdateMode.Realtime,
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
                _manager.DynamicMeshLightTexture0 = outputs[0];
                _manager.DynamicMeshLightTexture1 = outputs[1];
                _manager.DynamicMeshLightTexture2 = outputs[2];
                _manager.DynamicMeshLightInvWorldMatrix = Matrix4x4.TRS(center, Quaternion.identity, volumeSize).inverse;
                _manager.DynamicMeshLightInvEdgeSmooth = new Vector3(volumeSize.x / _edgeFade, volumeSize.y / _edgeFade, volumeSize.z / _edgeFade);
                _manager.DynamicMeshLightColor = Color.white;
                EditorUtility.SetDirty(_manager);
                LightVolumeManagerEditorBackend.CopyProxyToUdon(_manager);

                if (_disableExistingLights) DisableExistingPointLights();
                _manager.UpdateVolumes();
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(_manager.gameObject.scene);
                Selection.activeGameObject = _manager.gameObject;
                EditorGUIUtility.PingObject(_manager);
                EditorUtility.DisplayDialog("Realtime Dome Mesh Light", $"Created one realtime mesh Light Volume from {emitters.Count} dome screen sections. The live video render texture drives it every frame.", "Done");
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
                group.AddSample(area, (uv[ia] + uv[ib] + uv[ic]) / 3f);
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
