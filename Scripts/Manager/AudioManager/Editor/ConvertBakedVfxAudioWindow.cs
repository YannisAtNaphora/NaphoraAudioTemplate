using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Drag-and-drop fix for the bug class BakedVfxAudioImportWarning flags: a VFX prefab
// with its own baked AudioSource (Play On Awake + a real clip), bypassing
// AudioManager/Master Audio entirely. Drop the prefab in, review/adjust the Sound
// Group settings it proposes per baked AudioSource found, hit Convert -- it creates a
// matching Sound Group on MasterAudio.prefab (Edit-Mode safe, no Play Mode needed,
// via SoundGroupTemplateAuthoring), removes the baked AudioSource so the VFX becomes
// visual-only, and adds the matching AudioConfig constant.
//
// It deliberately does NOT add the AudioManager.Trigger() call anywhere -- picking the
// right call site (which gameplay event should actually fire this sound) needs a human
// (or an engineer/Claude) who understands when this VFX is meant to play, not something
// a generic drag-and-drop tool can infer.
// Run via: Naphora/Audio/Convert Baked VFX Audio...
public class ConvertBakedVfxAudioWindow : EditorWindow
{
    private const string MasterAudioPrefabPath = "Assets/NaphoraAudioTemplate/Audio/Prefabs/MasterAudio.prefab";
    private const string AudioConfigPath = "Assets/NaphoraAudioTemplate/Scripts/Manager/AudioManager/AudioConfig.cs";

    // ConvertBakedVfxAudioWindow.EnsureAudioConfigConstant matches on this exact text
    // to find where to insert new constants -- see AudioConfig.cs.
    private const string AudioConfigInsertMarker =
        "// Naphora/Audio/Convert Baked VFX Audio... appends new constants directly\n" +
        "    // below this marker line -- do not remove/reword it, the tool matches on\n" +
        "    // this exact text to find its insertion point.";

    private class Candidate
    {
        public string HierarchyPath;
        public AudioClip Clip;
        public string GroupName;
        public float Volume;
        public float Cooldown = 0.08f;
        public int MaxVoices = 4;
        public float PitchVariation = 0.08f;
        public string BusName = "[GSFX] GAMEPLAY";
        public bool Done;
    }

    private GameObject _target;
    private readonly List<Candidate> _candidates = new List<Candidate>();

    [MenuItem("Naphora/Audio/Convert Baked VFX Audio...")]
    public static void Open()
    {
        GetWindow<ConvertBakedVfxAudioWindow>("Convert Baked VFX Audio");
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Drag a VFX prefab with a baked AudioSource (Play On Awake + a clip) here. " +
            "Convert creates a matching Sound Group on MasterAudio, removes that AudioSource, and " +
            "adds the AudioConfig constant. You still need to add one AudioManager.Trigger() call " +
            "in the code that spawns/activates this VFX -- picking the right call site needs a human.",
            MessageType.Info);

        EditorGUILayout.Space();
        GameObject newTarget = (GameObject)EditorGUILayout.ObjectField("VFX Prefab", _target, typeof(GameObject), false);
        if (newTarget != _target)
        {
            _target = newTarget;
            RescanTarget();
        }

        if (_target == null)
            return;

        if (_candidates.Count == 0)
        {
            EditorGUILayout.HelpBox("No baked Play-On-Awake AudioSource with a clip found on this prefab.", MessageType.None);
            return;
        }

        foreach (var c in _candidates)
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField(c.HierarchyPath, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Clip", c.Clip != null ? c.Clip.name : "(none)");
                c.GroupName = EditorGUILayout.TextField("Sound Group Name", c.GroupName);
                c.BusName = EditorGUILayout.TextField("Bus", c.BusName);
                c.Volume = EditorGUILayout.Slider("Volume", c.Volume, 0f, 1f);
                c.Cooldown = EditorGUILayout.FloatField("Cooldown (s)", c.Cooldown);
                c.MaxVoices = EditorGUILayout.IntField("Max Voices", c.MaxVoices);
                c.PitchVariation = EditorGUILayout.Slider("Pitch Variation", c.PitchVariation, 0f, 0.25f);

                using (new EditorGUI.DisabledScope(c.Done))
                {
                    if (GUILayout.Button(c.Done ? "Converted" : "Convert"))
                        Convert(c);
                }
            }
        }
    }

    private void RescanTarget()
    {
        _candidates.Clear();
        if (_target == null)
            return;

        foreach (var source in _target.GetComponentsInChildren<AudioSource>(true))
        {
            AudioClip clip = source.clip != null ? source.clip : source.resource as AudioClip;
            if (!source.playOnAwake || clip == null)
                continue;

            _candidates.Add(new Candidate
            {
                HierarchyPath = GetHierarchyPath(source.transform, _target.transform),
                Clip = clip,
                GroupName = "sfx_" + Regex.Replace(clip.name, "[^a-zA-Z0-9]+", "_").ToLowerInvariant().Trim('_'),
                Volume = source.volume,
            });
        }
    }

    private void Convert(Candidate c)
    {
        string prefabPath = AssetDatabase.GetAssetPath(_target);
        if (string.IsNullOrEmpty(prefabPath))
        {
            EditorUtility.DisplayDialog("Convert Baked VFX Audio",
                "Could not resolve an asset path for this prefab -- drag the prefab asset from the Project window, not a scene instance.",
                "OK");
            return;
        }

        bool groupCreated;
        GameObject masterAudioRoot = PrefabUtility.LoadPrefabContents(MasterAudioPrefabPath);
        try
        {
            if (masterAudioRoot.transform.Find(c.GroupName) != null)
            {
                groupCreated = false;
            }
            else
            {
                SoundGroupTemplateAuthoring.CloneGroupFromTemplate(
                    masterAudioRoot, c.GroupName, c.Clip,
                    volume: c.Volume,
                    cooldownSeconds: c.Cooldown,
                    maxVoices: c.MaxVoices,
                    pitchVariation: c.PitchVariation,
                    busName: c.BusName);
                PrefabUtility.SaveAsPrefabAsset(masterAudioRoot, MasterAudioPrefabPath);
                groupCreated = true;
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(masterAudioRoot);
        }

        GameObject vfxRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Transform found = FindByPath(vfxRoot.transform, c.HierarchyPath);
            AudioSource source = found != null ? found.GetComponent<AudioSource>() : null;
            if (source != null)
            {
                // Removed outright, not just nulled/disabled -- a disabled AudioSource still
                // holding a clip reference still ships in the build (Unity decides build
                // inclusion by walking serialized references, not enabled/playOnAwake state).
                Object.DestroyImmediate(source, true);
                PrefabUtility.SaveAsPrefabAsset(vfxRoot, prefabPath);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(vfxRoot);
        }

        string constantName = EnsureAudioConfigConstant(c.GroupName);

        c.Done = true;
        string nextSteps =
            $"Sound Group \"{c.GroupName}\" is ready in MasterAudio ({(groupCreated ? "newly created" : "already existed, reused")}), " +
            $"the baked AudioSource on \"{c.HierarchyPath}\" is removed, and AudioConfig.{constantName} is added.\n\n" +
            $"Still needed (code side):\n" +
            $"Call AudioManager.Trigger(AudioConfig.{constantName}) from wherever this VFX actually gets spawned/activated in code.";

        Debug.Log($"[ConvertBakedVfxAudio] {nextSteps}");
        EditorUtility.DisplayDialog("Convert Baked VFX Audio", nextSteps, "OK");
    }

    // Adds `public const string <Name> = "<groupName>";` to AudioConfig.cs, right after
    // AudioConfigInsertMarker, unless a constant for this exact groupName already exists
    // (in which case that existing constant's name is reused). Returns the constant's
    // identifier name either way. Constant names are PascalCase from groupName's
    // underscore-separated words (e.g. "sfx_minion_explode" -> "Sfx_MinionExplode"),
    // de-duplicated against existing identifiers if that name is already taken by a
    // different value.
    internal static string EnsureAudioConfigConstant(string groupName)
    {
        string text = File.ReadAllText(AudioConfigPath);

        Match existingByValue = Regex.Match(text, "public const string (\\w+) = \"" + Regex.Escape(groupName) + "\";");
        if (existingByValue.Success)
            return existingByValue.Groups[1].Value;

        string baseName = ToConstantName(groupName);
        string candidateName = baseName;
        int suffix = 2;
        while (Regex.IsMatch(text, "public const string " + Regex.Escape(candidateName) + " "))
            candidateName = baseName + suffix++;

        int markerIndex = text.IndexOf(AudioConfigInsertMarker, System.StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            Debug.LogWarning("[ConvertBakedVfxAudio] Could not find the insert marker in AudioConfig.cs -- " +
                $"add this constant manually:\n    public const string {candidateName} = \"{groupName}\";");
            return candidateName;
        }

        int insertAt = text.IndexOf('\n', markerIndex) + 1;
        string newLine = $"\n    public const string {candidateName} = \"{groupName}\";\n";
        text = text.Insert(insertAt, newLine);

        File.WriteAllText(AudioConfigPath, text, new UTF8Encoding(false));
        AssetDatabase.ImportAsset(AudioConfigPath, ImportAssetOptions.ForceUpdate);

        return candidateName;
    }

    private static string ToConstantName(string groupName)
    {
        string[] parts = groupName.Split(new[] { '_' }, System.StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i];
            sb.Append(char.ToUpperInvariant(p[0]));
            if (p.Length > 1)
                sb.Append(p.Substring(1));
            if (i == 0)
                sb.Append('_');
        }

        return sb.ToString();
    }

    private static string GetHierarchyPath(Transform t, Transform root)
    {
        if (t == root)
            return t.name;

        return GetHierarchyPath(t.parent, root) + "/" + t.name;
    }

    private static Transform FindByPath(Transform root, string path)
    {
        string[] parts = path.Split('/');
        Transform current = root;
        for (int i = 1; i < parts.Length; i++)
        {
            current = current.Find(parts[i]);
            if (current == null)
                return null;
        }

        return current;
    }
}
