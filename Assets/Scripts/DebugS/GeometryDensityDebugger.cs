using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class GeometryDensityDebugger : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public float threshold = 0.5f;
        public Color withinColor = new Color(0f, 1f, 0f, 0.4f);
        public Color exceedColor = new Color(1f, 0f, 0f, 0.4f);
    }

    class GeometryDensityPass : ScriptableRenderPass
    {
        ComputeShader compute;
        Shader blendShader;
        Settings settings;
        int overlayID = Shader.PropertyToID("_GeometryDensityOverlay");
        Material blendMaterial;

        public GeometryDensityPass(ComputeShader compute, Shader blendShader, Settings settings)
        {
            this.compute = compute;
            this.blendShader = blendShader;
            this.settings = settings;
            if (blendShader != null)
                blendMaterial = CoreUtils.CreateEngineMaterial(blendShader);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.enableRandomWrite = true;
            desc.msaaSamples = 1; // compute shaders cannot write to MSAA targets
            desc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm;
            desc.depthBufferBits = 0;
            cmd.GetTemporaryRT(overlayID, desc, FilterMode.Point);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (compute == null || blendMaterial == null)
                return;

            Camera cam = renderingData.cameraData.camera;
            int totalVertices = 0;
            foreach (var r in Object.FindObjectsOfType<Renderer>())
            {
                if (!r.isVisible)
                    continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                    totalVertices += mf.sharedMesh.vertexCount;
                var smr = r as SkinnedMeshRenderer;
                if (smr != null && smr.sharedMesh != null)
                    totalVertices += smr.sharedMesh.vertexCount;
            }
            float screenArea = (float)cam.pixelWidth * cam.pixelHeight;
            float ratio = screenArea > 0 ? totalVertices / screenArea : 0f;
            Color overlayColor = ratio > settings.threshold ? settings.exceedColor : settings.withinColor;

            int kernel = compute.FindKernel("CSMain");
            CommandBuffer cmd = CommandBufferPool.Get("GeometryDensity");
            cmd.SetComputeVectorParam(compute, "_Color", overlayColor);
            cmd.SetComputeTextureParam(compute, kernel, "Result", overlayID);
            int tgx = Mathf.CeilToInt(cam.pixelWidth / 8f);
            int tgy = Mathf.CeilToInt(cam.pixelHeight / 8f);
            cmd.DispatchCompute(compute, kernel, tgx, tgy, 1);

            var source = renderingData.cameraData.renderer.cameraColorTarget;
            Blit(cmd, overlayID, source, blendMaterial);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            if (cmd == null) return;
            cmd.ReleaseTemporaryRT(overlayID);
        }
    }

    public Settings settings = new Settings();
    ComputeShader computeShader;
    Shader blendShader;
    GeometryDensityPass pass;

    public override void Create()
    {
        computeShader = Resources.Load<ComputeShader>("GeometryDensityOverlay");
        blendShader = Resources.Load<Shader>("GeometryDensityBlend");
        pass = new GeometryDensityPass(computeShader, blendShader, settings)
        {
            renderPassEvent = RenderPassEvent.AfterRendering
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (computeShader == null || blendShader == null)
            return;
        renderer.EnqueuePass(pass);
    }
}
