using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Dresses the training stage as a cyber training room: a polished composite floor with an
/// emissive grid, a neon ring boundary, panelled walls with minimalist pillars and light strips,
/// a cool three-point light rig, a global post-processing volume and an emissive projectile.
/// The decor is rebuilt from scratch under a single root on every run, while the gameplay objects
/// (floor collider, boundary walls, fighters, managers) are only re-dressed, never replaced.
/// </summary>
public static class ArenaSceneSetup
{
    private const string ScenePath = FighterSceneSetup.ScenePath;
    private const string TexturesRoot = "Assets/_Project/Art/Environment/Textures";
    private const string MaterialsRoot = "Assets/_Project/Art/Materials";
    private const string RenderingRoot = "Assets/_Project/Art/Rendering";
    private const string VolumeProfilePath = RenderingRoot + "/ArenaPostProcess.asset";
    private const string ProjectilePrefabPath = "Assets/_Project/Prefabs/Sphere.prefab";
    private const string LitShaderName = "Universal Render Pipeline/Lit";
    private const string UnlitShaderName = "Universal Render Pipeline/Unlit";
    private const string EmissionKeyword = "_EMISSION";
    private const int MaxTextureSize = 1024;
    private const int TextureAnisoLevel = 8;

    private const string ArenaRootName = "Arena";
    private const string KeyLightName = "Directional Light";
    private const string FillLightName = "Fill Light";
    private const string RimLightName = "Rim Light";
    private const string GlobalVolumeName = "Global Volume";
    private const string OuterFloorName = "Outer Floor";

    private const float BackDecorZ = 4.6f;
    private const float PillarSpacing = 5.5f;
    private const int PillarCount = 7;
    private const float PillarHeight = 7f;
    private const float RingHalfLength = 11f;
    private const float RingHalfWidth = 4.5f;
    private const float StripThickness = 0.06f;
    private const float PlatformFrameWidth = 0.55f;
    private const float BackSlabZ = 7.2f;
    private const float BackSlabHeight = 6.4f;
    private const float SideColumnX = 14.5f;

    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
    private static readonly int MetallicId = Shader.PropertyToID("_Metallic");

    private static readonly Color AccentColor = new Color(0.25f, 0.85f, 1f);
    private static readonly Color EnergyColor = new Color(0.35f, 0.8f, 1f);

    private struct ArenaMaterials
    {
        public Material Floor;
        public Material Panel;
        public Material Pillar;
        public Material Accent;
        public Material Energy;
    }

    [MenuItem("Fighting Game/Build Training Room", priority = 40)]
    public static void BuildTrainingRoom()
    {
        Scene scene = EnsureSceneOpen();

        ConfigureTextureImports();
        ArenaMaterials materials = CreateMaterials();

        DressStageGeometry(materials);
        BuildArenaDecor(materials);
        ConfigureLighting();
        ConfigurePostProcessing();
        ConfigureProjectile(materials.Energy);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ArenaSceneSetup] Training room built in '{scene.path}'.");
    }

    private static Scene EnsureSceneOpen()
    {
        Scene active = SceneManager.GetActiveScene();
        return active.path == ScenePath ? active : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    // ------------------------------------------------------------------ assets

    /// <summary>
    /// Tiling surfaces are clamped to 1024 and block-compressed at high quality (BC7 on desktop),
    /// with mipmaps so the floor grid stays stable at the far end of the shadow range.
    /// </summary>
    private static void ConfigureTextureImports()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexturesRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.anisoLevel = TextureAnisoLevel;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = false;
            importer.maxTextureSize = MaxTextureSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.crunchedCompression = false;
            importer.SaveAndReimport();
        }
    }

    private static ArenaMaterials CreateMaterials()
    {
        EnsureFolder(MaterialsRoot);

        return new ArenaMaterials
        {
            Floor = CreateLitMaterial("M_TechFloor", "tech_floor", new Vector2(6f, 2f), Color.white, 0.55f, 0.2f, AccentColor * 0.9f),
            Panel = CreateLitMaterial("M_TechPanel", "tech_panel", new Vector2(5f, 1f), Color.white, 0.45f, 0.5f, AccentColor * 1.4f),
            Pillar = CreateLitMaterial("M_TechPillar", null, Vector2.one, new Color(0.1f, 0.11f, 0.14f), 0.6f, 0.7f, Color.black),
            Accent = CreateEmissiveMaterial("M_NeonAccent", AccentColor * 3.2f),
            Energy = CreateEmissiveMaterial("M_HadoukenEnergy", EnergyColor * 4f)
        };
    }

    private static Material CreateLitMaterial(string name, string textureBaseName, Vector2 tiling, Color tint, float smoothness, float metallic, Color emission)
    {
        Material material = LoadOrCreateMaterial(name);
        Texture2D albedo = textureBaseName != null ? LoadTexture(textureBaseName + "_albedo.png") : null;
        Texture2D emissionMap = textureBaseName != null ? LoadTexture(textureBaseName + "_emission.png") : null;

        material.SetTexture(BaseMapId, albedo);
        material.SetTextureScale(BaseMapId, tiling);
        material.SetColor(BaseColorId, tint);
        material.SetFloat(SmoothnessId, smoothness);
        material.SetFloat(MetallicId, metallic);

        material.SetTexture(EmissionMapId, emissionMap);
        material.SetColor(EmissionColorId, emissionMap != null ? emission : Color.black);
        if (emissionMap != null)
        {
            material.EnableKeyword(EmissionKeyword);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            material.DisableKeyword(EmissionKeyword);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Self-lit surfaces (neon strips, energy) use the unlit shader with an HDR colour above 1.0 so
    /// the bloom pass picks them up and the tone mapper keeps their hue instead of a lit response.
    /// </summary>
    private static Material CreateEmissiveMaterial(string name, Color hdrColor)
    {
        Material material = LoadOrCreateMaterial(name, UnlitShaderName);
        material.SetColor(BaseColorId, hdrColor);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material LoadOrCreateMaterial(string name, string shaderName = LitShaderName)
    {
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

    private static Texture2D LoadTexture(string fileName)
    {
        return AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexturesRoot}/{fileName}");
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        Directory.CreateDirectory(path);
        AssetDatabase.Refresh();
    }

    // ------------------------------------------------------------------ stage

    /// <summary>
    /// Re-skins the existing floor and boundary cubes. The boundary nearest the camera keeps its
    /// collider for wall bounces but stops rendering so it never blocks the view.
    /// </summary>
    private static void DressStageGeometry(ArenaMaterials materials)
    {
        MeshRenderer[] renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        MeshRenderer frontWall = null;

        foreach (MeshRenderer renderer in renderers)
        {
            GameObject target = renderer.gameObject;
            if (target.GetComponentInParent<RobotController>() != null) continue;
            if (target.transform.root.name == ArenaRootName) continue;

            if (target.TryGetComponent(out MeshCollider _))
            {
                renderer.sharedMaterial = materials.Floor;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                continue;
            }

            if (!target.TryGetComponent(out BoxCollider _)) continue;

            renderer.sharedMaterial = materials.Panel;
            renderer.enabled = true;
            if (frontWall == null || target.transform.position.z < frontWall.transform.position.z) frontWall = renderer;
        }

        if (frontWall != null) frontWall.enabled = false;
    }

    private static void BuildArenaDecor(ArenaMaterials materials)
    {
        GameObject existing = GameObject.Find(ArenaRootName);
        if (existing != null) Object.DestroyImmediate(existing);

        Transform root = new GameObject(ArenaRootName).transform;

        GameObject outerFloor = CreatePrimitive(PrimitiveType.Plane, OuterFloorName, root, new Vector3(0f, -0.02f, 0f), Vector3.zero, new Vector3(8f, 1f, 6f), materials.Pillar);
        Object.DestroyImmediate(outerFloor.GetComponent<Collider>());

        BuildPlatformFrame(root, materials.Pillar);
        BuildRingBoundary(root, materials.Accent);
        BuildBackWallDecor(root, materials);
        BuildDepthLayers(root, materials);
        ConfigureDecorShadows(root, materials.Accent);
    }

    /// <summary>
    /// Self-lit strips and the ground plane contribute nothing to the shadow map, so they are
    /// excluded from every cascade pass; unlit surfaces cannot receive shadows either.
    /// </summary>
    private static void ConfigureDecorShadows(Transform root, Material selfLit)
    {
        foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>())
        {
            bool isSelfLit = renderer.sharedMaterial == selfLit;
            bool isFloor = renderer.name == OuterFloorName;

            renderer.shadowCastingMode = isSelfLit || isFloor ? ShadowCastingMode.Off : ShadowCastingMode.On;
            renderer.receiveShadows = !isSelfLit;
        }
    }

    /// <summary>
    /// Flat metal frame just outside the neon lines, like a tatami border, so the platform reads as
    /// a raised training mat against the polished floor.
    /// </summary>
    private static void BuildPlatformFrame(Transform root, Material material)
    {
        const float lift = 0.02f;
        float outerLength = (RingHalfLength + PlatformFrameWidth) * 2f;
        float outerWidth = (RingHalfWidth + PlatformFrameWidth) * 2f;
        float zOffset = RingHalfWidth + PlatformFrameWidth * 0.5f;
        float xOffset = RingHalfLength + PlatformFrameWidth * 0.5f;

        CreatePrimitive(PrimitiveType.Cube, "Frame North", root, new Vector3(0f, lift, zOffset), Vector3.zero, new Vector3(outerLength, lift * 2f, PlatformFrameWidth), material);
        CreatePrimitive(PrimitiveType.Cube, "Frame South", root, new Vector3(0f, lift, -zOffset), Vector3.zero, new Vector3(outerLength, lift * 2f, PlatformFrameWidth), material);
        CreatePrimitive(PrimitiveType.Cube, "Frame East", root, new Vector3(xOffset, lift, 0f), Vector3.zero, new Vector3(PlatformFrameWidth, lift * 2f, outerWidth), material);
        CreatePrimitive(PrimitiveType.Cube, "Frame West", root, new Vector3(-xOffset, lift, 0f), Vector3.zero, new Vector3(PlatformFrameWidth, lift * 2f, outerWidth), material);
    }

    /// <summary>
    /// Mid and far layers behind the pillars: staggered wall slabs with a light slot, a low service
    /// ledge and tall side columns, so the parallax from the tracking camera reads as real depth.
    /// </summary>
    private static void BuildDepthLayers(Transform root, ArenaMaterials materials)
    {
        float firstPillarX = -PillarSpacing * (PillarCount - 1) * 0.5f;

        for (int i = 0; i < PillarCount - 1; i++)
        {
            float x = firstPillarX + PillarSpacing * (i + 0.5f);
            float height = i % 2 == 0 ? BackSlabHeight : BackSlabHeight * 0.8f;
            CreatePrimitive(PrimitiveType.Cube, "Back Slab", root, new Vector3(x, height * 0.5f, BackSlabZ), Vector3.zero, new Vector3(PillarSpacing - 1.4f, height, 0.5f), materials.Panel);
            CreatePrimitive(PrimitiveType.Cube, "Slab Slot", root, new Vector3(x, height - 0.5f, BackSlabZ - 0.27f), Vector3.zero, new Vector3(PillarSpacing - 2.4f, 0.05f, 0.02f), materials.Accent);
        }

        float ledgeSpan = PillarSpacing * (PillarCount - 1) + 1.2f;
        CreatePrimitive(PrimitiveType.Cube, "Service Ledge", root, new Vector3(0f, 0.3f, BackDecorZ - 1.05f), Vector3.zero, new Vector3(ledgeSpan, 0.6f, 0.7f), materials.Pillar);
        CreatePrimitive(PrimitiveType.Cube, "Ledge Strip", root, new Vector3(0f, 0.6f, BackDecorZ - 1.41f), Vector3.zero, new Vector3(ledgeSpan, 0.03f, 0.03f), materials.Accent);

        for (int x = -1; x <= 1; x += 2)
        {
            CreatePrimitive(PrimitiveType.Cube, "Side Column", root, new Vector3(x * SideColumnX, 3f, 2.2f), Vector3.zero, new Vector3(0.8f, 6f, 0.8f), materials.Pillar);
            CreatePrimitive(PrimitiveType.Cube, "Side Column Strip", root, new Vector3(x * SideColumnX, 3f, 1.78f), Vector3.zero, new Vector3(0.05f, 5.4f, 0.02f), materials.Accent);
        }
    }

    /// <summary>
    /// Flush neon strips outline the playable ring so the boundary reads instantly on camera without
    /// a physical lip the fighters could visually clip through.
    /// </summary>
    private static void BuildRingBoundary(Transform root, Material accent)
    {
        const float width = 0.12f;

        CreatePrimitive(PrimitiveType.Cube, "Ring Line North", root, new Vector3(0f, StripThickness * 0.5f, RingHalfWidth), Vector3.zero, new Vector3(RingHalfLength * 2f, StripThickness, width), accent);
        CreatePrimitive(PrimitiveType.Cube, "Ring Line South", root, new Vector3(0f, StripThickness * 0.5f, -RingHalfWidth), Vector3.zero, new Vector3(RingHalfLength * 2f, StripThickness, width), accent);
        CreatePrimitive(PrimitiveType.Cube, "Ring Line East", root, new Vector3(RingHalfLength, StripThickness * 0.5f, 0f), Vector3.zero, new Vector3(width, StripThickness, RingHalfWidth * 2f), accent);
        CreatePrimitive(PrimitiveType.Cube, "Ring Line West", root, new Vector3(-RingHalfLength, StripThickness * 0.5f, 0f), Vector3.zero, new Vector3(width, StripThickness, RingHalfWidth * 2f), accent);

        for (int x = -1; x <= 1; x += 2)
        {
            for (int z = -1; z <= 1; z += 2)
            {
                CreatePrimitive(PrimitiveType.Cube, "Corner Marker", root, new Vector3(x * RingHalfLength, 0.5f, z * RingHalfWidth), Vector3.zero, new Vector3(0.18f, 1f, 0.18f), accent);
            }
        }
    }

    private static void BuildBackWallDecor(Transform root, ArenaMaterials materials)
    {
        float firstPillarX = -PillarSpacing * (PillarCount - 1) * 0.5f;
        float span = PillarSpacing * (PillarCount - 1) + 1.2f;

        CreatePrimitive(PrimitiveType.Cube, "Ceiling Beam", root, new Vector3(0f, PillarHeight + 0.25f, BackDecorZ), Vector3.zero, new Vector3(span, 0.5f, 0.6f), materials.Pillar);
        CreatePrimitive(PrimitiveType.Cube, "Ceiling Light Bar", root, new Vector3(0f, PillarHeight, BackDecorZ - 0.32f), Vector3.zero, new Vector3(span, 0.08f, 0.08f), materials.Accent);
        CreatePrimitive(PrimitiveType.Cube, "Skirting Light Bar", root, new Vector3(0f, 0.12f, BackDecorZ - 0.32f), Vector3.zero, new Vector3(span, 0.06f, 0.06f), materials.Accent);

        for (int i = 0; i < PillarCount; i++)
        {
            float x = firstPillarX + i * PillarSpacing;
            CreatePrimitive(PrimitiveType.Cube, "Pillar", root, new Vector3(x, PillarHeight * 0.5f, BackDecorZ), Vector3.zero, new Vector3(0.6f, PillarHeight, 0.6f), materials.Pillar);
            CreatePrimitive(PrimitiveType.Cube, "Pillar Strip", root, new Vector3(x, PillarHeight * 0.5f, BackDecorZ - 0.31f), Vector3.zero, new Vector3(0.05f, PillarHeight - 0.6f, 0.02f), materials.Accent);

            if (i % 2 == 1)
            {
                BuildAccentLight(root, new Vector3(x, PillarHeight * 0.55f, BackDecorZ - 1.2f));
            }
        }
    }

    private static void BuildAccentLight(Transform root, Vector3 position)
    {
        GameObject lightObject = new GameObject("Accent Light");
        lightObject.transform.SetParent(root, false);
        lightObject.transform.position = position;

        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = AccentColor;
        light.intensity = 0.9f;
        light.range = 8f;
        light.shadows = LightShadows.None;
    }

    private static GameObject CreatePrimitive(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 eulerAngles, Vector3 scale, Material material)
    {
        GameObject primitive = GameObject.CreatePrimitive(type);
        primitive.name = name;
        primitive.transform.SetParent(parent, false);
        primitive.transform.localPosition = position;
        primitive.transform.localEulerAngles = eulerAngles;
        primitive.transform.localScale = scale;
        primitive.isStatic = true;

        if (material != null) primitive.GetComponent<MeshRenderer>().sharedMaterial = material;

        Collider collider = primitive.GetComponent<Collider>();
        if (collider != null && type != PrimitiveType.Plane) Object.DestroyImmediate(collider);

        return primitive;
    }

    // ------------------------------------------------------------------ lighting

    private static void ConfigureLighting()
    {
        Light key = FindOrCreateDirectional(KeyLightName);
        key.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
        key.color = new Color(0.9f, 0.95f, 1f);
        key.intensity = 1.9f;
        key.shadows = LightShadows.Soft;
        key.shadowStrength = 0.92f;
        key.shadowBias = 0.03f;
        key.shadowNormalBias = 0.5f;

        Light fill = FindOrCreateDirectional(FillLightName);
        fill.transform.rotation = Quaternion.Euler(30f, 50f, 0f);
        fill.color = new Color(0.45f, 0.7f, 1f);
        fill.intensity = 0.5f;
        fill.shadows = LightShadows.None;

        Light rim = FindOrCreateDirectional(RimLightName);
        rim.transform.rotation = Quaternion.Euler(22f, 168f, 0f);
        rim.color = new Color(0.55f, 0.9f, 1f);
        rim.intensity = 1.9f;
        rim.shadows = LightShadows.None;

        RenderSettings.sun = key;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.22f, 0.3f, 0.42f);
        RenderSettings.ambientEquatorColor = new Color(0.14f, 0.17f, 0.22f);
        RenderSettings.ambientGroundColor = new Color(0.04f, 0.05f, 0.07f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.02f, 0.03f, 0.05f);
        RenderSettings.fogStartDistance = 28f;
        RenderSettings.fogEndDistance = 70f;
    }

    private static Light FindOrCreateDirectional(string name)
    {
        GameObject existing = GameObject.Find(name);
        Light light = existing != null ? existing.GetComponent<Light>() : null;

        if (light == null)
        {
            light = new GameObject(name).AddComponent<Light>();
        }

        light.type = LightType.Directional;
        return light;
    }

    // ------------------------------------------------------------------ post-processing

    private static void ConfigurePostProcessing()
    {
        VolumeProfile profile = LoadOrCreateVolumeProfile();

        Tonemapping tonemapping = GetOrAddOverride<Tonemapping>(profile);
        tonemapping.mode.Override(TonemappingMode.ACES);

        Bloom bloom = GetOrAddOverride<Bloom>(profile);
        bloom.threshold.Override(1f);
        bloom.intensity.Override(0.6f);
        bloom.scatter.Override(0.6f);

        Vignette vignette = GetOrAddOverride<Vignette>(profile);
        vignette.intensity.Override(0.25f);
        vignette.smoothness.Override(0.4f);

        ColorAdjustments color = GetOrAddOverride<ColorAdjustments>(profile);
        color.postExposure.Override(0.1f);
        color.contrast.Override(10f);
        color.saturation.Override(5f);

        EditorUtility.SetDirty(profile);

        GameObject volumeObject = GameObject.Find(GlobalVolumeName) ?? new GameObject(GlobalVolumeName);
        Volume volume = volumeObject.TryGetComponent(out Volume existingVolume) ? existingVolume : volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 0f;
        volume.sharedProfile = profile;

        Camera camera = Camera.main;
        if (camera == null) return;

        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.02f, 0.03f, 0.05f);

        UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = true;
        cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        cameraData.antialiasingQuality = AntialiasingQuality.High;
        cameraData.renderShadows = true;
    }

    private static VolumeProfile LoadOrCreateVolumeProfile()
    {
        EnsureFolder(RenderingRoot);

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

    private static void ConfigureProjectile(Material energyMaterial)
    {
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
