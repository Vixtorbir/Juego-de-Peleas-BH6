#if UNITY_INCLUDE_TESTS && UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Framing invariant of the training hall: wherever the fighting camera goes, including the lift
/// and pull-back of a full juggle, the top edge of its frame lands on the stage architecture. The
/// check is pure geometry on the camera pose and vertical field of view, so it holds headless and
/// is independent of the game view aspect.
/// </summary>
public class StageFramingPlayModeTests
{
    private const string ScenePath = "Assets/_Project/Scenes/TrainingRoom.unity";
    private const float IntroWait = 2.5f;
    private const float ContactDistance = 1.25f;
    private const float StepTimeout = 4f;
    private const float BackWallZ = 7.2f;
    private const float EnclosureHeight = 16f;
    private const float EnclosureMargin = 0.5f;
    private const float CornerX = 14.9f;

    private RobotController player1;
    private RobotController player2;
    private Camera stageCamera;

    [UnitySetUp]
    public IEnumerator LoadScene()
    {
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;
        yield return new WaitForSecondsRealtime(IntroWait);

        RobotController[] fighters = Object.FindObjectsByType<RobotController>(FindObjectsSortMode.None);
        System.Array.Sort(fighters, (a, b) => string.CompareOrdinal(a.name, b.name));
        player1 = fighters[0];
        player2 = fighters[1];
        stageCamera = Camera.main;
        Assert.IsNotNull(stageCamera, "Training room has no main camera.");
    }

    [UnityTest]
    public IEnumerator FrameTopStaysInsideEnclosureThroughAJuggle()
    {
        Teleport(player1, -0.5f);
        Teleport(player2, -0.5f + ContactDistance);
        yield return null;

        float highestTop = SampleFrameTop();
        player1.EnterAttack(player1.launcherBase);
        yield return WaitUntilRealtime(() => player2.CurrentState is AirborneHurtState, "Launcher never connected.");

        while (!(player2.CurrentState is KnockdownState))
        {
            highestTop = Mathf.Max(highestTop, SampleFrameTop());
            yield return null;
        }

        Assert.Less(highestTop, EnclosureHeight - EnclosureMargin, "Camera frame rose above the stage enclosure during the juggle.");
    }

    [UnityTest]
    public IEnumerator FrameTopStaysInsideEnclosureAtFullSeparation()
    {
        Teleport(player1, -CornerX);
        Teleport(player2, CornerX);
        yield return new WaitForSecondsRealtime(1f);

        float highestTop = 0f;
        for (int i = 0; i < 30; i++)
        {
            highestTop = Mathf.Max(highestTop, SampleFrameTop());
            yield return null;
        }

        Assert.Less(highestTop, EnclosureHeight - EnclosureMargin, "Camera frame rose above the stage enclosure at full pull-back.");
    }

    /// <summary>
    /// Height at which the top edge of the vertical field of view meets the back wall plane.
    /// </summary>
    private float SampleFrameTop()
    {
        Transform camera = stageCamera.transform;
        Vector3 topDirection = Quaternion.AngleAxis(-stageCamera.fieldOfView * 0.5f, camera.right) * camera.forward;
        if (topDirection.z <= 0.0001f) return float.MaxValue;

        float distance = (BackWallZ - camera.position.z) / topDirection.z;
        return camera.position.y + topDirection.y * distance;
    }

    private static void Teleport(RobotController fighter, float x)
    {
        fighter.characterController.enabled = false;
        fighter.transform.position = new Vector3(x, 0.05f, 0f);
        fighter.characterController.enabled = true;
        fighter.FaceOpponent();
    }

    private static IEnumerator WaitUntilRealtime(System.Func<bool> condition, string failureMessage)
    {
        float deadline = Time.realtimeSinceStartup + StepTimeout;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > deadline) Assert.Fail(failureMessage);
            yield return null;
        }
    }
}
#endif
