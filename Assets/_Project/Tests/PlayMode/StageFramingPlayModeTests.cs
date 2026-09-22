#if UNITY_INCLUDE_TESTS && UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Framing invariant of the training stage: wherever the fighting camera goes, including the lift
/// and pull-back of a full juggle and the widest corner separation, the bottom edge of its frame
/// lands on the grid floor and never on the void in front of or beside it. The sky above the wall is
/// part of the stage's look and is not a leak. The check is pure geometry on the camera pose and
/// vertical field of view, so it holds headless and is independent of the game view aspect.
/// </summary>
public class StageFramingPlayModeTests
{
    private const string ScenePath = "Assets/_Project/Scenes/TrainingRoom.unity";
    private const float IntroWait = 2.5f;
    private const float ContactDistance = 1.25f;
    private const float StepTimeout = 4f;
    private const float FloorHeight = 0f;
    private const float FloorHalfSize = 30f;
    private const float FloorMargin = 0.5f;
    private const float CornerX = 10.7f;

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

        TrainingModeController trainingMode = Object.FindFirstObjectByType<TrainingModeController>();
        if (trainingMode != null) trainingMode.enabled = false;
        stageCamera = Camera.main;
        Assert.IsNotNull(stageCamera, "Training stage has no main camera.");
    }

    [UnityTest]
    public IEnumerator FrameBottomLandsOnFloorThroughAJuggle()
    {
        Teleport(player1, -0.5f);
        Teleport(player2, -0.5f + ContactDistance);
        yield return null;

        AssertBottomEdgeOnFloor();
        player1.EnterAttack(player1.launcherBase);
        yield return WaitUntilRealtime(() => player2.CurrentState is AirborneHurtState, "Launcher never connected.");

        while (!(player2.CurrentState is KnockdownState))
        {
            AssertBottomEdgeOnFloor();
            yield return null;
        }
    }

    [UnityTest]
    public IEnumerator FrameBottomLandsOnFloorAtFullSeparation()
    {
        Teleport(player1, -CornerX);
        Teleport(player2, CornerX);
        yield return new WaitForSecondsRealtime(1f);

        for (int i = 0; i < 30; i++)
        {
            AssertBottomEdgeOnFloor();
            yield return null;
        }
    }

    /// <summary>
    /// Casts the bottom edge of the vertical field of view onto the floor plane and requires the
    /// hit to fall inside the grid floor.
    /// </summary>
    private void AssertBottomEdgeOnFloor()
    {
        Transform camera = stageCamera.transform;
        Vector3 bottomDirection = Quaternion.AngleAxis(stageCamera.fieldOfView * 0.5f, camera.right) * camera.forward;
        Assert.Less(bottomDirection.y, 0f, "Bottom edge of the frame does not look down at the floor.");

        float distance = (FloorHeight - camera.position.y) / bottomDirection.y;
        Vector3 hit = camera.position + bottomDirection * distance;

        Assert.LessOrEqual(Mathf.Abs(hit.x), FloorHalfSize - FloorMargin, "Bottom edge of the frame falls off the side of the floor.");
        Assert.LessOrEqual(Mathf.Abs(hit.z), FloorHalfSize - FloorMargin, "Bottom edge of the frame falls short of the floor.");
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
