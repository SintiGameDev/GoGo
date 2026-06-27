using System.Collections;
using UnityEngine;

/// <summary>
/// Wird auf das Goal GameObject gesetzt. Erkennt Spieler-Kollision und triggert Scene-Wechsel.
/// Arbeitet mit dem Tag "Goal" und ist kompatibel mit Grabbable-Scripts.
/// </summary>
[RequireComponent(typeof(Collider))]
public class GoalTrigger : MonoBehaviour
{
    [Header("Goal Settings")]
    [Tooltip("Tag des Spielers")]
    public string playerTag = "Player";

    [Tooltip("Sollte automatisch auf 'Goal' gesetzt sein")]
    public string goalTag = "Goal";

    [Header("Visual Effects")]
    [Tooltip("Partikeleffekt beim Erreichen (optional)")]
    public GameObject reachedEffect;

    [Tooltip("Sound beim Erreichen (optional)")]
    public AudioClip reachedSound;

    [Tooltip("Skalierungs-Animation beim Erreichen")]
    public bool playScaleAnimation = true;
    public float scaleAnimationDuration = 0.3f;
    public AnimationCurve scaleCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);

    [Header("Slow Motion")]
    [Tooltip("Slow-Motion beim Erreichen aktivieren")]
    public bool useSlowMotion = true;
    public float slowMotionScale = 0.3f;
    public float slowMotionDuration = 0.8f;

    [Header("Collision Detection")]
    [Tooltip("Separater Trigger-Collider für Goal-Erkennung (empfohlen bei Grabbable)")]
    public Collider goalTriggerCollider;

    [Tooltip("Automatisch einen separaten Trigger-Collider erstellen")]
    public bool autoCreateTriggerCollider = true;

    [Tooltip("Radius des automatisch erstellten Trigger-Colliders")]
    public float triggerRadius = 1.5f;

    [Header("Debug")]
    public bool showDebugInfo = true;
    public bool showGizmo = true;
    public Color gizmoColor = Color.yellow;

    private bool hasBeenTriggered = false;
    private SceneDirector sceneDirector;
    private Vector3 originalScale;
    private Renderer goalRenderer;

    void Awake()
    {
        // Stelle sicher, dass der Tag korrekt ist
        if (!gameObject.CompareTag(goalTag))
        {
            Debug.LogWarning($"⚠️ GoalTrigger ({gameObject.name}): Tag ist '{gameObject.tag}' statt '{goalTag}'. Setze Tag auf 'Goal'...");
            gameObject.tag = goalTag;
        }
    }

    void Start()
    {
        // Finde SceneDirector in der Scene
        sceneDirector = FindObjectOfType<SceneDirector>();

        if (sceneDirector == null)
        {
            Debug.LogError("❌ GoalTrigger: Kein SceneDirector in der Scene gefunden!");
        }

        // Setup
        originalScale = transform.localScale;
        goalRenderer = GetComponent<Renderer>();

        // Setup Trigger-Collider
        SetupTriggerCollider();

        // Prüfe Setup
        ValidateSetup();
    }

    void SetupTriggerCollider()
    {
        // Wenn kein Trigger-Collider zugewiesen und Auto-Create aktiviert ist
        if (goalTriggerCollider == null && autoCreateTriggerCollider)
        {
            // Suche nach vorhandenen Trigger-Collidern
            Collider[] colliders = GetComponents<Collider>();
            foreach (Collider col in colliders)
            {
                if (col.isTrigger)
                {
                    goalTriggerCollider = col;
                    if (showDebugInfo)
                        Debug.Log($"✅ Vorhandener Trigger-Collider gefunden: {col.GetType().Name}");
                    return;
                }
            }

            // Erstelle neuen Trigger-Collider als Child
            GameObject triggerObj = new GameObject("GoalTrigger");
            triggerObj.transform.SetParent(transform);
            triggerObj.transform.localPosition = Vector3.zero;
            triggerObj.transform.localRotation = Quaternion.identity;
            triggerObj.layer = gameObject.layer;

            SphereCollider sphereTrigger = triggerObj.AddComponent<SphereCollider>();
            sphereTrigger.isTrigger = true;
            sphereTrigger.radius = triggerRadius;

            goalTriggerCollider = sphereTrigger;

            if (showDebugInfo)
                Debug.Log($"✅ Neuer Trigger-Collider erstellt: SphereCollider (Radius: {triggerRadius})");
        }
        else if (goalTriggerCollider != null)
        {
            // Stelle sicher, dass der zugewiesene Collider ein Trigger ist
            if (!goalTriggerCollider.isTrigger)
            {
                Debug.LogWarning($"⚠️ Zugewiesener Goal Trigger Collider ist kein Trigger! Setze isTrigger = true");
                goalTriggerCollider.isTrigger = true;
            }

            if (showDebugInfo)
                Debug.Log($"✅ Goal Trigger Collider zugewiesen: {goalTriggerCollider.GetType().Name}");
        }
    }

    void ValidateSetup()
    {
        if (showDebugInfo)
        {
            Debug.Log($"✅ GoalTrigger Setup: {gameObject.name}");
            Debug.Log($"   Tag: {gameObject.tag} (sollte '{goalTag}' sein)");
            Debug.Log($"   Layer: {LayerMask.LayerToName(gameObject.layer)}");
            Debug.Log($"   Position: {transform.position}");

            // Zeige alle Collider
            Collider[] allColliders = GetComponentsInChildren<Collider>(true);
            Debug.Log($"   Collider gefunden: {allColliders.Length}");
            foreach (Collider col in allColliders)
            {
                Debug.Log($"      - {col.gameObject.name}: {col.GetType().Name} (Trigger: {col.isTrigger})");
            }

            // Finde Player und prüfe Layer Collision
            GameObject player = GameObject.FindGameObjectWithTag(playerTag);
            if (player != null)
            {
                bool canCollide = !Physics.GetIgnoreLayerCollision(gameObject.layer, player.layer);
                Debug.Log($"   Player gefunden: {player.name}");
                Debug.Log($"   Layer Collision: {(canCollide ? "✅ Aktiviert" : "❌ Deaktiviert")}");
            }
            else
            {
                Debug.LogWarning($"   ⚠️ Kein Player mit Tag '{playerTag}' gefunden!");
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (hasBeenTriggered)
            return;

        if (showDebugInfo)
        {
            Debug.Log($"🔔 GoalTrigger: OnTriggerEnter - {other.gameObject.name} (Tag: '{other.tag}')");
        }

        // Prüfe auch Parent Objects (für Child-Collider wie beim FPSController)
        if (IsPlayer(other.gameObject))
        {
            OnGoalReached();
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (hasBeenTriggered)
            return;

        if (showDebugInfo)
        {
            Debug.Log($"💥 GoalTrigger: OnCollisionEnter - {collision.gameObject.name}");
            Debug.Log($"   Hinweis: Collision Events sollten vermieden werden. Nutze Trigger!");
        }

        if (IsPlayer(collision.gameObject))
        {
            OnGoalReached();
        }
    }

    bool IsPlayer(GameObject obj)
    {
        // Prüfe direkt
        if (obj.CompareTag(playerTag))
        {
            if (showDebugInfo)
                Debug.Log($"   ✅ Player erkannt: {obj.name}");
            return true;
        }

        // Prüfe Parent-Hierarchy (wichtig für Child-Collider)
        Transform current = obj.transform;
        while (current != null)
        {
            if (current.CompareTag(playerTag))
            {
                if (showDebugInfo)
                    Debug.Log($"   ✅ Player erkannt via Parent: {current.name}");
                return true;
            }
            current = current.parent;
        }

        if (showDebugInfo)
            Debug.Log($"   ❌ Kein Player Tag gefunden");

        return false;
    }

    void OnGoalReached()
    {
        if (hasBeenTriggered)
            return;

        hasBeenTriggered = true;

        Debug.Log($"🎉 GOAL ERREICHT: {gameObject.name}");

        // Effekte abspielen
        PlayEffects();

        // Slow Motion oder direkter Scene-Wechsel
        if (useSlowMotion)
        {
            StartCoroutine(SlowMotionSequence());
        }
        else
        {
            LoadNextScene();
        }
    }

    void PlayEffects()
    {
        // Partikeleffekt
        if (reachedEffect != null)
        {
            Instantiate(reachedEffect, transform.position, Quaternion.identity);
        }

        // Sound
        if (reachedSound != null)
        {
            AudioSource.PlayClipAtPoint(reachedSound, transform.position);
        }

        // Skalierungs-Animation
        if (playScaleAnimation)
        {
            StartCoroutine(ScaleAnimation());
        }
    }

    IEnumerator ScaleAnimation()
    {
        float elapsed = 0f;

        while (elapsed < scaleAnimationDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = elapsed / scaleAnimationDuration;
            float curveValue = scaleCurve.Evaluate(progress);

            transform.localScale = originalScale * (1f + curveValue * 0.5f);

            yield return null;
        }
    }

    IEnumerator SlowMotionSequence()
    {
        // Slow Motion aktivieren
        Time.timeScale = slowMotionScale;
        Time.fixedDeltaTime = 0.02f * Time.timeScale;

        Debug.Log($"⏱️ Slow Motion aktiviert: {slowMotionScale}x für {slowMotionDuration}s");

        // Warte in Real-Time
        yield return new WaitForSecondsRealtime(slowMotionDuration);

        // Time Scale zurücksetzen
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;

        LoadNextScene();
    }

    void LoadNextScene()
    {
        // Scene Director benachrichtigen
        if (sceneDirector != null)
        {
            sceneDirector.LoadNextScene();
        }
        else
        {
            Debug.LogError("❌ Kein SceneDirector gefunden! Kann Scene nicht laden.");
        }

        // Optional: Goal ausblenden statt zerstören (falls Grabbable-Script Probleme macht)
        if (showDebugInfo)
            Debug.Log($"👻 Goal wird unsichtbar: {gameObject.name}");

        // Deaktiviere alle Renderer
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            r.enabled = false;
        }

        // Deaktiviere alle Collider
        Collider[] colliders = GetComponentsInChildren<Collider>();
        foreach (Collider c in colliders)
        {
            c.enabled = false;
        }

        // Optional: Objekt komplett deaktivieren
        // gameObject.SetActive(false);
    }

    // Manueller Test (Drücke 'G' im Play Mode)
    void Update()
    {
        if (showDebugInfo && Input.GetKeyDown(KeyCode.G))
        {
            Debug.Log("🧪 Manueller Goal-Test (G gedrückt)");
            OnGoalReached();
        }

        // Zeige Tag-Warnung wenn falsch
        if (!gameObject.CompareTag(goalTag) && Time.frameCount % 300 == 0)
        {
            Debug.LogWarning($"⚠️ {gameObject.name}: Tag ist '{gameObject.tag}' statt '{goalTag}'!");
        }
    }

    void OnDrawGizmos()
    {
        if (!showGizmo)
            return;

        // Zeichne Trigger-Collider
        if (goalTriggerCollider != null)
        {
            Gizmos.color = gizmoColor;

            if (goalTriggerCollider is SphereCollider sphere)
            {
                Vector3 center = goalTriggerCollider.transform.TransformPoint(sphere.center);
                float radius = sphere.radius * goalTriggerCollider.transform.lossyScale.x;
                Gizmos.DrawWireSphere(center, radius);
            }
            else if (goalTriggerCollider is BoxCollider box)
            {
                Gizmos.matrix = goalTriggerCollider.transform.localToWorldMatrix;
                Gizmos.DrawWireCube(box.center, box.size);
                Gizmos.matrix = Matrix4x4.identity;
            }
            else if (goalTriggerCollider is CapsuleCollider capsule)
            {
                Vector3 center = goalTriggerCollider.transform.TransformPoint(capsule.center);
                float radius = capsule.radius * goalTriggerCollider.transform.lossyScale.x;
                Gizmos.DrawWireSphere(center, radius);
            }
        }
        else if (autoCreateTriggerCollider)
        {
            // Zeige wo der Trigger erstellt werden würde
            Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.5f);
            Gizmos.DrawWireSphere(transform.position, triggerRadius);
        }

        // Label
#if UNITY_EDITOR
        string label = $"GOAL\nTag: {gameObject.tag}";
        UnityEditor.Handles.Label(transform.position + Vector3.up * 2f, label, new GUIStyle()
        {
            normal = new GUIStyleState() { textColor = gizmoColor },
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        });
#endif
    }

    void OnDrawGizmosSelected()
    {
        // Zeige Detection-Range
        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.3f);

        if (goalTriggerCollider != null && goalTriggerCollider is SphereCollider sphere)
        {
            Vector3 center = goalTriggerCollider.transform.TransformPoint(sphere.center);
            float radius = sphere.radius * goalTriggerCollider.transform.lossyScale.x;
            Gizmos.DrawSphere(center, radius);
        }
        else
        {
            Gizmos.DrawSphere(transform.position, autoCreateTriggerCollider ? triggerRadius : 1f);
        }
    }
}