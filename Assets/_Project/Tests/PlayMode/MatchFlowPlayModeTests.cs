#if UNITY_INCLUDE_TESTS && UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode coverage of the match flow driven by real device input: the pause stops scene time,
/// the round clock, hitstop and the command buffer and lifts cleanly without leaking a stopped time
/// scale; a pause taken inside the super freeze resumes into the freeze instead of cutting it; and
/// the dedicated super button activates the super art only on a full gauge and a fully pressed
/// trigger. Input goes through virtual devices so the tests exercise the same bindings a player does;
/// device input is routed to play mode regardless of Game view and application focus, which a
/// headless run never has.
/// </summary>
public class MatchFlowPlayModeTests
{
    private const string ScenePath = "Assets/_Project/Scenes/TrainingRoom.unity";
    private const float IntroWait = 2.5f;
    private const float ContactDistance = 1.25f;
    private const float StepTimeout = 6f;
    private const float PauseHold = 1.5f;
    private const float FreezeHold = 0.8f;
    private const float HitstopProbe = 1f;
    private const float MinFreezeLeftAtPause = 0.1f;
    private const float PartialTriggerPress = 0.3f;
    private const float FullTriggerPress = 1f;
    private const float FullGaugeDamage = 1000f;
    private const int RejectionFrames = 20;

    private RobotController player1;
    private RobotController player2;
    private MatchManager match;
    private Keyboard keyboard;
    private Gamepad gamepad;
    private InputSettings.EditorInputBehaviorInPlayMode previousEditorInputBehavior;
    private InputSettings.BackgroundBehavior previousBackgroundBehavior;

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
        match = MatchManager.Instance;

        previousEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
        previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        keyboard = InputSystem.AddDevice<Keyboard>();
        yield return WaitUntilRealtime(() => match.CurrentState == MatchManager.MatchState.Fighting, "Match never reached the fighting state.");

        Teleport(player1, -0.5f);
        Teleport(player2, -0.5f + ContactDistance);
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator RemoveDevices()
    {
        if (keyboard != null) InputSystem.RemoveDevice(keyboard);
        if (gamepad != null) InputSystem.RemoveDevice(gamepad);
        keyboard = null;
        gamepad = null;
        InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInputBehavior;
        InputSystem.settings.backgroundBehavior = previousBackgroundBehavior;
        Time.timeScale = 1f;
        yield return null;
    }

    [UnityTest]
    public IEnumerator PauseFreezesTheFightAndResumesCleanly()
    {
        MatchBannerView banner = Object.FindFirstObjectByType<CombatHudController>().Banner;
        player1.SuperMeter.AwardForDamageDealt(FullGaugeDamage, false);

        // A first cycle outside the window keeps one-time Mono JIT compilation of the pause path out
        // of the steady-state measurement; the gauge is filled beforehand so its full-meter telegraph
        // is not billed to the pause.
        yield return TapKey(Key.Escape);
        yield return TapKey(Key.Enter);
        Assert.IsFalse(match.IsPaused, "Warm-up pause cycle did not resume.");

        player2.FreezeFor(HitstopProbe);

        var probe = new PlayerLoopAllocationProbe();
        PlayerLoopAllocationProbe.StartRecording();
        probe.BeginWindow();

        yield return TapKey(Key.Escape);
        Assert.IsTrue(match.IsPaused, "Escape during the fight must pause the match.");
        Assert.AreEqual(0f, Time.timeScale, "Pausing must stop scene time.");
        Assert.AreEqual("PAUSE", banner.MainText, "The banner must announce the pause.");

        int secondsAtPause = match.SecondsRemaining;
        float hitstopAtPause = player2.FreezeRemaining;

        yield return TapKey(Key.U);
        yield return new WaitForSecondsRealtime(PauseHold);

        Assert.AreEqual(secondsAtPause, match.SecondsRemaining, "The round clock ran while paused.");
        Assert.AreEqual(hitstopAtPause, player2.FreezeRemaining, 0.0001f, "Hitstop drained while paused.");
        Assert.AreEqual(FighterCommand.None, player1.bufferedCommand, "A press during the pause reached the command buffer.");

        yield return TapKey(Key.Enter);

        probe.EndWindow();
        PlayerLoopAllocationProbe.StopRecording();
        Debug.Log("[Pause] frames=" + probe.Frames + " combatGameGcBytes=" + probe.AverageBytesPerFrame + " totalBytes=" + probe.Bytes);

        Assert.IsFalse(match.IsPaused, "Enter during the pause must resume the match.");
        Assert.AreEqual(1f, Time.timeScale, "Resuming must restore normal time.");
        Assert.AreEqual(0f, banner.Alpha, 0.001f, "The pause notice must clear on resume.");
        Assert.Greater(probe.Frames, 0, "The allocation probe recorded no frames.");
        Assert.AreEqual(0, probe.AverageBytesPerFrame, "Pausing and resuming allocates managed memory inside the player loop.");

        yield return TapKey(Key.Escape);
        Assert.IsTrue(match.IsPaused, "Test setup failed to pause before teardown.");

        Object.Destroy(match.gameObject);
        yield return null;
        Assert.AreEqual(1f, Time.timeScale, "Destroying a paused match must restore normal time.");
    }

    [UnityTest]
    public IEnumerator PauseDuringSuperFreezeResumesIntoTheFreeze()
    {
        CombatEffectsManager effects = CombatEffectsManager.Instance;
        Assert.IsNotNull(effects, "CombatEffectsManager is missing from the training room.");

        player1.SuperMeter.AwardForDamageDealt(FullGaugeDamage, false);
        player1.bufferedCommand = FighterCommand.SuperArt;
        yield return WaitUntilRealtime(() => player1.CurrentState is SuperArtState && effects.IsSlowMotionActive, "Super art never opened its freeze.");

        yield return PressKey(Key.Escape);
        float freezeAtPause = effects.SlowMotionRemaining;
        Assert.IsTrue(match.IsPaused, "Escape during the super freeze must pause the match.");
        Assert.Greater(freezeAtPause, MinFreezeLeftAtPause, "Test setup paused too late into the freeze to observe it.");
        yield return ReleaseKeys();

        yield return new WaitForSecondsRealtime(FreezeHold);
        Assert.AreEqual(0f, Time.timeScale, "The slow-motion timer expired through the pause and restarted time.");
        Assert.AreEqual(freezeAtPause, effects.SlowMotionRemaining, 0.0001f, "The super freeze drained while paused.");
        Assert.IsTrue(player1.CurrentState is SuperArtState, "The super art advanced while paused.");

        yield return PressKey(Key.Enter);
        Assert.IsFalse(match.IsPaused, "Enter must resume the match.");
        Assert.AreEqual(0f, Time.timeScale, "Resuming must return to the interrupted freeze, not to full speed.");
        yield return ReleaseKeys();

        yield return WaitUntilRealtime(() => Mathf.Approximately(Time.timeScale, 1f), "Scene time never recovered after the super freeze.");
    }

    [UnityTest]
    public IEnumerator SuperArtButtonRequiresFullGaugeAndFullTriggerPress()
    {
        Assert.AreEqual(0f, player1.SuperMeter.CurrentCharge, "A new round must start on an empty gauge.");

        yield return TapKey(Key.U);
        yield return AssertSuperArtNotActivated("The super button fired on an empty gauge.");

        player1.SuperMeter.AwardForDamageDealt(FullGaugeDamage, false);
        gamepad = InputSystem.AddDevice<Gamepad>();
        yield return null;

        InputSystem.QueueStateEvent(gamepad, new GamepadState { rightTrigger = PartialTriggerPress });
        yield return AssertSuperArtNotActivated("A trigger resting short of its press point fired the super art.");

        InputSystem.QueueStateEvent(gamepad, new GamepadState { rightTrigger = FullTriggerPress });
        yield return WaitUntilRealtime(() => player1.CurrentState is SuperArtState, "A full trigger press on a full gauge never activated the super art.");
        InputSystem.QueueStateEvent(gamepad, new GamepadState());

        Assert.AreEqual(0f, player1.SuperMeter.CurrentCharge, 0.01f, "The macro must spend the whole gauge.");
    }

    private IEnumerator AssertSuperArtNotActivated(string failureMessage)
    {
        for (int i = 0; i < RejectionFrames; i++)
        {
            Assert.IsFalse(player1.CurrentState is SuperArtState, failureMessage);
            Assert.AreNotEqual(FighterCommand.SuperArt, player1.bufferedCommand, failureMessage);
            yield return null;
        }
    }

    private IEnumerator TapKey(Key key)
    {
        yield return PressKey(key);
        yield return ReleaseKeys();
    }

    private IEnumerator PressKey(Key key)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return null;
    }

    private IEnumerator ReleaseKeys()
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
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
