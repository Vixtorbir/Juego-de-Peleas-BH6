using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Derives frame data from the attack clips instead of forcing clips onto hand-typed frame data.
/// Each attack clip is sampled on the reference fighter rig to find when the striking limb
/// reaches full extension; the clip is then trimmed so its wind-up starts just before the strike,
/// and the <see cref="AttackData"/> startup, active and recovery are written to match the motion
/// at natural playback speed. Startup and recovery are capped per attack weight: a clip with a
/// long wind-up is trimmed further into its swing rather than sped up, and the entry and
/// return-to-stance cross-fades hide both cuts.
/// </summary>
public static class AttackTimingCalibrator
{
    private const string AttackDataRoot = "Assets/_Project/Attacks";
    private const float SampleStep = 1f / 60f;
    private const float WindupThreshold = 0.25f;
    private const float RetractThreshold = 0.5f;
    private const float WindupLead = 0.12f;
    private const float ContactLead = 0.033f;
    private const float ContactThreshold = 0.85f;
    private const float LauncherContactThreshold = 0.55f;
    private const float FullExtensionThreshold = 1f;
    private const float RisingStartup = 0.05f;
    private const float RisingMinActive = 0.15f;
    private const float RisingMaxActive = 0.3f;
    private const float MinActiveDuration = 0.083f;
    private const float MaxActiveDuration = 0.25f;
    private const float MinRecovery = 0.2f;
    private const float RisingReachWeight = 0.8f;
    private const float RisingReachHeight = 1.25f;

    private static readonly float[] RecoveryCapByWeight = { 0.22f, 0.32f, 0.42f };
    private static readonly float[] StartupCapByWeight = { 0.22f, 0.33f, 0.45f };
    private const float SpecialStartupCap = 0.5f;
    private const int MaxTrimPasses = 2;

    private static readonly HumanBodyBones[] StrikingLimbs =
    {
        HumanBodyBones.LeftHand,
        HumanBodyBones.RightHand,
        HumanBodyBones.LeftFoot,
        HumanBodyBones.RightFoot
    };

    private readonly struct ClipProfile
    {
        public readonly float WindupTime;
        public readonly float ContactTime;
        public readonly float PeakTime;
        public readonly float RetractTime;
        public readonly float RestReach;
        public readonly float PeakReach;

        public ClipProfile(float windupTime, float contactTime, float peakTime, float retractTime, float restReach, float peakReach)
        {
            WindupTime = windupTime;
            ContactTime = contactTime;
            PeakTime = peakTime;
            RetractTime = retractTime;
            RestReach = restReach;
            PeakReach = peakReach;
        }
    }

    [MenuItem("Fighting Game/Calibrate Attack Timing", priority = 25)]
    public static void CalibrateAttackTiming()
    {
        GameObject rigAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FighterModelImportSetup.ReferenceAvatarModelPath);
        if (rigAsset == null)
        {
            Debug.LogError($"[AttackTimingCalibrator] Reference rig not found at '{FighterModelImportSetup.ReferenceAvatarModelPath}'.");
            return;
        }

        GameObject rig = (GameObject)PrefabUtility.InstantiatePrefab(rigAsset);
        rig.hideFlags = HideFlags.HideAndDontSave;
        Animator animator = rig.GetComponent<Animator>();
        var report = new StringBuilder("[AttackTimingCalibrator] Frame data derived from clips:\n");

        try
        {
            AnimationMode.StartAnimationMode();

            foreach (string guid in AssetDatabase.FindAssets("t:AttackData", new[] { AttackDataRoot }))
            {
                AttackData attack = AssetDatabase.LoadAssetAtPath<AttackData>(AssetDatabase.GUIDToAssetPath(guid));
                if (attack == null || attack.animationClip == null) continue;

                Calibrate(attack, rig, animator, report);
            }
        }
        finally
        {
            AnimationMode.StopAnimationMode();
            Object.DestroyImmediate(rig);
        }

        AssetDatabase.SaveAssets();
        Debug.Log(report.ToString());
    }

    private static void Calibrate(AttackData attack, GameObject rig, Animator animator, StringBuilder report)
    {
        float speed = Mathf.Max(attack.animationSpeed, 1f);
        float startupCap = attack.specialCancelable ? StartupCapByWeight[(int)attack.weight] : SpecialStartupCap;

        float contactThreshold = SelectContactThreshold(attack);
        ClipProfile profile = Profile(attack.animationClip, rig, animator, contactThreshold);
        float startup = ComputeStartup(attack, profile, speed);

        for (int pass = 0; pass < MaxTrimPasses; pass++)
        {
            float windupTrim = Mathf.Max(0f, profile.WindupTime - WindupLead);
            float capTrim = Mathf.Max(0f, (startup - startupCap) * speed);
            float trim = Mathf.Max(windupTrim, capTrim);
            if (trim <= SampleStep || !TrimClipStart(attack.animationClip, trim)) break;

            attack.animationClip = ReloadClip(attack.animationClip);
            profile = Profile(attack.animationClip, rig, animator, contactThreshold);
            startup = ComputeStartup(attack, profile, speed);
        }

        float active = ComputeActive(attack, profile, startup, speed);
        float naturalRecovery = (profile.RetractTime - profile.PeakTime) / speed;
        float recovery = Mathf.Clamp(naturalRecovery, MinRecovery, RecoveryCapByWeight[(int)attack.weight]);

        attack.startupTime = Round(startup);
        attack.activeTime = Round(active);
        attack.recoveryTime = Round(recovery);
        EditorUtility.SetDirty(attack);

        report.AppendLine($"  {attack.name,-16} clip {attack.animationClip.name,-16} {attack.animationClip.length:F2}s  " +
                          $"contact {profile.ContactTime:F2}s peak {profile.PeakTime:F2}s (reach {profile.RestReach:F2}->{profile.PeakReach:F2} m)  " +
                          $"startup {attack.startupTime:F3}  active {attack.activeTime:F3}  recovery {attack.recoveryTime:F3}");
    }

    private static float SelectContactThreshold(AttackData attack)
    {
        switch (attack.strikeTiming)
        {
            case StrikeTiming.FullExtension: return FullExtensionThreshold;
            case StrikeTiming.Rising: return FullExtensionThreshold;
            default: return attack.isLauncher ? LauncherContactThreshold : ContactThreshold;
        }
    }

    private static float ComputeStartup(AttackData attack, ClipProfile profile, float speed)
    {
        if (attack.strikeTiming == StrikeTiming.Rising) return RisingStartup;
        return Mathf.Max(SampleStep, (profile.ContactTime - ContactLead) / speed);
    }

    private static float ComputeActive(AttackData attack, ClipProfile profile, float startup, float speed)
    {
        if (attack.strikeTiming == StrikeTiming.Rising)
            return Mathf.Clamp(profile.PeakTime / speed - startup + ContactLead, RisingMinActive, RisingMaxActive);

        return Mathf.Clamp((profile.PeakTime - profile.ContactTime + ContactLead) / speed, MinActiveDuration, MaxActiveDuration);
    }

    /// <summary>
    /// Samples the clip on the rig and tracks the furthest striking limb. Reach is the limb's
    /// distance in front of the root plus a share of its height above the shoulder line, so
    /// uppercuts register their rising fist as well as straight punches and kicks. Contact is the
    /// first frame the limb crosses <paramref name="contactThreshold"/> of its full travel: near
    /// full extension for straight strikes, mid-rise for launchers whose fist sweeps through the target.
    /// </summary>
    private static ClipProfile Profile(AnimationClip clip, GameObject rig, Animator animator, float contactThreshold)
    {
        int sampleCount = Mathf.Max(2, Mathf.CeilToInt(clip.length / SampleStep) + 1);
        var reach = new List<float>(sampleCount);

        for (int i = 0; i < sampleCount; i++)
        {
            float time = Mathf.Min(i * SampleStep, clip.length);
            AnimationMode.SampleAnimationClip(rig, clip, time);
            reach.Add(MeasureReach(rig.transform, animator));
        }

        float rest = reach[0];
        float peak = rest;
        int peakIndex = 0;
        for (int i = 1; i < reach.Count; i++)
        {
            if (reach[i] <= peak) continue;
            peak = reach[i];
            peakIndex = i;
        }

        float windupLevel = rest + (peak - rest) * WindupThreshold;
        int windupIndex = 0;
        for (int i = 0; i < peakIndex; i++)
        {
            if (reach[i] < windupLevel) continue;
            windupIndex = i;
            break;
        }

        float contactLevel = rest + (peak - rest) * contactThreshold;
        int contactIndex = peakIndex;
        for (int i = windupIndex; i <= peakIndex; i++)
        {
            if (reach[i] < contactLevel) continue;
            contactIndex = i;
            break;
        }

        float retractLevel = rest + (peak - rest) * RetractThreshold;
        int retractIndex = reach.Count - 1;
        for (int i = peakIndex + 1; i < reach.Count; i++)
        {
            if (reach[i] > retractLevel) continue;
            retractIndex = i;
            break;
        }

        return new ClipProfile(windupIndex * SampleStep, contactIndex * SampleStep, peakIndex * SampleStep, retractIndex * SampleStep, rest, peak);
    }

    private static float MeasureReach(Transform root, Animator animator)
    {
        float best = float.MinValue;

        foreach (HumanBodyBones bone in StrikingLimbs)
        {
            Transform limb = animator.GetBoneTransform(bone);
            if (limb == null) continue;

            Vector3 local = root.InverseTransformPoint(limb.position);
            float rising = Mathf.Max(0f, local.y - RisingReachHeight) * RisingReachWeight;
            best = Mathf.Max(best, local.z + rising);
        }

        return best;
    }

    private static bool TrimClipStart(AnimationClip clip, float trimSeconds)
    {
        string path = AssetDatabase.GetAssetPath(clip);
        if (AssetImporter.GetAtPath(path) is not ModelImporter importer) return false;

        ModelImporterClipAnimation[] clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
        bool changed = false;

        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i].name != clip.name) continue;

            float frame = clips[i].firstFrame + trimSeconds * clip.frameRate;
            if (frame >= clips[i].lastFrame - 1f) continue;

            clips[i].firstFrame = Mathf.Floor(frame);
            changed = true;
        }

        if (!changed) return false;

        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        return true;
    }

    private static AnimationClip ReloadClip(AnimationClip clip)
    {
        string path = AssetDatabase.GetAssetPath(clip);
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is AnimationClip reloaded && reloaded.name == clip.name) return reloaded;
        }

        return clip;
    }

    private static float Round(float seconds) => Mathf.Round(seconds * 1000f) / 1000f;
}
