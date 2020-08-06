namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Applies relevant settings before rendering transparent objects
    /// </summary>

    internal class TransparentSettingsPass : ScriptableRenderPass
    {
        bool m_shouldReceiveShadows;

        const string m_ProfilerTag = "Transparent Settings Pass";
        private static readonly ProfilingSampler m_ProfilingSampler = new ProfilingSampler(m_ProfilerTag);

        public TransparentSettingsPass(RenderPassEvent evt, bool shadowReceiveSupported)
        {
            renderPassEvent = evt;
            m_shouldReceiveShadows = shadowReceiveSupported;
        }

        public bool Setup(ref RenderingData renderingData)
        {
            // Currently we only need to enqueue this pass when the user
            // doesn't want transparent objects to receive shadows
            return !m_shouldReceiveShadows;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // Get a command buffer...
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                ShadowData shadowData = renderingData.shadowData;
                int cascadesCount = shadowData.mainLightShadowCascadesCount;

                bool shouldReceiveNoCascades = m_shouldReceiveShadows && cascadesCount == 1;
                bool shouldReceiveCascades = m_shouldReceiveShadows && cascadesCount > 1;

                // Toggle light shadows enabled based on the renderer setting set in the constructor
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadows, shouldReceiveNoCascades);
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowsCascades, shouldReceiveCascades);
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.AdditionalLightShadows, m_shouldReceiveShadows);
            }

            // Execute and release the command buffer...
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
