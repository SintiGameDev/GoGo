// =============================================================
//  MatcapGenerator.cs
//  EDITOR-Script -> muss in einem Ordner namens "Editor" liegen!
//  z.B. Assets/Editor/MatcapGenerator.cs
//
//  Rendert eine beleuchtete Kugel prozedural in eine Textur
//  und speichert sie als PNG. Menue: Tools > Traumwelt > Matcap Generator
// =============================================================
using UnityEditor;
using UnityEngine;
using System.IO;

public class MatcapGenerator : EditorWindow
{
    // --- Presets ---
    enum Preset { Clay, Chrome, Wax, SciFiRim }

    Preset _preset      = Preset.Clay;
    int    _size        = 512;
    Color  _baseColor   = new Color(0.8f, 0.45f, 0.35f); // Ton-Orange
    Color  _lightColor  = Color.white;
    Vector3 _lightDir   = new Vector3(-0.5f, 0.6f, 0.7f); // von oben-links-vorne
    float  _specPower   = 32f;   // Glanzlicht-Schaerfe
    float  _specStrength= 0.6f;
    float  _ambient     = 0.15f;
    bool   _transparentBG = false; // Hintergrund durchsichtig statt schwarz?

    [MenuItem("Tools/Traumwelt/Matcap Generator")]
    static void Open() => GetWindow<MatcapGenerator>("Matcap Generator");

    void OnGUI()
    {
        EditorGUILayout.LabelField("Matcap Generator", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        var newPreset = (Preset)EditorGUILayout.EnumPopup("Preset", _preset);
        if (newPreset != _preset) { _preset = newPreset; ApplyPreset(); }

        _size          = EditorGUILayout.IntPopup("Aufloesung", _size,
                            new[] {"256","512","1024"}, new[] {256,512,1024});
        _baseColor     = EditorGUILayout.ColorField("Basis-Farbe", _baseColor);
        _lightColor    = EditorGUILayout.ColorField("Licht-Farbe", _lightColor);
        _lightDir      = EditorGUILayout.Vector3Field("Licht-Richtung", _lightDir);
        _specPower     = EditorGUILayout.Slider("Glanz-Schaerfe", _specPower, 1f, 256f);
        _specStrength  = EditorGUILayout.Slider("Glanz-Staerke", _specStrength, 0f, 2f);
        _ambient       = EditorGUILayout.Slider("Ambient", _ambient, 0f, 1f);
        _transparentBG = EditorGUILayout.Toggle("Hintergrund transparent", _transparentBG);

        EditorGUILayout.Space();
        if (GUILayout.Button("Matcap generieren & speichern"))
            Generate();

        EditorGUILayout.HelpBox(
            "Tipp: Nach dem Import den Wrap Mode auf 'Clamp' stellen.\n" +
            "Bei transparentem BG: Alpha Is Transparency aktivieren.",
            MessageType.Info);
    }

    void ApplyPreset()
    {
        switch (_preset)
        {
            case Preset.Clay:
                _baseColor = new Color(0.8f, 0.45f, 0.35f);
                _specPower = 16f; _specStrength = 0.25f; _ambient = 0.2f;
                break;
            case Preset.Chrome:
                _baseColor = new Color(0.55f, 0.6f, 0.7f);
                _specPower = 128f; _specStrength = 1.4f; _ambient = 0.05f;
                break;
            case Preset.Wax:
                _baseColor = new Color(0.95f, 0.85f, 0.75f);
                _specPower = 8f; _specStrength = 0.4f; _ambient = 0.35f;
                break;
            case Preset.SciFiRim:
                _baseColor = new Color(0.02f, 0.05f, 0.12f); // fast dunkel
                _specPower = 64f; _specStrength = 0.5f; _ambient = 0.0f;
                break;
        }
    }

    void Generate()
    {
        var tex = new Texture2D(_size, _size, TextureFormat.RGBA32, false);
        var pixels = new Color[_size * _size];

        Vector3 L = _lightDir.normalized;
        Vector3 viewDir = new Vector3(0, 0, 1); // Kamera schaut entlang +Z

        for (int y = 0; y < _size; y++)
        for (int x = 0; x < _size; x++)
        {
            // Pixel -> [-1..1]
            float u = (x + 0.5f) / _size * 2f - 1f;
            float v = (y + 0.5f) / _size * 2f - 1f;
            float r2 = u * u + v * v;

            Color outCol;

            if (r2 > 1f)
            {
                // ausserhalb der Kugel
                outCol = _transparentBG ? new Color(0,0,0,0) : Color.black;
            }
            else
            {
                // Kugel-Normale rekonstruieren: z aus der Kugelgleichung
                float z = Mathf.Sqrt(1f - r2);
                Vector3 N = new Vector3(u, v, z); // schon normalisiert (Einheitskugel)

                // Lambert-Diffus
                float diff = Mathf.Max(0f, Vector3.Dot(N, L));

                // Blinn-Phong-Glanzlicht
                Vector3 H = (L + viewDir).normalized;
                float spec = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(N, H)), _specPower);

                Color c = _baseColor * (_ambient + diff)
                        + _lightColor * (spec * _specStrength);

                // SciFiRim: Rand kuenstlich aufleuchten lassen
                if (_preset == Preset.SciFiRim)
                {
                    float rim = Mathf.Pow(1f - z, 3f); // z klein am Rand
                    c += new Color(0.2f, 0.6f, 1.0f) * rim * 1.5f;
                }

                c.a = 1f;
                outCol = c;
            }

            pixels[y * _size + x] = outCol;
        }

        tex.SetPixels(pixels);
        tex.Apply();

        // Speichern
        string dir = "Assets/Matcaps";
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        string path = $"{dir}/Matcap_{_preset}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.Refresh();

        // Import-Settings automatisch korrekt setzen
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null)
        {
            imp.wrapMode = TextureWrapMode.Clamp; // wichtig fuer Matcaps!
            imp.alphaIsTransparency = _transparentBG;
            imp.SaveAndReimport();
        }

        Debug.Log($"Matcap gespeichert: {path}");
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
    }
}
