namespace Naphora.AudioKit.Core.Config
{
    /// <summary>
    /// High-level mix states. Pass these to AudioManager.SetMixState().
    /// Each value must be mapped to a snapshot via AudioManagerBehaviour's
    /// Mix State Snapshots list (drag-and-drop from the AudioMixer window).
    /// Names mirror the AudioMixerSnapshot names in the template's
    /// NaphoraGameMixer.mixer — add a value here and a matching snapshot
    /// together when a project needs another state.
    ///
    /// Values are pinned explicitly rather than left to implicit declaration
    /// order: NaphoraAudioMusicManager.prefab stores each MixStateSnapshot's
    /// state as a raw int, so removing or reordering a member without pinning
    /// the rest would silently repoint every later snapshot at the wrong
    /// state. Add new members with the next free number; don't renumber
    /// existing ones without also fixing the prefab's saved values.
    /// </summary>
    public enum MixState
    {
        Game = 0,
        Menu = 1,
        PauseMenu = 2,
        GameOver = 3,
        // 4 was GameBoostMode in the source project — game-specific, not part of this template.
        Video = 5,
        Advert = 6,
        Rewind = 7
        // 8/9 were Boss/MiniBoss in a project with boss fights — game-specific, not part of
        // this template. Next free number for a new project-specific state is 10.
    }
}
