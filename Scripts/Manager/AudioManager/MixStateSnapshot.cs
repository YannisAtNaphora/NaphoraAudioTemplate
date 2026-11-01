using System;
using UnityEngine.Audio;

namespace Naphora.AudioKit.Core.Config
{
    /// <summary>
    /// Direct binding of a MixState to an AudioMixerSnapshot asset, assigned
    /// by dragging the snapshot from the AudioMixer window onto
    /// AudioManagerBehaviour. Avoids name-based lookup (AudioMixer.FindSnapshot),
    /// which silently breaks on typos, renames, or a stale Play-mode session.
    /// </summary>
    [Serializable]
    public struct MixStateSnapshot
    {
        public MixState state;
        public AudioMixerSnapshot snapshot;
    }
}
