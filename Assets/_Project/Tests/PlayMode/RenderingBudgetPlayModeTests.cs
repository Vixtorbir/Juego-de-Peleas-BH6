#if UNITY_INCLUDE_TESTS && UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode performance budget for the training room. Drives a few seconds of scripted combat,
/// samples the editor render statistics and the game code's managed allocations, logs the
/// averages so a run doubles as a before/after report, and checks them against fixed budgets.
/// Render statistics only exist while a Game view is rendering, so the batch and shadow budgets
/// are enforced in an editor run and skipped headless; allocations are read from the profiler's
/// player-loop samples through <see cref="PlayerLoopAllocationProbe"/>, which is valid in both.
/// </summary>
public class RenderingBudgetPlayModeTests
{
    private const string ScenePath = "Assets/_Project/Scenes/TrainingRoom.unity";
    private const float IntroWait = 2.5f;
    private const float ContactDistance = 1.25f;
    private const float WarmupSeconds = 0.5f;
    private const int SamplePairs = 3;
    private const float SegmentSeconds = 1.5f;
    private const float SecondsBetweenAttacks = 0.7f;

    private const int MaxBatches = 80;
    private const int MaxSetPassCalls = 48;
    private const int MaxShadowCasters = 52;
    private const long MaxGameGcBytesPerFrame = 32;

    private RobotController player1;
    private RobotController player2;

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

        Teleport(player1, -0.5f);
        Teleport(player2, -0.5f + ContactDistance);
        yield return null;
    }

    [UnityTest]
    public IEnumerator CombatSceneStaysWithinRenderAndAllocationBudget()
    {
        yield return new WaitForSeconds(WarmupSeconds);

        PlayerLoopAllocationProbe.StartRecording();
        FrameSample idle = new FrameSample();
        FrameSample combat = new FrameSample();

        for (int pair = 0; pair < SamplePairs; pair++)
        {
            yield return Sample(idle, false);
            yield return Sample(combat, true);
        }

        PlayerLoopAllocationProbe.StopRecording();

        long averageBatches = combat.Batches / combat.Frames;
        long averageSetPass = combat.SetPassCalls / combat.Frames;
        long averageShadowCasters = combat.ShadowCasters / combat.Frames;
        long idleGc = idle.Allocations.AverageBytesPerFrame;
        long combatGc = combat.Allocations.AverageBytesPerFrame;
        bool rendered = averageBatches > 0;

        Debug.Log(
            "[RenderingBudget] fps=" + (combat.Frames / (SamplePairs * SegmentSeconds)).ToString("F0") +
            " batches=" + averageBatches +
            " drawCalls=" + combat.DrawCalls / combat.Frames +
            " setPass=" + averageSetPass +
            " shadowCasters=" + averageShadowCasters +
            " triangles=" + combat.Triangles / combat.Frames +
            " idleGameGcBytes=" + idleGc +
            " combatGameGcBytes=" + combatGc +
            " attacks=" + combat.Attacks +
            " defenderTransitions=" + combat.Transitions +
            " rendered=" + rendered);

        Assert.Greater(combat.Transitions, 0, "Defender never reacted; the sample did not exercise combat.");
        Assert.LessOrEqual(combatGc, MaxGameGcBytesPerFrame, "Game code allocates managed memory inside the combat loop.");

        if (!rendered) yield break;

        Assert.LessOrEqual(averageBatches, MaxBatches, "Average batch count exceeds the arena budget.");
        Assert.LessOrEqual(averageSetPass, MaxSetPassCalls, "Average SetPass count exceeds the arena budget.");
        Assert.LessOrEqual(averageShadowCasters, MaxShadowCasters, "Too many shadow casters reach the shadow passes.");
    }

    [UnityTest]
    public IEnumerator FramePacingIsLockedToSixtyHertz()
    {
        yield return null;

        Assert.AreEqual(1f / 60f, Time.fixedDeltaTime, 0.0001f, "Physics step must match the 60 Hz frame data.");
        Assert.AreEqual(1, QualitySettings.vSyncCount, "VSync must be enabled for stable frame pacing.");
        Assert.AreEqual(60, Application.targetFrameRate, "Target frame rate must be 60 when VSync is unavailable.");
    }

    private sealed class FrameSample
    {
        public readonly PlayerLoopAllocationProbe Allocations = new PlayerLoopAllocationProbe();
        public long Batches, DrawCalls, SetPassCalls, ShadowCasters, Triangles;
        public int Frames, Attacks, Transitions;
    }

    private IEnumerator Sample(FrameSample sample, bool fighting)
    {
        RobotState lastState = player2.CurrentState;
        float start = Time.time;
        float nextAttackTime = start;
        sample.Allocations.BeginWindow();

        while (Time.time - start < SegmentSeconds)
        {
            if (fighting && Time.time >= nextAttackTime)
            {
                player1.bufferedCommand = sample.Attacks % 2 == 0 ? FighterCommand.LeftPunch : FighterCommand.RightKick;
                nextAttackTime += SecondsBetweenAttacks;
                sample.Attacks++;
            }

            yield return null;
            sample.Frames++;

            sample.Batches += UnityEditor.UnityStats.batches;
            sample.DrawCalls += UnityEditor.UnityStats.drawCalls;
            sample.SetPassCalls += UnityEditor.UnityStats.setPassCalls;
            sample.ShadowCasters += UnityEditor.UnityStats.shadowCasters;
            sample.Triangles += UnityEditor.UnityStats.triangles;

            if (!ReferenceEquals(lastState, player2.CurrentState))
            {
                lastState = player2.CurrentState;
                sample.Transitions++;
            }
        }

        sample.Allocations.EndWindow();
    }

    private static void Teleport(RobotController fighter, float x)
    {
        fighter.characterController.enabled = false;
        fighter.transform.position = new Vector3(x, 0.05f, 0f);
        fighter.characterController.enabled = true;
        fighter.FaceOpponent();
    }
}
#endif
