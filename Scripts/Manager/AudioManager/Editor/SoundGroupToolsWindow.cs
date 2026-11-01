using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Two Edit-Mode jobs on MasterAudio.prefab that otherwise mean a lot of Inspector clicking:
//  1. Duplicate an existing Sound Group under a new name (refused if that name already exists),
//     with its own volume and bus, optionally adding the matching AudioConfig constant.
//  2. Route a batch of groups to one bus, creating the bus first if it doesn't exist yet.
// Run via: Naphora/Audio/Sound Group Tools...
public class SoundGroupToolsWindow : EditorWindow
{
    private const string MasterAudioPrefabPath = "Assets/NaphoraAudioTemplate/Audio/Prefabs/MasterAudio.prefab";

    private List<string> _groups = new List<string>();
    private List<string> _buses = new List<string>();
    private Vector2 _scroll;

    // Duplicate
    private string _sourceSearch = "";
    private string _sourceName;
    private bool _sourcePickerOpen;
    private string _newName = "";
    private float _volume = 1f;
    private int _dupBusIndex;
    private bool _addConstant = true;

    // Assign bus
    private string _filter = "";
    private readonly HashSet<string> _ticked = new HashSet<string>();
    private int _assignBusIndex;
    private string _newBusName = "";
    private bool _createNewBus = true;
    private int _routingSourceIndex = -1;   // bus whose mixer output a new bus copies; -1 = pick a default

    [MenuItem("Naphora/Audio/Sound Group Tools...")]
    public static void Open()
    {
        GetWindow<SoundGroupToolsWindow>("Sound Group Tools");
    }

    private void OnEnable()
    {
        Refresh();
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DrawDuplicate();
        EditorGUILayout.Space(12);
        DrawAssignBus();
        EditorGUILayout.EndScrollView();
    }

    private void DrawDuplicate()
    {
        EditorGUILayout.LabelField("Duplicate Sound Group", EditorStyles.boldLabel);
        if (_groups.Count == 0)
        {
            EditorGUILayout.HelpBox("No Sound Groups found on " + MasterAudioPrefabPath, MessageType.Warning);
            return;
        }

        // Type-to-filter instead of a Popup over the whole (potentially huge) group list.
        EditorGUILayout.LabelField("Source Group", _sourceName ?? "(none selected)");
        string newSearch = EditorGUILayout.TextField("Search", _sourceSearch);
        if (newSearch != _sourceSearch)
        {
            _sourceSearch = newSearch;
            _sourcePickerOpen = true;
        }

        if (_sourcePickerOpen && !string.IsNullOrEmpty(_sourceSearch))
        {
            var matches = _groups.Where(g => g.IndexOf(_sourceSearch, System.StringComparison.OrdinalIgnoreCase) >= 0).Take(12);
            foreach (string match in matches)
            {
                if (GUILayout.Button(match, EditorStyles.miniButton))
                {
                    _sourceName = match;
                    _sourceSearch = match;
                    _sourcePickerOpen = false;
                    LoadSourceSettings();
                }
            }
        }

        _newName = EditorGUILayout.TextField("New Group Name", _newName).Trim();
        _volume = EditorGUILayout.Slider("Volume", _volume, 0f, 1f);
        _dupBusIndex = EditorGUILayout.Popup("Bus", _dupBusIndex, _buses.ToArray());
        _addConstant = EditorGUILayout.ToggleLeft("Add AudioConfig constant (under the Convert tool's marker)", _addConstant);

        bool exists = _groups.Contains(_newName);
        if (exists)
            EditorGUILayout.HelpBox($"A Sound Group named \"{_newName}\" already exists -- pick another name.", MessageType.Error);

        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_sourceName) || string.IsNullOrEmpty(_newName) || exists))
        {
            if (GUILayout.Button("Duplicate", GUILayout.Height(26)))
                Duplicate();
        }
    }

    private void DrawAssignBus()
    {
        EditorGUILayout.LabelField("Assign Groups To Bus", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            _filter = EditorGUILayout.TextField("Filter", _filter);
            if (GUILayout.Button("Tick Filtered", GUILayout.Width(95)))
                TickFiltered();
            if (GUILayout.Button("None", GUILayout.Width(45)))
                _ticked.Clear();
        }

        foreach (string group in Filtered())
        {
            bool on = _ticked.Contains(group);
            if (EditorGUILayout.ToggleLeft(group, on) != on)
            {
                if (on) _ticked.Remove(group);
                else _ticked.Add(group);
            }
        }

        _createNewBus = EditorGUILayout.ToggleLeft("Create a new bus (if it doesn't exist yet)", _createNewBus);
        if (_createNewBus)
        {
            _newBusName = EditorGUILayout.TextField("New Bus Name", _newBusName).Trim();
            if (_routingSourceIndex < 0)
                _routingSourceIndex = Mathf.Max(0, _buses.Count - 1);
            _routingSourceIndex = EditorGUILayout.Popup("Copy Mixer Output From", _routingSourceIndex, _buses.ToArray());
        }
        else
            _assignBusIndex = EditorGUILayout.Popup("Bus", _assignBusIndex, _buses.ToArray());

        string target = _createNewBus ? _newBusName : _buses[Mathf.Clamp(_assignBusIndex, 0, _buses.Count - 1)];
        using (new EditorGUI.DisabledScope(_ticked.Count == 0 || string.IsNullOrEmpty(target)))
        {
            if (GUILayout.Button($"Assign {_ticked.Count} group(s) to \"{target}\"", GUILayout.Height(26)))
                AssignBus(target);
        }
    }

    private IEnumerable<string> Filtered()
    {
        return string.IsNullOrEmpty(_filter)
            ? _groups
            : _groups.Where(g => g.IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private void TickFiltered()
    {
        foreach (string g in Filtered())
            _ticked.Add(g);
    }

    private void Refresh()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MasterAudioPrefabPath);
        _groups = SoundGroupDuplicateAuthoring.ListGroupNames(prefab);
        _buses = SoundGroupDuplicateAuthoring.ListBusNames(prefab);
        if (_sourceName != null && !_groups.Contains(_sourceName))
            _sourceName = null;
        LoadSourceSettings();
        Repaint();
    }

    private void LoadSourceSettings()
    {
        if (_sourceName == null)
            return;

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MasterAudioPrefabPath);
        if (SoundGroupDuplicateAuthoring.TryGetGroupSettings(prefab, _sourceName, out float volume, out string bus))
        {
            _volume = volume;
            _dupBusIndex = Mathf.Max(0, _buses.IndexOf(bus));
        }
    }

    private void Duplicate()
    {
        string source = _sourceName;
        string bus = _buses[_dupBusIndex];
        bool done;

        GameObject root = PrefabUtility.LoadPrefabContents(MasterAudioPrefabPath);
        try
        {
            done = SoundGroupDuplicateAuthoring.Duplicate(root, source, _newName, _volume, bus) != null;
            if (done)
                PrefabUtility.SaveAsPrefabAsset(root, MasterAudioPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        if (!done)
            return;

        string constant = _addConstant ? ConvertBakedVfxAudioWindow.EnsureAudioConfigConstant(_newName) : null;
        Debug.Log($"[SoundGroupTools] Duplicated \"{source}\" as \"{_newName}\" (volume {_volume:0.##}, bus {bus})" +
                  (constant != null ? $", AudioConfig.{constant} added." : "."));

        _newName = "";
        Refresh();
    }

    private void AssignBus(string busName)
    {
        bool created = false;
        int changed;

        GameObject root = PrefabUtility.LoadPrefabContents(MasterAudioPrefabPath);
        try
        {
            if (_createNewBus)
            {
                string routingFrom = _buses[Mathf.Clamp(_routingSourceIndex, 0, _buses.Count - 1)];
                created = SoundGroupDuplicateAuthoring.EnsureBus(root, busName, routingFrom);
            }

            changed = SoundGroupDuplicateAuthoring.AssignBus(root, _ticked, busName);
            PrefabUtility.SaveAsPrefabAsset(root, MasterAudioPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        Debug.Log($"[SoundGroupTools] {(created ? $"Created bus \"{busName}\". " : "")}Routed {changed} group(s) to \"{busName}\": " +
                  string.Join(", ", _ticked.OrderBy(n => n)));
        Refresh();
    }
}
