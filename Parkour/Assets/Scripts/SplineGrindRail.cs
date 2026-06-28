using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Splines;

/// <summary>
/// Kommt auf ein GameObject mit einem SplineContainer (Unity Splines-Paket,
/// com.unity.splines). Erzeugt automatisch eine Kette von Kapsel-Collidern
/// entlang der GESAMTEN Bahn (nicht mehr nur an den beiden Enden) - der Spieler
/// kann dadurch an jeder beliebigen Stelle einsteigen, nicht nur an Start/Ende.
///
/// Performance-Überlegung: Eine Trigger-Kette aus Collidern, die nur bei
/// tatsächlicher Kollision per OnTriggerEnter feuert, ist günstiger als eine
/// ständige Distanzprüfung in Update() - Unitys Physik-Engine übernimmt das
/// Culling (Broadphase) bereits effizient, eine eigene Pro-Frame-Distanzprüfung
/// wäre hier tatsächlich die teurere Variante.
///
/// Einstiegsrichtung: Beim Eintreten wird die BEWEGUNGSRICHTUNG des Spielers
/// (CharacterController.velocity) mit der Spline-Tangente an der nächstgelegenen
/// Stelle verglichen (Skalarprodukt) - läuft er eher Richtung Bahnende oder
/// Richtung Bahnanfang, das bestimmt die Grind-Richtung.
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

    [Header("Trigger-Kette entlang der Bahn")]
    [Tooltip("Tag des Spielers, der die Bahn auslösen darf")]
    public string playerTag = "Player";

    [Tooltip("Radius der Kapsel-Collider entlang der Bahn (= wie nah der Spieler an die Schiene muss)")]
    public float railRadius = 0.6f;

    [Tooltip("Anzahl der Segmente, in die die Bahn für die Trigger-Kette unterteilt wird. Höher = genauere Anschmiegung an Kurven, aber mehr Collider-Objekte.")]
    public int segmentCount = 20;

    [Tooltip("Trigger-Collider automatisch erzeugen (empfohlen)")]
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
            CreateSegmentTriggers();
        }
    }

    /// <summary>
    /// Erzeugt eine Kette von Kapsel-Collidern entlang der Bahn. Jedes Segment
    /// kennt sein eigenes t-Intervall [tStart, tEnd], damit beim Einstieg sofort
    /// klar ist, an welcher ungefähren Stelle der Bahn der Spieler steht.
    /// </summary>
    void CreateSegmentTriggers()
    {
        for (int i = 0; i < segmentCount; i++)
        {
            float tStart = i / (float)segmentCount;
            float tEnd = (i + 1) / (float)segmentCount;
            float tMid = (tStart + tEnd) * 0.5f;

            Vector3 posStart = (Vector3)splineContainer.EvaluatePosition(splineIndex, tStart);
            Vector3 posEnd = (Vector3)splineContainer.EvaluatePosition(splineIndex, tEnd);

            CreateSegmentCollider($"GrindSegment_{i}", posStart, posEnd, tStart, tEnd, tMid);
        }

        if (showDebugInfo)
            Debug.Log($"✅ SplineGrindRail: {segmentCount} Trigger-Segmente entlang der Bahn erzeugt");
    }

    void CreateSegmentCollider(string name, Vector3 posA, Vector3 posB, float tStart, float tEnd, float tMid)
    {
        GameObject segObj = new GameObject(name);
        segObj.transform.SetParent(transform);

        Vector3 midPoint = (posA + posB) * 0.5f;
        segObj.transform.position = midPoint;

        Vector3 direction = posB - posA;
        float segmentLength = direction.magnitude;

        // CapsuleCollider richtet seine Achse standardmäßig entlang der lokalen
        // Y-Achse aus - das Objekt rotieren, damit diese Achse mit der
        // Bahnrichtung dieses Segments übereinstimmt.
        if (direction.sqrMagnitude > 0.0001f)
        {
            segObj.transform.rotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
        }

        CapsuleCollider col = segObj.AddComponent<CapsuleCollider>();
        col.isTrigger = true;
        col.radius = railRadius;
        col.direction = 1; // Y-Achse (lokal)
        col.height = Mathf.Max(segmentLength, railRadius * 2f);

        SplineGrindSegmentTrigger segTrigger = segObj.AddComponent<SplineGrindSegmentTrigger>();
        segTrigger.Initialize(this, tStart, tEnd, tMid, playerTag);
    }

    /// <summary>
    /// Wird von einem Segment-Trigger aufgerufen, wenn der Spieler eintritt.
    /// Bestimmt die Einstiegsrichtung aus der Bewegungsrichtung des Spielers
    /// (Skalarprodukt mit der Spline-Tangente an der Eintrittsstelle) und
    /// leitet an den SplineGrindHandler weiter.
    /// </summary>
    public void OnPlayerEnteredSegment(GameObject player, float tStart, float tEnd, float tMid)
    {
        SplineGrindHandler handler = player.GetComponentInParent<SplineGrindHandler>();

        if (handler == null)
        {
            if (showDebugInfo)
                Debug.LogWarning($"⚠️ SplineGrindRail: Spieler '{player.name}' hat keinen SplineGrindHandler!");
            return;
        }

        // Genauesten Einstiegspunkt auf der Bahn innerhalb dieses Segments finden
        // (grob über ein paar Sub-Samples, da SplineUtility.GetNearestPoint pro
        // Aufruf etwas teurer ist und wir nur innerhalb eines kurzen Segments suchen)
        float nearestT = FindNearestTOnSegment(player.transform.position, tStart, tEnd);

        // Bewegungsrichtung des Spielers ermitteln
        CharacterController cc = player.GetComponentInParent<CharacterController>();
        Vector3 playerVelocity = cc != null ? cc.velocity : Vector3.zero;

        bool movingForward = DetermineDirectionFromVelocity(playerVelocity, nearestT);

        handler.StartGrind(splineContainer, splineIndex, movingForward, nearestT);

        if (showDebugInfo)
            Debug.Log($"🛤️ SplineGrindRail: Einstieg bei t={nearestT:F2} ({(movingForward ? "vorwärts" : "rückwärts")}), Spieler-Speed: {playerVelocity.magnitude:F1} m/s");
    }

    /// <summary>
    /// Tastet das Segment grob ab, um den nächstgelegenen Punkt auf der Spline
    /// zur Spieler-Position zu finden (genauer als die Segment-Mitte zu nehmen).
    /// </summary>
    float FindNearestTOnSegment(Vector3 playerPos, float tStart, float tEnd, int subSamples = 8)
    {
        float bestT = tStart;
        float bestDist = float.MaxValue;

        for (int i = 0; i <= subSamples; i++)
        {
            float t = Mathf.Lerp(tStart, tEnd, i / (float)subSamples);
            Vector3 pos = (Vector3)splineContainer.EvaluatePosition(splineIndex, t);
            float dist = (pos - playerPos).sqrMagnitude;

            if (dist < bestDist)
            {
                bestDist = dist;
                bestT = t;
            }
        }

        return bestT;
    }

    /// <summary>
    /// Vergleicht die Spieler-Bewegungsrichtung mit der Spline-Tangente an der
    /// Eintrittsstelle per Skalarprodukt. Positiv -> Spieler bewegt sich in
    /// Tangentenrichtung (Richtung t=1, "vorwärts"). Negativ -> Gegenrichtung.
    /// </summary>
    bool DetermineDirectionFromVelocity(Vector3 playerVelocity, float t)
    {
        if (playerVelocity.sqrMagnitude < 0.01f)
            return true; // Spieler steht praktisch still - Default: vorwärts

        Vector3 tangent = (Vector3)splineContainer.EvaluateTangent(splineIndex, t);
        float dot = Vector3.Dot(playerVelocity.normalized, tangent.normalized);

        return dot >= 0f;
    }

    void OnDrawGizmos()
    {
        if (!showGizmos)
            return;

        SplineContainer container = GetComponent<SplineContainer>();
        if (container == null || container.Splines.Count <= splineIndex)
            return;

        Gizmos.color = gizmoColor;

        int samples = 32;
        Vector3 prev = (Vector3)container.EvaluatePosition(splineIndex, 0f);
        for (int i = 1; i <= samples; i++)
        {
            float t = i / (float)samples;
            Vector3 point = (Vector3)container.EvaluatePosition(splineIndex, t);
            Gizmos.DrawLine(prev, point);
            prev = point;
        }

        // Trigger-Radius entlang der Bahn als halbtransparente Indikation zeigen
        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.25f);
        int radiusSamples = 12;
        for (int i = 0; i <= radiusSamples; i++)
        {
            float t = i / (float)radiusSamples;
            Vector3 point = (Vector3)container.EvaluatePosition(splineIndex, t);
            Gizmos.DrawWireSphere(point, railRadius);
        }

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere((Vector3)container.EvaluatePosition(splineIndex, 0f), railRadius * 1.3f);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere((Vector3)container.EvaluatePosition(splineIndex, 1f), railRadius * 1.3f);
    }
}

/// <summary>
/// Wird automatisch auf jedes Bahn-Segment gesetzt. Reicht OnTriggerEnter an
/// die SplineGrindRail weiter, zusammen mit dem t-Intervall dieses Segments.
/// </summary>
public class SplineGrindSegmentTrigger : MonoBehaviour
{
    private SplineGrindRail rail;
    private float tStart;
    private float tEnd;
    private float tMid;
    private string playerTag;

    public void Initialize(SplineGrindRail parentRail, float segmentTStart, float segmentTEnd, float segmentTMid, string tag)
    {
        rail = parentRail;
        tStart = segmentTStart;
        tEnd = segmentTEnd;
        tMid = segmentTMid;
        playerTag = tag;
    }

    void OnTriggerEnter(Collider other)
    {
        if (rail == null)
            return;

        if (IsPlayer(other.gameObject))
        {
            rail.OnPlayerEnteredSegment(other.gameObject, tStart, tEnd, tMid);
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
