#if UNITY_INCLUDE_TESTS && UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode coverage of the combat feel rules in the training scene: chain and gatling cancels
/// produce true combos, launchers put the defender into a juggle whose height decays with every
/// air hit, every clean hit freezes both fighters on their own timers with the defender held
/// longer, a wall bounce is granted once per combo, a knockdown cannot be hit off the ground,
/// a press during recovery fires on the first actionable frame, a cornered defender stays pinned
/// while the attacker absorbs the pushback, and projectiles land and recycle without growing the scene.
/// </summary>
public class CombatChainPlayModeTests
{
    private const string ScenePath = "Assets/_Project/Scenes/TrainingRoom.unity";
    private const float IntroWait = 2.5f;
    private const float ContactDistance = 1.25f;
    private const float StepTimeout = 3f;
    private const float RouteTimeout = 6f;
    private const float CornerX = 14.9f;
    private const float WallPinTolerance = 0.08f;
    private const float WallBounceTolerance = 0.2f;
    private const float MinAttackerCornerShove = 0.1f;
    private const float RelaunchHeight = 1.5f;
    private const float MinJuggleDecay = 0.5f;
    private const float WhiffDistance = 6f;
    private const float RecoveryProbeDelay = 0.25f;

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
    public IEnumerator GatlingRouteLinksWithoutGaps()
    {
        FighterCommand[] route = { FighterCommand.LeftPunch, FighterCommand.RightPunch, FighterCommand.LeftKick, FighterCommand.Shoryuken };
        float lastHealth = player2.Health.CurrentHealth;
        int hitsLanded = 0;
        int nextStep = 1;
        bool defenderRecoveredBetweenHits = false;

        player1.bufferedCommand = route[0];

        float deadline = Time.realtimeSinceStartup + RouteTimeout;
        while (Time.realtimeSinceStartup < deadline && hitsLanded < route.Length)
        {
            if (player2.Health.CurrentHealth < lastHealth)
            {
                lastHealth = player2.Health.CurrentHealth;
                hitsLanded++;
                if (nextStep < route.Length) player1.bufferedCommand = route[nextStep++];
            }
            else if (hitsLanded > 0 && player2.CurrentState.IsNeutral)
            {
                defenderRecoveredBetweenHits = true;
            }

            yield return null;
        }

        Assert.AreEqual(route.Length, hitsLanded, "Jab > Cross > Kick > Shoryuken should land four hits.");
        Assert.IsFalse(defenderRecoveredBetweenHits, "Defender regained control mid-route: a link in the gatling dropped.");
        Assert.AreEqual(route.Length, player2.Hurtbox.ComboHitCount, "Combo counter should track every hit of the route.");
    }

    [UnityTest]
    public IEnumerator LauncherJugglesAndShoryukenHitsAirborneDefender()
    {
        float startHealth = player2.Health.CurrentHealth;

        player1.EnterAttack(player1.launcherBase);
        yield return WaitForState<AirborneHurtState>(player2);

        float afterLauncher = player2.Health.CurrentHealth;
        Assert.Less(afterLauncher, startHealth, "Launcher dealt no damage.");

        yield return WaitUntilRealtime(() => player2.transform.position.y > 0.6f, "Defender never left the ground.");
        Assert.IsFalse(player2.IsGrounded, "Defender should be airborne after the launcher.");

        player1.EnterShoryuken(player1.shoryukenData);
        yield return WaitUntilRealtime(() => player2.Health.CurrentHealth < afterLauncher, "Shoryuken never connected with the airborne defender.");
        Assert.IsTrue(player2.CurrentState is AirborneHurtState, "Air hit should re-launch the defender into the juggle state.");

        yield return WaitUntilRealtime(() => player2.CurrentState is KnockdownState, "Defender never landed into a knockdown.");
        yield return WaitForState<IdleState>(player2);
    }

    [UnityTest]
    public IEnumerator JuggleGravityScalesDeterministically()
    {
        player1.EnterAttack(player1.launcherBase);
        yield return WaitForState<AirborneHurtState>(player2);

        float launchApex = 0f;
        yield return WaitUntilRealtime(() => TrackApex(ref launchApex) && player2.transform.position.y < launchApex - 0.05f, "Defender never came down from the launch.");
        Assert.Greater(launchApex, RelaunchHeight, "Launcher apex is too low to measure a relaunch against.");

        yield return WaitUntilRealtime(() => player2.transform.position.y < RelaunchHeight, "Defender never fell back into relaunch height.");
        float relaunchOrigin = player2.transform.position.y;

        bool connected = player2.Hurtbox.ReceiveHit(player1.shoryukenData, player1);
        Assert.IsTrue(connected, "Air hit on the falling defender was rejected.");
        Assert.AreEqual(1, player2.Hurtbox.Combo.JuggleHits, "Relaunch should count as the first air hit of the juggle.");

        float relaunchApex = relaunchOrigin;
        yield return WaitUntilRealtime(() => TrackApex(ref relaunchApex) && player2.transform.position.y < relaunchApex - 0.05f, "Defender never came down from the relaunch.");

        float launchRise = launchApex;
        float relaunchRise = relaunchApex - relaunchOrigin;
        Assert.Less(relaunchRise, launchRise - MinJuggleDecay, "Juggle decay did not shorten the relaunch: infinite juggles are possible.");
    }

    [UnityTest]
    public IEnumerator WallBounceTriggersOnceThenPins()
    {
        Teleport(player2, CornerX - 3f);
        Teleport(player1, CornerX - 3f - ContactDistance);
        yield return null;

        player1.EnterAttack(player1.launcherBase);
        yield return WaitForState<AirborneHurtState>(player2);
        yield return WaitUntilRealtime(() => player2.transform.position.y > 0.6f, "Defender never left the ground.");

        Assert.IsTrue(player2.Hurtbox.ReceiveHit(player1.rightKickBase, player1), "Air roundhouse was rejected.");
        AirborneHurtState juggle = player2.CurrentState as AirborneHurtState;
        Assert.IsNotNull(juggle, "Wall-bounce hit should keep the defender in the juggle state.");
        Assert.AreEqual(BounceMode.WallBounce, juggle.PendingBounce, "First air roundhouse should grant a wall bounce.");
        Assert.AreEqual(1, player2.Hurtbox.Combo.WallBounces, "Wall bounce should be consumed on the granting hit.");

        float furthestX = player2.transform.position.x;
        yield return WaitUntilRealtime(() => TrackApex(ref furthestX, player2.transform.position.x) && juggle.PendingBounce == BounceMode.None, "Defender never reached the wall.");
        Assert.LessOrEqual(furthestX, CornerX + WallBounceTolerance, "Defender was pushed through the wall.");

        yield return WaitUntilRealtime(() => player2.transform.position.x < furthestX - 0.2f, "Defender did not rebound off the wall.");
        Assert.IsTrue(player2.CurrentState is AirborneHurtState, "Rebound should keep the defender airborne.");

        Assert.IsTrue(player2.Hurtbox.ReceiveHit(player1.rightKickBase, player1), "Second air roundhouse was rejected.");
        Assert.AreEqual(BounceMode.None, juggle.PendingBounce, "Second wall bounce in the same combo must be locked out.");
        Assert.AreEqual(1, player2.Hurtbox.Combo.WallBounces, "Wall bounce count must stay at one per combo.");
    }

    [UnityTest]
    public IEnumerator KnockdownIgnoresOtgHitAndWakesInvulnerable()
    {
        float startHealth = player2.Health.CurrentHealth;

        player1.EnterCrouchAttack(player1.crouchSweepBase);
        yield return WaitForState<KnockdownState>(player2);

        float afterSweep = player2.Health.CurrentHealth;
        Assert.Less(afterSweep, startHealth, "Sweep dealt no damage.");

        bool hit = player2.Hurtbox.ReceiveHit(player1.leftPunchBase, player1);
        Assert.IsFalse(hit, "A knocked-down defender must not be hit off the ground.");
        Assert.AreEqual(afterSweep, player2.Health.CurrentHealth, 0.001f, "OTG hit dealt damage.");
        Assert.IsTrue(player2.CurrentState is KnockdownState, "OTG hit changed the defender's state.");

        yield return WaitForState<IdleState>(player2);
        Assert.IsTrue(player2.IsInvincible, "Wake-up should start with strike invulnerability.");
        yield return WaitUntilRealtime(() => !player2.IsInvincible, "Wake-up invulnerability never expired.");
    }

    [UnityTest]
    public IEnumerator CleanHitFreezesBothFightersAsymmetrically()
    {
        player1.EnterAttack(player1.rightPunchBase);
        yield return WaitUntilRealtime(() => player2.IsFrozen, "No hitstop was applied to the defender.");

        Assert.IsTrue(player1.IsFrozen, "Attacker should freeze on contact.");
        Assert.Greater(player2.FreezeRemaining, player1.FreezeRemaining, "Defender should be held longer than the attacker.");
        Assert.AreEqual(1f, Time.timeScale, 0.001f, "Per-fighter hitstop must leave the global time scale alone.");

        float defenderFrozen = 0f;
        while (player2.IsFrozen)
        {
            defenderFrozen += Time.unscaledDeltaTime;
            yield return null;
        }

        Assert.IsFalse(player1.IsFrozen, "Attacker should thaw before the defender.");
        Assert.GreaterOrEqual(defenderFrozen, 0.12f, "Heavy punch hitstop should last well over a light hit's five frames.");
    }

    [UnityTest]
    public IEnumerator BufferedPressDuringRecoveryFiresOnFirstActionableFrame()
    {
        Teleport(player2, -0.5f + WhiffDistance);
        yield return null;

        player1.EnterAttack(player1.leftPunchBase);
        yield return new WaitForSeconds(RecoveryProbeDelay);
        Assert.IsTrue(player1.CurrentState is AttackState, "Probe fired outside the jab's recovery.");

        player1.bufferedCommand = FighterCommand.RightKick;

        int idleFrames = 0;
        yield return WaitUntilRealtime(() =>
        {
            if (player1.CurrentState is IdleState) idleFrames++;
            return player1.CurrentState is AttackState attack && attack.Data == player1.rightKickBase;
        }, "Roundhouse buffered during recovery never came out.");

        Assert.LessOrEqual(idleFrames, 1, "Buffered press should fire on the first actionable frame, not after a visible idle.");
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

        player1.EnterAttack(player1.rightKickBase);
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

        player1.EnterSpecialAttack(player1.hadoukenData);
        yield return WaitUntilRealtime(() => player1.Projectiles.CountInFlight() > 0, "Hadouken was never fired.");
        yield return WaitUntilRealtime(() => player2.Health.CurrentHealth < startHealth, "Hadouken never connected.");

        Assert.AreEqual(0, player1.Projectiles.CountInFlight(), "Projectile should return to the pool on impact.");
        Assert.AreEqual(projectilesBefore, CountProjectilesInScene(), "Firing must reuse pooled projectiles, never instantiate.");
        Assert.IsTrue(player2.CurrentState is HitstunState, "Projectile hit should put the defender in hitstun.");
        Assert.IsFalse(player1.IsFrozen, "A projectile landing must not freeze the fighter who threw it.");
    }

    private bool TrackApex(ref float apex)
    {
        return TrackApex(ref apex, player2.transform.position.y);
    }

    private static bool TrackApex(ref float apex, float value)
    {
        apex = Mathf.Max(apex, value);
        return true;
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
