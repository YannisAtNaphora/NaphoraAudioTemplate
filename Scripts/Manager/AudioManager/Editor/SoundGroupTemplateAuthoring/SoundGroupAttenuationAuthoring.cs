using System.Collections.Generic;
using DarkTonic.MasterAudio;
using UnityEngine;

// Edit-Mode bulk attenuation for existing Sound Groups. Master Audio's own group Bulk Edit
// doesn't cover 3D distance settings, and its Audio Source Templates only apply when a group
// is created -- so re-tuning distances meant hand-editing every variation's AudioSource.
// Same MAScripts-only contract as SoundGroupTemplateAuthoring: callers pass plain values and
// own the LoadPrefabContents / SaveAsPrefabAsset session.
public static class SoundGroupAttenuationAuthoring
{
    public struct GroupInfo
    {
        public string Name;
        public string BusName;
        public bool BusForces2D;
        public int VariationCount;
        public string Current;   // short readout of the first variation's settings
    }

    public static List<GroupInfo> ListGroups(GameObject masterAudioRoot)
    {
        var result = new List<GroupInfo>();
        if (masterAudioRoot == null)
            return result;

        var masterAudio = masterAudioRoot.GetComponent<MasterAudio>();
        foreach (Transform child in masterAudioRoot.transform)
        {
            var group = child.GetComponent<MasterAudioGroup>();
            if (group == null)
                continue;

            GroupBus bus = GetBus(masterAudio, group);
            var variations = group.GetComponentsInChildren<SoundGroupVariation>(true);
            AudioSource first = variations.Length > 0 ? variations[0].GetComponent<AudioSource>() : null;

            result.Add(new GroupInfo
            {
                Name = child.name,
                BusName = bus != null ? bus.busName : "(no bus)",
                BusForces2D = bus != null && bus.forceTo2D,
                VariationCount = variations.Length,
                Current = Describe(group, first),
            });
        }

        return result;
    }

    // Writes the rolloff to every variation of each named group and switches the group to a
    // custom spatial blend (the group setting overrides the AudioSource's own blend at play time).
    // clearBusForce2D also unticks Force to 2D on those groups' buses, which otherwise wins over both.
    public static int Apply(
        GameObject masterAudioRoot,
        ICollection<string> groupNames,
        float spatialBlend,
        AnimationCurve rolloffCurve,
        float minDistance,
        float maxDistance,
        float spread,
        float dopplerLevel,
        bool clearBusForce2D)
    {
        var masterAudio = masterAudioRoot.GetComponent<MasterAudio>();
        int sourcesChanged = 0;

        foreach (string name in groupNames)
        {
            Transform child = masterAudioRoot.transform.Find(name);
            var group = child != null ? child.GetComponent<MasterAudioGroup>() : null;
            if (group == null)
            {
                Debug.LogWarning($"[SoundGroupAttenuationAuthoring] Group \"{name}\" not found -- skipped.");
                continue;
            }

            group.spatialBlendType = MasterAudio.ItemSpatialBlendType.ForceToCustom;
            group.spatialBlend = spatialBlend;

            GroupBus bus = GetBus(masterAudio, group);
            if (clearBusForce2D && bus != null && bus.forceTo2D)
            {
                bus.forceTo2D = false;
                Debug.Log($"[SoundGroupAttenuationAuthoring] Unticked Force to 2D on bus \"{bus.busName}\" (used by \"{name}\").");
            }

            foreach (var variation in group.GetComponentsInChildren<SoundGroupVariation>(true))
            {
                var source = variation.GetComponent<AudioSource>();
                if (source == null)
                    continue;

                source.spatialBlend = spatialBlend;
                source.rolloffMode = AudioRolloffMode.Custom;
                source.maxDistance = maxDistance;
                source.minDistance = Mathf.Min(minDistance, maxDistance);
                source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, rolloffCurve);
                source.spread = spread;
                source.dopplerLevel = dopplerLevel;
                sourcesChanged++;
            }
        }

        return sourcesChanged;
    }

    private static GroupBus GetBus(MasterAudio masterAudio, MasterAudioGroup group)
    {
        if (masterAudio == null)
            return null;

        int listIndex = group.busIndex - MasterAudio.HardCodedBusOptions;
        return listIndex >= 0 && listIndex < masterAudio.groupBuses.Count ? masterAudio.groupBuses[listIndex] : null;
    }

    private static string Describe(MasterAudioGroup group, AudioSource source)
    {
        string blend = group.spatialBlendType == MasterAudio.ItemSpatialBlendType.ForceToCustom
            ? $"Custom {group.spatialBlend:0.##}"
            : group.spatialBlendType.ToString();

        if (source == null)
            return blend;

        return $"{blend}, {source.rolloffMode} {source.minDistance:0.#}-{source.maxDistance:0.#}";
    }
}
