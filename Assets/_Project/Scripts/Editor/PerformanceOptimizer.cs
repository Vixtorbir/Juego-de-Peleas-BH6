using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Applies the project-wide performance profile for the arena: a shadow setup sized to the combat
/// camera (short distance, two cascades, main light only), no unused screen copies, GPU
/// instancing on every project material, VSync pacing on every quality tier, the combat physics
/// layers with a pruned collision matrix, a kinematic projectile so its trigger never moves as
/// static geometry, and always-animating fighters. Idempotent; runs standalone from the menu or
/// as the last step of <see cref="FightingGameSetupPipeline.RunAll"/>.
/// </summary>
public static class PerformanceOptimizer
{
    private const string PipelineAssetPath = "Assets/Settings/PC_RPAsset.asset";
    private const string MaterialsRoot = "Assets/_Project/Art/Materials";
    private const string ProjectilePrefabPath = "Assets/_Project/Prefabs/Sphere.prefab";

    private const float ShadowDistance = 18f;
    private const int ShadowCascadeCount = 2;
    private const float CascadeSplit = 0.4f;
    private const int AdditionalLightsPerObjectLimit = 2;
    private const int SoftShadowQualityMedium = 2;
    private const float CameraFarClipPlane = 80f;

    [MenuItem("Fighting Game/Optimize Performance Settings", priority = 60)]
    public static void OptimizePerformanceSettings()
    {
        ConfigureRenderPipeline();
        ConfigureQualityTiers();
        ConfigurePhysics();
        ConfigureMaterials();
        ConfigureProjectilePrefab();
        ConfigureScene();

        AssetDatabase.SaveAssets();
        Debug.Log("[PerformanceOptimizer] Performance profile applied.");
    }

    /// <summary>
    /// The pipeline asset is edited through its serialized properties so the change survives
    /// URP API renames and is written exactly as the inspector would.
    /// </summary>
    private static void ConfigureRenderPipeline()
    {
        Object pipeline = AssetDatabase.LoadMainAssetAtPath(PipelineAssetPath);
        if (pipeline == null)
        {
            Debug.LogError($"[PerformanceOptimizer] Pipeline asset not found at '{PipelineAssetPath}'.");
            return;
        }

        var serialized = new SerializedObject(pipeline);
        SetFloat(serialized, "m_ShadowDistance", ShadowDistance);
        SetInt(serialized, "m_ShadowCascadeCount", ShadowCascadeCount);
        SetFloat(serialized, "m_Cascade2Split", CascadeSplit);
        SetBool(serialized, "m_AdditionalLightShadowsSupported", false);
        SetInt(serialized, "m_AdditionalLightsPerObjectLimit", AdditionalLightsPerObjectLimit);
        SetInt(serialized, "m_SoftShadowQuality", SoftShadowQualityMedium);
        SetBool(serialized, "m_RequireOpaqueTexture", false);
        SetBool(serialized, "m_UseSRPBatcher", true);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(pipeline);
    }

    /// <summary>
    /// VSync pacing is stored per quality tier, so every tier is visited; the runtime bootstrap in
    /// <see cref="PerformanceSettings"/> re-asserts the same values when the game starts.
    /// </summary>
    private static void ConfigureQualityTiers()
    {
        int activeTier = QualitySettings.GetQualityLevel();

        for (int tier = 0; tier < QualitySettings.names.Length; tier++)
        {
            QualitySettings.SetQualityLevel(tier, false);
            QualitySettings.vSyncCount = 1;
        }

        QualitySettings.SetQualityLevel(activeTier, false);
        Time.fixedDeltaTime = PerformanceSettings.FixedTimestep;
    }

    private static void ConfigurePhysics()
    {
        CombatLayers.EnsureLayers();
        CombatLayers.ConfigureCollisionMatrix();
    }

    private static void ConfigureMaterials()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialsRoot }))
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (material == null || material.enableInstancing) continue;

            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
        }
    }

    /// <summary>
    /// A trigger moved by its transform without a Rigidbody is a static collider to PhysX, which
    /// rebuilds broadphase data every time it moves. A kinematic body makes the projectile a proper
    /// moving trigger and lets it receive contact with the static boundary walls.
    /// </summary>
    private static void ConfigureProjectilePrefab()
    {
        GameObject prefab = PrefabUtility.LoadPrefabContents(ProjectilePrefabPath);
        if (prefab == null) return;

        Rigidbody body = prefab.TryGetComponent(out Rigidbody existing) ? existing : prefab.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.None;
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;

        CombatLayers.SetLayerRecursively(prefab, CombatLayers.ProjectileLayer);

        PrefabUtility.SaveAsPrefabAsset(prefab, ProjectilePrefabPath);
        PrefabUtility.UnloadPrefabContents(prefab);
    }

    private static void ConfigureScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != FighterSceneSetup.ScenePath)
            scene = EditorSceneManager.OpenScene(FighterSceneSetup.ScenePath, OpenSceneMode.Single);

        AssignFighterLayers();
        AssignStageLayers();
        ConfigureCamera();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void AssignFighterLayers()
    {
        foreach (RobotController fighter in Object.FindObjectsByType<RobotController>(FindObjectsSortMode.None))
        {
            CombatLayers.SetLayerRecursively(fighter.gameObject, CombatLayers.FighterLayer);

            foreach (Hitbox hitbox in fighter.GetComponentsInChildren<Hitbox>(true))
                hitbox.gameObject.layer = CombatLayers.HitboxLayer;

            Animator animator = fighter.animator != null ? fighter.animator : fighter.GetComponent<Animator>();
            if (animator != null)
            {
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false;
            }

            EditorUtility.SetDirty(fighter.gameObject);
        }
    }

    /// <summary>
    /// The floor is the only MeshCollider and the boundary cubes are the only BoxColliders outside
    /// the fighters and the decor root, so they can be classified without naming conventions.
    /// </summary>
    private static void AssignStageLayers()
    {
        foreach (Collider collider in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            GameObject target = collider.gameObject;
            if (target.GetComponentInParent<RobotController>() != null) continue;
            if (SceneHierarchyOrganizer.IsUnderDecor(target.transform, ArenaSceneSetup.ArenaRootName)) continue;

            if (collider is MeshCollider) target.layer = CombatLayers.GroundLayer;
            else if (collider is BoxCollider) target.layer = CombatLayers.WallLayer;
            else continue;

            EditorUtility.SetDirty(target);
        }
    }

    private static void ConfigureCamera()
    {
        Camera camera = Camera.main;
        if (camera == null) return;

        camera.farClipPlane = CameraFarClipPlane;
        EditorUtility.SetDirty(camera);
    }

    private static void SetFloat(SerializedObject target, string property, float value)
    {
        SerializedProperty found = target.FindProperty(property);
        if (found != null) found.floatValue = value;
        else Debug.LogWarning($"[PerformanceOptimizer] Property '{property}' not found on '{target.targetObject.name}'.");
    }

    private static void SetInt(SerializedObject target, string property, int value)
    {
        SerializedProperty found = target.FindProperty(property);
        if (found != null) found.intValue = value;
        else Debug.LogWarning($"[PerformanceOptimizer] Property '{property}' not found on '{target.targetObject.name}'.");
    }

    private static void SetBool(SerializedObject target, string property, bool value)
    {
        SerializedProperty found = target.FindProperty(property);
        if (found != null) found.boolValue = value;
        else Debug.LogWarning($"[PerformanceOptimizer] Property '{property}' not found on '{target.targetObject.name}'.");
    }
}
