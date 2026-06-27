using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Verwaltet allgemeine Spielzustände und Einstellungen, wie das Verhalten des Cursors.
/// </summary>
public class GameManager : MonoBehaviour
{
    [Header("Cursor Settings")]
    public bool lockCursorOnStart = true;

    [Header("Input Settings")]
    public KeyCode restartKey = KeyCode.R;
    public KeyCode quitKey = KeyCode.Escape;

    [Header("Scene Settings")]
    public bool reloadCurrentScene = true; // Wenn false, kannst du einen bestimmten Scene-Namen angeben
    public string sceneToLoad = ""; // Optional: Spezifischer Scene-Name

    // Die Start-Methode wird einmal beim Laden der Szene aufgerufen, bevor das erste Frame-Update.
    void Start()
    {
        if (lockCursorOnStart)
        {
            LockCursor();
        }
    }

    void Update()
    {
        // Restart-Funktion (R-Taste)
        if (Input.GetKeyDown(restartKey))
        {
            RestartGame();
        }

        // Quit-Funktion (Escape-Taste)
        if (Input.GetKeyDown(quitKey))
        {
            QuitGame();
        }
    }

    /// <summary>
    /// Sperrt und versteckt den Cursor
    /// </summary>
    void LockCursor()
    {
        // 1. Cursor ausblenden (Setze die Sichtbarkeit auf 'false').
        // Der Cursor wird somit im Spiel nicht mehr dargestellt.
        Cursor.visible = false;

        // 2. Cursor in der Mitte des Spiel-Fensters sperren.
        // Dies verhindert, dass der Cursor das Spielfenster verlässt und ist
        // essenziell für die Kamerasteuerung in First-Person-Spielen.
        Cursor.lockState = CursorLockMode.Locked;

        Debug.Log("Cursor wurde ausgeblendet und gesperrt.");
    }

    /// <summary>
    /// Entsperrt und zeigt den Cursor wieder an
    /// </summary>
    public void UnlockCursor()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        Debug.Log("Cursor wurde freigegeben und ist wieder sichtbar.");
    }

    /// <summary>
    /// Startet das aktuelle Level neu
    /// </summary>
    void RestartGame()
    {
        Debug.Log("Restarting game...");

        // Time.timeScale zurücksetzen (wichtig falls Slow-Motion aktiv war)
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;

        if (reloadCurrentScene)
        {
            // Lade die aktuelle Szene neu
            Scene currentScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(currentScene.name);
            Debug.Log($"Scene '{currentScene.name}' wird neu geladen.");
        }
        else if (!string.IsNullOrEmpty(sceneToLoad))
        {
            // Lade eine spezifische Szene
            SceneManager.LoadScene(sceneToLoad);
            Debug.Log($"Scene '{sceneToLoad}' wird geladen.");
        }
        else
        {
            Debug.LogWarning("Kein Scene-Name angegeben! Bitte 'reloadCurrentScene' aktivieren oder 'sceneToLoad' setzen.");
        }
    }

    /// <summary>
    /// Beendet das Spiel
    /// </summary>
    void QuitGame()
    {
        Debug.Log("Quitting game...");

#if UNITY_EDITOR
        // Im Unity Editor wird der Play Mode beendet
        UnityEditor.EditorApplication.isPlaying = false;
        Debug.Log("Play Mode beendet (Editor).");
#else
            // Im Build wird die Anwendung geschlossen
            Application.Quit();
            Debug.Log("Anwendung wird beendet.");
#endif
    }

    /// <summary>
    /// Öffentliche Methode zum Neustarten (kann von UI Buttons aufgerufen werden)
    /// </summary>
    public void RestartButton()
    {
        RestartGame();
    }

    /// <summary>
    /// Öffentliche Methode zum Beenden (kann von UI Buttons aufgerufen werden)
    /// </summary>
    public void QuitButton()
    {
        QuitGame();
    }

    /* * HINWEIS: Für ein vollständiges Spiel solltest du möglicherweise Logik
     * hinzufügen, um den Cursor z.B. bei Pause-Menüs oder im Inventar
     * mit UnlockCursor() wieder freizugeben.
     * 
     * WICHTIG: Stelle sicher, dass die Scene in den Build Settings hinzugefügt ist!
     * File -> Build Settings -> Add Open Scenes
     */
}