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
        [Range(1f, 15f)] public float UpdatesPerSecond = 5f;
        [Range(0f, 1f)] public float ScreenColor = 0.65f;
        [Range(0f, 1f)] public float FollowVideoBrightness = 0.25f;
        public Color ColorMultiplier = Color.white;
        public bool AntiFlickering = true;

#if UDONSHARP
        private Color32[] _pixels;
#endif
        private RenderTexture _downsampledTexture;
        private Color _smoothedColor = Color.white;
        private float _previousColorTime;
        private float _nextUpdateTime;
        private bool _readbackPending;

        private void Start() {
            if (TargetLight == null) TargetLight = GetComponent<Light>();
            if (TargetLight != null) _smoothedColor = TargetLight.color;
            _previousColorTime = Time.time;
            _downsampledTexture = new RenderTexture(64, 32, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) {
                useMipMap = true,
                autoGenerateMips = true
            };
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
            if (peak <= 0.001f || TargetLight == null) return;

            Color normalizedColor = sourceColor / peak;
            normalizedColor.a = 1f;
            Color targetColor = Color.Lerp(normalizedColor, sourceColor, FollowVideoBrightness);
            float luminance = targetColor.r * 0.2126f + targetColor.g * 0.7152f + targetColor.b * 0.0722f;
            targetColor = Color.Lerp(new Color(luminance, luminance, luminance, 1f), targetColor, ScreenColor) * ColorMultiplier;
            targetColor.a = 1f;

            float deltaTime = Mathf.Max(Time.time - _previousColorTime, 0f);
            _previousColorTime = Time.time;
            float blend = AntiFlickering ? 1f - Mathf.Exp(-deltaTime * 6f) : 1f;
            _smoothedColor = Color.Lerp(_smoothedColor, targetColor, blend);
            TargetLight.color = _smoothedColor;
        }
    }
}
