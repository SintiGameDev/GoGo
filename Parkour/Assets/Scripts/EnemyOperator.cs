using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyOperator : MonoBehaviour
{
    [Header("Grapple Settings")]
    public float grappleSpeed = 25f;

    [Header("Magnet-Anziehung (Ease-In)")]
    [Tooltip("Geschwindigkeit ganz am Anfang des Grapples (relativ zu grappleSpeed, 0-1). " +
             "Niedrig = deutlich spürbares 'Anschnappen' statt sofortiger Vollgeschwindigkeit.")]
    [Range(0f, 1f)]
    public float grappleStartSpeedFactor = 0.25f;
    [Tooltip("Sekunden, bis die volle grappleSpeed erreicht ist (Ease-In-Dauer). " +
             "Kleiner = schnellerer, snappier Magnet-Ruck.")]
    public float grappleAccelTime = 0.18f;
    [Tooltip("Kurvenform des Ease-In: <1 = langsamer Start mit hartem Schub am Ende (magnetisch), " +
             "1 = linear, >1 = sanfter Auslauf")]
    public float grappleAccelCurvePower = 0.5f;
    public float killRadius = 2f;
    public string grabbableTag = "Grabbable";

    [Header("Momentum nach erfolgreichem Grapple")]
    [Tooltip("Wie viel der grappleSpeed nach einem ERFOLGREICHEN Grapple (Gegner erreicht/zerstört) als Schwung erhalten bleibt. 0 = kompletter Stop wie bisher, 1 = volle grappleSpeed bleibt erhalten. Gilt NICHT bei abgebrochenem Grapple (EndGrapple(false)).")]
    [Range(0f, 1.5f)]
    public float momentumRetentionMultiplier = 0.8f;

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
    [Tooltip("Maximale Highlight-Staerke (0-1), die an _HighlightStrength im Shader gesendet wird")]
    [Range(0f, 1f)]
    public float highlightStrength = 1f;
    [Tooltip("Pulsieren des Highlights, damit der Spieler deutlich sieht: hier kann interagiert werden")]
    public bool pulseHighlight = true;
    public float pulseSpeed = 4f;
    [Tooltip("Untere Grenze des Pulsierens (relativ zu highlightStrength)")]
    [Range(0f, 1f)]
    public float pulseMin = 0.4f;

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
    [Tooltip("Radius der tatsächlichen Mesh-Oberfläche für den Hit-Tracer. " +
             "WICHTIG: komplett unabhängig vom SphereCollider (der ist nur die " +
             "distanzbasierte Grab-Trigger-Range und kann viel größer/kleiner sein " +
             "als das echte Mesh). Bei gleich großen Gegnern reicht ein globaler Wert.")]
    public float killSurfaceRadius = 0.6f;
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

    // Optionaler Linseneffekt-Director (Neon-White Game Feel). Wird in Start()
    // per GetComponent geholt; null-safe, das Grapple-System funktioniert auch ohne.
    private GrappleLensDirector lensDirector;

    // Letzte tatsächliche Flugrichtung während des Grapples (wird in jedem
    // UpdateGrapple()-Tick aktualisiert) - EndGrapple(true) nutzt das, um dem
    // Spieler beim Loslassen seinen Schwung in genau dieser Richtung mitzugeben,
    // statt dass er abrupt stehen bleibt (SC_FPSController.moveDirection wurde
    // während des Grapples nie aktualisiert, da die Bewegung über einen
    // separaten direkten characterController.Move()-Aufruf hier im Skript läuft).
    private Vector3 lastGrappleDirection = Vector3.zero;
    private Vector3 grappleTarget;
    private GameObject targetEnemy;
    private Transform targetHitVolumeTransform; // Child-Collider-Transform, liefert die echte Hit-Position
    private float grappleElapsedTime = 0f;
    // Start-Flugstrecke (Reststrecke beim ersten UpdateGrapple-Tick, Hit-Tracer-
    // basiert: distance - impactDistance). Referenz fuer den streckenbasierten
    // Linseneffekt-Fortschritt (1 - rest/start) statt der zeitbasierten Rampe.
    private float grappleStartRemainingDistance = -1f;

    private bool isInSlowMotion = false;
    private float slowMotionTimer = 0f;
    private float killHeight = 0f;
    private bool hasAutoJumped = false;

    private GameObject currentHighlightedObject = null;
    private Renderer highlightedRenderer = null;
    private MaterialPropertyBlock highlightPropBlock;
    private static readonly int HighlightStrengthID = Shader.PropertyToID("_HighlightStrength");

    // Dynamic Collider State - ÜBERARBEITET
    private Dictionary<GameObject, SphereCollider> enemyColliders = new Dictionary<GameObject, SphereCollider>();
    private Dictionary<GameObject, float> originalColliderRadii = new Dictionary<GameObject, float>();
    private Dictionary<GameObject, float> targetColliderRadii = new Dictionary<GameObject, float>(); // NEUE: Smooth Updates
    private Dictionary<GameObject, bool> wasColliderOriginallyTrigger = new Dictionary<GameObject, bool>(); // NEUE: Original State

    void Start()
    {
        fpsController = GetComponent<SC_FPSController>();
        characterController = GetComponent<CharacterController>();
        lensDirector = GetComponent<GrappleLensDirector>();

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
            UpdateHighlightPulse();
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
                // Der Tag sitzt jetzt auf dem unsichtbaren Child-HitVolume, NICHT
                // mehr auf dem Enemy-Parent. enemyRoot zeigt zurueck auf das
                // eigentliche Objekt mit Mesh/Renderer/Shader (fuer Highlight/Destroy).
                EnemyHitVolume hitVolume = hit.collider.GetComponent<EnemyHitVolume>();
                GameObject enemyRoot = hitVolume != null && hitVolume.enemyRoot != null
                    ? hitVolume.enemyRoot
                    : hit.collider.gameObject; // Fallback falls kein HitVolume vorhanden

                StartGrapple(enemyRoot, hit.collider.transform, hit.point);
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
                // Highlight (Fresnel-Rim) laeuft weiterhin auf dem Parent-Renderer,
                // auch wenn der Raycast jetzt das Child-HitVolume trifft.
                EnemyHitVolume hitVolume = hit.collider.GetComponent<EnemyHitVolume>();
                GameObject enemyRoot = hitVolume != null && hitVolume.enemyRoot != null
                    ? hitVolume.enemyRoot
                    : hit.collider.gameObject;

                if (currentHighlightedObject != enemyRoot)
                {
                    RemoveHighlight();
                    ApplyHighlight(enemyRoot);
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
            if (highlightPropBlock == null)
                highlightPropBlock = new MaterialPropertyBlock();

            highlightedRenderer.GetPropertyBlock(highlightPropBlock);
            highlightPropBlock.SetFloat(HighlightStrengthID, highlightStrength);
            highlightedRenderer.SetPropertyBlock(highlightPropBlock);
        }
    }

    // Pulsieren: laeuft jeden Frame, solange ein Objekt gehighlightet ist.
    void UpdateHighlightPulse()
    {
        if (currentHighlightedObject == null || highlightedRenderer == null)
            return;

        if (!pulseHighlight)
            return;

        float t = (Mathf.Sin(Time.time * pulseSpeed) * 0.5f + 0.5f);
        float strength = Mathf.Lerp(highlightStrength * pulseMin, highlightStrength, t);

        highlightedRenderer.GetPropertyBlock(highlightPropBlock);
        highlightPropBlock.SetFloat(HighlightStrengthID, strength);
        highlightedRenderer.SetPropertyBlock(highlightPropBlock);
    }

    void RemoveHighlight()
    {
        if (currentHighlightedObject != null && highlightedRenderer != null)
        {
            if (highlightPropBlock == null)
                highlightPropBlock = new MaterialPropertyBlock();

            // Nur die Highlight-Staerke auf 0 -> Rim verschwindet, restliche
            // (ggf. von anderen Skripten gesetzte) Properties bleiben erhalten.
            highlightedRenderer.GetPropertyBlock(highlightPropBlock);
            highlightPropBlock.SetFloat(HighlightStrengthID, 0f);
            highlightedRenderer.SetPropertyBlock(highlightPropBlock);
        }

        currentHighlightedObject = null;
        highlightedRenderer = null;
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

    void StartGrapple(GameObject enemy, Transform hitVolumeTransform, Vector3 hitPoint)
    {
        RemoveHighlight();

        grappleElapsedTime = 0f;
        grappleStartRemainingDistance = -1f; // wird im ersten UpdateGrapple-Tick gesetzt
        targetHitVolumeTransform = hitVolumeTransform;

        // NEUE: Collider komplett deaktivieren während Grapple (verhindert ANY Collision)
        // Key ist jetzt das Child-HitVolume-GameObject (traegt den SphereCollider),
        // NICHT mehr der Enemy-Parent.
        GameObject hitVolumeGO = hitVolumeTransform != null ? hitVolumeTransform.gameObject : enemy;
        if (enableDynamicColliders && enemyColliders.ContainsKey(hitVolumeGO))
        {
            SphereCollider sphereCol = enemyColliders[hitVolumeGO];
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
        if (lensDirector != null) lensDirector.OnGrappleStart();
        targetEnemy = enemy;
        grappleTarget = hitVolumeTransform != null ? hitVolumeTransform.position : enemy.transform.position;

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

        // Position des Child-HitVolumes nutzen (das ist die tatsaechliche
        // Kill-Geometrie), NICHT mehr targetEnemy.transform.position direkt -
        // Parent koennte z.B. einen abweichenden Pivot haben.
        Transform hitTransform = targetHitVolumeTransform != null ? targetHitVolumeTransform : targetEnemy.transform;

        grappleTarget = hitTransform.position;
        Vector3 toTarget = grappleTarget - transform.position;
        float distance = toTarget.magnitude;
        Vector3 direction = distance > 0.0001f ? toTarget / distance : Vector3.zero;

        // Für den Momentum-Erhalt nach EndGrapple(true) merken, in welche Richtung
        // der Spieler sich GERADE bewegt - direction kann sich über die Dauer des
        // Grapples leicht ändern (Gegner/Spieler bewegen sich), daher hier bei
        // jedem Tick aktualisieren statt nur einmal beim Start zu berechnen.
        if (direction != Vector3.zero)
        {
            lastGrappleDirection = direction;
        }

        // Hit Tracer: Radius kommt jetzt direkt vom Child-SphereCollider (echte
        // Hit-Geometrie, vom Tess-Shader/Vertex-Displacement komplett entkoppelt -
        // der Collider sitzt auf einem eigenen, unsichtbaren Child-Objekt).
        float impactDistance = CalculateImpactDistance(hitTransform.gameObject, transform.position, direction, distance);

        if (characterController != null)
        {
            // Magnet-Anziehung: Ease-In statt konstanter grappleSpeed. Start
            // bei grappleStartSpeedFactor (z.B. 25%), rampt binnen grappleAccelTime
            // Sekunden auf volle grappleSpeed hoch. grappleAccelCurvePower < 1
            // sorgt fuer einen spuerbaren "Schub" Richtung Ende der Rampe statt
            // gleichmaessigem Anstieg -> fuehlt sich an wie ein zuschnappender Magnet.
            grappleElapsedTime += Time.unscaledDeltaTime;
            float accelT = grappleAccelTime > 0.0001f
                ? Mathf.Clamp01(grappleElapsedTime / grappleAccelTime)
                : 1f;
            float curvedT = Mathf.Pow(accelT, Mathf.Max(0.01f, grappleAccelCurvePower));
            float speedFactor = Mathf.Lerp(grappleStartSpeedFactor, 1f, curvedT);
            float currentGrappleSpeed = grappleSpeed * speedFactor;

            // unscaledDeltaTime: der Grapple soll IMMER gleich knackig sein,
            // auch wenn gerade noch eine Slow-Motion aus einem vorherigen Kill
            // laeuft (timeScale < 1). Mit Time.deltaTime wuerde der Flug sonst
            // entsprechend der Slow-Motion verlangsamt -> "zaehes" Grabbing.
            float step = currentGrappleSpeed * Time.unscaledDeltaTime;

            // Wie weit dürfen wir uns noch nähern, bevor wir die Mesh-Oberfläche erreichen?
            float maxAllowedStep = Mathf.Max(0f, distance - impactDistance);

            // Streckenbasierter Linseneffekt-Fortschritt (Hit-Tracer-basiert):
            // beim ersten Tick die noch zu fliegende Reststrecke als Referenz
            // einfrieren, danach progress = 1 - (rest / start). Dadurch laeuft
            // die Beat-Kurve ueber die ECHTE Flugdauer ab und passt sich der
            // Distanz an - egal ob der Gegner nah oder weit weg ist.
            if (grappleStartRemainingDistance < 0f)
            {
                grappleStartRemainingDistance = Mathf.Max(0.0001f, maxAllowedStep);
            }
            if (lensDirector != null)
            {
                float flightProgress = 1f - Mathf.Clamp01(maxAllowedStep / grappleStartRemainingDistance);
                lensDirector.OnGrappleProgress(flightProgress);
            }

            // Bewegung clampen -> Spieler bleibt immer vor der echten Mesh-Oberfläche
            float clampedStep = Mathf.Min(step, maxAllowedStep);

            if (clampedStep > 0f)
            {
                characterController.Move(direction * clampedStep);
            }
        }

        if (distance <= impactDistance)
        {
            // Zielposition VOR dem Destroy cachen: targetEnemy/targetHitVolumeTransform
            // werden in DestroyEnemyWithSlowMotion() bzw. danach ungueltig, der Spieler
            // soll aber trotzdem exakt an dieser Stelle ankommen (gewuenschte Bewegungs-
            // mechanik: Position des Enemies einnehmen, nicht kurz davor stehen bleiben).
            Vector3 finalSnapPosition = grappleTarget;
            DestroyEnemyWithSlowMotion();
            SnapToFinalGrapplePosition(finalSnapPosition);
        }
    }

    // Bewegt den Spieler im selben Frame des Kills zur tatsaechlichen Enemy-
    // Position, statt nur kurz vor der Aufprall-Distanz stehen zu bleiben.
    // Nutzt characterController.Move() mit dem Differenzvektor -> respektiert
    // weiterhin Unity-Kollisionsregeln (kein Teleport durch Waende).
    void SnapToFinalGrapplePosition(Vector3 finalPosition)
    {
        if (characterController == null) return;

        Vector3 delta = finalPosition - transform.position;
        if (delta.sqrMagnitude > 0.0001f)
        {
            characterController.Move(delta);
        }
    }

    // Hit Tracer - berechnet den Abstand zur Kill-Oberflaeche anhand von
    // killSurfaceRadius (fester, globaler Wert fuer die echte Mesh-Groesse).
    // BEWUSST NICHT der SphereCollider aus enemyColliders: jener Collider ist
    // die distanzbasierte Grab-TRIGGER-Range (skaliert 0.5-2.5 je nach Kamera-
    // Abstand) und hat keinen Bezug zur tatsaechlichen visuellen Mesh-Groesse -
    // genau das fuehrte zum "Instant-Kill beim Anklicken"-Bug. Ebenfalls NICHT
    // Renderer.bounds: der TraumweltDeformTess-Shader verschiebt Vertices per
    // Tessellation (_NoiseAmp), wodurch Bounds verzerrt/aufgeblaeht sein koennen.
    // Hit Tracer - berechnet den Abstand zur Kill-Oberflaeche anhand des
    // ECHTEN SphereColliders auf dem Child-HitVolume ("enemy"-Parameter ist hier
    // das Child-GameObject, siehe Aufruf in UpdateGrapple). Dadurch ist die
    // Geometrie 1:1 das, was man im Scene-View sieht und direkt einstellen kann -
    // kein geschaetzter globaler Wert, kein vom Tess-Shader verzerrtes Renderer.bounds.
    float CalculateImpactDistance(GameObject hitVolumeGO, Vector3 fromPosition, Vector3 direction, float distanceToTarget)
    {
        SphereCollider sphereCol = hitVolumeGO.GetComponent<SphereCollider>();
        // Waehrend des Grapples wird sphereCol.radius bewusst auf 0.1f gesetzt
        // (StartGrapple, verhindert Physik-Kollision) - DANN den ORIGINAL-Radius
        // aus originalColliderRadii nehmen, sonst friert der Tracer auf 0.1 ein.
        // Sonst (kein aktiver Grapple-Collider-Override) IMMER den LIVE-Wert von
        // sphereCol.radius lesen, damit Inspector-Aenderungen sofort wirken -
        // der originalColliderRadii-Cache wird nur EINMAL bei der Registrierung
        // befuellt und wuerde Live-Edits sonst ignorieren.
        float sphereRadius;
        if (sphereCol != null)
        {
            bool isCollapsedForGrapple = isGrappling && Mathf.Approximately(sphereCol.radius, 0.1f);
            if (isCollapsedForGrapple && originalColliderRadii.TryGetValue(hitVolumeGO, out float origRadius))
            {
                sphereRadius = origRadius;
            }
            else
            {
                sphereRadius = sphereCol.radius;
            }
        }
        else
        {
            sphereRadius = Mathf.Max(killSurfaceRadius, fallbackImpactDistance);
        }

        if (direction == Vector3.zero)
        {
            return Mathf.Max(sphereRadius, fallbackImpactDistance);
        }

        Vector3 toCenter = hitVolumeGO.transform.position - fromPosition;
        float tCenter = Vector3.Dot(toCenter, direction);
        float distSqToAxis = toCenter.sqrMagnitude - tCenter * tCenter;
        float radiusSq = sphereRadius * sphereRadius;

        if (distSqToAxis <= radiusSq)
        {
            float offset = Mathf.Sqrt(radiusSq - distSqToAxis);
            float hitDistance = tCenter - offset;

            if (hitDistance >= 0f)
            {
                return Mathf.Clamp(hitDistance + impactSurfaceBuffer, 0f, distanceToTarget);
            }
        }

        // Strahl verfehlt die Sphere (z.B. Pivot stark versetzt) -> Fallback
        // auf direkten Abstand zur Sphere-Oberfläche entlang der Distanz.
        return Mathf.Clamp(distanceToTarget - sphereRadius + impactSurfaceBuffer, 0f, distanceToTarget);
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
            targetHitVolumeTransform = null; // Destroy() zerstört auch das Child mit
        }

        // NEUE: Weiß-Flash + FOV-Bounce im exakten Kill-Moment - kaschiert verbleibendes
        // Mesh-Clipping, da der Bildschirm im Aufprall-Frame kurz aufhellt.
        if (fpsController != null)
        {
            fpsController.TriggerImpactFeedback();
        }
        if (lensDirector != null) lensDirector.OnGrappleImpact();

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

            // Schwung beim ERFOLGREICHEN Grapple-Abschluss mitgeben: dies ist der
            // tatsächliche Erfolgspfad (DestroyEnemyWithSlowMotion -> StartSlowMotion),
            // NICHT EndGrapple(true) - letzteres wird im Code nirgends mit "true"
            // aufgerufen. SC_FPSController.moveDirection.x/.z wurde während des
            // gesamten Grapples nie aktualisiert (separate direkte Move()-Aufrufe in
            // UpdateGrapple()), daher hier explizit nachträglich setzen, bevor der
            // spätere Auto-Jump (PerformAutoJump -> ForceJump) nur die Y-Komponente
            // anfasst und die hier gesetzte horizontale Velocity unangetastet lässt.
            Vector3 momentumVelocity = lastGrappleDirection * grappleSpeed * momentumRetentionMultiplier;
            fpsController.ModifyMoveDirection(momentumVelocity);
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
        targetHitVolumeTransform = null;

        if (fpsController != null)
        {
            fpsController.SetGrapplingState(false);

            // Schwung nur bei ERFOLGREICHEM Grapple mitgeben (Gegner erreicht/
            // zerstört) - bei einem Abbruch (z.B. Gegner verschwindet während
            // des Grapples) bewusst NICHT, das soll sich nicht wie eine Belohnung
            // anfühlen. SC_FPSController.moveDirection wurde während des Grapples
            // nie aktualisiert (siehe lastGrappleDirection-Kommentar oben), daher
            // hier explizit nachträglich die tatsächliche Flugrichtung+Tempo setzen.
            if (successful)
            {
                Vector3 momentumVelocity = lastGrappleDirection * grappleSpeed * momentumRetentionMultiplier;
                fpsController.ModifyMoveDirection(momentumVelocity);
            }
        }

        if (lensDirector != null && !successful) lensDirector.OnGrappleAbort();

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
            Transform hitT = targetHitVolumeTransform != null ? targetHitVolumeTransform : targetEnemy.transform;

            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, hitT.position);

            Gizmos.color = Color.yellow;
            SphereCollider hitSphere = hitT.GetComponent<SphereCollider>();
            float gizmoRadius = hitSphere != null
                ? (originalColliderRadii.TryGetValue(hitT.gameObject, out float r) ? r : hitSphere.radius)
                : killSurfaceRadius;
            Gizmos.DrawWireSphere(hitT.position, gizmoRadius);
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