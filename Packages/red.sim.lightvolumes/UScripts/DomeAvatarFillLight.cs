#if !UDONSHARP && (UNITY_EDITOR || COMPILER_UDONSHARP)
#define UDONSHARP
#endif

using UnityEngine;

#if UDONSHARP
using UdonSharp;
using VRC.SDK3.Rendering;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
#endif

namespace VRCLightVolumes {
#if UDONSHARP
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class DomeAvatarFillLight : UdonSharpBehaviour
#else
    public class DomeAvatarFillLight : MonoBehaviour
#endif
    {
        public Texture TargetRenderTexture;
        public Light TargetLight;
        [Range(1f, 30f)] public float UpdatesPerSecond = 12f;
        [Range(1f, 30f)] public float ResponseSpeed = 18f;
        [Range(0f, 1f)] public float ScreenColor = 0.65f;
        [Range(0f, 1f)] public float FollowVideoBrightness = 0.25f;
        [Range(0.25f, 4f)] public float ScreenLightBoost = 2f;
        public Color ColorMultiplier = Color.white;
        public bool AntiFlickering = true;
        public bool FollowClosestScreen = true;
        public Vector3 ScreenCenter;
        public float ScreenRadius = 5f;
        [Range(0f, 2f)] public float ScreenInset = 0.15f;
        [Range(0.25f, 5f)] public float LightDistanceFromAvatar = 2f;
        [HideInInspector] public int SettingsVersion;

#if UDONSHARP
        private Color32[] _pixels;
        private VRCPlayerApi _localPlayer;
#endif
        private RenderTexture _downsampledTexture;
        private Color _smoothedColor = Color.white;
        private float _previousColorTime;
        private float _nextUpdateTime;
        private bool _readbackPending;

        private void Start() {
            if (TargetLight == null) TargetLight = GetComponent<Light>();
            UpgradeSettings();
            if (TargetLight != null) _smoothedColor = TargetLight.color;
#if UDONSHARP
            _localPlayer = Networking.LocalPlayer;
#endif
            _previousColorTime = Time.time;
            _downsampledTexture = new RenderTexture(64, 32, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            _downsampledTexture.useMipMap = true;
            _downsampledTexture.autoGenerateMips = true;
            _downsampledTexture.Create();
#if UDONSHARP
            _pixels = new Color32[1];
#endif
        }

        private void OnDestroy() {
            _readbackPending = false;
            RenderTexture texture = _downsampledTexture;
            _downsampledTexture = null;
            if (texture == null) return;
#if COMPILER_UDONSHARP
            Destroy(texture);
#else
            RenderTexture.active = null;
            texture.Release();
            if (Application.isPlaying) Destroy(texture);
            else DestroyImmediate(texture);
#endif
        }

#if UDONSHARP
        private void Update() {
            UpdateSourcePosition();
            if (_readbackPending || TargetRenderTexture == null || TargetLight == null || !TargetLight.enabled || Time.time < _nextUpdateTime) return;
            _nextUpdateTime = Time.time + 1f / Mathf.Max(UpdatesPerSecond, 1f);
            VRCGraphics.Blit(TargetRenderTexture, _downsampledTexture);
            _readbackPending = true;
            VRCAsyncGPUReadback.Request(_downsampledTexture, _downsampledTexture.mipmapCount - 1, (IUdonEventReceiver)this);
        }

        public override void OnAsyncGpuReadbackComplete(VRCAsyncGPUReadbackRequest request) {
            _readbackPending = false;
            if (!request.hasError && request.TryGetData(_pixels)) SetColor(_pixels[0]);
        }
#else
        private void Update() {
            UpdateSourcePosition();
            if (_readbackPending || TargetRenderTexture == null || TargetLight == null || !TargetLight.enabled || Time.time < _nextUpdateTime) return;
            _nextUpdateTime = Time.time + 1f / Mathf.Max(UpdatesPerSecond, 1f);
            Graphics.Blit(TargetRenderTexture, _downsampledTexture);
            _readbackPending = true;
            UnityEngine.Rendering.AsyncGPUReadback.Request(_downsampledTexture, _downsampledTexture.mipmapCount - 1, OnUnityAsyncGpuReadbackComplete);
        }

        private void OnUnityAsyncGpuReadbackComplete(UnityEngine.Rendering.AsyncGPUReadbackRequest request) {
            _readbackPending = false;
            if (request.hasError) return;
            Unity.Collections.NativeArray<Color32> pixels = request.GetData<Color32>();
            if (pixels.Length > 0) SetColor(pixels[0]);
        }
#endif

        private void SetColor(Color sourceColor) {
            float peak = Mathf.Max(sourceColor.r, Mathf.Max(sourceColor.g, sourceColor.b));
            if (TargetLight == null) return;

            bool isBlackFrame = peak <= 0.001f;
            Color targetColor = Color.black;
            if (!isBlackFrame) {
                Color normalizedColor = sourceColor / peak;
                normalizedColor.a = 1f;
                targetColor = Color.Lerp(normalizedColor, sourceColor, FollowVideoBrightness);
                float luminance = targetColor.r * 0.2126f + targetColor.g * 0.7152f + targetColor.b * 0.0722f;
                targetColor = Color.Lerp(new Color(luminance, luminance, luminance, 1f), targetColor, ScreenColor);
            }
            targetColor *= ColorMultiplier * ScreenLightBoost;
            targetColor.a = 1f;

            float deltaTime = Mathf.Max(Time.time - _previousColorTime, 0f);
            _previousColorTime = Time.time;
            float blend = AntiFlickering && !isBlackFrame ? 1f - Mathf.Exp(-deltaTime * Mathf.Max(ResponseSpeed, 1f)) : 1f;
            _smoothedColor = Color.Lerp(_smoothedColor, targetColor, blend);
            TargetLight.color = _smoothedColor;
        }

        private void UpgradeSettings() {
            if (SettingsVersion < 1) {
                if (UpdatesPerSecond <= 5f) UpdatesPerSecond = 12f;
                ResponseSpeed = 18f;
            }
            if (SettingsVersion < 2) {
                ScreenCenter = transform.position;
                ScreenRadius = TargetLight != null ? Mathf.Max(TargetLight.range * 0.666667f, 0.1f) : 5f;
            }
            if (SettingsVersion < 3) LightDistanceFromAvatar = 2f;
            if (SettingsVersion < 4) ScreenLightBoost = 2f;
            SettingsVersion = 4;
        }

        private void UpdateSourcePosition() {
            if (!FollowClosestScreen || TargetLight == null || ScreenRadius <= 0f) return;
#if UDONSHARP
            if (!Utilities.IsValid(_localPlayer)) _localPlayer = Networking.LocalPlayer;
            if (!Utilities.IsValid(_localPlayer)) return;
            Vector3 receiverPosition = _localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
#else
            Camera viewer = Camera.main;
            if (viewer == null) return;
            Vector3 receiverPosition = viewer.transform.position;
#endif
            Vector3 centerToReceiver = receiverPosition - ScreenCenter;
            if (centerToReceiver.sqrMagnitude <= 0.000001f) return;
            float sourceRadius = Mathf.Max(ScreenRadius - ScreenInset, 0f);
            Vector3 screenPosition = ScreenCenter + centerToReceiver.normalized * sourceRadius;
            Vector3 receiverToScreen = screenPosition - receiverPosition;
            float receiverToScreenDistance = receiverToScreen.magnitude;
            if (receiverToScreenDistance <= LightDistanceFromAvatar) TargetLight.transform.position = screenPosition;
            else TargetLight.transform.position = receiverPosition + receiverToScreen / receiverToScreenDistance * LightDistanceFromAvatar;
        }
    }
}
