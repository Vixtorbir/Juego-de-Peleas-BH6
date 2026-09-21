using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the <see cref="CombatAudioLibrary"/> asset from the clips under the audio folder,
/// grouping variations by file name prefix, installs an <see cref="AudioManager"/> on the
/// scene's managers object and routes its pools to the SFX and Music groups of the combat mixer.
/// Import settings are tuned per role: short SFX decompress on load, music streams.
/// </summary>
public static class AudioSceneSetup
{
    private const string AudioRoot = "Assets/_Project/Audio";
    private const string LibraryPath = AudioRoot + "/CombatAudioLibrary.asset";
    private const string MixerPath = AudioRoot + "/CombatMixer.mixer";
    private const string MusicFolder = AudioRoot + "/Music";
    private const string SfxGroupName = "SFX";
    private const string MusicGroupName = "Music";
    private const float MusicMasterVolume = 0.16f;

    private readonly struct EventBinding
    {
        public readonly string Field;
        public readonly string Prefix;
        public readonly float Volume;
        public readonly float PitchSpread;

        public EventBinding(string field, string prefix, float volume, float pitchSpread)
        {
            Field = field;
            Prefix = prefix;
            Volume = volume;
            PitchSpread = pitchSpread;
        }
    }

    private static readonly EventBinding[] Bindings =
    {
        new EventBinding("whiff", "whiff_", 0.55f, 0.12f),
        new EventBinding("lightHit", "hit_light_", 0.85f, 0.08f),
        new EventBinding("heavyHit", "hit_heavy_", 1f, 0.06f),
        new EventBinding("kickHit", "hit_kick_", 0.95f, 0.08f),
        new EventBinding("block", "block_", 0.7f, 0.1f),
        new EventBinding("hadoukenCast", "hadouken_cast_", 0.6f, 0.05f),
        new EventBinding("hadoukenShot", "hadouken_shot_", 0.8f, 0.05f),
        new EventBinding("hadoukenImpact", "hadouken_impact_", 0.9f, 0.06f),
        new EventBinding("shoryukenRise", "shoryuken_rise_", 0.85f, 0.03f),
        new EventBinding("counterHit", "counter_hit_", 0.75f, 0.04f),
        new EventBinding("knockoutImpact", "knockout_impact_", 1f, 0.02f),
        new EventBinding("knockoutSting", "knockout_sting_", 0.9f, 0f)
    };

    [MenuItem("Fighting Game/Setup Combat Audio", priority = 50)]
    public static void SetupCombatAudio()
    {
        ConfigureClipImports();
        CombatAudioLibrary library = BuildLibrary();
        InstallAudioManager(library);
        AssetDatabase.SaveAssets();

        Debug.Log($"[AudioSceneSetup] Combat audio library built at '{LibraryPath}'.");
    }

    private static void ConfigureClipImports()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is not AudioImporter importer) continue;

            bool isMusic = path.StartsWith(MusicFolder, StringComparison.Ordinal);
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = isMusic ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = isMusic ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM;
            settings.quality = 0.7f;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = !isMusic;
            importer.loadInBackground = isMusic;
            importer.SaveAndReimport();
        }
    }

    private static CombatAudioLibrary BuildLibrary()
    {
        CombatAudioLibrary library = AssetDatabase.LoadAssetAtPath<CombatAudioLibrary>(LibraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<CombatAudioLibrary>();
            AssetDatabase.CreateAsset(library, LibraryPath);
        }

        Dictionary<string, List<AudioClip>> clipsByPrefix = IndexClips();
        var serialized = new SerializedObject(library);

        foreach (EventBinding binding in Bindings)
        {
            SerializedProperty eventProperty = serialized.FindProperty(binding.Field);
            SerializedProperty variations = eventProperty.FindPropertyRelative("variations");
            List<AudioClip> clips = clipsByPrefix.TryGetValue(binding.Prefix, out List<AudioClip> found) ? found : new List<AudioClip>();

            variations.arraySize = clips.Count;
            for (int i = 0; i < clips.Count; i++)
                variations.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];

            eventProperty.FindPropertyRelative("volume").floatValue = binding.Volume;
            eventProperty.FindPropertyRelative("pitchSpread").floatValue = binding.PitchSpread;

            if (clips.Count == 0)
                Debug.LogWarning($"[AudioSceneSetup] No clips found with prefix '{binding.Prefix}' for event '{binding.Field}'.");
        }

        serialized.FindProperty("battleTheme").objectReferenceValue = FindBattleTheme();
        serialized.FindProperty("musicVolume").floatValue = 1f;

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(library);
        return library;
    }

    /// <summary>
    /// The battle theme is the single track under the music folder; when several exist the first in
    /// path order wins so the choice is deterministic across machines.
    /// </summary>
    private static AudioClip FindBattleTheme()
    {
        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { MusicFolder });
        if (guids.Length == 0)
        {
            Debug.LogWarning($"[AudioSceneSetup] No music clip found under '{MusicFolder}'.");
            return null;
        }

        var paths = new string[guids.Length];
        for (int i = 0; i < guids.Length; i++) paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
        Array.Sort(paths, StringComparer.Ordinal);

        return AssetDatabase.LoadAssetAtPath<AudioClip>(paths[0]);
    }

    private static Dictionary<string, List<AudioClip>> IndexClips()
    {
        var index = new Dictionary<string, List<AudioClip>>(StringComparer.OrdinalIgnoreCase);

        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith(MusicFolder, StringComparison.Ordinal)) continue;

            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) continue;

            foreach (EventBinding binding in Bindings)
            {
                if (!clip.name.StartsWith(binding.Prefix, StringComparison.OrdinalIgnoreCase)) continue;

                if (!index.TryGetValue(binding.Prefix, out List<AudioClip> list))
                {
                    list = new List<AudioClip>();
                    index[binding.Prefix] = list;
                }

                list.Add(clip);
                break;
            }
        }

        foreach (List<AudioClip> list in index.Values)
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        return index;
    }

    private static void InstallAudioManager(CombatAudioLibrary library)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != FighterSceneSetup.ScenePath)
            scene = EditorSceneManager.OpenScene(FighterSceneSetup.ScenePath, OpenSceneMode.Single);

        AudioManager manager = UnityEngine.Object.FindFirstObjectByType<AudioManager>();
        if (manager == null)
        {
            var host = new GameObject(nameof(AudioManager));
            host.transform.SetParent(SceneHierarchyOrganizer.GetOrCreateCategory(SceneHierarchyOrganizer.ManagersCategory), false);
            manager = host.AddComponent<AudioManager>();
        }

        var serialized = new SerializedObject(manager);
        serialized.FindProperty("library").objectReferenceValue = library;
        serialized.FindProperty("musicMasterVolume").floatValue = MusicMasterVolume;
        serialized.FindProperty("sfxMixerGroup").objectReferenceValue = FindMixerGroup(SfxGroupName);
        serialized.FindProperty("musicMixerGroup").objectReferenceValue = FindMixerGroup(MusicGroupName);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    /// <summary>
    /// Resolves a child group of the combat mixer by exact name. A missing mixer or group leaves the
    /// pool routed to the listener, which <see cref="AudioManager"/> tolerates, and is reported so the
    /// mix is never silently flat.
    /// </summary>
    private static AudioMixerGroup FindMixerGroup(string groupName)
    {
        AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
        if (mixer == null)
        {
            Debug.LogError($"[AudioSceneSetup] Combat mixer not found at '{MixerPath}'; '{groupName}' left unrouted.");
            return null;
        }

        foreach (AudioMixerGroup group in mixer.FindMatchingGroups(groupName))
        {
            if (group.name == groupName) return group;
        }

        Debug.LogError($"[AudioSceneSetup] Group '{groupName}' not found in '{MixerPath}'; left unrouted.");
        return null;
    }
}
