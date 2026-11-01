using System.Collections.Generic;
using DarkTonic.MasterAudio;
using UnityEngine;

// Edit-Mode duplicate of an existing Sound Group under a new name, with its own volume and bus --
// e.g. splitting a shared sound into a separate group that can be mixed on its own. The copy keeps
// every variation, clip and setting of the source. Same MAScripts-only contract as
// SoundGroupTemplateAuthoring: callers pass plain values and own the LoadPrefabContents session.
public static class SoundGroupDuplicateAuthoring
{
    // Shown for "no bus"; Master Audio treats any busIndex below HardCodedBusOptions as unrouted.
    public const string NoBusLabel = "(no bus)";

    public static List<string> ListGroupNames(GameObject masterAudioRoot)
    {
        var names = new List<string>();
        if (masterAudioRoot == null)
            return names;

        foreach (Transform child in masterAudioRoot.transform)
        {
            if (child.GetComponent<MasterAudioGroup>() != null)
                names.Add(child.name);
        }

        names.Sort(System.StringComparer.OrdinalIgnoreCase);
        return names;
    }

    // First entry is always NoBusLabel, then every bus in the MasterAudio's own list order.
    public static List<string> ListBusNames(GameObject masterAudioRoot)
    {
        var names = new List<string> { NoBusLabel };
        var masterAudio = masterAudioRoot != null ? masterAudioRoot.GetComponent<MasterAudio>() : null;
        if (masterAudio != null)
        {
            foreach (GroupBus bus in masterAudio.groupBuses)
                names.Add(bus.busName);
        }

        return names;
    }

    public static bool GroupExists(GameObject masterAudioRoot, string groupName)
    {
        return masterAudioRoot != null && !string.IsNullOrEmpty(groupName) && masterAudioRoot.transform.Find(groupName) != null;
    }

    public static bool TryGetGroupSettings(GameObject masterAudioRoot, string groupName, out float volume, out string busName)
    {
        volume = 1f;
        busName = NoBusLabel;

        Transform child = masterAudioRoot != null ? masterAudioRoot.transform.Find(groupName) : null;
        var group = child != null ? child.GetComponent<MasterAudioGroup>() : null;
        if (group == null)
            return false;

        volume = group.groupMasterVolume;
        GroupBus bus = GetBus(masterAudioRoot.GetComponent<MasterAudio>(), group.busIndex);
        if (bus != null)
            busName = bus.busName;
        return true;
    }

    // Returns null (and logs) if the source is missing or the new name is already taken -- never overwrites.
    public static GameObject Duplicate(GameObject masterAudioRoot, string sourceName, string newName, float volume, string busName)
    {
        Transform source = masterAudioRoot.transform.Find(sourceName);
        if (source == null || source.GetComponent<MasterAudioGroup>() == null)
        {
            Debug.LogError($"[SoundGroupDuplicateAuthoring] No Sound Group named \"{sourceName}\".");
            return null;
        }

        if (GroupExists(masterAudioRoot, newName))
        {
            Debug.LogWarning($"[SoundGroupDuplicateAuthoring] A Sound Group named \"{newName}\" already exists -- nothing duplicated.");
            return null;
        }

        GameObject copy = Object.Instantiate(source.gameObject, masterAudioRoot.transform, false);
        copy.name = newName;
        copy.transform.SetSiblingIndex(source.GetSiblingIndex() + 1);   // sits next to its source in the hierarchy

        var group = copy.GetComponent<MasterAudioGroup>();
        group.groupMasterVolume = volume;
        group.busIndex = BusIndexFor(masterAudioRoot.GetComponent<MasterAudio>(), busName);

        return copy;
    }

    // Adds a bus at the end of the list (so existing groups' bus indices don't shift). Its mixer output
    // and 2D setting are copied from copyRoutingFrom -- a bus with no mixerChannel would bypass the
    // AudioMixer, i.e. the SFX volume slider, mute and mix-state snapshots. Returns false if one with
    // that name already exists.
    public static bool EnsureBus(GameObject masterAudioRoot, string busName, string copyRoutingFrom)
    {
        var masterAudio = masterAudioRoot.GetComponent<MasterAudio>();
        if (masterAudio == null || string.IsNullOrEmpty(busName) || masterAudio.groupBuses.Exists(b => b.busName == busName))
            return false;

        GroupBus template = masterAudio.groupBuses.Find(b => b.busName == copyRoutingFrom);
        if (template == null)
            Debug.LogWarning($"[SoundGroupDuplicateAuthoring] Bus \"{copyRoutingFrom}\" not found -- \"{busName}\" created with no mixer output, assign one in the MasterAudio Inspector.");

        masterAudio.groupBuses.Add(new GroupBus
        {
            busName = busName,
            mixerChannel = template != null ? template.mixerChannel : null,
            forceTo2D = template != null && template.forceTo2D,
        });
        return true;
    }

    // Routes each named group to busName (NoBusLabel = unroute). Returns how many groups were changed.
    public static int AssignBus(GameObject masterAudioRoot, IEnumerable<string> groupNames, string busName)
    {
        var masterAudio = masterAudioRoot.GetComponent<MasterAudio>();
        int busIndex = BusIndexFor(masterAudio, busName);
        int changed = 0;

        foreach (string name in groupNames)
        {
            Transform child = masterAudioRoot.transform.Find(name);
            var group = child != null ? child.GetComponent<MasterAudioGroup>() : null;
            if (group == null)
            {
                Debug.LogWarning($"[SoundGroupDuplicateAuthoring] No Sound Group named \"{name}\" -- skipped.");
                continue;
            }

            group.busIndex = busIndex;
            changed++;
        }

        return changed;
    }

    private static int BusIndexFor(MasterAudio masterAudio, string busName)
    {
        if (masterAudio == null || string.IsNullOrEmpty(busName) || busName == NoBusLabel)
            return 0;

        int listIndex = masterAudio.groupBuses.FindIndex(b => b.busName == busName);
        if (listIndex < 0)
        {
            Debug.LogWarning($"[SoundGroupDuplicateAuthoring] Bus \"{busName}\" not found -- duplicate left unrouted.");
            return 0;
        }

        return MasterAudio.HardCodedBusOptions + listIndex;
    }

    private static GroupBus GetBus(MasterAudio masterAudio, int busIndex)
    {
        if (masterAudio == null)
            return null;

        int listIndex = busIndex - MasterAudio.HardCodedBusOptions;
        return listIndex >= 0 && listIndex < masterAudio.groupBuses.Count ? masterAudio.groupBuses[listIndex] : null;
    }
}
