using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Creates the shared <see cref="CombatRules"/> asset with the class defaults, authors the command
/// normals and jumping normals the roster needs beyond the calibrated base set, binds every attack,
/// the rules and the stage wall layer to the fighters in the training scene, and writes the hit
/// reactions and cancel flags so the combo routes (launch, wall bounce, ground bounce, knockdown,
/// jump and dash cancels) exist as data. Existing assets keep their tuned values; only missing
/// assets and references are created. New attacks reuse an existing clip through their own animator
/// trigger, so a purpose-made clip can replace the placeholder later without touching data.
/// </summary>
public static class CombatRulesSetup
{
    public const string RulesPath = "Assets/_Project/Combat/CombatRules.asset";

    private const string AttacksFolder = "Assets/_Project/Attacks";
    private const float Frame = 1f / 60f;

    private readonly struct AttackSpec
    {
        public readonly string AssetName;
        public readonly string Trigger;
        public readonly AttackWeight Weight;
        public readonly AttackImpactSound Sound;
        public readonly float Damage;
        public readonly float Hitstun;
        public readonly float Blockstun;
        public readonly float HitPushback;
        public readonly float BlockPushback;
        public readonly Vector3 HitboxOffset;
        public readonly Vector3 HitboxSize;
        public readonly bool IsAirAttack;
        public readonly bool IsOverhead;
        public readonly bool IsLauncher;
        public readonly bool SpecialCancelable;
        public readonly bool JumpCancelable;
        public readonly bool DashCancelable;
        public readonly HitReaction Grounded;
        public readonly HitReaction Airborne;

        public AttackSpec(string assetName, string trigger, AttackWeight weight, AttackImpactSound sound,
            float damage, float hitstun, float blockstun, float hitPushback, float blockPushback,
            Vector3 hitboxOffset, Vector3 hitboxSize, bool isAirAttack, bool isOverhead, bool isLauncher,
            bool specialCancelable, bool jumpCancelable, bool dashCancelable, HitReaction grounded, HitReaction airborne)
        {
            AssetName = assetName;
            Trigger = trigger;
            Weight = weight;
            Sound = sound;
            Damage = damage;
            Hitstun = hitstun;
            Blockstun = blockstun;
            HitPushback = hitPushback;
            BlockPushback = blockPushback;
            HitboxOffset = hitboxOffset;
            HitboxSize = hitboxSize;
            IsAirAttack = isAirAttack;
            IsOverhead = isOverhead;
            IsLauncher = isLauncher;
            SpecialCancelable = specialCancelable;
            JumpCancelable = jumpCancelable;
            DashCancelable = dashCancelable;
            Grounded = grounded;
            Airborne = airborne;
        }
    }

    /// <summary>
    /// Command normals and jumping normals. Frame timings are left to the calibrator, which reads
    /// them from the placeholder clip; jumping hitboxes are tall and sit low because the attacker is
    /// above the target.
    /// </summary>
    private static readonly AttackSpec[] AuthoredAttacks =
    {
        new AttackSpec("SO_Overhead", "Overhead", AttackWeight.Heavy, AttackImpactSound.Punch,
            16f, 0.5f, 0.3f, 3f, 3f, new Vector3(0f, 1f, 1f), new Vector3(0.5f, 1.2f, 0.8f),
            false, true, false, false, false, true, HitReaction.Hitstun, HitReaction.Hitstun),
        new AttackSpec("SO_AntiAir", "AntiAir", AttackWeight.Heavy, AttackImpactSound.Punch,
            12f, 0.4f, 0.3f, 1.5f, 3f, new Vector3(0f, 1.9f, 0.7f), new Vector3(0.5f, 1f, 0.7f),
            false, false, true, true, true, true, HitReaction.Launch, HitReaction.Launch),
        new AttackSpec("SO_AirLeftPunch", "AirLeftPunch", AttackWeight.Light, AttackImpactSound.Punch,
            5f, 0.3f, 0.17f, 1f, 1.5f, new Vector3(0f, 0.6f, 1f), new Vector3(0.6f, 1.6f, 1f),
            true, true, false, false, false, false, HitReaction.Hitstun, HitReaction.Hitstun),
        new AttackSpec("SO_AirLeftKick", "AirLeftKick", AttackWeight.Light, AttackImpactSound.Kick,
            7f, 0.33f, 0.18f, 1.5f, 2.5f, new Vector3(0f, 0.5f, 1.1f), new Vector3(0.6f, 1.6f, 1.1f),
            true, true, false, false, false, false, HitReaction.Hitstun, HitReaction.Hitstun),
        new AttackSpec("SO_AirRightPunch", "AirRightPunch", AttackWeight.Medium, AttackImpactSound.Punch,
            10f, 0.4f, 0.25f, 1.2f, 3f, new Vector3(0f, 0.6f, 1.1f), new Vector3(0.6f, 1.6f, 1f),
            true, true, false, false, false, false, HitReaction.Hitstun, HitReaction.Hitstun),
        new AttackSpec("SO_AirRightKick", "AirRightKick", AttackWeight.Heavy, AttackImpactSound.Kick,
            14f, 0.5f, 0.3f, 2f, 4f, new Vector3(0f, 0.5f, 1.2f), new Vector3(0.7f, 1.6f, 1.1f),
            true, true, false, false, false, false, HitReaction.Knockdown, HitReaction.GroundBounce)
    };

    private readonly struct CancelFlags
    {
        public readonly string AssetName;
        public readonly bool JumpCancelable;
        public readonly bool DashCancelable;

        public CancelFlags(string assetName, bool jumpCancelable, bool dashCancelable)
        {
            AssetName = assetName;
            JumpCancelable = jumpCancelable;
            DashCancelable = dashCancelable;
        }
    }

    private static readonly CancelFlags[] BaseAttackCancels =
    {
        new CancelFlags("SO_Launcher", true, true),
        new CancelFlags("SO_RightPunch", false, true),
        new CancelFlags("SO_RightKick", false, true)
    };

    private readonly struct ReactionAssignment
    {
        public readonly string AssetName;
        public readonly HitReaction Grounded;
        public readonly HitReaction Airborne;

        public ReactionAssignment(string assetName, HitReaction grounded, HitReaction airborne)
        {
            AssetName = assetName;
            Grounded = grounded;
            Airborne = airborne;
        }
    }

    private static readonly ReactionAssignment[] Reactions =
    {
        new ReactionAssignment("SO_Launcher", HitReaction.Launch, HitReaction.Launch),
        new ReactionAssignment("SO_Shoryuken", HitReaction.Launch, HitReaction.Launch),
        new ReactionAssignment("SO_RightKick", HitReaction.Hitstun, HitReaction.WallBounce),
        new ReactionAssignment("SO_RightPunch", HitReaction.Hitstun, HitReaction.GroundBounce),
        new ReactionAssignment("SO_CrouchSweep", HitReaction.Knockdown, HitReaction.Knockdown)
    };

    [MenuItem("Fighting Game/Setup Combat Rules", priority = 25)]
    public static void SetupCombatRules()
    {
        CombatRules rules = EnsureRulesAsset();
        EnsureAuthoredAttacks();
        AssignAttackReactions();
        AssignCancelFlags();
        BindFighters(rules);
        AssetDatabase.SaveAssets();

        Debug.Log($"[CombatRulesSetup] Combat rules ready at '{RulesPath}'.");
    }

    private static CombatRules EnsureRulesAsset()
    {
        CombatRules rules = AssetDatabase.LoadAssetAtPath<CombatRules>(RulesPath);
        if (rules != null) return rules;

        Directory.CreateDirectory(Path.GetDirectoryName(RulesPath));
        AssetDatabase.Refresh();

        rules = ScriptableObject.CreateInstance<CombatRules>();
        AssetDatabase.CreateAsset(rules, RulesPath);
        return rules;
    }

    private static void EnsureAuthoredAttacks()
    {
        foreach (AttackSpec spec in AuthoredAttacks)
        {
            string path = AttacksFolder + "/" + spec.AssetName + ".asset";
            if (AssetDatabase.LoadAssetAtPath<AttackData>(path) != null) continue;

            AttackData attack = ScriptableObject.CreateInstance<AttackData>();
            attack.animationTrigger = spec.Trigger;
            attack.weight = spec.Weight;
            attack.impactSound = spec.Sound;
            attack.damage = spec.Damage;
            attack.hitstunDuration = spec.Hitstun;
            attack.blockstunDuration = spec.Blockstun;
            attack.hitPushback = spec.HitPushback;
            attack.blockPushback = spec.BlockPushback;
            attack.hitboxOffset = spec.HitboxOffset;
            attack.hitboxSize = spec.HitboxSize;
            attack.attackHeight = AttackHeight.Mid;
            attack.isAirAttack = spec.IsAirAttack;
            attack.isOverhead = spec.IsOverhead;
            attack.isLauncher = spec.IsLauncher;
            attack.specialCancelable = spec.SpecialCancelable;
            attack.jumpCancelable = spec.JumpCancelable;
            attack.dashCancelable = spec.DashCancelable;
            attack.groundedReaction = spec.Grounded;
            attack.airborneReaction = spec.Airborne;
            AssetDatabase.CreateAsset(attack, path);
        }

        EnsureBranch("SO_AirLeftPunch", FighterCommand.LeftKick, "SO_AirLeftKick");
        EnsureBranch("SO_LeftPunch", FighterCommand.LeftPunch, "SO_LeftPunch");
    }

    /// <summary>
    /// Adds an explicit target-combo link when it is missing. Same-tier links that the gatling rule
    /// cannot express live here: the light-to-light air link and the rapid jab, whose repetition is
    /// bounded at runtime by the rules' rapid-chain limit.
    /// </summary>
    private static void EnsureBranch(string fromName, FighterCommand command, string toName)
    {
        AttackData from = LoadAttack(fromName);
        AttackData to = LoadAttack(toName);
        if (from == null || to == null) return;

        from.comboBranches ??= new List<ComboBranch>();
        foreach (ComboBranch existing in from.comboBranches)
        {
            if (existing.inputCommand == command && existing.nextAttack == to) return;
        }

        from.comboBranches.Add(new ComboBranch { inputCommand = command, nextAttack = to });
        EditorUtility.SetDirty(from);
    }

    private static void AssignCancelFlags()
    {
        foreach (CancelFlags flags in BaseAttackCancels)
        {
            AttackData attack = LoadAttack(flags.AssetName);
            if (attack == null) continue;
            if (attack.jumpCancelable == flags.JumpCancelable && attack.dashCancelable == flags.DashCancelable) continue;

            attack.jumpCancelable = flags.JumpCancelable;
            attack.dashCancelable = flags.DashCancelable;
            EditorUtility.SetDirty(attack);
        }
    }

    private static AttackData LoadAttack(string assetName)
    {
        return AssetDatabase.LoadAssetAtPath<AttackData>(AttacksFolder + "/" + assetName + ".asset");
    }

    /// <summary>
    /// Writes the authored reactions and removes duplicate explicit chain links, which the
    /// universal gatling rule made redundant.
    /// </summary>
    private static void AssignAttackReactions()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:AttackData", new[] { AttacksFolder }))
        {
            AttackData attack = AssetDatabase.LoadAssetAtPath<AttackData>(AssetDatabase.GUIDToAssetPath(guid));
            if (attack == null) continue;

            bool changed = RemoveDuplicateBranches(attack);

            foreach (ReactionAssignment assignment in Reactions)
            {
                if (attack.name != assignment.AssetName) continue;

                changed |= attack.groundedReaction != assignment.Grounded || attack.airborneReaction != assignment.Airborne;
                attack.groundedReaction = assignment.Grounded;
                attack.airborneReaction = assignment.Airborne;
            }

            if (changed) EditorUtility.SetDirty(attack);
        }
    }

    private static bool RemoveDuplicateBranches(AttackData attack)
    {
        if (attack.comboBranches == null) return false;

        bool changed = false;
        for (int i = attack.comboBranches.Count - 1; i > 0; i--)
        {
            ComboBranch branch = attack.comboBranches[i];
            for (int j = 0; j < i; j++)
            {
                ComboBranch earlier = attack.comboBranches[j];
                if (earlier.inputCommand != branch.inputCommand || earlier.nextAttack != branch.nextAttack) continue;

                attack.comboBranches.RemoveAt(i);
                changed = true;
                break;
            }
        }

        return changed;
    }

    private static void BindFighters(CombatRules rules)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != FighterSceneSetup.ScenePath)
            scene = EditorSceneManager.OpenScene(FighterSceneSetup.ScenePath, OpenSceneMode.Single);

        int wallLayer = CombatLayers.WallLayer;
        foreach (RobotController fighter in Object.FindObjectsByType<RobotController>(FindObjectsSortMode.None))
        {
            fighter.combatRules = rules;
            if (wallLayer >= 0) fighter.wallLayer = 1 << wallLayer;
            if (fighter.overheadBase == null) fighter.overheadBase = LoadAttack("SO_Overhead");
            if (fighter.antiAirBase == null) fighter.antiAirBase = LoadAttack("SO_AntiAir");
            if (fighter.airLeftPunchBase == null) fighter.airLeftPunchBase = LoadAttack("SO_AirLeftPunch");
            if (fighter.airRightPunchBase == null) fighter.airRightPunchBase = LoadAttack("SO_AirRightPunch");
            if (fighter.airLeftKickBase == null) fighter.airLeftKickBase = LoadAttack("SO_AirLeftKick");
            if (fighter.airRightKickBase == null) fighter.airRightKickBase = LoadAttack("SO_AirRightKick");
            EditorUtility.SetDirty(fighter);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}
