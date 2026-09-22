#if UNITY_INCLUDE_TESTS && UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class SuperArtPlayModeTests
{
    private const string ScenePath = "Assets/_Project/Scenes/TrainingRoom.unity";
    private const float IntroWait = 2.5f;
    private const float ContactDistance = 1.25f;
    private const float WhiffMargin = 3f;
    private const float SuperTimeout = 6f;
    private const float RejectionProbe = 0.75f;
    private const int ExpectedBeats = 5;
    private const float MinSuperDamageRatio = 0.3f;
    private const float MaxSuperDamageRatio = 0.45f;
    private const float ShortOfFullDamage = 35f;
    private const float FullGaugeDamage = 1000f;

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

        TrainingModeController trainingMode = Object.FindFirstObjectByType<TrainingModeController>();
        if (trainingMode != null) trainingMode.enabled = false;

        Teleport(player1, -0.5f);
        Teleport(player2, -0.5f + ContactDistance);
        yield return null;
    }

    [UnityTest]
    public IEnumerator SuperArtSpendsFullGaugeAndLocksTheDefender()
    {
        FillGauge(player1);
        Assert.IsTrue(player1.SuperMeter.IsFull, "Test setup failed to fill the gauge.");

        float startHealth = player2.Health.CurrentHealth;
        player1.bufferedCommand = FighterCommand.SuperArt;

        yield return WaitForState<SuperArtState>(player1);
        Assert.AreEqual(0f, player1.SuperMeter.CurrentCharge, 0.01f, "Activating the super art must drain the whole gauge.");

        int maxComboHits = 0;
        bool defenderGuarded = false;

        yield return WaitUntilRealtime(() =>
        {
            maxComboHits = Mathf.Max(maxComboHits, player2.Hurtbox.ComboHitCount);
            defenderGuarded |= player2.CurrentState is BlockstunState;
            return player2.CurrentState is KnockdownState;
        }, "Super art never put the defender into a hard knockdown.");

        float damageDealt = startHealth - player2.Health.CurrentHealth;
        float damageRatio = damageDealt / player2.Health.MaxHealth;

        Assert.IsFalse(defenderGuarded, "The cinematic string must be unblockable once the lunge connects.");
        Assert.AreEqual(ExpectedBeats, maxComboHits, "Lunge plus four cinematic beats should register five hits.");
        Assert.GreaterOrEqual(damageRatio, MinSuperDamageRatio, "Super art dealt less than a third of the life bar.");
        Assert.LessOrEqual(damageRatio, MaxSuperDamageRatio, "Super art dealt more than its budgeted share of the life bar.");

        yield return WaitForState<IdleState>(player1);
    }

    [UnityTest]
    public IEnumerator SuperArtIsRejectedWithoutFullGauge()
    {
        player1.SuperMeter.AwardForDamageDealt(ShortOfFullDamage, false);
        float chargeBefore = player1.SuperMeter.CurrentCharge;

        Assert.IsFalse(player1.SuperMeter.IsFull, "Test setup must leave the gauge short of full.");
        Assert.Greater(chargeBefore, 0f, "Test setup must leave charge on the gauge to detect a partial drain.");

        float startHealth = player2.Health.CurrentHealth;
        player1.bufferedCommand = FighterCommand.SuperArt;

        float deadline = Time.realtimeSinceStartup + RejectionProbe;
        while (Time.realtimeSinceStartup < deadline)
        {
            Assert.IsFalse(player1.CurrentState is SuperArtState, "Super art activated on an incomplete gauge.");
            yield return null;
        }

        Assert.AreEqual(chargeBefore, player1.SuperMeter.CurrentCharge, 0.01f, "A refused super art must not drain the gauge.");
        Assert.AreEqual(startHealth, player2.Health.CurrentHealth, 0.01f, "A refused super art must not deal damage.");
    }

    [UnityTest]
    public IEnumerator SuperArtWhiffLeavesPunishableRecovery()
    {
        Teleport(player1, -2f);
        Teleport(player2, -2f + SuperArtState.LungeReach + WhiffMargin);
        yield return null;

        FillGauge(player1);
        float startHealth = player2.Health.CurrentHealth;
        player1.bufferedCommand = FighterCommand.SuperArt;

        yield return WaitForState<SuperArtState>(player1);
        SuperArtState super = (SuperArtState)player1.CurrentState;

        float activationTime = Time.realtimeSinceStartup;
        bool reachedCinematic = false;

        yield return WaitUntilRealtime(() =>
        {
            reachedCinematic |= super.IsCinematic;
            return player1.CurrentState is IdleState;
        }, "Whiffed super art never returned control to the fighter.");

        float lockout = Time.realtimeSinceStartup - activationTime;
        AttackData lunge = player1.superArtLunge;

        Assert.IsFalse(reachedCinematic, "A whiffed lunge must never open the cinematic string.");
        Assert.AreEqual(0, super.BeatsResolved, "A whiffed lunge must not resolve any beat.");
        Assert.AreEqual(startHealth, player2.Health.CurrentHealth, 0.01f, "A whiffed super art dealt damage.");
        Assert.Greater(lockout, lunge.ActiveEndTime + lunge.recoveryTime, "Whiff recovery is shorter than the move's own frame data: the super would be safe.");
    }

    [UnityTest]
    public IEnumerator SuperArtRunsWithoutManagedAllocations()
    {
        // The first super art of the session pays one-time editor costs (Mono JIT, lazy material
        // validation on the first hitspark), so run one outside the window and measure the second.
        yield return PerformSuperArt();
        yield return WaitForState<IdleState>(player2);
        player2.Health.ResetHealth();
        Teleport(player1, -0.5f);
        Teleport(player2, -0.5f + ContactDistance);
        yield return null;

        var probe = new PlayerLoopAllocationProbe();
        PlayerLoopAllocationProbe.StartRecording();
        probe.BeginWindow();

        yield return PerformSuperArt();

        probe.EndWindow();
        PlayerLoopAllocationProbe.StopRecording();

        Debug.Log("[SuperArt] frames=" + probe.Frames + " combatGameGcBytes=" + probe.AverageBytesPerFrame + " totalBytes=" + probe.Bytes);

        Assert.Greater(probe.Frames, 0, "The allocation probe recorded no frames.");
        Assert.AreEqual(0, probe.Bytes, "Super art allocates managed memory inside the combat loop.");
    }

    private IEnumerator PerformSuperArt()
    {
        FillGauge(player1);
        player1.bufferedCommand = FighterCommand.SuperArt;
        yield return WaitForState<SuperArtState>(player1);
        yield return WaitUntilRealtime(() => player2.CurrentState is KnockdownState, "Super art never completed.");
        yield return WaitForState<IdleState>(player1);
    }

    private static void FillGauge(RobotController fighter)
    {
        fighter.SuperMeter.AwardForDamageDealt(FullGaugeDamage, false);
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
        yield return WaitUntilRealtime(() => fighter.CurrentState is T, $"{fighter.name} never entered {typeof(T).Name}.");
    }

    private static IEnumerator WaitUntilRealtime(System.Func<bool> condition, string failureMessage)
    {
        float deadline = Time.realtimeSinceStartup + SuperTimeout;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > deadline) Assert.Fail(failureMessage);
            yield return null;
        }
    }
}
#endif
