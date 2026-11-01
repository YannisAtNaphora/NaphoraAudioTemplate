using System.Collections.Generic;
using UnityEngine;

namespace Naphora.AudioKit.Core.Config
{
    // Distance rolloff for a set of Sound Groups, applied to MasterAudio.prefab with
    // Naphora/Audio/Apply Attenuation Presets... Distances are from the AudioListener: with
    // the Listener Tether enabled that's a few units from the player; without it the listener
    // is up on the camera, so every distance here needs adding the camera's follow distance.
    [CreateAssetMenu(menuName = "Naphora/Audio/Attenuation Preset", fileName = "Attenuation_")]
    public class AttenuationPreset : ScriptableObject
    {
        // 1 = fully positional (pans), 0 = plain 2D.
        [Range(0f, 1f)] public float spatialBlend = 1f;

        // Full volume at any distance -- only the direction (pan) changes.
        public bool directionOnly;

        // Full volume up to here, then an eased fade down to volumeAtFadeEnd at fadeEndDistance.
        // Beyond fadeEndDistance the volume holds at volumeAtFadeEnd rather than dropping to silence.
        [Min(0f)] public float fullVolumeDistance = 10f;
        [Min(0.1f)] public float fadeEndDistance = 32f;
        [Range(0f, 1f)] public float volumeAtFadeEnd = 0.15f;

        [Range(0f, 360f)] public float spread;
        // Off by default: doppler on fast projectiles/explosions reads as random pitch wobble on mobile.
        [Range(0f, 5f)] public float dopplerLevel;

        // Sound Group names this preset was last applied to -- kept by the apply window so a
        // retuned preset can be reapplied without re-picking groups. A group belongs to one preset.
        public List<string> soundGroups = new List<string>();

        // Unity's custom rolloff curve runs over distance / maxDistance (0..1).
        public AnimationCurve BuildRolloffCurve()
        {
            if (directionOnly)
                return new AnimationCurve(new Keyframe(0f, 1f, 0f, 0f), new Keyframe(1f, 1f, 0f, 0f));

            float end = Mathf.Max(0.1f, fadeEndDistance);
            float knee = Mathf.Clamp(fullVolumeDistance / end, 0f, 0.99f);

            // Flat tangents on every key give an S-shaped ease instead of Logarithmic's sharp early drop.
            return new AnimationCurve(
                new Keyframe(0f, 1f, 0f, 0f),
                new Keyframe(knee, 1f, 0f, 0f),
                new Keyframe(1f, volumeAtFadeEnd, 0f, 0f));
        }

        public float MaxDistance => directionOnly ? Mathf.Max(fadeEndDistance, 1000f) : Mathf.Max(0.1f, fadeEndDistance);

        public float MinDistance => directionOnly ? 1f : fullVolumeDistance;
    }
}
