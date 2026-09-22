#if UNITY_INCLUDE_TESTS && UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode coverage of the combat feel rules in the training scene: chain and gatling cancels
/// produce true combos, launchers put the defender into a juggle whose height decays with every
/// air hit, a launcher jump-cancels into an air chain that spikes the body into a ground bounce,
/// an air dash is granted once per jump, an overhead breaks a crouching guard while a mid does not,
/// every clean hit freezes both fighters on their own timers with the defender held longer, a wall
/// bounce is granted once per combo, a knockdown cannot be hit off the ground, a press during
/// recovery fires on the first actionable frame, a cornered defender stays pinned while the attacker
/// absorbs the pushback, and projectiles land and recycle without growing the scene.
/// </summary>
public class CombatChainPlayModeTests
{
    private const string ScenePath = "Assets/_Project/Scenes/TrainingRoom.unity";
    private const float IntroWait = 2.5f;
    private const float ContactDistance = 1.25f;
    private const float StepTimeout = 3f;
    private const float RouteTimeout = 6f;
    private const float CornerX = 10.7f;
    private const float WallPinTolerance = 0.08f;
    private const float WallBounceTolerance = 0.2f;
    private const float MinAttackerCornerShove = 0.1f;
    private const float RelaunchHeight = 1.5f;
    private const float MinJuggleDecay = 0.5f;
    private const float WhiffDistance = 6f;
    private const float RecoveryProbeDelay = 0.25f;
    private const float AirDashLockoutProbe = 0.15f;
    private const float AirDashProbeHeight = 1.2f;
    private const float JumpInHeight = 2f;
    private const float DashAttackProbeDelay = 0.06f;
    private const float MomentumProbeDuration = 0.15f;
    private const float MinDashAttackTravel = 0.3f;
    private const float MinMicroStepTravel = 0.1f;

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
    public IEnumerator LauncherJumpCancelsIntoAirCombo()
    {
        FighterCommand[] airRoute = { FighterCommand.LeftPunch, FighterCommand.RightPunch, FighterCommand.RightKick };
        float facing = player1.GetScreenFacingSign();
        float lastHealth = player2.Health.CurrentHealth;
        int hitsLanded = 0;
        int nextStep = 0;
        bool defenderRecoveredBetweenHits = false;

        player1.EnterAttack(player1.launcherBase);
        yield return WaitForState<AirborneHurtState>(player2);
        hitsLanded = 1;
        lastHealth = player2.Health.CurrentHealth;

        player1.SetMovementInput(new Vector2(facing, 1f));
        yield return WaitForState<JumpState>(player1);
        player1.SetMovementInput(new Vector2(facing, 0f));
        Assert.IsTrue(player2.CurrentState is AirborneHurtState, "Jump cancel must not interrupt the launch.");

        float deadline = Time.realtimeSinceStartup + RouteTimeout;
        while (Time.realtimeSinceStartup < deadline && hitsLanded < airRoute.Length + 1)
        {
            if (nextStep == 0 && player1.CurrentState is JumpState && player1.IsAboveAirActionHeight)
            {
                player1.bufferedCommand = airRoute[nextStep++];
            }

            if (player2.Health.CurrentHealth < lastHealth)
            {
                lastHealth = player2.Health.CurrentHealth;
                hitsLanded++;
                if (nextStep > 0 && nextStep < airRoute.Length) player1.bufferedCommand = airRoute[nextStep++];
            }
            else if (player2.CurrentState.IsNeutral)
            {
                defenderRecoveredBetweenHits = true;
            }

            yield return null;
        }

        player1.SetMovementInput(Vector2.zero);
        Assert.AreEqual(airRoute.Length + 1, hitsLanded, "Launcher > jump cancel > j.LP > j.RP > j.RK should land four hits.");
        Assert.IsFalse(defenderRecoveredBetweenHits, "Defender regained control mid-air: a link in the air chain dropped.");
        Assert.AreEqual(1, player2.Hurtbox.Combo.GroundBounces, "The air spike should grant the combo's ground bounce.");

        yield return WaitUntilRealtime(() => player2.CurrentState is KnockdownState, "Spiked defender never came to rest in a knockdown.");
    }

    [UnityTest]
    public IEnumerator AirDashExecutesOncePerJumpAndResetsOnLanding()
    {
        Teleport(player2, -0.5f + WhiffDistance);
        yield return null;

        player1.SetMovementInput(Vector2.up);
        yield return WaitForState<JumpState>(player1);
        player1.SetMovementInput(Vector2.zero);
        yield return WaitUntilRealtime(() => player1.transform.position.y > AirDashProbeHeight, "Fighter never rose high enough to probe the air dash.");

        player1.bufferedCommand = FighterCommand.DashForward;
        yield return WaitForState<AirDashState>(player1);
        Assert.AreEqual(1, player1.AirDashesUsed, "First air dash should consume the jump's allowance.");

        yield return WaitForState<JumpState>(player1);
        player1.bufferedCommand = FighterCommand.DashForward;
        yield return new WaitForSecondsRealtime(AirDashLockoutProbe);

        Assert.IsFalse(player1.CurrentState is AirDashState, "A second air dash in the same jump must be locked out.");
        Assert.AreEqual(1, player1.AirDashesUsed, "Locked-out dash must not count.");
        Assert.AreEqual(FighterCommand.None, player1.bufferedCommand, "Locked-out dash press should expire with the buffer leniency instead of firing on landing.");

        yield return WaitForState<IdleState>(player1);
        Assert.AreEqual(0, player1.AirDashesUsed, "Landing should restore the air dash.");

        player1.SetMovementInput(Vector2.up);
        yield return WaitForState<JumpState>(player1);
        player1.SetMovementInput(Vector2.zero);
        yield return WaitUntilRealtime(() => player1.transform.position.y > AirDashProbeHeight, "Fighter never rose high enough on the second jump.");

        player1.bufferedCommand = FighterCommand.DashForward;
        yield return WaitForState<AirDashState>(player1);
    }

    [UnityTest]
    public IEnumerator OverheadCommandNormalBreaksCrouchingGuard()
    {
        float defenderFacing = player2.GetScreenFacingSign();
        player2.SetMovementInput(new Vector2(-defenderFacing, -1f));
        yield return WaitForState<CrouchState>(player2);

        float guardedHealth = player2.Health.CurrentHealth;
        Assert.IsTrue(player2.Hurtbox.ReceiveHit(player1.rightPunchBase, player1), "Mid attack on a crouching guard should still connect as a block.");
        Assert.IsTrue(player2.CurrentState is BlockstunState, "A true mid must be blockable crouching.");
        Assert.Greater(player2.Health.CurrentHealth, guardedHealth - player1.rightPunchBase.damage * 0.5f, "Blocked mid should only chip.");

        yield return WaitForState<CrouchState>(player2);
        float crouchGuardHealth = player2.Health.CurrentHealth;

        player1.bufferedCommand = FighterCommand.Overhead;
        yield return WaitUntilRealtime(() => player1.CurrentState is AttackState attack && attack.Data == player1.overheadBase, "Forward + RightPunch never resolved to the overhead.");
        yield return WaitForState<HitstunState>(player2);
        Assert.Less(player2.Health.CurrentHealth, crouchGuardHealth - player1.overheadBase.damage * 0.5f, "Overhead should break a crouching guard for full damage.");

        player2.SetMovementInput(new Vector2(-defenderFacing, 0f));
        yield return WaitUntilRealtime(() => player2.CurrentState.IsNeutral && !player2.CurrentState.IsCrouchingPosture, "Defender never stood back up.");

        float standingGuardHealth = player2.Health.CurrentHealth;
        Assert.IsTrue(player2.Hurtbox.ReceiveHit(player1.overheadBase, player1), "Overhead on a standing guard should connect as a block.");
        Assert.IsTrue(player2.CurrentState is BlockstunState, "Overhead must be blockable standing.");
        Assert.Greater(player2.Health.CurrentHealth, standingGuardHealth - player1.overheadBase.damage * 0.5f, "Blocked overhead should only chip.");
        player2.SetMovementInput(Vector2.zero);
    }

    [UnityTest]
    public IEnumerator AirSpikeGroundBouncesOnceThenKnocksDown()
    {
        player1.EnterAttack(player1.launcherBase);
        yield return WaitForState<AirborneHurtState>(player2);
        yield return WaitUntilRealtime(() => player2.transform.position.y > 0.6f, "Defender never left the ground.");

        Assert.IsTrue(player2.Hurtbox.ReceiveHit(player1.airRightKickBase, player1), "Air spike on the airborne defender was rejected.");
        AirborneHurtState juggle = player2.CurrentState as AirborneHurtState;
        Assert.IsNotNull(juggle, "Spike should keep the defender in the juggle state until it lands.");
        Assert.AreEqual(BounceMode.GroundBounce, juggle.PendingBounce, "First spike should grant the ground bounce.");
        Assert.AreEqual(1, player2.Hurtbox.Combo.GroundBounces, "Ground bounce should be consumed on the granting hit.");

        yield return WaitUntilRealtime(() => juggle.PendingBounce == BounceMode.None && player2.CurrentState is AirborneHurtState, "Defender never bounced off the floor.");
        yield return WaitUntilRealtime(() => player2.transform.position.y > 0.6f, "Defender did not pop back up after the bounce.");

        Assert.IsTrue(player2.Hurtbox.ReceiveHit(player1.airRightKickBase, player1), "Second air spike was rejected.");
        Assert.AreEqual(BounceMode.None, juggle.PendingBounce, "Second spike in the same combo must not bounce again.");
        Assert.AreEqual(1, player2.Hurtbox.Combo.GroundBounces, "Ground bounce count must stay at one per combo.");

        yield return WaitUntilRealtime(() => player2.CurrentState is KnockdownState, "Spent spike should drop the defender straight into a knockdown.");
    }

    [UnityTest]
    public IEnumerator InvulnerableOpponentDeflectsHitsWithZeroDamage()
    {
        float startHealth = player2.Health.CurrentHealth;
        int deflectsBefore = player2.Hurtbox.DeflectCount;

        player2.GrantInvulnerability(1f);
        yield return null;
        Assert.IsTrue(player2.Flash.IsFlashing, "Invulnerability should tint the body.");

        Assert.IsFalse(player2.Hurtbox.TryReceiveHit(player1.leftPunchBase, player1, player2.transform.position, true, out HitRejection rejection));
        Assert.AreEqual(HitRejection.Invulnerable, rejection, "Strike on an invulnerable body should report a deflection.");

        player1.EnterAttack(player1.leftPunchBase);
        yield return WaitUntilRealtime(() => player2.Hurtbox.DeflectCount > deflectsBefore, "Jab into the invulnerable body never deflected.");

        Assert.AreEqual(startHealth, player2.Health.CurrentHealth, 0.001f, "Deflected strike dealt damage.");
        Assert.IsFalse(player1.AttackConnected, "Deflected strike must not count as connected.");
        Assert.IsFalse(player1.hitboxBase.IsActive, "Deflected strike should spend the hitbox.");
        Assert.IsTrue(player2.CurrentState is IdleState, "Deflection must not move the defender out of its state.");

        yield return WaitUntilRealtime(() => !player2.IsInvincible && player1.CurrentState is IdleState, "Fighters never settled after the deflection.");

        player1.EnterCrouchAttack(player1.crouchSweepBase);
        yield return WaitForState<KnockdownState>(player2);
        float downedHealth = player2.Health.CurrentHealth;

        Assert.IsFalse(player2.Hurtbox.TryReceiveHit(player1.leftPunchBase, player1, player2.transform.position, true, out rejection));
        Assert.AreEqual(HitRejection.Down, rejection, "Strike on a downed body should report a deflection.");
        Assert.AreEqual(downedHealth, player2.Health.CurrentHealth, 0.001f, "Downed body took damage.");
    }

    [UnityTest]
    public IEnumerator FullGatlingStringConnectsWithoutSpacingDropDueToMicroStep()
    {
        FighterCommand[] route = { FighterCommand.LeftPunch, FighterCommand.RightPunch, FighterCommand.RightKick, FighterCommand.Launcher };
        float facing = player1.GetScreenFacingSign();
        float lastHealth = player2.Health.CurrentHealth;
        int hitsLanded = 0;
        int nextStep = 1;
        bool defenderRecoveredBetweenHits = false;
        bool launcherObserved = false;
        float launcherEntryX = 0f;

        player1.bufferedCommand = route[0];

        float deadline = Time.realtimeSinceStartup + RouteTimeout;
        while (Time.realtimeSinceStartup < deadline && hitsLanded < route.Length)
        {
            if (!launcherObserved && player1.CurrentState is AttackState attack && attack.Data == player1.launcherBase)
            {
                launcherObserved = true;
                launcherEntryX = player1.transform.position.x;
            }

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

        Assert.AreEqual(route.Length, hitsLanded, "Jab > Cross > Roundhouse > Launcher should land four hits despite the roundhouse pushback.");
        Assert.IsFalse(defenderRecoveredBetweenHits, "Defender regained control mid-route: the string dropped.");
        Assert.IsTrue(launcherObserved, "Launcher never started.");
        Assert.Greater((player1.transform.position.x - launcherEntryX) * facing, MinMicroStepTravel, "Launcher should micro-step towards the pushed-back defender.");
        Assert.IsTrue(player2.CurrentState is AirborneHurtState, "Launcher should end the string with a juggle.");
    }

    [UnityTest]
    public IEnumerator AirAttackOnConnectCancelsLandingRecoveryIntoGroundedChain()
    {
        float facing = player1.GetScreenFacingSign();
        float startHealth = player2.Health.CurrentHealth;

        player1.SetMovementInput(new Vector2(facing, 1f));
        yield return WaitForState<JumpState>(player1);
        player1.SetMovementInput(Vector2.zero);

        yield return WaitUntilRealtime(() => player1.AirVelocity.y < 0f && player1.transform.position.y < JumpInHeight, "Fighter never came down into jump-in range.");
        player1.bufferedCommand = FighterCommand.LeftPunch;
        yield return WaitUntilRealtime(() => player2.Health.CurrentHealth < startHealth, "Jump-in jab never connected.");
        float afterJumpIn = player2.Health.CurrentHealth;

        player1.PressCommand(FighterCommand.Shoryuken);

        int idleFrames = 0;
        int recoveryFrames = 0;
        bool defenderRecovered = false;
        yield return WaitUntilRealtime(() =>
        {
            if (player1.CurrentState is IdleState) idleFrames++;
            if (player1.IsGrounded && player1.CurrentState is AirAttackState) recoveryFrames++;
            if (player2.CurrentState.IsNeutral) defenderRecovered = true;
            return player1.CurrentState is ShoryukenState;
        }, "Shoryuken never came out after the jump-in.");

        Assert.LessOrEqual(recoveryFrames, 1, "A connected jump-in should land-cancel instead of serving landing recovery.");
        Assert.LessOrEqual(idleFrames, 1, "Buffered Shoryuken should fire on the first grounded frame.");
        Assert.IsFalse(defenderRecovered, "Defender recovered before the ground follow-up: the jump-in link dropped.");

        yield return WaitUntilRealtime(() => player2.Health.CurrentHealth < afterJumpIn, "Shoryuken after the land-cancel never connected.");

        yield return WaitUntilRealtime(() => player1.CurrentState.IsNeutral && player2.CurrentState.IsNeutral, "Fighters never returned to neutral.");
        Teleport(player2, player1.transform.position.x + facing * WhiffDistance);
        yield return null;

        player1.SetMovementInput(new Vector2(facing, 1f));
        yield return WaitForState<JumpState>(player1);
        player1.SetMovementInput(Vector2.zero);
        yield return WaitUntilRealtime(() => player1.AirVelocity.y < 0f && player1.transform.position.y < JumpInHeight, "Fighter never came down on the whiffed jump-in.");
        player1.bufferedCommand = FighterCommand.LeftPunch;
        yield return WaitForState<AirAttackState>(player1);

        int whiffRecoveryFrames = 0;
        yield return WaitUntilRealtime(() =>
        {
            if (player1.IsGrounded && player1.CurrentState is AirAttackState) whiffRecoveryFrames++;
            return player1.CurrentState is IdleState;
        }, "Whiffed jump-in never landed.");

        Assert.GreaterOrEqual(whiffRecoveryFrames, 2, "A whiffed jump-in must still pay its landing recovery.");
    }

    [UnityTest]
    public IEnumerator DashAttackCarriesForwardMomentum()
    {
        Teleport(player2, -0.5f + WhiffDistance);
        yield return null;
        float facing = player1.GetScreenFacingSign();

        player1.bufferedCommand = FighterCommand.DashForward;
        yield return WaitForState<DashState>(player1);
        yield return new WaitForSeconds(DashAttackProbeDelay);

        player1.bufferedCommand = FighterCommand.RightPunch;
        yield return WaitUntilRealtime(() => player1.CurrentState is AttackState attack && attack.Data == player1.rightPunchBase, "Cross never came out of the dash.");
        float dashAttackStartX = player1.transform.position.x;
        yield return new WaitForSeconds(MomentumProbeDuration);
        float dashAttackTravel = (player1.transform.position.x - dashAttackStartX) * facing;

        yield return WaitForState<IdleState>(player1);
        player1.bufferedCommand = FighterCommand.RightPunch;
        yield return WaitUntilRealtime(() => player1.CurrentState is AttackState attack && attack.Data == player1.rightPunchBase, "Cross never came out of idle.");
        float idleAttackStartX = player1.transform.position.x;
        yield return new WaitForSeconds(MomentumProbeDuration);
        float idleAttackTravel = (player1.transform.position.x - idleAttackStartX) * facing;

        Assert.Greater(dashAttackTravel, MinDashAttackTravel, "A cross started from a forward dash should keep sliding forward.");
        Assert.Greater(dashAttackTravel, idleAttackTravel + MinDashAttackTravel * 0.5f, "Dash attack travel should clearly exceed a rooted attack.");
    }

    [UnityTest]
    public IEnumerator QueuedInputsResolveInOrder()
    {
        AttackData[] expected = { player1.leftPunchBase, player1.rightPunchBase, player1.rightKickBase };
        AttackData[] observed = new AttackData[expected.Length];
        int observedCount = 0;
        float lastHealth = player2.Health.CurrentHealth;
        int hitsLanded = 0;

        player1.PressCommand(FighterCommand.LeftPunch);
        yield return WaitForState<AttackState>(player1);
        observed[observedCount++] = ((AttackState)player1.CurrentState).Data;

        player1.PressCommand(FighterCommand.RightPunch);
        player1.PressCommand(FighterCommand.RightKick);
        Assert.AreEqual(FighterCommand.RightPunch, player1.bufferedCommand, "Second press should take the head of the buffer.");
        Assert.AreEqual(FighterCommand.RightKick, player1.QueuedCommand, "Third press should wait in the queue.");

        float deadline = Time.realtimeSinceStartup + RouteTimeout;
        while (Time.realtimeSinceStartup < deadline && hitsLanded < expected.Length)
        {
            if (player1.CurrentState is AttackState attack && observedCount < observed.Length && attack.Data != observed[observedCount - 1])
            {
                observed[observedCount++] = attack.Data;
            }

            if (player2.Health.CurrentHealth < lastHealth)
            {
                lastHealth = player2.Health.CurrentHealth;
                hitsLanded++;
            }

            yield return null;
        }

        Assert.AreEqual(expected.Length, hitsLanded, "Three queued presses should land three hits.");
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.AreEqual(expected[i], observed[i], $"Attack {i + 1} came out of order.");
        }
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
