using System;
using UnityEngine;
using UnityEditor;

/// <summary>
/// Converts DynamicSoundGroup + DynamicGroupVariation components to
/// MasterAudioGroup + SoundGroupVariation using reflection, so no
/// direct assembly reference to MasterAudio is needed.
/// Run via: Tools > Audio > Convert Dynamic Sound Groups
/// </summary>
public static class ConvertDynamicGroupsEditor
{
    private const string DynamicVariationType   = "DarkTonic.MasterAudio.DynamicGroupVariation";
    private const string DynamicGroupType       = "DarkTonic.MasterAudio.DynamicSoundGroup";
    private const string MAGroupType            = "DarkTonic.MasterAudio.MasterAudioGroup";
    private const string SGVariationType        = "DarkTonic.MasterAudio.SoundGroupVariation";
    private const string SGVariationUpdaterType = "DarkTonic.MasterAudio.SoundGroupVariationUpdater";

    [MenuItem("Tools/Audio/Convert Dynamic Sound Groups")]
    public static void ConvertAll()
    {
        Type typeDGV  = FindType(DynamicVariationType);
        Type typeDSG  = FindType(DynamicGroupType);
        Type typeMAG  = FindType(MAGroupType);
        Type typeSGV  = FindType(SGVariationType);
        Type typeSGVU = FindType(SGVariationUpdaterType);

        if (typeDGV == null || typeDSG == null || typeMAG == null || typeSGV == null)
        {
            EditorUtility.DisplayDialog("Convert Dynamic Sound Groups",
                "Could not find MasterAudio types. Make sure MasterAudio is imported correctly.", "OK");
            return;
        }

        int groupsConverted     = 0;
        int variationsConverted = 0;

        // ── Step 1: Convert DynamicGroupVariation -> SoundGroupVariation ──
        foreach (MonoBehaviour mb in GameObject.FindObjectsOfType(typeDGV, true))
        {
            GameObject go = mb.gameObject;
            if (go.GetComponent(typeSGV) != null) continue;

            Undo.RecordObject(go, "Convert Dynamic Group Variation");
            CopyFields(mb, Undo.DestroyObjectImmediate, go, typeSGV, typeSGVU, isDynVariation: true);

            EditorUtility.SetDirty(go);
            variationsConverted++;
            Debug.Log($"[ConvertDynamicGroups] Variation converted: {go.name}");
        }

        // ── Step 2: Convert DynamicSoundGroup -> MasterAudioGroup ─────────
        foreach (MonoBehaviour mb in GameObject.FindObjectsOfType(typeDSG, true))
        {
            GameObject go = mb.gameObject;
            if (go.GetComponent(typeMAG) != null) continue;

            Undo.RecordObject(go, "Convert Dynamic Sound Group");

            // Cache all serialised field values via reflection
            var fields = typeDSG.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var values = new System.Collections.Generic.Dictionary<string, object>();
            foreach (var f in fields)
            {
                try { values[f.Name] = f.GetValue(mb); } catch { }
            }

            Undo.DestroyObjectImmediate(mb);

            Component newComp = Undo.AddComponent(go, typeMAG);
            var newFields = typeMAG.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (var f in newFields)
            {
                if (values.TryGetValue(f.Name, out object val))
                {
                    try { f.SetValue(newComp, val); } catch { }
                }
            }

            EditorUtility.SetDirty(go);
            groupsConverted++;
            Debug.Log($"[ConvertDynamicGroups] Group converted: {go.name}");
        }

        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("Convert Dynamic Sound Groups",
            $"Done.\nGroups converted: {groupsConverted}\nVariations converted: {variationsConverted}\n\nSave your scene and prefab now.", "OK");
    }

    private static void CopyFields(MonoBehaviour source, Action<UnityEngine.Object> destroy,
        GameObject go, Type targetType, Type updaterType, bool isDynVariation)
    {
        var srcFields = source.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        var values = new System.Collections.Generic.Dictionary<string, object>();
        foreach (var f in srcFields)
        {
            try { values[f.Name] = f.GetValue(source); } catch { }
        }

        destroy(source);

        if (isDynVariation && updaterType != null && go.GetComponent(updaterType) == null)
            Undo.AddComponent(go, updaterType);

        Component newComp = Undo.AddComponent(go, targetType);
        var tgtFields = targetType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        foreach (var f in tgtFields)
        {
            if (values.TryGetValue(f.Name, out object val))
            {
                try { f.SetValue(newComp, val); } catch { }
            }
        }
    }

    private static Type FindType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type t = assembly.GetType(fullName);
            if (t != null) return t;
        }
        return null;
    }
}
