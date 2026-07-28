using System.IO;
using System.Text;
using UnityEngine;

public class ImageTest : MonoBehaviour
{
    public ComputeShader compute;
    public Texture2D[] testImages;
    public string outputFileName = "RoundTrip.png";

    [Header("Foveated ΔE")]
    public float deltaEFovea = 2f;
    public float deltaEParafovea = 5f;
    public float deltaEPeriphery = 10f;

    [Header("Foveated Radii")]
    [Range(0f, 0.5f)]
    public float foveaRadius = 0.08f;

    [Range(0f, 0.5f)]
    public float parafoveaRadius = 0.25f;
    
    [Header("OLED Model")]
    public float wr = 0.25f;
    public float wg = 0.65f;
    public float wb = 0.10f;
    public float wrgb = 0.05f; // NIEUZYWANY / ZREZYGNOWANO Z POPRZEDNIEGO MODELU

    const int THREADS = 8;

    void Start()
    {
        foreach (Texture2D tex in testImages)
        {
            Debug.Log("======================================");
            Debug.Log("IMAGE: " + tex.name);
            Debug.Log("======================================");

            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"Obraz: {tex.name}");
            sb.AppendLine("DeltaE;SavedPercent");
            
            for (int delta = 1; delta <= 15; delta++)
            {
                float saved = RunTest(tex, delta);
                sb.AppendLine($"{delta};{saved:F2}");

                Debug.Log(
                    $"DeltaE = {delta,2} | Energy Saved = {saved:F2}%"
                );
            }
            
            string path = Path.Combine(
                Application.dataPath,
                $"{tex.name}_DeltaE_Test.txt"
            );

            File.WriteAllText(path, sb.ToString());

            Debug.Log($"Zapisano wyniki do: {path}");

            Debug.Log("");
        }
    }

    RenderTexture CreateRT(int w, int h)
    {
        RenderTexture rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBFloat);
        rt.enableRandomWrite = true;
        rt.Create();
        return rt;
    }

    void Dispatch(int kernel, int w, int h)
    {
        compute.Dispatch(kernel,
            Mathf.CeilToInt(w / (float)THREADS),
            Mathf.CeilToInt(h / (float)THREADS),
            1);
    }

    float RunTest(Texture2D texture, float delta)
    {
        int kRgbToLab = compute.FindKernel("RGB_TO_LAB");
        int kLabToRgb = compute.FindKernel("LAB_TO_RGB");

        int w = texture.width;
        int h = texture.height;
        int pixelCount = w * h;

        RenderTexture src = CreateRT(w, h);
        Graphics.Blit(texture, src);

        RenderTexture lab = CreateRT(w, h);
        RenderTexture dst = CreateRT(w, h);

        ComputeBuffer energyOriginal =
            new ComputeBuffer(pixelCount, sizeof(float));

        ComputeBuffer energyOptimized =
            new ComputeBuffer(pixelCount, sizeof(float));

        // RGB -> LAB

        compute.SetTexture(kRgbToLab, "Input", src);
        compute.SetTexture(kRgbToLab, "Result", lab);
        compute.SetInts("ImageSize", w, h);

        Dispatch(kRgbToLab, w, h);

        // LAB -> RGB

        compute.SetTexture(kLabToRgb, "Input", lab);
        compute.SetTexture(kLabToRgb, "Result", dst);

        compute.SetBuffer(kLabToRgb, "EnergyOriginal", energyOriginal);
        compute.SetBuffer(kLabToRgb, "EnergyOptimized", energyOptimized);

        // wszędzie jednakowe DeltaE

        compute.SetFloat("DeltaEFovea", delta);
        compute.SetFloat("DeltaEParafovea", delta);
        compute.SetFloat("DeltaEPeriphery", delta);

        compute.SetFloat("FoveaRadius", foveaRadius);
        compute.SetFloat("ParafoveaRadius", parafoveaRadius);

        compute.SetInts("ImageSize", w, h);

        compute.SetFloat("wr", wr);
        compute.SetFloat("wg", wg);
        compute.SetFloat("wb", wb);
        compute.SetFloat("wrgb", wrgb);

        Dispatch(kLabToRgb, w, h);

        float[] orig = new float[pixelCount];
        float[] opt = new float[pixelCount];

        energyOriginal.GetData(orig);
        energyOptimized.GetData(opt);

        double sumOrig = 0;
        double sumOpt = 0;

        for (int i = 0; i < pixelCount; i++)
        {
            sumOrig += orig[i];
            sumOpt += opt[i];
        }

        float saved = (float)(100.0 * (1.0 - sumOpt / sumOrig));

        energyOriginal.Release();
        energyOptimized.Release();

        src.Release();
        lab.Release();
        dst.Release();

        return saved;
    }
}
