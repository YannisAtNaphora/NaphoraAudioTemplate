using System;

namespace Naphora.AudioKit.Core.Config
{
    /// <summary>
    /// Configures the snapshot transition time to use when moving from one
    /// mix state to another. Add entries here to override the default
    /// transition time for specific state changes (e.g. a slower fade into
    /// GameOver than the default snapshot fade).
    /// </summary>
    [Serializable]
    public struct MixStateTransition
    {
        public MixState from;
        public MixState to;
        public float transitionTime;
    }
}
