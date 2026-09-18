#if UNITY_INCLUDE_TESTS && UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode performance budget for the training room. Drives a few seconds of scripted combat,
/// samples the editor render statistics and the managed allocations of every frame, logs the
/// averages so a run doubles as a before/after report, and checks them against fixed budgets.
/// The two halves need opposite environments: render statistics only exist while a Game view is
/// rendering, so the batch and shadow budgets are enforced in an editor run, whereas the editor
/// GUI itself allocates while repainting, so the allocation budget (combat minus idle) is enforced
/// in a headless batch run where the harness overhead is constant between the two phases.
/// </summary>
public class RenderingBudgetPlayModeTests
{
    private const string ScenePath = "Assets/_Project/Scenes/TrainingRoom.unity";
    private const float IntroWait = 2.5f;
    private const float ContactDistance = 1.25f;
    private const float WarmupSeconds = 0.5f;
    private const float IdleSeconds = 2f;
    private const float SampleSeconds = 4f;
    private const float SecondsBetweenAttacks = 0.7f;

    private const int MaxBatches = 80;
    private const int MaxSetPassCalls = 48;
    private const int MaxShadowCasters = 52;
    private const long MaxCombatGcBytesPerFrame = 96;

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

        using ProfilerRecorder gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        using ProfilerRecorder gcCountRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count");

        FrameSample idle = new FrameSample();
        yield return Sample(idle, gcRecorder, gcCountRecorder, IdleSeconds, false);

        FrameSample combat = new FrameSample();
        yield return Sample(combat, gcRecorder, gcCountRecorder, SampleSeconds, true);

        long idleGc = idle.GcBytes / idle.Frames;
        long combatGc = combat.GcBytes / combat.Frames;
        long combatOnlyGc = System.Math.Max(0L, combatGc - idleGc);
        long averageBatches = combat.Batches / combat.Frames;
        long averageSetPass = combat.SetPassCalls / combat.Frames;
        long averageShadowCasters = combat.ShadowCasters / combat.Frames;
        bool rendered = averageBatches > 0;

        Debug.Log(
            "[RenderingBudget] fps=" + (combat.Frames / SampleSeconds).ToString("F0") +
            " batches=" + averageBatches +
            " drawCalls=" + combat.DrawCalls / combat.Frames +
            " setPass=" + averageSetPass +
            " shadowCasters=" + averageShadowCasters +
            " triangles=" + combat.Triangles / combat.Frames +
            " idleGcBytes=" + idleGc + "/" + idle.GcAllocations / idle.Frames +
            " combatGcBytes=" + combatGc + "/" + combat.GcAllocations / combat.Frames +
            " combatOnlyGcBytes=" + combatOnlyGc +
            " gcPeakBytes=" + combat.GcPeak +
            " attacks=" + combat.Attacks +
            " defenderTransitions=" + combat.Transitions +
            " rendered=" + rendered);

        Assert.Greater(combat.Transitions, 0, "Defender never reacted; the sample did not exercise combat.");

        if (!rendered)
        {
            Assert.LessOrEqual(combatOnlyGc, MaxCombatGcBytesPerFrame, "Combat loop allocates more managed memory per frame than the idle scene.");
            yield break;
        }

        Assert.LessOrEqual(averageBatches, MaxBatches, "Average batch count exceeds the arena budget.");
        Assert.LessOrEqual(averageSetPass, MaxSetPassCalls, "Average SetPass count exceeds the arena budget.");
        Assert.LessOrEqual(averageShadowCasters, MaxShadowCasters, "Too many shadow casters reach the shadow passes.");
    }

    private sealed class FrameSample
    {
        public long Batches, DrawCalls, SetPassCalls, ShadowCasters, Triangles;
        public long GcBytes, GcPeak, GcAllocations;
        public int Frames, Attacks, Transitions;
    }

    private IEnumerator Sample(FrameSample sample, ProfilerRecorder gcBytes, ProfilerRecorder gcCount, float seconds, bool fighting)
    {
        RobotState lastState = player2.CurrentState;
        float start = Time.time;
        float nextAttackTime = start;

        while (Time.time - start < seconds)
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

            long allocated = gcBytes.LastValue;
            sample.GcBytes += allocated;
            sample.GcAllocations += gcCount.LastValue;
            sample.GcPeak = System.Math.Max(sample.GcPeak, allocated);

            if (!ReferenceEquals(lastState, player2.CurrentState))
            {
                lastState = player2.CurrentState;
                sample.Transitions++;
            }
        }
    }

    [UnityTest]
    public IEnumerator FramePacingIsLockedToSixtyHertz()
    {
        yield return null;

        Assert.AreEqual(1f / 60f, Time.fixedDeltaTime, 0.0001f, "Physics step must match the 60 Hz frame data.");
        Assert.AreEqual(1, QualitySettings.vSyncCount, "VSync must be enabled for stable frame pacing.");
        Assert.AreEqual(60, Application.targetFrameRate, "Target frame rate must be 60 when VSync is unavailable.");
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
