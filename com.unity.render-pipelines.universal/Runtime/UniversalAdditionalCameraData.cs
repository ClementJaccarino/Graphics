using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;
using UnityEngine.Rendering;
using System.ComponentModel;

namespace UnityEngine.Rendering.LWRP
{
    [Obsolete("LWRP -> Universal (UnityUpgradable) -> UnityEngine.Rendering.Universal.UniversalAdditionalCameraData", true)]
    public class LWRPAdditionalCameraData
    {
    }
}

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Holds information about whether to override certain camera rendering options from the render pipeline asset.
    /// When set to <c>Off</c> option will be disabled regardless of what is set on the pipeline asset.
    /// When set to <c>On</c> option will be enabled regardless of what is set on the pipeline asset.
    /// When set to <c>UsePipelineSetting</c> value set in the <see cref="UniversalRenderPipelineAsset"/>.
    /// </summary>
    [MovedFrom("UnityEngine.Rendering.LWRP")] public enum CameraOverrideOption
    {
        Off,
        On,
        UsePipelineSettings,
    }

    //[Obsolete("Renderer override is no longer used, renderers are referenced by index on the pipeline asset.")]
    [MovedFrom("UnityEngine.Rendering.LWRP")] public enum RendererOverrideOption
    {
        Custom,
        UsePipelineSettings,
    }

    /// <summary>
    /// Holds information about the post-processing anti-aliasing mode.
    /// When set to <c>None</c> no post-processing anti-aliasing pass will be performed.
    /// When set to <c>Fast</c> a fast approximated anti-aliasing pass will render when resolving the camera to screen.
    /// When set to <c>SubpixelMorphologicalAntiAliasing</c> SMAA pass will render when resolving the camera to screen. You can choose the SMAA quality by setting <seealso cref="AntialiasingQuality"/>
    /// </summary>
    public enum AntialiasingMode
    {
        None,
        FastApproximateAntialiasing,
        SubpixelMorphologicalAntiAliasing,
        //TemporalAntialiasing
    }

    /// <summary>
    /// Holds information about the render type of a camera. Options are Base or Overlay.
    /// Base rendering type allows the camera to render to either the screen or to a texture.
    /// Overlay rendering type allows the camera to render on top of a previous camera output, thus compositing camera results.
    /// </summary>
    public enum CameraRenderType
    {
        Base,
        Overlay,
    }

    /// <summary>
    /// Controls SMAA anti-aliasing quality.
    /// </summary>
    public enum AntialiasingQuality
    {
        Low,
        Medium,
        High
    }

    /// <summary>
    /// Contains extension methods for Camera class.
    /// </summary>
    public static class CameraExtensions
    {
        /// <summary>
        /// Universal Render Pipeline exposes additional rendering data in a separate component.
        /// This method returns the additional data component for the given camera or create one if it doesn't exists yet.
        /// </summary>
        /// <param name="camera"></param>
        /// <returns>The <c>UniversalAdditinalCameraData</c> for this camera.</returns>
        /// <see cref="UniversalAdditionalCameraData"/>
        public static UniversalAdditionalCameraData GetUniversalAdditionalCameraData(this Camera camera)
        {
            var gameObject = camera.gameObject;
            bool componentExists = gameObject.TryGetComponent<UniversalAdditionalCameraData>(out var cameraData);
            if (!componentExists)
                cameraData = gameObject.AddComponent<UniversalAdditionalCameraData>();

            return cameraData;
        }
    }

    static class CameraTypeUtility
    {
        static string[] s_CameraTypeNames = Enum.GetNames(typeof(CameraRenderType)).ToArray();

        public static string GetName(this CameraRenderType type)
        {
            int typeInt = (int)type;
            if (typeInt < 0 || typeInt >= s_CameraTypeNames.Length)
                typeInt = (int)CameraRenderType.Base;
            return s_CameraTypeNames[typeInt];
        }
    }

    [CustomExtensionName("URP", typeof(UniversalRenderPipeline))]
    [HelpURL(Documentation.baseURL + Documentation.version + Documentation.subURL + "camera-component-reference" + Documentation.endURL)]
    class UniversalCameraExtension : Camera.IExtension
    {
        enum Version
        {
            Unversioned,
            UseCameraExtension,

            Max,
            Last = Max - 1
        }

        private Camera m_Handler;

        [FormerlySerializedAs("renderShadows"), SerializeField]
        bool m_RenderShadows = true;

        [SerializeField]
        CameraOverrideOption m_RequiresDepthTextureOption = CameraOverrideOption.UsePipelineSettings;

        [SerializeField]
        CameraOverrideOption m_RequiresOpaqueTextureOption = CameraOverrideOption.UsePipelineSettings;

        [SerializeField] CameraRenderType m_CameraType = CameraRenderType.Base;
        [SerializeField] List<Camera> m_Cameras = new List<Camera>();
        [SerializeField] int m_RendererIndex = -1;

        [SerializeField] LayerMask m_VolumeLayerMask = 1; // "Default"
        [SerializeField] Transform m_VolumeTrigger = null;

        [SerializeField] bool m_RenderPostProcessing = false;
        [SerializeField] AntialiasingMode m_Antialiasing = AntialiasingMode.None;
        [SerializeField] AntialiasingQuality m_AntialiasingQuality = AntialiasingQuality.High;
        [SerializeField] bool m_StopNaN = false;
        [SerializeField] bool m_Dithering = false;
        [SerializeField] bool m_ClearDepth = true;

        // Deprecated:
        [FormerlySerializedAs("requiresDepthTexture"), SerializeField]
        bool m_RequiresDepthTexture = false;

        [FormerlySerializedAs("requiresColorTexture"), SerializeField]
        bool m_RequiresColorTexture = false;

        [HideInInspector, SerializeField] Version m_Version = Version.Last;

        public float version => (float)m_Version;

        static UniversalCameraExtension s_DefaultAdditionalCameraData = null;
        internal static UniversalCameraExtension defaultAdditionalCameraData
        {
            get
            {
                if (s_DefaultAdditionalCameraData == null)
                    s_DefaultAdditionalCameraData = new UniversalCameraExtension();

                return s_DefaultAdditionalCameraData;
            }
        }

        /// <summary>
        /// Controls if this camera should render shadows.
        /// </summary>
        public bool renderShadows
        {
            get => m_RenderShadows;
            set => m_RenderShadows = value;
        }

        /// <summary>
        /// Controls if a camera should render depth.
        /// The depth is available to be bound in shaders as _CameraDepthTexture.
        /// <seealso cref="CameraOverrideOption"/>
        /// </summary>
        public CameraOverrideOption requiresDepthOption
        {
            get => m_RequiresDepthTextureOption;
            set => m_RequiresDepthTextureOption = value;
        }

        /// <summary>
        /// Controls if a camera should copy the color contents of a camera after rendering opaques.
        /// The color texture is available to be bound in shaders as _CameraOpaqueTexture.
        /// </summary>
        public CameraOverrideOption requiresColorOption
        {
            get => m_RequiresOpaqueTextureOption;
            set => m_RequiresOpaqueTextureOption = value;
        }

        /// <summary>
        /// Returns the camera renderType.
        /// <see cref="CameraRenderType"/>.
        /// </summary>
        public CameraRenderType renderType
        {
            get => m_CameraType;
            set => m_CameraType = value;
        }

        /// <summary>
        /// Returns the camera stack. Only valid for Base cameras.
        /// Overlay cameras have no stack and will return null.
        /// <seealso cref="CameraRenderType"/>.
        /// </summary>
        public List<Camera> cameraStack
        {
            get
            {
                if (IsInactiveExtension())
                {
                    Debug.LogWarning(string.Format("This camera's extension is not currently active."));
                    return null;
                }

                if (renderType != CameraRenderType.Base)
                {
                    var camera = m_Handler.gameObject.GetComponent<Camera>();
                    Debug.LogWarning(string.Format("{0}: This camera is of {1} type. Only Base cameras can have a camera stack.", camera.name, renderType));
                    return null;
                }

                if (scriptableRenderer.supportedRenderingFeatures.cameraStacking == false)
                {
                    var camera = m_Handler.gameObject.GetComponent<Camera>();
                    Debug.LogWarning(string.Format("{0}: This camera has a ScriptableRenderer that doesn't support camera stacking. Camera stack is null.", camera.name));
                    return null;
                }

                return m_Cameras;
            }
        }

        /// <summary>
        /// If true, this camera will clear depth value before rendering. Only valid for Overlay cameras.
        /// </summary>
        public bool clearDepth
        {
            get => m_ClearDepth;
        }

        /// <summary>
        /// Returns true if this camera needs to render depth information in a texture.
        /// If enabled, depth texture is available to be bound and read from shaders as _CameraDepthTexture after rendering skybox.
        /// </summary>
        public bool requiresDepthTexture
        {
            get
            {
                if (m_RequiresDepthTextureOption == CameraOverrideOption.UsePipelineSettings)
                {
                    return UniversalRenderPipeline.asset.supportsCameraDepthTexture;
                }
                else
                {
                    return m_RequiresDepthTextureOption == CameraOverrideOption.On;
                }
            }
            set { m_RequiresDepthTextureOption = (value) ? CameraOverrideOption.On : CameraOverrideOption.Off; }
        }

        /// <summary>
        /// Returns true if this camera requires to color information in a texture.
        /// If enabled, color texture is available to be bound and read from shaders as _CameraOpaqueTexture after rendering skybox.
        /// </summary>
        public bool requiresColorTexture
        {
            get
            {
                if (m_RequiresOpaqueTextureOption == CameraOverrideOption.UsePipelineSettings)
                {
                    return UniversalRenderPipeline.asset.supportsCameraOpaqueTexture;
                }
                else
                {
                    return m_RequiresOpaqueTextureOption == CameraOverrideOption.On;
                }
            }
            set { m_RequiresOpaqueTextureOption = (value) ? CameraOverrideOption.On : CameraOverrideOption.Off; }
        }

        /// <summary>
        /// Returns the <see cref="ScriptableRenderer"/> that is used to render this camera.
        /// </summary>
        public ScriptableRenderer scriptableRenderer
        {
            get => UniversalRenderPipeline.asset.GetRenderer(m_RendererIndex);
        }

        /// <summary>
        /// Use this to set this Camera's current <see cref="ScriptableRenderer"/> to one listed on the Render Pipeline Asset. Takes an index that maps to the list on the Render Pipeline Asset.
        /// </summary>
        /// <param name="index">The index that maps to the RendererData list on the currently assigned Render Pipeline Asset</param>
        public void SetRenderer(int index)
        {
            m_RendererIndex = index;
        }

        public LayerMask volumeLayerMask
        {
            get => m_VolumeLayerMask;
            set => m_VolumeLayerMask = value;
        }

        public Transform volumeTrigger
        {
            get => m_VolumeTrigger;
            set => m_VolumeTrigger = value;
        }

        /// <summary>
        /// Returns true if this camera should render post-processing.
        /// </summary>
        public bool renderPostProcessing
        {
            get => m_RenderPostProcessing;
            set => m_RenderPostProcessing = value;
        }

        /// <summary>
        /// Returns the current anti-aliasing mode used by this camera.
        /// <see cref="AntialiasingMode"/>.
        /// </summary>
        public AntialiasingMode antialiasing
        {
            get => m_Antialiasing;
            set => m_Antialiasing = value;
        }

        /// <summary>
        /// Returns the current anti-aliasing quality used by this camera.
        /// <seealso cref="antialiasingQuality"/>.
        /// </summary>
        public AntialiasingQuality antialiasingQuality
        {
            get => m_AntialiasingQuality;
            set => m_AntialiasingQuality = value;
        }

        public bool stopNaN
        {
            get => m_StopNaN;
            set => m_StopNaN = value;
        }

        public bool dithering
        {
            get => m_Dithering;
            set => m_Dithering = value;
        }

        void Awake(Camera camera) => m_Handler = camera;

        void OnDisable() => m_Handler = null;

        public bool IsActiveExtension() => m_Handler != null;
        public bool IsInactiveExtension() => m_Handler == null;
        
        public void OnDrawGizmos()
        {
            if (IsInactiveExtension())
                return;

            string path = "Packages/com.unity.render-pipelines.universal/Editor/Gizmos/";
            string gizmoName = "";
            Color tint = Color.white;

            if (m_CameraType == CameraRenderType.Base)
            {
                gizmoName = $"{path}Camera_Base.png";
            }
            else if (m_CameraType == CameraRenderType.Overlay)
            {
                gizmoName = $"{path}Camera_Overlay.png";
            }

#if UNITY_2019_2_OR_NEWER
#if UNITY_EDITOR
            if (Selection.activeObject == m_Handler.gameObject)
            {
                // Get the preferences selection color
                tint = SceneView.selectedOutlineColor;
            }
#endif
            if (!string.IsNullOrEmpty(gizmoName))
            {
                Gizmos.DrawIcon(m_Handler.transform.position, gizmoName, true, tint);
            }

            if (renderPostProcessing)
            {
                Gizmos.DrawIcon(m_Handler.transform.position, $"{path}Camera_PostProcessing.png", true, tint);
            }
#else
            if (renderPostProcessing)
            {
                Gizmos.DrawIcon(transform.position, $"{path}Camera_PostProcessing.png");
            }
            Gizmos.DrawIcon(transform.position, gizmoName);
#endif
        }

        internal void InitFromMigration(
            bool renderShadows,
            CameraOverrideOption requiresDepthTextureOption,
            CameraOverrideOption requiresOpaqueTextureOption,
            CameraRenderType cameraType,
            List<Camera> cameras,
            int rendererIndex,
            LayerMask volumeLayerMask,
            Transform volumeTrigger,
            bool renderPostProcessing,
            AntialiasingMode antialiasing,
            AntialiasingQuality antialiasingQuality,
            bool stopNaN,
            bool dithering,
            bool clearDepth,
            bool requiresDepthTexture,
            bool requiresColorTexture
            )
        {
            m_RenderShadows = renderShadows;
            m_RequiresDepthTextureOption = requiresDepthTextureOption;
            m_RequiresOpaqueTextureOption = requiresOpaqueTextureOption;
            m_CameraType = cameraType;
            m_Cameras = cameras;
            m_RendererIndex = rendererIndex;
            m_VolumeLayerMask = volumeLayerMask;
            m_VolumeTrigger = volumeTrigger;
            m_RenderPostProcessing = renderPostProcessing;
            m_Antialiasing = antialiasing;
            m_AntialiasingQuality = antialiasingQuality;
            m_StopNaN = stopNaN;
            m_Dithering = dithering;
            m_ClearDepth = clearDepth;
            m_RequiresDepthTexture = requiresDepthTexture;
            m_RequiresColorTexture = requiresColorTexture;
        }
    }

    [Obsolete("Use UniversalCameraExtension instead.")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [ImageEffectAllowedInSceneView]
    [MovedFrom("UnityEngine.Rendering.LWRP")] public class UniversalAdditionalCameraData : MonoBehaviour, ISerializationCallbackReceiver
    {
        private UniversalCameraExtension redirect
        {
            get
            {
                var extension = GetComponent<Camera>()?.GetExtension<UniversalCameraExtension>();
                if (extension == null)
                    throw new Exception($"There is no {typeof(UniversalCameraExtension)} extension on this Camera");
                return extension;
            }
        }

        [FormerlySerializedAs("renderShadows"), SerializeField, Obsolete("Keeped for migration only, use UniversalCameraExtension")]
        bool m_RenderShadows = true;

        [SerializeField, Obsolete("Keeped for migration only, use UniversalCameraExtension")]
        CameraOverrideOption m_RequiresDepthTextureOption = CameraOverrideOption.UsePipelineSettings;

        [SerializeField, Obsolete("Keeped for migration only, use UniversalCameraExtension")]
        CameraOverrideOption m_RequiresOpaqueTextureOption = CameraOverrideOption.UsePipelineSettings;

        [SerializeField, Obsolete("Keeped for migration only, use UniversalCameraExtension")]
        CameraRenderType m_CameraType = CameraRenderType.Base;
		[SerializeField, Obsolete("Keeped for migration only, use UniversalCameraExtension")]
        List<Camera> m_Cameras = new List<Camera>();
		[SerializeField, Obsolete("Keeped for migration only, use UniversalCameraExtension")]
        int m_RendererIndex = -1;

        [SerializeField, Obsolete("Keeped for migration only, use UniversalCameraExtension")]
        LayerMask m_VolumeLayerMask = 1; // "Default"
        [SerializeField, Obsolete("Keeped for migration only, use UniversalCameraExtension")]
        Transform m_VolumeTrigger = null;

        [SerializeField, Obsolete("Keeped for migration only, use UniversalCameraExtension")]
        bool m_RenderPostProcessing = false;
        [SerializeField, Obsolete("Keeped for migration only, use UniversalCameraExtension")]
        AntialiasingMode m_Antialiasing = AntialiasingMode.None;
        [SerializeField, Obsolete("Keeped for migration only, use UniversalCameraExtension")]
        AntialiasingQuality m_AntialiasingQuality = AntialiasingQuality.High;
        [SerializeField, Obsolete("Keeped for migration only, use UniversalCameraExtension")]
        bool m_StopNaN = false;
        [SerializeField, Obsolete("Keeped for migration only, use UniversalCameraExtension")]
        bool m_Dithering = false;
        [SerializeField, Obsolete("Keeped for migration only, use UniversalCameraExtension")]
        bool m_ClearDepth = true;

        // Deprecated:
        [FormerlySerializedAs("requiresDepthTexture"), SerializeField, Obsolete("Keeped for migration only, use UniversalCameraExtension")]
        bool m_RequiresDepthTexture = false;

        [FormerlySerializedAs("requiresColorTexture"), SerializeField, Obsolete("Keeped for migration only, use UniversalCameraExtension")]
        bool m_RequiresColorTexture = false;

        [HideInInspector, SerializeField, Obsolete("Keeped for migration only, use UniversalCameraExtension")]
        float m_Version = 2;
        
        public float version
#pragma warning disable CS0618 // Type or member is obsolete
            => m_Version;
#pragma warning restore CS0618 // Type or member is obsolete

        /// <summary>
        /// Controls if this camera should render shadows.
        /// </summary>
        [Obsolete("Use directly UniversalCameraExtension.renderShadows instead.")]
        public bool renderShadows
        {
            get => redirect.renderShadows;
            set => redirect.renderShadows = value;
        }

        /// <summary>
        /// Controls if a camera should render depth.
        /// The depth is available to be bound in shaders as _CameraDepthTexture.
        /// <seealso cref="CameraOverrideOption"/>
        /// </summary>
        [Obsolete("Use directly UniversalCameraExtension.requiresDepthOption instead.")]
        public CameraOverrideOption requiresDepthOption
        {
            get => redirect.requiresDepthOption;
            set => redirect.requiresDepthOption = value;
        }

        /// <summary>
        /// Controls if a camera should copy the color contents of a camera after rendering opaques.
        /// The color texture is available to be bound in shaders as _CameraOpaqueTexture.
        /// </summary>
        [Obsolete("Use directly UniversalCameraExtension.requiresColorOption instead.")]
        public CameraOverrideOption requiresColorOption
        {
            get => redirect.requiresColorOption;
            set => redirect.requiresColorOption = value;
        }

        /// <summary>
        /// Returns the camera renderType.
        /// <see cref="CameraRenderType"/>.
        /// </summary>
        [Obsolete("Use directly UniversalCameraExtension.renderType instead.")]
        public CameraRenderType renderType
        {
            get => redirect.renderType;
            set => redirect.renderType = value;
        }

        /// <summary>
        /// Returns the camera stack. Only valid for Base cameras.
        /// Overlay cameras have no stack and will return null.
        /// <seealso cref="CameraRenderType"/>.
        /// </summary>
        [Obsolete("Use directly UniversalCameraExtension.cameraStack instead.")]
        public List<Camera> cameraStack => redirect.cameraStack;

        /// <summary>
        /// If true, this camera will clear depth value before rendering. Only valid for Overlay cameras.
        /// </summary>
        [Obsolete("Use directly UniversalCameraExtension.clearDepth instead.")]
        public bool clearDepth => redirect.clearDepth;

        /// <summary>
        /// Returns true if this camera needs to render depth information in a texture.
        /// If enabled, depth texture is available to be bound and read from shaders as _CameraDepthTexture after rendering skybox.
        /// </summary>
        [Obsolete("Use directly UniversalCameraExtension.requiresDepthTexture instead.")]
        public bool requiresDepthTexture
        {
            get => redirect.requiresDepthTexture;
            set => redirect.requiresDepthTexture = value;
        }

        /// <summary>
        /// Returns true if this camera requires to color information in a texture.
        /// If enabled, color texture is available to be bound and read from shaders as _CameraOpaqueTexture after rendering skybox.
        /// </summary>
        [Obsolete("Use directly UniversalCameraExtension.requiresColorTexture instead.")]
        public bool requiresColorTexture
        {
            get => redirect.requiresColorTexture;
            set => redirect.requiresColorTexture = value;
        }

        /// <summary>
        /// Returns the <see cref="ScriptableRenderer"/> that is used to render this camera.
        /// </summary>
        [Obsolete("Use directly UniversalCameraExtension.scriptableRenderer instead.")]
        public ScriptableRenderer scriptableRenderer => redirect.scriptableRenderer;

        /// <summary>
        /// Use this to set this Camera's current <see cref="ScriptableRenderer"/> to one listed on the Render Pipeline Asset. Takes an index that maps to the list on the Render Pipeline Asset.
        /// </summary>
        /// <param name="index">The index that maps to the RendererData list on the currently assigned Render Pipeline Asset</param>
        [Obsolete("Use directly UniversalCameraExtension.SetRenderer(int) instead.")]
        public void SetRenderer(int index) => redirect.SetRenderer(index);

        [Obsolete("Use directly UniversalCameraExtension.volumeLayerMask instead.")]
        public LayerMask volumeLayerMask
        {
            get => redirect.volumeLayerMask;
            set => redirect.volumeLayerMask = value;
        }

        [Obsolete("Use directly UniversalCameraExtension.volumeTrigger instead.")]
        public Transform volumeTrigger
        {
            get => redirect.volumeTrigger;
            set => redirect.volumeTrigger = value;
        }

        /// <summary>
        /// Returns true if this camera should render post-processing.
        /// </summary>
        [Obsolete("Use directly UniversalCameraExtension.renderPostProcessing instead.")]
        public bool renderPostProcessing
        {
            get => redirect.renderPostProcessing;
            set => redirect.renderPostProcessing = value;
        }

        /// <summary>
        /// Returns the current anti-aliasing mode used by this camera.
        /// <see cref="AntialiasingMode"/>.
        /// </summary>
        [Obsolete("Use directly UniversalCameraExtension.antialiasing instead.")]
        public AntialiasingMode antialiasing
        {
            get => redirect.antialiasing;
            set => redirect.antialiasing = value;
        }

        /// <summary>
        /// Returns the current anti-aliasing quality used by this camera.
        /// <seealso cref="antialiasingQuality"/>.
        /// </summary>
        [Obsolete("Use directly UniversalCameraExtension.antialiasingQuality instead.")]
        public AntialiasingQuality antialiasingQuality
        {
            get => redirect.antialiasingQuality;
            set => redirect.antialiasingQuality = value;
        }

        [Obsolete("Use directly UniversalCameraExtension.stopNaN instead.")]
        public bool stopNaN
        {
            get => redirect.stopNaN;
            set => redirect.stopNaN = value;
        }

        [Obsolete("Use directly UniversalCameraExtension.dithering instead.")]
        public bool dithering
        {
            get => redirect.dithering;
            set => redirect.dithering = value;
        }
        
        public void OnBeforeSerialize() { }
        
        public void OnAfterDeserialize()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if (m_Version <= 1) //use raw accessor for migration as camera extension is not available at this moment
            {
                m_RequiresDepthTextureOption = (m_RequiresDepthTexture) ? CameraOverrideOption.On : CameraOverrideOption.Off;
                m_RequiresOpaqueTextureOption = (m_RequiresColorTexture) ? CameraOverrideOption.On : CameraOverrideOption.Off;
                m_Version = 2;
            }
#pragma warning restore CS0618 // Type or member is obsolete
        }

        void Awake()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if (m_Version <= 2)
#pragma warning restore CS0618 // Type or member is obsolete
            {
                Camera cam = GetComponent<Camera>();
                UniversalCameraExtension extension;
                if (cam.HasExtension<UniversalCameraExtension>())
                    extension = cam.GetExtension<UniversalCameraExtension>();
                else
                    extension = cam.CreateExtension<UniversalCameraExtension>();

                extension.InitFromMigration(
#pragma warning disable CS0618 // Type or member is obsolete
                    m_RenderShadows,
                    m_RequiresDepthTextureOption,
                    m_RequiresOpaqueTextureOption,
                    m_CameraType,
                    m_Cameras,
                    m_RendererIndex,
                    m_VolumeLayerMask,
                    m_VolumeTrigger,
                    m_RenderPostProcessing,
                    m_Antialiasing,
                    m_AntialiasingQuality,
                    m_StopNaN,
                    m_Dithering,
                    m_ClearDepth,
                    m_RequiresDepthTexture,
                    m_RequiresColorTexture
#pragma warning restore CS0618 // Type or member is obsolete
                );
                
#pragma warning disable CS0618 // Type or member is obsolete
                m_Version = 3;
#pragma warning restore CS0618 // Type or member is obsolete
            }
        }
    }
}
