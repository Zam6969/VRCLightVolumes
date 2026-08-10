using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

namespace VRCLightVolumes {
    [CanEditMultipleObjects]
    [CustomEditor(typeof(PointLightVolumeInstance))]
    public class PointLightVolumeEditor : UnityEditor.Editor {
        private const string DebugFoldoutSessionKey = "VRCLightVolumes.PointLightVolumeEditor.DebugFoldout";
        private PointLightVolumeInstance PointLightVolume;

        private static readonly GUIContent _bakeShadowsButtonContent = new GUIContent("Bake Shadows", "Bakes or re-bakes shadow maps for all selected lights with Shadows enabled.");
        private static readonly GUIContent _clearShadowsButtonContent = new GUIContent("Clear Shadows", "Removes the assigned shadow maps from all selected lights without deleting their source assets.");
        private static readonly GUIContent _emptyContent = GUIContent.none;
        private static readonly string _textureMaterialHint = "None (Texture/Material)";
        private static readonly string _cubemapMaterialHint = "None (Texture/Material)";
        private static readonly string _projectionSourceObjectPickerFilter = "t:Texture t:Material";
        private static readonly string[] _lightTypeNames = { "Point Light", "Spot Light", "Area Light" };
        private static readonly string[] _projectionNames = { "Parametric", "LUT", "Custom" };
        private const float ObjectSelectorButtonWidth = 19f;
        private const float InspectorSectionSpacing = 10f;
        private const float ShadowGroupSpacing = 6f;
        private const float ShadowButtonSpacing = 6f;
        private const float ShapePickerSize = 48f;
        private const float ShapePickerRectangleWidth = 42f;
        private const float ShapePickerSpacing = 6f;
        private const float AreaCookiePreviewAspect = 1920f / 1080f;
        private const float AreaCookiePreviewMinHeight = 96f;
        private const float AreaCookiePreviewMaxHeight = 280f;
        private const float AreaCookieCropMinSize = 0.001f;
        private const float AreaCookieCropMinPreviewPixels = 1f;
        private const int AreaCookieCustomTriangleShape = 5;
        private const float AreaCookieTrianglePointRadius = 4f;
        private static readonly Color _shadowClipVisibleColor = new Color(0.2f, 0.65f, 1f, 0.75f);
        private static readonly Color _shadowClipHiddenColor = new Color(0.2f, 0.65f, 1f, 0.18f);
        private static readonly Color _areaCookiePreviewBackgroundColor = new Color(0.04f, 0.04f, 0.04f, 1f);
        private static readonly Color _areaCookiePreviewBorderColor = new Color(0f, 0f, 0f, 0.65f);
        private static readonly Color _areaCookieCropFillColor = new Color(1f, 0.84f, 0.12f, 0.16f);
        private static readonly Color _areaCookieCropBorderColor = new Color(1f, 0.84f, 0.12f, 0.95f);
        private static readonly Color _shapePickerFillColor = new Color(1f, 0.84f, 0.12f, 0.28f);
        private static readonly Color _shapePickerHoverColor = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color _shapePickerBorderColor = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color _shapePickerGuideColor = new Color(1f, 1f, 1f, 0.22f);
        private static GUIStyle _projectionSourceHintStyle;
        private Vector2 _areaCookieCropDragStart;
        private readonly Vector2[] _areaCookieTrianglePickPoints = new Vector2[3];
        private int _areaCookieTrianglePickCount;
        private bool _isDraggingAreaCookieCrop;
        private bool _isPickingAreaCookieTriangle;
        private bool _areaCookieCropHasDragged;
        private bool _debugExpanded;

        // Caches the inspected light and restores its live debug foldout state.
        private void OnEnable() {
            PointLightVolume = target as PointLightVolumeInstance;
            _debugExpanded = SessionState.GetBool(DebugFoldoutSessionKey, false);
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        // Removes the Undo callback owned by this inspector.
        private void OnDisable() {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        }

        // Draws type-specific authoring controls and synchronizes explicit changes.
        public override void OnInspectorGUI() {
            serializedObject.Update();
            int undoGroup = Undo.GetCurrentGroup();
            SerializedProperty lightTypeProperty = serializedObject.FindProperty("LightType");
            SerializedProperty projectionProperty = serializedObject.FindProperty("Projection");
            int lightType = Mathf.Clamp(lightTypeProperty.intValue, 0, 2);
            int projection = Mathf.Clamp(projectionProperty.intValue, 0, 2);
            DrawSectionHeader("Light", false);
            DrawPopup(lightTypeProperty, new GUIContent("Type", lightTypeProperty.tooltip), _lightTypeNames);
            lightType = Mathf.Clamp(lightTypeProperty.intValue, 0, 2);
            DrawProperty("IsDynamic", "Dynamic");
            DrawProperty("Color");
            DrawProperty("Intensity");
            DrawProperty("ShadingStrength", "Shading Strength");
            DrawProperty("BakeIntoProbes", "Bake Into Probes");
            DrawProperty("DebugRange", "Debug Range");

            DrawSectionHeader("Projection", true);
            if (lightType != 2) {
                DrawPopup(projectionProperty, new GUIContent("Projection", projectionProperty.tooltip), _projectionNames);
                projection = Mathf.Clamp(projectionProperty.intValue, 0, 2);
                if (projection == 1) DrawProperty("Range");
                else DrawProperty("LightSourceSize", "Light Source Size");
            }
            if (lightType == 1) DrawAngleDegrees();
            if (lightType == 1 && projection == 0) DrawProperty("Falloff");
            if (lightType == 1 && projection == 2) DrawProperty("SpotCookieAspect", "Spot Cookie Aspect");
            DrawActiveProjectionSourceField(lightType, projection);

            DrawSectionHeader("Shadows", true);
            SerializedProperty shadowsProperty = serializedObject.FindProperty("Shadows");
            EditorGUILayout.PropertyField(shadowsProperty, new GUIContent("Enabled", shadowsProperty.tooltip));
            bool drawShadowFields = shadowsProperty.hasMultipleDifferentValues || shadowsProperty.boolValue;
            bool propertiesChanged = serializedObject.ApplyModifiedProperties();

            if (drawShadowFields) {
                DrawProperty("WorldSpaceShadows", "Use World Space");
                GUILayout.Space(ShadowGroupSpacing);
                DrawLayerMask();
                DrawProperty("ExclusionMask", "Exclusion Mask");

                GUILayout.Space(ShadowGroupSpacing);
                DrawProperty("NearClip", "Near Plane");
                DrawProperty("FarClip", "Far Plane");
                if (lightType == 1) DrawProperty("ForceCubemapShadows", "Force Cubemap Shadows");
                DrawProperty("DebugClipPlanes", "Debug Clip Planes");

                GUILayout.Space(ShadowGroupSpacing);
                DrawProperty("Bias");
                DrawProperty("Blur");
                DrawProperty("ContactHardening", "Contact Hardening");

                GUILayout.Space(ShadowGroupSpacing);
                DrawTextureMaterialField("ShadowMap", _cubemapMaterialHint, true);

                DrawSectionHeader("Shadow Baking", true);
                DrawProperty("BakeInGame", "Bake In Game");
                DrawProperty("RebakeShadows", "Rebake Shadows");

                SerializedProperty shadowMapProperty = serializedObject.FindProperty("ShadowMap");
                GUILayout.Space(ShadowButtonSpacing);
                using (new EditorGUILayout.HorizontalScope()) {
                    if (GUILayout.Button(_bakeShadowsButtonContent)) {
                        propertiesChanged |= serializedObject.ApplyModifiedProperties();
                        BakeSelectedShadowMaps();
                        serializedObject.Update();
                        shadowMapProperty = serializedObject.FindProperty("ShadowMap");
                    }
                    using (new EditorGUI.DisabledScope(!shadowMapProperty.hasMultipleDifferentValues && shadowMapProperty.objectReferenceValue == null)) {
                        if (GUILayout.Button(_clearShadowsButtonContent)) shadowMapProperty.objectReferenceValue = null;
                    }
                }
            }

            DrawDebugSection();
            propertiesChanged |= serializedObject.ApplyModifiedProperties();
            if (!propertiesChanged) return;

            SyncTargets(true);
            Undo.CollapseUndoOperations(undoGroup);
        }

        // Draws a bold inspector section title with optional leading spacing.
        private static void DrawSectionHeader(string title, bool addTopSpacing) {
            if (addTopSpacing) GUILayout.Space(InspectorSectionSpacing);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        // Draws resolved light, projection, shadow and runtime-bake diagnostics.
        private void DrawDebugSection() {
            GUILayout.Space(InspectorSectionSpacing);
            EditorGUI.BeginChangeCheck();
            _debugExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(_debugExpanded, new GUIContent("Debug", "Shows read-only live Point Light Volume data for troubleshooting."));
            if (EditorGUI.EndChangeCheck()) SessionState.SetBool(DebugFoldoutSessionKey, _debugExpanded);

            if (_debugExpanded && PointLightVolume != null) {
                if (!EditorApplication.isPlaying) EditorGUILayout.HelpBox("Live values are populated in Play Mode. Resolved light, projection and shadow values show the current editor state.", MessageType.Info);
                if (targets.Length > 1) EditorGUILayout.HelpBox("Debug values are shown for the first selected Point Light Volume.", MessageType.Info);

                LightVolumeDebugGUI.DrawGroupHeader("Registration", false, "Shows which Manager owns this light and its registry priority.");
                LightVolumeDebugGUI.DrawObject(serializedObject, nameof(PointLightVolumeInstance.LightVolumeManager), PointLightVolume.LightVolumeManager, typeof(LightVolumeManager), "Manager");
                LightVolumeDebugGUI.DrawBool("Registered", PointLightVolume.RegisteredWithManagerPreview, "Whether this light is currently in a Manager registry.");
                LightVolumeDebugGUI.DrawBool("Active", PointLightVolume.IsActive, "Whether this light is currently eligible for rendering.");
                LightVolumeDebugGUI.DrawInt(serializedObject, nameof(PointLightVolumeInstance.RegistryOrder), PointLightVolume.RegistryOrder);
                LightVolumeDebugGUI.DrawFloat(serializedObject, nameof(PointLightVolumeInstance.RegistryWeight), PointLightVolume.RegistryWeight);

                LightVolumeDebugGUI.DrawGroupHeader("Resolved Light Data", true, "Values calculated from the Transform and light settings for shaders.");
                LightVolumeDebugGUI.DrawVector3(serializedObject, nameof(PointLightVolumeInstance.Position), PointLightVolume.Position);
                if (PointLightVolume.LightType != 2) LightVolumeDebugGUI.DrawVector3(serializedObject, nameof(PointLightVolumeInstance.Direction), PointLightVolume.Direction);
                LightVolumeDebugGUI.DrawQuaternion(serializedObject, nameof(PointLightVolumeInstance.Rotation), PointLightVolume.Rotation);
                LightVolumeDebugGUI.DrawFloat(serializedObject, nameof(PointLightVolumeInstance.SquaredRange), PointLightVolume.SquaredRange);
                LightVolumeDebugGUI.DrawFloat(serializedObject, nameof(PointLightVolumeInstance.SquaredScale), PointLightVolume.SquaredScale);
                LightVolumeDebugGUI.DrawBool("Range Dirty", PointLightVolume.IsRangeDirty, "Whether the Manager still needs to recalculate the effective range.");

                LightVolumeDebugGUI.DrawGroupHeader("Resolved Projection", true, "Resolved runtime source and layout for this light's projection.");
                LightVolumeDebugGUI.DrawText(serializedObject, nameof(PointLightVolumeInstance.ProjectionMode), GetProjectionModeName(PointLightVolume.ProjectionMode));
                LightVolumeDebugGUI.DrawText(serializedObject, nameof(PointLightVolumeInstance.ProjectionType), GetSourceTypeName(PointLightVolume.ProjectionType), "Source Type");
                LightVolumeDebugGUI.DrawObject(serializedObject, nameof(PointLightVolumeInstance.CustomTexture), PointLightVolume.CustomTexture, typeof(Texture), "Texture");
                LightVolumeDebugGUI.DrawObject(serializedObject, nameof(PointLightVolumeInstance.CustomTextureMaterial), PointLightVolume.CustomTextureMaterial, typeof(Material), "Material");
                LightVolumeDebugGUI.DrawBool("Cubemap Source", PointLightVolume.CustomTextureIsCubemap, "Whether the resolved texture is a cubemap.");
                LightVolumeDebugGUI.DrawBool("Depth Slices", PointLightVolume.CustomTextureHasDepthSlices, "Whether the resolved texture already contains array slices.");
                LightVolumeDebugGUI.DrawBool(serializedObject, nameof(PointLightVolumeInstance.AutoUpdateCustomTexture), PointLightVolume.AutoUpdateCustomTexture, "Dynamic Source");

                if (PointLightVolume.LightType == 2) {
                    LightVolumeDebugGUI.DrawGroupHeader("Area Cookie", true, "Live fallback color and GPU readback state for an Area Light cookie.");
                    LightVolumeDebugGUI.DrawText("Fallback Color", "#" + ColorUtility.ToHtmlStringRGBA(PointLightVolume.AreaLightFallbackColor), "Average cookie color used before detailed projection data is ready.");
                    LightVolumeDebugGUI.DrawFloat("Mirror", PointLightVolume.AreaCookieMirror, "Sign used to keep the Area Light cookie orientation correct.");
                    LightVolumeDebugGUI.DrawInt("Average Custom ID", PointLightVolume.AreaCookieAverageCustomId, "Runtime cookie-array source used for average-color readback.");
                    LightVolumeDebugGUI.DrawBool("Readback Pending", PointLightVolume.AreaCookieAverageReadbackPending, "Whether an average-color GPU readback is currently pending.");
                    LightVolumeDebugGUI.DrawBool("Readback Dirty", PointLightVolume.AreaCookieAverageReadbackDirty, "Whether the cookie average must be read again.");
                }

                if (PointLightVolume.Shadows) {
                    LightVolumeDebugGUI.DrawGroupHeader("Resolved Shadows", true, "Resolved shadow source and bake pose used by shaders.");
                    LightVolumeDebugGUI.DrawObject(serializedObject, nameof(PointLightVolumeInstance.ShadowMapTexture), PointLightVolume.ShadowMapTexture, typeof(Texture), "Texture");
                    LightVolumeDebugGUI.DrawObject(serializedObject, nameof(PointLightVolumeInstance.ShadowMapMaterial), PointLightVolume.ShadowMapMaterial, typeof(Material), "Material");
                    LightVolumeDebugGUI.DrawFloat(serializedObject, nameof(PointLightVolumeInstance.ShadowMapID), PointLightVolume.ShadowMapID);
                    LightVolumeDebugGUI.DrawBool("Uses Cubemap", PointLightVolume.ShadowMapUsesCubemap, "Whether this light samples a six-face shadow.");
                    LightVolumeDebugGUI.DrawBool("Cubemap Source", PointLightVolume.ShadowMapTextureIsCubemap, "Whether the assigned shadow texture is a cubemap.");
                    LightVolumeDebugGUI.DrawBool("Depth Slices", PointLightVolume.ShadowMapTextureHasDepthSlices, "Whether the assigned texture already contains array slices.");
                    LightVolumeDebugGUI.DrawBool(serializedObject, nameof(PointLightVolumeInstance.AutoUpdateShadowMap), PointLightVolume.AutoUpdateShadowMap, "Dynamic Source");
                    LightVolumeDebugGUI.DrawFloat("Baked Far Clip", PointLightVolume.BakedFarClip, "Far clipping plane used to encode the current shadow map.");
                    LightVolumeDebugGUI.DrawVector3(serializedObject, nameof(PointLightVolumeInstance.ShadowBakePosition), PointLightVolume.ShadowBakePosition, "Bake Position");
                    LightVolumeDebugGUI.DrawQuaternion(serializedObject, nameof(PointLightVolumeInstance.ShadowBakeRotation), PointLightVolume.ShadowBakeRotation, "Bake Rotation");
                }

                if (PointLightVolume.BakeInGame) {
                    LightVolumeDebugGUI.DrawGroupHeader("Runtime Shadow Baking", true, "Live state and temporary resources used while baking shadows in-game.");
                    LightVolumeDebugGUI.DrawBool("Bake Started", PointLightVolume.RuntimeShadowBakeStartedPreview, "Whether this light has started its runtime shadow bake.");
                    LightVolumeDebugGUI.DrawBool("Source Initialized", PointLightVolume.RuntimeShadowSourceInitializedPreview, "Whether the runtime shadow source is ready for the Manager.");
                    LightVolumeDebugGUI.DrawInt("Current Face", PointLightVolume.RuntimeShadowFaceIndexPreview, "Next cubemap face to render; non-cubemap shadows use one face.");
                    LightVolumeDebugGUI.DrawFloat("Receiver Near Plane", PointLightVolume.RuntimeShadowReceiverNearClipPreview, "Near clipping plane used by the runtime shadow receiver.");
                    LightVolumeDebugGUI.DrawFloat("Receiver Far Plane", PointLightVolume.RuntimeShadowReceiverFarClipPreview, "Far clipping plane used by the runtime shadow receiver.");
                    LightVolumeDebugGUI.DrawObject("Depth Texture", PointLightVolume.RuntimeShadowDepthTexturePreview, typeof(RenderTexture), "Temporary camera-depth render target.");
                    LightVolumeDebugGUI.DrawObject("Output Texture", PointLightVolume.RuntimeShadowTexturePreview, typeof(RenderTexture), "Runtime shadow result generated by this light.");
                    LightVolumeDebugGUI.DrawObject("Registered Texture", PointLightVolume.RuntimeShadowRegistrationTexturePreview, typeof(RenderTexture), "Texture currently registered in the Manager's shadow array.");
                    LightVolumeDebugGUI.DrawObject("Depth Material", PointLightVolume.RuntimeShadowDepthEncodeMaterial, typeof(Material), "Material that converts camera depth into shadow data.");
                    LightVolumeDebugGUI.DrawObject("Blur Material", PointLightVolume.RuntimeShadowBlurMaterial, typeof(Material), "Material that filters the runtime shadow result.");
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        // Converts a packed runtime projection mode to a readable inspector label.
        private static string GetProjectionModeName(int value) {
            if (value == 1) return "LUT";
            if (value == 2) return "Custom";
            return "Parametric";
        }

        // Converts a packed projection source type to a readable inspector label.
        private static string GetSourceTypeName(int value) {
            if (value == 1) return "Texture";
            if (value == 2) return "Material";
            return "None";
        }

        // Draws a serialized field while preserving its field-level tooltip.
        private void DrawProperty(string propertyName, string label = null) {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (label == null) EditorGUILayout.PropertyField(property, true);
            else EditorGUILayout.PropertyField(property, new GUIContent(label, property.tooltip), true);
        }

        // Draws an integer-backed popup with correct mixed-selection handling.
        private static void DrawPopup(SerializedProperty property, GUIContent label, string[] names) {
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            int value = EditorGUILayout.Popup(label, Mathf.Clamp(property.intValue, 0, names.Length - 1), names);
            if (EditorGUI.EndChangeCheck()) property.intValue = value;
            EditorGUI.showMixedValue = false;
        }

        // Draws a visual rectangle/corner-triangle picker for Area Light and crop shapes.
        private void DrawShapePicker(SerializedProperty property, GUIContent label) {
            Rect rowRect = EditorGUILayout.GetControlRect(false, ShapePickerSize);
            EditorGUI.BeginProperty(rowRect, label, property);
            Rect fieldRect = EditorGUI.PrefixLabel(rowRect, label);
            float availableWidth = Mathf.Max(fieldRect.width, ShapePickerRectangleWidth + ShapePickerSpacing + ShapePickerSize);
            Rect rectangleRect = new Rect(fieldRect.x, rowRect.y, Mathf.Min(ShapePickerRectangleWidth, availableWidth), ShapePickerSize);
            Rect cornerRect = new Rect(rectangleRect.xMax + ShapePickerSpacing, rowRect.y, ShapePickerSize, ShapePickerSize);
            if (cornerRect.xMax > fieldRect.xMax) {
                cornerRect.x = Mathf.Max(fieldRect.x, fieldRect.xMax - ShapePickerSize);
                rectangleRect.x = Mathf.Max(fieldRect.x, cornerRect.x - ShapePickerSpacing - ShapePickerRectangleWidth);
            }

            HandleShapePickerInput(rectangleRect, cornerRect, property);
            if (Event.current.type == EventType.Repaint) {
                int selectedShape = property.hasMultipleDifferentValues ? -1 : property.intValue;
                DrawShapePickerVisuals(rectangleRect, cornerRect, selectedShape > 4 ? -1 : Mathf.Clamp(selectedShape, 0, 4));
            }
            EditorGUI.EndProperty();
        }

        // Turns clicks on the visual picker into the serialized shape value.
        private void HandleShapePickerInput(Rect rectangleRect, Rect cornerRect, SerializedProperty property) {
            Event currentEvent = Event.current;
            if (currentEvent.type != EventType.MouseDown || currentEvent.button != 0) return;

            int newShape = -1;
            if (rectangleRect.Contains(currentEvent.mousePosition)) newShape = 0;
            else if (cornerRect.Contains(currentEvent.mousePosition)) newShape = GetShapeFromPickerPoint(cornerRect, currentEvent.mousePosition);
            if (newShape < 0) return;

            property.intValue = newShape;
            GUI.changed = true;
            currentEvent.Use();
        }

        // Maps each clicked corner of the picker square to its matching triangle shape.
        private int GetShapeFromPickerPoint(Rect rect, Vector2 point) {
            bool right = point.x >= rect.center.x;
            bool lower = point.y >= rect.center.y;
            if (!right && lower) return 1;
            if (right && lower) return 2;
            if (!right) return 3;
            return 4;
        }

        // Draws the icon-only shape picker.
        private void DrawShapePickerVisuals(Rect rectangleRect, Rect cornerRect, int selectedShape) {
            Vector2 mousePosition = Event.current.mousePosition;
            int hoverShape = rectangleRect.Contains(mousePosition) ? 0 : cornerRect.Contains(mousePosition) ? GetShapeFromPickerPoint(cornerRect, mousePosition) : -1;

            DrawShapePickerFrame(rectangleRect, selectedShape == 0, hoverShape == 0);
            Rect rectangleIcon = new Rect(rectangleRect.x + 9f, rectangleRect.y + 13f, rectangleRect.width - 18f, rectangleRect.height - 26f);
            EditorGUI.DrawRect(rectangleIcon, selectedShape == 0 ? _shapePickerFillColor : new Color(1f, 1f, 1f, 0.08f));
            DrawRectOutline(rectangleIcon, _shapePickerGuideColor, 1f);

            DrawShapePickerFrame(cornerRect, selectedShape > 0, hoverShape > 0);
            Handles.BeginGUI();
            if (selectedShape > 0) {
                Handles.color = _shapePickerFillColor;
                Handles.DrawAAConvexPolygon(GetAreaCookieShapeOverlayPoints(cornerRect, selectedShape, 0f));
            }
            if (hoverShape > 0 && hoverShape != selectedShape) {
                Handles.color = _shapePickerHoverColor;
                Handles.DrawAAConvexPolygon(GetAreaCookieShapeOverlayPoints(cornerRect, hoverShape, 0f));
            }
            Handles.color = _shapePickerGuideColor;
            Handles.DrawAAPolyLine(1f, new Vector3(cornerRect.xMin, cornerRect.yMax, 0f), new Vector3(cornerRect.center.x, cornerRect.center.y, 0f), new Vector3(cornerRect.xMax, cornerRect.yMin, 0f));
            Handles.DrawAAPolyLine(1f, new Vector3(cornerRect.xMin, cornerRect.yMin, 0f), new Vector3(cornerRect.center.x, cornerRect.center.y, 0f), new Vector3(cornerRect.xMax, cornerRect.yMax, 0f));
            if (selectedShape > 0) {
                Handles.color = _areaCookieCropBorderColor;
                Handles.DrawAAPolyLine(2f, CloseAreaCookiePolygon(GetAreaCookieShapeOverlayPoints(cornerRect, selectedShape, 0f)));
            }
            Handles.EndGUI();
        }

        // Draws the shared selectable picker frame.
        private void DrawShapePickerFrame(Rect rect, bool selected, bool hovered) {
            Color background = selected ? new Color(1f, 0.84f, 0.12f, 0.10f) : hovered ? _shapePickerHoverColor : new Color(1f, 1f, 1f, 0.04f);
            EditorGUI.DrawRect(rect, background);
            DrawRectOutline(rect, selected ? _areaCookieCropBorderColor : _shapePickerBorderColor, selected ? 2f : 1f);
        }

        // Presents the runtime half-angle radians field as a full cone angle in degrees.
        private void DrawAngleDegrees() {
            SerializedProperty angleProperty = serializedObject.FindProperty("Angle");
            float angleDegrees = angleProperty.floatValue * Mathf.Rad2Deg * 2f;
            EditorGUI.showMixedValue = angleProperty.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            angleDegrees = EditorGUILayout.Slider(new GUIContent("Angle", "Angle of a spotlight cone in degrees."), angleDegrees, 0.1f, 360f);
            if (EditorGUI.EndChangeCheck()) angleProperty.floatValue = angleDegrees * Mathf.Deg2Rad * 0.5f;
            EditorGUI.showMixedValue = false;
        }

        // Draws the serialized shadow layer mask using Unity's named layers.
        private void DrawLayerMask() {
            SerializedProperty layerMaskProperty = serializedObject.FindProperty("LayerMask");
            EditorGUI.showMixedValue = layerMaskProperty.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            int value = EditorGUILayout.MaskField(new GUIContent("Layer Mask", layerMaskProperty.tooltip), layerMaskProperty.intValue, InternalEditorUtility.layers);
            if (EditorGUI.EndChangeCheck()) layerMaskProperty.intValue = value;
            EditorGUI.showMixedValue = false;
        }

        // Bakes selected lights and rebuilds the primary Manager's shadow array once.
        private void BakeSelectedShadowMaps() {
            LightVolumeManager manager = LightVolumeManagerEditorBackend.GetPrimaryManager();
            bool rebuildShadowTextures = false;
            bool synchronized = false;
            for (int i = 0; i < targets.Length; i++) {
                PointLightVolumeInstance pointLightVolume = targets[i] as PointLightVolumeInstance;
                if (pointLightVolume == null || !pointLightVolume.Shadows) continue;
                PointLightVolumeEditorUtility.Sync(pointLightVolume, false, false);
                if (manager != null && pointLightVolume.LightVolumeManager == manager) synchronized = true;
                if (!PointLightShadowBaker.BakeShadowMap(pointLightVolume, $"| {pointLightVolume.gameObject.name} ({i + 1}/{targets.Length})", false)) continue;
                PointLightVolumeEditorUtility.Sync(pointLightVolume, false, false);
                if (manager != null && pointLightVolume.LightVolumeManager == manager) rebuildShadowTextures = true;
            }
            if (rebuildShadowTextures) LightVolumeManagerEditorBackend.ReinitializeShadowTextures(manager);
            else if (synchronized) LightVolumeManagerEditorBackend.RefreshManagerOnce(manager, true);
        }

        // Applies all selected proxies first, then rebuilds each shared array at most once.
        private void SyncTargets(bool recordUndo, bool reinitializeTextures = false, bool refreshRuntimeImmediately = true) {
            LightVolumeManager manager = LightVolumeManagerEditorBackend.GetPrimaryManager();
            int managerChanges = 0;
            bool refreshManager = false;
            for (int i = 0; i < targets.Length; i++) {
                PointLightVolumeInstance pointLightVolume = targets[i] as PointLightVolumeInstance;
                if (pointLightVolume == null) continue;
                int changes = PointLightVolumeEditorUtility.Sync(pointLightVolume, recordUndo, false);
                if (reinitializeTextures) changes |= PointLightVolumeEditorUtility.CustomTexturesChanged | PointLightVolumeEditorUtility.ShadowTexturesChanged;
                if (manager == null || pointLightVolume.LightVolumeManager != manager) continue;
                refreshManager = true;
                managerChanges |= changes;
            }

            if (managerChanges != 0) {
                if (recordUndo) Undo.RecordObject(manager, "Sync Point Light Volume Textures");
                LightVolumeManagerEditorBackend.ReinitializeTextures(manager, (managerChanges & PointLightVolumeEditorUtility.CustomTexturesChanged) != 0, (managerChanges & PointLightVolumeEditorUtility.ShadowTexturesChanged) != 0);
            } else if (refreshManager) {
                LightVolumeManagerEditorBackend.RefreshManagerOnce(manager, refreshRuntimeImmediately);
            }
        }

        // Rebuilds derived data and both texture arrays after an Undo or Redo operation.
        private void OnUndoRedoPerformed() {
            // Undo also restores hidden derived source fields, which makes ordinary source-change detection intentionally inconclusive. Rebuild both shared arrays once per manager.
            SyncTargets(false, true, false);
            Repaint();
        }

        // Draws only the texture or material source relevant to the selected projection mode.
        private void DrawActiveProjectionSourceField(int lightType, int projection) {
            if (lightType == 2) {
                DrawAreaCookieCropControls();
                return;
            }
            if (projection == 0) return;
            if (projection == 1) DrawTextureMaterialField("FalloffLUT", _textureMaterialHint, false);
            else if (lightType == 0) DrawTextureMaterialField("Cubemap", _cubemapMaterialHint, false);
            else if (lightType == 1) DrawTextureMaterialField("Cookie", _textureMaterialHint, false);
        }

        // Draws the Area Light cookie source, shape selector, normalized crop field and 16:9 visual picker.
        private void DrawAreaCookieCropControls() {
            SerializedProperty areaShapeProperty = serializedObject.FindProperty("AreaLightShape");
            if (areaShapeProperty != null) DrawShapePicker(areaShapeProperty, new GUIContent("Area Shape", areaShapeProperty.tooltip));

            DrawTextureMaterialField("Cookie", _textureMaterialHint, false);

            SerializedProperty previewProperty = serializedObject.FindProperty("AreaCookieCropPreview");
            if (previewProperty != null) EditorGUILayout.PropertyField(previewProperty, new GUIContent("Crop Preview", previewProperty.tooltip));

            SerializedProperty shapeProperty = serializedObject.FindProperty("AreaCookieCropShape");
            if (shapeProperty != null) DrawShapePicker(shapeProperty, new GUIContent("Crop Shape", shapeProperty.tooltip));

            SerializedProperty rotationProperty = serializedObject.FindProperty("AreaCookieCropRotation");
            if (rotationProperty != null) {
                using (new EditorGUILayout.HorizontalScope()) {
                    EditorGUILayout.Slider(rotationProperty, -180f, 180f, new GUIContent("Crop Rotation", rotationProperty.tooltip));
                    if (GUILayout.Button("Reset", GUILayout.Width(48f))) rotationProperty.floatValue = 0f;
                }
                if (!rotationProperty.hasMultipleDifferentValues) rotationProperty.floatValue = GetSafeAreaCookieCropRotation(rotationProperty.floatValue);
            }

            SerializedProperty cropProperty = serializedObject.FindProperty("AreaCookieCrop");
            SerializedProperty triangleAProperty = serializedObject.FindProperty("AreaCookieCropTriangleA");
            SerializedProperty triangleBProperty = serializedObject.FindProperty("AreaCookieCropTriangleB");
            SerializedProperty triangleCProperty = serializedObject.FindProperty("AreaCookieCropTriangleC");
            DrawAreaCookieTrianglePickerButton(shapeProperty);

            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.PropertyField(cropProperty, new GUIContent("Crop", cropProperty.tooltip));
                if (GUILayout.Button("Full", GUILayout.Width(44f))) cropProperty.vector4Value = new Vector4(0f, 0f, 1f, 1f);
            }

            UnityEngine.Object previewSource = previewProperty != null ? previewProperty.objectReferenceValue : null;
            UnityEngine.Object cookieSource = serializedObject.FindProperty("Cookie").objectReferenceValue;
            DrawAreaCookieCropPreview(cropProperty, shapeProperty, rotationProperty, triangleAProperty, triangleBProperty, triangleCProperty, previewSource, cookieSource);
        }

        // Draws the triangle-picking command row.
        private void DrawAreaCookieTrianglePickerButton(SerializedProperty shapeProperty) {
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.Space(EditorGUIUtility.labelWidth);
                bool nextPicking = GUILayout.Toggle(_isPickingAreaCookieTriangle, "Pick Triangle", EditorStyles.miniButton, GUILayout.Width(110f));
                if (nextPicking != _isPickingAreaCookieTriangle) {
                    _isPickingAreaCookieTriangle = nextPicking;
                    _areaCookieTrianglePickCount = 0;
                    _isDraggingAreaCookieCrop = false;
                    GUIUtility.hotControl = 0;
                    Repaint();
                }
                using (new EditorGUI.DisabledScope(!_isPickingAreaCookieTriangle)) {
                    GUILayout.Label(_areaCookieTrianglePickCount + "/3", GUILayout.Width(28f));
                }
                if (shapeProperty != null && !shapeProperty.hasMultipleDifferentValues && shapeProperty.intValue == AreaCookieCustomTriangleShape)
                    GUILayout.Label("Custom", GUILayout.Width(54f));
            }
        }

        // Draws the assigned guide image, cookie image, or a blank 1920x1080 canvas, then applies drag or point-click selections to AreaCookieCrop.
        private void DrawAreaCookieCropPreview(SerializedProperty cropProperty, SerializedProperty shapeProperty, SerializedProperty rotationProperty, SerializedProperty triangleAProperty, SerializedProperty triangleBProperty, SerializedProperty triangleCProperty, UnityEngine.Object previewSource, UnityEngine.Object cookieSource) {
            float previewAspect = GetAreaCookiePreviewAspect(previewSource, cookieSource);
            float previewHeight = Mathf.Clamp((EditorGUIUtility.currentViewWidth - 40f) / previewAspect, AreaCookiePreviewMinHeight, AreaCookiePreviewMaxHeight);
            Rect previewRect = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect(false, previewHeight));
            Rect canvasRect = FitRectToAspect(previewRect, previewAspect);
            HandleAreaCookieCropPreviewInput(canvasRect, cropProperty, shapeProperty, rotationProperty, triangleAProperty, triangleBProperty, triangleCProperty);

            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(previewRect, _areaCookiePreviewBackgroundColor);
            Texture previewTexture = GetAreaCookiePreviewTexture(previewSource, cookieSource);
            if (previewTexture != null) EditorGUI.DrawPreviewTexture(canvasRect, previewTexture, null, ScaleMode.StretchToFill);
            else EditorGUI.DrawRect(canvasRect, Color.black);

            DrawRectOutline(canvasRect, _areaCookiePreviewBorderColor, 1f);
            Rect cropRect = CropToPreviewRect(canvasRect, GetSafeAreaCookieCrop(cropProperty.vector4Value));
            int shape = shapeProperty != null ? Mathf.Clamp(shapeProperty.intValue, 0, 4) : 0;
            if (shapeProperty != null && shapeProperty.intValue == AreaCookieCustomTriangleShape) shape = AreaCookieCustomTriangleShape;
            float rotation = rotationProperty != null ? GetSafeAreaCookieCropRotation(rotationProperty.floatValue) : 0f;
            DrawAreaCookieCropShapeOverlay(cropRect, shape, rotation, GetVectorPropertyXY(triangleAProperty, GetDefaultAreaCookieTriangleA()), GetVectorPropertyXY(triangleBProperty, GetDefaultAreaCookieTriangleB()), GetVectorPropertyXY(triangleCProperty, GetDefaultAreaCookieTriangleC()));
            if (_isPickingAreaCookieTriangle) DrawAreaCookieTrianglePickOverlay(canvasRect);
        }

        // Handles click-drag crop selection or the three-click custom triangle picker.
        private void HandleAreaCookieCropPreviewInput(Rect canvasRect, SerializedProperty cropProperty, SerializedProperty shapeProperty, SerializedProperty rotationProperty, SerializedProperty triangleAProperty, SerializedProperty triangleBProperty, SerializedProperty triangleCProperty) {
            if (_isPickingAreaCookieTriangle) {
                HandleAreaCookieTrianglePickerInput(canvasRect, cropProperty, shapeProperty, rotationProperty, triangleAProperty, triangleBProperty, triangleCProperty);
                return;
            }

            int controlID = GUIUtility.GetControlID(FocusType.Passive, canvasRect);
            Event currentEvent = Event.current;
            switch (currentEvent.GetTypeForControl(controlID)) {
                case EventType.MouseDown:
                    if (currentEvent.button != 0 || !canvasRect.Contains(currentEvent.mousePosition)) return;
                    GUIUtility.hotControl = controlID;
                    _isDraggingAreaCookieCrop = true;
                    _areaCookieCropHasDragged = false;
                    _areaCookieCropDragStart = ClampPointToRect(currentEvent.mousePosition, canvasRect);
                    currentEvent.Use();
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != controlID || !_isDraggingAreaCookieCrop) return;
                    _areaCookieCropHasDragged = true;
                    SetAreaCookieCropFromPreviewDrag(canvasRect, cropProperty, currentEvent);
                    currentEvent.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl != controlID || !_isDraggingAreaCookieCrop) return;
                    if (_areaCookieCropHasDragged) SetAreaCookieCropFromPreviewDrag(canvasRect, cropProperty, currentEvent);
                    _isDraggingAreaCookieCrop = false;
                    _areaCookieCropHasDragged = false;
                    GUIUtility.hotControl = 0;
                    currentEvent.Use();
                    break;
            }
        }

        // Collects three preview clicks and converts them into a custom crop-local triangle.
        private void HandleAreaCookieTrianglePickerInput(Rect canvasRect, SerializedProperty cropProperty, SerializedProperty shapeProperty, SerializedProperty rotationProperty, SerializedProperty triangleAProperty, SerializedProperty triangleBProperty, SerializedProperty triangleCProperty) {
            int controlID = GUIUtility.GetControlID(FocusType.Passive, canvasRect);
            Event currentEvent = Event.current;
            switch (currentEvent.GetTypeForControl(controlID)) {
                case EventType.MouseDown:
                    if (!canvasRect.Contains(currentEvent.mousePosition)) return;
                    if (currentEvent.button == 1) {
                        CancelAreaCookieTrianglePick();
                        currentEvent.Use();
                        return;
                    }
                    if (currentEvent.button != 0) return;
                    GUIUtility.hotControl = controlID;
                    _areaCookieTrianglePickPoints[Mathf.Clamp(_areaCookieTrianglePickCount, 0, 2)] = PreviewPointToAreaCookieUv(currentEvent.mousePosition, canvasRect);
                    _areaCookieTrianglePickCount++;
                    if (_areaCookieTrianglePickCount >= 3) ApplyAreaCookieTrianglePick(canvasRect, cropProperty, shapeProperty, rotationProperty, triangleAProperty, triangleBProperty, triangleCProperty);
                    GUI.changed = true;
                    currentEvent.Use();
                    Repaint();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl != controlID) return;
                    GUIUtility.hotControl = 0;
                    currentEvent.Use();
                    break;
                case EventType.KeyDown:
                    if (currentEvent.keyCode != KeyCode.Escape) return;
                    CancelAreaCookieTrianglePick();
                    currentEvent.Use();
                    break;
                case EventType.MouseMove:
                    if (canvasRect.Contains(currentEvent.mousePosition)) Repaint();
                    break;
            }
        }

        // Applies the collected absolute preview points as a crop rectangle plus crop-local triangle points.
        private void ApplyAreaCookieTrianglePick(Rect canvasRect, SerializedProperty cropProperty, SerializedProperty shapeProperty, SerializedProperty rotationProperty, SerializedProperty triangleAProperty, SerializedProperty triangleBProperty, SerializedProperty triangleCProperty) {
            if (cropProperty == null || shapeProperty == null || triangleAProperty == null || triangleBProperty == null || triangleCProperty == null) {
                CancelAreaCookieTrianglePick();
                return;
            }

            Vector2 a = _areaCookieTrianglePickPoints[0];
            Vector2 b = _areaCookieTrianglePickPoints[1];
            Vector2 c = _areaCookieTrianglePickPoints[2];
            float left = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
            float right = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
            float bottom = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
            float top = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
            ExpandAreaCookieTriangleBounds(ref left, ref right, Mathf.Max(AreaCookieCropMinSize, AreaCookieCropMinPreviewPixels / Mathf.Max(canvasRect.width, 1f)));
            ExpandAreaCookieTriangleBounds(ref bottom, ref top, Mathf.Max(AreaCookieCropMinSize, AreaCookieCropMinPreviewPixels / Mathf.Max(canvasRect.height, 1f)));

            Vector4 crop = GetSafeAreaCookieCrop(new Vector4(left, bottom, right - left, top - bottom));
            cropProperty.vector4Value = crop;
            shapeProperty.intValue = AreaCookieCustomTriangleShape;
            if (rotationProperty != null) rotationProperty.floatValue = 0f;
            triangleAProperty.vector4Value = AreaCookieUvToCropLocalPoint(a, crop);
            triangleBProperty.vector4Value = AreaCookieUvToCropLocalPoint(b, crop);
            triangleCProperty.vector4Value = AreaCookieUvToCropLocalPoint(c, crop);
            CancelAreaCookieTrianglePick();
        }

        // Keeps a clicked triangle crop from becoming too small to edit or bake.
        private void ExpandAreaCookieTriangleBounds(ref float min, ref float max, float minSize) {
            min = Mathf.Clamp01(min);
            max = Mathf.Clamp01(max);
            if (max - min >= minSize) return;
            float size = Mathf.Min(minSize, 1f);
            float center = (min + max) * 0.5f;
            min = Mathf.Clamp(center - size * 0.5f, 0f, 1f - size);
            max = min + size;
        }

        // Stops the point picker without changing the current crop.
        private void CancelAreaCookieTrianglePick() {
            _isPickingAreaCookieTriangle = false;
            _areaCookieTrianglePickCount = 0;
            _isDraggingAreaCookieCrop = false;
            _areaCookieCropHasDragged = false;
            GUIUtility.hotControl = 0;
            Repaint();
        }

        // Converts a preview drag rectangle into normalized lower-left crop coordinates.
        private void SetAreaCookieCropFromPreviewDrag(Rect canvasRect, SerializedProperty cropProperty, Event currentEvent) {
            Vector2 end = ClampPointToRect(currentEvent.mousePosition, canvasRect);
            if (currentEvent.shift) end = ClampPointToRect(GetSquareDragEnd(_areaCookieCropDragStart, end), canvasRect);

            float left = Mathf.Min(_areaCookieCropDragStart.x, end.x);
            float right = Mathf.Max(_areaCookieCropDragStart.x, end.x);
            float top = Mathf.Min(_areaCookieCropDragStart.y, end.y);
            float bottom = Mathf.Max(_areaCookieCropDragStart.y, end.y);

            if (right - left < AreaCookieCropMinPreviewPixels) {
                if (end.x >= _areaCookieCropDragStart.x) right = Mathf.Min(canvasRect.xMax, left + AreaCookieCropMinPreviewPixels);
                else left = Mathf.Max(canvasRect.xMin, right - AreaCookieCropMinPreviewPixels);
            }
            if (bottom - top < AreaCookieCropMinPreviewPixels) {
                if (end.y >= _areaCookieCropDragStart.y) bottom = Mathf.Min(canvasRect.yMax, top + AreaCookieCropMinPreviewPixels);
                else top = Mathf.Max(canvasRect.yMin, bottom - AreaCookieCropMinPreviewPixels);
            }

            Vector4 crop = new Vector4(
                (left - canvasRect.x) / canvasRect.width,
                (canvasRect.yMax - bottom) / canvasRect.height,
                (right - left) / canvasRect.width,
                (bottom - top) / canvasRect.height
            );
            cropProperty.vector4Value = GetSafeAreaCookieCrop(crop);
            GUI.changed = true;
        }

        // Converts a preview-space point to normalized image UV with a lower-left origin.
        private Vector2 PreviewPointToAreaCookieUv(Vector2 point, Rect canvasRect) {
            Vector2 clamped = ClampPointToRect(point, canvasRect);
            return new Vector2(
                Mathf.Clamp01((clamped.x - canvasRect.x) / Mathf.Max(canvasRect.width, 0.0001f)),
                Mathf.Clamp01((canvasRect.yMax - clamped.y) / Mathf.Max(canvasRect.height, 0.0001f))
            );
        }

        // Converts normalized image UV with a lower-left origin to preview-space coordinates.
        private Vector2 AreaCookieUvToPreviewPoint(Vector2 uv, Rect canvasRect) {
            return new Vector2(
                canvasRect.x + Mathf.Clamp01(uv.x) * canvasRect.width,
                canvasRect.yMax - Mathf.Clamp01(uv.y) * canvasRect.height
            );
        }

        // Converts an absolute image UV to the saved crop-local triangle point format.
        private Vector4 AreaCookieUvToCropLocalPoint(Vector2 uv, Vector4 crop) {
            float x = (Mathf.Clamp01(uv.x) - crop.x) / Mathf.Max(crop.z, AreaCookieCropMinSize);
            float y = (Mathf.Clamp01(uv.y) - crop.y) / Mathf.Max(crop.w, AreaCookieCropMinSize);
            return GetSafeAreaCookieCropTrianglePoint(new Vector4(x, y, 0f, 0f));
        }

        // Returns the drag end point adjusted to a screen-square box around the drag start.
        private Vector2 GetSquareDragEnd(Vector2 start, Vector2 end) {
            Vector2 delta = end - start;
            float side = Mathf.Min(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
            if (side <= 0f) side = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
            float xSign = delta.x < 0f ? -1f : 1f;
            float ySign = delta.y < 0f ? -1f : 1f;
            return new Vector2(start.x + xSign * side, start.y + ySign * side);
        }

        // Fits a source aspect inside an available preview rectangle.
        private Rect FitRectToAspect(Rect rect, float aspect) {
            float width = rect.width;
            float height = width / aspect;
            if (height > rect.height) {
                height = rect.height;
                width = height * aspect;
            }
            return new Rect(rect.x + (rect.width - width) * 0.5f, rect.y + (rect.height - height) * 0.5f, width, height);
        }

        // Converts a normalized lower-left crop rectangle to preview-space coordinates.
        private Rect CropToPreviewRect(Rect canvasRect, Vector4 crop) {
            return new Rect(
                canvasRect.x + crop.x * canvasRect.width,
                canvasRect.y + (1f - crop.y - crop.w) * canvasRect.height,
                crop.z * canvasRect.width,
                crop.w * canvasRect.height
            );
        }

        // Uses the assigned guide/cookie texture aspect when possible, otherwise falls back to a blank 1920x1080 canvas.
        private float GetAreaCookiePreviewAspect(UnityEngine.Object previewSource, UnityEngine.Object cookieSource) {
            Texture previewTexture = GetAreaCookiePreviewTexture(previewSource, cookieSource);
            if (previewTexture == null || previewTexture.width <= 0 || previewTexture.height <= 0) return AreaCookiePreviewAspect;
            return (float)previewTexture.width / previewTexture.height;
        }

        // Returns the texture to display for visual crop picking.
        private Texture GetAreaCookiePreviewTexture(UnityEngine.Object previewSource, UnityEngine.Object cookieSource) {
            if (previewSource is Texture previewTexture) return previewTexture;
            if (cookieSource is Texture texture) return texture;
            if (cookieSource is Material material) return material.mainTexture;
            return null;
        }

        // Clamps a crop rectangle to a valid normalized subregion.
        private Vector4 GetSafeAreaCookieCrop(Vector4 crop) {
            float width = Mathf.Clamp(Mathf.Abs(crop.z), AreaCookieCropMinSize, 1f);
            float height = Mathf.Clamp(Mathf.Abs(crop.w), AreaCookieCropMinSize, 1f);
            float offsetX = Mathf.Clamp(crop.x, 0f, 1f - width);
            float offsetY = Mathf.Clamp(crop.y, 0f, 1f - height);
            return new Vector4(offsetX, offsetY, width, height);
        }

        // Clamps a custom triangle point to normalized crop-local coordinates.
        private Vector4 GetSafeAreaCookieCropTrianglePoint(Vector4 point) {
            return new Vector4(Mathf.Clamp01(point.x), Mathf.Clamp01(point.y), 0f, 0f);
        }

        private Vector4 GetDefaultAreaCookieTriangleA() {
            return new Vector4(0f, 0f, 0f, 0f);
        }

        private Vector4 GetDefaultAreaCookieTriangleB() {
            return new Vector4(1f, 0f, 0f, 0f);
        }

        private Vector4 GetDefaultAreaCookieTriangleC() {
            return new Vector4(0f, 1f, 0f, 0f);
        }

        // Reads a Vector4 serialized point safely for preview drawing.
        private Vector4 GetVectorPropertyXY(SerializedProperty property, Vector4 fallback) {
            return property != null && !property.hasMultipleDifferentValues ? GetSafeAreaCookieCropTrianglePoint(property.vector4Value) : fallback;
        }

        // Keeps rotations bounded for serialized data and preview math.
        private float GetSafeAreaCookieCropRotation(float rotation) {
            if (rotation != rotation) return 0f;
            float safeRotation = Mathf.Clamp(rotation, -360f, 360f);
            if (safeRotation <= -180f) safeRotation += 360f;
            else if (safeRotation > 180f) safeRotation -= 360f;
            return safeRotation;
        }

        // Clamps a point to a preview rectangle.
        private Vector2 ClampPointToRect(Vector2 point, Rect rect) {
            return new Vector2(Mathf.Clamp(point.x, rect.xMin, rect.xMax), Mathf.Clamp(point.y, rect.yMin, rect.yMax));
        }

        // Draws the in-progress three-click triangle points over the crop preview.
        private void DrawAreaCookieTrianglePickOverlay(Rect canvasRect) {
            if (_areaCookieTrianglePickCount <= 0) return;
            Handles.BeginGUI();
            Handles.color = _areaCookieCropBorderColor;
            Vector3[] points = new Vector3[_areaCookieTrianglePickCount];
            for (int i = 0; i < _areaCookieTrianglePickCount; i++) {
                Vector2 previewPoint = AreaCookieUvToPreviewPoint(_areaCookieTrianglePickPoints[i], canvasRect);
                points[i] = new Vector3(previewPoint.x, previewPoint.y, 0f);
                Handles.DrawSolidDisc(points[i], Vector3.forward, AreaCookieTrianglePointRadius);
            }
            if (_areaCookieTrianglePickCount > 1) Handles.DrawAAPolyLine(2f, points);
            if (_areaCookieTrianglePickCount == 2 && canvasRect.Contains(Event.current.mousePosition)) {
                Vector2 hoverPoint = ClampPointToRect(Event.current.mousePosition, canvasRect);
                Handles.DrawAAPolyLine(1f, points[1], new Vector3(hoverPoint.x, hoverPoint.y, 0f));
            }
            Handles.EndGUI();
        }

        // Draws either a rectangle overlay or the selected triangular crop mask with rotation applied.
        private void DrawAreaCookieCropShapeOverlay(Rect cropRect, int shape, float rotation, Vector4 triangleA, Vector4 triangleB, Vector4 triangleC) {
            Vector3[] points = GetAreaCookieShapeOverlayPoints(cropRect, shape, rotation, triangleA, triangleB, triangleC);
            if (points.Length < 3) return;
            if (shape > 0 || !Mathf.Approximately(rotation, 0f)) DrawRectOutline(cropRect, new Color(_areaCookieCropBorderColor.r, _areaCookieCropBorderColor.g, _areaCookieCropBorderColor.b, 0.35f), 1f);
            Handles.BeginGUI();
            Handles.color = _areaCookieCropFillColor;
            Handles.DrawAAConvexPolygon(points);
            Handles.color = _areaCookieCropBorderColor;
            Handles.DrawAAPolyLine(2f, CloseAreaCookiePolygon(points));
            Handles.EndGUI();
        }

        // Returns preview-space vertices matching the shader's crop shape after rotation and clipping.
        private Vector3[] GetAreaCookieShapeOverlayPoints(Rect rect, int shape, float rotation) {
            return GetAreaCookieShapeOverlayPoints(rect, shape, rotation, GetDefaultAreaCookieTriangleA(), GetDefaultAreaCookieTriangleB(), GetDefaultAreaCookieTriangleC());
        }

        // Returns preview-space vertices matching the shader's crop shape after rotation and clipping.
        private Vector3[] GetAreaCookieShapeOverlayPoints(Rect rect, int shape, float rotation, Vector4 triangleA, Vector4 triangleB, Vector4 triangleC) {
            List<Vector2> points = GetAreaCookieShapeUnitPoints(shape, triangleA, triangleB, triangleC);
            float aspect = rect.height > 0f ? rect.width / rect.height : 1f;
            for (int i = 0; i < points.Count; i++) points[i] = RotateAreaCookieUnitPoint(points[i], rotation, aspect);
            points = ClipAreaCookiePolygonToUnitRect(points);
            Vector3[] previewPoints = new Vector3[points.Count];
            for (int i = 0; i < points.Count; i++) previewPoints[i] = new Vector3(rect.x + points[i].x * rect.width, rect.y + points[i].y * rect.height, 0f);
            return previewPoints;
        }

        // Returns unit-space vertices in preview coordinates; Y grows downward to match IMGUI.
        private List<Vector2> GetAreaCookieShapeUnitPoints(int shape) {
            return GetAreaCookieShapeUnitPoints(shape, GetDefaultAreaCookieTriangleA(), GetDefaultAreaCookieTriangleB(), GetDefaultAreaCookieTriangleC());
        }

        // Returns unit-space vertices in preview coordinates; Y grows downward to match IMGUI.
        private List<Vector2> GetAreaCookieShapeUnitPoints(int shape, Vector4 triangleA, Vector4 triangleB, Vector4 triangleC) {
            if (shape == AreaCookieCustomTriangleShape)
                return new List<Vector2> { new Vector2(triangleA.x, 1f - triangleA.y), new Vector2(triangleB.x, 1f - triangleB.y), new Vector2(triangleC.x, 1f - triangleC.y) };
            if (shape == 1) return new List<Vector2> { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f) };
            if (shape == 2) return new List<Vector2> { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            if (shape == 3) return new List<Vector2> { new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f) };
            if (shape == 4) return new List<Vector2> { new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f) };
            return new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        }

        // Applies the same fitted center rotation used by the cookie crop shader, expressed in preview unit coordinates.
        private Vector2 RotateAreaCookieUnitPoint(Vector2 point, float rotation, float aspect) {
            if (Mathf.Approximately(rotation, 0f)) return point;
            float radians = rotation * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);
            float fitScale = GetAreaCookieRotationFitScale(sin, cos, aspect);
            Vector2 delta = (point - new Vector2(0.5f, 0.5f)) * fitScale;
            return new Vector2(0.5f + delta.x * cos - delta.y * sin, 0.5f + delta.x * sin + delta.y * cos);
        }

        // Returns the largest uniform scale that keeps a rotated crop inside its selected box.
        private float GetAreaCookieRotationFitScale(float sin, float cos, float aspect) {
            float safeAspect = Mathf.Max(Mathf.Abs(aspect), 0.0001f);
            float absSin = Mathf.Abs(sin);
            float absCos = Mathf.Abs(cos);
            float scaleX = safeAspect / Mathf.Max(absCos * safeAspect + absSin, 0.0001f);
            float scaleY = 1f / Mathf.Max(absSin * safeAspect + absCos, 0.0001f);
            return Mathf.Max(Mathf.Min(scaleX, scaleY), 0.0001f);
        }

        // Clips the overlay to guard against tiny floating-point edge cases.
        private List<Vector2> ClipAreaCookiePolygonToUnitRect(List<Vector2> points) {
            points = ClipAreaCookiePolygon(points, 0);
            points = ClipAreaCookiePolygon(points, 1);
            points = ClipAreaCookiePolygon(points, 2);
            return ClipAreaCookiePolygon(points, 3);
        }

        // Clips a convex polygon against one edge of the unit crop box.
        private List<Vector2> ClipAreaCookiePolygon(List<Vector2> points, int edge) {
            List<Vector2> output = new List<Vector2>();
            int count = points.Count;
            if (count == 0) return output;
            Vector2 previous = points[count - 1];
            bool previousInside = IsInsideAreaCookieClipEdge(previous, edge);
            for (int i = 0; i < count; i++) {
                Vector2 current = points[i];
                bool currentInside = IsInsideAreaCookieClipEdge(current, edge);
                if (currentInside) {
                    if (!previousInside) output.Add(IntersectAreaCookieClipEdge(previous, current, edge));
                    output.Add(current);
                } else if (previousInside) {
                    output.Add(IntersectAreaCookieClipEdge(previous, current, edge));
                }
                previous = current;
                previousInside = currentInside;
            }
            return output;
        }

        // Returns whether a unit point is inside the requested crop-box edge.
        private bool IsInsideAreaCookieClipEdge(Vector2 point, int edge) {
            if (edge == 0) return point.x >= 0f;
            if (edge == 1) return point.x <= 1f;
            if (edge == 2) return point.y >= 0f;
            return point.y <= 1f;
        }

        // Intersects a polygon edge with one crop-box edge.
        private Vector2 IntersectAreaCookieClipEdge(Vector2 from, Vector2 to, int edge) {
            Vector2 delta = to - from;
            float t = 0f;
            if (edge == 0) t = Mathf.Abs(delta.x) > 0.00001f ? (0f - from.x) / delta.x : 0f;
            else if (edge == 1) t = Mathf.Abs(delta.x) > 0.00001f ? (1f - from.x) / delta.x : 0f;
            else if (edge == 2) t = Mathf.Abs(delta.y) > 0.00001f ? (0f - from.y) / delta.y : 0f;
            else t = Mathf.Abs(delta.y) > 0.00001f ? (1f - from.y) / delta.y : 0f;
            return from + delta * Mathf.Clamp01(t);
        }

        // Returns a copy with the first point appended for outline drawing.
        private Vector3[] CloseAreaCookiePolygon(Vector3[] points) {
            Vector3[] closed = new Vector3[points.Length + 1];
            for (int i = 0; i < points.Length; i++) closed[i] = points[i];
            closed[points.Length] = points[0];
            return closed;
        }

        // Draws a crisp IMGUI rectangle outline.
        private void DrawRectOutline(Rect rect, Color color, float thickness) {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        // Resolves and draws a named texture-or-material serialized property.
        private void DrawTextureMaterialField(string propertyName, string acceptedTypesHint, bool isShadowSource) {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            DrawTextureMaterialField(property, EditorGUIUtility.TrTextContent(property.displayName, property.tooltip), acceptedTypesHint, isShadowSource);
        }

        // Draws a filtered object field with mixed values, picker support and an empty-state hint.
        private void DrawTextureMaterialField(SerializedProperty property, GUIContent label, string acceptedTypesHint, bool isShadowSource) {
            Rect rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            EditorGUI.BeginProperty(rect, label, property);
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            int controlID = GUIUtility.GetControlID(FocusType.Keyboard, rect);
            Rect fieldRect = EditorGUI.PrefixLabel(rect, controlID, label);
            bool drawHint = !property.hasMultipleDifferentValues && property.objectReferenceValue == null;
            bool hideNativeEmptyText = drawHint && Event.current.type == EventType.Repaint;
            Color contentColor = GUI.contentColor;
            if (hideNativeEmptyText) GUI.contentColor = new Color(contentColor.r, contentColor.g, contentColor.b, 0f);

            ShowProjectionSourcePickerOnSelectorClick(property, fieldRect, controlID);
            EditorGUI.BeginChangeCheck();
            UnityEngine.Object value = EditorGUI.ObjectField(fieldRect, _emptyContent, property.objectReferenceValue, typeof(UnityEngine.Object), false);
            if (hideNativeEmptyText) GUI.contentColor = contentColor;
            if (EditorGUI.EndChangeCheck()) property.objectReferenceValue = IsSupportedTextureMaterialSource(value, isShadowSource) ? value : null;
            UpdateProjectionSourceFromPicker(property, controlID, isShadowSource);
            if (drawHint) DrawProjectionSourceHint(fieldRect, acceptedTypesHint);
            EditorGUI.showMixedValue = false;
            EditorGUI.EndProperty();
        }

        // Opens a filtered Unity object picker when the field's selector button is clicked.
        private void ShowProjectionSourcePickerOnSelectorClick(SerializedProperty property, Rect fieldRect, int controlID) {
            Event currentEvent = Event.current;
            if (currentEvent.type != EventType.MouseDown || currentEvent.button != 0) return;
            Rect selectorRect = fieldRect;
            selectorRect.xMin = selectorRect.xMax - ObjectSelectorButtonWidth;
            if (!selectorRect.Contains(currentEvent.mousePosition)) return;
            EditorGUIUtility.ShowObjectPicker<UnityEngine.Object>(property.objectReferenceValue, false, _projectionSourceObjectPickerFilter, controlID);
            currentEvent.Use();
        }

        // Accepts supported picker selections and rejects incompatible projection sources.
        private void UpdateProjectionSourceFromPicker(SerializedProperty property, int controlID, bool isShadowSource) {
            Event currentEvent = Event.current;
            if (currentEvent.type != EventType.ExecuteCommand) return;
            string commandName = currentEvent.commandName;
            if (commandName != "ObjectSelectorUpdated" && commandName != "ObjectSelectorClosed") return;
            if (EditorGUIUtility.GetObjectPickerControlID() != controlID) return;
            if (commandName == "ObjectSelectorUpdated") {
                UnityEngine.Object value = EditorGUIUtility.GetObjectPickerObject();
                property.objectReferenceValue = IsSupportedTextureMaterialSource(value, isShadowSource) ? value : null;
            }
            currentEvent.Use();
        }

        // Draws accepted source types inside an otherwise empty object field.
        private void DrawProjectionSourceHint(Rect fieldRect, string acceptedTypesHint) {
            if (Event.current.type != EventType.Repaint) return;
            if (_projectionSourceHintStyle == null) {
                _projectionSourceHintStyle = new GUIStyle(EditorStyles.label);
                RectOffset objectFieldPadding = EditorStyles.objectField.padding;
                _projectionSourceHintStyle.padding = new RectOffset(objectFieldPadding.left, 0, objectFieldPadding.top, objectFieldPadding.bottom);
                _projectionSourceHintStyle.alignment = EditorStyles.objectField.alignment;
                _projectionSourceHintStyle.normal.textColor = EditorStyles.objectField.normal.textColor;
                _projectionSourceHintStyle.clipping = TextClipping.Clip;
            }
            Rect hintRect = fieldRect;
            hintRect.xMax -= ObjectSelectorButtonWidth;
            GUI.Label(hintRect, acceptedTypesHint, _projectionSourceHintStyle);
        }

        // Validates a texture or material against the current projection and shadow requirements.
        private bool IsSupportedTextureMaterialSource(UnityEngine.Object value, bool isShadowSource) {
            if (value == null) return true;
            if (isShadowSource) return value is Texture2DArray || value is Cubemap || value is RenderTexture || value is Material;
            if (value is RenderTexture || value is Material) return true;
            if (!(value is Texture)) return false;
            int lightType = Mathf.Clamp(serializedObject.FindProperty("LightType").intValue, 0, 2);
            int projection = Mathf.Clamp(serializedObject.FindProperty("Projection").intValue, 0, 2);
            return lightType == 2 || projection == 1 || projection == 2 && (lightType == 0 || lightType == 1);
        }

        // Returns the owning Manager's culling cutoff or the package default.
        private static float GetBrightnessCutoff(PointLightVolumeInstance pointLightVolume) {
            return pointLightVolume.LightVolumeManager != null ? pointLightVolume.LightVolumeManager.LightsBrightnessCutoff : 0.35f;
        }

        // Draws the Scene View shape, range and optional debug bounds for one light.
        private void DrawVolumeGUI(PointLightVolumeInstance pointLightVolume) {

            Transform t = pointLightVolume.transform;
            Vector3 origin = t.position;
            Vector3 lscale = pointLightVolume.transform.lossyScale;
            float scale = (lscale.x + lscale.y + lscale.z) / 3;
            float range = pointLightVolume.LightType != 2 && (pointLightVolume.Projection != 1 || pointLightVolume.FalloffLUT == null) ? pointLightVolume.LightSourceSize : pointLightVolume.Range;
            range *= scale;

            if (pointLightVolume.LightType == 0) { // Point Light Visualization

                // Calculating
                float bounds = 0;
                bool isDebug = pointLightVolume.DebugRange && (pointLightVolume.Projection != 1 || pointLightVolume.FalloffLUT == null);
                if (isDebug) bounds = Mathf.Sqrt(ComputePointLightSquaredBoundingSphere(pointLightVolume.Color, pointLightVolume.Intensity, range, GetBrightnessCutoff(pointLightVolume)));

                // Drawing
                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.color = new Color(1f, 1f, 0f, 0.6f);
                DrawPointLight(origin, range);
                if (isDebug) DrawPointLight(origin, bounds);
                DrawShadowClipGUI(pointLightVolume, origin, t);

                Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                Handles.color = new Color(1f, 1f, 0f, 0.15f);
                DrawPointLight(origin, range);
                if (isDebug) DrawPointLight(origin, bounds);
                DrawShadowClipGUI(pointLightVolume, origin, t);

            } else if (pointLightVolume.LightType == 1) { // Spot Light Visualization

                // Calculating
                Vector3 forward = t.forward;
                Vector3 right = t.right;
                Vector3 up = t.up;
                float halfAngleRad = Mathf.Clamp(pointLightVolume.Angle, 0.05f * Mathf.Deg2Rad, Mathf.PI);
                Vector3[] dirs = new Vector3[] { right, -right, up, -up };
                float bounds = 0;
                bool isDebug = pointLightVolume.DebugRange && (pointLightVolume.Projection != 1 || pointLightVolume.FalloffLUT == null);
                if (isDebug) bounds = Mathf.Sqrt(ComputePointLightSquaredBoundingSphere(pointLightVolume.Color, pointLightVolume.Intensity, range, GetBrightnessCutoff(pointLightVolume)));

                // Drawing
                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.color = new Color(1f, 1f, 0f, 0.6f);
                DrawSpotLight(origin, forward, halfAngleRad, range, dirs);

                if (isDebug) DrawSpotLight(origin, forward, halfAngleRad, bounds, dirs);
                DrawShadowClipGUI(pointLightVolume, origin, t);

                Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                Handles.color = new Color(1f, 1f, 0f, 0.15f);
                DrawSpotLight(origin, forward, halfAngleRad, range, dirs);

                if (isDebug) DrawSpotLight(origin, forward, halfAngleRad, bounds, dirs);
                DrawShadowClipGUI(pointLightVolume, origin, t);

            } else { // Area light

                float x = Mathf.Max(Mathf.Abs(pointLightVolume.transform.lossyScale.x), 0.001f);
                float y = Mathf.Max(Mathf.Abs(pointLightVolume.transform.lossyScale.y), 0.001f);
                int shape = Mathf.Clamp(pointLightVolume.AreaLightShape, 0, 4);

                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.color = new Color(1f, 1f, 0f, 0.6f);
                DrawAreaLight(origin, t.rotation, x, y, shape);

                if (pointLightVolume.DebugRange) DrawAreaLightDebug(origin, t.rotation, x, y, shape, pointLightVolume.Color, pointLightVolume.Intensity, GetBrightnessCutoff(pointLightVolume));
                DrawShadowClipGUI(pointLightVolume, origin, t);

                Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                Handles.color = new Color(1f, 1f, 0f, 0.15f);
                DrawAreaLight(origin, t.rotation, x, y, shape);

                if (pointLightVolume.DebugRange) DrawAreaLightDebug(origin, t.rotation, x, y, shape, pointLightVolume.Color, pointLightVolume.Intensity, GetBrightnessCutoff(pointLightVolume));
                DrawShadowClipGUI(pointLightVolume, origin, t);

            }

        }

        // Draws Scene View gizmos for every selected Point Light Volume.
        void OnSceneGUI() {
            foreach (var obj in Selection.gameObjects) {
                var volume = obj.GetComponent<PointLightVolumeInstance>();
                if (volume != null) DrawVolumeGUI(volume);
            }
        }

        // Draws a spotlight visualization using precalculated values
        private void DrawSpotLight(Vector3 origin, Vector3 forward, float halfAngleRad, float range, Vector3[] dirs) {

            float centerOffset = range * Mathf.Cos(halfAngleRad);
            Vector3 diskCenter = origin + forward * centerOffset;
            float radius = Mathf.Abs(range) * Mathf.Sin(halfAngleRad);
            float angleDeg = Mathf.Rad2Deg * halfAngleRad;

            Handles.DrawWireDisc(diskCenter, forward, radius);

            foreach (var dir in dirs) {
                Vector3 edge = diskCenter + dir * radius;
                Handles.DrawLine(origin, edge);
                Handles.DrawWireArc(origin, dir, forward, angleDeg, range);
            }
        }

        // Draws a pointlight visualization
        private void DrawPointLight(Vector3 center, float radius) {
            Handles.DrawWireArc(center, Vector3.right, Vector3.up, 360, radius);
            Handles.DrawWireArc(center, Vector3.up, Vector3.forward, 360, radius);
            Handles.DrawWireArc(center, Vector3.forward, Vector3.right, 360, radius);
        }

        // Draws the manually controlled shadow bake near-far space.
        private void DrawShadowClipGUI(PointLightVolumeInstance pointLightVolume, Vector3 origin, Transform transform) {
            if (!pointLightVolume.Shadows || !pointLightVolume.DebugClipPlanes) return;

            Handles.color = Handles.zTest == UnityEngine.Rendering.CompareFunction.LessEqual ? _shadowClipVisibleColor : _shadowClipHiddenColor;
            float nearClip = pointLightVolume.GetShadowNearClip();
            float farClip = pointLightVolume.GetShadowFarClip();
            bool drawSpotFrustum = pointLightVolume.LightType == 1 && !pointLightVolume.ShouldBakeCubemapShadows();
            if (!drawSpotFrustum) {
                DrawPointLight(origin, nearClip);
                DrawPointLight(origin, farClip);
                return;
            }

            float halfAngleRad = Mathf.Clamp(pointLightVolume.Angle, 0.05f * Mathf.Deg2Rad, 89.95f * Mathf.Deg2Rad);
            DrawSpotShadowClip(origin, transform.forward, transform.right, transform.up, halfAngleRad, nearClip, farClip);
        }

        // Draws a truncated spotlight shadow frustum between the near and far clip planes.
        private void DrawSpotShadowClip(Vector3 origin, Vector3 forward, Vector3 right, Vector3 up, float halfAngleRad, float nearClip, float farClip) {
            float tanHalfAngle = Mathf.Tan(halfAngleRad);
            Vector3 nearCenter = origin + forward * nearClip;
            Vector3 farCenter = origin + forward * farClip;
            float nearRadius = nearClip * tanHalfAngle;
            float farRadius = farClip * tanHalfAngle;

            Handles.DrawWireDisc(nearCenter, forward, nearRadius);
            Handles.DrawWireDisc(farCenter, forward, farRadius);
            Handles.DrawLine(nearCenter + right * nearRadius, farCenter + right * farRadius);
            Handles.DrawLine(nearCenter - right * nearRadius, farCenter - right * farRadius);
            Handles.DrawLine(nearCenter + up * nearRadius, farCenter + up * farRadius);
            Handles.DrawLine(nearCenter - up * nearRadius, farCenter - up * farRadius);
        }

        // Draws an Area Light emitter shape and its forward direction.
        private void DrawAreaLight(Vector3 center, Quaternion rotation, float width, float height, int shape) {
            Vector3[] corners = GetAreaLightShapeCorners(center, rotation, width, height, shape);
            for (int i = 0; i < corners.Length; i++) Handles.DrawLine(corners[i], corners[(i + 1) % corners.Length]);

            // Draw forward vector
            Handles.DrawLine(center, center + rotation * Vector3.forward * 0.5f);
        }

        // Returns Scene View vertices matching the Area Light emitter shape.
        private Vector3[] GetAreaLightShapeCorners(Vector3 center, Quaternion rotation, float width, float height, int shape) {
            Vector3 right = rotation * Vector3.right * (width * 0.5f);
            Vector3 up = rotation * Vector3.up * (height * 0.5f);

            Vector3 lowerLeft = center - right - up;
            Vector3 lowerRight = center + right - up;
            Vector3 upperLeft = center - right + up;
            Vector3 upperRight = center + right + up;

            if (shape == 1) return new[] { lowerLeft, lowerRight, upperLeft };
            if (shape == 2) return new[] { lowerLeft, lowerRight, upperRight };
            if (shape == 3) return new[] { lowerLeft, upperLeft, upperRight };
            if (shape == 4) return new[] { lowerRight, upperLeft, upperRight };
            return new[] { upperRight, upperLeft, lowerLeft, lowerRight };
        }

        // Draws the estimated culling sphere of an Area Light.
        private void DrawAreaLightDebug(Vector3 center, Quaternion rotation, float width, float height, int shape, Color color, float intensity, float cutoff) {

            // Light normal
            Vector3 up = rotation * Vector3.up;
            Vector3 right = rotation * Vector3.right;
            Vector3 forward = rotation * Vector3.forward;

            // Calculate the bounding sphere of the area light given the cutoff irradiance
            float minSolidAngle = Mathf.Clamp(cutoff / (Mathf.Max(color.r, Mathf.Max(color.g, color.b)) * intensity * Mathf.PI), -Mathf.PI * 2f, Mathf.PI * 2);
            float sqMaxDist = ComputeAreaLightSquaredBoundingSphere(width, height, shape, minSolidAngle);
            float radius = Mathf.Sqrt(sqMaxDist);

            Handles.DrawWireDisc(center, forward, radius);
            Handles.DrawWireArc(center, right, up * radius, 180f, radius);
            Handles.DrawWireArc(center, up, -right * radius, 180f, radius);

        }

        // Calculates squared Area Light range from emitter dimensions and minimum solid angle.
        float ComputeAreaLightSquaredBoundingSphere(float width, float height, int shape, float minSolidAngle) {
            float A = width * height * (shape == 0 ? 1f : 0.5f);
            float w2 = width * width;
            float h2 = height * height;
            float B = 0.25f * (w2 + h2);
            float t = Mathf.Tan(0.25f * minSolidAngle);
            float T = t * t;
            float TB = T * B;
            float discriminant = Mathf.Sqrt(TB * TB + 4.0f * T * A * A);
            float d2 = (discriminant - TB) * 0.125f / T;
            return d2;
        }

        // Calculates squared Point Light range from brightness, source size and cutoff.
        float ComputePointLightSquaredBoundingSphere(Color color, float intensity, float size, float cutoff) {
            float L = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            return Mathf.Max(Mathf.PI * 2 * L * Mathf.Abs(intensity) / (cutoff * cutoff) - 1, 0) * size * size;
        }

        // Keeps live debug values updating while their foldout is visible in play mode.
        public override bool RequiresConstantRepaint() {
            return _debugExpanded && EditorApplication.isPlaying;
        }

    }

}
