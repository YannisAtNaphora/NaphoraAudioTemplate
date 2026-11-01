using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using Naphora.AudioKit.Core.Config;

/// <summary>
/// Hidden MonoBehaviour owned by AudioManager.
/// Handles scene persistence and application lifecycle events.
/// Never reference this class directly – use AudioManager instead.
/// </summary>
[AddComponentMenu("Naphora/Audio/AudioManagerBehaviour")]
public class AudioManagerBehaviour : MonoBehaviour
{
    [Tooltip("Assign the Unity Audio Mixer used for mix-state snapshots.")]
    [SerializeField] private AudioMixer _audioMixer;

    [Header("Ambiance Volume Settings")]
    [Tooltip("Ambiance volume when music is enabled (0-1). Lower to avoid competing with music.")]
    [SerializeField] [Range(0f, 1f)] private float _ambianceVolumeWithMusic = 0.3f;

    [Tooltip("Ambiance volume when music is disabled (0-1). Can be at full volume.")]
    [SerializeField] [Range(0f, 1f)] private float _ambianceVolumeWithoutMusic = 1f;

    [Header("Mix State Transitions")]
    [Tooltip("Transition time (seconds) used when no explicit entry below matches the current → target mix state.")]
    [SerializeField] private float _defaultMixTransitionTime = 0.25f;

    [Tooltip("Optional per from→to overrides. Add an entry to give a specific state change its own transition time (e.g. Gameplay → GameOver).")]
    [SerializeField] private List<MixStateTransition> _mixStateTransitions = new List<MixStateTransition>();

    [Tooltip("Drag each AudioMixerSnapshot (from the AudioMixer window) onto its matching MixState. This is what AudioManager.SetMixState(MixState) actually transitions to.")]
    [SerializeField] private List<MixStateSnapshot> _mixStateSnapshots = new List<MixStateSnapshot>();

    [Header("Listener Tether")]
    [SerializeField] private ListenerTetherSettings _listenerTether = new ListenerTetherSettings();

    private void Awake()
    {
        foreach (var other in FindObjectsOfType<AudioManagerBehaviour>())
        {
            if (other == this) continue;

            if (_audioMixer != null && other._audioMixer == null)
            {
                // We have the mixer — destroy the no-mixer runtime fallback
                Destroy(other.gameObject);
            }
            else
            {
                // We're the duplicate or the no-mixer fallback — yield to the existing instance
                Destroy(gameObject);
                return;
            }
        }

        DontDestroyOnLoad(gameObject);
        AudioManager.RegisterAmbianceVolumes(_ambianceVolumeWithMusic, _ambianceVolumeWithoutMusic);
        AudioManager.RegisterMixer(_audioMixer);
        AudioManager.RegisterMixStateTransitions(_mixStateTransitions, _defaultMixTransitionTime);
        AudioManager.RegisterMixStateSnapshots(_mixStateSnapshots);

        if (_listenerTether.enabled)
        {
            var tetherGO = new GameObject("AudioListener (Tether)");
            tetherGO.transform.SetParent(transform, false);
            tetherGO.AddComponent<AudioListenerTether>().Init(_listenerTether);
        }

        // Always boot silent, unconditionally -- not gated on whether this project
        // currently has a video intro. If a music system's own playlist auto-starts
        // on its Start() (which always runs after every Awake(), this one included)
        // before anything has explicitly silenced the mixer, there's a real audible
        // pop/leak before the first deliberate SetMixState() call lands. "Video" is
        // just this state's name (kept for consistency with prior projects); it's
        // really "boot-silent," used whether or not an actual video plays.
        // The other half of this contract: every real menu-shown / gameplay-started
        // code path in this project MUST call SetMixState(Menu) / SetMixState(Game)
        // explicitly -- a path that's missing its call is a silent bug, since the
        // mixer just stays wherever it last was instead of following the screen the
        // player is actually looking at. See AudioConfig.UseMasterAudioMusicSystem's
        // comment for the full list of transition points this project currently wires.
        AudioManager.SetMixState(MixState.Video);

        // Wait for MasterAudio to initialize before setting mixer volumes
        StartCoroutine(InitializeAfterMasterAudio());

        // Generic Menu/Game mix-state wiring: every real way of reaching the menu or
        // gameplay (Play, Retry, restart-after-death, revive, return-to-menu, mode
        // select, ...) necessarily loads a scene, so hooking the load event once here
        // covers all of them without hunting down each individual code path per
        // project. Overlay-only states that don't reload a scene (PauseMenu, GameOver,
        // Advert, Rewind) aren't scene transitions and still need their own explicit
        // SetMixState/PushMixState call at whatever shows/hides them.
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // Instance id of the menu ambiance loop, tracked here (not per-caller) since it's
    // started/stopped by this same scene-load hook, not by any gameplay code.
    private static int _menuAmbianceLoopId = -1;

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return; // ignore additive/overlay loads

        // A loading/transition scene between Menu and the real destination isn't
        // "Game" -- flipping the mix state (and starting gameplay music) while it's
        // still just the loading image showing means it's already running once the
        // real destination scene appears on top of it. See AudioConfig.LoadingSceneName.
        if (!string.IsNullOrEmpty(AudioConfig.LoadingSceneName) && scene.name == AudioConfig.LoadingSceneName)
            return;

        bool isMenu = scene.name == AudioConfig.MenuSceneName;
        AudioManager.SetMixState(isMenu ? MixState.Menu : MixState.Game);

        if (isMenu)
        {
            // Menu is a known-good reset point -- see ClearMixStateStack for why this
            // matters (a stray unpaired PushMixState anywhere shouldn't be able to survive
            // a return to Menu and get wrongly restored to by some unrelated later Pop).
            AudioManager.ClearMixStateStack();

            if (_menuAmbianceLoopId < 0)
                StartCoroutine(StartMenuAmbianceWhenReady());
        }
        else if (_menuAmbianceLoopId >= 0)
        {
            AudioManager.StopInstance(_menuAmbianceLoopId);
            _menuAmbianceLoopId = -1;
        }
    }

    // The very first Menu entry (boot's Video/loading scene -> Menu) can race Master
    // Audio's own startup: if this scene loads before MasterAudio.SoundsReady,
    // MasterAudio.PlaySound silently drops the call -- no error, no retry -- so the
    // menu-open stinger and ambiance loop never start, even though every later Menu
    // entry (after Master Audio has warmed up, e.g. returning from gameplay) works
    // fine. Wait for the same readiness check InitializeAfterMasterAudio() already
    // uses before firing either one.
    private System.Collections.IEnumerator StartMenuAmbianceWhenReady()
    {
        while (DarkTonic.MasterAudio.MasterAudio.SafeInstance == null || !DarkTonic.MasterAudio.MasterAudio.SoundsReady)
            yield return null;

        // The player may have already left the menu again during the wait -- only
        // fire if we're still actually looking at it and nothing else started the
        // loop in the meantime.
        if (_menuAmbianceLoopId >= 0 || SceneManager.GetActiveScene().name != AudioConfig.MenuSceneName)
            yield break;

        AudioManager.Trigger(AudioConfig.Sfx_MainMenuOpen);
        _menuAmbianceLoopId = AudioManager.PlayLoop(AudioConfig.Amb_Menu_lp);
    }

    private System.Collections.IEnumerator InitializeAfterMasterAudio()
    {
        // Wait for MasterAudio to be ready
        while (DarkTonic.MasterAudio.MasterAudio.SafeInstance == null || !DarkTonic.MasterAudio.MasterAudio.SoundsReady)
        {
            yield return null;
        }

        // Wait one more frame to be safe
        yield return null;

        // Now initialize the mixer volumes
        AudioManager.InitializeMixerVolumes();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
            AudioManager.PauseAll();
        else
            StartCoroutine(ResumeAllWhenReady());
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            AudioManager.PauseAll();
        else
            StartCoroutine(ResumeAllWhenReady());
    }

    private System.Collections.IEnumerator ResumeAllWhenReady()
    {
        // Only wait on PlaylistController readiness when this project actually has
        // Master Audio's playlist system engaged -- see
        // AudioConfig.UseMasterAudioMusicSystem. When it's off, there will never be
        // any PlaylistControllers to wait for, and waiting anyway means the wait
        // never ends: AudioManager.ResumeAll() would never fire, leaving every
        // Master Audio sound paused for the rest of the session after the first
        // focus loss/regain (backgrounding the app is one of the most common mobile
        // interactions there is -- found the hard way during Chicken vs Aliens'
        // integration).
        if (AudioConfig.UseMasterAudioMusicSystem)
        {
            while (!AllPlaylistControllersReady())
                yield return null;
        }
        AudioManager.ResumeAll();
    }

    private static bool AllPlaylistControllersReady()
    {
        var instances = DarkTonic.MasterAudio.PlaylistController.Instances;
        // Defensive fallback in case this is ever called with the flag on but before
        // NaphoraMusicManager.Start() has created its controllers yet: zero controllers
        // means nothing to wait for, not "never ready".
        if (instances.Count == 0) return true;
        foreach (var pc in instances)
            if (!pc.ControllerIsReady) return false;
        return true;
    }
}
