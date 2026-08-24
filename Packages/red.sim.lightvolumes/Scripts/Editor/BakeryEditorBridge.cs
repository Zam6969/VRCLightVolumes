using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRCLightVolumes {
    // Keeps Bakery optional without a compile-time assembly reference or global scripting define.
    // Bakery is an Asset Store integration rather than a versioned UPM dependency, so its assembly and API are resolved only while its asmdefs are actually present in the project.
    internal static class BakeryEditorBridge {
        private const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags InstanceMethods = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Type BakeryVolumeType = ResolveType("BakeryVolume", "BakeryRuntimeAssembly");
        private static readonly Type BakeryLightMeshType = ResolveType("BakeryLightMesh", "BakeryRuntimeAssembly");
        private static readonly Type BakerySkyLightType = ResolveType("BakerySkyLight", "BakeryRuntimeAssembly");
        private static readonly Type BakeryPointLightType = ResolveType("BakeryPointLight", "BakeryRuntimeAssembly");
        private static readonly Type BakeryDirectLightType = ResolveType("BakeryDirectLight", "BakeryRuntimeAssembly");
        private static readonly Type BakeryGroupType = ResolveType("BakeryLightmapGroup", "BakeryRuntimeAssembly");
        private static readonly Type BakeryStorageType = ResolveType("ftLightmapsStorage", "BakeryRuntimeAssembly");
        private static readonly Type BakeryRendererType = ResolveType("ftRenderLightmap", "BakeryEditorAssembly");
        private static readonly Type BakeryBuildGraphicsType = ResolveType("ftBuildGraphics", "BakeryEditorAssembly");

        private static readonly EventInfo PreFullRenderEvent = BakeryRendererType?.GetEvent("OnPreFullRender", StaticFields);
        private static readonly EventInfo FinishedRenderEvent = BakeryRendererType?.GetEvent("OnFinishedFullRender", StaticFields);
        private static readonly FieldInfo BakeInProgressField = BakeryRendererType?.GetField("bakeInProgress", StaticFields);
        private static readonly FieldInfo UserCanceledField = BakeryRendererType?.GetField("userCanceled", StaticFields);
        private static readonly FieldInfo LightProbeGroupField = BakeryBuildGraphicsType?.GetField("lightProbeLMGroup", StaticFields);
        private static readonly FieldInfo VolumeGroupField = BakeryBuildGraphicsType?.GetField("volumeLMGroup", StaticFields);
        private static readonly FieldInfo ImplicitGroupsField = BakeryStorageType?.GetField("implicitGroups", InstanceFields);

        // Indicates whether Bakery integration is available.
        internal static bool IsAvailable => BakeryVolumeType != null && BakeryRendererType != null;

        // Exposes Bakery's optional component type to editor UI without creating a hard assembly dependency.
        internal static Type BakeryVolumeComponentType => BakeryVolumeType;

        internal static Type BakeryLightMeshComponentType => BakeryLightMeshType;

        internal static Type[] BakeryLightComponentTypes => new[] { BakerySkyLightType, BakeryPointLightType, BakeryDirectLightType, BakeryLightMeshType };

        // Whether this Bakery install can produce a mesh-light shadow mask for the realtime field.
        internal static bool SupportsDomeShadowMask => BakeryVolumeType != null && BakeryLightMeshType != null;

        // Indicates whether this Bakery version exposes the dedicated full-render lifecycle used for safe finalization.
        internal static bool SupportsFullRenderLifecycle => PreFullRenderEvent != null && FinishedRenderEvent != null && BakeInProgressField != null;

        // Checks whether Bakery volumes support full XYZ rotation.
        internal static bool SupportsFullRotation => BakeryVolumeType?.GetField("_rotateAroundXYZ", InstanceFields) != null;

        // Checks whether Bakery volumes support Y-axis rotation.
        internal static bool SupportsYRotation => BakeryVolumeType?.GetField("rotateAroundY", InstanceFields) != null;

        // Whether a Bakery bake operation is currently running.
        internal static bool IsBaking => ReadStaticBool(BakeInProgressField);

        // Whether the last Bakery render was canceled by the user.
        internal static bool WasCanceled => ReadStaticBool(UserCanceledField);

        // Subscribes a callback to Bakery's full-render start event.
        internal static void SubscribePreFullRender(EventHandler callback) {
            SetSubscription(PreFullRenderEvent, callback, true);
        }

        // Unsubscribes a callback from Bakery's full-render start event.
        internal static void UnsubscribePreFullRender(EventHandler callback) {
            SetSubscription(PreFullRenderEvent, callback, false);
        }

        // Subscribes a callback to Bakery's full-render completion event.
        internal static void SubscribeFinished(EventHandler callback) {
            SetSubscription(FinishedRenderEvent, callback, true);
        }

        // Unsubscribes a callback from Bakery's full-render completion event.
        internal static void UnsubscribeFinished(EventHandler callback) {
            SetSubscription(FinishedRenderEvent, callback, false);
        }

        // Synchronizes the Bakery helper component for a light volume.
        internal static void SetupVolume(LightVolumeInstance volume, bool createIfMissing) {
            if (!IsAvailable || volume == null || volume.LightVolumeManager == null) return;

            LightVolumeManager manager = volume.LightVolumeManager;
            if (!TryFindOwnedVolume(volume, out Component bakeryVolume)) return;
            if (manager.EditorIsBakeryMode && volume.Bake && bakeryVolume == null) {
                if (!createIfMissing) return;
                GameObject helper = new GameObject($"Bakery Volume - {volume.gameObject.name}") { tag = "EditorOnly" };
                Undo.RegisterCreatedObjectUndo(helper, "Create Bakery Volume");
                helper.transform.SetParent(volume.transform, false);
                bakeryVolume = helper.AddComponent(BakeryVolumeType);
            } else if ((!manager.EditorIsBakeryMode || !volume.Bake) && bakeryVolume != null) {
                GameObject helper = bakeryVolume.gameObject;
                bool inheritedPrefabObject = PrefabUtility.IsPartOfPrefabInstance(helper) && PrefabUtility.GetCorrespondingObjectFromSource(helper) != null;
                UnityEngine.Object target = inheritedPrefabObject ? bakeryVolume : helper;
                Undo.DestroyObjectImmediate(target);
                return;
            }

            if (!manager.EditorIsBakeryMode || bakeryVolume == null) return;

            bakeryVolume.gameObject.name = $"Bakery Volume - {volume.gameObject.name}";
            bakeryVolume.gameObject.tag = "EditorOnly";
            if ((bakeryVolume.gameObject.hideFlags & HideFlags.HideInHierarchy) == 0) bakeryVolume.gameObject.hideFlags |= HideFlags.HideInHierarchy;
            if ((bakeryVolume.hideFlags & HideFlags.HideInInspector) == 0) bakeryVolume.hideFlags |= HideFlags.HideInInspector;
            if (bakeryVolume.transform.parent != volume.transform) Undo.SetTransformParent(bakeryVolume.transform, volume.transform, "Parent Bakery Volume");
            bakeryVolume.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            bakeryVolume.transform.localScale = Vector3.one;

            SerializedObject serialized = new SerializedObject(bakeryVolume);
            SetBounds(serialized, "bounds", new Bounds(LightVolumeTools.GetPosition(volume), LightVolumeTools.GetScale(volume)));
            SetBool(serialized, "enableBaking", true);
            SetBool(serialized, "denoise", manager.Denoise);
            SetBool(serialized, "adaptiveRes", false);
            SetInt(serialized, "resolutionX", volume.Resolution.x);
            SetInt(serialized, "resolutionY", volume.Resolution.y);
            SetInt(serialized, "resolutionZ", volume.Resolution.z);
            SetEnum(serialized, "encoding", 0);
            if (SupportsFullRotation) {
                SetBool(serialized, "_rotateAroundXYZ", true);
                SetBool(serialized, "rotateAroundY", false);
            } else if (SupportsYRotation) {
                SetBool(serialized, "rotateAroundY", true);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            LVUtils.MarkDirty(bakeryVolume);
        }

        // Imports baked Bakery textures into a light volume when needed.
        internal static bool TryImportTextures(LightVolumeInstance volume) {
            if (!IsAvailable || volume == null || !volume.Bake || !TryFindOwnedVolume(volume, out Component bakeryVolume)) return false;
            if (bakeryVolume == null) return false;

            Texture3D texture0 = ReadTexture3D(bakeryVolume, "bakedTexture0");
            Texture3D texture1 = ReadTexture3D(bakeryVolume, "bakedTexture1");
            Texture3D texture2 = ReadTexture3D(bakeryVolume, "bakedTexture2");
            if (texture0 == null || volume.Texture0 == texture0 && volume.Texture1 == texture1 && volume.Texture2 == texture2) return false;

            volume.Texture0 = texture0;
            volume.Texture1 = texture1;
            volume.Texture2 = texture2;
            LVUtils.MarkDirty(volume);
            return true;
        }

        // Reads the shadow-mask texture produced by an existing Bakery Volume.
        internal static bool TryGetVolumeShadowMask(UnityEngine.Object candidate, out Texture3D shadowMask) {
            shadowMask = null;
            if (BakeryVolumeType == null || candidate == null || !BakeryVolumeType.IsInstanceOfType(candidate)) return false;
            shadowMask = ReadTexture3D((Component)candidate, "bakedMask");
            return shadowMask != null;
        }

        // Reads the directional SH textures produced by a Bakery Volume.
        internal static bool TryGetVolumeLightingTextures(UnityEngine.Object candidate, out Texture3D texture0, out Texture3D texture1, out Texture3D texture2) {
            texture0 = null;
            texture1 = null;
            texture2 = null;
            if (BakeryVolumeType == null || candidate == null || !BakeryVolumeType.IsInstanceOfType(candidate)) return false;
            Component component = candidate as Component;
            if (component == null) return false;
            texture0 = ReadTexture3D(component, "bakedTexture0");
            texture1 = ReadTexture3D(component, "bakedTexture1");
            texture2 = ReadTexture3D(component, "bakedTexture2");
            return texture0 != null;
        }

        // Includes or excludes an optional Bakery Volume from the next bake.
        internal static void SetVolumeBakingEnabled(UnityEngine.Object candidate, bool enabled) {
            if (BakeryVolumeType == null || candidate == null || !BakeryVolumeType.IsInstanceOfType(candidate)) return;
            SerializedObject serialized = new SerializedObject(candidate);
            SetBool(serialized, "enableBaking", enabled);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            LVUtils.MarkDirty(candidate);
        }

        internal static bool TryGetVolumeBakingEnabled(UnityEngine.Object candidate, out bool enabled) {
            enabled = false;
            if (BakeryVolumeType == null || candidate == null || !BakeryVolumeType.IsInstanceOfType(candidate)) return false;
            SerializedObject serialized = new SerializedObject(candidate);
            SerializedProperty property = serialized.FindProperty("enableBaking");
            if (property == null) return false;
            enabled = property.boolValue;
            return true;
        }

        // Starts Bakery's normal full render without introducing a hard dependency on Bakery assemblies.
        internal static bool TryStartFullRender(out string error) {
            error = null;
            if (BakeryRendererType == null) {
                error = "Bakery's editor renderer is not available.";
                return false;
            }
            if (IsBaking) {
                error = "Bakery is already baking.";
                return false;
            }

            try {
                FieldInfo instanceField = BakeryRendererType.GetField("instance", StaticFields);
                object instance = instanceField?.GetValue(null);
                if (instance == null) {
                    if (!typeof(ScriptableObject).IsAssignableFrom(BakeryRendererType)) {
                        error = "This Bakery version does not expose a compatible render window.";
                        return false;
                    }
                    instance = ScriptableObject.CreateInstance(BakeryRendererType);
                    instanceField?.SetValue(null, instance);
                }

                BakeryRendererType.GetMethod("Show", InstanceMethods, null, Type.EmptyTypes, null)?.Invoke(instance, null);
                BakeryRendererType.GetMethod("SaveRenderSettings", InstanceMethods, null, Type.EmptyTypes, null)?.Invoke(instance, null);
                BakeryRendererType.GetMethod("LoadRenderSettings", InstanceMethods, null, Type.EmptyTypes, null)?.Invoke(instance, null);
                MethodInfo renderButton = BakeryRendererType.GetMethod("RenderButton", InstanceMethods, null, new[] { typeof(bool) }, null);
                if (renderButton == null) {
                    error = "This Bakery version does not expose RenderButton(bool).";
                    return false;
                }
                renderButton.Invoke(instance, new object[] { false });
                return true;
            } catch (Exception exception) {
                Exception source = exception is TargetInvocationException invocation && invocation.InnerException != null ? invocation.InnerException : exception;
                error = source.Message;
                return false;
            }
        }

        // Configures the actual mesh as the only Bakery emitter for a dedicated screen-light volume pass.
        internal static bool TrySetupDomeMeshField(Renderer screenRenderer, LightVolumeManager manager, Vector3 center, Vector3 size, Vector3Int resolution, float cutoff, out UnityEngine.Object bakeryVolume, out string error) {
            bakeryVolume = null;
            error = null;
            if (!SupportsDomeShadowMask || screenRenderer == null || manager == null) {
                error = "Assign the combined dome screen renderer and install Bakery first.";
                return false;
            }

            Component lightMesh = screenRenderer.GetComponent(BakeryLightMeshType);
            if (lightMesh == null) lightMesh = screenRenderer.gameObject.AddComponent(BakeryLightMeshType);
            if (lightMesh == null) {
                error = "Bakery Light Mesh could not be added to the temporary screen emitter.";
                return false;
            }

            SerializedObject serializedLightMesh = new SerializedObject(lightMesh);
            SetColor(serializedLightMesh, "color", Color.white);
            SetFloat(serializedLightMesh, "intensity", 1f);
            SetFloat(serializedLightMesh, "cutoff", Mathf.Max(cutoff, 0.1f));
            SetInt(serializedLightMesh, "samples", 256);
            SetInt(serializedLightMesh, "samples2", 32);
            SetBool(serializedLightMesh, "selfShadow", true);
            SetBool(serializedLightMesh, "bakeToIndirect", true);
            SetBool(serializedLightMesh, "shadowmask", false);
            SetFloat(serializedLightMesh, "indirectIntensity", 1f);
            serializedLightMesh.ApplyModifiedPropertiesWithoutUndo();
            LVUtils.MarkDirty(lightMesh);

            const string helperName = "Realtime Mesh Light - Bakery Shadow Volume";
            Transform helperTransform = manager.transform.Find(helperName);
            GameObject helper = helperTransform != null ? helperTransform.gameObject : new GameObject(helperName);
            if (helperTransform == null) helper.transform.SetParent(manager.transform, false);
            Component volume = helper.GetComponent(BakeryVolumeType);
            if (volume == null) volume = helper.AddComponent(BakeryVolumeType);
            if (volume == null) {
                error = "Bakery Volume could not be created for the mesh shadow field.";
                return false;
            }

            SerializedObject serializedVolume = new SerializedObject(volume);
            SetBounds(serializedVolume, "bounds", new Bounds(center, size));
            SetBool(serializedVolume, "enableBaking", true);
            SetBool(serializedVolume, "adaptiveRes", false);
            SetBool(serializedVolume, "denoise", true);
            SetBool(serializedVolume, "isGlobal", false);
            SetInt(serializedVolume, "resolutionX", Mathf.Max(resolution.x, 4));
            SetInt(serializedVolume, "resolutionY", Mathf.Max(resolution.y, 4));
            SetInt(serializedVolume, "resolutionZ", Mathf.Max(resolution.z, 4));
            SetEnum(serializedVolume, "encoding", 0);
            serializedVolume.ApplyModifiedPropertiesWithoutUndo();
            LVUtils.MarkDirty(volume);
            bakeryVolume = volume;
            return true;
        }

        // Finds the baked Bakery Volume that most closely covers the realtime mesh-light field.
        internal static bool TryFindClosestVolumeShadowMask(Vector3 center, Vector3 size, out UnityEngine.Object bakeryVolume, out Texture3D shadowMask) {
            bakeryVolume = null;
            shadowMask = null;
            if (BakeryVolumeType == null) return false;

            Bounds targetBounds = new Bounds(center, size);
            float targetScale = Mathf.Max(size.sqrMagnitude, 0.0001f);
            float bestScore = float.PositiveInfinity;
            UnityEngine.Object[] candidates = Resources.FindObjectsOfTypeAll(BakeryVolumeType);
            for (int i = 0; i < candidates.Length; i++) {
                Component candidate = candidates[i] as Component;
                if (candidate == null || !candidate.gameObject.scene.IsValid()) continue;
                Texture3D candidateMask = ReadTexture3D(candidate, "bakedMask");
                FieldInfo boundsField = candidate.GetType().GetField("bounds", InstanceFields);
                if (candidateMask == null || boundsField == null || !(boundsField.GetValue(candidate) is Bounds candidateBounds) || !candidateBounds.Intersects(targetBounds)) continue;

                float score = (candidateBounds.center - center).sqrMagnitude / targetScale;
                score += (candidateBounds.size - size).sqrMagnitude / targetScale;
                if (score >= bestScore) continue;
                bestScore = score;
                bakeryVolume = candidate;
                shadowMask = candidateMask;
            }
            return bakeryVolume != null && shadowMask != null;
        }

        // Creates the Bakery authoring components needed to render only the screen visibility mask.
        internal static bool TrySetupDomeShadowMask(Renderer screenRenderer, LightVolumeManager manager, Vector3 center, Vector3 size, Vector3Int resolution, float cutoff, out UnityEngine.Object bakeryVolume, out string error) {
            bakeryVolume = null;
            error = null;
            if (!SupportsDomeShadowMask) {
                error = "This Bakery installation does not expose Bakery Light Mesh and Bakery Volume components.";
                return false;
            }
            if (screenRenderer == null || manager == null) {
                error = "Assign the dome screen renderer and Light Volume Manager first.";
                return false;
            }

            Component lightMesh = screenRenderer.GetComponent(BakeryLightMeshType);
            bool createdLightMesh = lightMesh == null;
            if (createdLightMesh) lightMesh = Undo.AddComponent(screenRenderer.gameObject, BakeryLightMeshType);
            if (lightMesh == null) {
                error = "Bakery Light Mesh could not be added to the dome renderer.";
                return false;
            }

            SerializedObject serializedLightMesh = new SerializedObject(lightMesh);
            if (createdLightMesh) {
                SetColor(serializedLightMesh, "color", Color.white);
                SetFloat(serializedLightMesh, "intensity", 1f);
                SetFloat(serializedLightMesh, "cutoff", Mathf.Max(cutoff, 0.1f));
                SetBool(serializedLightMesh, "selfShadow", true);
                SetBool(serializedLightMesh, "bakeToIndirect", false);
                SetBool(serializedLightMesh, "shadowmaskFalloff", false);
            }
            SetBool(serializedLightMesh, "shadowmask", true);
            serializedLightMesh.ApplyModifiedPropertiesWithoutUndo();
            LVUtils.MarkDirty(lightMesh);

            Light channelCarrier = screenRenderer.GetComponent<Light>();
            if (channelCarrier == null) {
                channelCarrier = Undo.AddComponent<Light>(screenRenderer.gameObject);
                channelCarrier.type = LightType.Point;
                channelCarrier.intensity = 0f;
                channelCarrier.range = Mathf.Max(cutoff, 0.1f);
                channelCarrier.cullingMask = 0;
                channelCarrier.shadows = LightShadows.None;
                channelCarrier.bounceIntensity = 0f;
                channelCarrier.lightmapBakeType = LightmapBakeType.Mixed;
                channelCarrier.hideFlags |= HideFlags.HideInInspector;
                EditorUtility.SetDirty(channelCarrier);
            } else if (channelCarrier.lightmapBakeType != LightmapBakeType.Mixed) {
                Undo.RecordObject(channelCarrier, "Prepare Bakery Screen Shadow Channel");
                channelCarrier.lightmapBakeType = LightmapBakeType.Mixed;
                EditorUtility.SetDirty(channelCarrier);
            }

            const string helperName = "Realtime Mesh Light - Bakery Shadow Volume";
            Transform helperTransform = manager.transform.Find(helperName);
            GameObject helper;
            if (helperTransform == null) {
                helper = new GameObject(helperName);
                Undo.RegisterCreatedObjectUndo(helper, "Create Bakery Screen Shadow Volume");
                helper.transform.SetParent(manager.transform, false);
            } else {
                helper = helperTransform.gameObject;
            }

            Component volume = helper.GetComponent(BakeryVolumeType);
            if (volume == null) volume = Undo.AddComponent(helper, BakeryVolumeType);
            if (volume == null) {
                error = "Bakery Volume could not be created for the realtime mesh light.";
                return false;
            }

            SerializedObject serializedVolume = new SerializedObject(volume);
            SetBounds(serializedVolume, "bounds", new Bounds(center, size));
            SetBool(serializedVolume, "enableBaking", true);
            SetBool(serializedVolume, "adaptiveRes", false);
            SetBool(serializedVolume, "denoise", true);
            SetBool(serializedVolume, "isGlobal", false);
            SetInt(serializedVolume, "resolutionX", Mathf.Max(resolution.x, 1));
            SetInt(serializedVolume, "resolutionY", Mathf.Max(resolution.y, 1));
            SetInt(serializedVolume, "resolutionZ", Mathf.Max(resolution.z, 1));
            SetEnum(serializedVolume, "encoding", 0);
            SetEnum(serializedVolume, "shadowmaskEncoding", 0);
            serializedVolume.ApplyModifiedPropertiesWithoutUndo();
            LVUtils.MarkDirty(volume);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            bakeryVolume = volume;
            error = createdLightMesh
                ? "Bakery Light Mesh and a matching Bakery Volume were created. Run Bakery in Shadowmask mode, then return here and import the mask."
                : "The existing Bakery Light Mesh and matching Bakery Volume were configured. Run Bakery in Shadowmask mode, then return here and import the mask.";
            return true;
        }

        // Reads Bakery's channel allocation after a successful shadowmask bake.
        internal static bool TryGetDomeShadowMaskChannel(Renderer screenRenderer, out int channel) {
            channel = -1;
            if (BakeryLightMeshType == null || screenRenderer == null) return false;
            Component lightMesh = screenRenderer.GetComponent(BakeryLightMeshType);
            FieldInfo channelField = lightMesh != null ? lightMesh.GetType().GetField("maskChannel", InstanceFields) : null;
            if (channelField == null || !(channelField.GetValue(lightMesh) is int value) || value < 0 || value > 3) return false;
            channel = value;
            return true;
        }

        // Finds a Bakery helper owned directly by the provided light volume.
        internal static bool TryFindOwnedVolume(LightVolumeInstance volume, out Component bakeryVolume) {
            bakeryVolume = null;
            if (BakeryVolumeType == null || volume == null) return false;

            Component[] candidates = volume.GetComponentsInChildren(BakeryVolumeType, true);
            for (int i = 0; i < candidates.Length; i++) {
                Component candidate = candidates[i];
                if (candidate == null || candidate.transform.parent != volume.transform) continue;
                if (bakeryVolume == null) {
                    bakeryVolume = candidate;
                    continue;
                }

                Debug.LogWarning($"[LightVolumes] Multiple direct Bakery Volume helpers found on {volume.gameObject.name}. Automatic Bakery setup was skipped.", volume);
                bakeryVolume = null;
                return false;
            }
            return true;
        }

        // Clears Bakery implicit probe and volume group references.
        internal static void ClearImplicitProbeGroups() {
            LightProbeGroupField?.SetValue(null, null);
            VolumeGroupField?.SetValue(null, null);
        }

        // Applies runtime volume and probe bitmasks to Bakery state.
        internal static bool TryApplyRuntimeBitmasks(int volumeBitmask, int probeBitmask) {
            object lightProbeGroup = LightProbeGroupField?.GetValue(null);
            object volumeGroup = VolumeGroupField?.GetValue(null);
            if (lightProbeGroup == null && volumeGroup == null) return false;
            SetGroupBitmask(lightProbeGroup, probeBitmask);
            SetGroupBitmask(volumeGroup, volumeBitmask);
            ApplyStoredBitmasks(volumeBitmask, probeBitmask);
            return true;
        }

        // Applies stored volume/probe masks to implicit groups across loaded scenes.
        internal static void ApplyStoredBitmasks(int volumeBitmask, int probeBitmask) {
            if (BakeryStorageType == null || BakeryGroupType == null || ImplicitGroupsField == null) return;

            for (int i = 0; i < SceneManager.sceneCount; i++) {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                GameObject storageObject = FindInScene(scene, "!ftraceLightmaps");
                Component storage = storageObject != null ? storageObject.GetComponent(BakeryStorageType) : null;
                IList groups = storage != null ? ImplicitGroupsField.GetValue(storage) as IList : null;
                if (groups == null) continue;
                for (int j = 0; j < groups.Count; j++) {
                    object group = groups[j];
                    if (group == null || !BakeryGroupType.IsInstanceOfType(group) || !ReadInstanceBool(group, "isImplicit") || !ReadInstanceBool(group, "probes")) continue;
                    string name = group is UnityEngine.Object unityObject ? unityObject.name : string.Empty;
                    SetGroupBitmask(group, name == "volumes" ? volumeBitmask : probeBitmask);
                }
            }
        }

        // Resolves a type from the expected Bakery assembly.
        private static Type ResolveType(string typeName, string assemblyName) {
            Type type = Type.GetType(typeName + ", " + assemblyName, false);
            if (type != null) return type;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++) {
                if (assemblies[i].GetName().Name != assemblyName) continue;
                return assemblies[i].GetType(typeName, false);
            }
            return null;
        }

        // Subscribes or unsubscribes a compatible callback from an optional Bakery lifecycle event.
        private static void SetSubscription(EventInfo eventInfo, EventHandler callback, bool subscribe) {
            if (eventInfo == null || callback == null) return;
            Delegate handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, callback.Target, callback.Method, false);
            if (handler == null) return;
            if (subscribe) eventInfo.AddEventHandler(null, handler);
            else eventInfo.RemoveEventHandler(null, handler);
        }

        // Reads a static boolean field value if available.
        private static bool ReadStaticBool(FieldInfo field) {
            return field != null && field.GetValue(null) is bool value && value;
        }

        // Reads a named boolean instance field if available.
        private static bool ReadInstanceBool(object target, string fieldName) {
            FieldInfo field = target.GetType().GetField(fieldName, InstanceFields);
            return field != null && field.GetValue(target) is bool value && value;
        }

        // Reads a named Texture3D field from a Bakery component.
        private static Texture3D ReadTexture3D(Component component, string fieldName) {
            return component.GetType().GetField(fieldName, InstanceFields)?.GetValue(component) as Texture3D;
        }

        // Sets a Bakery group's bitmask field.
        private static void SetGroupBitmask(object group, int value) {
            group?.GetType().GetField("bitmask", InstanceFields)?.SetValue(group, value);
        }

        // Finds a game object in scene roots and direct children by name.
        private static GameObject FindInScene(Scene scene, string name) {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) {
                GameObject root = roots[i];
                if (root.name == name) return root;
                Transform child = root.transform.Find(name);
                if (child != null) return child.gameObject;
            }
            return null;
        }

        // Writes a boolean serialized property if it exists.
        private static void SetBool(SerializedObject serialized, string name, bool value) {
            SerializedProperty property = serialized.FindProperty(name);
            if (property != null) property.boolValue = value;
        }

        // Writes an int serialized property if it exists.
        private static void SetInt(SerializedObject serialized, string name, int value) {
            SerializedProperty property = serialized.FindProperty(name);
            if (property != null) property.intValue = value;
        }

        // Writes a float serialized property if it exists.
        private static void SetFloat(SerializedObject serialized, string name, float value) {
            SerializedProperty property = serialized.FindProperty(name);
            if (property != null) property.floatValue = value;
        }

        // Writes a color serialized property if it exists.
        private static void SetColor(SerializedObject serialized, string name, Color value) {
            SerializedProperty property = serialized.FindProperty(name);
            if (property != null) property.colorValue = value;
        }

        // Writes an enum serialized property if it exists.
        private static void SetEnum(SerializedObject serialized, string name, int value) {
            SerializedProperty property = serialized.FindProperty(name);
            if (property != null) property.enumValueIndex = value;
        }

        // Writes a bounds serialized property if it exists.
        private static void SetBounds(SerializedObject serialized, string name, Bounds value) {
            SerializedProperty property = serialized.FindProperty(name);
            if (property != null) property.boundsValue = value;
        }
    }
}
