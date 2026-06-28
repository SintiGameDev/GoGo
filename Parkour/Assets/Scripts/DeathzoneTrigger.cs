using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Wird auf ein GameObject mit dem Tag "Deathzone" gesetzt (z.B. ein unsichtbarer
/// Trigger-Collider unterhalb des Levels, oder eine Gefahrenzone). Erkennt
/// Spieler-Kontakt per Trigger und lädt die aktuelle Szene direkt per
/// SceneManager.LoadScene() neu - bewusst UNABHÄNGIG von SceneDirector, damit
/// der Tod-Neustart auch funktioniert, falls SceneDirector fehlt oder seine
/// sceneList fehlerhaft konfiguriert ist.
///
/// Setup: Tag des GameObjects auf "Deathzone" setzen (in Unity zuerst unter
/// Tags & Layers anlegen, falls noch nicht vorhanden), Collider mit isTrigger=true.
/// </summary>
public class DeathzoneTrigger : MonoBehaviour
{
    [Header("Deathzone Settings")]
    [Tooltip("Tag des Spielers")]
    public string playerTag = "Player";

    [Tooltip("Sollte automatisch auf 'Deathzone' gesetzt sein")]
    public string deathzoneTag = "Deathzone";

    [Header("Effekt vor dem Neustart")]
    [Tooltip("Sound beim Berühren der Deathzone (optional)")]
    public AudioClip deathSound;

    [Tooltip("Kurze Slow-Motion vor dem Neustart aktivieren")]
    public bool useSlowMotion = true;
    public float slowMotionScale = 0.2f;
    public float slowMotionDuration = 0.4f;

    [Header("Debug")]
    public bool showDebugInfo = true;

    private bool hasBeenTriggered = false;

    void Awake()
    {
        // Stelle sicher, dass der Tag korrekt ist (analog zu GoalTrigger)
        if (!gameObject.CompareTag(deathzoneTag))
        {
            Debug.LogWarning($"⚠️ DeathzoneTrigger ({gameObject.name}): Tag ist '{gameObject.tag}' statt '{deathzoneTag}'. Setze Tag auf 'Deathzone'...");
            gameObject.tag = deathzoneTag;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (hasBeenTriggered)
            return;

        if (IsPlayer(other.gameObject))
        {
            OnDeathzoneReached();
        }
    }

    // Collision-Variante zusätzlich, falls der Deathzone-Collider mal nicht als
    // Trigger konfiguriert ist (analog zu GoalTrigger.OnCollisionEnter)
    void OnCollisionEnter(Collision collision)
    {
        if (hasBeenTriggered)
            return;

        if (showDebugInfo)
        {
            Debug.Log($"💥 DeathzoneTrigger: OnCollisionEnter - {collision.gameObject.name}. Hinweis: Collider sollte idealerweise isTrigger=true sein.");
        }

        if (IsPlayer(collision.gameObject))
        {
            OnDeathzoneReached();
        }
    }

    bool IsPlayer(GameObject obj)
    {
        if (obj.CompareTag(playerTag))
            return true;

        // Parent-Hierarchy prüfen (wichtig für Child-Collider, z.B. beim FPSController)
        Transform current = obj.transform;
        while (current != null)
        {
            if (current.CompareTag(playerTag))
                return true;
            current = current.parent;
        }

        return false;
    }

    void OnDeathzoneReached()
    {
        if (hasBeenTriggered)
            return;

        hasBeenTriggered = true;

        if (showDebugInfo)
            Debug.Log($"💀 DEATHZONE ERREICHT: {gameObject.name} - Neustart wird eingeleitet");

        if (deathSound != null)
        {
            AudioSource.PlayClipAtPoint(deathSound, transform.position);
        }

        if (useSlowMotion)
        {
            StartCoroutine(SlowMotionThenRestart());
        }
        else
        {
            RestartScene();
        }
    }

    IEnumerator SlowMotionThenRestart()
    {
        Time.timeScale = slowMotionScale;
        Time.fixedDeltaTime = 0.02f * Time.timeScale;

        if (showDebugInfo)
            Debug.Log($"⏱️ Deathzone Slow-Motion: {slowMotionScale}x für {slowMotionDuration}s");

        // Real-Time warten, damit die Verzögerung unabhängig von Time.timeScale
        // tatsächlich slowMotionDuration Sekunden dauert
        yield return new WaitForSecondsRealtime(slowMotionDuration);

        RestartScene();
    }

    void RestartScene()
    {
        // Time Scale immer zurücksetzen, BEVOR die neue Szene geladen wird -
        // sonst würde die neue Szene in der eingefrorenen Slow-Motion starten
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;

        Scene currentScene = SceneManager.GetActiveScene();

        if (showDebugInfo)
            Debug.Log($"🔄 DeathzoneTrigger: Lade Szene neu: '{currentScene.name}'");

        SceneManager.LoadScene(currentScene.name);
    }
}
