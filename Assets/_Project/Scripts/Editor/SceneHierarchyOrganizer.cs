using System;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Files every object of the training scene under a fixed set of semantic root categories and
/// normalises the names of the gameplay objects, so the Hierarchy reads like a level layout instead
/// of a prototype. Objects are only moved and renamed, never recreated: every fileID, and with it
/// every serialized cross reference, survives the pass. The prototype's floor and boundary cubes
/// are kept as the stage collision (they are the ground and wall colliders the fighters and
/// projectiles rely on) and re-filed under English names at their exact world positions. The pass
/// ends with a reference audit that fails loudly if any gameplay binding was lost.
/// </summary>
public static class SceneHierarchyOrganizer
{
    public const string ManagersCategory = "[MANAGERS]";
    public const string LightingCategory = "[LIGHTING]";
    public const string EnvironmentCategory = "[ENVIRONMENT]";
    public const string FightersCategory = "[FIGHTERS]";
    public const string CamerasCategory = "[CAMERAS]";
    public const string UiCategory = "[UI]";

    public const string StageCollisionName = "Stage Collision";
    public const string StageFloorName = "Floor";
    public const string Player1SpawnName = "P1_SpawnPoint";
    public const string Player2SpawnName = "P2_SpawnPoint";

    private const string ScenePath = FighterSceneSetup.ScenePath;
    private const string MeleeHitboxName = "Hitbox_Melee";
    private const string ProjectileSpawnName = "ProjectileSpawnPoint";
    private const string LegacySpawnGroupName = "Spawns";
    private const string LegacyRingName = "Ring";

    private static readonly string[] CategoryOrder =
    {
        ManagersCategory, LightingCategory, EnvironmentCategory, FightersCategory, CamerasCategory, UiCategory
    };

    private static readonly Type[] ManagerTypes =
    {
        typeof(MatchManager), typeof(AudioManager), typeof(CombatEffectsManager), typeof(HitsparkPool)
    };

    [MenuItem("Fighting Game/Organize Scene Hierarchy", priority = 50)]
    public static void OrganizeSceneHierarchy()
    {
        Scene scene = EnsureSceneOpen();

        FileManagers();
        FileLighting();
        FileEnvironment();
        FileFighters();
        FileCameras();
        FileUi();
        OrderCategories();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        var audit = new StringBuilder();
        bool intact = VerifySceneReferences(audit);
        if (intact) Debug.Log($"[SceneHierarchyOrganizer] Hierarchy organised in '{scene.path}'.\n{audit}");
        else Debug.LogError($"[SceneHierarchyOrganizer] Hierarchy organised but references were lost:\n{audit}");
    }

    /// <summary>
    /// Root object for a category, created at the origin on first use so setup steps that spawn
    /// new scene objects can file them straight away.
    /// </summary>
    public static Transform GetOrCreateCategory(string category)
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == category) return root.transform;
        }

        var created = new GameObject(category);
        created.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        return created.transform;
    }

    /// <summary>True when <paramref name="target"/> is the decor root or sits anywhere below it.</summary>
    public static bool IsUnderDecor(Transform target, string decorRootName)
    {
        for (Transform current = target; current != null; current = current.parent)
        {
            if (current.name == decorRootName) return true;
        }

        return false;
    }

    private static Scene EnsureSceneOpen()
    {
        Scene active = SceneManager.GetActiveScene();
        return active.path == ScenePath ? active : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    // ------------------------------------------------------------------ managers

    /// <summary>
    /// Every manager ends on its own object named after its type. A manager sharing an object with
    /// another manager is moved to a fresh host with its serialized fields intact; managers are
    /// singletons reached through their static instance, so no scene reference points at them.
    /// </summary>
    private static void FileManagers()
    {
        Transform category = GetOrCreateCategory(ManagersCategory);

        foreach (Type managerType in ManagerTypes)
        {
            var manager = UnityEngine.Object.FindFirstObjectByType(managerType, FindObjectsInactive.Include) as MonoBehaviour;
            if (manager == null) continue;

            if (SharesHostWithAnotherManager(manager)) manager = RelocateToOwnHost(manager);

            GameObject host = manager.gameObject;
            host.name = managerType.Name;
            host.transform.SetParent(category, false);
            host.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            host.transform.SetAsLastSibling();
        }
    }

    private static bool SharesHostWithAnotherManager(MonoBehaviour manager)
    {
        foreach (Type other in ManagerTypes)
        {
            if (other == manager.GetType()) continue;
            if (manager.GetComponent(other) != null) return true;
        }

        return false;
    }

    private static MonoBehaviour RelocateToOwnHost(MonoBehaviour manager)
    {
        Type managerType = manager.GetType();
        var relocated = (MonoBehaviour)new GameObject(managerType.Name).AddComponent(managerType);
        EditorUtility.CopySerialized(manager, relocated);
        UnityEngine.Object.DestroyImmediate(manager);

        return relocated;
    }

    // ------------------------------------------------------------------ lighting

    /// <summary>
    /// Directional lights and the global volume make up the lighting rig. The sun of the render
    /// settings is the key light and takes the name the arena setup looks it up by; a scene that
    /// predates the sun binding falls back to the light that casts shadows.
    /// </summary>
    private static void FileLighting()
    {
        Transform category = GetOrCreateCategory(LightingCategory);
        Light keyLight = RenderSettings.sun;

        Light[] lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Array.Sort(lights, (a, b) => string.CompareOrdinal(a.name, b.name));

        foreach (Light light in lights)
        {
            if (light.type != LightType.Directional) continue;
            if (keyLight == null && light.shadows != LightShadows.None) keyLight = light;
            light.transform.SetParent(category, true);
            light.transform.SetAsLastSibling();
        }

        if (keyLight != null)
        {
            keyLight.name = StageSceneSetup.KeyLightName;
            keyLight.transform.SetAsFirstSibling();
        }

        foreach (Volume volume in UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!volume.isGlobal) continue;
            volume.transform.SetParent(category, true);
            volume.transform.SetAsLastSibling();
        }
    }

    // ------------------------------------------------------------------ environment

    private static void FileEnvironment()
    {
        Transform category = GetOrCreateCategory(EnvironmentCategory);

        GameObject decor = GameObject.Find(StageSceneSetup.StageRootName);
        if (decor != null) decor.transform.SetParent(category, true);

        FileStageCollision(category);
    }

    /// <summary>
    /// The floor is the scene's only MeshCollider and the boundaries its only BoxColliders outside
    /// the fighters and the decor, which is how the performance pass classifies them too. They are
    /// named by their geometry and re-parented in world space, so the corner distance the tests
    /// depend on does not move by a millimetre.
    /// </summary>
    private static void FileStageCollision(Transform category)
    {
        Transform collision = category.Find(StageCollisionName);
        if (collision == null)
        {
            collision = new GameObject(StageCollisionName).transform;
            collision.SetParent(category, false);
        }

        collision.SetAsFirstSibling();

        foreach (Collider collider in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            GameObject target = collider.gameObject;
            if (target.GetComponentInParent<RobotController>() != null) continue;
            if (IsUnderDecor(target.transform, StageSceneSetup.StageRootName)) continue;
            if (target.GetComponent<HadoukenProjectile>() != null) continue;

            switch (collider)
            {
                case MeshCollider:
                    target.name = StageFloorName;
                    break;
                case BoxCollider:
                    target.name = DescribeBoundary(target.transform);
                    break;
                default:
                    continue;
            }

            target.transform.SetParent(collision, true);
        }

        GameObject legacyRing = GameObject.Find(LegacyRingName);
        if (legacyRing != null && legacyRing.transform.childCount == 0) UnityEngine.Object.DestroyImmediate(legacyRing);
    }

    private static string DescribeBoundary(Transform wall)
    {
        Vector3 size = wall.lossyScale;
        Vector3 position = wall.position;

        if (size.x >= size.z) return position.z < 0f ? "Wall_Front" : "Wall_Back";
        return position.x < 0f ? "Wall_West" : "Wall_East";
    }

    // ------------------------------------------------------------------ fighters

    private static void FileFighters()
    {
        Transform category = GetOrCreateCategory(FightersCategory);

        RobotController[] fighters = UnityEngine.Object.FindObjectsByType<RobotController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Array.Sort(fighters, (a, b) => string.CompareOrdinal(a.name, b.name));

        foreach (RobotController fighter in fighters)
        {
            fighter.transform.SetParent(category, true);
            fighter.transform.SetAsLastSibling();

            if (fighter.hitboxBase != null) fighter.hitboxBase.name = MeleeHitboxName;
            if (fighter.hadoukenSpawnPoint != null) fighter.hadoukenSpawnPoint.name = ProjectileSpawnName;
        }

        MatchManager match = UnityEngine.Object.FindFirstObjectByType<MatchManager>(FindObjectsInactive.Include);
        if (match != null)
        {
            FileSpawnPoint(match.p1SpawnPoint, Player1SpawnName, category);
            FileSpawnPoint(match.p2SpawnPoint, Player2SpawnName, category);
        }

        GameObject legacyGroup = GameObject.Find(LegacySpawnGroupName);
        if (legacyGroup != null && legacyGroup.transform.childCount == 0) UnityEngine.Object.DestroyImmediate(legacyGroup);
    }

    private static void FileSpawnPoint(Transform spawn, string name, Transform category)
    {
        if (spawn == null) return;

        spawn.name = name;
        spawn.SetParent(category, true);
        spawn.SetAsLastSibling();
    }

    // ------------------------------------------------------------------ cameras and UI

    private static void FileCameras()
    {
        Transform category = GetOrCreateCategory(CamerasCategory);

        foreach (Camera camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            camera.transform.SetParent(category, true);
        }
    }

    private static void FileUi()
    {
        Transform category = GetOrCreateCategory(UiCategory);

        foreach (Canvas canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!canvas.isRootCanvas) continue;
            canvas.transform.SetParent(category, false);
            canvas.transform.SetAsFirstSibling();
        }

        foreach (EventSystem eventSystem in UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            eventSystem.transform.SetParent(category, false);
            eventSystem.transform.SetAsLastSibling();
        }
    }

    private static void OrderCategories()
    {
        for (int i = 0; i < CategoryOrder.Length; i++)
        {
            GetOrCreateCategory(CategoryOrder[i]).SetSiblingIndex(i);
        }
    }

    // ------------------------------------------------------------------ audit

    /// <summary>
    /// Confirms every gameplay binding the match depends on is still connected: fighters, spawn
    /// points and camera targets, the HUD bindings, and each fighter's own dependencies.
    /// </summary>
    public static bool VerifySceneReferences(StringBuilder audit)
    {
        bool intact = true;

        MatchManager match = UnityEngine.Object.FindFirstObjectByType<MatchManager>(FindObjectsInactive.Include);
        intact &= Require(audit, match, "MatchManager");
        if (match != null)
        {
            intact &= Require(audit, match.player1, "MatchManager.player1");
            intact &= Require(audit, match.player2, "MatchManager.player2");
            intact &= Require(audit, match.p1SpawnPoint, "MatchManager.p1SpawnPoint");
            intact &= Require(audit, match.p2SpawnPoint, "MatchManager.p2SpawnPoint");
        }

        FightingCamera camera = UnityEngine.Object.FindFirstObjectByType<FightingCamera>(FindObjectsInactive.Include);
        intact &= Require(audit, camera, "FightingCamera");
        if (camera != null)
        {
            intact &= Require(audit, camera.player1, "FightingCamera.player1");
            intact &= Require(audit, camera.player2, "FightingCamera.player2");
        }

        CombatHudController hud = UnityEngine.Object.FindFirstObjectByType<CombatHudController>(FindObjectsInactive.Include);
        intact &= Require(audit, hud, "CombatHudController");
        if (hud != null) intact &= RequireAllObjectReferences(audit, hud);

        foreach (RobotController fighter in UnityEngine.Object.FindObjectsByType<RobotController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string prefix = fighter.name;
            intact &= Require(audit, fighter.inputReader, prefix + ".inputReader");
            intact &= Require(audit, fighter.animator, prefix + ".animator");
            intact &= Require(audit, fighter.characterController, prefix + ".characterController");
            intact &= Require(audit, fighter.hitboxBase, prefix + ".hitboxBase");
            intact &= Require(audit, fighter.opponentTarget, prefix + ".opponentTarget");
            intact &= Require(audit, fighter.hadoukenSpawnPoint, prefix + ".hadoukenSpawnPoint");
            intact &= Require(audit, fighter.hadoukenData, prefix + ".hadoukenData");
            intact &= Require(audit, fighter.hadoukenPrefab, prefix + ".hadoukenPrefab");
        }

        foreach (Type managerType in ManagerTypes)
        {
            intact &= Require(audit, UnityEngine.Object.FindFirstObjectByType(managerType, FindObjectsInactive.Include), managerType.Name);
        }

        return intact;
    }

    private static bool RequireAllObjectReferences(StringBuilder audit, UnityEngine.Object target)
    {
        bool intact = true;
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.GetIterator();

        while (property.NextVisible(true))
        {
            if (property.propertyType != SerializedPropertyType.ObjectReference || property.name == "m_Script") continue;
            intact &= Require(audit, property.objectReferenceValue, $"{target.GetType().Name}.{property.name}");
        }

        return intact;
    }

    private static bool Require(StringBuilder audit, UnityEngine.Object reference, string label)
    {
        bool present = reference != null;
        audit.AppendLine($"  {(present ? "ok  " : "LOST")} {label}");
        return present;
    }
}
