using UnityEngine;
using UnityEngine.Rendering;

namespace VRCLightVolumes {
    // Temporary serialized state used to restore a scene after the screen-only lightmapper pass.
    [AddComponentMenu("")]
    public sealed class DomeMeshLightBakeRestoreState : MonoBehaviour {
        public LightVolumeManager Manager;
        public Renderer SourceRenderer;
        public bool SourceRendererEnabled;
        public GameObject[] ContributorObjects;
        public bool[] ContributorActiveStates;
        public Component[] BakeryVolumes;
        public bool[] BakeryVolumeBakeStates;
        public Material[] Materials;
        public int[] MaterialGiFlags;
        public AmbientMode AmbientMode;
        public float AmbientIntensity;
        public Color AmbientSkyColor;
        public string[] TemporaryAssetPaths;
        public GameObject TemporaryEmitter;
        public Component BakeryHelperVolume;
        public float ShadowStrength;
        public float ShadowContrast;
    }
}
