using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Dresses the training stage as a concrete and timber training hall: a matte poured floor with a
/// rubber mat, plaster walls that rise sixteen metres into shadow, a timber canopy with warm
/// recessed downlights, a warm-key / cool-fill light rig, close atmospheric fog, drifting dust and
/// a global post-processing volume. Every surface texture is generated here as a tileable noise
/// image so the look needs no external art. The decor is rebuilt from scratch under a single root
/// on every run, while the gameplay objects (floor collider, boundary walls, fighters, managers)
/// are only re-dressed, never replaced. The enclosure height and the camera framing are tuned
/// together so the top of the frame never leaves the architecture, even on a full juggle.
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
    private const string ParticleShaderName = "Universal Render Pipeline/Particles/Unlit";
    private const string EmissionKeyword = "_EMISSION";
    private const string TransparentSurfaceKeyword = "_SURFACE_TYPE_TRANSPARENT";
    private const int SurfaceTextureSize = 1024;
    private const int MoteTextureSize = 64;
    private const int TextureAnisoLevel = 8;

    public const string ArenaRootName = "Arena";
    public const string KeyLightName = "Key Light";
    public const string DustMotesName = "Dust Motes";

    private const string FillLightName = "Fill Light";
    private const string RimLightName = "Rim Light";
    private const string GlobalVolumeName = "Global Volume";
    private const string OuterFloorName = "Outer Floor";
    private const string CanopyName = "Canopy";
    private const string RafterName = "Rafter";

    private const float BackDecorZ = 4.6f;
    private const float PillarSpacing = 5.5f;
    private const int PillarCount = 7;
    private const float PillarHeight = 11.5f;
    private const float MatHalfLength = 11f;
    private const float MatHalfWidth = 4.5f;
    private const float PlatformFrameWidth = 0.55f;
    private const float BackSlabZ = 7.2f;
    private const float LowerSlabHeight = 6.5f;
    private const float EnclosureHeight = 16f;
    private const float CanopyHeight = 11.5f;
    private const float CanopyDepth = 15f;
    private const float SideColumnX = 14.5f;
    private const int DownlightPairs = 2;
    private const int DustMoteCount = 180;

    private const float CameraPitch = 7f;
    private const float CameraMaxElevation = 2f;
    private const float CameraMaxZoomDistance = -18f;

    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
    private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
    private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
    private static readonly int BlendId = Shader.PropertyToID("_Blend");
    private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

    private static readonly Color WarmAccentColor = new Color(1f, 0.72f, 0.45f);
    private static readonly Color EnergyColor = new Color(0.35f, 0.8f, 1f);
    private static readonly Color FogColor = new Color(0.1f, 0.086f, 0.07f);
    private static readonly Color DownlightColor = new Color(1f, 0.84f, 0.64f);

    private struct ArenaMaterials
    {
        public Material Floor;
        public Material Mat;
        public Material Wall;
        public Material WallUpper;
        public Material Timber;
        public Material Accent;
        public Material Energy;
        public Material Dust;
    }

    [MenuItem("Fighting Game/Build Training Room", priority = 40)]
    public static void BuildTrainingRoom()
    {
        Scene scene = EnsureSceneOpen();

        GenerateSurfaceTextures();
        ConfigureTextureImports();
        ArenaMaterials materials = CreateMaterials();

        DressStageGeometry(materials);
        BuildArenaDecor(materials);
        ConfigureLighting();
        ConfigurePostProcessing();
        ConfigureCameraFraming();
        ConfigureProjectile(materials.Energy);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ArenaSceneSetup] Training hall built in '{scene.path}'.");
    }

    private static Scene EnsureSceneOpen()
    {
        Scene active = SceneManager.GetActiveScene();
        return active.path == ScenePath ? active : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    // ------------------------------------------------------------------ textures

    /// <summary>
    /// Tileable surface albedos from periodic value noise: poured concrete with fine speckle, warm
    /// plaster with soft mottling, dark timber with a warped grain, and a soft disc for dust motes.
    /// Existing files are regenerated so the look stays reproducible from code alone.
    /// </summary>
    private static void GenerateSurfaceTextures()
    {
        EnsureFolder(TexturesRoot);

        WriteTexture("concrete_albedo", SurfaceTextureSize, (u, v) =>
        {
            float mottle = FractalNoise(u, v, 5, 4);
            float speckle = ValueNoise(u * 96f, v * 96f, 96);
            float shade = 0.82f + 0.28f * mottle - 0.08f * speckle;
            return new Color(0.5f * shade, 0.49f * shade, 0.47f * shade);
        });

        WriteTexture("plaster_albedo", SurfaceTextureSize, (u, v) =>
        {
            float mottle = FractalNoise(u, v, 5, 3);
            float blotches = FractalNoise(u, v, 2, 1);
            float grain = ValueNoise(u * 64f, v * 64f, 64);
            float shade = 0.66f + 0.4f * mottle + 0.24f * blotches - 0.06f * grain;
            return new Color(0.68f * shade, 0.64f * shade, 0.58f * shade);
        });

        WriteTexture("timber_albedo", SurfaceTextureSize, (u, v) =>
        {
            float warp = FractalNoise(u, v, 3, 2) * 0.35f;
            float rings = Mathf.Abs(Mathf.Sin((u + warp) * Mathf.PI * 22f));
            float grain = 0.6f + 0.4f * Mathf.Pow(rings, 0.6f);
            float knots = FractalNoise(u, v, 4, 6);
            float shade = grain * (0.85f + 0.3f * knots);
            return new Color(0.3f * shade, 0.19f * shade, 0.11f * shade);
        });

        WriteTexture("dust_mote", MoteTextureSize, (u, v) =>
        {
            float radius = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            float alpha = Mathf.Clamp01(1f - radius);
            return new Color(1f, 1f, 1f, alpha * alpha);
        });
    }

    private delegate Color TexelAt(float u, float v);

    private static void WriteTexture(string name, int size, TexelAt texel)
    {
        string path = $"{TexturesRoot}/{name}.png";
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                pixels[y * size + x] = texel((x + 0.5f) / size, (y + 0.5f) / size);
            }
        }

        texture.SetPixels32(pixels);
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
    }

    /// <summary>
    /// Periodic value noise: the lattice wraps every <paramref name="period"/> cells, and callers
    /// sample it with a frequency equal to that period so the whole [0, 1) tile is seamless.
    /// </summary>
    private static float ValueNoise(float x, float y, int period)
    {
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        float tx = Mathf.SmoothStep(0f, 1f, x - x0);
        float ty = Mathf.SmoothStep(0f, 1f, y - y0);

        float a = LatticeHash(x0, y0, period);
        float b = LatticeHash(x0 + 1, y0, period);
        float c = LatticeHash(x0, y0 + 1, period);
        float d = LatticeHash(x0 + 1, y0 + 1, period);

        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
    }

    private static float LatticeHash(int x, int y, int period)
    {
        int px = ((x % period) + period) % period;
        int py = ((y % period) + period) % period;
        float value = Mathf.Sin(px * 127.1f + py * 311.7f) * 43758.5453f;
        return value - Mathf.Floor(value);
    }

    /// <summary>Octaves of tiling value noise; each octave doubles both its frequency and its lattice period.</summary>
    private static float FractalNoise(float u, float v, int octaves, int baseFrequency)
    {
        float sum = 0f;
        float amplitude = 0.5f;
        int frequency = baseFrequency;

        for (int i = 0; i < octaves; i++)
        {
            sum += ValueNoise(u * frequency, v * frequency, frequency) * amplitude;
            amplitude *= 0.5f;
            frequency *= 2;
        }

        return sum;
    }

    /// <summary>
    /// Tiling surfaces are clamped to 1024 and block-compressed at high quality (BC7 on desktop),
    /// with mipmaps so the floor grain stays stable at the far end of the shadow range. The dust
    /// disc stays uncompressed and clamped so the particle edge never ringing-artefacts.
    /// </summary>
    private static void ConfigureTextureImports()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexturesRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;

            bool isMote = path.EndsWith("dust_mote.png", System.StringComparison.Ordinal);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = isMote;
            importer.wrapMode = isMote ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            importer.anisoLevel = TextureAnisoLevel;
            importer.mipmapEnabled = !isMote;
            importer.streamingMipmaps = false;
            importer.maxTextureSize = SurfaceTextureSize;
            importer.textureCompression = isMote ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
            importer.crunchedCompression = false;
            importer.SaveAndReimport();
        }
    }

    // ------------------------------------------------------------------ materials

    private static ArenaMaterials CreateMaterials()
    {
        EnsureFolder(MaterialsRoot);

        return new ArenaMaterials
        {
            Floor = CreateLitMaterial("M_ConcreteFloor", "concrete_albedo", new Vector2(16f, 12f), Color.white, 0.28f),
            Mat = CreateLitMaterial("M_RubberMat", "concrete_albedo", new Vector2(5f, 2f), new Color(0.4f, 0.38f, 0.36f), 0.18f),
            Wall = CreateLitMaterial("M_PlasterWall", "plaster_albedo", new Vector2(4f, 3f), Color.white, 0.2f),
            WallUpper = CreateLitMaterial("M_PlasterWallUpper", "plaster_albedo", new Vector2(4f, 4f), new Color(0.36f, 0.34f, 0.32f), 0.16f),
            Timber = CreateLitMaterial("M_DarkTimber", "timber_albedo", new Vector2(1f, 4f), Color.white, 0.34f),
            Accent = CreateEmissiveMaterial("M_WarmAccent", WarmAccentColor * 1.3f),
            Energy = CreateEmissiveMaterial("M_HadoukenEnergy", EnergyColor * 4f),
            Dust = CreateDustMaterial()
        };
    }

    private static Material CreateLitMaterial(string name, string textureBaseName, Vector2 tiling, Color tint, float smoothness)
    {
        Material material = LoadOrCreateMaterial(name);
        Texture2D albedo = textureBaseName != null ? LoadTexture(textureBaseName + ".png") : null;

        material.SetTexture(BaseMapId, albedo);
        material.SetTextureScale(BaseMapId, tiling);
        material.SetColor(BaseColorId, tint);
        material.SetFloat(SmoothnessId, smoothness);
        material.SetFloat(MetallicId, 0f);
        material.SetColor(EmissionColorId, Color.black);
        material.DisableKeyword(EmissionKeyword);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;

        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Self-lit surfaces (warm accent slots, energy) use the unlit shader with an HDR colour above
    /// 1.0 so the bloom pass picks them up and the tone mapper keeps their hue.
    /// </summary>
    private static Material CreateEmissiveMaterial(string name, Color hdrColor)
    {
        Material material = LoadOrCreateMaterial(name, UnlitShaderName);
        material.SetColor(BaseColorId, hdrColor);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Additive transparent particle material for the dust motes. The blend state is written
    /// directly (surface transparent, additive blend, no depth write) so the material needs no
    /// inspector pass to render correctly.
    /// </summary>
    private static Material CreateDustMaterial()
    {
        Material material = LoadOrCreateMaterial("M_DustMote", ParticleShaderName);
        material.SetTexture(BaseMapId, LoadTexture("dust_mote.png"));
        material.SetColor(BaseColorId, new Color(1f, 0.88f, 0.72f, 0.4f));
        material.SetFloat(SurfaceId, 1f);
        material.SetFloat(BlendId, 2f);
        material.SetFloat(SrcBlendId, (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat(DstBlendId, (float)UnityEngine.Rendering.BlendMode.One);
        material.SetFloat(ZWriteId, 0f);
        material.EnableKeyword(TransparentSurfaceKeyword);
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
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
            if (SceneHierarchyOrganizer.IsUnderDecor(target.transform, ArenaRootName)) continue;

            if (target.TryGetComponent(out MeshCollider _))
            {
                renderer.sharedMaterial = materials.Floor;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                continue;
            }

            if (!target.TryGetComponent(out BoxCollider _)) continue;

            renderer.sharedMaterial = materials.Wall;
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
        root.SetParent(SceneHierarchyOrganizer.GetOrCreateCategory(SceneHierarchyOrganizer.EnvironmentCategory), false);

        GameObject outerFloor = CreatePrimitive(PrimitiveType.Plane, OuterFloorName, root, new Vector3(0f, -0.02f, 0f), Vector3.zero, new Vector3(8f, 1f, 6f), materials.Floor);
        Object.DestroyImmediate(outerFloor.GetComponent<Collider>());

        BuildTrainingMat(root, materials);
        BuildBackWall(root, materials);
        BuildCanopy(root, materials);
        BuildDustMotes(root, materials.Dust);
        ConfigureDecorShadows(root, materials.Accent);
    }

    /// <summary>
    /// Self-lit accents, the ground plane and the canopy contribute nothing useful to the shadow
    /// map (the rafters would only stripe the back wall), so they are excluded from the cascade
    /// passes; unlit surfaces cannot receive shadows either.
    /// </summary>
    private static void ConfigureDecorShadows(Transform root, Material selfLit)
    {
        foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>())
        {
            bool isSelfLit = renderer.sharedMaterial == selfLit;
            bool isFloor = renderer.name == OuterFloorName;
            bool isCanopy = renderer.name == CanopyName || renderer.name == RafterName;

            renderer.shadowCastingMode = isSelfLit || isFloor || isCanopy ? ShadowCastingMode.Off : ShadowCastingMode.On;
            renderer.receiveShadows = !isSelfLit;
        }
    }

    /// <summary>
    /// A raised rubber mat with a timber border marks the playable area, replacing the neon ring
    /// with something a fighter would actually train on. The mat is four quadrants rather than one
    /// slab so each piece picks up its nearest downlights within the per-object light limit.
    /// </summary>
    private static void BuildTrainingMat(Transform root, ArenaMaterials materials)
    {
        const float matThickness = 0.04f;
        for (int x = -1; x <= 1; x += 2)
        {
            for (int z = -1; z <= 1; z += 2)
            {
                CreatePrimitive(PrimitiveType.Cube, "Training Mat", root, new Vector3(x * MatHalfLength * 0.5f, matThickness * 0.5f, z * MatHalfWidth * 0.5f), Vector3.zero, new Vector3(MatHalfLength, matThickness, MatHalfWidth), materials.Mat);
            }
        }

        float outerLength = (MatHalfLength + PlatformFrameWidth) * 2f;
        float outerWidth = (MatHalfWidth + PlatformFrameWidth) * 2f;
        float zOffset = MatHalfWidth + PlatformFrameWidth * 0.5f;
        float xOffset = MatHalfLength + PlatformFrameWidth * 0.5f;
        float frameHeight = matThickness * 1.5f;

        CreatePrimitive(PrimitiveType.Cube, "Frame North", root, new Vector3(0f, frameHeight * 0.5f, zOffset), Vector3.zero, new Vector3(outerLength, frameHeight, PlatformFrameWidth), materials.Timber);
        CreatePrimitive(PrimitiveType.Cube, "Frame South", root, new Vector3(0f, frameHeight * 0.5f, -zOffset), Vector3.zero, new Vector3(outerLength, frameHeight, PlatformFrameWidth), materials.Timber);
        CreatePrimitive(PrimitiveType.Cube, "Frame East", root, new Vector3(xOffset, frameHeight * 0.5f, 0f), Vector3.zero, new Vector3(PlatformFrameWidth, frameHeight, outerWidth), materials.Timber);
        CreatePrimitive(PrimitiveType.Cube, "Frame West", root, new Vector3(-xOffset, frameHeight * 0.5f, 0f), Vector3.zero, new Vector3(PlatformFrameWidth, frameHeight, outerWidth), materials.Timber);
    }

    /// <summary>
    /// Timber pillars in front of plaster slabs. Each slab is split at the lower band: a lit lower
    /// wall the fighters play against and a darker upper wall that climbs to the enclosure height,
    /// so the top of the frame always lands on shadowed plaster. Alternating slabs carry a dim warm
    /// slot; a timber bench runs along the base.
    /// </summary>
    private static void BuildBackWall(Transform root, ArenaMaterials materials)
    {
        float firstPillarX = -PillarSpacing * (PillarCount - 1) * 0.5f;
        float upperHeight = EnclosureHeight - LowerSlabHeight;

        for (int i = 0; i < PillarCount - 1; i++)
        {
            float x = firstPillarX + PillarSpacing * (i + 0.5f);
            float slabWidth = PillarSpacing - 1f;
            CreatePrimitive(PrimitiveType.Cube, "Back Slab", root, new Vector3(x, LowerSlabHeight * 0.5f, BackSlabZ), Vector3.zero, new Vector3(slabWidth, LowerSlabHeight, 0.5f), materials.Wall);
            CreatePrimitive(PrimitiveType.Cube, "Upper Slab", root, new Vector3(x, LowerSlabHeight + upperHeight * 0.5f, BackSlabZ + 0.1f), Vector3.zero, new Vector3(slabWidth, upperHeight, 0.5f), materials.WallUpper);

            if (i % 2 == 0)
            {
                CreatePrimitive(PrimitiveType.Cube, "Slab Slot", root, new Vector3(x, LowerSlabHeight - 0.6f, BackSlabZ - 0.27f), Vector3.zero, new Vector3(slabWidth - 1.6f, 0.04f, 0.02f), materials.Accent);
            }
        }

        for (int i = 0; i < PillarCount; i++)
        {
            float x = firstPillarX + i * PillarSpacing;
            CreatePrimitive(PrimitiveType.Cube, "Pillar", root, new Vector3(x, PillarHeight * 0.5f, BackDecorZ), Vector3.zero, new Vector3(0.5f, PillarHeight, 0.5f), materials.Timber);
        }

        float benchSpan = PillarSpacing * (PillarCount - 1) + 1.2f;
        CreatePrimitive(PrimitiveType.Cube, "Bench", root, new Vector3(0f, 0.22f, BackDecorZ - 1.05f), Vector3.zero, new Vector3(benchSpan, 0.44f, 0.6f), materials.Timber);

        for (int x = -1; x <= 1; x += 2)
        {
            CreatePrimitive(PrimitiveType.Cube, "Side Column", root, new Vector3(x * SideColumnX, PillarHeight * 0.5f, 2.2f), Vector3.zero, new Vector3(0.8f, PillarHeight, 0.8f), materials.Timber);
        }
    }

    /// <summary>
    /// A dark plaster ceiling on timber beams closes the room from above, and warm recessed
    /// downlights under it pool light on the mat. The downlights carry no shadows: the project's
    /// performance profile keeps additional-light shadows off, so contact comes from the key light
    /// and ambient occlusion.
    /// </summary>
    private static void BuildCanopy(Transform root, ArenaMaterials materials)
    {
        float span = PillarSpacing * (PillarCount - 1) + 2f * SideColumnX - 2f;
        float canopyZ = BackDecorZ - CanopyDepth * 0.5f;

        CreatePrimitive(PrimitiveType.Cube, CanopyName, root, new Vector3(0f, CanopyHeight + 0.15f, canopyZ), Vector3.zero, new Vector3(span, 0.3f, CanopyDepth), materials.WallUpper);
        CreatePrimitive(PrimitiveType.Cube, "Ridge Beam", root, new Vector3(0f, CanopyHeight - 0.25f, BackDecorZ), Vector3.zero, new Vector3(span, 0.5f, 0.6f), materials.Timber);

        float firstPillarX = -PillarSpacing * (PillarCount - 1) * 0.5f;
        for (int i = 0; i < PillarCount; i++)
        {
            float x = firstPillarX + i * PillarSpacing;
            CreatePrimitive(PrimitiveType.Cube, RafterName, root, new Vector3(x, CanopyHeight - 0.25f, canopyZ), Vector3.zero, new Vector3(0.28f, 0.5f, CanopyDepth), materials.Timber);
        }

        for (int pair = 0; pair < DownlightPairs; pair++)
        {
            float x = (pair + 0.5f) * 8.5f;
            BuildDownlight(root, new Vector3(x, CanopyHeight - 0.4f, 0.4f));
            BuildDownlight(root, new Vector3(-x, CanopyHeight - 0.4f, 0.4f));
        }
    }

    private static void BuildDownlight(Transform root, Vector3 position)
    {
        GameObject lightObject = new GameObject("Downlight");
        lightObject.transform.SetParent(root, false);
        lightObject.transform.position = position;
        lightObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Spot;
        light.color = DownlightColor;
        light.intensity = 55f;
        light.range = 24f;
        light.spotAngle = 72f;
        light.innerSpotAngle = 30f;
        light.shadows = LightShadows.None;
    }

    /// <summary>
    /// Dust drifting through the downlights: a single world-space emitter of small additive discs
    /// with slow noise, prewarmed so the room is alive from the first frame. Simulated natively,
    /// so it adds nothing to the managed heap.
    /// </summary>
    private static void BuildDustMotes(Transform root, Material dustMaterial)
    {
        GameObject motes = new GameObject(DustMotesName);
        motes.transform.SetParent(root, false);
        motes.transform.position = new Vector3(0f, 5f, 0f);

        ParticleSystem system = motes.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.prewarm = true;
        main.playOnAwake = true;
        main.maxParticles = DustMoteCount;
        main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 11f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.08f);
        main.startColor = new Color(1f, 0.92f, 0.8f, 1f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -0.003f;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = true;
        emission.rateOverTime = DustMoteCount / 8.5f;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(28f, 9f, 8f);

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
        renderer.sortingFudge = 0f;
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

    /// <summary>
    /// Warm key from camera-left with soft shadows against a cool, shadowless fill from the right,
    /// a restrained warm rim from behind, warm-dark ambient and close fog in the wall colour so the
    /// far architecture recedes instead of reading as a flat backdrop.
    /// </summary>
    private static void ConfigureLighting()
    {
        Light key = FindOrCreateDirectional(KeyLightName);
        key.transform.rotation = Quaternion.Euler(52f, -24f, 0f);
        key.color = new Color(1f, 0.86f, 0.72f);
        key.intensity = 2f;
        key.shadows = LightShadows.Soft;
        key.shadowStrength = 0.9f;
        key.shadowBias = 0.025f;
        key.shadowNormalBias = 0.45f;

        Light fill = FindOrCreateDirectional(FillLightName);
        fill.transform.rotation = Quaternion.Euler(30f, 60f, 0f);
        fill.color = new Color(0.48f, 0.58f, 0.78f);
        fill.intensity = 0.45f;
        fill.shadows = LightShadows.None;

        Light rim = FindOrCreateDirectional(RimLightName);
        rim.transform.rotation = Quaternion.Euler(20f, 165f, 0f);
        rim.color = new Color(1f, 0.95f, 0.88f);
        rim.intensity = 0.9f;
        rim.shadows = LightShadows.None;

        RenderSettings.sun = key;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.2f, 0.18f, 0.16f);
        RenderSettings.ambientEquatorColor = new Color(0.12f, 0.1f, 0.09f);
        RenderSettings.ambientGroundColor = new Color(0.05f, 0.04f, 0.035f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = FogColor;
        RenderSettings.fogStartDistance = 14f;
        RenderSettings.fogEndDistance = 48f;
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

    // ------------------------------------------------------------------ post-processing

    private static void ConfigurePostProcessing()
    {
        VolumeProfile profile = LoadOrCreateVolumeProfile();

        Tonemapping tonemapping = GetOrAddOverride<Tonemapping>(profile);
        tonemapping.mode.Override(TonemappingMode.ACES);

        Bloom bloom = GetOrAddOverride<Bloom>(profile);
        bloom.threshold.Override(1.1f);
        bloom.intensity.Override(0.45f);
        bloom.scatter.Override(0.65f);

        Vignette vignette = GetOrAddOverride<Vignette>(profile);
        vignette.intensity.Override(0.32f);
        vignette.smoothness.Override(0.45f);

        ColorAdjustments color = GetOrAddOverride<ColorAdjustments>(profile);
        color.postExposure.Override(0.05f);
        color.contrast.Override(18f);
        color.saturation.Override(-6f);

        WhiteBalance whiteBalance = GetOrAddOverride<WhiteBalance>(profile);
        whiteBalance.temperature.Override(6f);
        whiteBalance.tint.Override(2f);

        FilmGrain grain = GetOrAddOverride<FilmGrain>(profile);
        grain.type.Override(FilmGrainLookup.Thin1);
        grain.intensity.Override(0.12f);
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

        Camera camera = Camera.main;
        if (camera == null) return;

        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = FogColor;

        UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = true;
        cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        cameraData.antialiasingQuality = AntialiasingQuality.High;
        cameraData.renderShadows = true;
    }

    /// <summary>
    /// Framing tuned with the enclosure: a shallower pitch, a lower juggle lift and a nearer far
    /// zoom keep the top of the frame on the upper wall band at every distance the camera can reach.
    /// </summary>
    private static void ConfigureCameraFraming()
    {
        FightingCamera fightingCamera = Object.FindFirstObjectByType<FightingCamera>();
        if (fightingCamera == null) return;

        fightingCamera.pitchAngle = CameraPitch;
        fightingCamera.maxElevation = CameraMaxElevation;
        fightingCamera.maxZoomDistance = CameraMaxZoomDistance;
        EditorUtility.SetDirty(fightingCamera);
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
