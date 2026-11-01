namespace Naphora.AudioKit.Core.Config
{
    /// <summary>
    /// Constants for Unity Audio Mixer exposed parameter names.
    /// Pass these to AudioManager.SetMixerFloat() / SetMixerVolume().
    ///
    /// Each constant must match an exposed parameter name exactly in the AudioMixer asset.
    /// Per-project: add or remove entries here to match what this game's AudioMixer exposes.
    /// </summary>
    public static class AudioMixParamID
    {
        public const string VOL_MasterSFX       = "VOL_MasterSFX";
        public const string VOL_MasterMUSIC     = "VOL_MasterMUSIC";
        public const string PIT_MasterMUSIC     = "PIT_MasterMUSIC";

        /// <summary>Ambiance bus volume. Set low/off while music is playing, full when music is off.</summary>
        public const string VOL_AmbianceMusicOn = "VOL_AmbianceMusicOn";
    }
}
