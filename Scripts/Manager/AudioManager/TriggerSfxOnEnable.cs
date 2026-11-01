using UnityEngine;

// For a VFX child that has no owning script to hook AudioManager.Trigger() into --
// something that plays purely off Unity's own OnEnable/Particle-System-Play-On-Awake,
// with nothing in code ever calling Instantiate()/SetActive() on it directly. Put this
// next to that VFX instead of a raw AudioSource, so the sound still comes from
// AudioManager (mixer routing, volume slider, mute, coverage tracking) rather than a
// baked, uncontrolled AudioSource. Prefer wiring a real code call site over this
// wherever one exists -- this is a fallback for the rare case where nothing in code
// ever activates the object directly.
public sealed class TriggerSfxOnEnable : MonoBehaviour
{
    [SerializeField] string soundId;

    void OnEnable()
    {
        if (!string.IsNullOrEmpty(soundId))
            AudioManager.Trigger(soundId, transform.position);
    }
}
