using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Splines;

/// <summary>
/// Spline-Grind auf SC_FPSController-Basis. Bewegt den Spieler smooth entlang
/// einer Spline-Bahn (Achterbahn-artig).
///
/// Smoothness-Design:
///   - CharacterController wird EINMAL beim Start deaktiviert, EINMAL beim Ende
///     wieder aktiviert (kein enable/disable pro Frame -> kein Geflacker).
///   - Bewegung läuft in FixedUpdate mit Time.fixedDeltaTime (deterministischer
///     Tick, smoother bei hoher Geschwindigkeit als der variable Update-Tick).
///   - Player-Yaw folgt der Spline-Tangente per Slerp (sanfter Übergang in
///     Kurven, nicht hartes Snapping).
///
/// Steuerung während des Grinds:
///   - Mauseingabe für Kamera-Pitch (hoch/runter schauen) bleibt aktiv -
///     der Spieler kann sich umsehen, der Körper folgt der Bahn unabhängig.
///   - Maus-Yaw (rechts/links schauen) wird vom Grind übernommen -
///     der Körper folgt automatisch der Bahn.
///   - Leertaste = Frühausstieg, katapultiert in Blickrichtung mit voller Grind-
///     Geschwindigkeit + Sprung-Boost nach oben.
/// </summary>
public class SplineGrindHandler : MonoBehaviour
{
    [Header("References")]
    public SC_FPSController fpsController;
    public CharacterController characterController;
    public Camera playerCamera;

    [Tooltip("Optional - bekommt beim Grind-Ende einen OnJumpAction-Aufruf, damit die Combo-Kette nicht abreißt")]
    public VelocityMultiplier velocityMultiplier;

    [Header("Grind-Geschwindigkeit")]
    [Tooltip("Minimale Grind-Geschwindigkeit (m/s) - egal wie langsam der Spieler ankommt, er grindet mindestens so schnell")]
    public float minGrindSpeed = 20f;

    [Tooltip("Maximale Grind-Geschwindigkeit (m/s)")]
    public float maxGrindSpeed = 45f;

    [Tooltip("Faktor, mit dem das Anlauftempo in Grind-Speed übersetzt wird (höher = schnellerer Grind bei hohem Anlauf)")]
    public float entrySpeedMultiplier = 1.5f;

    [Header("Verhalten")]
    [Tooltip("Vertikaler Offset, damit der Spieler optisch ÜBER der Schiene sitzt statt mittig drin")]
    public float verticalOffset = 0.5f;

    [Tooltip("Wie schnell der Spieler-Körper sich in die Spline-Tangentenrichtung dreht (höher = schneller/härter, niedriger = sanfter in Kurven)")]
    public float yawAlignSpeed = 12f;

    [Tooltip("Kurzer Cooldown nach Grind-Ende, bevor erneut gegrinded werden kann (verhindert sofortiges Re-Triggern am Endtrigger)")]
    public float regrindCooldown = 0.3f;

    [Header("Frühausstieg per Leertaste")]
    [Tooltip("Wie stark der Spieler beim Aussteigen nach vorne katapultiert wird (m/s, in horizontaler Blickrichtung)")]
    public float earlyExitForwardBoost = 25f;

    [Tooltip("Wie stark der Sprung-Boost nach oben beim Aussteigen ist")]
    public float earlyExitUpwardBoost = 10f;

    [Header("Kamera-Pitch während des Grinds")]
    [Tooltip("Sensitivität der Mauseingabe für Hoch/Runter-Schauen (entspricht SC_FPSController.lookSpeed)")]
    public float lookSpeed = 2.0f;

    [Tooltip("Maximaler Pitch-Ausschlag nach oben/unten")]
    public float lookXLimit = 45.0f;

    [Header("Debug")]
    public bool showDebugInfo = true;

    private bool isGrinding = false;
    private SplineContainer currentSpline;
    private int currentSplineIndex;
    private bool forward;
    private float currentT;
    private float grindSpeed;
    private float splineLength;
    private float lastGrindEndTime = -999f;

    // Kamera-Pitch wird selbst verwaltet, während canMove im SC_FPSController auf
    // false steht und dessen eigene Look-Logik dadurch ausgeschaltet ist
    private float cameraPitchDuringGrind = 0f;

    // Launch-Velocity, die nach dem Frühausstieg per LateUpdate in den
    // SC_FPSController geschrieben wird, NACHDEM dessen Update() seinen
    // moveDirection aus Input neu gesetzt hat - sonst würde der horizontale
    // Schub vom Input-basierten Setzen sofort überschrieben werden.
    private bool pendingEarlyExitLaunch = false;
    private Vector3 pendingLaunchVelocity = Vector3.zero;

    void Awake()
    {
        if (fpsController == null) fpsController = GetComponent<SC_FPSController>();
        if (characterController == null) characterController = GetComponent<CharacterController>();
        if (velocityMultiplier == null) velocityMultiplier = GetComponent<VelocityMultiplier>();
        if (playerCamera == null && fpsController != null) playerCamera = fpsController.playerCamera;

        if (fpsController == null)
            Debug.LogError("❌ SplineGrindHandler: SC_FPSController nicht gefunden!");
        if (characterController == null)
            Debug.LogError("❌ SplineGrindHandler: CharacterController nicht gefunden!");
    }

    /// <summary>
    /// Wird von SplineGrindRail aufgerufen, wenn der Spieler ein Trigger-Ende berührt.
    /// </summary>
    public void StartGrind(SplineContainer spline, int splineIndex, bool fromStart)
    {
        if (isGrinding) return;
        if (Time.time - lastGrindEndTime < regrindCooldown) return;
        if (fpsController == null || characterController == null) return;

        currentSpline = spline;
        currentSplineIndex = splineIndex;
        forward = fromStart;
        currentT = fromStart ? 0f : 1f;

        float entrySpeed = characterController.velocity.magnitude * entrySpeedMultiplier;
        grindSpeed = Mathf.Clamp(entrySpeed, minGrindSpeed, maxGrindSpeed);

        splineLength = spline.CalculateLength(splineIndex);
        if (splineLength < 0.01f) splineLength = 0.01f;

        isGrinding = true;
        fpsController.canMove = false;       // blockt Bewegung UND Maus-Yaw im SC_FPSController
        characterController.enabled = false; // EINMAL deaktivieren, kein Toggling pro Frame

        // Aktuellen Kamera-Pitch übernehmen, damit die Pitch-Steuerung während des
        // Grinds dort weitermacht, wo der Spieler vorher war
        if (playerCamera != null)
        {
            float currentPitch = playerCamera.transform.localEulerAngles.x;
            if (currentPitch > 180f) currentPitch -= 360f; // Wrap von 270° -> -90°
            cameraPitchDuringGrind = currentPitch;
        }

        if (showDebugInfo)
            Debug.Log($"🛤️ Grind gestartet! Richtung: {(forward ? "vorwärts" : "rückwärts")} | Speed: {grindSpeed:F1} m/s | Bahnlänge: {splineLength:F1} m");
    }

    void Update()
    {
        if (!isGrinding) return;

        // Mauseingabe für Kamera-Pitch (hoch/runter schauen) während des Grinds
        // selbst verarbeiten, da SC_FPSController durch canMove=false aus ist.
        // Pitch in Update statt FixedUpdate, weil Mauseingabe pro Frame variiert
        // und in FixedUpdate-Schritten holprig wirkt.
        HandleCameraPitchInput();

        // Frühausstieg per Leertaste
        if (Input.GetButtonDown("Jump"))
        {
            PerformEarlyExit();
        }
    }

    void HandleCameraPitchInput()
    {
        if (playerCamera == null) return;

        cameraPitchDuringGrind += -Input.GetAxis("Mouse Y") * lookSpeed;
        cameraPitchDuringGrind = Mathf.Clamp(cameraPitchDuringGrind, -lookXLimit, lookXLimit);
        playerCamera.transform.localRotation = Quaternion.Euler(cameraPitchDuringGrind, 0f, 0f);
    }

    void FixedUpdate()
    {
        if (!isGrinding) return;

        // Fortschritt entlang der Bahn mit festem Tick
        float deltaT = (grindSpeed / splineLength) * Time.fixedDeltaTime;
        currentT += forward ? deltaT : -deltaT;

        bool reachedEnd = forward ? (currentT >= 1f) : (currentT <= 0f);
        float clampedT = Mathf.Clamp01(currentT);

        // Position auf der Spline (Weltkoordinaten, +Offset für sichtbares "Aufsitzen")
        Vector3 targetPos = (Vector3)currentSpline.EvaluatePosition(currentSplineIndex, clampedT);
        targetPos.y += verticalOffset;
        transform.position = targetPos;

        // Player-Körper-Yaw zur Tangente drehen (sanft per Slerp, damit Kurven
        // smooth wirken statt hart zu snappen). Pitch/Roll des Körpers bleiben
        // bei 0 - es soll sich nicht der ganze Spieler vornüberkippen, nur der
        // Yaw folgt der Bahn, der Spieler bleibt aufrecht stehen.
        Vector3 tangent = (Vector3)currentSpline.EvaluateTangent(currentSplineIndex, clampedT);
        if (!forward) tangent = -tangent;
        Vector3 flatTangent = new Vector3(tangent.x, 0f, tangent.z);

        if (flatTangent.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(flatTangent.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, yawAlignSpeed * Time.fixedDeltaTime);
        }

        if (reachedEnd)
            EndGrind();
    }

    void PerformEarlyExit()
    {
        // Beim Frühausstieg in die aktuelle BLICKRICHTUNG des Spielers katapultieren,
        // nicht in die Schienenrichtung - das ist der Sinn der Mechanik: der Spieler
        // hat sich umgeschaut, zielt mit der Kamera, und springt dorthin ab.
        Vector3 launchDir;
        if (playerCamera != null)
        {
            Vector3 camForward = playerCamera.transform.forward;
            camForward.y = 0f;
            launchDir = camForward.sqrMagnitude > 0.0001f ? camForward.normalized : transform.forward;
        }
        else
        {
            launchDir = transform.forward;
        }

        // Velocity zusammenbauen: voller horizontaler Schub + vertikaler Sprung-Boost
        Vector3 launchVelocity = launchDir * earlyExitForwardBoost;
        launchVelocity.y = earlyExitUpwardBoost;

        EndGrind();

        // SC_FPSController.Update() läuft in dieser Frame-Sequenz möglicherweise NACH
        // unserem Update() und überschreibt moveDirection.x/.z aus Input. Daher
        // merken wir die Velocity hier nur vor und schreiben sie in LateUpdate,
        // wenn SC_FPSController.Update() garantiert schon durch ist.
        pendingEarlyExitLaunch = true;
        pendingLaunchVelocity = launchVelocity;

        if (showDebugInfo)
            Debug.Log($"🚀 Grind-Frühausstieg! Launch-Velocity: {launchVelocity} ({launchVelocity.magnitude:F1} m/s) in Blickrichtung");
    }

    void LateUpdate()
    {
        if (!pendingEarlyExitLaunch) return;

        // SC_FPSController.Update() ist jetzt garantiert durch (LateUpdate läuft
        // nach allen Update()s). Velocity setzen für den nächsten Frame.
        if (fpsController != null)
        {
            fpsController.ModifyMoveDirection(pendingLaunchVelocity);
        }

        pendingEarlyExitLaunch = false;
        pendingLaunchVelocity = Vector3.zero;
    }

    void EndGrind()
    {
        if (!isGrinding) return;

        // 1. Momentum für den automatischen Ausstieg berechnen
        // Ersetze 'currentGrindSpeed' durch den exakten Namen deiner Variable 
        // für die aktuelle Geschwindigkeit auf der Schiene.
        float exitSpeed = 20f; // Platzhalter für deine aktuelle Grind-Geschwindigkeit

        // Wir nutzen transform.forward, da dein Skript den Player-Yaw ohnehin 
        // an die Spline-Tangente anpasst. Ein leichter Up-Vektor verhindert 
        // das sofortige Absinken.
        Vector3 exitVelocity = (transform.forward * exitSpeed) + (Vector3.up * 2f);

        // 2. Deine bewährte LateUpdate Logik triggern
        pendingEarlyExitLaunch = true;
        pendingLaunchVelocity = exitVelocity;

        // 3. Bestehende Cleanup Logik
        isGrinding = false;
        lastGrindEndTime = Time.time;

        if (characterController != null)
            characterController.enabled = true;

        if (fpsController != null)
            fpsController.canMove = true;

        if (velocityMultiplier != null)
            velocityMultiplier.OnJumpAction();

        if (showDebugInfo)
            Debug.Log("🛤️ Grind beendet (mit Momentum in Blickrichtung)");
    }

    public bool IsGrinding() => isGrinding;
}
