using System;
using UnityEngine;

namespace Naphora.AudioKit.Core.Config
{
    // Places the one AudioListener partway along the camera -> player line instead of on the
    // camera. A follow/top-down camera sits far from the action, so with the listener on it every
    // sound is "far away" and attenuation squashes the whole mix. Tuned on AudioManagerBehaviour;
    // defaults suit a fast top-down/isometric combat game.
    [Serializable]
    public class ListenerTetherSettings
    {
        // Opt-in: AudioManager's no-scene fallback also uses these defaults, and a project with
        // its own listener setup shouldn't have it moved just by updating the template.
        public bool enabled;

        // Found with GameObject.FindWithTag unless code calls AudioManager.SetListenerTarget.
        public string targetTag = "Player";

        // 0 = on the camera, 1 = on the player. ~0.8 keeps a little height/behind offset so sounds
        // on the player still have some stereo width instead of collapsing dead centre.
        [Range(0f, 1f)] public float towardTarget = 0.8f;

        // Lift above the target's pivot (usually at its feet).
        public float heightOffset = 1f;

        // SmoothDamp time. Keep short for action games -- a long lag lets dashes/knockbacks drag
        // the listener behind and pan the player's own sounds off-centre.
        [Range(0f, 0.5f)] public float damping = 0.06f;

        // A jump bigger than this (respawn, room change, teleport) snaps instead of sliding over.
        [Min(1f)] public float snapDistance = 15f;
    }
}
