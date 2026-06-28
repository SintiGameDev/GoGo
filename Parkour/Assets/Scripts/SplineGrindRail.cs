using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Splines;

/// <summary>
/// Kommt auf ein GameObject mit einem SplineContainer (Unity Splines-Paket,
/// com.unity.splines). Erzeugt automatisch zwei Trigger-Collider an Anfang und
/// Ende des Splines. Berührt der Spieler einen davon, wird der SplineGrindHandler
/// am Spieler benachrichtigt, in welche Richtung gegrinded werden soll:
///   - Trigger am Anfang berührt  -> grind vorwärts (t: 0 -> 1)
///   - Trigger am Ende berührt    -> grind rückwärts (t: 1 -> 0)
///
/// Voraussetzung: Das Unity-Splines-Paket muss über den Package Manager
/// installiert sein (Window > Package Manager > "Splines").
/// </summary>
[RequireComponent(typeof(SplineContainer))]
public class SplineGrindRail : MonoBehaviour
{
    [Header("Spline")]
    [Tooltip("Index des Splines im Container (meist 0, falls nur ein Spline vorhanden)")]
    public int splineIndex = 0;

    [Header("Trigger-Enden")]
    [Tooltip("Tag des Spielers, der die Bahn auslösen darf")]
    public string playerTag = "Player";

    [Tooltip("Radius der automatisch erzeugten Trigger-Kugeln an Anfang/Ende")]
    public float triggerRadius = 1.0f;

    [Tooltip("Trigger-Collider automatisch erzeugen (empfohlen). Wenn aus, müssen sie manuell als Child mit SplineGrindEndTrigger zugewiesen werden.")]
    public bool autoCreateTriggers = true;

    [Header("Debug")]
    public bool showDebugInfo = true;
    public bool showGizmos = true;
    public Color gizmoColor = new Color(0.2f, 0.8f, 1f);

    private SplineContainer splineContainer;

    void Awake()
    {
        splineContainer = GetComponent<SplineContainer>();

        if (autoCreateTriggers)
        {
            CreateEndTriggers();
        }
    }

    void CreateEndTriggers()
    {
        Vector3 startPos = GetWorldPosition(0f);
        Vector3 endPos = GetWorldPosition(1f);

        CreateTrigger("GrindTrigger_Start", startPos, true);
        CreateTrigger("GrindTrigger_End", endPos, false);

        if (showDebugInfo)
            Debug.Log($"✅ SplineGrindRail: Trigger erzeugt an Start {startPos} und Ende {endPos}");
    }

    void CreateTrigger(string name, Vector3 worldPos, bool isStart)
    {
        GameObject triggerObj = new GameObject(name);
        triggerObj.transform.SetParent(transform);
        triggerObj.transform.position = worldPos;

        SphereCollider col = triggerObj.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = triggerRadius;

        SplineGrindEndTrigger endTrigger = triggerObj.AddComponent<SplineGrindEndTrigger>();
        endTrigger.Initialize(this, isStart, playerTag);
    }

    /// <summary>
    /// Wird von den End-Triggern aufgerufen, wenn der Spieler eintritt.
    /// Leitet an den SplineGrindHandler des Spielers weiter.
    /// </summary>
    public void OnPlayerEnteredEnd(GameObject player, bool fromStart)
    {
        SplineGrindHandler handler = player.GetComponentInParent<SplineGrindHandler>();

        if (handler == null)
        {
            if (showDebugInfo)
                Debug.LogWarning($"⚠️ SplineGrindRail: Spieler '{player.name}' hat keinen SplineGrindHandler!");
            return;
        }

        handler.StartGrind(splineContainer, splineIndex, fromStart);

        if (showDebugInfo)
            Debug.Log($"🛤️ SplineGrindRail: Grind gestartet ({(fromStart ? "vorwärts" : "rückwärts")})");
    }

    /// <summary>Weltposition auf dem Spline bei Interpolation t (0..1).</summary>
    public Vector3 GetWorldPosition(float t)
    {
        // EvaluatePosition des Containers liefert bereits Weltkoordinaten
        return (Vector3)splineContainer.EvaluatePosition(splineIndex, t);
    }

    void OnDrawGizmos()
    {
        if (!showGizmos)
            return;

        SplineContainer container = GetComponent<SplineContainer>();
        if (container == null || container.Splines.Count <= splineIndex)
            return;

        Gizmos.color = gizmoColor;

        // Spline grob als Linien-Sampling zeichnen
        int samples = 32;
        Vector3 prev = (Vector3)container.EvaluatePosition(splineIndex, 0f);
        for (int i = 1; i <= samples; i++)
        {
            float t = i / (float)samples;
            Vector3 point = (Vector3)container.EvaluatePosition(splineIndex, t);
            Gizmos.DrawLine(prev, point);
            prev = point;
        }

        // Enden markieren
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere((Vector3)container.EvaluatePosition(splineIndex, 0f), triggerRadius);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere((Vector3)container.EvaluatePosition(splineIndex, 1f), triggerRadius);
    }
}

/// <summary>
/// Wird automatisch auf die End-Trigger-Objekte gesetzt. Reicht OnTriggerEnter
/// an die SplineGrindRail weiter, zusammen mit der Info, ob es das Start- oder
/// Endende ist (= Grind-Richtung).
/// </summary>
public class SplineGrindEndTrigger : MonoBehaviour
{
    private SplineGrindRail rail;
    private bool isStart;
    private string playerTag;

    public void Initialize(SplineGrindRail parentRail, bool isStartEnd, string tag)
    {
        rail = parentRail;
        isStart = isStartEnd;
        playerTag = tag;
    }

    void OnTriggerEnter(Collider other)
    {
        if (rail == null)
            return;

        // Spieler direkt oder über Parent-Hierarchie erkennen (für Child-Collider)
        if (IsPlayer(other.gameObject))
        {
            rail.OnPlayerEnteredEnd(other.gameObject, isStart);
        }
    }

    bool IsPlayer(GameObject obj)
    {
        Transform current = obj.transform;
        while (current != null)
        {
            if (current.CompareTag(playerTag))
                return true;
            current = current.parent;
        }
        return false;
    }
}
