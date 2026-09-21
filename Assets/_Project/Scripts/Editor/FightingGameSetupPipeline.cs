using UnityEditor;
using UnityEngine;

/// <summary>
/// Runs the full scene integration in order: model import settings, attack timing calibration,
/// projectile release calibration, animator graph, fighter wiring, combat rules and hit reactions,
/// training room with lighting and
/// post-processing, combat audio, impact VFX, the performance profile (shadows, physics layers,
/// frame pacing) and finally the hierarchy layout with its reference audit.
/// Exposed as a menu item and as a batch-mode entry point (-executeMethod FightingGameSetupPipeline.RunAll).
/// </summary>
public static class FightingGameSetupPipeline
{
    [MenuItem("Fighting Game/Run Full Scene Setup", priority = 0)]
    public static void RunAll()
    {
        FighterModelImportSetup.ConfigureFighterModels();
        AttackTimingCalibrator.CalibrateAttackTiming();
        HadoukenReleaseCalibrator.CalibrateHadoukenRelease();
        AnimatorAutoSetup.SetupAnimatorController();
        FighterSceneSetup.SetupFightersInScene();
        CombatRulesSetup.SetupCombatRules();
        ArenaSceneSetup.BuildTrainingRoom();
        AudioSceneSetup.SetupCombatAudio();
        CombatVfxSceneSetup.SetupCombatVfx();
        PerformanceOptimizer.OptimizePerformanceSettings();
        SceneHierarchyOrganizer.OrganizeSceneHierarchy();
        AssetDatabase.SaveAssets();

        Debug.Log("[FightingGameSetupPipeline] Scene setup complete.");
    }
}
