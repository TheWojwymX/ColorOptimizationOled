using UnityEngine;
using System.Collections;
using UnityEngine.Rendering;

public class GPUTimer : MonoBehaviour
{
    public ComputeShader compute;
    public Texture2D inputTexture;

    [Header("Params")]
    public float deltaEFovea = 2f;
    public float deltaEParafovea = 5f;
    public float deltaEPeriphery = 10f;

    public float foveaRadius = 0.08f;
    public float parafoveaRadius = 0.25f;

    [Range(1, 64)]
    public int candidateSamples = 32;

    const int THREADS = 8;

    ComputeBuffer energyOriginal;
    ComputeBuffer energyOptimized;

    int kRgbToLab;
    int kLabToRgb;

    IEnumerator Start()
    {
        kRgbToLab = compute.FindKernel("RGB_TO_LAB");
        kLabToRgb = compute.FindKernel("LAB_TO_RGB");

        int w = inputTexture.width;
        int h = inputTexture.height;
        int pixelCount = w * h;

        RenderTexture src = CreateRT(w, h);
        RenderTexture lab = CreateRT(w, h);
        RenderTexture dst = CreateRT(w, h);

        Graphics.Blit(inputTexture, src);

        energyOriginal = new ComputeBuffer(pixelCount, sizeof(float));
        energyOptimized = new ComputeBuffer(pixelCount, sizeof(float));

        int gx = Mathf.CeilToInt(w / (float)THREADS);
        int gy = Mathf.CeilToInt(h / (float)THREADS);

        // =========================
        // WARMUP (IMPORTANT)
        // =========================
        for (int i = 0; i < 5; i++)
        {
            Run(src, lab, dst, gx, gy);
        }

        yield return null;

        // =========================
        // BENCHMARK
        // =========================
        int iterations = 30;
        float totalMs = 0f;

        for (int i = 0; i < iterations; i++)
        {
            float start = Time.realtimeSinceStartup;

            Run(src, lab, dst, gx, gy);

            // REAL GPU sync point
            yield return new WaitForEndOfFrame();

            float end = Time.realtimeSinceStartup;
            totalMs += (end - start) * 1000f;
        }

        Debug.Log($"AVG GPU+PIPELINE TIME: {totalMs / iterations:F3} ms");
        Debug.Log($"CandidateSamples = {candidateSamples}");

        energyOriginal.Release();
        energyOptimized.Release();
    }

    // =========================================================
    // FULL PIPELINE
    // =========================================================
    void Run(RenderTexture src, RenderTexture lab, RenderTexture dst, int gx, int gy)
    {
        // ---------------- RGB → LAB ----------------
        compute.SetTexture(kRgbToLab, "Input", src);
        compute.SetTexture(kRgbToLab, "Result", lab);
        compute.SetInts("ImageSize", src.width, src.height);

        compute.Dispatch(kRgbToLab, gx, gy, 1);

        // ---------------- LAB → RGB ----------------

        // 🔥 ALWAYS BIND BEFORE DISPATCH (FIX CRASH)
        compute.SetBuffer(kLabToRgb, "EnergyOriginal", energyOriginal);
        compute.SetBuffer(kLabToRgb, "EnergyOptimized", energyOptimized);

        compute.SetTexture(kLabToRgb, "Input", lab);
        compute.SetTexture(kLabToRgb, "Result", dst);

        compute.SetInts("ImageSize", src.width, src.height);

        compute.SetFloat("DeltaEFovea", deltaEFovea);
        compute.SetFloat("DeltaEParafovea", deltaEParafovea);
        compute.SetFloat("DeltaEPeriphery", deltaEPeriphery);

        compute.SetFloat("FoveaRadius", foveaRadius);
        compute.SetFloat("ParafoveaRadius", parafoveaRadius);

        compute.SetInt("CandidateSamples", candidateSamples);

        compute.Dispatch(kLabToRgb, gx, gy, 1);
    }

    RenderTexture CreateRT(int w, int h)
    {
        var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBFloat);
        rt.enableRandomWrite = true;
        rt.Create();
        return rt;
    }
}