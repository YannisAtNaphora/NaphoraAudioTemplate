using System.Reflection;
using UnityEditor;
using UnityEngine;
using Naphora.AudioKit.Core;

[CustomEditor(typeof(AudioIDReference))]
public class AudioIDReferenceEditor : Editor
{
    private static readonly Color HeaderColour  = new Color(0.18f, 0.18f, 0.18f);
    private static readonly Color RowEvenColour = new Color(0.22f, 0.22f, 0.22f);
    private static readonly Color RowOddColour  = new Color(0.26f, 0.26f, 0.26f);
    private static readonly Color CopiedColour  = new Color(0.20f, 0.55f, 0.20f);

    private string _copiedValue = null;
    private double _copiedTime  = 0;
    private const double CopiedFadeDuration = 1.5;

    public override void OnInspectorGUI()
    {
        // Repaint while the "Copied!" flash is active
        if (_copiedValue != null)
        {
            double elapsed = EditorApplication.timeSinceStartup - _copiedTime;
            if (elapsed < CopiedFadeDuration)
                Repaint();
            else
                _copiedValue = null;
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Audio ID Reference", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Copy any ID below and paste it as a MasterAudio Sound Group name.",
            EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.Space(6);

        FieldInfo[] fields = typeof(AudioConfig).GetFields(
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

        string currentGroup  = null;
        int    rowIndex      = 0;

        foreach (FieldInfo field in fields)
        {
            // Only care about const strings
            if (!field.IsLiteral || field.FieldType != typeof(string)) continue;

            // Derive a group header from the prefix (SFX_, Music_, etc.)
            string group = DeriveGroup(field.Name);
            if (group != currentGroup)
            {
                currentGroup = group;
                rowIndex     = 0;
                EditorGUILayout.Space(4);
                DrawRow(group, null, HeaderColour, EditorStyles.boldLabel, EditorStyles.boldLabel);
            }

            string value     = (string)field.GetRawConstantValue();
            bool   wasCopied = _copiedValue == value &&
                               (EditorApplication.timeSinceStartup - _copiedTime) < CopiedFadeDuration;

            Color bg = wasCopied ? CopiedColour
                                 : (rowIndex % 2 == 0 ? RowEvenColour : RowOddColour);

            if (DrawRow(field.Name, value, bg, EditorStyles.label, EditorStyles.label))
            {
                EditorGUIUtility.systemCopyBuffer = value;
                _copiedValue = value;
                _copiedTime  = EditorApplication.timeSinceStartup;
            }

            rowIndex++;
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox("Click any row to copy the ID to your clipboard.", MessageType.None);
    }

    /// <summary>Returns true if the row was clicked.</summary>
    private bool DrawRow(string label, string value, Color bg, GUIStyle labelStyle, GUIStyle valueStyle)
    {
        Rect rect = EditorGUILayout.GetControlRect(false, 20f);

        // Background
        EditorGUI.DrawRect(rect, bg);

        float padding = 6f;
        Rect  labelRect = new Rect(rect.x + padding,     rect.y + 2f, rect.width * 0.45f, rect.height);
        Rect  valueRect = new Rect(rect.x + rect.width * 0.47f, rect.y + 2f, rect.width * 0.53f - padding, rect.height);

        EditorGUI.LabelField(labelRect, label, labelStyle);
        if (value != null)
            EditorGUI.LabelField(valueRect, value, valueStyle);

        // Click detection
        Event e = Event.current;
        if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition) && value != null)
        {
            e.Use();
            return true;
        }

        return false;
    }

    private static string DeriveGroup(string fieldName)
    {
        int underscore = fieldName.IndexOf('_');
        return underscore > 0 ? fieldName.Substring(0, underscore) : "General";
    }
}
