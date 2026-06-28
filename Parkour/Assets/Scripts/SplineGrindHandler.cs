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

    [Tooltip("Dauer der sanften Magnet-Snap-Bewegung zur exakten Spline-Position beim Einstieg, falls der Spieler innerhalb des Magnet-Radius, aber nicht exakt auf der Linie war. Kurz halten (0.1-0.15s), damit es sich wie ein Snap anfühlt statt wie ein spürbarer Sog.")]
    public float magnetSnapDuration = 0.12f;

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

    // Magnet-Snap-Phase: läuft VOR dem regulären FixedUpdate-Grind-Loop, zieht
    // den Spieler sanft von seiner tatsächlichen Eintritts-Position zur exakten
    // Spline-Position. Nur aktiv, wenn der Spieler nicht bereits exakt auf der
    // Linie war (sonst wäre die Distanz ~0 und die Phase ist quasi unsichtbar).
    private bool isSnapping = false;
    private float snapStartTime = -999f;
    private Vector3 snapStartPosition;
    private Vector3 snapTargetPosition;

    // Interpolations-State für flüssige Bewegung: FixedUpdate (fester Tick,
    // z.B. 50Hz) berechnet nur die ZIEL-Position, transform.position wird aber
    // in Update() (läuft mit der tatsächlichen Bildschirm-Framerate, z.B. 144Hz)
    // zwischen der letzten und aktuellen Fixed-Position interpoliert. Ohne das
    // "springt" die Position nur alle Time.fixedDeltaTime Sekunden, was bei den
    // hohen Grind-Geschwindigkeiten (20-45 m/s) als sichtbares Ruckeln auffällt.
    private Vector3 previousFixedPosition;
    private Vector3 targetFixedPosition;

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
    /// Wird von SplineGrindRail aufgerufen, wenn der Spieler ein Bahn-Segment
    /// berührt - jetzt an JEDER Stelle der Bahn möglich, nicht nur an den Enden.
    /// startT ist die genaue Einstiegsposition auf der Spline (0..1), fromStart
    /// gibt die Richtung an (true = Richtung t=1, false = Richtung t=0), bereits
    /// aus der Spieler-Bewegungsrichtung von SplineGrindRail bestimmt.
    /// snapTargetPosition ist der exakte Weltpunkt auf der Spline, zu dem der
    /// Spieler per Magnet-Snap sanft hingezogen wird, falls er innerhalb des
    /// (größeren) Magnet-Radius, aber nicht exakt auf der Linie eingestiegen ist.
    /// </summary>
    public void StartGrind(SplineContainer spline, int splineIndex, bool fromStart, float startT = -1f, Vector3 snapTargetPos = default)
    {
        if (isGrinding) return;
        if (Time.time - lastGrindEndTime < regrindCooldown) return;
        if (fpsController == null || characterController == null) return;

        currentSpline = spline;
        currentSplineIndex = splineIndex;
        forward = fromStart;

        // startT < 0 signalisiert "kein Wert übergeben" (Abwärtskompatibilität zu
        // altem Aufruf-Stil mit nur 3 Argumenten) - dann auf das jeweilige Bahnende
        // zurückfallen, wie es das System vorher exklusiv getan hat.
        if (startT < 0f)
            currentT = fromStart ? 0f : 1f;
        else
            currentT = Mathf.Clamp01(startT);

        float entrySpeed = characterController.velocity.magnitude * entrySpeedMultiplier;
        grindSpeed = Mathf.Clamp(entrySpeed, minGrindSpeed, maxGrindSpeed);

        splineLength = spline.CalculateLength(splineIndex);
        if (splineLength < 0.01f) splineLength = 0.01f;

        isGrinding = true;
        fpsController.canMove = false;       // blockt Bewegung UND Maus-Yaw im SC_FPSController
        characterController.enabled = false; // EINMAL deaktivieren, kein Toggling pro Frame

        // Magnet-Snap-Phase einleiten: Ziel ist der exakte Spline-Punkt (inkl.
        // vertikalem Offset), Start ist die tatsächliche aktuelle Position des
        // Spielers (kann je nach Magnet-Radius spürbar von der Linie abweichen).
        // Falls snapTargetPos nicht übergeben wurde (default-Aufruf,
        // Abwärtskompatibilität), direkt auf die aktuelle Position zurückfallen -
        // dann ist die Distanz 0 und die Snap-Phase läuft quasi unsichtbar durch.
        Vector3 effectiveSnapTarget = snapTargetPos == default
            ? transform.position
            : snapTargetPos;
        effectiveSnapTarget.y += verticalOffset;

        snapStartPosition = transform.position;
        snapTargetPosition = effectiveSnapTarget;
        snapStartTime = Time.time;
        isSnapping = magnetSnapDuration > 0f && Vector3.Distance(snapStartPosition, snapTargetPosition) > 0.01f;

        // Interpolations-Startwerte auf die aktuelle Spieler-Position setzen (egal
        // ob Snap-Phase aktiv ist oder nicht) - sonst würde der Spieler im
        // allerersten Frame nach StartGrind() sichtbar zu seiner Einstiegsposition
        // "springen", bevor die Snap-Phase bzw. der reguläre Loop übernimmt.
        previousFixedPosition = transform.position;
        targetFixedPosition = transform.position;

        // Aktuellen Kamera-Pitch übernehmen, damit die Pitch-Steuerung während des
        // Grinds dort weitermacht, wo der Spieler vorher war
        if (playerCamera != null)
        {
            float currentPitch = playerCamera.transform.localEulerAngles.x;
            if (currentPitch > 180f) currentPitch -= 360f; // Wrap von 270° -> -90°
            cameraPitchDuringGrind = currentPitch;
        }

        if (showDebugInfo)
        {
            string snapInfo = isSnapping
                ? $" | Magnet-Snap: {Vector3.Distance(snapStartPosition, snapTargetPosition):F2}m über {magnetSnapDuration:F2}s"
                : "";
            Debug.Log($"🛤️ Grind gestartet! Richtung: {(forward ? "vorwärts" : "rückwärts")} | Speed: {grindSpeed:F1} m/s | Bahnlänge: {splineLength:F1} m{snapInfo}");
        }
    }

    void Update()
    {
        if (!isGrinding) return;

        // Mauseingabe für Kamera-Pitch (hoch/runter schauen) während des Grinds
        // selbst verarbeiten, da SC_FPSController durch canMove=false aus ist.
        // Pitch in Update statt FixedUpdate, weil Mauseingabe pro Frame variiert
        // und in FixedUpdate-Schritten holprig wirkt.
        HandleCameraPitchInput();

        if (isSnapping)
        {
            // Während der kurzen Magnet-Snap-Phase NICHT den regulären Grind-
            // Loop laufen lassen (kein Frühausstieg, keine Spline-Bewegung) -
            // der Spieler wird erst sanft zur Linie gezogen, bevor der
            // eigentliche Grind beginnt.
            UpdateMagnetSnap();
            return;
        }

        // Frühausstieg per Leertaste
        if (Input.GetButtonDown("Jump"))
        {
            PerformEarlyExit();
        }

        InterpolatePosition();
    }

    /// <summary>
    /// Bewegt den Spieler sanft von snapStartPosition zu snapTargetPosition über
    /// magnetSnapDuration. Läuft in Update() (nicht FixedUpdate), da die Snap-
    /// Phase sehr kurz ist (0.1-0.15s) und von der Bildschirm-Framerate profitiert,
    /// nicht vom festen Physik-Tick. Geht danach nahtlos in den regulären
    /// FixedUpdate-Grind-Loop über (EndMagnetSnap setzt die Interpolations-
    /// Startwerte dafür neu).
    /// </summary>
    void UpdateMagnetSnap()
    {
        float elapsed = Time.time - snapStartTime;

        if (elapsed >= magnetSnapDuration)
        {
            transform.position = snapTargetPosition;
            EndMagnetSnap();
            return;
        }

        float t = elapsed / magnetSnapDuration;
        // EaseOut: schnell am Anfang, sanft einrastend am Ende - fühlt sich wie
        // ein kurzer "Sog" an, der dann weich zum Stillstand auf der Linie kommt.
        float eased = 1f - Mathf.Pow(1f - t, 3f);
        transform.position = Vector3.Lerp(snapStartPosition, snapTargetPosition, eased);
    }

    /// <summary>
    /// Übergibt von der Snap-Phase in den regulären FixedUpdate-Grind-Loop.
    /// Setzt die Interpolations-Startwerte auf die jetzt exakte Spline-Position,
    /// damit InterpolatePosition() im nächsten Update() nicht plötzlich von der
    /// alten (Snap-Start-)Position aus zu interpolieren versucht.
    /// </summary>
    void EndMagnetSnap()
    {
        isSnapping = false;
        previousFixedPosition = transform.position;
        targetFixedPosition = transform.position;
    }

    /// <summary>
    /// Interpoliert transform.position zwischen der vorherigen und aktuellen
    /// FixedUpdate-Zielposition, basierend darauf, wie weit wir zeitlich zwischen
    /// zwei Fixed-Ticks stehen. Das verhindert das sichtbare "Springen" der
    /// Position alle Time.fixedDeltaTime Sekunden, das bei hohem Tempo (20-45 m/s)
    /// als Ruckeln auffällt - Standard-Fix für Bewegung, die in FixedUpdate
    /// berechnet, aber smooth dargestellt werden soll.
    /// </summary>
    void InterpolatePosition()
    {
        float interpolationFactor = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
        transform.position = Vector3.Lerp(previousFixedPosition, targetFixedPosition, interpolationFactor);
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
        if (isSnapping) return; // Snap-Phase läuft komplett in Update(), siehe UpdateMagnetSnap()

        // Vorherige Zielposition merken, BEVOR currentT weiterläuft - das ist
        // der Ausgangspunkt für die Interpolation in Update() bis zum nächsten Tick.
        previousFixedPosition = targetFixedPosition;

        // Fortschritt entlang der Bahn mit festem Tick
        float deltaT = (grindSpeed / splineLength) * Time.fixedDeltaTime;
        currentT += forward ? deltaT : -deltaT;

        bool reachedEnd = forward ? (currentT >= 1f) : (currentT <= 0f);
        float clampedT = Mathf.Clamp01(currentT);

        // Position auf der Spline (Weltkoordinaten, +Offset für sichtbares "Aufsitzen")
        Vector3 newTargetPos = (Vector3)currentSpline.EvaluatePosition(currentSplineIndex, clampedT);
        newTargetPos.y += verticalOffset;
        targetFixedPosition = newTargetPos;

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

        // try/finally: GARANTIERT, dass characterController.enabled wieder auf
        // true gesetzt wird, selbst wenn im Block darüber etwas schiefgeht (z.B.
        // ein ungültiger t-Wert bei EvaluateTangent). Vorher konnte eine Exception
        // hier den Controller dauerhaft deaktiviert lassen, ohne dass EndGrind()
        // je den unteren Teil der Methode erreicht hat.
        try
        {
            // currentT defensiv klemmen, BEVOR es für EvaluateTangent genutzt wird -
            // durch den festen FixedUpdate-Tick kann currentT beim Erreichen des
            // Bahnendes leicht über 1.0 bzw. unter 0.0 liegen (z.B. 1.03), was
            // außerhalb des für EvaluateTangent gültigen Bereichs [0,1] liegt.
            float safeT = Mathf.Clamp01(currentT);

            // Exit-Geschwindigkeit = die tatsächliche Grind-Geschwindigkeit DIESER
            // Fahrt (nicht mehr der alte Fixwert-Platzhalter) - ein schneller Anlauf
            // ergibt dadurch auch einen schnelleren Ausstieg am Bahnende.
            float exitSpeed = grindSpeed;

            // Exit-Richtung aus der Spline-Tangente am tatsächlichen Endpunkt holen,
            // nicht aus transform.forward - der Spieler-Yaw folgt der Tangente nur
            // per Slerp (siehe FixedUpdate) und kann ihr dadurch leicht hinterherhängen,
            // besonders in engen Kurven kurz vor dem Bahnende.
            Vector3 tangent = (Vector3)currentSpline.EvaluateTangent(currentSplineIndex, safeT);
            Vector3 exitDir = forward ? tangent.normalized : -tangent.normalized;

            Vector3 exitVelocity = (exitDir * exitSpeed) + (Vector3.up * 2f);

            pendingEarlyExitLaunch = true;
            pendingLaunchVelocity = exitVelocity;

            if (showDebugInfo)
                Debug.Log($"🛤️ Grind beendet (Exit-Speed: {exitSpeed:F1} m/s in Bahnrichtung)");
        }
        catch (System.Exception ex)
        {
            // Falls hier doch etwas schiefgeht: loggen, aber NICHT crashen lassen,
            // damit der finally-Block unten garantiert noch läuft.
            Debug.LogError($"❌ SplineGrindHandler.EndGrind(): Fehler bei Exit-Berechnung, Spieler wird trotzdem freigegeben. Fehler: {ex.Message}");
        }
        finally
        {
            // Diese Aufräumarbeiten laufen IMMER, auch wenn der try-Block oben
            // eine Exception geworfen hat - das ist der eigentliche Fix für das
            // "CharacterController bleibt nach dem Grind deaktiviert"-Problem.
            isGrinding = false;
            lastGrindEndTime = Time.time;

            if (characterController != null)
                characterController.enabled = true;

            if (fpsController != null)
                fpsController.canMove = true;

            if (velocityMultiplier != null)
                velocityMultiplier.OnJumpAction();
        }
    }

    public bool IsGrinding() => isGrinding;
}
