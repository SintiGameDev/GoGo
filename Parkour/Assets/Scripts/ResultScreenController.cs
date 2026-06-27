using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Wartet, bis der TimerController das Level final gestoppt hat (Goal erreicht),
/// und ermöglicht dem Spieler dann per Tastendruck:
///   R          -> aktuelle Szene neu starten
///   Leertaste  -> nächstes Level laden (über SceneDirector)
///
/// Getrennt von GoalTrigger gehalten, da GoalTrigger für Kollisionserkennung
/// zuständig ist und dieses Script für die Post-Result-Eingabe.
/// </summary>
public class ResultScreenController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Referenz zum TimerController (auto-detected wenn leer)")]
    public TimerController timerController;

    [Tooltip("Referenz zum SceneDirector für 'nächstes Level' (auto-detected wenn leer)")]
    public SceneDirector sceneDirector;

    [Header("Input")]
    public KeyCode restartKey = KeyCode.R;
    public KeyCode nextLevelKey = KeyCode.Space;

    [Tooltip("Zusätzliche Verzögerung nach Zielankunft, bevor Input akzeptiert wird (verhindert versehentliches Sofort-Restarten durch denselben Tastendruck, der z.B. eine andere Aktion ausgelöst hat)")]
    public float inputDelayAfterFinish = 0.3f;

    [Header("Debug")]
    public bool showDebugInfo = true;

    private bool finished = false;
    private float finishedAt = 0f;

    void Awake()
    {
        if (timerController == null)
            timerController = FindObjectOfType<TimerController>();

        if (sceneDirector == null)
            sceneDirector = FindObjectOfType<SceneDirector>();

        if (timerController == null && showDebugInfo)
            Debug.LogError("❌ ResultScreenController: Kein TimerController gefunden!");

        if (sceneDirector == null && showDebugInfo)
            Debug.LogWarning("⚠️ ResultScreenController: Kein SceneDirector gefunden. 'Nächstes Level' wird nicht funktionieren.");
    }

    void OnEnable()
    {
        if (timerController != null)
            timerController.OnTimerStopped += HandleTimerStopped;
    }

    void OnDisable()
    {
        if (timerController != null)
            timerController.OnTimerStopped -= HandleTimerStopped;
    }

    void HandleTimerStopped(float finalTime, TimerController.MedalRank rank)
    {
        finished = true;
        // Time.unscaledTime statt Time.time, da beim Zielerreichen oft Time.timeScale
        // durch Slow-Motion verändert ist (siehe GoalTrigger.SlowMotionSequence)
        finishedAt = Time.unscaledTime;

        if (showDebugInfo)
            Debug.Log("🎮 ResultScreenController: Wartet jetzt auf Input (R = Neustart, Leertaste = Nächstes Level)");
    }

    void Update()
    {
        if (!finished)
            return;

        if (Time.unscaledTime - finishedAt < inputDelayAfterFinish)
            return;

        if (Input.GetKeyDown(restartKey))
        {
            RestartLevel();
        }
        else if (Input.GetKeyDown(nextLevelKey))
        {
            LoadNextLevel();
        }
    }

    void RestartLevel()
    {
        if (showDebugInfo)
            Debug.Log("🔄 ResultScreenController: Neustart der aktuellen Szene");

        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;

        Scene currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.name);
    }

    void LoadNextLevel()
    {
        if (sceneDirector == null)
        {
            Debug.LogError("❌ ResultScreenController: Kein SceneDirector zum Laden des nächsten Levels!");
            return;
        }

        if (showDebugInfo)
            Debug.Log("➡️ ResultScreenController: Lade nächstes Level");

        sceneDirector.LoadNextScene();
    }
}
