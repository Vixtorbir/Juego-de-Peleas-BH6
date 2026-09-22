using UnityEngine;

[CreateAssetMenu(fileName = "CombatAudioLibrary", menuName = "Fighting Game/Combat Audio Library")]
public class CombatAudioLibrary : ScriptableObject
{
    [System.Serializable]
    public struct SoundEvent
    {
        public AudioClip[] variations;
        [Range(0f, 1f)] public float volume;
        [Range(0f, 0.5f)] public float pitchSpread;

        public bool HasClips => variations != null && variations.Length > 0;
    }

    [Header("Normals")]
    public SoundEvent whiff;
    public SoundEvent lightHit;
    public SoundEvent heavyHit;
    public SoundEvent kickHit;
    public SoundEvent block;

    [Header("Specials")]
    public SoundEvent hadoukenCast;
    public SoundEvent hadoukenShot;
    public SoundEvent hadoukenImpact;
    public SoundEvent shoryukenRise;

    [Header("System")]
    public SoundEvent counterHit;
    [Tooltip("Strike deflected by an invulnerable or downed body. Without clips the block layer plays pitched up.")]
    public SoundEvent deflect;
    public SoundEvent knockoutImpact;
    public SoundEvent knockoutSting;

    [Header("Music")]
    public AudioClip battleTheme;
    [Tooltip("Per-track trim. The overall music level is set by AudioManager.")]
    [Range(0f, 1f)] public float musicVolume = 1f;
}
