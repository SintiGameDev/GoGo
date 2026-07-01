// GradientRampBaker.cs
// Backing-Script fuer den Traumwelt/GradientRamp Shader.
// Gradient im Inspector malen, Ziel-Material zuweisen, dann "Bake & Assign" druecken.
// Kein Auto-Bake / kein OnValidate (vermeidet GUIStyle-Redraw-Fehler in Unity).

using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(fileName = "GradientRamp", menuName = "Traumwelt/Gradient Ramp Baker")]
public class GradientRampBaker : ScriptableObject
{
    [Tooltip("Der Farbverlauf, der in die Ramp-Textur gebacken wird.")]
    public Gradient gradient = new Gradient();

    [Tooltip("Breite der Ramp-Textur in Pixeln. 256 reicht meist.")]
    [Range(16, 1024)]
    public int resolution = 256;

    [Tooltip("Ziel-Material mit dem Traumwelt/GradientRamp Shader.")]
    public Material targetMaterial;

    [Tooltip("Shader-Property, in die die Textur geschrieben wird.")]
    public string textureProperty = "_RampTex";

#if UNITY_EDITOR
    public void BakeAndAssign()
    {
        if (gradient == null)      { Debug.LogWarning("[GradientRampBaker] Kein Gradient.");      return; }
        if (targetMaterial == null){ Debug.LogWarning("[GradientRampBaker] Kein Ziel-Material."); return; }

        string bakerPath = AssetDatabase.GetAssetPath(this);
        string dir       = string.IsNullOrEmpty(bakerPath) ? "Assets" : System.IO.Path.GetDirectoryName(bakerPath);
        string texPath   = $"{dir}/{name}_RampTex.asset";

        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

        if (tex == null || tex.width != resolution)
        {
            tex            = new Texture2D(resolution, 1, TextureFormat.RGBA32, false, false);
            tex.wrapMode   = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            AssetDatabase.CreateAsset(tex, texPath);
        }

        var pixels = new Color[resolution];
        for (int x = 0; x < resolution; x++)
        {
            float t = resolution == 1 ? 0f : (float)x / (resolution - 1);
            pixels[x] = gradient.Evaluate(t);
        }
        tex.SetPixels(pixels);
        tex.Apply(false, false);

        targetMaterial.SetTexture(textureProperty, tex);
        EditorUtility.SetDirty(tex);
        EditorUtility.SetDirty(targetMaterial);
        AssetDatabase.SaveAssets();

        Debug.Log($"[GradientRampBaker] Fertig: {texPath} -> '{textureProperty}' auf '{targetMaterial.name}'.");
    }
#endif
}

#if UNITY_EDITOR
[CustomEditor(typeof(GradientRampBaker))]
public class GradientRampBakerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();

        var baker = (GradientRampBaker)target;
        if (GUILayout.Button("Bake & Assign", GUILayout.Height(30)))
            baker.BakeAndAssign();

        EditorGUILayout.HelpBox(
            "1) Gradient malen  2) Ziel-Material zuweisen  3) Bake & Assign.\n" +
            "Erneut backen nach jeder Gradient-Aenderung.",
            MessageType.Info);
    }
}
#endif
