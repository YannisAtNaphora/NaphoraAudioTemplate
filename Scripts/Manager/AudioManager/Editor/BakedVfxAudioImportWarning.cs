using UnityEditor;
using UnityEngine;

// Catches a common bug class: a reused VFX prefab (usually a third-party asset-pack
// effect) carrying its own AudioSource with Play On Awake and a real clip assigned,
// which auto-plays independent of AudioManager/Master Audio the moment it's
// instantiated -- no mixer routing, no volume-slider respect, no mute, no coverage
// tracking. Fires whenever a prefab is imported or re-saved, so it catches both a
// brand-new VFX prefab being dragged in AND an existing gameplay prefab being saved
// after nesting one as a child.
public class BakedVfxAudioImportWarning : AssetPostprocessor
{
    private void OnPostprocessPrefab(GameObject prefab)
    {
        foreach (var source in prefab.GetComponentsInChildren<AudioSource>(true))
        {
            bool hasClip = source.clip != null || source.resource != null;
            if (!source.playOnAwake || !hasClip)
                continue;

            string clipName = source.clip != null ? source.clip.name : source.resource.name;
            Debug.LogWarning(
                $"[Audio] \"{assetPath}\" has a baked AudioSource on \"{GetHierarchyPath(source.transform, prefab.transform)}\" " +
                $"with Play On Awake + clip \"{clipName}\" assigned. This plays independent of AudioManager/Master Audio, which " +
                "means it does NOT respond to the player's SFX volume slider, mute, or mixer/mix-state ducking -- it's stuck at " +
                "whatever level it was baked at, disconnected from the rest of the game's mix. DON'T just delete/disable this " +
                "AudioSource and call it fixed -- that only silences it. The clip still needs a proper Sound Group added to " +
                "MasterAudio.prefab and an AudioManager.Trigger(AudioConfig.Sfx_*) call added to the code that spawns/activates " +
                "this prefab, THEN this baked AudioSource removed. See Naphora/Audio/Convert Baked VFX Audio... and " +
                "TriggerSfxOnEnable.cs for the established fix pattern.",
                source);
        }
    }

    private static string GetHierarchyPath(Transform t, Transform root)
    {
        if (t == root)
            return t.name;

        return GetHierarchyPath(t.parent, root) + "/" + t.name;
    }
}
