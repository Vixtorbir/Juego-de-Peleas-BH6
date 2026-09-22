using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Applies the import settings the fighter art needs. The playable characters are the Explosive
/// LLC fighter rigs: each becomes a Humanoid with its own generated avatar and its single material
/// slot remapped to a project URP material that keeps the pack's texture under a player tint, so
/// both fighters share the SRP Batcher path and read as blue and red at a glance. Every animation
/// library imports as Humanoid against a skinned reference rig rather than an avatar of its own: a
/// skeleton-only export has no T-pose (its rest pose is the first animation frame), so a generated
/// avatar carries a wrong muscle reference and the retargeted body ends up twisted at the ankles.
/// The pack's idle and walks borrow the avatar of Male.FBX, the rig they were exported from, and
/// the Mixamo combat library borrows Y Bot, which stays in the project purely as that reference.
/// Each take is renamed after its file, locomotion clips loop, and root motion is baked into the
/// pose so the CharacterController stays the only authority over the fighter's position. Frame
/// ranges already trimmed by the attack timing calibrator are preserved across re-runs.
/// </summary>
public static class FighterModelImportSetup
{
    public const string FighterPackRoot = "Assets/ExplosiveLLC/Fighter Pack Bundle FREE/Fighters";
    public const string Player1ModelPath = FighterPackRoot + "/Male Fighter Mecanim Animation Pack FREE/Characters/Male.FBX";
    public const string Player2ModelPath = FighterPackRoot + "/Heavy Fighter Mecanim Animation Pack FREE/Characters/Heavy.FBX";
    public const string PackAnimationsRoot = FighterPackRoot + "/Male Fighter Mecanim Animation Pack FREE/Animations";
    public const string ReferenceRigRoot = "Assets/_Project/Art/Characters";
    public const string AnimationsRoot = "Assets/_Project/Art/Animations";
    public const string ReferenceAvatarModelPath = ReferenceRigRoot + "/Mixamo/Y_Bot.fbx";

    private const string MaterialsRoot = "Assets/_Project/Art/Materials";
    private const string LitShaderName = "Universal Render Pipeline/Lit";
    private const char PackClipSeparator = '@';

    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
    private static readonly int MetallicId = Shader.PropertyToID("_Metallic");

    private static readonly string[] LoopingClipKeywords = { "Idle", "Walk", "Strafe", "Stance", "Guard" };

    /// <summary>
    /// Stance and locomotion clips whose body is squared up to the combat axis, with the yaw each
    /// one keeps once squared. A Mixamo fighting stance is authored bladed, and baking the root
    /// rotation "based upon original" carries that yaw into the pose, so both fighters end up
    /// showing the camera a shoulder instead of facing each other. Basing the bake on the body
    /// orientation instead lets Unity align the average hip and shoulder line with the fighter's
    /// forward, which gameplay already points at the opponent. The value is the stance blade kept
    /// on top of that: zero squares the fighter fully, and a small negative angle re-opens the
    /// guard without ever losing the facing.
    /// </summary>
    private static readonly Dictionary<string, float> SquaredStanceClips = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
    {
        { "Fight_Idle", 0f },
        { "Walk_Forward", 0f },
        { "Walk_Backward", 0f },
        { "Crouch_Idle", 0f },
        { "Dash_Forward", 0f },
        { "Dash_Backward", 0f }
    };

    private readonly struct FighterSkin
    {
        public readonly string ModelPath;
        public readonly string SourceMaterialName;
        public readonly string TextureName;
        public readonly string BodyMaterialName;
        public readonly Color Tint;

        public FighterSkin(string modelPath, string sourceMaterialName, string textureName, string bodyMaterialName, Color tint)
        {
            ModelPath = modelPath;
            SourceMaterialName = sourceMaterialName;
            TextureName = textureName;
            BodyMaterialName = bodyMaterialName;
            Tint = tint;
        }
    }

    /// <summary>
    /// Material slot names as authored inside each pack FBX, each redirected to a tinted copy of the
    /// pack's own texture so Player 1 reads blue and Player 2 red without touching vendor assets.
    /// </summary>
    private static readonly FighterSkin[] Skins =
    {
        new FighterSkin(Player1ModelPath, "Male", "Male", "M_FighterMale_P1", new Color(0.55f, 0.72f, 1f)),
        new FighterSkin(Player2ModelPath, "Heavy", "Heavy", "M_FighterHeavy_P2", new Color(1f, 0.5f, 0.45f))
    };

    [MenuItem("Fighting Game/Configure Fighter Models (Humanoid)", priority = 10)]
    public static void ConfigureFighterModels()
    {
        int characters = ConfigurePlayableCharacters();
        int packClips = ConfigureFolder(PackAnimationsRoot, ImportRole.PackAnimation);
        int referenceRigs = ConfigureFolder(ReferenceRigRoot, ImportRole.ReferenceRig);
        int libraryClips = ConfigureFolder(AnimationsRoot, ImportRole.LibraryAnimation);

        Debug.Log($"[FighterModelImportSetup] Configured {characters} playable character(s), {packClips} pack clip(s), {referenceRigs} reference rig(s) and {libraryClips} library clip(s).");
    }

    private enum ImportRole
    {
        PlayableCharacter,
        PackAnimation,
        ReferenceRig,
        LibraryAnimation
    }

    private static int ConfigurePlayableCharacters()
    {
        int configured = 0;
        foreach (FighterSkin skin in Skins)
        {
            if (AssetImporter.GetAtPath(skin.ModelPath) is not ModelImporter importer)
            {
                Debug.LogError($"[FighterModelImportSetup] Fighter model not found at '{skin.ModelPath}'.");
                continue;
            }

            ConfigureCharacterImporter(importer);
            RemapMaterial(importer, skin);
            importer.SaveAndReimport();
            LogAvatarStatus(skin.ModelPath);
            configured++;
        }

        return configured;
    }

    private static int ConfigureFolder(string root, ImportRole role)
    {
        if (!AssetDatabase.IsValidFolder(root)) return 0;

        string referenceAvatarPath = ReferenceAvatarPathFor(role);
        Avatar referenceAvatar = referenceAvatarPath != null ? FindAvatar(referenceAvatarPath) : null;
        if (referenceAvatarPath != null && referenceAvatar == null)
            Debug.LogError($"[FighterModelImportSetup] Reference avatar not found in '{referenceAvatarPath}'; animation clips will fall back to self-generated avatars.");

        int configured = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { root }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is not ModelImporter importer) continue;

            switch (role)
            {
                case ImportRole.PackAnimation:
                    ConfigureAnimationImporter(importer, PackClipName(path), referenceAvatar);
                    break;
                case ImportRole.ReferenceRig:
                    ConfigureCharacterImporter(importer);
                    ClearMaterialRemaps(importer);
                    importer.materialImportMode = ModelImporterMaterialImportMode.None;
                    break;
                default:
                    ConfigureAnimationImporter(importer, Path.GetFileNameWithoutExtension(path), referenceAvatar);
                    break;
            }

            importer.SaveAndReimport();
            configured++;

            if (role == ImportRole.ReferenceRig) LogAvatarStatus(path);
            else LogClipStatus(path);
        }

        return configured;
    }

    /// <summary>Pack clips are named "Character@Motion.FBX"; the motion name becomes the clip name.</summary>
    private static string PackClipName(string path)
    {
        string fileName = Path.GetFileNameWithoutExtension(path);
        int separator = fileName.IndexOf(PackClipSeparator);
        return separator >= 0 && separator < fileName.Length - 1 ? fileName.Substring(separator + 1) : fileName;
    }

    /// <summary>
    /// Animation-only exports carry no bind pose, so their rest pose is the first animated frame and
    /// an avatar generated from it retargets with twisted ankles. Both libraries therefore borrow the
    /// avatar of a skinned model that shares their skeleton: the pack clips use Male.FBX, whose rig
    /// they were exported from, and the Mixamo clips use the Y Bot reference rig.
    /// </summary>
    private static string ReferenceAvatarPathFor(ImportRole role) => role switch
    {
        ImportRole.PackAnimation => Player1ModelPath,
        ImportRole.LibraryAnimation => ReferenceAvatarModelPath,
        _ => null
    };

    private static Avatar FindAvatar(string modelPath)
    {
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
        {
            if (asset is Avatar avatar && avatar.isValid && avatar.isHuman) return avatar;
        }

        return null;
    }

    private static void ConfigureCharacterImporter(ModelImporter importer)
    {
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.isReadable = false;
    }

    /// <summary>
    /// Keeps material import on so the FBX's single slot exists, then redirects that slot to the
    /// tinted project material. The vendor material asset is left untouched.
    /// </summary>
    private static void RemapMaterial(ModelImporter importer, FighterSkin skin)
    {
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        importer.materialLocation = ModelImporterMaterialLocation.InPrefab;

        Texture2D texture = FindPackTexture(skin.ModelPath, skin.TextureName);
        Material body = LoadOrCreateBodyMaterial(skin.BodyMaterialName, texture, skin.Tint);

        ClearMaterialRemaps(importer);
        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), skin.SourceMaterialName), body);
    }

    private static void ClearMaterialRemaps(ModelImporter importer)
    {
        foreach (AssetImporter.SourceAssetIdentifier identifier in importer.GetExternalObjectMap().Keys)
            importer.RemoveRemap(identifier);
    }

    /// <summary>The pack keeps each character's texture next to its model under a sibling Textures folder.</summary>
    private static Texture2D FindPackTexture(string modelPath, string textureName)
    {
        string packRoot = Path.GetDirectoryName(Path.GetDirectoryName(modelPath));
        string texturesFolder = Path.Combine(packRoot, "Textures").Replace('\\', '/');

        foreach (string guid in AssetDatabase.FindAssets($"{textureName} t:Texture2D", new[] { texturesFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.Equals(Path.GetFileNameWithoutExtension(path), textureName, StringComparison.OrdinalIgnoreCase))
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        Debug.LogWarning($"[FighterModelImportSetup] Texture '{textureName}' not found under '{texturesFolder}'; the body material will be a flat tint.");
        return null;
    }

    private static Material LoadOrCreateBodyMaterial(string name, Texture2D texture, Color tint)
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

        material.SetTexture(BaseMapId, texture);
        material.SetColor(BaseColorId, tint);
        material.SetFloat(SmoothnessId, 0.35f);
        material.SetFloat(MetallicId, 0f);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void ConfigureAnimationImporter(ModelImporter importer, string clipName, Avatar referenceAvatar)
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
            bool squared = SquaredStanceClips.TryGetValue(clipName, out float stanceBlade);
            clip.keepOriginalOrientation = !squared;
            clip.rotationOffset = squared ? stanceBlade : 0f;
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
