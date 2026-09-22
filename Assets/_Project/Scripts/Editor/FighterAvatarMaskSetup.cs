using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the Avatar Masks the aerial attack layers blend through. A jumping normal may not play
/// its ground clip whole: the clip keeps the support leg planted on a floor that is not there, so
/// the silhouette stretches downwards mid-jump. Each mask therefore lets only the striking limbs
/// through and leaves the rest of the body to the base layer, which stays in the jump.
/// Punches need the torso and both arms; a kick additionally needs the leg that actually swings,
/// which is read out of the clip rather than assumed from the button that throws it.
/// </summary>
public static class FighterAvatarMaskSetup
{
    public const string MasksRoot = "Assets/_Project/Art/Animations/Masks";

    private const float SampleStep = 1f / 60f;

    private static readonly AvatarMaskBodyPart[] UpperBodyParts =
    {
        AvatarMaskBodyPart.Body,
        AvatarMaskBodyPart.Head,
        AvatarMaskBodyPart.LeftArm,
        AvatarMaskBodyPart.RightArm,
        AvatarMaskBodyPart.LeftFingers,
        AvatarMaskBodyPart.RightFingers
    };

    /// <summary>
    /// Returns the mask covering the torso, head and arms, plus <paramref name="strikingLeg"/> when
    /// one is given. The asset is created on first use and rewritten in place afterwards, so the
    /// GUID survives a rebuild and the Animator layers keep their reference.
    /// </summary>
    public static AvatarMask LoadOrCreateMask(string name, AvatarMaskBodyPart? strikingLeg)
    {
        if (!AssetDatabase.IsValidFolder(MasksRoot))
            AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(MasksRoot), System.IO.Path.GetFileName(MasksRoot));

        string path = $"{MasksRoot}/{name}.mask";
        AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
        bool created = mask == null;
        if (created) mask = new AvatarMask { name = name };

        for (AvatarMaskBodyPart part = 0; part < AvatarMaskBodyPart.LastBodyPart; part++)
            mask.SetHumanoidBodyPartActive(part, false);

        foreach (AvatarMaskBodyPart part in UpperBodyParts)
            mask.SetHumanoidBodyPartActive(part, true);

        if (strikingLeg.HasValue) mask.SetHumanoidBodyPartActive(strikingLeg.Value, true);

        if (created) AssetDatabase.CreateAsset(mask, path);
        EditorUtility.SetDirty(mask);
        return mask;
    }

    /// <summary>
    /// Reads the striking leg out of a kick clip by sampling it on the reference rig and comparing
    /// how high each foot leaves the floor. Lift separates the two legs far more cleanly than
    /// travelled distance: a kick that steps into its swing drags the support foot along the
    /// ground, but only the striking foot rises. Falls back to the right leg when the rig cannot
    /// be sampled.
    /// </summary>
    public static AvatarMaskBodyPart ResolveStrikingLeg(AnimationClip clip)
    {
        if (clip == null) return AvatarMaskBodyPart.RightLeg;

        GameObject rigAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FighterModelImportSetup.ReferenceAvatarModelPath);
        if (rigAsset == null)
        {
            Debug.LogWarning($"[FighterAvatarMaskSetup] Reference rig not found at '{FighterModelImportSetup.ReferenceAvatarModelPath}'; '{clip.name}' falls back to the right leg.");
            return AvatarMaskBodyPart.RightLeg;
        }

        GameObject rig = (GameObject)PrefabUtility.InstantiatePrefab(rigAsset);
        rig.hideFlags = HideFlags.HideAndDontSave;

        try
        {
            Animator animator = rig.GetComponent<Animator>();
            Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (leftFoot == null || rightFoot == null) return AvatarMaskBodyPart.RightLeg;

            AnimationMode.StartAnimationMode();
            AnimationMode.SampleAnimationClip(rig, clip, 0f);
            float leftFloor = rig.transform.InverseTransformPoint(leftFoot.position).y;
            float rightFloor = rig.transform.InverseTransformPoint(rightFoot.position).y;

            float leftLift = 0f;
            float rightLift = 0f;
            int sampleCount = Mathf.Max(2, Mathf.CeilToInt(clip.length / SampleStep) + 1);

            for (int i = 1; i < sampleCount; i++)
            {
                AnimationMode.SampleAnimationClip(rig, clip, Mathf.Min(i * SampleStep, clip.length));
                leftLift = Mathf.Max(leftLift, rig.transform.InverseTransformPoint(leftFoot.position).y - leftFloor);
                rightLift = Mathf.Max(rightLift, rig.transform.InverseTransformPoint(rightFoot.position).y - rightFloor);
            }

            AvatarMaskBodyPart leg = leftLift > rightLift ? AvatarMaskBodyPart.LeftLeg : AvatarMaskBodyPart.RightLeg;
            Debug.Log($"[FighterAvatarMaskSetup] '{clip.name}' strikes with the {(leg == AvatarMaskBodyPart.LeftLeg ? "left" : "right")} leg (lift L {leftLift:F2} m / R {rightLift:F2} m).");
            return leg;
        }
        finally
        {
            AnimationMode.StopAnimationMode();
            Object.DestroyImmediate(rig);
        }
    }
}
