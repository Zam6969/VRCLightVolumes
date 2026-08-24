using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRCLightVolumes {
    internal static class DomeMeshLightShadowBaker {
        private const float MinimumWeight = 0.000001f;

        internal static bool BakeFromColliders(Material[] materials, CustomRenderTexture[] outputs, Matrix4x4 volumeMatrix, int layerMask, float bias, bool includeRenderMeshes) {
            if (!TryReadEmitterData(materials, out Color[] positions, out Color[] normals, out int emitterCount)) {
                EditorUtility.DisplayDialog("Realtime Mesh Light Shadows", "The generated screen emitter data could not be read. Rebuild the realtime mesh light and try again.", "OK");
                return false;
            }

            int width = outputs[0].width;
            int height = outputs[0].height;
            int depth = outputs[0].volumeDepth;
            Color[] pixels = new Color[width * height * depth];
            Material sourceMaterial = materials[0];
            float projectionRange = Mathf.Max(sourceMaterial.GetFloat("_ProjectionRange"), 0.01f);
            float inverseRangeSquared = 1f / (projectionRange * projectionRange);
            float floorBoost = Mathf.Max(sourceMaterial.GetFloat("_FloorLightBoost"), 1f);
            bool previousBackfaceQueries = Physics.queriesHitBackfaces;
            bool canceled = false;
            TemporaryRenderMeshColliders temporaryRenderMeshes = null;

            try {
                if (includeRenderMeshes) temporaryRenderMeshes = TemporaryRenderMeshColliders.Create(layerMask);
                Collider[] colliders = UnityEngine.Object.FindObjectsOfType<Collider>();
                if (colliders.Length == 0) {
                    EditorUtility.DisplayDialog("Realtime Mesh Light Shadows", "No enabled colliders or shadow-casting render meshes were found on the selected layers.", "OK");
                    return false;
                }

                Physics.SyncTransforms();
                Physics.queriesHitBackfaces = true;
                for (int z = 0; z < depth && !canceled; z++) {
                    for (int y = 0; y < height; y++) {
                        float progress = (z * height + y) / (float)(depth * height);
                        if (EditorUtility.DisplayCancelableProgressBar("Baking Realtime Mesh Light Shadows", $"Tracing static visibility {Mathf.RoundToInt(progress * 100f)}%", progress)) {
                            canceled = true;
                            break;
                        }
                        for (int x = 0; x < width; x++) {
                            Vector3 uvw = new Vector3((x + 0.5f) / width, (y + 0.5f) / height, (z + 0.5f) / depth);
                            Vector3 worldPosition = volumeMatrix.MultiplyPoint3x4(uvw - Vector3.one * 0.5f);
                            float totalWeight = 0f;
                            float visibleWeight = 0f;

                            for (int emitterIndex = 0; emitterIndex < emitterCount; emitterIndex++) {
                                Color positionArea = positions[emitterIndex];
                                Vector3 emitterPosition = new Vector3(positionArea.r, positionArea.g, positionArea.b);
                                Vector3 emitterNormal = new Vector3(normals[emitterIndex].r, normals[emitterIndex].g, normals[emitterIndex].b);
                                Vector3 receiverFromEmitter = worldPosition - emitterPosition;
                                float distanceSquared = Mathf.Max(receiverFromEmitter.sqrMagnitude, 0.0001f);
                                float distance = Mathf.Sqrt(distanceSquared);
                                float emitterFacing = Mathf.Clamp01(Vector3.Dot(emitterNormal, receiverFromEmitter) / distance);
                                float rangeMask = Mathf.Clamp01(1f - distanceSquared * inverseRangeSquared);
                                if (emitterFacing <= 0f || rangeMask <= 0f || positionArea.a <= 0f) continue;

                                float sourceRadiusSquared = Mathf.Max(positionArea.a / Mathf.PI, 0.0001f);
                                float floorAmount = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((emitterPosition.y - worldPosition.y) / Mathf.Max(projectionRange * 0.25f, 0.5f)));
                                float weight = Mathf.Lerp(1f, floorBoost, floorAmount) * positionArea.a * emitterFacing / (distanceSquared + sourceRadiusSquared) * rangeMask * rangeMask;
                                totalWeight += weight;
                                if (!IsOccluded(worldPosition, emitterPosition, layerMask, bias)) visibleWeight += weight;
                            }

                            float visibility = totalWeight > MinimumWeight ? Mathf.Clamp01(visibleWeight / totalWeight) : 1f;
                            pixels[x + width * (y + height * z)] = new Color(visibility, visibility, visibility, visibility);
                        }
                    }
                }
            } finally {
                Physics.queriesHitBackfaces = previousBackfaceQueries;
                temporaryRenderMeshes?.Dispose();
                EditorUtility.ClearProgressBar();
            }

            if (canceled) return false;

            Texture3D texture = new Texture3D(width, height, depth, TextureFormat.RGBAHalf, false) {
                name = "DomeMeshLightBakedShadows",
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels(pixels);
            texture.Apply(false, false);

            string materialPath = AssetDatabase.GetAssetPath(materials[0]);
            string folder = string.IsNullOrEmpty(materialPath) ? "Assets/LightVolumesDome" : Path.GetDirectoryName(materialPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder)) folder = "Assets/LightVolumesDome";
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/DomeMeshLightBakedShadows.asset");
            AssetDatabase.CreateAsset(texture, assetPath);
            ApplyMask(materials, outputs, texture, 0, true);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(texture);
            EditorUtility.DisplayDialog("Realtime Mesh Light Shadows", $"Baked static screen shadows into {width} x {height} x {depth} voxels. The changing video colors now use this visibility mask.", "Done");
            return true;
        }

        internal static void ApplyMask(Material[] materials, CustomRenderTexture[] outputs, Texture3D texture, int channel, bool enabled) {
            Vector4 channelVector = GetChannelVector(channel);
            Undo.RecordObjects(materials, "Change Realtime Mesh Light Shadow Mask");
            for (int i = 0; i < materials.Length; i++) {
                materials[i].SetTexture("_BakedOcclusion", texture);
                materials[i].SetVector("_BakedOcclusionChannel", channelVector);
                materials[i].SetFloat("_UseBakedOcclusion", enabled && texture != null ? 1f : 0f);
                EditorUtility.SetDirty(materials[i]);
            }
            Refresh(outputs);
        }

        internal static void SetStrength(Material[] materials, CustomRenderTexture[] outputs, float strength) {
            Undo.RecordObjects(materials, "Change Realtime Mesh Light Shadow Strength");
            for (int i = 0; i < materials.Length; i++) {
                materials[i].SetFloat("_BakedShadowStrength", Mathf.Clamp01(strength));
                EditorUtility.SetDirty(materials[i]);
            }
            Refresh(outputs);
        }

        internal static void SetContrast(Material[] materials, CustomRenderTexture[] outputs, float contrast) {
            Undo.RecordObjects(materials, "Change Realtime Mesh Light Shadow Contrast");
            for (int i = 0; i < materials.Length; i++) {
                materials[i].SetFloat("_BakedShadowContrast", Mathf.Clamp(contrast, 0.5f, 8f));
                EditorUtility.SetDirty(materials[i]);
            }
            Refresh(outputs);
        }

        internal static void SetEnabled(Material[] materials, CustomRenderTexture[] outputs, bool enabled) {
            Undo.RecordObjects(materials, "Toggle Realtime Mesh Light Shadows");
            for (int i = 0; i < materials.Length; i++) {
                bool hasTexture = materials[i].GetTexture("_BakedOcclusion") != null;
                materials[i].SetFloat("_UseBakedOcclusion", enabled && hasTexture ? 1f : 0f);
                EditorUtility.SetDirty(materials[i]);
            }
            Refresh(outputs);
        }

        internal static Vector4 GetChannelVector(int channel) {
            if (channel == 1) return new Vector4(0f, 1f, 0f, 0f);
            if (channel == 2) return new Vector4(0f, 0f, 1f, 0f);
            if (channel == 3) return new Vector4(0f, 0f, 0f, 1f);
            return new Vector4(1f, 0f, 0f, 0f);
        }

        private static bool TryReadEmitterData(Material[] materials, out Color[] positions, out Color[] normals, out int emitterCount) {
            positions = null;
            normals = null;
            emitterCount = 0;
            if (materials == null || materials.Length == 0 || materials[0] == null) return false;
            Texture2D positionTexture = materials[0].GetTexture("_EmitterPositionArea") as Texture2D;
            Texture2D normalTexture = materials[0].GetTexture("_EmitterNormal") as Texture2D;
            if (positionTexture == null || normalTexture == null || !positionTexture.isReadable || !normalTexture.isReadable) return false;
            positions = positionTexture.GetPixels();
            normals = normalTexture.GetPixels();
            emitterCount = Mathf.Min(Mathf.RoundToInt(materials[0].GetFloat("_EmitterCount")), Mathf.Min(positions.Length, normals.Length));
            return emitterCount > 0;
        }

        private static bool IsOccluded(Vector3 receiver, Vector3 emitter, int layerMask, float bias) {
            Vector3 toEmitter = emitter - receiver;
            float distance = toEmitter.magnitude;
            if (distance <= bias * 2f) return false;
            Vector3 direction = toEmitter / distance;
            Vector3 origin = receiver + direction * bias;
            return Physics.Raycast(origin, direction, distance - bias * 2f, layerMask, QueryTriggerInteraction.Ignore);
        }

        private static void Refresh(CustomRenderTexture[] outputs) {
            for (int i = 0; i < outputs.Length; i++) {
                EditorUtility.SetDirty(outputs[i]);
                if (!outputs[i].IsCreated()) {
                    outputs[i].Create();
                    outputs[i].Initialize();
                }
                outputs[i].Update();
            }
            SceneView.RepaintAll();
        }

        private sealed class TemporaryRenderMeshColliders : IDisposable {
            private readonly List<GameObject> _temporaryObjects = new List<GameObject>();
            private readonly List<Mesh> _temporaryMeshes = new List<Mesh>();

            internal static TemporaryRenderMeshColliders Create(int layerMask) {
                TemporaryRenderMeshColliders scope = new TemporaryRenderMeshColliders();
                Renderer[] renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
                for (int i = 0; i < renderers.Length; i++) {
                    Renderer renderer = renderers[i];
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.shadowCastingMode == ShadowCastingMode.Off) continue;
                    if ((layerMask & (1 << renderer.gameObject.layer)) == 0) continue;

                    try {
                        scope.AddRenderer(renderer);
                    } catch (Exception exception) {
                        Debug.LogWarning($"[LightVolumes] Skipped shadow geometry '{renderer.name}': {exception.Message}", renderer);
                    }
                }
                return scope;
            }

            private void AddRenderer(Renderer renderer) {
                Mesh mesh = null;
                MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
                if (meshFilter != null) mesh = meshFilter.sharedMesh;
                if (mesh == null && renderer is SkinnedMeshRenderer skinnedRenderer) {
                    mesh = new Mesh { name = renderer.name + " Shadow Bake Mesh" };
                    _temporaryMeshes.Add(mesh);
                    skinnedRenderer.BakeMesh(mesh);
                }
                if (mesh == null || mesh.vertexCount == 0) return;

                GameObject temporaryObject = new GameObject(renderer.name + " Shadow Bake Collider") {
                    hideFlags = HideFlags.HideAndDontSave,
                    layer = renderer.gameObject.layer
                };
                _temporaryObjects.Add(temporaryObject);
                temporaryObject.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
                temporaryObject.transform.localScale = renderer.transform.lossyScale;
                MeshCollider meshCollider = temporaryObject.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = mesh;
                meshCollider.convex = false;
                meshCollider.isTrigger = false;
            }

            public void Dispose() {
                for (int i = 0; i < _temporaryObjects.Count; i++) {
                    if (_temporaryObjects[i] != null) UnityEngine.Object.DestroyImmediate(_temporaryObjects[i]);
                }
                for (int i = 0; i < _temporaryMeshes.Count; i++) {
                    if (_temporaryMeshes[i] != null) UnityEngine.Object.DestroyImmediate(_temporaryMeshes[i]);
                }
                Physics.SyncTransforms();
            }
        }
    }
}
