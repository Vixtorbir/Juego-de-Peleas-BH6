using UnityEngine;
using UnityEngine.Audio;

public class AudioManager : MonoBehaviour
{
    private const int SfxSourceCount = 10;
    private const float DuckedMusicVolumeRatio = 0.55f;
    private const float MusicDuckRecoverySpeed = 0.8f;
    private const float ImpactDuckAttack = 0.04f;
    private const float ImpactDuckHold = 0.12f;
    private const float ImpactDuckRelease = 0.35f;
    private const float KnockoutDuckHold = 1.6f;
    private const float KnockoutDuckRelease = 0.8f;
    private const float HeavyThumpVolumeRatio = 0.35f;
    private const float DeflectFallbackPitch = 0.35f;
    private const float DeflectFallbackVolume = 0.6f;

    public static AudioManager Instance { get; private set; }

    [SerializeField] private CombatAudioLibrary library;

    [Header("Mixer Routing")]
    [Tooltip("Output group for every one-shot combat effect. Optional; unrouted sources play straight to the listener.")]
    [SerializeField] private AudioMixerGroup sfxMixerGroup;
    [Tooltip("Output group for the battle theme. Optional; unrouted sources play straight to the listener.")]
    [SerializeField] private AudioMixerGroup musicMixerGroup;
    [Tooltip("Resting mix. Optional; with no snapshots the music source volume is ducked directly.")]
    [SerializeField] private AudioMixerSnapshot defaultSnapshot;
    [Tooltip("Mix during a heavy impact: music pulled under the hit for a few frames.")]
    [SerializeField] private AudioMixerSnapshot impactSnapshot;
    [Tooltip("Mix during the knockout beat.")]
    [SerializeField] private AudioMixerSnapshot knockoutSnapshot;

    [Header("Music")]
    [SerializeField] private bool playMusicOnStart = true;
    [Tooltip("Master level for the battle theme. Kept low so the music sits under the hit and block effects.")]
    [SerializeField, Range(0f, 1f)] private float musicMasterVolume = 0.16f;

    private AudioSource[] sfxSources;
    private AudioSource musicSource;
    private int nextSfxSource;
    private float musicVolumeScale = 1f;
    private float duckHoldRemaining;
    private float duckRelease;
    private bool duckActive;

    private bool HasSnapshots => defaultSnapshot != null && impactSnapshot != null && knockoutSnapshot != null;

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

        if (!duckActive) return;

        duckHoldRemaining -= Time.unscaledDeltaTime;
        if (duckHoldRemaining > 0f) return;

        duckActive = false;
        defaultSnapshot.TransitionTo(duckRelease);
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

        if (attack.weight == AttackWeight.Heavy || attack.isLauncher || counterHit)
        {
            PlayEvent(in library.knockoutImpact, HeavyThumpVolumeRatio);
            DuckMusic(impactSnapshot, ImpactDuckAttack, ImpactDuckHold, ImpactDuckRelease);
        }
    }

    public void PlayBlock()
    {
        if (library != null) PlayEvent(in library.block);
    }

    public void PlayDeflect()
    {
        if (library == null) return;

        if (library.deflect.HasClips) PlayEvent(in library.deflect);
        else PlayEvent(in library.block, DeflectFallbackVolume, DeflectFallbackPitch);
    }

    public void PlayBounce()
    {
        if (library == null) return;

        PlayEvent(in library.heavyHit);
        DuckMusic(impactSnapshot, ImpactDuckAttack, ImpactDuckHold, ImpactDuckRelease);
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

    public void PlayKnockout()
    {
        if (library == null) return;

        PlayEvent(in library.knockoutImpact);
        PlayEvent(in library.knockoutSting);

        if (HasSnapshots) DuckMusic(knockoutSnapshot, ImpactDuckAttack, KnockoutDuckHold, KnockoutDuckRelease);
        else musicVolumeScale = DuckedMusicVolumeRatio;
    }

    // A duck that is already holding just gets extended.
    private void DuckMusic(AudioMixerSnapshot target, float attack, float hold, float release)
    {
        if (!HasSnapshots) return;

        target.TransitionTo(attack);
        duckHoldRemaining = Mathf.Max(duckHoldRemaining, hold);
        duckRelease = release;
        duckActive = true;
    }

    private void PlayEvent(in CombatAudioLibrary.SoundEvent soundEvent, float volumeScale = 1f, float pitchOffset = 0f)
    {
        if (!soundEvent.HasClips) return;

        AudioClip clip = soundEvent.variations[Random.Range(0, soundEvent.variations.Length)];
        if (clip == null) return;

        AudioSource source = sfxSources[nextSfxSource];
        nextSfxSource = (nextSfxSource + 1) % sfxSources.Length;

        source.pitch = 1f + pitchOffset + Random.Range(-soundEvent.pitchSpread, soundEvent.pitchSpread);
        source.PlayOneShot(clip, soundEvent.volume * volumeScale);
    }

    private void BuildSources()
    {
        sfxSources = new AudioSource[SfxSourceCount];
        for (int i = 0; i < SfxSourceCount; i++)
        {
            sfxSources[i] = CreateSource("SFX Source", false, sfxMixerGroup);
        }

        musicSource = CreateSource("Music Source", true, musicMixerGroup);
    }

    private AudioSource CreateSource(string name, bool loop, AudioMixerGroup outputGroup)
    {
        var holder = new GameObject(name);
        holder.transform.SetParent(transform, false);

        AudioSource source = holder.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;
        source.ignoreListenerPause = true;
        if (outputGroup != null) source.outputAudioMixerGroup = outputGroup;
        return source;
    }
}
