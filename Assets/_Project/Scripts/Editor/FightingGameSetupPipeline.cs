using UnityEditor;
using UnityEngine;

/// <summary>
/// Runs the full scene integration in order: model import settings, attack timing calibration,
/// animator graph, fighter wiring, training room with lighting and post-processing, combat audio
/// and impact VFX. Exposed as a menu item and as a batch-mode entry point
/// (-executeMethod FightingGameSetupPipeline.RunAll).
/// </summary>
public static class FightingGameSetupPipeline
{
    [MenuItem("Fighting Game/Run Full Scene Setup", priority = 0)]
    public static void RunAll()
    {
        FighterModelImportSetup.ConfigureFighterModels();
        AttackTimingCalibrator.CalibrateAttackTiming();
        AnimatorAutoSetup.SetupAnimatorController();
        FighterSceneSetup.SetupFightersInScene();
        ArenaSceneSetup.BuildTrainingRoom();
        AudioSceneSetup.SetupCombatAudio();
        CombatVfxSceneSetup.SetupCombatVfx();
        AssetDatabase.SaveAssets();

        Debug.Log("[FightingGameSetupPipeline] Scene setup complete.");
    }
}
