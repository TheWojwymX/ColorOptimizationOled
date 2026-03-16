using UnityEngine;
using System.IO;

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
    [Range(0f,0.5f)]
    public float foveaRadius = 0.08f;

    [Range(0f,0.5f)]
    public float parafoveaRadius = 0.25f;

    [Header("Optimization")]
    [Range(1,1024)]
    public int candidateSamples = 8;

    public Vector3 oledWeights = new Vector3(0.265f,0.670f,0.065f);

    const int THREADS = 8;

    void Start()
    {
        int kRgbToLab = compute.FindKernel("RGB_TO_LAB");
        int kLabToRgb = compute.FindKernel("LAB_TO_RGB");

        int w = inputTexture.width;
        int h = inputTexture.height;
        int pixelCount = w * h;

        RenderTexture src = CreateRT(w,h);
        Graphics.Blit(inputTexture, src);

        RenderTexture lab = CreateRT(w,h);
        RenderTexture dst = CreateRT(w,h);

        ComputeBuffer energyOriginal = new ComputeBuffer(pixelCount,sizeof(float));
        ComputeBuffer energyOptimized = new ComputeBuffer(pixelCount,sizeof(float));

        compute.SetTexture(kRgbToLab,"Input",src);
        compute.SetTexture(kRgbToLab,"Result",lab);
        compute.SetInts("ImageSize",w,h);

        Dispatch(kRgbToLab,w,h);

        compute.SetTexture(kLabToRgb,"Input",lab);
        compute.SetTexture(kLabToRgb,"Result",dst);

        compute.SetBuffer(kLabToRgb,"EnergyOriginal",energyOriginal);
        compute.SetBuffer(kLabToRgb,"EnergyOptimized",energyOptimized);

        compute.SetFloat("DeltaEFovea",deltaEFovea);
        compute.SetFloat("DeltaEParafovea",deltaEParafovea);
        compute.SetFloat("DeltaEPeriphery",deltaEPeriphery);

        compute.SetFloat("FoveaRadius",foveaRadius);
        compute.SetFloat("ParafoveaRadius",parafoveaRadius);

        compute.SetInt("CandidateSamples",candidateSamples);
        compute.SetVector("OLEDWeights",oledWeights);
        compute.SetInts("ImageSize",w,h);

        Dispatch(kLabToRgb,w,h);

        Texture2D outTex = new Texture2D(w,h,TextureFormat.RGBA32,false);
        RenderTexture.active = dst;
        outTex.ReadPixels(new Rect(0,0,w,h),0,0);
        outTex.Apply();
        RenderTexture.active = null;

        Save(outTex);

        float[] orig = new float[pixelCount];
        float[] opt = new float[pixelCount];

        energyOriginal.GetData(orig);
        energyOptimized.GetData(opt);

        double sumOrig=0;
        double sumOpt=0;

        for(int i=0;i<pixelCount;i++)
        {
            sumOrig+=orig[i];
            sumOpt+=opt[i];
        }

        double savedPercent = 100.0*(1.0 - sumOpt/sumOrig);

        Debug.Log($"OLED Energy Saved: {savedPercent:F4}%");

        energyOriginal.Release();
        energyOptimized.Release();

        src.Release();
        lab.Release();
        dst.Release();
    }

    RenderTexture CreateRT(int w,int h)
    {
        RenderTexture rt = new RenderTexture(w,h,0,RenderTextureFormat.ARGBFloat);
        rt.enableRandomWrite=true;
        rt.Create();
        return rt;
    }

    void Dispatch(int kernel,int w,int h)
    {
        compute.Dispatch(kernel,
            Mathf.CeilToInt(w/(float)THREADS),
            Mathf.CeilToInt(h/(float)THREADS),
            1);
    }

    void Save(Texture2D tex)
    {
        byte[] bytes = tex.EncodeToPNG();
        string path = Path.Combine(Application.dataPath,outputFileName);
        File.WriteAllBytes(path,bytes);

        Debug.Log("Saved to: "+path);
    }
}