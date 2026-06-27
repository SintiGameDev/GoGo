using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Verwaltet Scene-Loading. Wird von GoalTrigger aufgerufen.
/// </summary>
public class SceneDirector : MonoBehaviour
{
    [Header("Scene Management")]
    [Tooltip("Ordnerpfad relativ zu Assets/ (z.B. 'Scenes/Levels')")]
    public string sceneFolderPath = "Scenes";

    [Tooltip("Liste aller verfügbaren Scenes (automatisch gefüllt)")]
    public List<string> sceneList = new List<string>();

    [Tooltip("Aktueller Scene-Index in der Liste")]
    public int currentSceneIndex = 0;

    [Header("Transition Settings")]
    [Tooltip("Verzögerung vor dem Scene-Wechsel")]
    public float transitionDelay = 0.1f;

    [Tooltip("Loop zurück zum Anfang wenn alle Scenes durch sind")]
    public bool loopScenes = true;

    [Header("Debug")]
    public bool showDebugInfo = true;

    void Start()
    {
        FindCurrentSceneIndex();

        if (showDebugInfo)
        {
            Debug.Log($"🎬 SceneDirector gestartet");
            Debug.Log($"   Aktuelle Scene: {SceneManager.GetActiveScene().name} (Index: {currentSceneIndex})");
            Debug.Log($"   Scenes in Liste: {sceneList.Count}");
        }
    }

    void FindCurrentSceneIndex()
    {
        if (sceneList.Count == 0)
        {
            Debug.LogWarning("⚠️ SceneDirector: Scene Liste ist leer! Klicke 'Refresh Scene List' im Inspector.");
            return;
        }

        string currentSceneName = SceneManager.GetActiveScene().name;
        currentSceneIndex = sceneList.FindIndex(s => s == currentSceneName);

        if (currentSceneIndex == -1)
        {
            Debug.LogWarning($"⚠️ SceneDirector: Scene '{currentSceneName}' nicht in der Liste gefunden!");
            currentSceneIndex = 0;
        }
    }

    /// <summary>
    /// Lädt die nächste Scene in der Liste
    /// </summary>
    public void LoadNextScene()
    {
        StartCoroutine(LoadNextSceneCoroutine());
    }

    IEnumerator LoadNextSceneCoroutine()
    {
        if (transitionDelay > 0)
        {
            yield return new WaitForSeconds(transitionDelay);
        }

        if (sceneList.Count == 0)
        {
            Debug.LogError("❌ SceneDirector: Keine Scenes in der Liste!");
            yield break;
        }

        // Nächsten Index berechnen
        int nextIndex = currentSceneIndex + 1;

        // Loop-Logik
        if (nextIndex >= sceneList.Count)
        {
            if (loopScenes)
            {
                nextIndex = 0;
                Debug.Log("🔄 SceneDirector: Ende erreicht, Loop zurück zum Anfang");
            }
            else
            {
                Debug.Log("🏁 SceneDirector: Alle Scenes abgeschlossen!");
                yield break;
            }
        }

        string nextSceneName = sceneList[nextIndex];

        Debug.Log($"📂 SceneDirector: Lade Scene {nextIndex}: '{nextSceneName}'");

        // Time Scale sicherstellen (falls Slow Motion aktiv war)
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;

        // Scene laden
        SceneManager.LoadScene(nextSceneName);
    }

    /// <summary>
    /// Lädt eine bestimmte Scene per Index
    /// </summary>
    public void LoadSceneByIndex(int index)
    {
        if (index < 0 || index >= sceneList.Count)
        {
            Debug.LogError($"❌ SceneDirector: Index {index} außerhalb der Liste (0-{sceneList.Count - 1})!");
            return;
        }

        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;

        Debug.Log($"📂 SceneDirector: Lade Scene {index}: '{sceneList[index]}'");
        SceneManager.LoadScene(sceneList[index]);
    }

    /// <summary>
    /// Lädt eine Scene per Name
    /// </summary>
    public void LoadSceneByName(string sceneName)
    {
        if (!sceneList.Contains(sceneName))
        {
            Debug.LogError($"❌ SceneDirector: Scene '{sceneName}' nicht in der Liste!");
            return;
        }

        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;

        Debug.Log($"📂 SceneDirector: Lade Scene '{sceneName}'");
        SceneManager.LoadScene(sceneName);
    }

    /// <summary>
    /// Lädt die aktuelle Scene neu
    /// </summary>
    public void ReloadCurrentScene()
    {
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;

        string currentScene = SceneManager.GetActiveScene().name;
        Debug.Log($"🔄 SceneDirector: Lade Scene neu: '{currentScene}'");
        SceneManager.LoadScene(currentScene);
    }

#if UNITY_EDITOR
    /// <summary>
    /// Sucht automatisch alle Scenes im Ordner (Editor Only)
    /// </summary>
    public void RefreshSceneList()
    {
        sceneList.Clear();

        string searchPath = "Assets/" + sceneFolderPath;
        string[] sceneGUIDs = AssetDatabase.FindAssets("t:Scene", new[] { searchPath });

        if (sceneGUIDs.Length == 0)
        {
            Debug.LogWarning($"⚠️ Keine Scenes gefunden in '{searchPath}'");
            return;
        }

        foreach (string guid in sceneGUIDs)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string sceneName = System.IO.Path.GetFileNameWithoutExtension(path);
            sceneList.Add(sceneName);
        }

        sceneList.Sort(); // Alphabetisch sortieren

        Debug.Log($"✅ {sceneList.Count} Scenes gefunden in '{searchPath}'");

        FindCurrentSceneIndex();
    }
#endif
}

#if UNITY_EDITOR
[CustomEditor(typeof(SceneDirector))]
public class SceneDirectorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        SceneDirector director = (SceneDirector)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Scene Management", EditorStyles.boldLabel);

        // Refresh Button
        if (GUILayout.Button("Refresh Scene List", GUILayout.Height(35)))
        {
            director.RefreshSceneList();
            EditorUtility.SetDirty(director);
        }

        EditorGUILayout.Space(5);

        // Info Box
        string statusMessage = $"📁 Scenes gefunden: {director.sceneList.Count}\n" +
                              $"📍 Aktueller Index: {director.currentSceneIndex}\n" +
                              $"📂 Pfad: Assets/{director.sceneFolderPath}";

        EditorGUILayout.HelpBox(statusMessage, MessageType.Info);

        // Scene Liste anzeigen
        if (director.sceneList.Count > 0)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Scene Reihenfolge:", EditorStyles.boldLabel);

            EditorGUI.indentLevel++;
            for (int i = 0; i < director.sceneList.Count; i++)
            {
                bool isCurrent = (i == director.currentSceneIndex);
                string prefix = isCurrent ? "▶ " : "   ";

                GUIStyle style = new GUIStyle(EditorStyles.label);
                if (isCurrent)
                {
                    style.fontStyle = FontStyle.Bold;
                    style.normal.textColor = Color.green;
                }

                EditorGUILayout.LabelField($"{prefix}{i}: {director.sceneList[i]}", style);
            }
            EditorGUI.indentLevel--;
        }
        else
        {
            EditorGUILayout.HelpBox("❌ Keine Scenes in der Liste! Klicke 'Refresh Scene List'", MessageType.Warning);
        }

        // Play Mode Controls
        if (Application.isPlaying)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Play Mode Controls", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("⏭ Next Scene", GUILayout.Height(25)))
            {
                director.LoadNextScene();
            }
            if (GUILayout.Button("🔄 Reload", GUILayout.Height(25)))
            {
                director.ReloadCurrentScene();
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif