using UnityEngine;

public class FPSCounter : MonoBehaviour
{
    private int frameCount = 0;
    private float timer = 0f;

    void Update()
    {
        frameCount++;
        timer += Time.unscaledDeltaTime;

        if (timer >= 1f)
        {
            float averageFPS = frameCount / timer;
            Debug.Log($"Średni FPS: {averageFPS:F1}");

            frameCount = 0;
            timer = 0f;
        }
    }
}