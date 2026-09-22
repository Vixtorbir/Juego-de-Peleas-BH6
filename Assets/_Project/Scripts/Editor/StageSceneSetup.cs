using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Installs the modular training stage: converts the vendor materials from the built-in pipeline
/// to URP, places the stage prefab so its floor sits at ground height with its wall behind the
/// fighters, extends the grid floor under the whole camera range, moves the gameplay boundary walls
/// to the visible edges of the stage, lights the room from the shipped HDRI sky with a warm key and
/// cool fill, tunes the post-processing volume, frames the camera and adds drifting dust. The stage
/// is rebuilt under a single root on every run and any decor left by the earlier procedural arena is
/// removed; gameplay objects (floor collider, boundary walls, fighters, managers) are only
/// repositioned or hidden, never replaced.
/// </summary>
public static class StageSceneSetup
{
    public const string StageRootName = "Stage";
    public const string KeyLightName = "Key Light";
    public const string DustMotesName = "Dust Motes";

    private const string ScenePath = FighterSceneSetup.ScenePath;
    private const string StagePrefabPath = "Assets/SDF_BasicTrainingStage/Prefabs/Environment/TrainingStage_Background.prefab";
    private const string StageMaterialsRoot = "Assets/SDF_BasicTrainingStage/Materials";
    private const string GridMaterialPath = StageMaterialsRoot + "/Environments/TrainingStage/TrainSta_Grid_MAT.mat";
    private const string GridGlowMaterialPath = StageMaterialsRoot + "/Environments/TrainingStage/TrainSta_GridGlow_MAT.mat";
    private const string SkyboxMaterialPath = "Assets/SDF_BasicTrainingStage/Textures/HDRI/HDRI_Training_Stage_MAT.mat";
    private const string FighterPackRoot = FighterModelImportSetup.FighterPackRoot;
    private const string TexturesRoot = "Assets/_Project/Art/Environment/Textures";
    private const string MaterialsRoot = "Assets/_Project/Art/Materials";
    private const string RenderingRoot = "Assets/_Project/Art/Rendering";
    private const string VolumeProfilePath = RenderingRoot + "/ArenaPostProcess.asset";
    private const string ProjectilePrefabPath = "Assets/_Project/Prefabs/Sphere.prefab";
    private const string LitShaderName = "Universal Render Pipeline/Lit";
    private const string UnlitShaderName = "Universal Render Pipeline/Unlit";
    private const string ParticleShaderName = "Universal Render Pipeline/Particles/Unlit";
    private const string NormalMapKeyword = "_NORMALMAP";
    private const string TransparentSurfaceKeyword = "_SURFACE_TYPE_TRANSPARENT";

    private const string FillLightName = "Fill Light";
    private const string RimLightName = "Rim Light";
    private const string GlobalVolumeName = "Global Volume";
    private const string FloorExtensionName = "Floor Extension";
    private const string LegacyArenaRootName = "Arena";
    private const string WallEastName = "Wall_East";
    private const string WallWestName = "Wall_West";

    private const float StageFrontZ = -7f;
    private const float FloorExtensionHalfSize = 30f;
    private const float FloorExtensionDrop = 0.02f;
    private const float BoundaryWallX = 11.6f;
    private const float CameraSideBound = 8f;
    private const int DustMoteCount = 180;

    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
    private static readonly int GlossinessId = Shader.PropertyToID("_Glossiness");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int TintColorId = Shader.PropertyToID("_TintColor");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
    private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
    private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
    private static readonly int BlendId = Shader.PropertyToID("_Blend");
    private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

    private static readonly Color EnergyColor = new Color(0.35f, 0.8f, 1f);

    [MenuItem("Fighting Game/Build Training Stage", priority = 40)]
    public static void BuildTrainingStage()
    {
        Scene scene = EnsureSceneOpen();

        ConvertVendorMaterials();
        Material gridMaterial = AssetDatabase.LoadAssetAtPath<Material>(GridMaterialPath);

        Transform root = RebuildStageRoot();
        PlaceStagePrefab(root);
        BuildFloorExtension(root, gridMaterial);
        BuildDustMotes(root, CreateDustMaterial());
        PlaceBoundaryWalls();
        HideCollisionGeometry(gridMaterial);
        ConfigureLighting();
        ConfigurePostProcessing();
        ConfigureCameraFraming();
        ConfigureProjectile();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"[StageSceneSetup] Training stage installed in '{scene.path}'.");
    }

    private static Scene EnsureSceneOpen()
    {
        Scene active = SceneManager.GetActiveScene();
        return active.path == ScenePath ? active : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    // ------------------------------------------------------------------ materials

    /// <summary>
    /// Vendor materials ship on built-in shaders that URP renders magenta. Each is upgraded in place:
    /// Standard surfaces to URP/Lit with their albedo, normal map and gloss carried over, the additive
    /// grid glow to the URP particle shader with an additive blend state, and the fighter materials
    /// (kept for the library characters) to URP/Lit. The skybox already uses a URP-supported shader.
    /// </summary>
    private static void ConvertVendorMaterials()
    {
        ConvertStandardToLit(GridMaterialPath);
        ConvertAdditiveToParticles(GridGlowMaterialPath);

        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { FighterPackRoot }))
        {
            ConvertStandardToLit(AssetDatabase.GUIDToAssetPath(guid));
        }
    }

    private static void ConvertStandardToLit(string path)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader lit = Shader.Find(LitShaderName);
        if (material == null || material.shader == lit) return;

        Texture albedo = material.HasProperty(MainTexId) ? material.GetTexture(MainTexId) : null;
        Vector2 tiling = material.HasProperty(MainTexId) ? material.GetTextureScale(MainTexId) : Vector2.one;
        Texture normal = material.HasProperty(BumpMapId) ? material.GetTexture(BumpMapId) : null;
        float smoothness = material.HasProperty(GlossinessId) ? material.GetFloat(GlossinessId) : 0.5f;
        float metallic = material.HasProperty(MetallicId) ? material.GetFloat(MetallicId) : 0f;
        Color tint = material.HasProperty(ColorId) ? material.GetColor(ColorId) : Color.white;

        material.shader = lit;
        material.SetTexture(BaseMapId, albedo);
        material.SetTextureScale(BaseMapId, tiling);
        material.SetColor(BaseColorId, tint);
        material.SetFloat(SmoothnessId, smoothness);
        material.SetFloat(MetallicId, metallic);
        material.SetTexture(BumpMapId, normal);
        if (normal != null) material.EnableKeyword(NormalMapKeyword);
        else material.DisableKeyword(NormalMapKeyword);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
    }

    private static void ConvertAdditiveToParticles(string path)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader particles = Shader.Find(ParticleShaderName);
        if (material == null || material.shader == particles) return;

        Texture albedo = material.HasProperty(MainTexId) ? material.GetTexture(MainTexId) : null;
        Vector2 tiling = material.HasProperty(MainTexId) ? material.GetTextureScale(MainTexId) : Vector2.one;
        Color tint = material.HasProperty(TintColorId) ? material.GetColor(TintColorId) : Color.white;

        material.shader = particles;
        material.SetTexture(BaseMapId, albedo);
        material.SetTextureScale(BaseMapId, tiling);
        material.SetColor(BaseColorId, tint);
        ApplyAdditiveBlend(material);
        EditorUtility.SetDirty(material);
    }

    /// <summary>
    /// Writes the transparent additive blend state directly so the material needs no inspector pass.
    /// </summary>
    private static void ApplyAdditiveBlend(Material material)
    {
        material.SetFloat(SurfaceId, 1f);
        material.SetFloat(BlendId, 2f);
        material.SetFloat(SrcBlendId, (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat(DstBlendId, (float)UnityEngine.Rendering.BlendMode.One);
        material.SetFloat(ZWriteId, 0f);
        material.EnableKeyword(TransparentSurfaceKeyword);
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    private static Material CreateDustMaterial()
    {
        Material material = LoadOrCreateMaterial("M_DustMote", ParticleShaderName);
        material.SetTexture(BaseMapId, AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexturesRoot}/dust_mote.png"));
        material.SetColor(BaseColorId, new Color(1f, 0.92f, 0.8f, 0.35f));
        ApplyAdditiveBlend(material);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material CreateEnergyMaterial()
    {
        Material material = LoadOrCreateMaterial("M_HadoukenEnergy", UnlitShaderName);
        material.SetColor(BaseColorId, EnergyColor * 4f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material LoadOrCreateMaterial(string name, string shaderName)
    {
        if (!AssetDatabase.IsValidFolder(MaterialsRoot))
        {
            Directory.CreateDirectory(MaterialsRoot);
            AssetDatabase.Refresh();
        }

        string path = $"{MaterialsRoot}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find(shaderName);

        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
        }

        material.enableInstancing = true;
        return material;
    }

    // ------------------------------------------------------------------ stage

    private static Transform RebuildStageRoot()
    {
        DestroyIfPresent(StageRootName);
        DestroyIfPresent(LegacyArenaRootName);

        Transform root = new GameObject(StageRootName).transform;
        root.SetParent(SceneHierarchyOrganizer.GetOrCreateCategory(SceneHierarchyOrganizer.EnvironmentCategory), false);
        return root;
    }

    private static void DestroyIfPresent(string name)
    {
        GameObject existing = GameObject.Find(name);
        if (existing != null) Object.DestroyImmediate(existing);
    }

    /// <summary>
    /// The vendor stage is authored for a camera on its +Z side, so it is instantiated turned around
    /// and then translated from its rendered bounds: floor top at ground height, front edge at
    /// <see cref="StageFrontZ"/>, wall behind the fighters. Its own mesh collider is disabled so the
    /// gameplay floor collider stays the only ground the fighters and raycasts see.
    /// </summary>
    private static void PlaceStagePrefab(Transform root)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StagePrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[StageSceneSetup] Stage prefab not found at '{StagePrefabPath}'.");
            return;
        }

        GameObject stage = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
        stage.name = "TrainingStage";
        stage.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        stage.transform.localPosition = Vector3.zero;

        Renderer[] renderers = stage.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            stage.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, StageFrontZ - bounds.min.z);
        }

        foreach (Collider collider in stage.GetComponentsInChildren<Collider>())
        {
            collider.enabled = false;
        }

        foreach (Renderer renderer in renderers)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.gameObject.isStatic = true;
        }
    }

    /// <summary>
    /// A wide grid plane just under the stage floor carries the same material out to the edges of
    /// the camera range, so the bottom of the frame always lands on floor while the sky stays above
    /// the wall.
    /// </summary>
    private static void BuildFloorExtension(Transform root, Material gridMaterial)
    {
        GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.name = FloorExtensionName;
        plane.transform.SetParent(root, false);
        plane.transform.localPosition = new Vector3(0f, -FloorExtensionDrop, 0f);
        plane.transform.localScale = new Vector3(FloorExtensionHalfSize * 0.2f, 1f, FloorExtensionHalfSize * 0.2f);
        plane.isStatic = true;
        Object.DestroyImmediate(plane.GetComponent<Collider>());

        MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = gridMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = true;
    }

    /// <summary>
    /// The boundary walls move to the visible edge of the stage so a cornered fighter is pinned on
    /// grid, not in the sky. Depth, height and layers are untouched.
    /// </summary>
    private static void PlaceBoundaryWalls()
    {
        MoveWall(WallEastName, BoundaryWallX);
        MoveWall(WallWestName, -BoundaryWallX);
    }

    private static void MoveWall(string name, float x)
    {
        GameObject wall = GameObject.Find(name);
        if (wall == null)
        {
            Debug.LogError($"[StageSceneSetup] Boundary wall '{name}' not found in the scene.");
            return;
        }

        Vector3 position = wall.transform.position;
        position.x = x;
        wall.transform.position = position;
        EditorUtility.SetDirty(wall);
    }

    /// <summary>
    /// The gameplay floor and boundary walls exist for collision only: every visible surface comes
    /// from the vendor stage, so their renderers are switched off. They keep the grid material so no
    /// renderer in the scene points at a material that no longer exists.
    /// </summary>
    private static void HideCollisionGeometry(Material gridMaterial)
    {
        foreach (MeshRenderer renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            GameObject target = renderer.gameObject;
            if (target.GetComponentInParent<RobotController>() != null) continue;
            if (target.GetComponent<HadoukenProjectile>() != null) continue;
            if (SceneHierarchyOrganizer.IsUnderDecor(target.transform, StageRootName)) continue;
            if (!target.TryGetComponent(out Collider _)) continue;

            renderer.sharedMaterial = gridMaterial;
            renderer.enabled = false;
            EditorUtility.SetDirty(renderer);
        }
    }

    /// <summary>
    /// Dust drifting through the light: a single world-space emitter of small additive discs with
    /// slow noise, prewarmed so the stage is alive from the first frame. Simulated natively, so it
    /// adds nothing to the managed heap.
    /// </summary>
    private static void BuildDustMotes(Transform root, Material dustMaterial)
    {
        GameObject motes = new GameObject(DustMotesName);
        motes.transform.SetParent(root, false);
        motes.transform.position = new Vector3(0f, 4f, 0f);

        ParticleSystem system = motes.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.prewarm = true;
        main.playOnAwake = true;
        main.maxParticles = DustMoteCount;
        main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 11f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.08f);
        main.startColor = new Color(1f, 0.95f, 0.85f, 1f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -0.003f;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = true;
        emission.rateOverTime = DustMoteCount / 8.5f;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(22f, 8f, 10f);

        ParticleSystem.NoiseModule noise = system.noise;
        noise.enabled = true;
        noise.strength = 0.14f;
        noise.frequency = 0.18f;
        noise.scrollSpeed = 0.08f;
        noise.damping = true;
        noise.quality = ParticleSystemNoiseQuality.Medium;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = system.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = fade;

        ParticleSystemRenderer renderer = motes.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = dustMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // ------------------------------------------------------------------ lighting

    /// <summary>
    /// The shipped HDRI drives the sky, ambient and reflections. A warm key with soft shadows and a
    /// cool shadowless fill shape the fighters against the neutral grid; no fog, since the stage is
    /// open to the sky by design.
    /// </summary>
    private static void ConfigureLighting()
    {
        Material skybox = AssetDatabase.LoadAssetAtPath<Material>(SkyboxMaterialPath);
        if (skybox == null) Debug.LogError($"[StageSceneSetup] Skybox material not found at '{SkyboxMaterialPath}'.");

        Light key = FindOrCreateDirectional(KeyLightName);
        key.transform.rotation = Quaternion.Euler(48f, -28f, 0f);
        key.color = new Color(1f, 0.93f, 0.85f);
        key.intensity = 1.7f;
        key.shadows = LightShadows.Soft;
        key.shadowStrength = 0.85f;
        key.shadowBias = 0.025f;
        key.shadowNormalBias = 0.45f;

        Light fill = FindOrCreateDirectional(FillLightName);
        fill.transform.rotation = Quaternion.Euler(30f, 60f, 0f);
        fill.color = new Color(0.55f, 0.65f, 0.85f);
        fill.intensity = 0.4f;
        fill.shadows = LightShadows.None;

        Light rim = FindOrCreateDirectional(RimLightName);
        rim.transform.rotation = Quaternion.Euler(20f, 165f, 0f);
        rim.color = new Color(1f, 0.95f, 0.9f);
        rim.intensity = 0.6f;
        rim.shadows = LightShadows.None;

        RenderSettings.sun = key;
        RenderSettings.skybox = skybox;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.reflectionIntensity = 0.7f;
        RenderSettings.fog = false;
        DynamicGI.UpdateEnvironment();
    }

    private static Light FindOrCreateDirectional(string name)
    {
        GameObject existing = GameObject.Find(name);
        Light light = existing != null ? existing.GetComponent<Light>() : null;

        if (light == null)
        {
            light = new GameObject(name).AddComponent<Light>();
            light.transform.SetParent(SceneHierarchyOrganizer.GetOrCreateCategory(SceneHierarchyOrganizer.LightingCategory), false);
        }

        light.type = LightType.Directional;
        return light;
    }

    // ------------------------------------------------------------------ post-processing & camera

    private static void ConfigurePostProcessing()
    {
        VolumeProfile profile = LoadOrCreateVolumeProfile();

        Tonemapping tonemapping = GetOrAddOverride<Tonemapping>(profile);
        tonemapping.mode.Override(TonemappingMode.ACES);

        Bloom bloom = GetOrAddOverride<Bloom>(profile);
        bloom.threshold.Override(1f);
        bloom.intensity.Override(0.5f);
        bloom.scatter.Override(0.6f);

        Vignette vignette = GetOrAddOverride<Vignette>(profile);
        vignette.intensity.Override(0.28f);
        vignette.smoothness.Override(0.45f);

        ColorAdjustments color = GetOrAddOverride<ColorAdjustments>(profile);
        color.postExposure.Override(0f);
        color.contrast.Override(12f);
        color.saturation.Override(0f);

        WhiteBalance whiteBalance = GetOrAddOverride<WhiteBalance>(profile);
        whiteBalance.temperature.Override(0f);
        whiteBalance.tint.Override(0f);

        FilmGrain grain = GetOrAddOverride<FilmGrain>(profile);
        grain.type.Override(FilmGrainLookup.Thin1);
        grain.intensity.Override(0.08f);
        grain.response.Override(0.8f);

        EditorUtility.SetDirty(profile);

        GameObject volumeObject = GameObject.Find(GlobalVolumeName);
        if (volumeObject == null)
        {
            volumeObject = new GameObject(GlobalVolumeName);
            volumeObject.transform.SetParent(SceneHierarchyOrganizer.GetOrCreateCategory(SceneHierarchyOrganizer.LightingCategory), false);
        }

        Volume volume = volumeObject.TryGetComponent(out Volume existingVolume) ? existingVolume : volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 0f;
        volume.sharedProfile = profile;
    }

    /// <summary>
    /// The sky shows above the wall by design, so the camera clears to the skybox; the horizontal
    /// clamp keeps the framing inside the visible grid.
    /// </summary>
    private static void ConfigureCameraFraming()
    {
        Camera camera = Camera.main;
        if (camera != null)
        {
            camera.clearFlags = CameraClearFlags.Skybox;

            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.antialiasingQuality = AntialiasingQuality.High;
            cameraData.renderShadows = true;
        }

        FightingCamera fightingCamera = Object.FindFirstObjectByType<FightingCamera>();
        if (fightingCamera == null) return;

        fightingCamera.leftBound = -CameraSideBound;
        fightingCamera.rightBound = CameraSideBound;
        EditorUtility.SetDirty(fightingCamera);
    }

    private static VolumeProfile LoadOrCreateVolumeProfile()
    {
        if (!AssetDatabase.IsValidFolder(RenderingRoot))
        {
            Directory.CreateDirectory(RenderingRoot);
            AssetDatabase.Refresh();
        }

        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        if (profile != null) return profile;

        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, VolumeProfilePath);
        return profile;
    }

    private static T GetOrAddOverride<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet(out T component)) return component;

        component = profile.Add<T>(true);
        AssetDatabase.AddObjectToAsset(component, profile);
        return component;
    }

    // ------------------------------------------------------------------ projectile

    private static void ConfigureProjectile()
    {
        Material energyMaterial = CreateEnergyMaterial();
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(ProjectilePrefabPath);
        if (prefabRoot == null) return;

        if (prefabRoot.TryGetComponent(out MeshRenderer renderer))
        {
            renderer.sharedMaterial = energyMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        Transform glow = prefabRoot.transform.Find("Glow");
        if (glow == null)
        {
            glow = new GameObject("Glow").transform;
            glow.SetParent(prefabRoot.transform, false);
        }

        Light light = glow.TryGetComponent(out Light existingLight) ? existingLight : glow.gameObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = EnergyColor;
        light.intensity = 3f;
        light.range = 5f;
        light.shadows = LightShadows.None;

        PrefabUtility.SaveAsPrefabAsset(prefabRoot, ProjectilePrefabPath);
        PrefabUtility.UnloadPrefabContents(prefabRoot);
    }
}
