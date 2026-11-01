/// <summary>
/// Per-project configuration for AudioManager.
/// This is the only file that should change when adopting the audio template.
///
/// Every Sfx_* value below must be typed verbatim as the matching Sound
/// Group's GameObject name in the MasterAudio prefab — AudioManager.Trigger
/// resolves purely by that string, there is no other lookup.
/// </summary>
public static class AudioConfig
{
    /// <summary>Name of the PlaylistController GameObject in the scene.</summary>
    public const string MusicControllerName = "MusicController";

    /// <summary>
    /// Build Settings scene name of this project's menu. AudioManagerBehaviour
    /// subscribes to SceneManager.sceneLoaded once and calls
    /// SetMixState(Menu) when this scene becomes active, SetMixState(Game)
    /// for anything else -- deliberately generic instead of hunting down every
    /// individual Retry/Play/return-to-menu/revive code path project by
    /// project, since all of them necessarily load a scene. Overlay-only
    /// states that don't reload a scene (PauseMenu, GameOver, Advert, Rewind)
    /// aren't covered by this and still need their own explicit SetMixState/
    /// PushMixState calls at whatever shows/hides them.
    /// </summary>
    public const string MenuSceneName = "Main Menu";

    /// <summary>
    /// Build Settings scene name of this project's loading/transition screen, if
    /// it's a real scene in its own right (as opposed to an overlay). Scene-load
    /// hooks that branch on MenuSceneName (AudioManagerBehaviour's mix state,
    /// a project's own gameplay-music manager if it has one) should ignore this
    /// scene entirely rather than treating it as "Game" -- otherwise a brief
    /// loading scene sitting between Menu and the real target scene flips the mix
    /// state and starts gameplay music while the loading image is still showing,
    /// which is then still running once the real destination scene appears on top
    /// of it. Leave empty if this project's loading screen is an overlay, not a
    /// scene of its own.
    /// </summary>
    public const string LoadingSceneName = "";

    /// <summary>
    /// Sound Group name of a looping menu ambiance track, started via
    /// AudioManager.PlayLoop when MenuSceneName becomes active and stopped via
    /// StopInstance when it isn't (see AudioManagerBehaviour.OnSceneLoaded) --
    /// same scene-load hook as the Menu/Game mix state, not a separate script.
    /// Leave empty if this project has no menu ambiance: PlayLoop no-ops
    /// harmlessly on an empty/null soundID (Validate() rejects it silently).
    /// </summary>
    public const string Amb_Menu_lp = "";

    // --- Feature toggles ----------------------------------------------------
    // Master switch for Master Audio's Playlist-based music system (NaphoraMusicManager
    // + its PlaylistControllers). False means this project is using its own music
    // manager instead and Master Audio's playlist side should be left completely
    // inert -- not "not ready yet", genuinely not in use. Controls two things:
    //  1. NaphoraMusicManager.Start() -- false means it never instantiates its Menu/
    //     Game PlaylistControllers at all. Leave false until this project has real
    //     Menu/Game playlists authored on PLController_MainMenu / PLController_Game,
    //     otherwise the Menu one auto-plays an empty playlist on boot
    //     (startPlaylistOnAwake) and logs a "will not play empty Playlist clip" warning.
    //  2. AudioManagerBehaviour.ResumeAllWhenReady() -- false means it resumes
    //     immediately after a focus regain instead of waiting on PlaylistController
    //     readiness. Waiting when false would wait forever: with zero controllers
    //     ever instantiated, "all controllers ready" never becomes true, so
    //     AudioManager.ResumeAll() would never fire and every Master Audio sound
    //     would stay paused for the rest of the session after the first focus
    //     loss/regain.
    //
    // AudioManagerBehaviour always boots silent (MixState.Video, unconditionally --
    // see its Awake()) regardless of this flag. Every real menu-shown /
    // gameplay-started code path in this project must call SetMixState(Menu) /
    // SetMixState(Game) to lift that silence -- a path missing its call is a
    // silent bug (the mixer just stays wherever it last was).
    public const bool UseMasterAudioMusicSystem = false;

    // --- Base SFX ---------------------------------------------------------
    // Generic placeholder/reusable sounds shipped with this template — the
    // actual clips live under Audio/SFX/NaphoraPlaceHolderSFX/ and are
    // already wired up as Sound Groups directly under the MasterAudio
    // prefab. Use as-is, swap the clips for real ones, or delete the
    // entries a project doesn't need. sfx_button_click has no dedicated
    // constant here on purpose — a project's own button-click sound
    // usually belongs in its own project-specific section below.
    public const string BaseSFX_WindowClose          = "basesfx_ui_bubble_low_window_close";
    public const string BaseSFX_WindowOpen           = "basesfx_ui_bubble_low_window_open";
    public const string BaseSFX_CoinCollect          = "basesfx_coin_sm_collect_rnd";
    public const string BaseSFX_BubblePop01          = "basesfx_ui_bubble_pop_01";
    public const string BaseSFX_BubblePop02          = "basesfx_ui_bubble_pop_02";
    public const string BaseSFX_BubblePop03          = "basesfx_ui_bubble_pop_03";
    public const string BaseSFX_BubblePop04          = "basesfx_ui_bubble_pop_04";
    public const string BaseSFX_BubblePop05          = "basesfx_ui_bubble_pop_05";
    public const string BaseSFX_BubblePop06          = "basesfx_ui_bubble_pop_06";
    public const string BaseSFX_BubblePop07          = "basesfx_ui_bubble_pop_07";
    public const string BaseSFX_BubblePop08          = "basesfx_ui_bubble_pop_08";
    public const string BaseSFX_CurrencyCollect      = "basesfx_ui_currecy_collect";
    public const string BaseSFX_GemCollect           = "basesfx_ui_gem_collect";
    public const string BaseSFX_AchievementSparkle   = "basesfx_ui_notify_achievement_sparkle";
    public const string BaseSFX_Notification         = "basesfx_ui_notify_notification";
    public const string BaseSFX_ObtainReward         = "basesfx_ui_obtain_reward";
    public const string BaseSFX_PurchaseComplete     = "basesfx_ui_purchase_complete";
    public const string BaseSFX_WhooshMedium         = "basesfx_whoosh_md";
    public const string BaseSFX_WhooshSmall          = "basesfx_whoosh_sm";
    public const string BaseSFX_ButtonPlayClick      = "sfx_button_play_click";
    public const string BaseSFX_ButtonAppear01       = "sfx_button_appear_01";
    public const string BaseSFX_ButtonAppear02       = "sfx_button_appear_02";
    public const string BaseSFX_ButtonAppear03       = "sfx_button_appear_03";
    public const string BaseSFX_CoinPackPurchasedSpawn = "ui_coin_pack_purchased_spawn";
    public const string BaseSFX_CurrencyCollectQuick = "ui_currecy_collect";
    public const string BaseSFX_CurrencyCollectEnd   = "ui_currency_collect_end";

    // --- Project SFX --------------------------------------------------------
    // This project's own sounds go here — each constant's value must match a
    // Sound Group name authored in MasterAudio.prefab exactly. Nothing below
    // is real; delete this block once the project has its own entries.
    //
    // public const string Sfx_ButtonClick = "sfx_button_click";
    // public const string Sfx_LevelStart  = "sfx_level_start";

    // --- Combo ladder (optional pattern) ------------------------------------
    // AudioManager.TriggerStep(sequenceID, step, maxStep) plays Sound Groups
    // named "{sequenceID}_01".."{sequenceID}_{maxStep:D2}" in order — a ready
    // way to score escalating combo/streak feedback. Uncomment and author the
    // matching Sound Groups to use it:
    //
    // public const string Seq_ComboLadder  = "combo";
    // public const int    ComboLadderSteps = 11;
}
