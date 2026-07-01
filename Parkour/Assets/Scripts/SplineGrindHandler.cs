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

    [Tooltip("Zusätzliche Glättung NUR für die Kamera-Rotation (unabhängig vom Körper-Yaw oben). Niedriger = weicher, nimmt Tangenten-Sprünge in engen Kurven weniger direkt mit. Empfehlung: 6-10, deutlich weicher als yawAlignSpeed.")]
    public float cameraYawSmoothSpeed = 8f;

    [Tooltip("Glättet die für die Bewegung verwendete Tangente über ein kleines Zeitfenster (Exponential-Moving-Average). Reduziert Mikro-Zacken aus eng aufeinanderfolgenden Spline-Kontrollpunkten, die sonst als Ruckeln in Kurven auffallen. 0 = aus.")]
    public float tangentSmoothSpeed = 15f;

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
    private float localSplineLength; // unskalierte Länge, wie sie GetPointAtLinearDistance erwartet
    private float lastGrindEndTime = -999f;

    // Zurückgelegte Bogenlänge (m) seit Grind-Start - Basis für die distanzbasierte
    // Bewegung. Ersetzt die rein t-basierte Fortbewegung: t ist KEIN linearer
    // Indikator für Bogenlänge (in engen Kurven legt ein gleich großes delta-t
    // weniger tatsächliche Meter zurück als auf Geraden), was bei konstantem
    // deltaT in Kurven zu spürbaren Geschwindigkeits-/Rucken-Artefakten führte.
    // SplineUtility.GetPointAtLinearDistance arbeitet stattdessen direkt in Metern.
    private float traveledDistance = 0f;

    // Geglätteter (gefilterter) Tangenten-Wert, der tatsächlich für Körper- UND
    // Kamera-Rotation verwendet wird. Roh-Tangenten aus eng beieinanderliegenden
    // Spline-Kontrollpunkten können von Sample zu Sample leicht "zittern" - das
    // schlägt sonst direkt auf die Rotation durch. Ein EMA-Filter glättet das,
    // ohne die Reaktionsfähigkeit in echten Kurven sichtbar zu verzögern.
    private Vector3 smoothedTangent = Vector3.forward;

    // Separate, weichere Yaw-Rotation NUR für die Kamera (zusätzlich zur
    // Körper-Rotation in FixedUpdate). Entkoppelt die Kamera von kurzfristigen
    // Tangenten-Sprüngen in engen Kurven, die der Körper per Slerp zwar auch
    // glättet, aber mit yawAlignSpeed ggf. immer noch zu direkt übernimmt.
    private Quaternion cameraYawRotation = Quaternion.identity;

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

    // Selbst akkumulierter Timer für die Positions-Interpolation zwischen zwei
    // FixedUpdate-Ticks. WARUM nicht einfach (Time.time - Time.fixedTime) /
    // Time.fixedDeltaTime: Time.fixedTime beschreibt in neueren Unity-Versionen
    // den Zeitpunkt des aktuellen Physik-Subschritts, nicht zuverlässig "Zeit seit
    // letztem abgeschlossenen FixedUpdate relativ zu jetzt". Bei schwankender
    // Framerate (z.B. durch Partikel/Shader-Last während des Grinds) kann der
    // daraus berechnete Bruchteil unsauber zwischen 0 und 1 hin- und herspringen
    // oder kurz an den Rändern "kleben" - das erzeugt genau das beobachtete
    // Positions-Hoppeln. Ein selbst geführter Timer, der in FixedUpdate auf 0
    // zurückgesetzt und in Update() per Time.deltaTime hochgezählt wird, liefert
    // garantiert einen monoton steigenden, saubere 0->1-Bruchteil.
    private float fixedUpdateTimer = 0f;

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
        if (isGrinding)
        {
            if (showDebugInfo) Debug.Log("🔍 StartGrind() abgewiesen: isGrinding bereits true");
            return;
        }
        if (Time.time - lastGrindEndTime < regrindCooldown)
        {
            if (showDebugInfo) Debug.Log($"🔍 StartGrind() abgewiesen: regrindCooldown aktiv (noch {regrindCooldown - (Time.time - lastGrindEndTime):F3}s)");
            return;
        }
        if (fpsController == null || characterController == null)
        {
            if (showDebugInfo) Debug.Log("🔍 StartGrind() abgewiesen: fpsController oder characterController null");
            return;
        }

        if (showDebugInfo) Debug.Log($"🔍 StartGrind() LÄUFT DURCH bei t={Mathf.Clamp01(startT):F2}, canMove vorher={fpsController.canMove}, controller.enabled vorher={characterController.enabled}");

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

        // CalculateLength() auf dem SplineContainer liefert die WELTLÄNGE (berück-
        // sichtigt transform.lossyScale) - praktisch für grindSpeed in m/s und die
        // reachedEnd-Prüfung unten. GetPointAtLinearDistance() arbeitet aber direkt
        // auf dem rohen Spline-Objekt OHNE Skalierung. Bei einem skalierten Rail
        // (z.B. Scale 50,50,50) klaffen beide Längen massiv auseinander - wurde
        // vorher nicht unterschieden, wodurch traveledDistance (in Weltmetern)
        // sofort weit über die lokale Spline-Länge hinausschoss und t auf 1
        // sprang. localSplineLength ist der Korrekturfaktor dafür.
        localSplineLength = currentSpline.Splines[splineIndex].GetLength();
        if (localSplineLength < 0.0001f) localSplineLength = 0.0001f;

        // Bogenlänge an der aktuellen t-Position ermitteln, damit traveledDistance
        // konsistent zum übergebenen startT beginnt (nicht einfach bei 0) - sonst
        // würde beim Einstieg mitten auf der Bahn die distanzbasierte Bewegung
        // fälschlich wieder vom Bahnanfang aus rechnen.
        traveledDistance = currentT * splineLength;

        // Initiale Tangente für den Smoothing-Filter direkt auf den echten Wert
        // setzen statt auf Vector3.forward zu starten - sonst würde der Filter im
        // ersten Augenblick des Grinds erst "einschwingen" und kurz falsch zeigen.
        Vector3 initialTangent = (Vector3)spline.EvaluateTangent(splineIndex, currentT);
        if (!fromStart) initialTangent = -initialTangent;
        if (initialTangent.sqrMagnitude > 0.0001f)
            smoothedTangent = initialTangent.normalized;
        cameraYawRotation = transform.rotation;

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
        fixedUpdateTimer = 0f;

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
        fixedUpdateTimer = 0f;
    }

    /// <summary>
    /// Interpoliert transform.position zwischen der vorherigen und aktuellen
    /// FixedUpdate-Zielposition, basierend darauf, wie weit wir zeitlich zwischen
    /// zwei Fixed-Ticks stehen. Das verhindert das sichtbare "Springen" der
    /// Position alle Time.fixedDeltaTime Sekunden, das bei hohem Tempo (20-45 m/s)
    /// als Ruckeln auffällt - Standard-Fix für Bewegung, die in FixedUpdate
    /// berechnet, aber smooth dargestellt werden soll.
    ///
    /// Verwendet einen SELBST geführten Timer (fixedUpdateTimer) statt
    /// (Time.time - Time.fixedTime) / Time.fixedDeltaTime - letztere Formel war
    /// der eigentliche Grund für das beobachtete Positions-Hoppeln: Time.fixedTime
    /// liefert in modernen Unity-Versionen keinen verlässlichen "Zeit seit letztem
    /// abgeschlossenen Tick"-Wert (an interne Physik-Subschritte gekoppelt), bei
    /// schwankender Framerate sprang der daraus berechnete Bruchteil unsauber.
    /// </summary>
    void InterpolatePosition()
    {
        fixedUpdateTimer += Time.deltaTime;
        float interpolationFactor = Mathf.Clamp01(fixedUpdateTimer / Time.fixedDeltaTime);
        transform.position = Vector3.Lerp(previousFixedPosition, targetFixedPosition, interpolationFactor);
    }

    void HandleCameraPitchInput()
    {
        if (playerCamera == null) return;

        cameraPitchDuringGrind += -Input.GetAxis("Mouse Y") * lookSpeed;
        cameraPitchDuringGrind = Mathf.Clamp(cameraPitchDuringGrind, -lookXLimit, lookXLimit);

        // Yaw-Differenz zwischen dem (weicher geglätteten) cameraYawRotation-Ziel
        // und dem tatsächlichen Body-Yaw (transform.rotation, das per yawAlignSpeed
        // in FixedUpdate gedreht wird) als zusätzlichen lokalen Yaw-Offset auf die
        // Kamera legen. Der Body dreht sich also etwas "direkter" der Bahn nach
        // (wichtig für die Bewegungsrichtung), die Kamera bekommt zusätzlich noch
        // einen eigenen, weicheren Nachlauf-Anteil - das ist der Kern des Fixes
        // gegen das Mitnehmen jeder kleinen Tangenten-Unebenheit in Kurven.
        float yawOffset = Quaternion.Angle(transform.rotation, cameraYawRotation);
        Vector3 cross = Vector3.Cross(transform.forward, cameraYawRotation * Vector3.forward);
        float signedYawOffset = (cross.y < 0f) ? -yawOffset : yawOffset;

        playerCamera.transform.localRotation = Quaternion.Euler(cameraPitchDuringGrind, signedYawOffset, 0f);
    }

    void FixedUpdate()
    {
        if (!isGrinding) return;
        if (isSnapping) return; // Snap-Phase läuft komplett in Update(), siehe UpdateMagnetSnap()

        // Timer für die Update()-Interpolation zurücksetzen - JETZT beginnt ein
        // neuer Fixed-Tick, also ist die "Zeit seit letztem Tick" wieder 0.
        fixedUpdateTimer = 0f;

        // Vorherige Zielposition merken, BEVOR sich traveledDistance weiterbewegt -
        // das ist der Ausgangspunkt für die Interpolation in Update() bis zum
        // nächsten Tick.
        previousFixedPosition = targetFixedPosition;

        // Fortschritt jetzt in METERN statt in t - deltaDistance ist die tatsächlich
        // zurückgelegte Bogenlänge in diesem Tick, unabhängig davon, wie eng die
        // Spline an dieser Stelle gekrümmt ist. Das ist der Hauptfix gegen das
        // Ruckeln in Kurven: vorher bewegte sich der Spieler bei festem deltaT in
        // engen Kurven effektiv LANGSAMER (t deckt dort weniger Meter ab), was als
        // ungleichmäßiges Stottern wahrgenommen wurde.
        float deltaDistance = grindSpeed * Time.fixedDeltaTime;
        traveledDistance += forward ? deltaDistance : -deltaDistance;
        traveledDistance = Mathf.Clamp(traveledDistance, 0f, splineLength);

        bool reachedEnd = forward ? (traveledDistance >= splineLength) : (traveledDistance <= 0f);

        // GetPointAtLinearDistance übersetzt Bogenlänge -> t für uns (intern per
        // Bisektion/Lookup-Table je nach Spline-Implementierung) und erwartet die
        // Distanz in LOKALEN (unskalierten) Spline-Einheiten. traveledDistance ist
        // aber in Weltmetern (da grindSpeed eine Weltgeschwindigkeit ist) - bei
        // einem skalierten Rail-GameObject (z.B. Scale 50,50,50) muss daher erst
        // auf lokale Distanz umgerechnet werden, sonst überschreitet der Wert die
        // lokale Spline-Länge sofort massiv und t springt auf 1 (= Spieler "tele-
        // portiert" quasi ans Bahnende, exakt das beobachtete Symptom).
        float localTraveledDistance = traveledDistance * (localSplineLength / splineLength);
        SplineUtility.GetPointAtLinearDistance(currentSpline.Splines[currentSplineIndex], 0f, localTraveledDistance, out float resultT);
        currentT = resultT;
        float clampedT = Mathf.Clamp01(currentT);

        // Position auf der Spline (Weltkoordinaten, +Offset für sichtbares "Aufsitzen")
        Vector3 newTargetPos = (Vector3)currentSpline.EvaluatePosition(currentSplineIndex, clampedT);
        newTargetPos.y += verticalOffset;
        targetFixedPosition = newTargetPos;

        // Rohe Tangente holen und richtungsabhängig ausrichten
        Vector3 rawTangent = (Vector3)currentSpline.EvaluateTangent(currentSplineIndex, clampedT);
        if (!forward) rawTangent = -rawTangent;
        Vector3 flatRawTangent = new Vector3(rawTangent.x, 0f, rawTangent.z);

        if (flatRawTangent.sqrMagnitude > 0.0001f)
        {
            flatRawTangent.Normalize();

            // EMA-Glättung der Tangente: dämpft Sample-zu-Sample-"Zittern" der
            // Rohtangente (z.B. durch eng beieinanderliegende Spline-Knots), bevor
            // sie für Körper- UND Kamera-Rotation verwendet wird. tangentSmoothSpeed
            // = 0 deaktiviert den Filter (rohe Tangente wird direkt durchgereicht).
            if (tangentSmoothSpeed > 0f)
            {
                float smoothFactor = 1f - Mathf.Exp(-tangentSmoothSpeed * Time.fixedDeltaTime);
                smoothedTangent = Vector3.Slerp(smoothedTangent, flatRawTangent, smoothFactor);
            }
            else
            {
                smoothedTangent = flatRawTangent;
            }
        }

        // Player-Körper-Yaw zur (geglätteten) Tangente drehen (sanft per Slerp,
        // damit Kurven smooth wirken statt hart zu snappen). Pitch/Roll des Körpers
        // bleiben bei 0 - es soll sich nicht der ganze Spieler vornüberkippen, nur
        // der Yaw folgt der Bahn, der Spieler bleibt aufrecht stehen.
        if (smoothedTangent.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(smoothedTangent, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, yawAlignSpeed * Time.fixedDeltaTime);

            // Kamera-Yaw separat und weicher nachführen (cameraYawSmoothSpeed statt
            // yawAlignSpeed) - das ist der zweite Teil des Fixes: selbst die bereits
            // geglättete Körper-Rotation kann in engen Kurven noch zu direkt für die
            // Kamera sein, da der Körper-Yaw primär für GAMEPLAY (Bewegungsrichtung)
            // gedacht ist, die Kamera aber das ist, was der Spieler tatsächlich SIEHT.
            cameraYawRotation = Quaternion.Slerp(cameraYawRotation, targetRot, cameraYawSmoothSpeed * Time.fixedDeltaTime);
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
        // WICHTIG: Der frühere Guard "if (!isGrinding) return;" ganz am Anfang
        // konnte das Cleanup unten (characterController.enabled = true, canMove =
        // true) KOMPLETT verhindern, wenn EndGrind() in einem Zustand aufgerufen
        // wurde, in dem isGrinding bereits (z.B. durch eine Race zwischen zwei
        // StartGrind()-Aufrufen kurz nacheinander auf derselben fast-geraden
        // Strecke) auf false stand - der Spieler blieb dann mit deaktiviertem
        // CharacterController dauerhaft hängen, KOMPLETT ohne Exception oder Log,
        // da der alte Guard die Methode sofort verlassen hat, bevor sie überhaupt
        // etwas tun konnte. Jetzt entscheidet wasActuallyGrinding nur noch, OB die
        // Exit-Geschwindigkeit/-Richtung berechnet wird (das ergibt nur bei einem
        // ECHTEN aktiven Grind Sinn) - das Cleanup im finally-Block darunter läuft
        // davon komplett unabhängig und IMMER, egal was oben passiert.
        bool wasActuallyGrinding = isGrinding;

        try
        {
            if (wasActuallyGrinding)
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
            else if (showDebugInfo)
            {
                Debug.Log("🔍 EndGrind() aufgerufen, obwohl isGrinding bereits false war - Cleanup läuft trotzdem (Sicherheitsnetz gegen Stuck-Zustand)");
            }
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
            // eine Exception geworfen hat ODER wasActuallyGrinding false war -
            // das ist der eigentliche Fix gegen den dauerhaften "Spieler hängt
            // bewegungslos fest"-Zustand auf fast geraden Bahnstrecken.
            isGrinding = false;
            lastGrindEndTime = Time.time;

            // Kamera-Yaw-Offset auf 0 zurücksetzen, BEVOR SC_FPSController seine
            // eigene Look-Logik wieder übernimmt (canMove = true unten) - sonst
            // könnte für einen Frame noch der zuletzt geglättete Yaw-Offset aus
            // der Kurve auf der Kamera stehen, bis SC_FPSController sie selbst
            // neu setzt.
            if (playerCamera != null)
                playerCamera.transform.localRotation = Quaternion.Euler(cameraPitchDuringGrind, 0f, 0f);

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
