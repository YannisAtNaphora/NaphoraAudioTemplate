using System.Collections.Generic;
using System.Linq;
using System.Text;
using DarkTonic.MasterAudio;
using UnityEngine;
using UnityEngine.Audio;
using Naphora.AudioKit.Core;
using Naphora.AudioKit.Core.Config;

/// <summary>
/// Single entry point for all audio calls in the game.
/// Engineers must never call Master Audio directly — use this class only.
///
/// Bootstraps automatically at runtime via [RuntimeInitializeOnLoadMethod].
/// Survives scene loads (DontDestroyOnLoad on the hidden AudioManagerBehaviour).
///
/// <code>
/// // One-shot SFX.
/// AudioManager.Trigger(AudioConfig.Sfx_CoinPickup);
///
/// // One-shot with a pitch set once at the moment it plays.
/// AudioManager.TriggerWithPitch(AudioConfig.Sfx_EggShot, pitch: 1.1f);
///
/// // A loop whose pitch needs to keep changing after it starts (an engine hum
/// // climbing with speed, say) -- TriggerWithPitch only sets pitch once at the
/// // start, so a loop instance is what SetInstancePitch retunes later.
/// int engineLoopId = AudioManager.PlayLoop(AudioConfig.Sfx_EngineHum);
/// // ...each frame, or whenever speed changes:
/// AudioManager.SetInstancePitch(engineLoopId, Mathf.Lerp(0.8f, 1.6f, speed01));
/// // ...when done:
/// AudioManager.StopInstance(engineLoopId);
/// </code>
/// </summary>
public static class AudioManager
{
    // Name of the PlaylistController GameObject expected in the AudioSetup prefab,
    // used by the simple single-controller TriggerMusic()/StopMusic() path.
    private const string MusicControllerName = AudioConfig.MusicControllerName;

    // PlayerPrefs keys for persistence.
    private const string PrefKeyMusic       = "MusicEnabled";
    private const string PrefKeySound       = "SoundEnabled";
    private const string PrefKeySFXVolume   = "SFXVolume";
    private const string PrefKeyMusicVolume = "MusicVolume";

    // Shipped default, 0-1. Matches AudioDefaults.Music in Wisp/IAudioService.cs --
    // the two systems' volumes aren't wired together yet, but a fresh install should
    // still open at the same level regardless of which one ends up driving playback.
    private const float DefaultMusicVolume = 0.7f;

    // Loop instance tracking: wrapper ID → MA variation reference.
    private static int _nextInstanceId = 1;
    private static readonly Dictionary<int, SoundGroupVariation> _loopInstances
        = new Dictionary<int, SoundGroupVariation>();

    // Throttle counters for TriggerThrottled, keyed by caller-supplied throttleKey.
    private static readonly Dictionary<string, int> _throttleCounts = new Dictionary<string, int>();

    // Unity Audio Mixer — assigned via AudioManagerBehaviour inspector.
    private static AudioMixer _audioMixer;

    // Mix state — direct snapshot references (assigned via AudioManagerBehaviour
    // Inspector, drag-and-drop from the AudioMixer window) and from→to transition timing.
    private static Dictionary<MixState, AudioMixerSnapshot> _mixStateSnapshotRefs = new Dictionary<MixState, AudioMixerSnapshot>();
    private static MixState _currentMixState = MixState.Game;
    private static List<MixStateTransition> _mixStateTransitions = new List<MixStateTransition>();
    private static float _defaultMixTransitionTime = 0.25f;

    // Interruption stack for PushMixState/PopMixState (ads, and anything else that
    // ducks the mix temporarily without caring what was playing underneath).
    private static readonly Stack<MixState> _mixStateStack = new Stack<MixState>();

    // Ambiance volume settings — assigned via AudioManagerBehaviour inspector.
    private static float _ambianceVolumeWithMusic = 0.3f;
    private static float _ambianceVolumeWithoutMusic = 1f;

#if UNITY_EDITOR
    // ── Audio coverage tracking (Editor only) ───────────────────────────
    // Tracks which sound IDs played successfully vs. which are missing their
    // MasterAudio setup during this play session. Dumped on stop via
    // DumpAudioCoverageReport.
    private static readonly HashSet<string> _playedSoundIDs = new HashSet<string>();
    private static readonly HashSet<string> _missingSoundIDs = new HashSet<string>();
#endif

    // ── Bootstrap ─────────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (Object.FindObjectOfType<AudioManagerBehaviour>() == null)
        {
            var go = new GameObject("[AudioManager]");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<AudioManagerBehaviour>();
        }

        // DO NOT instantiate MasterAudio - it should already be in the scene
        // The scene's MasterAudio prefab has all the sound groups configured
#if UNITY_EDITOR
        if (Object.FindObjectOfType<MasterAudio>() == null)
        {
            Debug.LogWarning("[AudioManager] No MasterAudio found in scene! Make sure your scene has a MasterAudio prefab with sound groups configured.");
        }

        _playedSoundIDs.Clear();
        _missingSoundIDs.Clear();
        Application.quitting -= DumpAudioCoverageReport;
        Application.quitting += DumpAudioCoverageReport;
#endif
    }

    // ── SFX — Trigger ─────────────────────────────────────────────────────

    /// <summary>Plays a 2D (non-positional) sound. Use for UI, rewards, feedback.</summary>
    public static void Trigger(string soundID)
    {
        if (!Validate(soundID)) return;

        var result = MasterAudio.PlaySound(soundID);
        HandleResult(result, soundID);
    }

    /// <summary>Plays a 2D sound at a specific volume (0–1). Used for staggered chord tones.</summary>
    public static void TriggerWithVolume(string soundID, float volume)
    {
        if (!Validate(soundID)) return;

        var result = MasterAudio.PlaySound(soundID, volume);
        HandleResult(result, soundID);
    }

    /// <summary>
    /// Plays a 2D sound at a specific pitch, e.g. combo-driven pitch scaling.
    /// pitch is an absolute multiplier (1 = unmodified); volume is 0–1.
    /// </summary>
    public static void TriggerWithPitch(string soundID, float pitch, float volume = 1f)
    {
        if (!Validate(soundID)) return;

        var result = MasterAudio.PlaySound(soundID, volume, pitch);
        HandleResult(result, soundID);
    }

    /// <summary>Plays a sound at a world-space position.</summary>
    public static void Trigger(string soundID, Vector3 position)
    {
        if (!Validate(soundID)) return;

        var result = MasterAudio.PlaySound3DAtVector3(soundID, position);
        HandleResult(result, soundID);
    }

    /// <summary>Plays a sound that follows a transform.</summary>
    public static void Trigger(string soundID, Transform followTarget)
    {
        if (!Validate(soundID)) return;

        if (followTarget == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"[Audio] \"{soundID}\" — followTarget is null.");
#endif
            return;
        }

        var result = MasterAudio.PlaySound3DFollowTransform(soundID, followTarget);
        HandleResult(result, soundID);
    }

    /// <summary>
    /// Plays step N (1-based) of an ordered sound sequence, e.g. a combo ladder
    /// that pitches upward as a chain builds. The Sound Group triggered is
    /// "{sequenceID}_{step:D2}", clamped to [1, maxStep] so an out-of-range
    /// step (e.g. a combo that keeps climbing past the authored ladder) still
    /// plays the top step instead of failing silently.
    /// </summary>
    public static void TriggerStep(string sequenceID, int step, int maxStep, float volume = 1f)
    {
        if (!Validate(sequenceID)) return;
        if (maxStep < 1)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"[Audio] TriggerStep(\"{sequenceID}\") — maxStep must be >= 1.");
#endif
            return;
        }

        int clamped = Mathf.Clamp(step, 1, maxStep);
        string soundID = $"{sequenceID}_{clamped:D2}";
        TriggerWithVolume(soundID, volume);
    }

    /// <summary>
    /// Plays soundID for the first <paramref name="initial"/> occurrences under
    /// <paramref name="throttleKey"/>, then only every (skip + 1)th occurrence
    /// after that — an anti-spam guard for rapid repeated triggers such as
    /// stacked pickups. Call <see cref="ResetThrottle"/> when the triggering
    /// context ends (e.g. a combo resets) so the count doesn't leak into the
    /// next run.
    /// </summary>
    public static void TriggerThrottled(string soundID, string throttleKey, int initial = 3, int skip = 2)
    {
        if (!Validate(soundID)) return;

        _throttleCounts.TryGetValue(throttleKey, out int count);
        count++;
        _throttleCounts[throttleKey] = count;

        bool shouldPlay = count <= initial || (count - initial) % (skip + 1) == 0;
        if (shouldPlay) Trigger(soundID);
    }

    /// <summary>Resets a throttle counter used by <see cref="TriggerThrottled"/>.</summary>
    public static void ResetThrottle(string throttleKey)
    {
        _throttleCounts.Remove(throttleKey);
    }

    // ── Music ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts a music clip by name on the single Music PlaylistController
    /// named AudioConfig.MusicControllerName. Crossfade duration is set on
    /// the PlaylistController in the AudioSetup prefab.
    /// For a menu/game/level split with crossfading, use PlayMenuMusic() /
    /// PlayGameMusic() / PlayLevelMusic() / PlayEndGameMusic() instead, which
    /// route through NaphoraMusicManager.
    /// </summary>
    public static void TriggerMusic(string trackID)
    {
        if (!Validate(trackID)) return;

        var controller = PlaylistController.InstanceByName(MusicControllerName);
        if (controller == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"[Audio] PlaylistController \"{MusicControllerName}\" not found. Add it to the scene.");
#endif
            return;
        }

        controller.TriggerPlaylistClip(trackID);
    }

    /// <summary>
    /// Hard-stops both NaphoraMusicManager controllers (menu and game) immediately,
    /// no fade. For a screen that must be silent underneath -- the first-launch
    /// cutscene, say -- where a MixState duck alone can't be trusted: it only silences
    /// whatever bus faders that snapshot happens to override, and the menu playlist's
    /// own auto-start (startPlaylistOnAwake) can beat any snapshot transition to it.
    /// </summary>
    public static void StopAllMusicImmediate() => NaphoraMusicManager.Instance?.StopAllImmediate();

    /// <summary>Stops music on the single Music PlaylistController with a fade-out.</summary>
    public static void StopMusic(float fadeOut = 0.5f)
    {
        var controller = PlaylistController.InstanceByName(MusicControllerName);
        if (controller == null) return;

        if (fadeOut > 0f)
            controller.FadeToVolume(0f, fadeOut, () => controller.StopPlaylist());
        else
            controller.StopPlaylist();
    }

    /// <summary>Crossfades to menu music. Requires a NaphoraMusicManager in the scene.</summary>
    public static void PlayMenuMusic() => NaphoraMusicManager.Instance?.PlayMenuMusic();

    /// <summary>Crossfades to default game music. Requires a NaphoraMusicManager in the scene.</summary>
    public static void PlayGameMusic() => NaphoraMusicManager.Instance?.PlayGameMusic();

    /// <summary>Crossfades to the playlist mapped to levelNumber. Requires a NaphoraMusicManager in the scene.</summary>
    public static void PlayLevelMusic(int levelNumber) => NaphoraMusicManager.Instance?.PlayLevelMusic(levelNumber);

    /// <summary>Crossfades to the "EndGame" playlist. Requires a NaphoraMusicManager in the scene.</summary>
    public static void PlayEndGameMusic() => NaphoraMusicManager.Instance?.PlayEndGameMusic();

    // ── Stop ──────────────────────────────────────────────────────────────

    /// <summary>Stops all currently playing instances of a sound group.</summary>
    public static void Stop(string soundID)
    {
        if (!Validate(soundID)) return;

        MasterAudio.StopAllOfSound(soundID);
    }

    // ── Volume / Mute ─────────────────────────────────────────────────────

    /// <summary>Sets the Master Audio master volume (0–1). Persisted across sessions.</summary>
    public static void SetMasterVolume(float volume)
    {
        MasterAudio.MasterVolumeLevel = Mathf.Clamp01(volume);
    }

    /// <summary>Enables or disables all SFX groups. Persisted to PlayerPrefs.</summary>
    public static void SetSoundEnabled(bool enabled)
    {
        PlayerPrefs.SetInt(PrefKeySound, enabled ? 1 : 0);
        PlayerPrefs.Save();

        float sfxVolume = PlayerPrefs.GetFloat(PrefKeySFXVolume, 1f);
        SetMixerVolume(AudioMixParamID.VOL_MasterSFX, enabled ? sfxVolume : 0f);

        try
        {
            if (MasterAudio.SafeInstance != null)
            {
                MasterAudio.MixerMuted = !enabled;
                MasterAudio.MasterVolumeLevel = enabled ? sfxVolume : 0f;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[AudioManager] Failed to set MasterAudio.MixerMuted: {ex.Message}");
        }
    }

    /// <summary>Enables or disables music on both controllers. Persisted to PlayerPrefs.</summary>
    public static void SetMusicEnabled(bool enabled)
    {
        PlayerPrefs.SetInt(PrefKeyMusic, enabled ? 1 : 0);
        PlayerPrefs.Save();

        float musicVolume = PlayerPrefs.GetFloat(PrefKeyMusicVolume, DefaultMusicVolume);
        SetMixerVolume(AudioMixParamID.VOL_MasterMUSIC, enabled ? musicVolume : 0f);

        SetAmbianceVolumeForMusicState();
    }

    /// <summary>Returns whether music is currently enabled. Reads from PlayerPrefs.</summary>
    public static bool IsMusicEnabled() => PlayerPrefs.GetInt(PrefKeyMusic, 1) == 1;

    /// <summary>Returns whether SFX are currently enabled. Reads from PlayerPrefs.</summary>
    public static bool IsSoundEnabled() => PlayerPrefs.GetInt(PrefKeySound, 1) == 1;

    /// <summary>Sets SFX volume from a slider (0–1). Persisted across sessions. Respects the mute toggle.</summary>
    public static void SetSFXVolume(float volume)
    {
        volume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(PrefKeySFXVolume, volume);
        PlayerPrefs.Save();

        bool enabled = IsSoundEnabled();
        SetMixerVolume(AudioMixParamID.VOL_MasterSFX, enabled ? volume : 0f);

        try
        {
            if (MasterAudio.SafeInstance != null)
            {
                MasterAudio.MasterVolumeLevel = enabled ? volume : 0f;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[AudioManager] Failed to set MasterAudio.MasterVolumeLevel: {ex.Message}");
        }
    }

    /// <summary>Sets music volume from a slider (0–1). Persisted across sessions. Respects the mute toggle.</summary>
    public static void SetMusicVolume(float volume)
    {
        volume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(PrefKeyMusicVolume, volume);
        PlayerPrefs.Save();

        bool enabled = IsMusicEnabled();
        SetMixerVolume(AudioMixParamID.VOL_MasterMUSIC, enabled ? volume : 0f);
    }

    /// <summary>Returns the current SFX volume (0–1). Reads from PlayerPrefs.</summary>
    public static float GetSFXVolume() => PlayerPrefs.GetFloat(PrefKeySFXVolume, 1f);

    /// <summary>Returns the current music volume (0–1). Reads from PlayerPrefs.</summary>
    public static float GetMusicVolume() => PlayerPrefs.GetFloat(PrefKeyMusicVolume, DefaultMusicVolume);

    // ── Pause / Resume ────────────────────────────────────────────────────

    /// <summary>Pauses all Master Audio sounds. Called by AudioManagerBehaviour on app pause/focus loss.</summary>
    public static void PauseAll()
    {
        try
        {
            if (MasterAudio.SafeInstance != null)
                MasterAudio.PauseEverything();
        }
        catch (System.Exception) { }
    }

    /// <summary>Resumes all Master Audio sounds. Called by AudioManagerBehaviour on app resume/focus gain.</summary>
    public static void ResumeAll()
    {
        try
        {
            if (MasterAudio.SafeInstance != null)
                MasterAudio.UnpauseEverything();
        }
        catch (System.Exception) { }
    }

    // ── Loop support ──────────────────────────────────────────────────────

    /// <summary>
    /// Plays a non-positional (2D) looping sound.
    /// The Sound Group must have looping enabled in the AudioSetup prefab.
    /// Returns an instance ID used to stop this specific instance later.
    /// Returns -1 if the sound could not be played.
    /// </summary>
    public static int PlayLoop(string soundID)
    {
        if (!Validate(soundID)) return -1;

        var result = MasterAudio.PlaySound(soundID);

        if (result == null || result.SoundPlayed == false)
        {
#if UNITY_EDITOR
            _missingSoundIDs.Add(soundID);
            Debug.LogWarning($"[Audio] \"{soundID}\" — needs implementation");
#endif
            return -1;
        }

#if UNITY_EDITOR
        _playedSoundIDs.Add(soundID);
#endif

        int id = _nextInstanceId++;
        if (result.ActingVariation != null)
            _loopInstances[id] = result.ActingVariation;

        return id;
    }

    /// <summary>
    /// Plays a looping sound following a transform.
    /// The Sound Group must have looping enabled in the AudioSetup prefab.
    /// Returns an instance ID used to stop this specific instance later.
    /// Returns -1 if the sound could not be played.
    /// </summary>
    public static int PlayLoop(string soundID, Transform target)
    {
        if (!Validate(soundID)) return -1;

        if (target == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"[Audio] \"{soundID}\" — target is null.");
#endif
            return -1;
        }

        var result = MasterAudio.PlaySound3DFollowTransform(soundID, target);

        if (result == null || result.SoundPlayed == false)
        {
#if UNITY_EDITOR
            _missingSoundIDs.Add(soundID);
            Debug.LogWarning($"[Audio] \"{soundID}\" — needs implementation");
#endif
            return -1;
        }

#if UNITY_EDITOR
        _playedSoundIDs.Add(soundID);
#endif

        int id = _nextInstanceId++;

        if (result.ActingVariation != null)
            _loopInstances[id] = result.ActingVariation;

        return id;
    }

    /// <summary>Stops a specific loop instance returned from <see cref="PlayLoop"/>.</summary>
    public static void StopInstance(int instanceId)
    {
        if (instanceId < 0) return;

        if (_loopInstances.TryGetValue(instanceId, out SoundGroupVariation variation))
        {
            if (variation != null)
                variation.Stop();

            _loopInstances.Remove(instanceId);
        }
    }

    /// <summary>
    /// Retunes a specific loop instance's pitch after it's already playing -- an engine hum
    /// climbing with speed, a looping alarm getting more frantic, etc. TriggerWithPitch only
    /// sets pitch once at the moment a sound starts; this is for a loop started via PlayLoop
    /// that needs its pitch kept in sync with something changing over time. No-ops quietly if
    /// instanceId is stale (already stopped) or invalid.
    /// </summary>
    public static void SetInstancePitch(int instanceId, float pitch)
    {
        if (instanceId < 0) return;

        if (_loopInstances.TryGetValue(instanceId, out SoundGroupVariation variation) && variation != null && variation.VarAudio != null)
            variation.VarAudio.pitch = pitch;
    }

    // ── Mix states ────────────────────────────────────────────────────────

    /// <summary>
    /// Registers the Unity Audio Mixer used for snapshot transitions.
    /// Called automatically by AudioManagerBehaviour on Awake.
    /// </summary>
    public static void RegisterMixer(AudioMixer mixer)
    {
        _audioMixer = mixer;
    }

    /// <summary>
    /// Initializes bus volumes from saved PlayerPrefs.
    /// Called by AudioManagerBehaviour after MasterAudio is ready.
    /// </summary>
    public static void InitializeMixerVolumes()
    {
        bool soundEnabled = IsSoundEnabled();
        float sfxVolume = PlayerPrefs.GetFloat(PrefKeySFXVolume, 1f);

        try
        {
            if (MasterAudio.SafeInstance != null)
            {
                MasterAudio.MixerMuted = !soundEnabled;
                MasterAudio.MasterVolumeLevel = soundEnabled ? sfxVolume : 0f;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[AudioManager] Failed to set MasterAudio.MixerMuted: {ex.Message}");
        }

        if (_audioMixer == null)
        {
#if UNITY_EDITOR
            Debug.LogError("[AudioManager] InitializeMixerVolumes - No AudioMixer assigned! Cannot set mixer volumes.");
#endif
            return;
        }

        SetMixerVolume(AudioMixParamID.VOL_MasterSFX, soundEnabled ? sfxVolume : 0f);

        bool musicEnabled = IsMusicEnabled();
        float musicVolume = PlayerPrefs.GetFloat(PrefKeyMusicVolume, DefaultMusicVolume);
        SetMixerVolume(AudioMixParamID.VOL_MasterMUSIC, musicEnabled ? musicVolume : 0f);

        float ambianceVolume = musicEnabled ? _ambianceVolumeWithMusic : _ambianceVolumeWithoutMusic;
        SetMixerVolume(AudioMixParamID.VOL_AmbianceMusicOn, ambianceVolume);
    }

    // Listener tether target (see ListenerTetherSettings). Overrides the tag lookup -- call it
    // from the player's own setup in projects whose player isn't tagged; null goes back to the tag.
    public static Transform ListenerTargetOverride { get; private set; }

    public static void SetListenerTarget(Transform target)
    {
        ListenerTargetOverride = target;
    }

    /// <summary>
    /// Registers the ambiance volume settings for music enabled/disabled states.
    /// Called automatically by AudioManagerBehaviour on Awake.
    /// </summary>
    public static void RegisterAmbianceVolumes(float volumeWithMusic, float volumeWithoutMusic)
    {
        _ambianceVolumeWithMusic = volumeWithMusic;
        _ambianceVolumeWithoutMusic = volumeWithoutMusic;
    }

    /// <summary>
    /// Sets the ambiance volume based on whether music is currently enabled.
    /// Uses the registered volume settings from AudioManagerBehaviour.
    /// </summary>
    public static void SetAmbianceVolumeForMusicState()
    {
        float targetVolume = IsMusicEnabled() ? _ambianceVolumeWithMusic : _ambianceVolumeWithoutMusic;
        SetMixerVolume(AudioMixParamID.VOL_AmbianceMusicOn, targetVolume);
    }

    /// <summary>
    /// Sets a named exposed parameter on the Unity Audio Mixer.
    /// Use <see cref="AudioMixParamID"/> constants as paramName.
    /// Typical use: driving loop volumes, RTPC-style parameters.
    /// </summary>
    public static void SetMixerFloat(string paramName, float value)
    {
        if (_audioMixer == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning("[Audio] SetMixerFloat — no AudioMixer assigned. Assign it on AudioManagerBehaviour.");
#endif
            return;
        }
        _audioMixer.SetFloat(paramName, value);
    }

    /// <summary>
    /// Sets a Unity AudioMixer volume parameter using a normalized 0–1 linear value.
    /// 0 = silent (-80 dB), 1 = unity gain (0 dB).
    /// Handles the linear-to-dB conversion so callers never deal with raw dB values.
    /// </summary>
    public static void SetMixerVolume(string paramName, float normalizedVolume)
    {
        float db = normalizedVolume <= 0.0001f ? -80f : Mathf.Log10(normalizedVolume) * 20f;
        SetMixerFloat(paramName, db);
    }

    /// <summary>
    /// Registers the from→to mix state transition table used by
    /// <see cref="SetMixState(MixState)"/>. Called automatically by
    /// AudioManagerBehaviour on Awake.
    /// </summary>
    public static void RegisterMixStateTransitions(List<MixStateTransition> transitions, float defaultTransitionTime)
    {
        _mixStateTransitions = transitions ?? new List<MixStateTransition>();
        _defaultMixTransitionTime = defaultTransitionTime;
    }

    /// <summary>
    /// Registers the MixState → AudioMixerSnapshot bindings assigned on
    /// AudioManagerBehaviour (drag-and-drop from the AudioMixer window).
    /// Called automatically by AudioManagerBehaviour on Awake.
    /// </summary>
    public static void RegisterMixStateSnapshots(List<MixStateSnapshot> snapshots)
    {
        _mixStateSnapshotRefs = new Dictionary<MixState, AudioMixerSnapshot>();
        if (snapshots == null) return;

        foreach (var entry in snapshots)
        {
            if (entry.snapshot == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[Audio] MixState \"{entry.state}\" has no snapshot assigned on AudioManagerBehaviour.");
#endif
                continue;
            }
            _mixStateSnapshotRefs[entry.state] = entry.snapshot;
        }
    }

    // Set while a sequence (currently: the first-launch cutscene) owns the mix outright.
    // Any SetMixState call made while locked is queued instead of applied -- last write
    // wins -- and released the moment UnlockMixState() runs, so nothing (ShowMenu(), a
    // stray ad, anything) can pull the mix out from under it mid-sequence.
    private static bool _mixStateLocked;
    private static MixState? _queuedMixState;

    /// <summary>
    /// Transitions the Unity Audio Mixer to the given mix state, using the
    /// AudioMixerSnapshot assigned to it on AudioManagerBehaviour. Looks up
    /// the transition time configured for (current state → state) in the
    /// registered transition table, falling back to the default transition
    /// time if no explicit entry exists. To add a new mix state, add a value
    /// to <see cref="MixState"/> and drag its snapshot onto
    /// AudioManagerBehaviour's Mix State Snapshots list.
    ///
    /// If the mix is currently locked (<see cref="LockMixState"/>), this queues the
    /// request instead of applying it -- see there for why.
    /// </summary>
    public static void SetMixState(MixState state)
    {
        if (_mixStateLocked)
        {
            _queuedMixState = state;
#if UNITY_EDITOR
            Debug.Log($"[Audio] MixState \"{state}\" queued -- locked to \"{_currentMixState}\" until UnlockMixState().");
#endif
            return;
        }

        ApplyMixState(state);
    }

    /// <summary>
    /// Locks the mix to <paramref name="state"/> and holds it there: every
    /// <see cref="SetMixState"/> call until the matching <see cref="UnlockMixState"/>
    /// is queued rather than applied. For a sequence that has to own the mix outright --
    /// the first-launch cutscene is the current use -- where something elsewhere in the
    /// game calling SetMixState (ShowMenu's Menu, say) must not be able to cut the lock
    /// short just because it happened to run mid-sequence.
    /// </summary>
    public static void LockMixState(MixState state)
    {
        _mixStateLocked = false; // this call itself must not be queued by a stale lock
        _queuedMixState = null;
        ApplyMixState(state);
        _mixStateLocked = true;
    }

    /// <summary>
    /// Releases a lock taken with <see cref="LockMixState"/>, applying whichever state
    /// was queued (most recent wins) while it was held, if any.
    /// </summary>
    public static void UnlockMixState()
    {
        _mixStateLocked = false;
        if (_queuedMixState.HasValue)
        {
            var queued = _queuedMixState.Value;
            _queuedMixState = null;
            ApplyMixState(queued);
        }
    }

    private static void ApplyMixState(MixState state)
    {
        if (!_mixStateSnapshotRefs.TryGetValue(state, out AudioMixerSnapshot snapshot) || snapshot == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"[Audio] MixState \"{state}\" has no snapshot assigned on AudioManagerBehaviour.");
#endif
            // Still track the intended state even without a snapshot to apply, so
            // Push/Lock restore logic and the queue above stay consistent regardless
            // of whether every state has been wired up in the Editor yet.
            _currentMixState = state;
            return;
        }

        float transitionTime = _defaultMixTransitionTime;
        foreach (var transition in _mixStateTransitions)
        {
            if (transition.from == _currentMixState && transition.to == state)
            {
                transitionTime = transition.transitionTime;
                break;
            }
        }

        snapshot.TransitionTo(transitionTime);
        ReapplyMixVolumesAfterSnapshotChange();
        _currentMixState = state;
#if UNITY_EDITOR
        Debug.Log($"[Audio] MixState -> {state}");
#endif
    }

    /// <summary>The mix state currently active (or most recently requested).</summary>
    public static MixState CurrentMixState => _currentMixState;

    /// <summary>
    /// Switches to a transient mix state — an interstitial or rewarded ad, say — while
    /// remembering whatever was active, so <see cref="PopMixState"/> can put it back
    /// exactly rather than assuming a fixed state to return to. Calls nest: an ad shown
    /// mid-GameOver restores to GameOver, one shown from the Menu restores to Menu.
    /// </summary>
    public static void PushMixState(MixState state)
    {
        _mixStateStack.Push(_currentMixState);
        SetMixState(state);
    }

    /// <summary>Restores the mix state that was active before the matching <see cref="PushMixState"/>.</summary>
    public static void PopMixState()
    {
        if (_mixStateStack.Count == 0)
        {
#if UNITY_EDITOR
            Debug.LogWarning("[Audio] PopMixState called with nothing pushed — ignoring.");
#endif
            return;
        }

        SetMixState(_mixStateStack.Pop());
    }

    /// <summary>
    /// Empties the Push/Pop interruption stack outright, without applying any mix state.
    /// Menu is a known-good reset point -- nothing should still be "pending" to restore to
    /// once the player is back there, so AudioManagerBehaviour calls this on every scene
    /// load into Menu. Defends against a stray PushMixState somewhere that never got its
    /// matching Pop (a bug, or a screen that closed via scene reload instead of its own
    /// Close() path): without this, that stale entry would sit on the stack indefinitely
    /// and get restored to by some unrelated, much later PopMixState call.
    /// </summary>
    public static void ClearMixStateStack()
    {
        _mixStateStack.Clear();
    }

    /// <summary>
    /// Re-applies SFX/Music/Ambiance mixer volumes after a snapshot
    /// transition, so the snapshot's baked-in levels don't override the
    /// player's current volume/mute preferences.
    /// </summary>
    private static void ReapplyMixVolumesAfterSnapshotChange()
    {
        bool soundEnabled = IsSoundEnabled();
        float sfxVolume = PlayerPrefs.GetFloat(PrefKeySFXVolume, 1f);
        SetMixerVolume(AudioMixParamID.VOL_MasterSFX, soundEnabled ? sfxVolume : 0f);

        try
        {
            if (MasterAudio.SafeInstance != null)
            {
                MasterAudio.MixerMuted = !soundEnabled;
                MasterAudio.MasterVolumeLevel = soundEnabled ? sfxVolume : 0f;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[AudioManager] Failed to set MasterAudio.MixerMuted in SetMixState: {ex.Message}");
        }

        bool musicEnabled = IsMusicEnabled();
        float musicVolume = PlayerPrefs.GetFloat(PrefKeyMusicVolume, DefaultMusicVolume);
        SetMixerVolume(AudioMixParamID.VOL_MasterMUSIC, musicEnabled ? musicVolume : 0f);

        float ambianceVolume = musicEnabled ? _ambianceVolumeWithMusic : _ambianceVolumeWithoutMusic;
        SetMixerVolume(AudioMixParamID.VOL_AmbianceMusicOn, ambianceVolume);
    }

    // ── Internal helpers ──────────────────────────────────────────────────

    private static bool Validate(string soundID)
    {
        if (string.IsNullOrEmpty(soundID))
        {
#if UNITY_EDITOR
            Debug.LogWarning("[Audio] soundID is null or empty.");
#endif
            return false;
        }
        if (soundID[0] == '_') return false; // intentionally silent — no log
        return true;
    }

    private static void HandleResult(PlaySoundResult result, string soundID)
    {
#if UNITY_EDITOR
        if (result == null || result.SoundPlayed == false)
        {
            _missingSoundIDs.Add(soundID);
            Debug.LogWarning($"[Audio] \"{soundID}\" — needs implementation");
        }
        else
        {
            _playedSoundIDs.Add(soundID);
        }
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// Logs which sound IDs played successfully and which still need a
    /// MasterAudio Sound Group set up, for everything triggered this session.
    /// Runs automatically when play mode (or the build) stops.
    /// </summary>
    private static void DumpAudioCoverageReport()
    {
        Application.quitting -= DumpAudioCoverageReport;

        if (_missingSoundIDs.Count == 0)
        {
            Debug.Log($"[AudioManager] Audio coverage report — all {_playedSoundIDs.Count} sound(s) triggered this session played successfully.");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"[AudioManager] Audio coverage report — {_missingSoundIDs.Count} sound(s) need implementation:");
        foreach (var soundID in _missingSoundIDs.OrderBy(id => id))
            sb.AppendLine($"  - {soundID}");
        sb.AppendLine($"({_playedSoundIDs.Count} sound(s) played successfully this session.)");

        Debug.LogWarning(sb.ToString());
    }
#endif
}
