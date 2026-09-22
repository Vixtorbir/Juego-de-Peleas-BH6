using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Renders the training scene through its combat camera into a PNG, from the Editor menu or from
/// a batch-mode run, so arena and lighting changes can be reviewed without entering Play mode.
/// </summary>
public static class SceneScreenshotTool
{
    private const string OutputDirectory = "Screenshots";
    private const string IdlePoseClipName = "Fight_Idle";
    private const string AttackPoseClipName = "Hadouken";
    private const float IdlePoseTime = 0.5f;
    private const float AttackPoseNormalizedTime = 0.6f;
    private const int Width = 1920;
    private const int Height = 1080;

    [MenuItem("Fighting Game/Capture Scene Screenshot", priority = 100)]
    public static void CaptureTrainingRoom()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != FighterSceneSetup.ScenePath)
            EditorSceneManager.OpenScene(FighterSceneSetup.ScenePath, OpenSceneMode.Single);

        Camera camera = Camera.main;
        if (camera == null)
        {
            Debug.LogError("[SceneScreenshotTool] No main camera in the scene.");
            return;
        }

        FightingCamera fightingCamera = camera.GetComponent<FightingCamera>();
        if (fightingCamera != null && fightingCamera.player1 != null && fightingCamera.player2 != null)
        {
            Vector3 midPoint = (fightingCamera.player1.position + fightingCamera.player2.position) * 0.5f;
            float separation = Vector3.Distance(fightingCamera.player1.position, fightingCamera.player2.position);
            float depth = Mathf.Clamp(fightingCamera.baseOffset.z - separation * fightingCamera.zoomMultiplier, fightingCamera.maxZoomDistance, fightingCamera.minZoomDistance);
            camera.transform.position = new Vector3(midPoint.x, midPoint.y + fightingCamera.baseOffset.y, depth);
            camera.transform.rotation = Quaternion.Euler(fightingCamera.pitchAngle, 0f, 0f);
        }

        AnimationMode.StartAnimationMode();
        PoseFighters();

        var renderTexture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousTarget = camera.targetTexture;
        camera.targetTexture = renderTexture;
        Canvas[] overlays = AttachOverlayCanvases(camera);
        camera.Render();
        RestoreOverlayCanvases(overlays);
        camera.targetTexture = previousTarget;

        RenderTexture.active = renderTexture;
        var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        AnimationMode.StopAnimationMode();

        Directory.CreateDirectory(OutputDirectory);
        string outputPath = Path.Combine(OutputDirectory, "TrainingRoom.png");
        File.WriteAllBytes(outputPath, texture.EncodeToPNG());

        Object.DestroyImmediate(texture);
        Object.DestroyImmediate(renderTexture);
        Debug.Log($"[SceneScreenshotTool] Saved '{outputPath}'.");
    }

    /// <summary>
    /// Screen-space overlay canvases are not part of a camera render, so the HUD is switched to
    /// camera space on the capturing camera for the duration of the render and switched back after,
    /// which lets a capture double as a layout check of the HUD.
    /// </summary>
    private static Canvas[] AttachOverlayCanvases(Camera camera)
    {
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        var attached = new System.Collections.Generic.List<Canvas>(canvases.Length);

        foreach (Canvas canvas in canvases)
        {
            if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            attached.Add(canvas);
        }

        Canvas.ForceUpdateCanvases();
        return attached.ToArray();
    }

    private static void RestoreOverlayCanvases(Canvas[] canvases)
    {
        foreach (Canvas canvas in canvases)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = null;
        }
    }

    /// <summary>
    /// Poses the fighters facing each other: the first holds the guard idle, the second is frozen at
    /// the Hadouken release frame, so a capture doubles as a check of retargeting and attack timing.
    /// </summary>
    private static void PoseFighters()
    {
        RobotController[] fighters = Object.FindObjectsByType<RobotController>(FindObjectsSortMode.None);
        System.Array.Sort(fighters, (a, b) => string.CompareOrdinal(a.name, b.name));

        for (int i = 0; i < fighters.Length; i++)
        {
            RobotController fighter = fighters[i];
            fighter.FaceOpponent();

            Animator animator = fighter.animator;
            if (animator == null || animator.runtimeAnimatorController == null) continue;

            bool attackPose = i % 2 == 1;
            AnimationClip pose = FindClip(animator, attackPose ? AttackPoseClipName : IdlePoseClipName);
            if (pose == null) continue;

            float time = attackPose ? pose.length * AttackPoseNormalizedTime : IdlePoseTime;
            AnimationMode.SampleAnimationClip(animator.gameObject, pose, time);
        }
    }

    private static AnimationClip FindClip(Animator animator, string clipName)
    {
        foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
        {
            if (clip.name == clipName) return clip;
        }

        return null;
    }
}
