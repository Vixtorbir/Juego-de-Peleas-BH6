#if UNITY_INCLUDE_TESTS && UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode coverage of the combat HUD in the training room: life bars snap on damage while the
/// ghost fill holds and then drains, the combo counter appears from the second hit and fades when
/// the defender recovers, the round clock counts down and resolves time over by remaining life,
/// and game code with an active HUD allocates nothing measurable inside the player loop.
/// </summary>
public class CombatHudPlayModeTests
{
    private const string ScenePath = "Assets/_Project/Scenes/TrainingRoom.unity";
    private const float IntroWait = 2.5f;
    private const float ContactDistance = 1.25f;
    private const float StepTimeout = 4f;
    private const float FillTolerance = 0.01f;
    private const float GhostDelay = 0.4f;
    private const int SamplePairs = 3;
    private const float SegmentSeconds = 1.5f;
    private const float SecondsBetweenAttacks = 0.7f;
    private const long MaxHudGcBytesPerFrame = 32;

    private RobotController player1;
    private RobotController player2;
    private MatchManager match;
    private CombatHudController hud;

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
        match = MatchManager.Instance;
        hud = Object.FindFirstObjectByType<CombatHudController>();

        Assert.IsNotNull(hud, "CombatHUD is missing from the training room.");
        yield return WaitUntilRealtime(() => match.CurrentState == MatchManager.MatchState.Fighting, "Match never reached the fighting state.");

        Teleport(player1, -0.5f);
        Teleport(player2, -0.5f + ContactDistance);
        yield return null;
    }

    [UnityTest]
    public IEnumerator HealthBarSnapsOnDamageAndGhostCatchesUpAfterDelay()
    {
        HealthBarView bar = hud.Player2HealthBar;
        Assert.AreSame(player2.Health, bar.BoundHealth, "Player 2 bar is not bound to Player 2's health.");
        Assert.AreEqual(1f, bar.ForegroundFill, FillTolerance);

        player2.Health.TakeDamage(30f);
        yield return null;

        Assert.AreEqual(0.7f, bar.ForegroundFill, FillTolerance, "Foreground fill should snap to the new health at once.");
        Assert.AreEqual(1f, bar.GhostFill, FillTolerance, "Ghost fill must hold its previous value right after the hit.");

        yield return new WaitForSecondsRealtime(GhostDelay * 0.5f);
        Assert.AreEqual(1f, bar.GhostFill, FillTolerance, "Ghost fill drained before the hold delay elapsed.");

        yield return WaitUntilRealtime(() => bar.GhostFill < 0.99f, "Ghost fill never started draining.");
        yield return WaitUntilRealtime(() => Mathf.Abs(bar.GhostFill - bar.ForegroundFill) < FillTolerance, "Ghost fill never caught up with the foreground.");
    }

    [UnityTest]
    public IEnumerator ComboCounterShowsFromSecondHitAndFadesWhenComboEnds()
    {
        ComboCounterView counter = hud.Player1Combo;
        Assert.AreSame(player2.Hurtbox, counter.BoundHurtbox, "Player 1's counter must listen to Player 2's hurtbox.");
        Assert.AreEqual(0f, counter.Alpha, FillTolerance, "Counter should start hidden.");

        player1.bufferedCommand = FighterCommand.LeftPunch;
        yield return WaitForState<AttackState>(player1);
        yield return WaitUntilRealtime(() => player2.Hurtbox.ComboHitCount == 1, "First hit never landed.");
        Assert.AreEqual(0f, counter.Alpha, FillTolerance, "A single hit must not show the counter.");

        player1.bufferedCommand = FighterCommand.RightPunch;
        yield return WaitUntilRealtime(() => player2.Hurtbox.ComboHitCount == 2, "Chain into the second hit never landed.");
        yield return null;

        Assert.IsTrue(counter.IsVisible, "Counter should be visible on the second hit.");
        Assert.AreEqual(1f, counter.Alpha, FillTolerance);

        yield return WaitUntilRealtime(() => !counter.IsVisible, "Counter never received the combo end.");
        yield return WaitUntilRealtime(() => counter.Alpha <= FillTolerance, "Counter never faded out after the combo ended.");
    }

    [UnityTest]
    public IEnumerator RoundClockCountsDownAndResolvesTimeOverByRemainingLife()
    {
        int startSeconds = match.SecondsRemaining;
        Assert.AreEqual(match.SecondsRemaining.ToString("00"), hud.TimerText, "Timer label does not mirror the match clock.");

        yield return new WaitForSecondsRealtime(1.2f);
        Assert.Less(match.SecondsRemaining, startSeconds, "Round clock did not count down while fighting.");
        Assert.AreEqual(match.SecondsRemaining.ToString("00"), hud.TimerText);

        bool timeOverRaised = false;
        match.OnTimeOver += () => timeOverRaised = true;

        player2.Health.TakeDamage(20f);
        match.SetRemainingTime(0.3f);
        yield return WaitUntilRealtime(() => timeOverRaised, "Time over never fired after the clock ran out.");

        Assert.AreEqual("00", hud.TimerText, "Timer label should read zero at time over.");
        Assert.AreEqual(MatchManager.MatchState.RoundEnd, match.CurrentState);
        Assert.AreEqual(1, match.Player1RoundsWon, "Player 1 had more life and should take the round.");
        Assert.AreEqual(0, match.Player2RoundsWon);
        Assert.AreEqual("TIME OVER", hud.Banner.MainText, "Banner should announce time over.");
    }

    /// <summary>
    /// Allocations are read from the profiler's player-loop samples, so the editor harness's own
    /// per-frame garbage never leaks into the figure. Idle and combat windows are both reported.
    /// </summary>
    [UnityTest]
    public IEnumerator ActiveHudAddsNoAllocationsToCombatLoop()
    {
        yield return new WaitForSeconds(0.5f);

        PlayerLoopAllocationProbe.StartRecording();
        AllocationSample idle = new AllocationSample();
        AllocationSample combat = new AllocationSample();
        float healthBeforeCombat = player2.Health.CurrentHealth;

        for (int pair = 0; pair < SamplePairs; pair++)
        {
            yield return SampleAllocations(idle, false);
            yield return SampleAllocations(combat, true);
        }

        PlayerLoopAllocationProbe.StopRecording();

        long idlePerFrame = idle.Probe.AverageBytesPerFrame;
        long combatPerFrame = combat.Probe.AverageBytesPerFrame;
        Debug.Log("[CombatHud] idleGameGcBytes=" + idlePerFrame + " combatGameGcBytes=" + combatPerFrame + " frames=" + combat.Probe.Frames);

        Assert.Less(player2.Health.CurrentHealth, healthBeforeCombat, "Sample did not exercise combat.");
        Assert.LessOrEqual(combatPerFrame, MaxHudGcBytesPerFrame, "Game code allocates managed memory while the HUD reacts to combat.");
    }

    private sealed class AllocationSample
    {
        public readonly PlayerLoopAllocationProbe Probe = new PlayerLoopAllocationProbe();
        public int Attacks;
    }

    private IEnumerator SampleAllocations(AllocationSample sample, bool fighting)
    {
        float start = Time.time;
        float nextAttackTime = start;
        sample.Probe.BeginWindow();

        while (Time.time - start < SegmentSeconds)
        {
            if (fighting && Time.time >= nextAttackTime)
            {
                player1.bufferedCommand = sample.Attacks % 2 == 0 ? FighterCommand.LeftPunch : FighterCommand.RightKick;
                nextAttackTime += SecondsBetweenAttacks;
                sample.Attacks++;
            }

            yield return null;
        }

        sample.Probe.EndWindow();
    }

    private static void Teleport(RobotController fighter, float x)
    {
        fighter.characterController.enabled = false;
        fighter.transform.position = new Vector3(x, 0.05f, 0f);
        fighter.characterController.enabled = true;
        fighter.FaceOpponent();
    }

    private static IEnumerator WaitForState<T>(RobotController fighter) where T : RobotState
    {
        yield return WaitUntilRealtime(() => fighter.CurrentState is T, fighter.name + " never entered " + typeof(T).Name + ".");
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
