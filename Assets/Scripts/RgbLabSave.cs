using UnityEngine;
using System.IO;
using System.Text;

public class RgbLabSave : MonoBehaviour
{
    public ComputeShader compute;
    public Texture2D inputTexture;
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
        int kRgbToLab = compute.FindKernel("RGB_TO_LAB");
        int kLabToRgb = compute.FindKernel("LAB_TO_RGB");

        int w = inputTexture.width;
        int h = inputTexture.height;
        int pixelCount = w * h;

        RenderTexture src = CreateRT(w, h);
        Graphics.Blit(inputTexture, src);

        RenderTexture lab = CreateRT(w, h);
        RenderTexture dst = CreateRT(w, h);

        ComputeBuffer energyOriginal = new ComputeBuffer(pixelCount, sizeof(float));
        ComputeBuffer energyOptimized = new ComputeBuffer(pixelCount, sizeof(float));

        // ---------- RGB → LAB ----------
        compute.SetTexture(kRgbToLab, "Input", src);
        compute.SetTexture(kRgbToLab, "Result", lab);
        compute.SetInts("ImageSize", w, h);

        Dispatch(kRgbToLab, w, h);

        // ---------- LAB → RGB ----------
        compute.SetTexture(kLabToRgb, "Input", lab);
        compute.SetTexture(kLabToRgb, "Result", dst);

        compute.SetBuffer(kLabToRgb, "EnergyOriginal", energyOriginal);
        compute.SetBuffer(kLabToRgb, "EnergyOptimized", energyOptimized);

        compute.SetFloat("DeltaEFovea", deltaEFovea);
        compute.SetFloat("DeltaEParafovea", deltaEParafovea);
        compute.SetFloat("DeltaEPeriphery", deltaEPeriphery);

        compute.SetFloat("FoveaRadius", foveaRadius);
        compute.SetFloat("ParafoveaRadius", parafoveaRadius);

        compute.SetInts("ImageSize", w, h);

        compute.SetFloat("wr", wr);
        compute.SetFloat("wg", wg);
        compute.SetFloat("wb", wb);
        compute.SetFloat("wrgb", wrgb);

        Dispatch(kLabToRgb, w, h);

        // ---------- readback ----------
        Texture2D outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        RenderTexture.active = dst;
        outTex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        outTex.Apply();
        RenderTexture.active = null;

        SaveRGBHistogram(inputTexture, outTex);
        
        Save(outTex);

        float[] orig = new float[pixelCount];
        float[] opt = new float[pixelCount];

        energyOriginal.GetData(orig);
        energyOptimized.GetData(opt);
        SaveEnergyHistogram(orig, opt);

        double sumOrig = 0;
        double sumOpt = 0;

        for (int i = 0; i < pixelCount; i++)
        {
            sumOrig += orig[i];
            sumOpt += opt[i];
        }

        double savedPercent = 100.0 * (1.0 - sumOpt / sumOrig);

        Debug.Log($"OLED Energy Saved: {savedPercent:F4}%");

        energyOriginal.Release();
        energyOptimized.Release();

        src.Release();
        lab.Release();
        dst.Release();
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

    void Save(Texture2D tex)
    {
        byte[] bytes = tex.EncodeToPNG();
        string path = Path.Combine(Application.dataPath, outputFileName);
        File.WriteAllBytes(path, bytes);

        Debug.Log("Saved to: " + path);
    }
    
    void SaveEnergyHistogram(float[] orig, float[] opt, int bins = 100)
    {
        int[] histOrig = new int[bins];
        int[] histOpt = new int[bins];

        for (int i = 0; i < orig.Length; i++)
        {
            float e1 = Mathf.Clamp01(orig[i]);
            float e2 = Mathf.Clamp01(opt[i]);

            int b1 = Mathf.Min((int)(e1 * bins), bins - 1);
            int b2 = Mathf.Min((int)(e2 * bins), bins - 1);

            histOrig[b1]++;
            histOpt[b2]++;
        }

        string path = Path.Combine(Application.dataPath, "energy_histogram.csv");

        var sb = new StringBuilder();

        sb.AppendLine("bin,original,optimized");

        for (int i = 0; i < bins; i++)
        {
            int binId = i + 1; 

            sb.Append(binId);
            sb.Append(",");
            sb.Append(histOrig[i]);
            sb.Append(",");
            sb.Append(histOpt[i]);
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString());

        Debug.Log("Histogram saved to: " + path);
    }
    
    void SaveRGBHistogram(Texture2D before, Texture2D after, int bins = 256)
    {
        int[] rBefore = new int[bins];
        int[] gBefore = new int[bins];
        int[] bBefore = new int[bins];

        int[] rAfter = new int[bins];
        int[] gAfter = new int[bins];
        int[] bAfter = new int[bins];

        Color[] beforePixels = before.GetPixels();
        Color[] afterPixels = after.GetPixels();

        int pixelCount = beforePixels.Length;

        for (int i = 0; i < pixelCount; i++)
        {
            Color c1 = beforePixels[i];
            Color c2 = afterPixels[i];

            int rb = Mathf.Clamp((int)(c1.r * (bins - 1)), 0, bins - 1);
            int gb = Mathf.Clamp((int)(c1.g * (bins - 1)), 0, bins - 1);
            int bb = Mathf.Clamp((int)(c1.b * (bins - 1)), 0, bins - 1);

            int ra = Mathf.Clamp((int)(c2.r * (bins - 1)), 0, bins - 1);
            int ga = Mathf.Clamp((int)(c2.g * (bins - 1)), 0, bins - 1);
            int ba = Mathf.Clamp((int)(c2.b * (bins - 1)), 0, bins - 1);

            rBefore[rb]++;
            gBefore[gb]++;
            bBefore[bb]++;

            rAfter[ra]++;
            gAfter[ga]++;
            bAfter[ba]++;
        }

        string path = Path.Combine(Application.dataPath, "rgb_histogram.csv");

        var sb = new StringBuilder();

        sb.AppendLine("bin,r_before,g_before,b_before,r_after,g_after,b_after");

        for (int i = 0; i < bins; i++)
        {
            sb.Append(i + 1).Append(",");
            sb.Append(rBefore[i]).Append(",");
            sb.Append(gBefore[i]).Append(",");
            sb.Append(bBefore[i]).Append(",");
            sb.Append(rAfter[i]).Append(",");
            sb.Append(gAfter[i]).Append(",");
            sb.Append(bAfter[i]);
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString());

        Debug.Log("RGB histogram saved to: " + path);
    }
}