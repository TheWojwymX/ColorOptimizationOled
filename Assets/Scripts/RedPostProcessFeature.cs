using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class RedPostProcessFeature : ScriptableRendererFeature
{
    class RedPass : ScriptableRenderPass
    {
        public ComputeShader computeShader;

        class PassData
        {
            public ComputeShader cs;
            public TextureHandle src;
            public TextureHandle temp;
            public int kernel;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();

            TextureHandle cameraColor = resources.activeColorTexture;

            // ======================================================
            // 1. COPY CAMERA COLOR (bo compute nie powinien pisać w place)
            // ======================================================
            var desc = renderGraph.GetTextureDesc(cameraColor);
            desc.name = "CameraCopy";
            desc.enableRandomWrite = true;

            TextureHandle copyTex = renderGraph.CreateTexture(desc);

            using (var copyPass = renderGraph.AddRasterRenderPass<PassData>("Copy Camera", out var cdata))
            {
                cdata.src = cameraColor;

                copyPass.UseTexture(cameraColor, AccessFlags.Read);
                copyPass.SetRenderAttachment(copyTex, 0);

                copyPass.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, d.src, new Vector4(1,1,0,0), 0, false);
                });
            }

            // ======================================================
            // 2. COMPUTE PASS
            // ======================================================
            var computeDesc = renderGraph.GetTextureDesc(cameraColor);
            computeDesc.name = "RedOutput";
            computeDesc.enableRandomWrite = true;

            TextureHandle redTex = renderGraph.CreateTexture(computeDesc);

            using (var computePass = renderGraph.AddComputePass<PassData>("Red Compute", out var data))
            {
                data.cs = computeShader;
                data.src = copyTex;
                data.temp = redTex;
                data.kernel = computeShader.FindKernel("CSMain");

                computePass.UseTexture(copyTex, AccessFlags.Read);
                computePass.UseTexture(redTex, AccessFlags.Write);

                computePass.SetRenderFunc((PassData d, ComputeGraphContext ctx) =>
                {
                    var cmd = ctx.cmd;

                    cmd.SetComputeTextureParam(d.cs, d.kernel, "Input", d.src);
                    cmd.SetComputeTextureParam(d.cs, d.kernel, "Result", d.temp);

                    int w = Screen.width;
                    int h = Screen.height;

                    cmd.DispatchCompute(
                        d.cs,
                        d.kernel,
                        Mathf.CeilToInt(w / 8f),
                        Mathf.CeilToInt(h / 8f),
                        1
                    );
                });
            }

            // ======================================================
            // 3. OUTPUT PASS (TO CAMERA!)
            // ======================================================
            using (var outputPass = renderGraph.AddRasterRenderPass<PassData>("Output", out var od))
            {
                od.src = redTex;

                outputPass.UseTexture(redTex, AccessFlags.Read);

                // 🔥 KLUCZ: render do kamery
                outputPass.SetRenderAttachment(cameraColor, 0);

                outputPass.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, d.src, new Vector4(1,1,0,0), 0, false);
                });
            }
        }
    }

    RedPass pass;
    public ComputeShader computeShader;

    public override void Create()
    {
        pass = new RedPass
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        pass.computeShader = computeShader;
        renderer.EnqueuePass(pass);
    }
}