using UnityEngine;

/// <summary>
/// Dash/Slide-Mechanik: Doppel-Tap auf eine Bewegungsrichtung (W/A/S/D, relativ
/// zur Blickrichtung) löst einen kurzen Geschwindigkeits-Peak in diese Richtung
/// aus, der über dashDuration abklingt. Wirkt UNABHÄNGIG davon, ob die Taste
/// nach dem zweiten Tap weiter gehalten wird.
///
/// FUNKTIONIERT OHNE JEGLICHE ÄNDERUNG AN SC_FPSController.cs.
///
/// WARUM ÜBER moveDirection-Manipulation (GetMoveDirection/ModifyMoveDirection)
/// NICHT ZUVERLÄSSIG GEHT: SC_FPSController.Update() setzt moveDirection mehrfach
/// INNERHALB seiner eigenen Methode neu (einmal komplett aus Input, danach nur
/// noch die Y-Komponente für Sprung/Schwerkraft) und ruft erst GANZ AM ENDE
/// characterController.Move(moveDirection * Time.deltaTime) auf - alles ohne
/// Zwischenaufruf einer anderen Methode. Ein separates Skript kann sich von
/// außen nicht zwischen "letztes Setzen von moveDirection" und "Move()-Aufruf"
/// schalten, egal in welcher Script-Execution-Order es läuft: läuft es VOR
/// SC_FPSController, überschreibt dessen eigene Input-Berechnung die hier
/// geschriebenen Werte sofort wieder; läuft es NACH SC_FPSController, kommt der
/// Wert erst im nächsten Frame zur Wirkung (ein Frame zu spät).
///
/// LÖSUNG: Dieses Skript bewegt den CharacterController mit einem EIGENEN,
/// ZUSÄTZLICHEN characterController.Move()-Aufruf für die Dash-Komponente -
/// komplett entkoppelt von SC_FPSControllers eigenem Move()-Aufruf für die
/// normale Bewegung. CharacterController.Move() kann mehrfach pro Frame
/// aufgerufen werden; die Effekte auf die Position summieren sich, Kollisionen
/// werden bei jedem Aufruf separat (und korrekt) aufgelöst.
///
/// Auf dasselbe GameObject wie SC_FPSController packen.
/// </summary>
[RequireComponent(typeof(SC_FPSController))]
[RequireComponent(typeof(CharacterController))]
public class DashController : MonoBehaviour
{
    [Header("Dash-Geschwindigkeit")]
    [Tooltip("Geschwindigkeit (m/s) direkt beim Dash-Start (Peak)")]
    public float dashPeakSpeed = 18f;

    [Tooltip("Wie lange der Dash insgesamt dauert, bis die Zusatz-Velocity auf 0 abgeklungen ist")]
    public float dashDuration = 0.35f;

    [Tooltip("Abkling-Kurve über die Dash-Dauer (0=Start/Peak, 1=Ende). Default: schnell am Anfang, sanft auslaufend.")]
    public AnimationCurve dashFalloffCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("Doppel-Tap Erkennung")]
    [Tooltip("Maximales Zeitfenster zwischen zwei Tastendrücken derselben Richtung, damit es als Doppel-Tap zählt")]
    public float doubleTapWindow = 0.3f;

    [Header("Cooldown")]
    [Tooltip("Sperre nach jedem Dash, bevor der nächste ausgelöst werden kann (gilt für ALLE Richtungen gemeinsam, nicht pro Taste einzeln)")]
    public float dashCooldown = 1.2f;

    [Header("Debug")]
    public bool showDebugInfo = false;

    private CharacterController characterController;

    // Letzter Tastendruck-Zeitpunkt pro Richtung, für die Doppel-Tap-Erkennung.
    // Vier separate Timer, da z.B. W-W und A-A unabhängig voneinander als
    // Doppel-Tap zählen sollen, nicht W-A o.ä.
    private float lastTapTimeForward = -999f;
    private float lastTapTimeBack = -999f;
    private float lastTapTimeLeft = -999f;
    private float lastTapTimeRight = -999f;

    private float lastDashEndTime = -999f;

    private bool isDashing = false;
    private float dashStartTime = -999f;
    private Vector3 dashDirection = Vector3.zero; // World-Space, gesetzt bei Dash-Start

    void Awake()
    {
        characterController = GetComponent<CharacterController>();
    }

    void Update()
    {
        DetectDoubleTapInput();
        ApplyDashMovement();
    }

    void DetectDoubleTapInput()
    {
        // KeyDown statt GetAxis, da wir den exakten MOMENT eines neuen Tastendrucks
        // brauchen, um ihn gegen den letzten Tastendruck derselben Richtung zu prüfen -
        // GetAxis liefert einen kontinuierlichen Wert, keinen einzelnen Druck-Zeitpunkt.
        if (Input.GetKeyDown(KeyCode.W))
            CheckDoubleTap(ref lastTapTimeForward, transform.forward);

        if (Input.GetKeyDown(KeyCode.S))
            CheckDoubleTap(ref lastTapTimeBack, -transform.forward);

        if (Input.GetKeyDown(KeyCode.A))
            CheckDoubleTap(ref lastTapTimeLeft, -transform.right);

        if (Input.GetKeyDown(KeyCode.D))
            CheckDoubleTap(ref lastTapTimeRight, transform.right);
    }

    void CheckDoubleTap(ref float lastTapTime, Vector3 worldDirection)
    {
        float now = Time.time;
        float timeSinceLastTap = now - lastTapTime;

        if (timeSinceLastTap <= doubleTapWindow)
        {
            TryStartDash(worldDirection);
            // Timer zurücksetzen, damit ein dritter schneller Tap nicht sofort
            // wieder als Doppel-Tap zählt (erst wieder nach einer neuen Pause)
            lastTapTime = -999f;
        }
        else
        {
            lastTapTime = now;
        }
    }

    void TryStartDash(Vector3 worldDirection)
    {
        if (isDashing)
            return;

        // Während der Controller von außen deaktiviert ist (z.B. während eines
        // Spline-Grinds über SplineGrindHandler), soll erst gar kein neuer Dash
        // starten - der Spieler hat in dem Zustand sowieso keine reguläre
        // Bewegungskontrolle (vgl. SC_FPSController.canMove), ein im Hintergrund
        // "unsichtbar" laufender und abklingender Dash wäre nur verwirrend.
        if (!characterController.enabled)
            return;

        if (Time.time - lastDashEndTime < dashCooldown)
        {
            if (showDebugInfo)
                Debug.Log($"⏳ Dash noch im Cooldown ({dashCooldown - (Time.time - lastDashEndTime):F2}s verbleibend)");
            return;
        }

        Vector3 flatDirection = new Vector3(worldDirection.x, 0f, worldDirection.z);
        if (flatDirection.sqrMagnitude < 0.0001f)
            return;

        isDashing = true;
        dashStartTime = Time.time;
        dashDirection = flatDirection.normalized;

        if (showDebugInfo)
            Debug.Log($"💨 Dash gestartet! Richtung: {dashDirection} | Peak-Speed: {dashPeakSpeed:F1} m/s");
    }

    /// <summary>
    /// Berechnet die aktuelle Dash-Zusatz-Velocity (abklingender Peak) und
    /// bewegt den CharacterController damit über einen EIGENEN, zusätzlichen
    /// Move()-Aufruf - komplett unabhängig von SC_FPSControllers eigenem
    /// Move()-Aufruf für die normale Bewegung (siehe Klassen-Kommentar oben,
    /// warum eine moveDirection-Manipulation hier nicht zuverlässig funktioniert).
    /// Egal in welcher Reihenfolge diese Methode relativ zu SC_FPSController.Update()
    /// läuft - der Effekt auf die Position ist in jedem Fall noch in diesem Frame
    /// sichtbar, da es ein komplett separater Move()-Aufruf ist.
    /// </summary>
    void ApplyDashMovement()
    {
        if (!isDashing)
            return;

        // Guard: characterController.enabled kann von außen deaktiviert sein (z.B.
        // von SplineGrindHandler während eines Grinds, das den Controller EINMAL
        // deaktiviert statt pro Frame zu togglen - siehe dort). Move() auf einem
        // deaktivierten CharacterController wirft eine Warnung und hat keinen
        // Effekt. Den laufenden Dash NICHT abbrechen (isDashing bleibt true,
        // dashStartTime bleibt unverändert) - er pausiert einfach für die Dauer
        // der Deaktivierung und macht beim nächsten aktiven Frame an der Stelle
        // weiter, an der die verstrichene Zeit (elapsed) bereits steht. Das ist
        // ein bewusster Kompromiss: ein Dash, der zufällig auf einen Grind-Einstieg
        // trifft, klingt dadurch ggf. etwas schneller ab (da Time.time weiterläuft,
        // auch wenn Move() nicht greift), ist aber immer noch korrekter als ein
        // Move()-Aufruf auf inaktivem Controller oder ein hartes Abschneiden mitten
        // im Dash.
        if (!characterController.enabled)
            return;

        float elapsed = Time.time - dashStartTime;

        if (elapsed >= dashDuration)
        {
            isDashing = false;
            lastDashEndTime = Time.time;

            if (showDebugInfo)
                Debug.Log("💨 Dash beendet");

            return;
        }

        float progress = elapsed / dashDuration;
        float falloff = dashFalloffCurve.Evaluate(progress);
        Vector3 dashVelocity = dashDirection * dashPeakSpeed * falloff;

        // Rein horizontaler Zusatz-Move - KEINE Y-Komponente, damit Sprung/Fall/
        // Schwerkraft (von SC_FPSController.Update() im selben Frame separat
        // verarbeitet) unangetastet bleiben. Zwei unabhängige Move()-Aufrufe auf
        // demselben CharacterController in einem Frame summieren sich korrekt
        // in ihrer Wirkung auf die Position, inklusive jeweils eigener
        // Kollisionsauflösung.
        characterController.Move(dashVelocity * Time.deltaTime);
    }

    public bool IsDashing() => isDashing;
    public float GetCooldownRemaining() => Mathf.Max(0f, dashCooldown - (Time.time - lastDashEndTime));
}
