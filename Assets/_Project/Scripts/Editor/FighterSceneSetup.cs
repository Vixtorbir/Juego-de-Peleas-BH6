using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Replaces the visuals of every fighter in the training scene with a rigged humanoid model of real
/// combat proportions (the Explosive LLC Male and Heavy fighters) and re-fits the collision capsule, melee hitbox holder
/// and projectile anchor to the model. The projectile anchor is placed at the palms measured on the
/// Hadouken release frame, falling back to a height ratio when the clip cannot be sampled. Works on
/// the existing fighter GameObjects so every cross reference (opponent, input reader, attack data)
/// survives untouched.
/// </summary>
public static class FighterSceneSetup
{
    public const string ScenePath = "Assets/_Project/Scenes/TrainingRoom.unity";

    private const string ModelObjectName = "Model";
    private const string MeleeHitboxName = "Hitbox_Melee";
    private const string ProjectileSpawnName = "ProjectileSpawnPoint";
    private const string HitboxPreviewProperty = "previewAttack";

    private const float CapsuleRadius = 0.35f;
    private const float ProjectileHeightRatio = 0.6f;
    private const float ProjectileForwardOffset = 0.3f;
    private const float FallbackModelHeight = 1.8f;

    private static readonly string[] FighterModelPaths =
    {
        FighterModelImportSetup.Player1ModelPath,
        FighterModelImportSetup.Player2ModelPath
    };

    [MenuItem("Fighting Game/Setup Fighters In Scene", priority = 30)]
    public static void SetupFightersInScene()
    {
        Scene scene = EnsureSceneOpen();
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimatorAutoSetup.ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"[FighterSceneSetup] Animator controller not found at '{AnimatorAutoSetup.ControllerPath}'. Run 'Fighting Game/Setup Animator Controller' first.");
            return;
        }

        CombatLayers.EnsureLayers();

        RobotController[] fighters = UnityEngine.Object.FindObjectsByType<RobotController>(FindObjectsSortMode.None);
        Array.Sort(fighters, (a, b) => string.CompareOrdinal(a.name, b.name));

        if (fighters.Length == 0)
        {
            Debug.LogError("[FighterSceneSetup] No RobotController found in the open scene.");
            return;
        }

        Vector3? palmAnchor = MeasureProjectileAnchor(fighters);

        for (int i = 0; i < fighters.Length; i++)
        {
            string modelPath = FighterModelPaths[i % FighterModelPaths.Length];
            SetupFighter(fighters[i], modelPath, controller, palmAnchor);
        }

        for (int i = 0; i < fighters.Length; i++)
        {
            RobotController opponent = fighters[(i + 1) % fighters.Length];
            LinkOpponent(fighters[i], opponent);
            CopyMissingAssetReferences(fighters[i], opponent);
            ConfigureMeleeHitbox(fighters[i]);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[FighterSceneSetup] Configured {fighters.Length} fighter(s) in '{scene.path}'.");
    }

    private static Scene EnsureSceneOpen()
    {
        Scene active = SceneManager.GetActiveScene();
        if (active.path == ScenePath) return active;

        return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    /// <summary>
    /// Samples the Hadouken clip once for the whole roster; every fighter shares the animation set,
    /// so the release pose is the same for all of them.
    /// </summary>
    private static Vector3? MeasureProjectileAnchor(RobotController[] fighters)
    {
        foreach (RobotController fighter in fighters)
        {
            if (fighter.hadoukenData == null || fighter.hadoukenData.animationClip == null) continue;

            var report = new StringBuilder();
            if (HadoukenReleaseCalibrator.TryMeasureRelease(fighter.hadoukenData, report, out HadoukenReleaseCalibrator.ReleaseProfile profile))
                return profile.PalmAnchor;

            Debug.LogWarning($"[FighterSceneSetup] Hadouken release could not be measured; projectile anchor falls back to the height ratio.\n{report}");
            break;
        }

        return null;
    }

    private static void SetupFighter(RobotController fighter, string modelPath, AnimatorController controller, Vector3? palmAnchor)
    {
        GameObject root = fighter.gameObject;
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (modelAsset == null)
        {
            Debug.LogError($"[FighterSceneSetup] Model not found at '{modelPath}' for '{root.name}'.");
            return;
        }

        RemovePlaceholderVisuals(root);
        root.layer = CombatLayers.FighterLayer;
        GameObject model = ReplaceModel(root, modelAsset);
        float height = MeasureHeight(root, model);

        ConfigureAnimator(fighter, modelPath, controller);
        ConfigureCharacterController(fighter, height);
        EnsureComponent<Hurtbox>(root);
        EnsureComponent<CombatFlash>(root);

        fighter.hitboxBase = ConfigureMeleeHitbox(fighter);
        fighter.hadoukenSpawnPoint = ConfigureProjectileSpawn(fighter, height, palmAnchor);
        AssignTimedClips(fighter, controller);

        EditorUtility.SetDirty(fighter);
        Debug.Log($"[FighterSceneSetup] '{root.name}' -> '{modelAsset.name}' (height {height:F2} m).");
    }

    private static void RemovePlaceholderVisuals(GameObject root)
    {
        if (root.TryGetComponent(out MeshRenderer renderer)) UnityEngine.Object.DestroyImmediate(renderer);
        if (root.TryGetComponent(out MeshFilter filter)) UnityEngine.Object.DestroyImmediate(filter);
    }

    private static GameObject ReplaceModel(GameObject root, GameObject modelAsset)
    {
        RemovePreviousModels(root);

        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.transform);
        model.name = ModelObjectName;
        CombatLayers.SetLayerRecursively(model, root.layer);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;

        if (model.TryGetComponent(out Animator nestedAnimator))
            UnityEngine.Object.DestroyImmediate(nestedAnimator);

        return model;
    }

    /// <summary>
    /// Clears the previous visual, including prefab instances whose source asset no longer exists
    /// (their name carries a "Missing Prefab" suffix, so a plain name lookup would leave them behind).
    /// </summary>
    private static void RemovePreviousModels(GameObject root)
    {
        for (int i = root.transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = root.transform.GetChild(i).gameObject;
            bool isModel = child.name.StartsWith(ModelObjectName, StringComparison.Ordinal);
            bool isOrphanInstance = PrefabUtility.IsPrefabAssetMissing(child);

            if (isModel || isOrphanInstance) UnityEngine.Object.DestroyImmediate(child);
        }
    }

    private static float MeasureHeight(GameObject root, GameObject model)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return FallbackModelHeight;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        float height = bounds.max.y - root.transform.position.y;
        return height > 0.5f ? height : FallbackModelHeight;
    }

    private static void ConfigureAnimator(RobotController fighter, string modelPath, AnimatorController controller)
    {
        Animator animator = EnsureComponent<Animator>(fighter.gameObject);
        animator.avatar = FindAvatar(modelPath);
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.updateMode = AnimatorUpdateMode.Normal;

        fighter.animator = animator;
        if (animator.avatar == null)
            Debug.LogWarning($"[FighterSceneSetup] No avatar found in '{modelPath}'. Run 'Fighting Game/Configure Fighter Models (Humanoid)' first.");
    }

    private static Avatar FindAvatar(string modelPath)
    {
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
        {
            if (asset is Avatar avatar) return avatar;
        }

        return null;
    }

    private static void ConfigureCharacterController(RobotController fighter, float height)
    {
        CharacterController capsule = EnsureComponent<CharacterController>(fighter.gameObject);
        capsule.height = height;
        capsule.radius = Mathf.Min(CapsuleRadius, height * 0.25f);
        capsule.center = new Vector3(0f, height * 0.5f, 0f);
        fighter.characterController = capsule;
    }

    /// <summary>
    /// The melee hitbox holder sits at the fighter origin with no offset of its own: every attack
    /// reshapes the box from its <see cref="AttackData"/> offset and size, so the reach of each strike
    /// is authored next to its frame data instead of on a scene object. A kinematic Rigidbody makes
    /// the volume a kinematic trigger, so PhysX tracks it as a moving actor instead of a static shape.
    /// </summary>
    private static Hitbox ConfigureMeleeHitbox(RobotController fighter)
    {
        Hitbox hitbox = fighter.hitboxBase != null ? fighter.hitboxBase : fighter.GetComponentInChildren<Hitbox>(true);
        if (hitbox == null)
        {
            GameObject hitboxObject = new GameObject(MeleeHitboxName);
            hitboxObject.transform.SetParent(fighter.transform, false);
            hitboxObject.AddComponent<BoxCollider>();
            hitbox = hitboxObject.AddComponent<Hitbox>();
        }

        Rigidbody body = EnsureComponent<Rigidbody>(hitbox.gameObject);
        body.isKinematic = true;
        body.useGravity = false;

        hitbox.gameObject.layer = CombatLayers.HitboxLayer;
        hitbox.transform.localPosition = Vector3.zero;
        hitbox.transform.localRotation = Quaternion.identity;
        hitbox.transform.localScale = Vector3.one;

        AttackData preview = fighter.leftPunchBase;
        if (hitbox.TryGetComponent(out BoxCollider box))
        {
            box.isTrigger = true;
            if (preview != null)
            {
                box.center = preview.hitboxOffset;
                box.size = preview.hitboxSize;
            }
        }

        var serialized = new SerializedObject(hitbox);
        serialized.FindProperty(HitboxPreviewProperty).objectReferenceValue = preview;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return hitbox;
    }

    private static Transform ConfigureProjectileSpawn(RobotController fighter, float height, Vector3? palmAnchor)
    {
        Transform spawn = fighter.hadoukenSpawnPoint;
        if (spawn == null) spawn = fighter.transform.Find(ProjectileSpawnName);
        if (spawn == null)
        {
            spawn = new GameObject(ProjectileSpawnName).transform;
            spawn.SetParent(fighter.transform, false);
        }

        spawn.name = ProjectileSpawnName;
        Vector3 fallback = new Vector3(0f, height * ProjectileHeightRatio, fighter.characterController.radius + ProjectileForwardOffset);
        spawn.SetLocalPositionAndRotation(palmAnchor ?? fallback, Quaternion.identity);
        return spawn;
    }

    private static void AssignTimedClips(RobotController fighter, AnimatorController controller)
    {
        fighter.hitReactionClip = AnimatorAutoSetup.GetStateClip(controller, FighterAnimatorParameters.HitstunTrigger);
        fighter.blockReactionClip = AnimatorAutoSetup.GetStateClip(controller, FighterAnimatorParameters.BlockstunTrigger);
        fighter.dashForwardClip = AnimatorAutoSetup.GetStateClip(controller, FighterAnimatorParameters.DashForwardTrigger);
        fighter.dashBackwardClip = AnimatorAutoSetup.GetStateClip(controller, FighterAnimatorParameters.DashBackwardTrigger);
    }

    private static void LinkOpponent(RobotController fighter, RobotController opponent)
    {
        if (fighter == opponent) return;

        fighter.opponentTarget = opponent.transform;
        EditorUtility.SetDirty(fighter);
    }

    /// <summary>
    /// Fills any unassigned asset reference (attack data, projectile prefab) from the opponent so a
    /// fighter that was set up as a passive training dummy becomes fully playable.
    /// </summary>
    private static void CopyMissingAssetReferences(RobotController target, RobotController source)
    {
        if (target == source) return;

        var targetObject = new SerializedObject(target);
        var sourceObject = new SerializedObject(source);
        SerializedProperty property = targetObject.GetIterator();
        bool changed = false;

        while (property.NextVisible(true))
        {
            if (property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue != null) continue;

            SerializedProperty counterpart = sourceObject.FindProperty(property.propertyPath);
            if (counterpart == null || counterpart.objectReferenceValue == null) continue;
            if (!EditorUtility.IsPersistent(counterpart.objectReferenceValue)) continue;

            property.objectReferenceValue = counterpart.objectReferenceValue;
            changed = true;
        }

        if (changed) targetObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static T EnsureComponent<T>(GameObject target) where T : Component
    {
        return target.TryGetComponent(out T component) ? component : target.AddComponent<T>();
    }
}
