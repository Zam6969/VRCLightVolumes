using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace VRCLightVolumes {

    [CanEditMultipleObjects]
    [CustomEditor(typeof(PointLightVolume))]
    public class PointLightVolumeEditor : Editor {

        PointLightVolume PointLightVolume;
        private static readonly GUIContent _bakeShadowsButtonContent = new GUIContent("Bake Shadows", "Bakes or re-bakes shadow maps for all selected lights with Shadows enabled.");
        private static readonly GUIContent _emptyContent = GUIContent.none;
        private static readonly string _textureMaterialHint = "None (Texture/Material)";
        private static readonly string _cubemapMaterialHint = "None (Texture/Material)";
        private static readonly string _projectionSourceObjectPickerFilter = "t:Texture t:Material";
        private const float ObjectSelectorButtonWidth = 19f;
        private const float AreaCookiePreviewAspect = 1920f / 1080f;
        private const float AreaCookiePreviewMinHeight = 96f;
        private const float AreaCookiePreviewMaxHeight = 280f;
        private const float AreaCookieCropMinSize = 0.001f;
        private const float AreaCookieCropMinPreviewPixels = 1f;
        private static readonly Color _shadowClipVisibleColor = new Color(0.2f, 0.65f, 1f, 0.75f);
        private static readonly Color _shadowClipHiddenColor = new Color(0.2f, 0.65f, 1f, 0.18f);
        private static readonly Color _areaCookiePreviewBackgroundColor = new Color(0.04f, 0.04f, 0.04f, 1f);
        private static readonly Color _areaCookiePreviewBorderColor = new Color(0f, 0f, 0f, 0.65f);
        private static readonly Color _areaCookieCropFillColor = new Color(1f, 0.84f, 0.12f, 0.16f);
        private static readonly Color _areaCookieCropBorderColor = new Color(1f, 0.84f, 0.12f, 0.95f);
        private static GUIStyle _projectionSourceHintStyle = null;
        private Vector2 _areaCookieCropDragStart;
        private bool _isDraggingAreaCookieCrop;
        private bool _areaCookieCropHasDragged;

        private void OnEnable() {
            PointLightVolume = (PointLightVolume)target;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        private void OnDisable() {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        }

        public override void OnInspectorGUI() {

            serializedObject.Update();
            int undoGroup = Undo.GetCurrentGroup();

            List<string> hiddenFields = new List<string> { "m_Script", "PointLightVolumeInstance", "LightVolumeSetup" };
            hiddenFields.Add("ShadowMap");
            hiddenFields.Add("RebakeShadows");
            hiddenFields.Add("Bias");
            hiddenFields.Add("LayerMask");
            hiddenFields.Add("ObjectMask");
            hiddenFields.Add("NearPlane");
            hiddenFields.Add("FarPlane");
            hiddenFields.Add("DebugClipPlanes");
            hiddenFields.Add("Blur");
            hiddenFields.Add("ContactHardening");
            hiddenFields.Add("UseWorldSpace");
            hiddenFields.Add("ForceCubemapShadows");
            hiddenFields.Add("FalloffLUT");
            hiddenFields.Add("Cubemap");
            hiddenFields.Add("Cookie");
            hiddenFields.Add("AreaCookieCrop");
            hiddenFields.Add("AreaCookieCropPreview");
            hiddenFields.Add("Shadows");
            hiddenFields.Add("ShadingStrength");
            hiddenFields.Add("BakeIntoProbes");
            hiddenFields.Add("DebugRange");

            if (PointLightVolume.Type != PointLightVolume.LightType.SpotLight || PointLightVolume.Projection != PointLightVolume.LightProjection.Custom) {
                hiddenFields.Add("SpotCookieAspect");
            }
            
            if(PointLightVolume.Type == PointLightVolume.LightType.PointLight) {
                hiddenFields.Add("Angle");
                hiddenFields.Add("Falloff");
            }

            if (PointLightVolume.Type == PointLightVolume.LightType.AreaLight) {
                hiddenFields.Add("Angle");
                hiddenFields.Add("Falloff");
                hiddenFields.Add("Projection");
                hiddenFields.Add("Range");
                hiddenFields.Add("FalloffLUT");
                hiddenFields.Add("Cubemap");
                hiddenFields.Add("LightSourceSize");
            }

            if (PointLightVolume.Projection == PointLightVolume.LightProjection.Parametric) {
                hiddenFields.Add("Range");
            } else if (PointLightVolume.Projection == PointLightVolume.LightProjection.Custom) {
                hiddenFields.Add("Falloff");
                hiddenFields.Add("Range");
            } else if (PointLightVolume.Projection == PointLightVolume.LightProjection.LUT) {
                hiddenFields.Add("Falloff");
                hiddenFields.Add("LightSourceSize");
            }

            DrawPropertiesExcluding(serializedObject, hiddenFields.ToArray());
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ShadingStrength"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("BakeIntoProbes"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("DebugRange"));
            DrawActiveProjectionSourceField();
            SerializedProperty shadowsProperty = serializedObject.FindProperty("Shadows");
            EditorGUILayout.PropertyField(shadowsProperty);
            bool drawShadowFields = shadowsProperty.hasMultipleDifferentValues || shadowsProperty.boolValue;

            bool propertiesChanged = serializedObject.ApplyModifiedProperties();

            if (drawShadowFields) {
                DrawTextureMaterialField("ShadowMap", _cubemapMaterialHint, true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("LayerMask"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("ObjectMask"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("NearPlane"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("FarPlane"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("DebugClipPlanes"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Bias"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Blur"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("ContactHardening"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("UseWorldSpace"));
                if (PointLightVolume.Type == PointLightVolume.LightType.SpotLight) EditorGUILayout.PropertyField(serializedObject.FindProperty("ForceCubemapShadows"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("RebakeShadows"));

                if (GUILayout.Button(_bakeShadowsButtonContent)) {
                    propertiesChanged |= serializedObject.ApplyModifiedProperties();
                    propertiesChanged |= BakeSelectedShadowMaps();
                    serializedObject.Update();
                }
            }

            propertiesChanged |= serializedObject.ApplyModifiedProperties();
            if (propertiesChanged) {
                SyncTargets(true);
                Undo.CollapseUndoOperations(undoGroup);
            }

        }

        // Bakes shadows for selected point light volumes and rebuilds each touched shadow array once.
        private bool BakeSelectedShadowMaps() {
            bool bakedAny = false;
            HashSet<LightVolumeSetup> reinitializeSetups = null;
            for (int i = 0; i < targets.Length; i++) {
                PointLightVolume pointLightVolume = targets[i] as PointLightVolume;
                if (pointLightVolume == null || !pointLightVolume.Shadows) continue;
                if (!pointLightVolume.BakeShadowMap($"| {pointLightVolume.gameObject.name} ({i + 1}/{targets.Length})", false)) continue;
                bakedAny = true;
                if (pointLightVolume.LightVolumeSetup == null) continue;
                if (reinitializeSetups == null) reinitializeSetups = new HashSet<LightVolumeSetup>();
                reinitializeSetups.Add(pointLightVolume.LightVolumeSetup);
            }
            if (reinitializeSetups != null) {
                foreach (LightVolumeSetup lightVolumeSetup in reinitializeSetups) {
                    if (lightVolumeSetup != null) lightVolumeSetup.ReinitializeShadowTextures();
                }
            }
            return bakedAny;
        }

        // Syncs changed inspector values into runtime instances and shader globals immediately.
        private void SyncTargets(bool recordUndo) {
            HashSet<LightVolumeSetup> textureUndoRecordedSetups = null;
            for (int i = 0; i < targets.Length; i++) {
                PointLightVolume pointLightVolume = targets[i] as PointLightVolume;
                if (pointLightVolume == null) continue;
                bool customTexturesChanged = pointLightVolume.HasEditorCustomTextureChanges();
                bool shadowTexturesChanged = pointLightVolume.HasEditorShadowTextureChanges();
                if (recordUndo && (customTexturesChanged || shadowTexturesChanged) && pointLightVolume.LightVolumeSetup != null) {
                    if (textureUndoRecordedSetups == null) textureUndoRecordedSetups = new HashSet<LightVolumeSetup>();
                    if (textureUndoRecordedSetups.Add(pointLightVolume.LightVolumeSetup)) RecordTextureReinitializeUndo(pointLightVolume.LightVolumeSetup);
                }
                pointLightVolume.SyncEditorChanges(customTexturesChanged, shadowTexturesChanged, recordUndo);
                if (pointLightVolume.LightVolumeSetup != null) {
                    if (customTexturesChanged) pointLightVolume.LightVolumeSetup.ReinitializeCustomTextures();
                    if (shadowTexturesChanged) pointLightVolume.LightVolumeSetup.ReinitializeShadowTextures();
                }
            }
        }

        // Records objects that can be rewritten when point light texture arrays are reindexed.
        private void RecordTextureReinitializeUndo(LightVolumeSetup lightVolumeSetup) {
            if (lightVolumeSetup.LightVolumeManager != null) Undo.RecordObject(lightVolumeSetup.LightVolumeManager, "Sync Point Light Volume Textures");
            for (int i = 0; i < lightVolumeSetup.PointLightVolumes.Count; i++) {
                PointLightVolume pointLightVolume = lightVolumeSetup.PointLightVolumes[i];
                if (pointLightVolume != null && pointLightVolume.PointLightVolumeInstance != null) Undo.RecordObject(pointLightVolume.PointLightVolumeInstance, "Sync Point Light Volume Textures");
            }
        }

        // Restores runtime mirror data after Unity applies Undo or Redo to the authoring component.
        private void OnUndoRedoPerformed() {
            SyncTargets(false);
            Repaint();
        }

        // Draws the projection source that matches the selected projection and light type.
        private void DrawActiveProjectionSourceField() {
            if (PointLightVolume.Type == PointLightVolume.LightType.AreaLight) {
                DrawTextureMaterialField("Cookie", _textureMaterialHint, false);
                DrawAreaCookieCropControls();
                return;
            }
            if (PointLightVolume.Projection == PointLightVolume.LightProjection.Parametric) return;
            if (PointLightVolume.Projection == PointLightVolume.LightProjection.LUT) {
                DrawTextureMaterialField("FalloffLUT", _textureMaterialHint, false);
            } else if (PointLightVolume.Type == PointLightVolume.LightType.PointLight) {
                DrawTextureMaterialField("Cubemap", _cubemapMaterialHint, false);
            } else if (PointLightVolume.Type == PointLightVolume.LightType.SpotLight) {
                DrawTextureMaterialField("Cookie", _textureMaterialHint, false);
            }
        }

        // Draws the normalized Area Light crop field plus a draggable 16:9 visual picker.
        private void DrawAreaCookieCropControls() {
            SerializedProperty cropProperty = serializedObject.FindProperty("AreaCookieCrop");
            SerializedProperty previewProperty = serializedObject.FindProperty("AreaCookieCropPreview");
            if (previewProperty != null) EditorGUILayout.PropertyField(previewProperty);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(cropProperty);
            if (GUILayout.Button("Full", GUILayout.Width(44f))) {
                cropProperty.vector4Value = new Vector4(0f, 0f, 1f, 1f);
            }
            EditorGUILayout.EndHorizontal();
            UnityEngine.Object previewSource = previewProperty != null ? previewProperty.objectReferenceValue : null;
            DrawAreaCookieCropPreview(cropProperty, previewSource, serializedObject.FindProperty("Cookie").objectReferenceValue);
        }

        // Draws the assigned guide image, cookie image, or a blank 1920x1080 canvas, then applies drag selections to AreaCookieCrop.
        private void DrawAreaCookieCropPreview(SerializedProperty cropProperty, UnityEngine.Object previewSource, UnityEngine.Object cookieSource) {
            float previewAspect = GetAreaCookiePreviewAspect(previewSource, cookieSource);
            float previewHeight = Mathf.Clamp((EditorGUIUtility.currentViewWidth - 40f) / previewAspect, AreaCookiePreviewMinHeight, AreaCookiePreviewMaxHeight);
            Rect previewRect = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect(false, previewHeight));
            Rect canvasRect = FitRectToAspect(previewRect, previewAspect);
            HandleAreaCookieCropPreviewInput(canvasRect, cropProperty);

            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(previewRect, _areaCookiePreviewBackgroundColor);
            Texture previewTexture = GetAreaCookiePreviewTexture(previewSource, cookieSource);
            if (previewTexture != null) {
                EditorGUI.DrawPreviewTexture(canvasRect, previewTexture, null, ScaleMode.StretchToFill);
            } else {
                EditorGUI.DrawRect(canvasRect, Color.black);
            }

            DrawRectOutline(canvasRect, _areaCookiePreviewBorderColor, 1f);
            Rect cropRect = CropToPreviewRect(canvasRect, GetSafeAreaCookieCrop(cropProperty.vector4Value));
            EditorGUI.DrawRect(cropRect, _areaCookieCropFillColor);
            DrawRectOutline(cropRect, _areaCookieCropBorderColor, 2f);
        }

        // Handles click-drag crop selection. Hold Shift to force a square selection in image pixels.
        private void HandleAreaCookieCropPreviewInput(Rect canvasRect, SerializedProperty cropProperty) {
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

        // Clamps a point to a preview rectangle.
        private Vector2 ClampPointToRect(Vector2 point, Rect rect) {
            return new Vector2(Mathf.Clamp(point.x, rect.xMin, rect.xMax), Mathf.Clamp(point.y, rect.yMin, rect.yMax));
        }

        // Draws a crisp IMGUI rectangle outline.
        private void DrawRectOutline(Rect rect, Color color, float thickness) {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        // Draws a named texture/material source field using the original serialized label and tooltip.
        private void DrawTextureMaterialField(string propertyName, string acceptedTypesHint, bool isShadowSource) {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            DrawTextureMaterialField(property, EditorGUIUtility.TrTextContent(property.displayName, property.tooltip), acceptedTypesHint, isShadowSource);
        }

        // Draws and validates a compact texture/material source object field.
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
            if (EditorGUI.EndChangeCheck()) {
                property.objectReferenceValue = IsSupportedTextureMaterialSource(value, isShadowSource) ? value : null;
            }
            UpdateProjectionSourceFromPicker(property, controlID, isShadowSource);
            if (drawHint) DrawProjectionSourceHint(fieldRect, acceptedTypesHint);
            EditorGUI.showMixedValue = false;
            EditorGUI.EndProperty();
        }

        // Opens a filtered native object picker when the selector button is clicked.
        private void ShowProjectionSourcePickerOnSelectorClick(SerializedProperty property, Rect fieldRect, int controlID) {
            Event currentEvent = Event.current;
            if (currentEvent.type != EventType.MouseDown || currentEvent.button != 0) return;
            Rect selectorRect = fieldRect;
            selectorRect.xMin = selectorRect.xMax - ObjectSelectorButtonWidth;
            if (!selectorRect.Contains(currentEvent.mousePosition)) return;
            EditorGUIUtility.ShowObjectPicker<UnityEngine.Object>(property.objectReferenceValue, false, _projectionSourceObjectPickerFilter, controlID);
            currentEvent.Use();
        }

        // Applies a valid value selected through the filtered projection source picker.
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

        // Draws an accepted-types hint over the native empty ObjectField text without covering the native frame or focus state.
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

        // Checks if an object can be used by the selected texture/material source field.
        private bool IsSupportedTextureMaterialSource(UnityEngine.Object value, bool isShadowSource) {
            if (value == null) return true;
            if (isShadowSource) return value is Texture2DArray || value is Cubemap || value is RenderTexture || value is Material;
            if (value is RenderTexture || value is Material) return true;
            if (PointLightVolume.Type == PointLightVolume.LightType.AreaLight) return value is Texture;
            if (PointLightVolume.Projection == PointLightVolume.LightProjection.LUT) return value is Texture;
            if (PointLightVolume.Projection == PointLightVolume.LightProjection.Custom && PointLightVolume.Type == PointLightVolume.LightType.PointLight) return value is Texture;
            if (PointLightVolume.Projection == PointLightVolume.LightProjection.Custom && PointLightVolume.Type == PointLightVolume.LightType.SpotLight) return value is Texture;
            return false;
        }

        private void DrawVolumeGUI(PointLightVolume pointLightVolume) {

            Transform t = pointLightVolume.transform;
            Vector3 origin = t.position;
            Vector3 lscale = pointLightVolume.transform.lossyScale;
            float scale = (lscale.x + lscale.y + lscale.z) / 3;
            float range = pointLightVolume.Type != PointLightVolume.LightType.AreaLight && (pointLightVolume.Projection != PointLightVolume.LightProjection.LUT || pointLightVolume.FalloffLUT == null) ? pointLightVolume.LightSourceSize : pointLightVolume.Range;
            range *= scale;

            if (pointLightVolume.Type == PointLightVolume.LightType.PointLight) { // Point Light Visualization

                // Calculating

                float bounds = 0;

                bool isDebug = pointLightVolume.DebugRange && (pointLightVolume.Projection != PointLightVolume.LightProjection.LUT || pointLightVolume.FalloffLUT == null);

                if (isDebug) {
                    bounds = Mathf.Sqrt(ComputePointLightSquaredBoundingSphere(pointLightVolume.Color, pointLightVolume.Intensity, range, pointLightVolume.LightVolumeSetup.BrightnessCutoff));
                }

                // Drawing

                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.color = new Color(1f, 1f, 0f, 0.6f);
                DrawPointLight(origin, range);
                if (isDebug) {
                    DrawPointLight(origin, bounds);
                }
                DrawShadowClipGUI(pointLightVolume, origin, t);

                Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                Handles.color = new Color(1f, 1f, 0f, 0.15f);
                DrawPointLight(origin, range);
                if (isDebug) {
                    DrawPointLight(origin, bounds);
                }
                DrawShadowClipGUI(pointLightVolume, origin, t);

            } else if (pointLightVolume.Type == PointLightVolume.LightType.SpotLight) { // Spot Light Visualization

                // Calculating

                Vector3 forward = t.forward;
                Vector3 right = t.right;
                Vector3 up = t.up;

                float spotAngle = Mathf.Clamp(pointLightVolume.Angle, 0f, 360f);
                float halfAngleRad = spotAngle * 0.5f * Mathf.Deg2Rad;
                
                Vector3[] dirs = new Vector3[] { right, -right, up, -up };
                float bounds = 0;

                bool isDebug = pointLightVolume.DebugRange && (pointLightVolume.Projection != PointLightVolume.LightProjection.LUT || pointLightVolume.FalloffLUT == null);

                if (isDebug) {
                    bounds = Mathf.Sqrt(ComputePointLightSquaredBoundingSphere(pointLightVolume.Color, pointLightVolume.Intensity, range, pointLightVolume.LightVolumeSetup.BrightnessCutoff));
                }

                // Drawing

                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.color = new Color(1f, 1f, 0f, 0.6f);
                DrawSpotLight(origin, forward, halfAngleRad, range, dirs);

                if (isDebug)
                    DrawSpotLight(origin, forward, halfAngleRad, bounds, dirs);
                DrawShadowClipGUI(pointLightVolume, origin, t);

                Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                Handles.color = new Color(1f, 1f, 0f, 0.15f);
                DrawSpotLight(origin, forward, halfAngleRad, range, dirs);

                if (isDebug) {
                    DrawSpotLight(origin, forward, halfAngleRad, bounds, dirs);
                }
                DrawShadowClipGUI(pointLightVolume, origin, t);

            } else { // Area light

                float x = Mathf.Max(Mathf.Abs(pointLightVolume.transform.lossyScale.x), 0.001f);
                float y = Mathf.Max(Mathf.Abs(pointLightVolume.transform.lossyScale.y), 0.001f);

                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.color = new Color(1f, 1f, 0f, 0.6f);
                DrawAreaLight(origin, t.rotation, x, y);

                if(pointLightVolume.DebugRange)
                    DrawAreaLightDebug(origin, t.rotation, x, y, pointLightVolume.Color, pointLightVolume.Intensity, pointLightVolume.LightVolumeSetup.BrightnessCutoff);
                DrawShadowClipGUI(pointLightVolume, origin, t);

                Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                Handles.color = new Color(1f, 1f, 0f, 0.15f);
                DrawAreaLight(origin, t.rotation, x, y);

                if (pointLightVolume.DebugRange)
                    DrawAreaLightDebug(origin, t.rotation, x, y, pointLightVolume.Color, pointLightVolume.Intensity, pointLightVolume.LightVolumeSetup.BrightnessCutoff);
                DrawShadowClipGUI(pointLightVolume, origin, t);

            }

        }

        void OnSceneGUI() {
            foreach (var obj in Selection.gameObjects) {
                var volume = obj.GetComponent<PointLightVolume>();
                if (volume != null) {
                    DrawVolumeGUI(volume);
                }
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
        private void DrawShadowClipGUI(PointLightVolume pointLightVolume, Vector3 origin, Transform transform) {
            if (!pointLightVolume.Shadows || !pointLightVolume.DebugClipPlanes) return;

            Handles.color = Handles.zTest == UnityEngine.Rendering.CompareFunction.LessEqual ? _shadowClipVisibleColor : _shadowClipHiddenColor;
            float nearClip = pointLightVolume.GetShadowNearClip();
            float farClip = pointLightVolume.GetShadowFarClip();
            bool drawSpotFrustum = pointLightVolume.Type == PointLightVolume.LightType.SpotLight && !pointLightVolume.ShouldBakeCubemapShadows();
            if (!drawSpotFrustum) {
                DrawPointLight(origin, nearClip);
                DrawPointLight(origin, farClip);
                return;
            }

            float halfAngleRad = Mathf.Clamp(pointLightVolume.Angle, 0.1f, 179.9f) * 0.5f * Mathf.Deg2Rad;
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

        private void DrawAreaLight(Vector3 center, Quaternion rotation, float width, float height) {
            Vector3 right = rotation * Vector3.right * (width * 0.5f);
            Vector3 up = rotation * Vector3.up * (height * 0.5f);

            Vector3[] corners = new Vector3[4];
            corners[0] = center + right + up; // Top Right
            corners[1] = center - right + up; // Top Left
            corners[2] = center - right - up; // Bottom Left
            corners[3] = center + right - up; // Bottom Right

            // Draw the rectangle
            Handles.DrawLine(corners[0], corners[1]);
            Handles.DrawLine(corners[1], corners[2]);
            Handles.DrawLine(corners[2], corners[3]);
            Handles.DrawLine(corners[3], corners[0]);
            
            // Draw forward vector
            Handles.DrawLine(center, center + rotation * Vector3.forward * 0.5f);
        }

        private void DrawAreaLightDebug(Vector3 center, Quaternion rotation, float width, float height, Color color, float intensity, float cutoff) {

            // Light normal
            Vector3 up = rotation * Vector3.up;
            Vector3 right = rotation * Vector3.right;
            Vector3 forward = rotation * Vector3.forward;

            // Calculate the bounding sphere of the area light given the cutoff irradiance
            float minSolidAngle = Mathf.Clamp(cutoff / (Mathf.Max(color.r, Mathf.Max(color.g, color.b)) * intensity * Mathf.PI), -Mathf.PI * 2f, Mathf.PI * 2);
            float sqMaxDist = ComputeAreaLightSquaredBoundingSphere(width, height, minSolidAngle);
            float radius = Mathf.Sqrt(sqMaxDist);

            Handles.DrawWireDisc(center, forward, radius);
            Handles.DrawWireArc(center, right, up * radius, 180f, radius);
            Handles.DrawWireArc(center, up, -right * radius, 180f, radius);

        }

        float ComputeAreaLightSquaredBoundingSphere(float width, float height, float minSolidAngle) {
            float A = width * height;
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

        float ComputePointLightSquaredBoundingSphere(Color color, float intensity, float size, float cutoff) {
            float L = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            return Mathf.Max(Mathf.PI * 2 * L * Mathf.Abs(intensity) / (cutoff * cutoff) - 1, 0) * size * size;
        }

    }

}
