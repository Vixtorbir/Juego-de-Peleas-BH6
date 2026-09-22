using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class ScreenshotTool
{
    private const string OutputDirectory = "Screenshots";
    private const int Width = 1920;
    private const int Height = 1080;

    [MenuItem("Tools/Capture Screenshot %#k")]
    public static void Capture()
    {
        Directory.CreateDirectory(OutputDirectory);
        string path = Path.Combine(OutputDirectory, $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");

        // In Play mode the game view already has the HUD composited, so just grab it.
        if (EditorApplication.isPlaying)
        {
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"Screenshot saved to {path}");
            return;
        }

        Camera camera = Camera.main;
        if (camera == null)
        {
            Debug.LogWarning("No main camera in the open scene.");
            return;
        }

        var renderTexture = new RenderTexture(Width, Height, 24);
        var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);

        camera.targetTexture = renderTexture;
        camera.Render();
        camera.targetTexture = null;

        RenderTexture.active = renderTexture;
        texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        texture.Apply();
        RenderTexture.active = null;

        File.WriteAllBytes(path, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        UnityEngine.Object.DestroyImmediate(renderTexture);

        Debug.Log($"Screenshot saved to {path}");
    }
}
