using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;

public class OledOptimizationFeature : ScriptableRendererFeature
{
    class OledPass : ScriptableRenderPass
    {
        public ComputeShader compute;

        int kRgbToLab;
        int kLabToRgb;

        class PassData
        {
            public ComputeShader cs;

            public TextureHandle cameraCopy;
            public TextureHandle labTex;
            public TextureHandle resultTex;

            public int kRgbToLab;
            public int kLabToRgb;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();

            TextureHandle cameraColor = resources.activeColorTexture;

            // =====================================================
            // COPY TEXTURE (ARGB FLOAT)
            // =====================================================

            var floatDesc = renderGraph.GetTextureDesc(cameraColor);

            floatDesc.enableRandomWrite = true;
            floatDesc.colorFormat = GraphicsFormat.R32G32B32A32_SFloat;

            floatDesc.name = "CameraCopyFloat";

            TextureHandle cameraCopy = renderGraph.CreateTexture(floatDesc);

            using (var pass = renderGraph.AddRasterRenderPass<PassData>("CopyCamera", out var data))
            {
                data.cameraCopy = cameraCopy;

                pass.UseTexture(cameraColor, AccessFlags.Read);
                pass.SetRenderAttachment(cameraCopy, 0);

                pass.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(
                        ctx.cmd,
                        cameraColor,
                        new Vector4(1,1,0,0),
                        0,
                        false
                    );
                });
            }

            // =====================================================
            // LAB BUFFER
            // =====================================================

            floatDesc.name = "LabTexture";

            TextureHandle labTex = renderGraph.CreateTexture(floatDesc);

            // =====================================================
            // RESULT BUFFER
            // =====================================================

            floatDesc.name = "ResultTexture";

            TextureHandle resultTex = renderGraph.CreateTexture(floatDesc);

            // =====================================================
            // COMPUTE PASS
            // =====================================================

            using (var pass = renderGraph.AddComputePass<PassData>("OLED Compute", out var data))
            {
                data.cs = compute;

                data.cameraCopy = cameraCopy;
                data.labTex = labTex;
                data.resultTex = resultTex;

                data.kRgbToLab = kRgbToLab;
                data.kLabToRgb = kLabToRgb;

                pass.UseTexture(cameraCopy, AccessFlags.Read);
                pass.UseTexture(labTex, AccessFlags.ReadWrite);
                pass.UseTexture(resultTex, AccessFlags.ReadWrite);

                pass.SetRenderFunc((PassData d, ComputeGraphContext ctx) =>
                {
                    var cmd = ctx.cmd;

                    int w = Screen.width;
                    int h = Screen.height;
                    float deltaEFovea = 2.0f;
                    float deltaEParafovea = 4.0f;
                    float deltaEPeriphery = 6.0f;
                    float foveaRadius = 0.04f;
                    float parafoveaRadius = 0.12f;
                    int candidateSamples = 8;
                    
                    cmd.SetComputeFloatParam(d.cs, "DeltaEFovea", deltaEFovea);
                    cmd.SetComputeFloatParam(d.cs, "DeltaEParafovea", deltaEParafovea);
                    cmd.SetComputeFloatParam(d.cs, "DeltaEPeriphery", deltaEPeriphery);

                    cmd.SetComputeFloatParam(d.cs, "FoveaRadius", foveaRadius);
                    cmd.SetComputeFloatParam(d.cs, "ParafoveaRadius", parafoveaRadius);

                    cmd.SetComputeIntParam(d.cs, "CandidateSamples", candidateSamples);

                    cmd.SetComputeIntParams(d.cs, "ImageSize", w, h);

                    cmd.SetComputeFloatParam(d.cs, "wr", 0.55f);
                    cmd.SetComputeFloatParam(d.cs, "wg", 0.35f);
                    cmd.SetComputeFloatParam(d.cs, "wb", 1.0f);
                    cmd.SetComputeFloatParam(d.cs, "wrgb", 0.05f);

                    // =========================
                    // RGB -> LAB
                    // =========================

                    cmd.SetComputeTextureParam(
                        d.cs,
                        d.kRgbToLab,
                        "Input",
                        d.cameraCopy
                    );

                    cmd.SetComputeTextureParam(
                        d.cs,
                        d.kRgbToLab,
                        "Result",
                        d.labTex
                    );

                    cmd.SetComputeIntParams(
                        d.cs,
                        "ImageSize",
                        w,
                        h
                    );

                    cmd.DispatchCompute(
                        d.cs,
                        d.kRgbToLab,
                        Mathf.CeilToInt(w / 8f),
                        Mathf.CeilToInt(h / 8f),
                        1
                    );

                    // =========================
                    // LAB -> RGB
                    // =========================

                    cmd.SetComputeTextureParam(
                        d.cs,
                        d.kLabToRgb,
                        "Input",
                        d.labTex
                    );

                    cmd.SetComputeTextureParam(
                        d.cs,
                        d.kLabToRgb,
                        "Result",
                        d.resultTex
                    );

                    cmd.DispatchCompute(
                        d.cs,
                        d.kLabToRgb,
                        Mathf.CeilToInt(w / 8f),
                        Mathf.CeilToInt(h / 8f),
                        1
                    );
                });
            }

            // =====================================================
            // FINAL OUTPUT
            // =====================================================

            using (var pass = renderGraph.AddRasterRenderPass<PassData>("FinalBlit", out var data))
            {
                data.resultTex = resultTex;

                pass.UseTexture(resultTex, AccessFlags.Read);

                pass.SetRenderAttachment(cameraColor, 0);

                pass.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(
                        ctx.cmd,
                        d.resultTex,
                        new Vector4(1,1,0,0),
                        0,
                        false
                    );
                });
            }
        }

        public void Init()
        {
            kRgbToLab = compute.FindKernel("RGB_TO_LAB");
            kLabToRgb = compute.FindKernel("LAB_TO_RGB");
        }
    }

    OledPass pass;

    public ComputeShader compute;

    public override void Create()
    {
        pass = new OledPass();

        pass.compute = compute;

        pass.renderPassEvent =
            RenderPassEvent.AfterRenderingPostProcessing;

        pass.Init();
    }

    public override void AddRenderPasses(
        ScriptableRenderer renderer,
        ref RenderingData renderingData
    )
    {
        renderer.EnqueuePass(pass);
    }
}