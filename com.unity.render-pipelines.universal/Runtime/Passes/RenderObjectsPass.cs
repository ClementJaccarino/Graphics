using System.Collections.Generic;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;
using UnityEngine.Scripting.APIUpdating;

namespace UnityEngine.Experimental.Rendering.Universal
{
    [MovedFrom("UnityEngine.Experimental.Rendering.LWRP")] public class RenderObjectsPass : ScriptableRenderPass
    {
        RenderQueueType renderQueueType;
        FilteringSettings m_FilteringSettings;
        RenderObjects.CustomCameraSettings m_CameraSettings;
        string m_ProfilerTag;

        public Material overrideMaterial { get; set; }
        public int overrideMaterialPassIndex { get; set; }

        List<ShaderTagId> m_ShaderTagIdList = new List<ShaderTagId>();

        public void SetDetphState(bool writeEnabled, CompareFunction function = CompareFunction.Less)
        {
            m_RenderStateBlock.mask |= RenderStateMask.Depth;
            m_RenderStateBlock.depthState = new DepthState(writeEnabled, function);
        }

        public void SetStencilState(int reference, CompareFunction compareFunction, StencilOp passOp, StencilOp failOp, StencilOp zFailOp)
        {
            StencilState stencilState = StencilState.defaultValue;
            stencilState.enabled = true;
            stencilState.SetCompareFunction(compareFunction);
            stencilState.SetPassOperation(passOp);
            stencilState.SetFailOperation(failOp);
            stencilState.SetZFailOperation(zFailOp);

            m_RenderStateBlock.mask |= RenderStateMask.Stencil;
            m_RenderStateBlock.stencilReference = reference;
            m_RenderStateBlock.stencilState = stencilState;
        }

        RenderStateBlock m_RenderStateBlock;

        public RenderObjectsPass(string profilerTag, RenderPassEvent renderPassEvent, string[] shaderTags, RenderQueueType renderQueueType, int layerMask, RenderObjects.CustomCameraSettings cameraSettings)
        {
            m_ProfilerTag = profilerTag;
            this.renderPassEvent = renderPassEvent;
            this.renderQueueType = renderQueueType;
            this.overrideMaterial = null;
            this.overrideMaterialPassIndex = 0;
            RenderQueueRange renderQueueRange = (renderQueueType == RenderQueueType.Transparent)
                ? RenderQueueRange.transparent
                : RenderQueueRange.opaque;
            m_FilteringSettings = new FilteringSettings(renderQueueRange, layerMask);

            if (shaderTags != null && shaderTags.Length > 0)
            {
                foreach (var passName in shaderTags)
                    m_ShaderTagIdList.Add(new ShaderTagId(passName));
            }
            else
            {
                m_ShaderTagIdList.Add(new ShaderTagId("UniversalForward"));
                m_ShaderTagIdList.Add(new ShaderTagId("LightweightForward"));
                m_ShaderTagIdList.Add(new ShaderTagId("SRPDefaultUnlit"));
            }

            m_RenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
            m_CameraSettings = cameraSettings;

        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            SortingCriteria sortingCriteria = (renderQueueType == RenderQueueType.Transparent)
                ? SortingCriteria.CommonTransparent
                : renderingData.cameraData.defaultOpaqueSortFlags;

            DrawingSettings drawingSettings = CreateDrawingSettings(m_ShaderTagIdList, ref renderingData, sortingCriteria);
            drawingSettings.overrideMaterial = overrideMaterial;
            drawingSettings.overrideMaterialPassIndex = overrideMaterialPassIndex;

            Camera camera = renderingData.cameraData.camera;
            float cameraAspect = (float) camera.pixelWidth / (float) camera.pixelHeight;
            CommandBuffer cmd = CommandBufferPool.Get(m_ProfilerTag);
            using (new ProfilingSample(cmd, m_ProfilerTag))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                if (URPCameraMode.isPureURP)
                {
                    // XRTODO: Enable pure mode globally in UniversalRenderPipeline.cs
                    cmd.EnableGlobalShaderKeyword("UNITY_PURE_URP_ON");

                    Matrix4x4 viewMatrix = camera.worldToCameraMatrix;
                    Matrix4x4 projectionMatrix = camera.projectionMatrix;
                    if (m_CameraSettings.overrideCamera)
                    {
                        projectionMatrix = Matrix4x4.Perspective(m_CameraSettings.cameraFieldOfView, cameraAspect,
                            camera.nearClipPlane, camera.farClipPlane);

                        viewMatrix = camera.worldToCameraMatrix;
                        Vector4 cameraTranslation = viewMatrix.GetColumn(3);
                        viewMatrix.SetColumn(3, cameraTranslation + m_CameraSettings.offset);
                    }
                    projectionMatrix = GL.GetGPUProjectionMatrix(projectionMatrix, true);
                    Matrix4x4 viewProjMatrix = projectionMatrix * viewMatrix;
                    Matrix4x4 invViewProjMatrix = Matrix4x4.Inverse(viewProjMatrix);

                    cmd.SetGlobalMatrix(Shader.PropertyToID("_ViewMatrix"), viewMatrix);
                    cmd.SetGlobalMatrix(Shader.PropertyToID("_InvViewMatrix"), Matrix4x4.Inverse(viewMatrix));
                    cmd.SetGlobalMatrix(Shader.PropertyToID("_ProjMatrix"), projectionMatrix);
                    cmd.SetGlobalMatrix(Shader.PropertyToID("_InvProjMatrix"), Matrix4x4.Inverse(projectionMatrix));
                    cmd.SetGlobalMatrix(Shader.PropertyToID("_ViewProjMatrix"), viewProjMatrix);
                    cmd.SetGlobalMatrix(Shader.PropertyToID("_InvViewProjMatrix"), Matrix4x4.Inverse(viewProjMatrix));

                    // XRTODO: deprecate and remove cmd.SetViewProjectionMatrices once custom passes are moved to pure URP land
                    // Right now this need to be called to not break custom render feature that uses custom material
                    cmd.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
                    
                    context.ExecuteCommandBuffer(cmd);
                }
                else
                {
                    if (m_CameraSettings.overrideCamera)
                    {
                        Matrix4x4 projectionMatrix = Matrix4x4.Perspective(m_CameraSettings.cameraFieldOfView, cameraAspect,
                            camera.nearClipPlane, camera.farClipPlane);

                        Matrix4x4 viewMatrix = camera.worldToCameraMatrix;
                        Vector4 cameraTranslation = viewMatrix.GetColumn(3);
                        viewMatrix.SetColumn(3, cameraTranslation + m_CameraSettings.offset);
                        cmd.SetViewProjectionMatrices(viewMatrix, projectionMatrix);

                        context.ExecuteCommandBuffer(cmd);
                    }
                }

                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref m_FilteringSettings,
                    ref m_RenderStateBlock);

                if (URPCameraMode.isPureURP)
                {
                    if (m_CameraSettings.overrideCamera && m_CameraSettings.restoreCamera)
                    {
                        Matrix4x4 projectionMatrix = Matrix4x4.Perspective(camera.fieldOfView, cameraAspect,
                            camera.nearClipPlane, camera.farClipPlane);

                        cmd.Clear();
                        // XRTODO: deprecate and remove cmd.SetViewProjectionMatrices once custom passes are moved to pure URP land
                        cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, projectionMatrix);
                    }
                    // XRTODO: Remove this once pure mode is on globally
                    cmd.DisableGlobalShaderKeyword("UNITY_PURE_URP_ON");
                }
                else
                {
                    if (m_CameraSettings.overrideCamera && m_CameraSettings.restoreCamera)
                    {
                        Matrix4x4 projectionMatrix = Matrix4x4.Perspective(camera.fieldOfView, cameraAspect,
                            camera.nearClipPlane, camera.farClipPlane);

                        cmd.Clear();
                        {
                            cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, projectionMatrix);
                        }
                    }
                }
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
