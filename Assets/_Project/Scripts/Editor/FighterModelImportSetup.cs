using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Applies the import settings the fighter art needs. Character FBX files (Mixamo Y Bot / X Bot)
/// become Humanoid rigs with a generated avatar and project materials remapped onto the Mixamo
/// material slots. Animation FBX files (skeleton-only Mixamo clips) are imported as Humanoid using
/// the Y Bot avatar as their rig definition: a skeleton-only export has no T-pose of its own (its
/// rest pose is the first animation frame), so an avatar generated from it carries a wrong muscle
/// reference and the retargeted body ends up twisted. Each take is renamed after its file,
/// locomotion clips loop, and root motion is baked into the pose so the CharacterController stays
/// the only authority over the fighter's position. Frame ranges already trimmed by the attack
/// timing calibrator are preserved across re-runs.
/// </summary>
public static class FighterModelImportSetup
{
    public const string CharactersRoot = "Assets/_Project/Art/Characters";
    public const string AnimationsRoot = "Assets/_Project/Art/Animations";
    public const string ReferenceAvatarModelPath = CharactersRoot + "/Mixamo/Y_Bot.fbx";

    private const string MaterialsRoot = "Assets/_Project/Art/Materials";
    private const string LitShaderName = "Universal Render Pipeline/Lit";
    private const string JointsMaterialName = "M_FighterJoints";
    private const string JointsMaterialKeyword = "Joints";

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
    private static readonly int MetallicId = Shader.PropertyToID("_Metallic");

    private static readonly string[] LoopingClipKeywords = { "Idle", "Walk", "Strafe", "Stance", "Guard" };

    private readonly struct FighterSkin
    {
        public readonly string ModelName;
        public readonly string BodyMaterialName;
        public readonly Color BodyColor;
        public readonly string[] SourceMaterialNames;

        public FighterSkin(string modelName, string bodyMaterialName, Color bodyColor, params string[] sourceMaterialNames)
        {
            ModelName = modelName;
            BodyMaterialName = bodyMaterialName;
            BodyColor = bodyColor;
            SourceMaterialNames = sourceMaterialNames;
        }
    }

    /// <summary>
    /// Material slot names as authored inside each Mixamo FBX. Slots containing "Joints" receive the
    /// shared dark joints material; every other slot receives the fighter's body colour.
    /// </summary>
    private static readonly FighterSkin[] Skins =
    {
        new FighterSkin("Y_Bot", "M_FighterBody_P1", new Color(0.16f, 0.42f, 0.85f), "Alpha_Body_MAT", "Alpha_Joints_MAT"),
        new FighterSkin("X_Bot", "M_FighterBody_P2", new Color(0.82f, 0.22f, 0.18f), "Beta_HighLimbsGeoSG3", "Beta_Joints_MAT1")
    };

    [MenuItem("Fighting Game/Configure Fighter Models (Humanoid)", priority = 10)]
    public static void ConfigureFighterModels()
    {
        int characters = ConfigureFolder(CharactersRoot, isAnimationLibrary: false);
        int animations = ConfigureFolder(AnimationsRoot, isAnimationLibrary: true);

        Debug.Log($"[FighterModelImportSetup] Configured {characters} character model(s) and {animations} animation file(s).");
    }

    private static int ConfigureFolder(string root, bool isAnimationLibrary)
    {
        if (!AssetDatabase.IsValidFolder(root)) return 0;

        Avatar referenceAvatar = isAnimationLibrary ? FindAvatar(ReferenceAvatarModelPath) : null;
        if (isAnimationLibrary && referenceAvatar == null)
            Debug.LogError($"[FighterModelImportSetup] Reference avatar not found in '{ReferenceAvatarModelPath}'; animation clips will fall back to self-generated avatars.");

        int configured = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { root }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is not ModelImporter importer) continue;

            if (isAnimationLibrary) ConfigureAnimationImporter(importer, path, referenceAvatar);
            else ConfigureCharacterImporter(importer, path);

            importer.SaveAndReimport();
            configured++;

            if (isAnimationLibrary) LogClipStatus(path);
            else LogAvatarStatus(path);
        }

        return configured;
    }

    private static Avatar FindAvatar(string modelPath)
    {
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
        {
            if (asset is Avatar avatar && avatar.isValid && avatar.isHuman) return avatar;
        }

        return null;
    }

    private static void ConfigureCharacterImporter(ModelImporter importer, string path)
    {
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        importer.isReadable = false;

        RemapMaterials(importer, Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>
    /// Mixamo characters ship two material slots (body surface and joints). Both are redirected to
    /// project URP materials so each fighter gets a distinct colour and the SRP Batcher sees shared assets.
    /// </summary>
    private static void RemapMaterials(ModelImporter importer, string modelName)
    {
        int skinIndex = Array.FindIndex(Skins, skin => string.Equals(skin.ModelName, modelName, StringComparison.OrdinalIgnoreCase));
        if (skinIndex < 0) return;

        FighterSkin skin = Skins[skinIndex];
        Material body = LoadOrCreateLitMaterial(skin.BodyMaterialName, skin.BodyColor, 0.55f, 0.1f);
        Material joints = LoadOrCreateLitMaterial(JointsMaterialName, new Color(0.12f, 0.12f, 0.14f), 0.7f, 0.6f);

        foreach (AssetImporter.SourceAssetIdentifier identifier in importer.GetExternalObjectMap().Keys)
            importer.RemoveRemap(identifier);

        foreach (string materialName in skin.SourceMaterialNames)
        {
            bool isJoints = materialName.IndexOf(JointsMaterialKeyword, StringComparison.OrdinalIgnoreCase) >= 0;
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), materialName), isJoints ? joints : body);
        }
    }

    private static Material LoadOrCreateLitMaterial(string name, Color color, float smoothness, float metallic)
    {
        if (!AssetDatabase.IsValidFolder(MaterialsRoot))
            AssetDatabase.CreateFolder(Path.GetDirectoryName(MaterialsRoot), Path.GetFileName(MaterialsRoot));

        string path = $"{MaterialsRoot}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find(LitShaderName);

        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
        }

        material.SetColor(BaseColorId, color);
        material.SetFloat(SmoothnessId, smoothness);
        material.SetFloat(MetallicId, metallic);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void ConfigureAnimationImporter(ModelImporter importer, string path, Avatar referenceAvatar)
    {
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = referenceAvatar != null ? ModelImporterAvatarSetup.CopyFromOther : ModelImporterAvatarSetup.CreateFromThisModel;
        importer.sourceAvatar = referenceAvatar;
        importer.importAnimation = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importBlendShapes = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.isReadable = false;

        string clipName = Path.GetFileNameWithoutExtension(path);
        ModelImporterClipAnimation[] clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;

        for (int i = 0; i < clips.Length; i++)
        {
            ModelImporterClipAnimation clip = clips[i];
            clip.name = clips.Length == 1 ? clipName : $"{clipName}_{i}";
            clip.loopTime = IsLoopingClip(clipName);
            clip.loopPose = clip.loopTime;
            clip.lockRootRotation = true;
            clip.lockRootHeightY = true;
            clip.lockRootPositionXZ = true;
            clip.keepOriginalOrientation = true;
            clip.keepOriginalPositionY = true;
            clip.keepOriginalPositionXZ = true;
        }

        importer.clipAnimations = clips;
    }

    private static bool IsLoopingClip(string clipName)
    {
        foreach (string keyword in LoopingClipKeywords)
        {
            if (clipName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }

        return false;
    }

    private static void LogClipStatus(string path)
    {
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is not AnimationClip clip || clip.name.StartsWith("__preview__", StringComparison.Ordinal)) continue;

            if (clip.isHumanMotion)
                Debug.Log($"[FighterModelImportSetup] '{path}': humanoid clip '{clip.name}' ({clip.length:F2} s, loop={clip.isLooping}).");
            else
                Debug.LogError($"[FighterModelImportSetup] '{path}': clip '{clip.name}' is not humanoid motion; check the avatar mapping.");
            return;
        }

        Debug.LogError($"[FighterModelImportSetup] '{path}': no animation clip was imported.");
    }

    private static void LogAvatarStatus(string path)
    {
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is not Avatar avatar) continue;

            if (avatar.isValid && avatar.isHuman)
                Debug.Log($"[FighterModelImportSetup] '{path}': humanoid avatar generated.");
            else
                Debug.LogError($"[FighterModelImportSetup] '{path}': avatar is not a valid humanoid (valid={avatar.isValid}, human={avatar.isHuman}). Check the bone mapping in the Rig tab.");
            return;
        }

        Debug.LogError($"[FighterModelImportSetup] '{path}': no avatar was generated.");
    }
}
