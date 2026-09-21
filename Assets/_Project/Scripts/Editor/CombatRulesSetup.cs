using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Creates the shared <see cref="CombatRules"/> asset with the class defaults, binds it and the
/// stage wall layer to every fighter in the training scene, and authors the hit reactions of the
/// roster's attacks so the juggle routes (launch, wall bounce, ground bounce, knockdown) exist as
/// data. Existing assets keep their values; only missing references are filled.
/// </summary>
public static class CombatRulesSetup
{
    public const string RulesPath = "Assets/_Project/Combat/CombatRules.asset";

    private const string AttacksFolder = "Assets/_Project/Attacks";

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
        AssignAttackReactions();
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
            EditorUtility.SetDirty(fighter);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}
