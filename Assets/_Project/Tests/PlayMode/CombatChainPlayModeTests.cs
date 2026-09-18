#if UNITY_INCLUDE_TESTS && UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode coverage of the combat feel rules in the training scene: on-connect chain cancels
/// produce true combos, launchers put the defender into a juggle that can be followed up in the
/// air, every clean hit freezes the game clock for the tier's hitstop, a cornered defender stays
/// pinned while the attacker absorbs the pushback, and projectiles land and recycle without
/// growing the scene.
/// </summary>
public class CombatChainPlayModeTests
{
    private const string ScenePath = "Assets/_Project/Scenes/TrainingRoom.unity";
    private const float IntroWait = 2.5f;
    private const float ContactDistance = 1.25f;
    private const float StepTimeout = 3f;
    private const float CornerX = 14.9f;
    private const float WallPinTolerance = 0.08f;
    private const float MinAttackerCornerShove = 0.1f;

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

    [UnityTest]
    public IEnumerator CornerPushbackPinsDefenderAndShovesAttacker()
    {
        Teleport(player2, CornerX);
        Teleport(player1, CornerX - ContactDistance);
        yield return null;

        float defenderStartX = player2.transform.position.x;
        float attackerStartX = player1.transform.position.x;
        float defenderMaxX = defenderStartX;

        player1.ChangeState(new AttackState(player1, player1.rightKickBase));
        yield return WaitForState<HitstunState>(player2);

        while (player2.CurrentState is HitstunState)
        {
            defenderMaxX = Mathf.Max(defenderMaxX, player2.transform.position.x);
            yield return null;
        }

        Assert.LessOrEqual(defenderMaxX - defenderStartX, WallPinTolerance, "Cornered defender was pushed through the wall.");
        Assert.GreaterOrEqual(player2.transform.position.x, defenderStartX - WallPinTolerance, "Cornered defender bounced back off the wall.");
        Assert.Less(player1.transform.position.x, attackerStartX - MinAttackerCornerShove, "Attacker did not absorb the corner pushback.");
        Assert.Greater(player2.transform.position.x, player1.transform.position.x, "Fighters swapped sides during corner pushback.");
    }

    [UnityTest]
    public IEnumerator HadoukenHitsFromRangeAndReturnsToPool()
    {
        Teleport(player1, 0f);
        Teleport(player2, 4f);
        yield return null;

        int projectilesBefore = CountProjectilesInScene();
        float startHealth = player2.Health.CurrentHealth;

        player1.ChangeState(new SpecialAttackState(player1, player1.hadoukenData, player1.Projectiles, player1.hadoukenSpawnPoint));
        yield return WaitUntilRealtime(() => player1.Projectiles.CountInFlight() > 0, "Hadouken was never fired.");
        yield return WaitUntilRealtime(() => player2.Health.CurrentHealth < startHealth, "Hadouken never connected.");

        Assert.AreEqual(0, player1.Projectiles.CountInFlight(), "Projectile should return to the pool on impact.");
        Assert.AreEqual(projectilesBefore, CountProjectilesInScene(), "Firing must reuse pooled projectiles, never instantiate.");
        Assert.IsTrue(player2.CurrentState is HitstunState, "Projectile hit should put the defender in hitstun.");
    }

    private static int CountProjectilesInScene()
    {
        return Object.FindObjectsByType<HadoukenProjectile>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
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
