using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Scripting.APIUpdating;

namespace RocketFooxball.Runtime.Rendering
{
    /// <summary>Applies quality-dependent state to one gameplay camera. Project assets stay editor-owned.</summary>
    [DisallowMultipleComponent]
    [MovedFrom(false, "RocketFooxball", "RocketFooxball.Runtime", "GraphicsQualityRuntime")]
    public sealed class GraphicsQualityRuntime : MonoBehaviour
    {
        public const int HighQualityIndex = 0;
        public const int LowQualityIndex = 1;

        [SerializeField] private Camera targetCamera;

        private int appliedQualityIndex = -1;

        public Camera TargetCamera => targetCamera;

        private void Awake()
        {
            ApplyCurrentQuality();
        }

        private void OnEnable()
        {
            ApplyCurrentQuality();
        }

        private void Update()
        {
            var qualityIndex = QualitySettings.GetQualityLevel();
            if (qualityIndex != appliedQualityIndex)
            {
                ApplyCurrentQuality();
            }
        }

        /// <summary>Applies current quality immediately; does not change quality index or gameplay state.</summary>
        public void ApplyCurrentQuality()
        {
            if (targetCamera == null)
            {
                targetCamera = GetComponent<Camera>();
                if (targetCamera == null)
                {
                    targetCamera = GetComponentInChildren<Camera>(true);
                }
            }

            if (targetCamera == null)
            {
                appliedQualityIndex = QualitySettings.GetQualityLevel();
                return;
            }

            var qualityIndex = QualitySettings.GetQualityLevel();
            var high = qualityIndex == HighQualityIndex;
            var cameraData = targetCamera.GetUniversalAdditionalCameraData();

            // Camera.allowHDR controls the camera render target. Pipeline HDR is selected by the quality asset.
            targetCamera.allowHDR = high;
            cameraData.antialiasing = high
                ? AntialiasingMode.SubpixelMorphologicalAntiAliasing
                : AntialiasingMode.FastApproximateAntialiasing;
            cameraData.antialiasingQuality = high ? AntialiasingQuality.High : AntialiasingQuality.Low;
            cameraData.renderPostProcessing = high;
            appliedQualityIndex = qualityIndex;
        }
    }
}
