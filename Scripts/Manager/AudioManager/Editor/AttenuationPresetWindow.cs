using System.Collections.Generic;
using System.IO;
using System.Linq;
using Naphora.AudioKit.Core.Config;
using UnityEditor;
using UnityEngine;

// Assigns Sound Groups on MasterAudio.prefab to AttenuationPresets and writes each preset's
// rolloff onto every variation's AudioSource (via SoundGroupAttenuationAuthoring, Edit-Mode safe).
// Each preset keeps its own group list, so after retuning a preset "Reapply All" pushes the
// new numbers out without re-picking anything.
// Run via: Naphora/Audio/Apply Attenuation Presets...
public class AttenuationPresetWindow : EditorWindow
{
    private const string MasterAudioPrefabPath = "Assets/NaphoraAudioTemplate/Audio/Prefabs/MasterAudio.prefab";
    private const string PresetFolder = "Assets/NaphoraAudioTemplate/Audio/AttenuationPresets";

    private AttenuationPreset _preset;
    private List<SoundGroupAttenuationAuthoring.GroupInfo> _groups = new List<SoundGroupAttenuationAuthoring.GroupInfo>();
    private readonly HashSet<string> _ticked = new HashSet<string>();
    private Dictionary<string, AttenuationPreset> _owners = new Dictionary<string, AttenuationPreset>();
    private string _search = "";
    private bool _clearBusForce2D = true;
    private Vector2 _scroll;

    [MenuItem("Naphora/Audio/Apply Attenuation Presets...")]
    public static void Open()
    {
        GetWindow<AttenuationPresetWindow>("Attenuation Presets");
    }

    private void OnEnable()
    {
        Refresh();
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Pick a preset, tick the Sound Groups that should use it, then Apply. Distances are measured " +
            "from the AudioListener -- the default presets assume the Listener Tether on AudioManagerBehaviour " +
            "is enabled (listener a few units from the player, not up on the camera). " +
            "Ticking a group that belongs to another preset moves it to this one.",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            var newPreset = (AttenuationPreset)EditorGUILayout.ObjectField("Preset", _preset, typeof(AttenuationPreset), false);
            if (newPreset != _preset)
            {
                _preset = newPreset;
                LoadTicksFromPreset();
            }

            if (GUILayout.Button("Create Defaults", GUILayout.Width(110)))
                CreateDefaultPresets();
        }

        _clearBusForce2D = EditorGUILayout.ToggleLeft("Untick \"Force to 2D\" on the buses of applied groups (otherwise the bus keeps them 2D)", _clearBusForce2D);

        using (new EditorGUILayout.HorizontalScope())
        {
            _search = EditorGUILayout.TextField("Filter", _search);
            if (GUILayout.Button("All", GUILayout.Width(40)))
                foreach (var g in Visible()) _ticked.Add(g.Name);
            if (GUILayout.Button("None", GUILayout.Width(45)))
                foreach (var g in Visible()) _ticked.Remove(g.Name);
            if (GUILayout.Button("Refresh", GUILayout.Width(60)))
                Refresh();
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        foreach (var g in Visible())
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool on = _ticked.Contains(g.Name);
                bool newOn = EditorGUILayout.ToggleLeft(g.Name, on, GUILayout.Width(260));
                if (newOn != on)
                {
                    if (newOn) _ticked.Add(g.Name);
                    else _ticked.Remove(g.Name);
                }

                _owners.TryGetValue(g.Name, out AttenuationPreset owner);
                GUILayout.Label(owner != null ? owner.name : "-", GUILayout.Width(150));
                GUILayout.Label(g.BusName + (g.BusForces2D ? "  [bus forces 2D]" : ""), GUILayout.Width(260));
                GUILayout.Label(g.Current);
            }
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(_preset == null))
            {
                if (GUILayout.Button($"Apply \"{(_preset != null ? _preset.name : "-")}\" to {_ticked.Count} group(s)", GUILayout.Height(28)))
                    ApplySelected();
            }

            if (GUILayout.Button("Reapply All Presets", GUILayout.Height(28), GUILayout.Width(160)))
                ReapplyAll();
        }
    }

    private IEnumerable<SoundGroupAttenuationAuthoring.GroupInfo> Visible()
    {
        return string.IsNullOrEmpty(_search)
            ? _groups
            : _groups.Where(g => g.Name.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0
                              || g.BusName.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private void Refresh()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MasterAudioPrefabPath);
        _groups = SoundGroupAttenuationAuthoring.ListGroups(prefab);

        _owners = new Dictionary<string, AttenuationPreset>();
        foreach (var preset in AllPresets())
            foreach (string name in preset.soundGroups)
                _owners[name] = preset;

        Repaint();
    }

    private void LoadTicksFromPreset()
    {
        _ticked.Clear();
        if (_preset != null)
            foreach (string name in _preset.soundGroups)
                _ticked.Add(name);
    }

    private void ApplySelected()
    {
        if (!EditorUtility.DisplayDialog("Apply Attenuation Preset",
                $"Write \"{_preset.name}\" to {_ticked.Count} Sound Group(s) on MasterAudio.prefab?", "Apply", "Cancel"))
            return;

        // A group belongs to one preset: take the ticked ones away from any other preset first.
        foreach (var other in AllPresets())
        {
            if (other == _preset)
                continue;
            if (other.soundGroups.RemoveAll(n => _ticked.Contains(n)) > 0)
                EditorUtility.SetDirty(other);
        }

        _preset.soundGroups = _ticked.OrderBy(n => n).ToList();
        EditorUtility.SetDirty(_preset);
        AssetDatabase.SaveAssets();

        WriteToMasterAudio(new[] { _preset });
    }

    private void ReapplyAll()
    {
        var presets = AllPresets().Where(p => p.soundGroups.Count > 0).ToList();
        if (presets.Count == 0)
        {
            EditorUtility.DisplayDialog("Apply Attenuation Preset", "No preset has any Sound Groups assigned yet.", "OK");
            return;
        }

        WriteToMasterAudio(presets);
    }

    private void WriteToMasterAudio(IList<AttenuationPreset> presets)
    {
        int changed = 0;
        GameObject root = PrefabUtility.LoadPrefabContents(MasterAudioPrefabPath);
        try
        {
            foreach (var p in presets)
            {
                changed += SoundGroupAttenuationAuthoring.Apply(
                    root, p.soundGroups, p.spatialBlend, p.BuildRolloffCurve(),
                    p.MinDistance, p.MaxDistance, p.spread, p.dopplerLevel, _clearBusForce2D);
            }

            PrefabUtility.SaveAsPrefabAsset(root, MasterAudioPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        Debug.Log($"[AttenuationPresets] Updated {changed} variation AudioSource(s) from {presets.Count} preset(s).");
        Refresh();
    }

    private static List<AttenuationPreset> AllPresets()
    {
        return AssetDatabase.FindAssets("t:" + nameof(AttenuationPreset))
            .Select(guid => AssetDatabase.LoadAssetAtPath<AttenuationPreset>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(p => p != null)
            .ToList();
    }

    // Fast top-down combat starting points, assuming the Listener Tether (listener ~5 units from the
    // player; the screen edge is roughly 12-18 units out). Small dies just off-screen, Medium
    // carries a little past it, Large reaches well off it. Spread keeps close sources from
    // hard-panning into one ear. Direction Only never fades and stays partly centred, so the
    // player's own hits always land solidly.
    private void CreateDefaultPresets()
    {
        if (!AssetDatabase.IsValidFolder(PresetFolder))
        {
            Directory.CreateDirectory(PresetFolder);
            AssetDatabase.Refresh();
        }

        CreatePreset("Attenuation_Small", p => { p.fullVolumeDistance = 6f; p.fadeEndDistance = 20f; p.volumeAtFadeEnd = 0.05f; p.spread = 40f; });
        CreatePreset("Attenuation_Medium", p => { p.fullVolumeDistance = 10f; p.fadeEndDistance = 32f; p.volumeAtFadeEnd = 0.15f; p.spread = 30f; });
        CreatePreset("Attenuation_Large", p => { p.fullVolumeDistance = 16f; p.fadeEndDistance = 55f; p.volumeAtFadeEnd = 0.3f; p.spread = 20f; });
        CreatePreset("Attenuation_DirectionOnly", p =>
        {
            p.directionOnly = true;
            p.spatialBlend = 0.7f;
            p.spread = 60f;
        });

        AssetDatabase.SaveAssets();
        Refresh();
    }

    private static void CreatePreset(string name, System.Action<AttenuationPreset> setup)
    {
        string path = $"{PresetFolder}/{name}.asset";
        if (AssetDatabase.LoadAssetAtPath<AttenuationPreset>(path) != null)
            return;   // never overwrite a preset that may already be tuned

        var preset = CreateInstance<AttenuationPreset>();
        setup(preset);
        AssetDatabase.CreateAsset(preset, path);
    }
}
