using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Adds the <see cref="TrainingModeController"/> to the training scene and binds it to the match
/// manager. Idempotent: an existing controller is rebound in place so its tuned values survive.
/// </summary>
public static class TrainingModeSceneSetup
{
    private const string HostName = nameof(TrainingModeController);

    [MenuItem("Fighting Game/Setup Training Mode", priority = 20)]
    public static void SetupTrainingMode()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != FighterSceneSetup.ScenePath)
            scene = EditorSceneManager.OpenScene(FighterSceneSetup.ScenePath, OpenSceneMode.Single);

        MatchManager match = Object.FindFirstObjectByType<MatchManager>(FindObjectsInactive.Include);
        if (match == null)
        {
            Debug.LogError("[TrainingModeSceneSetup] No MatchManager in the training scene; run the full scene setup first.");
            return;
        }

        TrainingModeController training = Object.FindFirstObjectByType<TrainingModeController>(FindObjectsInactive.Include);
        if (training == null) training = new GameObject(HostName).AddComponent<TrainingModeController>();

        var serialized = new SerializedObject(training);
        serialized.FindProperty("matchManager").objectReferenceValue = match;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[TrainingModeSceneSetup] Training mode bound to the match manager.");
    }
}
