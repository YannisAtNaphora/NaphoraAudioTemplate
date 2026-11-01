using DarkTonic.MasterAudio;
using UnityEditor;
using UnityEngine;

// Standalone Editor utility: creates a real Sound Group (MasterAudioGroup +
// SoundGroupVariation) by cloning a named Master Audio group template, without
// requiring Play Mode (MasterAudio.CreateSoundGroup needs SoundsReady, which
// never becomes true outside Play Mode). Only depends on MAScripts, not on
// Naphora.AudioKit -- usable by any project on Master Audio, including
// lightweight playables that don't want the full AudioManager wrapper.
//
// Caller owns the PrefabUtility.LoadPrefabContents / SaveAsPrefabAsset session
// so many groups can be authored in one load/save pass:
//
//   GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
//   SoundGroupTemplateAuthoring.CloneGroupFromTemplate(root, "sfx_example", clip, volume: 0.8f);
//   PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
//   PrefabUtility.UnloadPrefabContents(root);

// Caller-facing stand-in for MasterAudio.ItemSpatialBlendType. Callers outside
// this assembly must never need MasterAudio.ItemSpatialBlendType in their own
// compilation -- a method's default-parameter value is part of its signature,
// so even an unpassed default of that type would force every caller's assembly
// to reference MAScripts directly too (bit Naphora.AudioKit.Editor once
// already). Keeping the real Master Audio type fully inside this file's own
// implementation is what makes this tool genuinely standalone.
public enum GroupSpatialBlend
{
    TwoD,
    ThreeD,
}

public static class SoundGroupTemplateAuthoring
{
    public const string DefaultTemplatePrefabPath =
        "Assets/Plugins/DarkTonic/MasterAudio/Sources/Prefabs/GroupTemplates/Default Single.prefab";

    /// <summary>
    /// Clones <paramref name="templatePrefabPath"/> as a new child of
    /// <paramref name="masterAudioRoot"/>, named <paramref name="groupName"/>, with a
    /// single clip variation. Returns null (and logs) if the group already exists,
    /// the template or clip can't be loaded, or the template has no
    /// MasterAudioGroup/SoundGroupVariation on it.
    /// spatialBlend is always set explicitly -- never inherited from the
    /// template or an existing sibling group. Master Audio's own "Default Single"
    /// template doesn't even serialize this field (it predates it), silently
    /// falling back to the ForceTo3D C# default, and this project's own
    /// MasterAudio.prefab shipped with placeholder groups inconsistently left 3D
    /// by mistake -- nothing here can be trusted implicitly.
    /// busName routes the new group to an existing bus on masterAudioRoot's own
    /// MasterAudio component (by groupBuses list name, the same way the Inspector's
    /// bus dropdown does it) -- left null/not found leaves the group unrouted
    /// ("No Bus"), same as a fresh "Default Single" clone. MasterAudio.RouteGroupToBus
    /// can't be used here: it resolves the group through the runtime registry
    /// (MasterAudio.Instance), which is only populated in Play Mode.
    /// </summary>
    public static GameObject CloneGroupFromTemplate(
        GameObject masterAudioRoot,
        string groupName,
        AudioClip clip,
        float volume = 1f,
        float cooldownSeconds = 0f,
        int maxVoices = 1,
        float pitchVariation = 0f,
        string busName = null,
        GroupSpatialBlend spatialBlend = GroupSpatialBlend.TwoD,
        string templatePrefabPath = DefaultTemplatePrefabPath)
    {
        if (masterAudioRoot == null)
        {
            Debug.LogError("[SoundGroupTemplateAuthoring] masterAudioRoot is null.");
            return null;
        }

        if (masterAudioRoot.transform.Find(groupName) != null)
        {
            Debug.LogWarning($"[SoundGroupTemplateAuthoring] Group \"{groupName}\" already exists under {masterAudioRoot.name} -- skipped.");
            return null;
        }

        var template = AssetDatabase.LoadAssetAtPath<GameObject>(templatePrefabPath);
        if (template == null)
        {
            Debug.LogError($"[SoundGroupTemplateAuthoring] Could not load group template at \"{templatePrefabPath}\".");
            return null;
        }

        if (clip == null)
        {
            Debug.LogError($"[SoundGroupTemplateAuthoring] No clip supplied for group \"{groupName}\".");
            return null;
        }

        var groupGO = Object.Instantiate(template, masterAudioRoot.transform, false);
        groupGO.name = groupName;

        var group = groupGO.GetComponent<MasterAudioGroup>();
        if (group == null)
        {
            Debug.LogError($"[SoundGroupTemplateAuthoring] Template \"{templatePrefabPath}\" has no MasterAudioGroup component.");
            Object.DestroyImmediate(groupGO);
            return null;
        }

        var variation = groupGO.GetComponentInChildren<SoundGroupVariation>();
        if (variation == null)
        {
            Debug.LogError($"[SoundGroupTemplateAuthoring] Template \"{templatePrefabPath}\" has no SoundGroupVariation child.");
            Object.DestroyImmediate(groupGO);
            return null;
        }

        group.groupMasterVolume = volume;
        group.spatialBlendType = spatialBlend == GroupSpatialBlend.TwoD
            ? MasterAudio.ItemSpatialBlendType.ForceTo2D
            : MasterAudio.ItemSpatialBlendType.ForceTo3D;

        if (!string.IsNullOrEmpty(busName))
        {
            var masterAudio = masterAudioRoot.GetComponent<MasterAudio>();
            int busListIndex = masterAudio == null
                ? -1
                : masterAudio.groupBuses.FindIndex(b => b.busName == busName);

            if (busListIndex < 0)
                Debug.LogWarning($"[SoundGroupTemplateAuthoring] Bus \"{busName}\" not found on {masterAudioRoot.name} -- \"{groupName}\" left unrouted.");
            else
                group.busIndex = MasterAudio.HardCodedBusOptions + busListIndex;
        }

        if (cooldownSeconds > 0f)
        {
            group.limitMode = MasterAudioGroup.LimitMode.TimeBased;
            group.minimumTimeBetween = cooldownSeconds;
        }
        group.limitPolyphony = maxVoices > 0;
        group.voiceLimitCount = maxVoices;

        variation.VarAudio.clip = clip;
        variation.VarAudio.pitch = 1f;
        variation.original_pitch = 1f;
        variation.transform.name = clip.name;

        if (pitchVariation > 0f)
        {
            variation.useRandomPitch = true;
            variation.randomPitchMode = SoundGroupVariation.RandomPitchMode.AddToClipPitch;
            variation.randomPitchMin = -pitchVariation;
            variation.randomPitchMax = pitchVariation;
        }

        return groupGO;
    }
}
