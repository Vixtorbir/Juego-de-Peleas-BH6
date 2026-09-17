using UnityEngine;

/// <summary>
/// Scene-wide audio service. One-shot SFX are played through a fixed round-robin pool of
/// AudioSources created once at startup, so overlapping hits never cut each other off and no
/// components or clips are allocated during the match. Music runs on its own looping source.
/// Gameplay code only ever calls the semantic methods below; the mapping to clips lives in the
/// <see cref="CombatAudioLibrary"/> asset.
/// </summary>
public class AudioManager : MonoBehaviour
{
    private const int SfxSourceCount = 10;
    private const float DuckedMusicVolumeRatio = 0.55f;
    private const float MusicDuckRecoverySpeed = 0.8f;

    public static AudioManager Instance { get; private set; }

    [SerializeField] private CombatAudioLibrary library;
    [SerializeField] private bool playMusicOnStart = true;
    [Tooltip("Master level for the battle theme. Kept low so the music sits under the hit and block effects.")]
    [SerializeField, Range(0f, 1f)] private float musicMasterVolume = 0.16f;

    private AudioSource[] sfxSources;
    private AudioSource musicSource;
    private int nextSfxSource;
    private float musicVolumeScale = 1f;

    private float MusicBaseVolume => library.musicVolume * musicMasterVolume;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        BuildSources();
    }

    private void Start()
    {
        if (playMusicOnStart) PlayBattleTheme();
    }

    private void Update()
    {
        if (musicSource == null || library == null) return;

        musicVolumeScale = Mathf.MoveTowards(musicVolumeScale, 1f, MusicDuckRecoverySpeed * Time.unscaledDeltaTime);
        musicSource.volume = MusicBaseVolume * musicVolumeScale;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void PlayBattleTheme()
    {
        if (library == null || library.battleTheme == null) return;

        musicSource.clip = library.battleTheme;
        musicSource.volume = MusicBaseVolume;
        musicSource.Play();
    }

    public void StopMusic()
    {
        musicSource.Stop();
    }

    public void PlayWhiff()
    {
        if (library != null) PlayEvent(in library.whiff);
    }

    /// <summary>
    /// Impact sound for an attack that landed, chosen by the attack's impact type and weight.
    /// </summary>
    public void PlayHit(AttackData attack, bool counterHit)
    {
        if (library == null || attack == null) return;

        switch (attack.impactSound)
        {
            case AttackImpactSound.Kick:
                PlayEvent(in library.kickHit);
                break;
            case AttackImpactSound.Energy:
                PlayEvent(in library.hadoukenImpact);
                break;
            default:
                if (attack.weight == AttackWeight.Heavy) PlayEvent(in library.heavyHit);
                else PlayEvent(in library.lightHit);
                break;
        }

        if (counterHit) PlayEvent(in library.counterHit);
    }

    public void PlayBlock()
    {
        if (library != null) PlayEvent(in library.block);
    }

    public void PlayHadoukenCast()
    {
        if (library != null) PlayEvent(in library.hadoukenCast);
    }

    public void PlayHadoukenShot()
    {
        if (library != null) PlayEvent(in library.hadoukenShot);
    }

    public void PlayShoryukenRise()
    {
        if (library != null) PlayEvent(in library.shoryukenRise);
    }

    /// <summary>
    /// Knockout layer: heavy impact plus a tonal sting, with the music ducked underneath.
    /// </summary>
    public void PlayKnockout()
    {
        if (library == null) return;

        PlayEvent(in library.knockoutImpact);
        PlayEvent(in library.knockoutSting);
        musicVolumeScale = DuckedMusicVolumeRatio;
    }

    private void PlayEvent(in CombatAudioLibrary.SoundEvent soundEvent)
    {
        if (!soundEvent.HasClips) return;

        AudioClip clip = soundEvent.variations[Random.Range(0, soundEvent.variations.Length)];
        if (clip == null) return;

        AudioSource source = sfxSources[nextSfxSource];
        nextSfxSource = (nextSfxSource + 1) % sfxSources.Length;

        source.pitch = 1f + Random.Range(-soundEvent.pitchSpread, soundEvent.pitchSpread);
        source.PlayOneShot(clip, soundEvent.volume);
    }

    private void BuildSources()
    {
        sfxSources = new AudioSource[SfxSourceCount];
        for (int i = 0; i < SfxSourceCount; i++)
        {
            sfxSources[i] = CreateSource("SFX Source", false);
        }

        musicSource = CreateSource("Music Source", true);
    }

    private AudioSource CreateSource(string name, bool loop)
    {
        var holder = new GameObject(name);
        holder.transform.SetParent(transform, false);

        AudioSource source = holder.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;
        source.ignoreListenerPause = true;
        return source;
    }
}
