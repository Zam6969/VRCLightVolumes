using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace VRCLightVolumes {
    [InitializeOnLoad]
    internal static class DomeMeshLightLightmapperShadowBaker {
        private const string RestoreObjectName = "Realtime Mesh Light - Screen Bake Restore State";
        private const string EmitterObjectName = "Realtime Mesh Light - Temporary Screen Emitter";
        private const string OutputFolder = "Assets/LightVolumesDome";
        private static bool _subscribed;

        static DomeMeshLightLightmapperShadowBaker() {
            EditorApplication.delayCall += RecoverInterruptedBake;
        }

        internal static bool IsInProgress => FindRestoreState() != null;

        internal static bool Start(Renderer sourceRenderer, LightVolumeManager manager, Vector3 center, Vector3 size, Vector3Int resolution, float cutoff, float strength, float contrast, out string error) {
            error = null;
            if (sourceRenderer == null || manager == null) {
                error = "Assign the combined screen mesh and Light Volume Manager first.";
                return false;
            }
            if (!BakeryEditorBridge.SupportsDomeShadowMask || !BakeryEditorBridge.SupportsFullRenderLifecycle) {
                error = "This Bakery version does not expose the mesh-light, volume, and full-bake APIs needed by this workflow.";
                return false;
            }
            if (BakeryEditorBridge.IsBaking || IsInProgress) {
                error = "A Bakery or realtime mesh shadow bake is already running.";
                return false;
            }

            DomeMeshLightBakeRestoreState state = null;
            try {
                EnsureAssetFolder(OutputFolder);
                state = CaptureSceneState(sourceRenderer, manager, strength, contrast);
                DisableExternalContributors(state);
                Renderer emitterRenderer = CreateTemporaryEmitter(sourceRenderer, state);
                if (!BakeryEditorBridge.TrySetupDomeMeshField(emitterRenderer, manager, center, size, resolution, cutoff, out UnityEngine.Object helperVolume, out error)) {
                    RestoreScene(state);
                    return false;
                }

                state.BakeryHelperVolume = helperVolume as Component;
                DisableOtherBakeryVolumes(state, helperVolume);
                EditorUtility.SetDirty(state);
                EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
                EditorSceneManager.SaveOpenScenes();
                Subscribe();
                EditorApplication.delayCall += StartBakeryPass;
                return true;
            } catch (Exception exception) {
                error = exception.Message;
                if (state != null) RestoreScene(state);
                return false;
            }
        }

        [MenuItem("Tools/Light Volumes/Restore Interrupted Mesh Shadow Bake")]
        private static void RestoreInterruptedBakeMenu() {
            DomeMeshLightBakeRestoreState state = FindRestoreState();
            if (state == null) {
                EditorUtility.DisplayDialog("Realtime Mesh Light Shadows", "No interrupted screen shadow bake was found.", "OK");
                return;
            }
            RestoreScene(state);
            EditorSceneManager.SaveOpenScenes();
            EditorUtility.DisplayDialog("Realtime Mesh Light Shadows", "The scene lighting state was restored.", "Done");
        }

        private static void StartBakeryPass() {
            DomeMeshLightBakeRestoreState state = FindRestoreState();
            if (state == null) return;
            if (BakeryEditorBridge.TryStartFullRender(out string error)) return;
            RestoreScene(state);
            EditorSceneManager.SaveOpenScenes();
            EditorUtility.DisplayDialog("Realtime Mesh Light Shadows", "Bakery could not start the screen-only pass:\n\n" + error, "OK");
        }

        private static void OnBakeryFinished(object sender, EventArgs args) {
            Unsubscribe();
            EditorApplication.delayCall += FinishScreenPass;
        }

        private static void FinishScreenPass() {
            DomeMeshLightBakeRestoreState state = FindRestoreState();
            if (state == null) return;
            if (BakeryEditorBridge.WasCanceled) {
                RestoreScene(state);
                EditorSceneManager.SaveOpenScenes();
                EditorUtility.DisplayDialog("Realtime Mesh Light Shadows", "The Bakery screen pass was canceled. Your original scene lighting was restored.", "OK");
                return;
            }

            LightVolumeManager manager = state.Manager;
            float strength = state.ShadowStrength;
            float contrast = state.ShadowContrast;
            Component helper = state.BakeryHelperVolume;
            if (!BakeryEditorBridge.TryGetVolumeLightingTextures(helper, out Texture3D source0, out Texture3D source1, out Texture3D source2)) {
                RestoreScene(state);
                EditorSceneManager.SaveOpenScenes();
                EditorUtility.DisplayDialog("Realtime Mesh Light Shadows", "Bakery finished, but the dedicated screen volume did not contain lighting textures. Check that Volumes are enabled in Bakery and try again.", "OK");
                return;
            }

            Texture3D texture0 = CopyTexture(source0, "DomeMeshScreenShadowL0");
            CopyTexture(source1, "DomeMeshScreenShadowL1A");
            CopyTexture(source2, "DomeMeshScreenShadowL1B");
            float normalization = CalculateNormalization(texture0);
            Vector3Int resolution = new Vector3Int(texture0.width, texture0.height, texture0.depth);
            bool applied = DomeMeshLightAtlasBridgeUtility.ApplyLightmapperScreenField(manager, texture0, resolution, normalization, strength, contrast);

            RestoreScene(state);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            if (!applied) {
                EditorUtility.DisplayDialog("Realtime Mesh Light Shadows", "The mesh shadow field baked successfully, but the standard Light Volume bridge could not accept it.", "OK");
                return;
            }

            EditorGUIUtility.PingObject(texture0);
            EditorApplication.delayCall += StartRestoredWorldBake;
        }

        private static void StartRestoredWorldBake() {
            if (!BakeryEditorBridge.TryStartFullRender(out string error)) {
                EditorUtility.DisplayDialog("Realtime Mesh Light Shadows", "The screen shadow field is ready, but Bakery could not start the restored world bake:\n\n" + error + "\n\nRun your normal Bakery bake once before uploading.", "OK");
                return;
            }
            EditorUtility.DisplayDialog("Realtime Mesh Light Shadows", "The screen-mesh shadow field is ready. Bakery has started the normal world bake with your original lights and materials restored.", "Done");
        }

        private static DomeMeshLightBakeRestoreState CaptureSceneState(Renderer sourceRenderer, LightVolumeManager manager, float strength, float contrast) {
            GameObject restoreObject = new GameObject(RestoreObjectName) { tag = "EditorOnly" };
            SceneManager.MoveGameObjectToScene(restoreObject, manager.gameObject.scene);
            restoreObject.transform.SetParent(manager.transform, false);
            DomeMeshLightBakeRestoreState state = restoreObject.AddComponent<DomeMeshLightBakeRestoreState>();
            state.Manager = manager;
            state.SourceRenderer = sourceRenderer;
            state.SourceRendererEnabled = sourceRenderer.enabled;
            state.ShadowStrength = Mathf.Clamp01(strength);
            state.ShadowContrast = Mathf.Clamp(contrast, 0.5f, 8f);
            state.AmbientMode = RenderSettings.ambientMode;
            state.AmbientIntensity = RenderSettings.ambientIntensity;
            state.AmbientSkyColor = RenderSettings.ambientSkyColor;

            HashSet<GameObject> contributors = new HashSet<GameObject>();
            AddSceneObjects(Resources.FindObjectsOfTypeAll<Light>(), contributors);
            AddSceneObjects(Resources.FindObjectsOfTypeAll<ReflectionProbe>(), contributors);
            Type[] bakeryLightTypes = BakeryEditorBridge.BakeryLightComponentTypes;
            for (int i = 0; i < bakeryLightTypes.Length; i++) {
                Type type = bakeryLightTypes[i];
                if (type == null) continue;
                UnityEngine.Object[] objects = Resources.FindObjectsOfTypeAll(type);
                for (int j = 0; j < objects.Length; j++) {
                    Component component = objects[j] as Component;
                    if (IsSceneComponent(component)) contributors.Add(component.gameObject);
                }
            }
            List<Component> volumes = new List<Component>();
            Type volumeType = BakeryEditorBridge.BakeryVolumeComponentType;
            if (volumeType != null) {
                UnityEngine.Object[] objects = Resources.FindObjectsOfTypeAll(volumeType);
                for (int i = 0; i < objects.Length; i++) {
                    Component component = objects[i] as Component;
                    if (IsSceneComponent(component)) volumes.Add(component);
                }
            }
            state.BakeryVolumes = volumes.ToArray();
            state.BakeryVolumeBakeStates = new bool[state.BakeryVolumes.Length];
            for (int i = 0; i < state.BakeryVolumes.Length; i++) {
                BakeryEditorBridge.TryGetVolumeBakingEnabled(state.BakeryVolumes[i], out state.BakeryVolumeBakeStates[i]);
                contributors.Add(state.BakeryVolumes[i].gameObject);
            }

            state.ContributorObjects = new List<GameObject>(contributors).ToArray();
            state.ContributorActiveStates = new bool[state.ContributorObjects.Length];
            for (int i = 0; i < state.ContributorObjects.Length; i++) state.ContributorActiveStates[i] = state.ContributorObjects[i].activeSelf;

            HashSet<Material> materials = new HashSet<Material>();
            Renderer[] renderers = Resources.FindObjectsOfTypeAll<Renderer>();
            for (int i = 0; i < renderers.Length; i++) {
                if (!IsSceneComponent(renderers[i]) || !renderers[i].gameObject.activeInHierarchy) continue;
                Material[] shared = renderers[i].sharedMaterials;
                for (int j = 0; j < shared.Length; j++) if (shared[j] != null) materials.Add(shared[j]);
            }
            state.Materials = new List<Material>(materials).ToArray();
            state.MaterialGiFlags = new int[state.Materials.Length];
            for (int i = 0; i < state.Materials.Length; i++) state.MaterialGiFlags[i] = (int)state.Materials[i].globalIlluminationFlags;
            return state;
        }

        private static void DisableExternalContributors(DomeMeshLightBakeRestoreState state) {
            for (int i = 0; i < state.ContributorObjects.Length; i++) {
                GameObject candidate = state.ContributorObjects[i];
                if (candidate != null && candidate.activeSelf) candidate.SetActive(false);
            }
            for (int i = 0; i < state.Materials.Length; i++) {
                if (state.Materials[i] != null) state.Materials[i].globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientIntensity = 0f;
            RenderSettings.ambientSkyColor = Color.black;
            state.SourceRenderer.enabled = false;
        }

        private static void DisableOtherBakeryVolumes(DomeMeshLightBakeRestoreState state, UnityEngine.Object helperVolume) {
            for (int i = 0; i < state.BakeryVolumes.Length; i++) {
                if (state.BakeryVolumes[i] != null && state.BakeryVolumes[i] != helperVolume) BakeryEditorBridge.SetVolumeBakingEnabled(state.BakeryVolumes[i], false);
            }
            if (helperVolume is Component helperComponent && !helperComponent.gameObject.activeSelf) helperComponent.gameObject.SetActive(true);
            BakeryEditorBridge.SetVolumeBakingEnabled(helperVolume, true);
        }

        private static Renderer CreateTemporaryEmitter(Renderer sourceRenderer, DomeMeshLightBakeRestoreState state) {
            Mesh sourceMesh = null;
            MeshFilter sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
            if (sourceFilter != null) sourceMesh = sourceFilter.sharedMesh;
            if (sourceMesh == null && sourceRenderer is SkinnedMeshRenderer skinned) {
                sourceMesh = new Mesh { name = "DomeMeshScreenBakeMesh" };
                skinned.BakeMesh(sourceMesh);
                string meshPath = AssetDatabase.GenerateUniqueAssetPath(OutputFolder + "/DomeMeshScreenBakeMesh.asset");
                AssetDatabase.CreateAsset(sourceMesh, meshPath);
                AddTemporaryAsset(state, meshPath);
            }
            if (sourceMesh == null) throw new InvalidOperationException("The selected renderer does not contain a MeshFilter or bakeable skinned mesh.");

            Shader standard = Shader.Find("Standard");
            if (standard == null) throw new InvalidOperationException("Unity's Standard shader is not available for the temporary emissive screen.");
            Material material = new Material(standard) { name = "Dome Mesh Screen Bake Emitter" };
            material.SetColor("_Color", Color.black);
            material.SetColor("_EmissionColor", Color.white);
            material.EnableKeyword("_EMISSION");
            material.doubleSidedGI = true;
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            string materialPath = AssetDatabase.GenerateUniqueAssetPath(OutputFolder + "/DomeMeshScreenBakeEmitter.mat");
            AssetDatabase.CreateAsset(material, materialPath);
            AddTemporaryAsset(state, materialPath);

            GameObject emitter = new GameObject(EmitterObjectName) { tag = "EditorOnly", layer = sourceRenderer.gameObject.layer };
            SceneManager.MoveGameObjectToScene(emitter, sourceRenderer.gameObject.scene);
            state.TemporaryEmitter = emitter;
            emitter.transform.SetPositionAndRotation(sourceRenderer.transform.position, sourceRenderer.transform.rotation);
            emitter.transform.localScale = sourceRenderer.transform.lossyScale;
            MeshFilter filter = emitter.AddComponent<MeshFilter>();
            filter.sharedMesh = sourceMesh;
            MeshRenderer renderer = emitter.AddComponent<MeshRenderer>();
            int materialCount = Mathf.Max(sourceMesh.subMeshCount, 1);
            Material[] materials = new Material[materialCount];
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = false;
            GameObjectUtility.SetStaticEditorFlags(emitter, StaticEditorFlags.ContributeGI);
            return renderer;
        }

        private static Texture3D CopyTexture(Texture3D source, string name) {
            if (source == null) return null;
            string destination = AssetDatabase.GenerateUniqueAssetPath(OutputFolder + "/" + name + ".asset");
            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (!string.IsNullOrEmpty(sourcePath) && AssetDatabase.CopyAsset(sourcePath, destination)) return AssetDatabase.LoadAssetAtPath<Texture3D>(destination);
            Texture3D copy = UnityEngine.Object.Instantiate(source);
            copy.name = name;
            AssetDatabase.CreateAsset(copy, destination);
            return copy;
        }

        private static float CalculateNormalization(Texture3D texture) {
            try {
                Color[] pixels = texture.GetPixels();
                float maximum = 0f;
                for (int i = 0; i < pixels.Length; i++) maximum = Mathf.Max(maximum, pixels[i].r, pixels[i].g, pixels[i].b);
                return maximum > 0.0001f ? Mathf.Clamp(1f / maximum, 0.01f, 100f) : 1f;
            } catch {
                return 1f;
            }
        }

        private static void RestoreScene(DomeMeshLightBakeRestoreState state) {
            if (state == null) return;
            Unsubscribe();
            bool helperWasCaptured = false;
            for (int i = 0; i < state.BakeryVolumes.Length; i++) {
                Component volume = state.BakeryVolumes[i];
                if (volume == null) continue;
                if (volume == state.BakeryHelperVolume) helperWasCaptured = true;
                BakeryEditorBridge.SetVolumeBakingEnabled(volume, state.BakeryVolumeBakeStates[i]);
            }
            if (state.BakeryHelperVolume != null && !helperWasCaptured) BakeryEditorBridge.SetVolumeBakingEnabled(state.BakeryHelperVolume, false);
            for (int i = 0; i < state.ContributorObjects.Length; i++) {
                GameObject candidate = state.ContributorObjects[i];
                if (candidate != null && candidate.activeSelf != state.ContributorActiveStates[i]) candidate.SetActive(state.ContributorActiveStates[i]);
            }
            if (state.SourceRenderer != null) state.SourceRenderer.enabled = state.SourceRendererEnabled;
            for (int i = 0; i < state.Materials.Length; i++) {
                if (state.Materials[i] != null) state.Materials[i].globalIlluminationFlags = (MaterialGlobalIlluminationFlags)state.MaterialGiFlags[i];
            }
            RenderSettings.ambientMode = state.AmbientMode;
            RenderSettings.ambientIntensity = state.AmbientIntensity;
            RenderSettings.ambientSkyColor = state.AmbientSkyColor;
            DynamicGI.UpdateEnvironment();

            string[] temporaryPaths = state.TemporaryAssetPaths;
            GameObject emitter = state.TemporaryEmitter;
            GameObject restoreObject = state.gameObject;
            if (emitter != null) UnityEngine.Object.DestroyImmediate(emitter);
            if (restoreObject != null) UnityEngine.Object.DestroyImmediate(restoreObject);
            if (temporaryPaths != null) {
                for (int i = 0; i < temporaryPaths.Length; i++) if (!string.IsNullOrEmpty(temporaryPaths[i])) AssetDatabase.DeleteAsset(temporaryPaths[i]);
            }
            AssetDatabase.SaveAssets();
            SceneView.RepaintAll();
        }

        private static void RecoverInterruptedBake() {
            DomeMeshLightBakeRestoreState state = FindRestoreState();
            if (state == null) return;
            if (BakeryEditorBridge.IsBaking) {
                Subscribe();
                return;
            }
            RestoreScene(state);
            EditorSceneManager.SaveOpenScenes();
            Debug.LogWarning("[LightVolumes] Restored scene lighting after an interrupted screen-mesh shadow bake.");
        }

        private static DomeMeshLightBakeRestoreState FindRestoreState() {
            DomeMeshLightBakeRestoreState[] states = Resources.FindObjectsOfTypeAll<DomeMeshLightBakeRestoreState>();
            for (int i = 0; i < states.Length; i++) if (IsSceneComponent(states[i])) return states[i];
            return null;
        }

        private static void Subscribe() {
            if (_subscribed) return;
            BakeryEditorBridge.SubscribeFinished(OnBakeryFinished);
            _subscribed = true;
        }

        private static void Unsubscribe() {
            if (!_subscribed) return;
            BakeryEditorBridge.UnsubscribeFinished(OnBakeryFinished);
            _subscribed = false;
        }

        private static void AddSceneObjects<T>(T[] components, HashSet<GameObject> objects) where T : Component {
            for (int i = 0; i < components.Length; i++) if (IsSceneComponent(components[i])) objects.Add(components[i].gameObject);
        }

        private static bool IsSceneComponent(Component component) {
            return component != null && component.gameObject.scene.IsValid() && component.gameObject.scene.isLoaded && !EditorUtility.IsPersistent(component);
        }

        private static void AddTemporaryAsset(DomeMeshLightBakeRestoreState state, string path) {
            List<string> paths = state.TemporaryAssetPaths != null ? new List<string>(state.TemporaryAssetPaths) : new List<string>();
            paths.Add(path);
            state.TemporaryAssetPaths = paths.ToArray();
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
