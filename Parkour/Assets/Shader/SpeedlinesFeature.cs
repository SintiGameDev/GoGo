using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// ScriptableRendererFeature fuer den Speedlines Fullscreen-Effekt.
// Setup: URP Renderer Asset (z.B. "Universal Renderer Data") auswaehlen ->
// "Add Renderer Feature" -> "Speedlines Feature". Material-Feld im Inspector
// mit einem Material zuweisen, das auf Speedlines.shader basiert.
public class SpeedlinesFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public Material speedlinesMaterial;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    }

    public Settings settings = new Settings();

    private SpeedlinesPass pass;

    public override void Create()
    {
        pass = new SpeedlinesPass(settings.speedlinesMaterial)
        {
            renderPassEvent = settings.renderPassEvent
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.speedlinesMaterial == null) return;
        if (renderingData.cameraData.cameraType != CameraType.Game) return;

        // WICHTIG: cameraColorTargetHandle darf NICHT hier abgerufen werden ->
        // "You can only call cameraColorTargetHandle inside the scope of a
        // ScriptableRenderPass". Stattdessen die renderer-Referenz an den Pass
        // uebergeben und das Handle erst in Execute() (innerhalb des Passes) holen.
        pass.SetRenderer(renderer);
        renderer.EnqueuePass(pass);
    }

    private class SpeedlinesPass : ScriptableRenderPass
    {
        private readonly Material material;
        private ScriptableRenderer renderer;
        private RTHandle tempTexture;

        public SpeedlinesPass(Material mat)
        {
            material = mat;
        }

        public void SetRenderer(ScriptableRenderer activeRenderer)
        {
            renderer = activeRenderer;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            RenderingUtils.ReAllocateIfNeeded(ref tempTexture, desc, name: "_SpeedlinesTemp");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (material == null || renderer == null) return;

            // Handle erst hier abrufen -- wir sind jetzt innerhalb des Pass-Scopes
            RTHandle source = renderer.cameraColorTargetHandle;
            if (source == null) return;

            CommandBuffer cmd = CommandBufferPool.Get("Speedlines");

            Blitter.BlitCameraTexture(cmd, source, tempTexture, material, 0);
            Blitter.BlitCameraTexture(cmd, tempTexture, source);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
        }
    }

    protected override void Dispose(bool disposing)
    {
        pass = null;
    }
}
