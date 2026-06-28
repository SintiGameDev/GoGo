using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyOperator : MonoBehaviour
{
    [Header("Grapple Settings")]
    public float grappleSpeed = 25f;
    public float killRadius = 2f;
    public string grabbableTag = "Grabbable";

    [Header("Slow Motion Settings")]
    public float slowMotionTimeScale = 0.2f;
    public float slowMotionDuration = 0.15f;
    public float autoJumpForce = 12f;
    public float heightDeltaToEndSlowMo = 3f;

    [Header("Raycast Settings")]
    public float maxGrappleDistance = 50f;
    public Camera playerCamera;
    public LayerMask grabbableLayerMask = ~0; // Alle Layer standardmäßig

    [Header("Highlight Settings")]
    public Color highlightColor = Color.yellow;
    public float highlightIntensity = 1.5f;
    public bool useEmission = true;

    [Header("Dynamic Collider Settings")]
    public float minColliderRadius = 0.5f;
    public float maxColliderRadius = 2.5f;
    public float minScaleDistance = 5f;
    public float maxScaleDistance = 30f;
    public bool enableDynamicColliders = true;

    // NEUE: Collision Prevention
    [Header("Collision Prevention")]
    public bool useTriggersInsteadOfColliders = true; // Verhindert CharacterController Collision
    public bool disablePlayerCollisionWithGrabbables = true; // Physics Layer basiert
    public float colliderUpdateSmoothness = 5f; // Wie smooth Collider-Changes sind
    public float safetyMargin = 0.2f; // Extra Abstand für Safety

    [Header("Hit Tracer (Impact Point)")]
    [Tooltip("Zusätzlicher Puffer vor der echten Mesh-Oberfläche, an dem der Enemy zerstört wird")]
    public float impactSurfaceBuffer = 0.4f;
    [Tooltip("Fallback-Distanz, falls kein Renderer/Bounds gefunden werden kann")]
    public float fallbackImpactDistance = 2f;

    [Header("Effects")]
    public GameObject destroyEffect;
    public AudioClip grappleSound;
    public AudioClip destroySound;

    [Header("Debug")]
    [Tooltip("Zeigt das OnGUI-Debug-Overlay (Grappling-Distanz, Slow-Motion-Status, Trigger-Info) - bei Bedarf ausschalten, um die Anzeige zu verstecken, ohne den Code zu entfernen")]
    public bool showDebugInfo = false;

    private SC_FPSController fpsController;
    private CharacterController characterController;
    private bool isGrappling = false;
    private Vector3 grappleTarget;
    private GameObject targetEnemy;

    private bool isInSlowMotion = false;
    private float slowMotionTimer = 0f;
    private float killHeight = 0f;
    private bool hasAutoJumped = false;

    private GameObject currentHighlightedObject = null;
    private Renderer highlightedRenderer = null;
    private Material[] originalMaterials = null;
    private Material[] highlightMaterials = null;

    // Dynamic Collider State - ÜBERARBEITET
    private Dictionary<GameObject, SphereCollider> enemyColliders = new Dictionary<GameObject, SphereCollider>();
    private Dictionary<GameObject, float> originalColliderRadii = new Dictionary<GameObject, float>();
    private Dictionary<GameObject, float> targetColliderRadii = new Dictionary<GameObject, float>(); // NEUE: Smooth Updates
    private Dictionary<GameObject, bool> wasColliderOriginallyTrigger = new Dictionary<GameObject, bool>(); // NEUE: Original State

    void Start()
    {
        fpsController = GetComponent<SC_FPSController>();
        characterController = GetComponent<CharacterController>();

        if (fpsController == null)
        {
            Debug.LogError("EnemyOperator benötigt SC_FPSController Component!");
        }

        if (characterController == null)
        {
            Debug.LogError("EnemyOperator benötigt CharacterController Component!");
        }

        if (playerCamera == null)
        {
            Debug.LogError("EnemyOperator benötigt eine Player Camera Referenz!");
        }

        // WICHTIG: Grabbable Layer Setup prüfen
        CheckGrabbableLayerSetup();

        if (enableDynamicColliders)
        {
            RegisterAllGrabbableObjects();
        }
    }

    // NEUE: Prüft ob Layer-Setup korrekt ist
    void CheckGrabbableLayerSetup()
    {
        GameObject[] grabbables = GameObject.FindGameObjectsWithTag(grabbableTag);

        if (grabbables.Length == 0)
        {
            Debug.LogWarning("Keine Grabbable Objects gefunden!");
            return;
        }

        // Prüfe erstes Grabbable Object
        int grabbableLayer = grabbables[0].layer;

        if (grabbableLayer == LayerMask.NameToLayer("Default"))
        {
            Debug.LogWarning("⚠️ WARNUNG: Grabbable Objects sind auf 'Default' Layer! Empfehlung: Erstelle einen 'Grabbable' Layer.");
        }

        // Prüfe ob Player mit Grabbables kollidiert
        if (disablePlayerCollisionWithGrabbables)
        {
            int playerLayer = gameObject.layer;

            if (!Physics.GetIgnoreLayerCollision(playerLayer, grabbableLayer))
            {
                Debug.Log($"ℹ️ Layer Collision ist aktiv zwischen Player ({LayerMask.LayerToName(playerLayer)}) und Grabbables ({LayerMask.LayerToName(grabbableLayer)})");
                Debug.Log("💡 TIP: Deaktiviere die Collision in Edit → Project Settings → Physics → Layer Collision Matrix");
            }
        }
    }

    void Update()
    {
        // Dynamic Collider Update - NUR wenn nicht am Grappling
        if (enableDynamicColliders && !isGrappling)
        {
            UpdateDynamicColliders();
        }

        // Highlight Update
        if (!isGrappling)
        {
            UpdateHighlight();
        }

        // Grapple Input
        if (Input.GetMouseButtonDown(1) && !isGrappling)
        {
            TryGrappleToEnemy();
        }

        // Grappling Update
        if (isGrappling)
        {
            UpdateGrapple();
        }

        // Slow Motion Update
        if (isInSlowMotion)
        {
            UpdateSlowMotion();
        }
    }

    void TryGrappleToEnemy()
    {
        if (playerCamera == null) return;

        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        RaycastHit hit;

        // WICHTIG: Nutze grabbableLayerMask für den Raycast
        if (Physics.Raycast(ray, out hit, maxGrappleDistance, grabbableLayerMask))
        {
            if (hit.collider.CompareTag(grabbableTag))
            {
                StartGrapple(hit.collider.gameObject, hit.point);
            }
            else
            {
                Debug.Log("Kein Grabbable Object getroffen!");
            }
        }
    }

    void UpdateHighlight()
    {
        if (playerCamera == null) return;

        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        RaycastHit hit;

        // Nutze grabbableLayerMask
        if (Physics.Raycast(ray, out hit, maxGrappleDistance, grabbableLayerMask))
        {
            if (hit.collider.CompareTag(grabbableTag))
            {
                if (currentHighlightedObject != hit.collider.gameObject)
                {
                    RemoveHighlight();
                    ApplyHighlight(hit.collider.gameObject);
                }
            }
            else
            {
                RemoveHighlight();
            }
        }
        else
        {
            RemoveHighlight();
        }
    }

    void ApplyHighlight(GameObject target)
    {
        currentHighlightedObject = target;
        highlightedRenderer = target.GetComponent<Renderer>();

        if (highlightedRenderer != null)
        {
            originalMaterials = highlightedRenderer.materials;
            highlightMaterials = new Material[originalMaterials.Length];

            for (int i = 0; i < originalMaterials.Length; i++)
            {
                highlightMaterials[i] = new Material(originalMaterials[i]);
                highlightMaterials[i].color = highlightColor;

                if (useEmission)
                {
                    highlightMaterials[i].EnableKeyword("_EMISSION");
                    highlightMaterials[i].SetColor("_EmissionColor", highlightColor * highlightIntensity);
                }
            }

            highlightedRenderer.materials = highlightMaterials;
        }
    }

    void RemoveHighlight()
    {
        if (currentHighlightedObject != null && highlightedRenderer != null && originalMaterials != null)
        {
            highlightedRenderer.materials = originalMaterials;

            if (highlightMaterials != null)
            {
                foreach (Material mat in highlightMaterials)
                {
                    if (mat != null)
                    {
                        Destroy(mat);
                    }
                }
            }
        }

        currentHighlightedObject = null;
        highlightedRenderer = null;
        originalMaterials = null;
        highlightMaterials = null;
    }

    void RegisterAllGrabbableObjects()
    {
        GameObject[] grabbables = GameObject.FindGameObjectsWithTag(grabbableTag);

        foreach (GameObject obj in grabbables)
        {
            SphereCollider sphereCol = obj.GetComponent<SphereCollider>();

            if (sphereCol != null)
            {
                enemyColliders[obj] = sphereCol;
                originalColliderRadii[obj] = sphereCol.radius;
                targetColliderRadii[obj] = sphereCol.radius; // NEUE: Initial target

                // NEUE: Original Trigger State speichern
                wasColliderOriginallyTrigger[obj] = sphereCol.isTrigger;

                // WICHTIG: Als Trigger markieren wenn aktiviert
                if (useTriggersInsteadOfColliders)
                {
                    sphereCol.isTrigger = true;
                    Debug.Log($"✅ {obj.name}: Collider auf Trigger gesetzt (verhindert CharacterController Collision)");
                }

                Debug.Log($"Registered: {obj.name} | Original Radius: {sphereCol.radius} | Layer: {LayerMask.LayerToName(obj.layer)}");
            }
            else
            {
                Debug.LogWarning($"⚠️ {obj.name} hat keinen SphereCollider!");
            }
        }

        Debug.Log($"✅ {enemyColliders.Count} Grabbable Objects registriert");
    }

    void UpdateDynamicColliders()
    {
        List<GameObject> toRemove = new List<GameObject>();

        foreach (var kvp in enemyColliders)
        {
            GameObject enemy = kvp.Key;
            SphereCollider sphereCol = kvp.Value;

            if (enemy == null || sphereCol == null)
            {
                toRemove.Add(enemy);
                continue;
            }

            float distance = Vector3.Distance(transform.position, enemy.transform.position);

            // NEUE: Berechne Target Radius mit Safety Margin
            float newTargetRadius = CalculateColliderRadius(distance) + safetyMargin;
            targetColliderRadii[enemy] = newTargetRadius;

            // NEUE: SMOOTH Radius Update statt instant
            float currentRadius = sphereCol.radius;
            float smoothedRadius = Mathf.Lerp(currentRadius, newTargetRadius, Time.deltaTime * colliderUpdateSmoothness);

            sphereCol.radius = smoothedRadius;
        }

        foreach (GameObject obj in toRemove)
        {
            enemyColliders.Remove(obj);
            originalColliderRadii.Remove(obj);
            targetColliderRadii.Remove(obj);
            wasColliderOriginallyTrigger.Remove(obj);
        }
    }

    float CalculateColliderRadius(float distance)
    {
        float clampedDistance = Mathf.Clamp(distance, minScaleDistance, maxScaleDistance);
        float normalizedDistance = (clampedDistance - minScaleDistance) / (maxScaleDistance - minScaleDistance);
        float radius = Mathf.Lerp(minColliderRadius, maxColliderRadius, normalizedDistance);
        return radius;
    }

    public void RegisterGrabbableObject(GameObject obj)
    {
        if (obj.CompareTag(grabbableTag))
        {
            SphereCollider sphereCol = obj.GetComponent<SphereCollider>();

            if (sphereCol != null && !enemyColliders.ContainsKey(obj))
            {
                enemyColliders[obj] = sphereCol;
                originalColliderRadii[obj] = sphereCol.radius;
                targetColliderRadii[obj] = sphereCol.radius;
                wasColliderOriginallyTrigger[obj] = sphereCol.isTrigger;

                if (useTriggersInsteadOfColliders)
                {
                    sphereCol.isTrigger = true;
                }

                Debug.Log($"Dynamically registered: {obj.name}");
            }
        }
    }

    void StartGrapple(GameObject enemy, Vector3 hitPoint)
    {
        RemoveHighlight();

        // NEUE: Collider komplett deaktivieren während Grapple (verhindert ANY Collision)
        if (enableDynamicColliders && enemyColliders.ContainsKey(enemy))
        {
            SphereCollider sphereCol = enemyColliders[enemy];
            if (sphereCol != null)
            {
                // Option 1: Radius auf 0 setzen
                sphereCol.radius = 0.1f;

                // Option 2: Collider komplett deaktivieren (noch sicherer)
                //sphereCol.enabled = false;

                Debug.Log($"✅ Collider deaktiviert während Grapple");
            }
        }

        isGrappling = true;
        targetEnemy = enemy;
        grappleTarget = enemy.transform.position;

        if (fpsController != null)
        {
            fpsController.SetGrapplingState(true);
        }

        if (grappleSound != null)
        {
            AudioSource.PlayClipAtPoint(grappleSound, transform.position);
        }

        Debug.Log($"Grappling zu: {enemy.name}");
    }

    void UpdateGrapple()
    {
        if (targetEnemy == null)
        {
            EndGrapple(false);
            return;
        }

        grappleTarget = targetEnemy.transform.position;
        Vector3 toTarget = grappleTarget - transform.position;
        float distance = toTarget.magnitude;
        Vector3 direction = distance > 0.0001f ? toTarget / distance : Vector3.zero;

        // Hit Tracer: nutzt EXAKT dieselbe Position + Richtung wie die Bewegung.
        // Vorher: Ray ging Richtung bounds.center, distance/direction aber Richtung
        // transform.position (Pivot) -> zwei verschiedene Punkte, dadurch inkonsistent
        // (Instant-Kill bei großem Buffer, "Kriechen" + Clipping bei kleinem Buffer).
        float impactDistance = CalculateImpactDistance(targetEnemy, transform.position, direction, distance);

        if (characterController != null)
        {
            float step = grappleSpeed * Time.deltaTime;

            // Wie weit dürfen wir uns noch nähern, bevor wir die Mesh-Oberfläche erreichen?
            float maxAllowedStep = Mathf.Max(0f, distance - impactDistance);

            // Bewegung clampen -> Spieler bleibt immer vor der echten Mesh-Oberfläche
            float clampedStep = Mathf.Min(step, maxAllowedStep);

            if (clampedStep > 0f)
            {
                characterController.Move(direction * clampedStep);
            }
        }

        if (distance <= impactDistance)
        {
            DestroyEnemyWithSlowMotion();
        }
    }

    // NEUE: Hit Tracer - berechnet den Abstand zur tatsächlichen Mesh-Oberfläche
    // entlang DERSELBEN Linie, auf der sich der Spieler bewegt (fromPosition -> direction).
    // So sind Distance-Check und Bewegungs-Clamp immer konsistent zueinander.
    float CalculateImpactDistance(GameObject enemy, Vector3 fromPosition, Vector3 direction, float distanceToTarget)
    {
        if (direction == Vector3.zero)
        {
            return Mathf.Max(killRadius, fallbackImpactDistance);
        }

        Renderer rend = enemy.GetComponent<Renderer>();
        if (rend == null)
        {
            rend = enemy.GetComponentInChildren<Renderer>();
        }

        if (rend != null)
        {
            Bounds bounds = rend.bounds;
            Ray tracerRay = new Ray(fromPosition, direction);

            if (bounds.IntersectRay(tracerRay, out float hitDistance))
            {
                // Clamp: impactDistance darf NIE größer sein als die tatsächlich
                // verbleibende Distanz zum Pivot. Ein riesiger Buffer führt dadurch
                // kontrolliert zu "sofort zerstören", statt zu inkonsistentem Verhalten.
                return Mathf.Clamp(hitDistance + impactSurfaceBuffer, 0f, distanceToTarget);
            }
        }

        // Fallback, falls kein Renderer gefunden wurde oder der Ray die Bounds verfehlt
        return Mathf.Min(Mathf.Max(killRadius, fallbackImpactDistance), distanceToTarget);
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        // ABSICHTLICH DEAKTIVIERT: Der Hit-Tracer in UpdateGrapple() entscheidet bereits
        // jeden Frame geometrisch exakt (Renderer.bounds), wann der Enemy zerstört wird.
        // Diese Methode reagierte vorher zusätzlich auf physische Collider-Treffer -
        // das ist riskant, weil die dynamische Collider-Größenänderung (enemyColliders /
        // UpdateDynamicColliders) Radius-Werte setzt, die von der Physics-Engine nicht
        // immer im exakt selben Frame übernommen werden. Dadurch könnte dieser Callback
        // mit einem veralteten (zu großen) Radius feuern und die Zeitlupe zu früh auslösen.
    }

    // NEUE: OnTriggerEnter für Trigger-basierte Collider
    void OnTriggerEnter(Collider other)
    {
        // ABSICHTLICH DEAKTIVIERT: gleicher Grund wie OnControllerColliderHit oben.
        // Der dynamisch resizte SphereCollider (enemyColliders) ist NICHT mehr die
        // Quelle für den Destroy-Zeitpunkt - nur noch der Hit-Tracer in UpdateGrapple().
        // Damit kann die Collider-Größenänderung keinen Einfluss mehr auf das Timing haben.
    }

    void DestroyEnemyWithSlowMotion()
    {
        if (targetEnemy != null)
        {
            Vector3 enemyPosition = targetEnemy.transform.position;

            if (destroyEffect != null)
            {
                Instantiate(destroyEffect, enemyPosition, Quaternion.identity);
            }

            if (destroySound != null)
            {
                AudioSource.PlayClipAtPoint(destroySound, enemyPosition);
            }

            Debug.Log($"Grabbable Object zerstört: {targetEnemy.name}");

            Destroy(targetEnemy);
            targetEnemy = null;
        }

        // NEUE: Weiß-Flash + FOV-Bounce im exakten Kill-Moment - kaschiert verbleibendes
        // Mesh-Clipping, da der Bildschirm im Aufprall-Frame kurz aufhellt.
        if (fpsController != null)
        {
            fpsController.TriggerImpactFeedback();
        }

        isGrappling = false;
        StartSlowMotion();
    }

    void StartSlowMotion()
    {
        isInSlowMotion = true;
        hasAutoJumped = false;
        slowMotionTimer = 0f;
        killHeight = transform.position.y;

        Time.timeScale = slowMotionTimeScale;
        Time.fixedDeltaTime = 0.02f * Time.timeScale;

        if (fpsController != null)
        {
            fpsController.SetGrapplingState(false);
        }

        Debug.Log("Slow Motion gestartet!");
    }

    void UpdateSlowMotion()
    {
        slowMotionTimer += Time.unscaledDeltaTime;

        if (!hasAutoJumped && slowMotionTimer >= slowMotionDuration)
        {
            PerformAutoJump();
            hasAutoJumped = true;
        }

        if (hasAutoJumped)
        {
            float currentHeight = transform.position.y;
            float heightGained = currentHeight - killHeight;

            if (heightGained >= heightDeltaToEndSlowMo)
            {
                EndSlowMotion();
            }
        }
    }

    void PerformAutoJump()
    {
        if (fpsController != null)
        {
            fpsController.ForceJump(autoJumpForce);
        }

        Debug.Log("Auto-Jump ausgeführt!");
    }

    void EndSlowMotion()
    {
        isInSlowMotion = false;
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;
        Debug.Log("Slow Motion beendet!");
    }

    void EndGrapple(bool successful)
    {
        isGrappling = false;
        targetEnemy = null;

        if (fpsController != null)
        {
            fpsController.SetGrapplingState(false);
        }

        Debug.Log(successful ? "Grapple erfolgreich!" : "Grapple abgebrochen");
    }

    void OnDestroy()
    {
        RemoveHighlight();

        // NEUE: Originale States wiederherstellen
        foreach (var kvp in enemyColliders)
        {
            GameObject enemy = kvp.Key;
            SphereCollider sphereCol = kvp.Value;

            if (enemy != null && sphereCol != null)
            {
                // Radius zurücksetzen
                if (originalColliderRadii.ContainsKey(enemy))
                {
                    sphereCol.radius = originalColliderRadii[enemy];
                }

                // Trigger State zurücksetzen
                if (wasColliderOriginallyTrigger.ContainsKey(enemy))
                {
                    sphereCol.isTrigger = wasColliderOriginallyTrigger[enemy];
                }

                // Collider wieder aktivieren
                sphereCol.enabled = true;
            }
        }
    }

    void OnDrawGizmos()
    {
        if (isGrappling && targetEnemy != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, targetEnemy.transform.position);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(targetEnemy.transform.position, killRadius);
        }

        if (enableDynamicColliders && enemyColliders != null)
        {
            foreach (var kvp in enemyColliders)
            {
                GameObject enemy = kvp.Key;
                SphereCollider sphereCol = kvp.Value;

                if (enemy != null && sphereCol != null)
                {
                    // Grün = Trigger, Rot = Collider
                    Gizmos.color = sphereCol.isTrigger ?
                        new Color(0, 1, 0, 0.3f) :
                        new Color(1, 0, 0, 0.3f);

                    Gizmos.DrawWireSphere(enemy.transform.position, sphereCol.radius);

                    // Target Radius visualisieren
                    if (targetColliderRadii.ContainsKey(enemy))
                    {
                        Gizmos.color = Color.yellow;
                        Gizmos.DrawWireSphere(enemy.transform.position, targetColliderRadii[enemy]);
                    }
                }
            }
        }
    }

    void OnGUI()
    {
        if (!showDebugInfo)
            return;

        if (isGrappling)
        {
            float distance = targetEnemy != null ?
                Vector3.Distance(transform.position, targetEnemy.transform.position) : 0f;
            GUI.Label(new Rect(10, 140, 300, 20), $"Grappling! Distance: {distance:F2}m");
        }

        if (isInSlowMotion)
        {
            float heightGained = transform.position.y - killHeight;
            GUI.Label(new Rect(10, 160, 400, 20),
                $"SLOW MOTION | Height: {heightGained:F2}/{heightDeltaToEndSlowMo:F2}m");

            if (!hasAutoJumped)
            {
                GUI.Label(new Rect(10, 180, 400, 20),
                    $"Auto-Jump in: {(slowMotionDuration - slowMotionTimer):F2}s");
            }
        }

        // NEUE: Collision Prevention Status
        GUI.color = Color.green;
        GUI.Label(new Rect(10, 200, 400, 20),
            $"Triggers: {(useTriggersInsteadOfColliders ? "ON" : "OFF")} | " +
            $"Registered Objects: {enemyColliders.Count}");
        GUI.color = Color.white;
    }
}