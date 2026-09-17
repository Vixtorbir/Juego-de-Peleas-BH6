#if UNITY_INCLUDE_TESTS && UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode coverage of the combat feel rules in the training scene: on-connect chain cancels
/// produce true combos, launchers put the defender into a juggle that can be followed up in the
/// air, and every clean hit freezes the game clock for the tier's hitstop.
/// </summary>
public class CombatChainPlayModeTests
{
    private const string ScenePath = "Assets/_Project/Scenes/TrainingRoom.unity";
    private const float IntroWait = 2.5f;
    private const float ContactDistance = 1.25f;
    private const float StepTimeout = 3f;

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
    public IEnumerator JabChainsIntoCrossAsTrueCombo()
    {
        float startHealth = player2.Health.CurrentHealth;
        int hitsLanded = 0;
        bool defenderRecoveredBetweenHits = false;
        float lastHealth = startHealth;

        player1.bufferedCommand = FighterCommand.LeftPunch;
        yield return WaitForState<AttackState>(player1);
        player1.bufferedCommand = FighterCommand.RightPunch;

        float deadline = Time.realtimeSinceStartup + StepTimeout;
        while (Time.realtimeSinceStartup < deadline && hitsLanded < 2)
        {
            if (player2.Health.CurrentHealth < lastHealth)
            {
                lastHealth = player2.Health.CurrentHealth;
                hitsLanded++;
            }
            else if (hitsLanded == 1 && player2.CurrentState is IdleState)
            {
                defenderRecoveredBetweenHits = true;
            }

            yield return null;
        }

        Assert.AreEqual(2, hitsLanded, "Jab followed by Cross should land two hits.");
        Assert.IsFalse(defenderRecoveredBetweenHits, "Defender left hitstun before the Cross connected: not a true combo.");
        Assert.Less(player2.Health.CurrentHealth, startHealth - player1.leftPunchBase.damage, "Second hit dealt no damage.");
    }

    [UnityTest]
    public IEnumerator LauncherJugglesAndShoryukenHitsAirborneDefender()
    {
        float startHealth = player2.Health.CurrentHealth;

        player1.ChangeState(new AttackState(player1, player1.launcherBase));
        yield return WaitForState<AirborneHurtState>(player2);

        float afterLauncher = player2.Health.CurrentHealth;
        Assert.Less(afterLauncher, startHealth, "Launcher dealt no damage.");

        yield return WaitUntilRealtime(() => player2.transform.position.y > 0.6f, "Defender never left the ground.");
        Assert.IsFalse(player2.IsGrounded, "Defender should be airborne after the launcher.");

        player1.ChangeState(new ShoryukenState(player1, player1.shoryukenData));
        yield return WaitUntilRealtime(() => player2.Health.CurrentHealth < afterLauncher, "Shoryuken never connected with the airborne defender.");
        Assert.IsTrue(player2.CurrentState is AirborneHurtState, "Air hit should re-launch the defender into the juggle state.");

        yield return WaitUntilRealtime(() => player2.CurrentState is HitstunState, "Defender never landed into knockdown recovery.");
        yield return WaitForState<IdleState>(player2);
    }

    [UnityTest]
    public IEnumerator CleanHitFreezesGameClock()
    {
        player1.ChangeState(new AttackState(player1, player1.rightPunchBase));
        yield return WaitUntilRealtime(() => CombatEffectsManager.Instance.IsHitstopActive, "No hitstop was triggered by a clean hit.");

        Assert.Less(Time.timeScale, 0.05f, "Hitstop should freeze the game clock.");

        float frozenFor = 0f;
        while (CombatEffectsManager.Instance.IsHitstopActive)
        {
            frozenFor += Time.unscaledDeltaTime;
            yield return null;
        }

        Assert.GreaterOrEqual(frozenFor, 0.12f, "Heavy punch hitstop should last well over a light hit's five frames.");
        Assert.AreEqual(1f, Time.timeScale, 0.001f, "Time scale must return to normal after hitstop.");
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
        float deadline = Time.realtimeSinceStartup + StepTimeout;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > deadline) Assert.Fail(failureMessage);
            yield return null;
        }
    }
}
#endif
