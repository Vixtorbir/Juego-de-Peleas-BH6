using UnityEditor;
using UnityEngine;

/// <summary>
/// Runs the full scene integration in order: model import settings, combat rules with the authored
/// attack roster, animator graph (which assigns placeholder clips), attack timing calibration,
/// projectile release calibration, fighter wiring, modular training stage with lighting and
/// post-processing, combat audio, impact VFX, the combat HUD, training mode, the performance profile (shadows, physics layers,
/// frame pacing) and finally the hierarchy layout with its reference audit.
/// Exposed as a menu item and as a batch-mode entry point (-executeMethod FightingGameSetupPipeline.RunAll).
/// </summary>
public static class FightingGameSetupPipeline
{
    [MenuItem("Fighting Game/Run Full Scene Setup", priority = 0)]
    public static void RunAll()
    {
        FighterModelImportSetup.ConfigureFighterModels();
        CombatRulesSetup.SetupCombatRules();
        AnimatorAutoSetup.SetupAnimatorController();
        AttackTimingCalibrator.CalibrateAttackTiming();
        HadoukenReleaseCalibrator.CalibrateHadoukenRelease();
        FighterSceneSetup.SetupFightersInScene();
        StageSceneSetup.BuildTrainingStage();
        AudioSceneSetup.SetupCombatAudio();
        CombatVfxSceneSetup.SetupCombatVfx();
        CombatHudSceneSetup.SetupCombatHud();
        TrainingModeSceneSetup.SetupTrainingMode();
        PerformanceOptimizer.OptimizePerformanceSettings();
        SceneHierarchyOrganizer.OrganizeSceneHierarchy();
        AssetDatabase.SaveAssets();

        Debug.Log("[FightingGameSetupPipeline] Scene setup complete.");
    }
}
