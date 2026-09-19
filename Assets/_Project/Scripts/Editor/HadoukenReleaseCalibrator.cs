using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Locks the projectile release to the animation instead of the other way round. The Hadouken clip
/// is sampled on the reference rig to find the frame where the hands arrive at full extension;
/// that time becomes the attack's startup, the remaining clip becomes its follow-through, and the
/// palm position on that frame becomes the projectile anchor on every fighter, so the energy ball
/// leaves the hands at the instant they lock out. Arrival is read as the first frame covering
/// <see cref="ArrivalThreshold"/> of the hands' travel: a thrust decelerates into a plateau where the
/// pose settles by millimetres for several frames, and keying on the mathematical peak of that
/// plateau releases the projectile visibly after the arms have stopped.
/// </summary>
public static class HadoukenReleaseCalibrator
{
    private const string HadoukenAssetPath = "Assets/_Project/Attacks/SO_Hadouken.asset";
    private const float SampleStep = 1f / 60f;
    private const float ArrivalThreshold = 0.97f;
    private const float MinRecovery = 0.2f;
    private const float MaxRecovery = 0.42f;
    private const float MinFollowThrough = 0.2f;

    /// <summary>Measured release pose of the projectile clip, in fighter root space.</summary>
    public readonly struct ReleaseProfile
    {
        /// <summary>Clip time at which the hands arrive at full extension.</summary>
        public readonly float ReleaseTime;

        /// <summary>Midpoint between both palms on the release frame, relative to the fighter root.</summary>
        public readonly Vector3 PalmAnchor;

        /// <summary>Playable length of the clip at authored speed.</summary>
        public readonly float ClipLength;

        public ReleaseProfile(float releaseTime, Vector3 palmAnchor, float clipLength)
        {
            ReleaseTime = releaseTime;
            PalmAnchor = palmAnchor;
            ClipLength = clipLength;
        }
    }

    [MenuItem("Fighting Game/Calibrate Hadouken Release", priority = 26)]
    public static void CalibrateHadoukenRelease()
    {
        AttackData hadouken = AssetDatabase.LoadAssetAtPath<AttackData>(HadoukenAssetPath);
        if (hadouken == null || hadouken.animationClip == null)
        {
            Debug.LogError($"[HadoukenReleaseCalibrator] '{HadoukenAssetPath}' is missing or has no clip.");
            return;
        }

        var report = new StringBuilder();
        if (!TryMeasureRelease(hadouken, report, out ReleaseProfile profile))
        {
            Debug.LogError(report.ToString());
            return;
        }

        float speed = Mathf.Max(hadouken.animationSpeed, 1f);
        float startup = profile.ReleaseTime / speed;
        float followThrough = (profile.ClipLength - profile.ReleaseTime) / speed - hadouken.activeTime;

        hadouken.startupTime = Round(startup);
        hadouken.recoveryTime = Round(Mathf.Clamp(followThrough, MinRecovery, MaxRecovery));
        EditorUtility.SetDirty(hadouken);
        AssetDatabase.SaveAssets();

        if (followThrough < MinFollowThrough)
            Debug.LogWarning($"[HadoukenReleaseCalibrator] Clip ends {followThrough:F3}s after release; recovery is padded to {hadouken.recoveryTime:F3}s.");

        int aligned = AlignProjectileSpawnPoints(profile.PalmAnchor);

        report.AppendLine($"  release {profile.ReleaseTime:F3}s -> startup {hadouken.startupTime:F3}  active {hadouken.activeTime:F3}  recovery {hadouken.recoveryTime:F3}  (clip {profile.ClipLength:F3}s)");
        report.AppendLine($"  palm anchor {profile.PalmAnchor} (fighter space) applied to {aligned} spawn point(s)");
        Debug.Log(report.ToString());
    }

    /// <summary>
    /// Moves every fighter's projectile spawn point in the training scene to the measured palm
    /// anchor. The spawn objects are kept, only repositioned, so the controller references hold.
    /// </summary>
    private static int AlignProjectileSpawnPoints(Vector3 palmAnchor)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != FighterSceneSetup.ScenePath)
            scene = EditorSceneManager.OpenScene(FighterSceneSetup.ScenePath, OpenSceneMode.Single);

        int aligned = 0;
        foreach (RobotController fighter in Object.FindObjectsByType<RobotController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (fighter.hadoukenSpawnPoint == null) continue;

            fighter.hadoukenSpawnPoint.SetLocalPositionAndRotation(palmAnchor, Quaternion.identity);
            EditorUtility.SetDirty(fighter.hadoukenSpawnPoint);
            aligned++;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return aligned;
    }

    /// <summary>
    /// Read-only audit: logs the per-frame hand extension and the release frame without touching
    /// the attack data, so a clip change can be inspected before recalibrating.
    /// </summary>
    [MenuItem("Fighting Game/Report Hadouken Release", priority = 27)]
    public static void ReportHadoukenRelease()
    {
        AttackData hadouken = AssetDatabase.LoadAssetAtPath<AttackData>(HadoukenAssetPath);
        if (hadouken == null || hadouken.animationClip == null)
        {
            Debug.LogError($"[HadoukenReleaseCalibrator] '{HadoukenAssetPath}' is missing or has no clip.");
            return;
        }

        var report = new StringBuilder();
        if (!TryMeasureRelease(hadouken, report, out ReleaseProfile profile))
        {
            Debug.LogError(report.ToString());
            return;
        }

        report.AppendLine($"  release {profile.ReleaseTime:F3}s of {profile.ClipLength:F3}s; current startup {hadouken.startupTime:F3} active {hadouken.activeTime:F3} recovery {hadouken.recoveryTime:F3}");
        report.AppendLine($"  palm anchor {profile.PalmAnchor} (fighter space)");
        Debug.Log(report.ToString());
    }

    /// <summary>
    /// Samples the clip on the reference rig and returns the release frame and palm anchor. The
    /// report receives one line per sampled frame so the release choice can be audited against the
    /// motion. Returns false when the rig or a hand bone is unavailable.
    /// </summary>
    public static bool TryMeasureRelease(AttackData hadouken, StringBuilder report, out ReleaseProfile profile)
    {
        profile = default;
        report.AppendLine("[HadoukenReleaseCalibrator] Hand extension per frame (fighter space, metres):");

        GameObject rigAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FighterModelImportSetup.ReferenceAvatarModelPath);
        if (rigAsset == null)
        {
            report.AppendLine($"  reference rig not found at '{FighterModelImportSetup.ReferenceAvatarModelPath}'.");
            return false;
        }

        GameObject rig = (GameObject)PrefabUtility.InstantiatePrefab(rigAsset);
        rig.hideFlags = HideFlags.HideAndDontSave;

        try
        {
            Animator animator = rig.GetComponent<Animator>();
            Transform leftHand = animator != null ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
            Transform rightHand = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
            if (leftHand == null || rightHand == null)
            {
                report.AppendLine("  reference rig has no humanoid hand bones.");
                return false;
            }

            AnimationClip clip = hadouken.animationClip;
            int sampleCount = Mathf.Max(2, Mathf.CeilToInt(clip.length / SampleStep) + 1);
            var reach = new float[sampleCount];
            var anchors = new Vector3[sampleCount];

            AnimationMode.StartAnimationMode();
            try
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    float time = Mathf.Min(i * SampleStep, clip.length);
                    AnimationMode.SampleAnimationClip(rig, clip, time);

                    Vector3 left = rig.transform.InverseTransformPoint(leftHand.position);
                    Vector3 right = rig.transform.InverseTransformPoint(rightHand.position);
                    reach[i] = Mathf.Max(left.z, right.z);
                    anchors[i] = (left + right) * 0.5f;

                    report.AppendLine($"  f{i:D2} t={time:F3}  L(z {left.z:F3} y {left.y:F3})  R(z {right.z:F3} y {right.y:F3})  reach {reach[i]:F3}");
                }
            }
            finally
            {
                AnimationMode.StopAnimationMode();
            }

            int releaseIndex = FindArrivalIndex(reach);
            Vector3 palm = anchors[releaseIndex];
            profile = new ReleaseProfile(releaseIndex * SampleStep, new Vector3(0f, palm.y, palm.z), clip.length);
            return true;
        }
        finally
        {
            Object.DestroyImmediate(rig);
        }
    }

    /// <summary>
    /// First sample at which the hands have covered <see cref="ArrivalThreshold"/> of their travel
    /// from the rest pose to the furthest extension; falls back to the peak itself for a clip
    /// that never settles.
    /// </summary>
    private static int FindArrivalIndex(float[] reach)
    {
        float rest = reach[0];
        float peak = rest;
        int peakIndex = 0;

        for (int i = 1; i < reach.Length; i++)
        {
            if (reach[i] <= peak) continue;
            peak = reach[i];
            peakIndex = i;
        }

        float arrivalLevel = rest + (peak - rest) * ArrivalThreshold;
        for (int i = 0; i <= peakIndex; i++)
        {
            if (reach[i] >= arrivalLevel) return i;
        }

        return peakIndex;
    }

    private static float Round(float seconds) => Mathf.Round(seconds * 1000f) / 1000f;
}
