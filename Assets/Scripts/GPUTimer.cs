using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

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

    IEnumerator Start()
    {
        int kRgbToLab = compute.FindKernel("RGB_TO_LAB");
        int kLabToRgb = compute.FindKernel("LAB_TO_RGB");

        int w = inputTexture.width;
        int h = inputTexture.height;
        int pixelCount = w * h;

        RenderTexture src = CreateRT(w, h);
        RenderTexture lab = CreateRT(w, h);
        RenderTexture dst = CreateRT(w, h);

        Graphics.Blit(inputTexture, src);

        ComputeBuffer energyOriginal = new ComputeBuffer(pixelCount, sizeof(float));
        ComputeBuffer energyOptimized = new ComputeBuffer(pixelCount, sizeof(float));

        // ---------------- BIND ----------------
        compute.SetTexture(kRgbToLab, "Input", src);
        compute.SetTexture(kRgbToLab, "Result", lab);
        compute.SetInts("ImageSize", w, h);

        compute.SetTexture(kLabToRgb, "Input", lab);
        compute.SetTexture(kLabToRgb, "Result", dst);

        compute.SetBuffer(kLabToRgb, "EnergyOriginal", energyOriginal);
        compute.SetBuffer(kLabToRgb, "EnergyOptimized", energyOptimized);

        // 🔥 IMPORTANT: ALL PARAMETERS
        compute.SetInt("CandidateSamples", candidateSamples);

        compute.SetFloat("DeltaEFovea", deltaEFovea);
        compute.SetFloat("DeltaEParafovea", deltaEParafovea);
        compute.SetFloat("DeltaEPeriphery", deltaEPeriphery);

        compute.SetFloat("FoveaRadius", foveaRadius);
        compute.SetFloat("ParafoveaRadius", parafoveaRadius);

        // ---------------- WARMUP ----------------
        Dispatch(kRgbToLab, w, h);
        Dispatch(kLabToRgb, w, h);

        yield return new WaitForSeconds(0.1f);

        // ---------------- BENCH ----------------
        int iterations = 10;
        float totalMs = 0f;

        ComputeBuffer sync = new ComputeBuffer(1, sizeof(float));

        for (int i = 0; i < iterations; i++)
        {
            bool done = false;
            float start = Time.realtimeSinceStartup;

            Dispatch(kRgbToLab, w, h);
            Dispatch(kLabToRgb, w, h);

            AsyncGPUReadback.Request(sync, (AsyncGPUReadbackRequest req) =>
            {
                totalMs += (Time.realtimeSinceStartup - start) * 1000f;
                done = true;
            });

            while (!done)
                yield return null;
        }

        Debug.Log($"AVG GPU TIME: {totalMs / iterations:F3} ms");
        Debug.Log($"CandidateSamples = {candidateSamples}");

        energyOriginal.Release();
        energyOptimized.Release();
        sync.Release();
    }

    void Dispatch(int k, int w, int h)
    {
        compute.Dispatch(k,
            Mathf.CeilToInt(w / (float)THREADS),
            Mathf.CeilToInt(h / (float)THREADS),
            1);
    }

    RenderTexture CreateRT(int w, int h)
    {
        var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBFloat);
        rt.enableRandomWrite = true;
        rt.Create();
        return rt;
    }
}