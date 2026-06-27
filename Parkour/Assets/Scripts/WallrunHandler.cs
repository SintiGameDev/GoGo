using UnityEngine;

/// <summary>
/// Automatischer Wallrun (Mirror's-Edge-Stil): läuft beim seitlichen Anlaufen
/// einer Wand mit genug Tempo automatisch an, KEIN Tastendruck nötig. Während
/// des Wallruns hält die Schwerkraft stark reduziert (Spieler "läuft" an der
/// Wand statt zu fallen), bis Zeit/Tempo ausläuft oder der Spieler abspringt.
///
/// Ist ein VORZUSTAND für Walljump: WalljumpHandler prüft selbst, ob ein
/// Wallrun aktiv ist (siehe WalljumpHandler.TryWalljump - dort wird sowohl der
/// freie Fall an einer Wand als auch ein aktiver Wallrun als Sprungbasis
/// akzeptiert). Dieses Skript selbst reagiert NICHT auf die Action-Taste -
/// das bleibt zentral beim PlayerActionResolver/WalljumpHandler.
/// </summary>
public class WallrunHandler : MonoBehaviour
{
    [Header("References")]
    public PlayerMotor motor;
    public PlayerLook look;
    public WallDetector wallDetector;
    public GroundMovement groundMovement;

    [Header("Start-Bedingungen")]
    [Tooltip("Mindest-horizontales Tempo, um einen Wallrun zu starten")]
    public float minSpeedToStartWallrun = 5.0f;

    [Tooltip("Wie 'seitlich' der Anlauf zur Wand sein muss, um zu starten (0 = nur exakt parallel, 1 = auch fast frontal). Vergleichswert ist |dot(bewegungsrichtung, wandnormale)| - niedriger Wert = nur bei wirklich seitlichem Anlauf.")]
    [Range(0f, 1f)]
    public float maxFrontalityToStart = 0.5f;

    [Tooltip("Mindestzeit in der Luft, bevor ein Wallrun starten kann (verhindert Wallrun direkt vom Boden aus an einer angrenzenden Wand)")]
    public float minAirTimeToStart = 0.05f;

    [Header("Wallrun-Verhalten")]
    [Tooltip("Maximale Dauer eines einzelnen Wallruns, bevor der Spieler automatisch abfällt")]
    public float maxWallrunDuration = 1.4f;

    [Tooltip("Schwerkraft-Multiplikator während des Wallruns (klein = kaum Fallen, simuliert das Wand-Laufen)")]
    public float wallrunGravityMultiplier = 0.15f;

    [Tooltip("Wie stark das Tempo während des Wallruns über die Zeit abklingt (0 = kein Abklingen, 1 = volles Abklingen auf 0 am Ende)")]
    public float wallrunSpeedDecay = 0.3f;

    [Tooltip("Mindesttempo, unter dem der Wallrun automatisch abbricht (Spieler ist zu langsam geworden)")]
    public float minSpeedToContinueWallrun = 2.0f;

    [Header("Cooldown")]
    [Tooltip("Wie lange nach Ende eines Wallruns an DERSELBEN Wand kein neuer Wallrun startet (verhindert Mini-Wallrun-Spam beim Abprallen)")]
    public float wallrunCooldown = 0.3f;

    [Header("Debug")]
    public bool showDebugInfo = true;

    private bool isWallrunning = false;
    private float wallrunTimer = 0f;
    private float wallrunStartSpeed = 0f;
    private Vector3 wallrunDirection = Vector3.forward;
    private Vector3 currentWallNormal = Vector3.zero;
    private float lastWallrunEndTime = -999f;

    void Awake()
    {
        if (motor == null) motor = GetComponent<PlayerMotor>();
        if (look == null) look = GetComponent<PlayerLook>();
        if (wallDetector == null) wallDetector = GetComponent<WallDetector>();
        if (groundMovement == null) groundMovement = GetComponent<GroundMovement>();
    }

    void Update()
    {
        if (isWallrunning)
        {
            UpdateWallrun();
        }
        else
        {
            TryStartWallrun();
        }
    }

    void TryStartWallrun()
    {
        if (motor.IsGrounded())
            return;

        if (motor.GetTimeSinceUngrounded() < minAirTimeToStart)
            return;

        if (Time.time - lastWallrunEndTime < wallrunCooldown)
            return;

        float currentSpeed = motor.GetHorizontalSpeed();
        if (currentSpeed < minSpeedToStartWallrun)
            return;

        if (!wallDetector.TryGetWallAheadWide(out RaycastHit hit, halfAngle: 60f))
            return;

        Vector3 horizontalVel = motor.GetHorizontalVelocity();
        Vector3 moveDir = horizontalVel.normalized;
        Vector3 flatNormal = hit.normal;
        flatNormal.y = 0f;
        flatNormal.Normalize();

        // Seitlichkeit prüfen: bei echtem seitlichem Anlauf ist die Bewegung
        // fast senkrecht zur Wandnormale (dot nahe 0). Ein frontaler Anlauf
        // (dot nahe ±1) soll NICHT automatisch in einen Wallrun münden, sondern
        // weiterhin dem direkten Walljump-Absprung vorbehalten bleiben.
        float frontality = Mathf.Abs(Vector3.Dot(moveDir, flatNormal));
        if (frontality > maxFrontalityToStart)
            return;

        StartWallrun(flatNormal, moveDir, currentSpeed);
    }

    void StartWallrun(Vector3 wallNormal, Vector3 moveDir, float startSpeed)
    {
        isWallrunning = true;
        wallrunTimer = 0f;
        wallrunStartSpeed = startSpeed;
        currentWallNormal = wallNormal;

        // Bewegungsrichtung auf die Wandebene projizieren, damit der Spieler
        // exakt PARALLEL zur Wand läuft statt langsam in sie hinein/raus zu drehen
        wallrunDirection = Vector3.ProjectOnPlane(moveDir, wallNormal).normalized;

        if (groundMovement != null)
            groundMovement.movementBlocked = true;

        if (showDebugInfo)
            Debug.Log($"🏃 Wallrun gestartet! Richtung: {wallrunDirection} | Start-Speed: {startSpeed:F1} m/s");
    }

    void UpdateWallrun()
    {
        wallrunTimer += Time.deltaTime;

        // Wand muss während des Laufs weiterhin vorhanden sein, sonst Ende
        // (Spieler ist am Ende der Wand angekommen oder die Wand macht einen Knick)
        bool stillHasWall = wallDetector.TryGetWallAheadWide(out RaycastHit hit, halfAngle: 70f);

        float t = Mathf.Clamp01(wallrunTimer / maxWallrunDuration);
        float currentSpeed = Mathf.Lerp(wallrunStartSpeed, wallrunStartSpeed * (1f - wallrunSpeedDecay), t);

        motor.SetHorizontalVelocity(wallrunDirection * currentSpeed);

        // Schwerkraft stark reduziert anwenden (motor.ApplyGravity läuft normal
        // weiter, hier wird nur die Y-Komponente nachträglich gedämpft, damit
        // PlayerMotor selbst nicht für den Wallrun-Sonderfall angepasst werden muss)
        Vector3 vel = motor.GetVelocity();
        vel.y *= wallrunGravityMultiplier;
        // Geringe konstante Steighilfe, damit der Spieler nicht "klebt" sondern
        // sich wie im Vorbild minimal an der Wand nach oben zieht, bevor er fällt
        motor.SetVelocity(new Vector3(vel.x, vel.y, vel.z));

        bool timeUp = wallrunTimer >= maxWallrunDuration;
        bool tooSlow = currentSpeed < minSpeedToContinueWallrun;
        bool grounded = motor.IsGrounded();

        if (!stillHasWall || timeUp || tooSlow || grounded)
        {
            EndWallrun();
        }

        if (showDebugInfo && Time.frameCount % 30 == 0)
        {
            Debug.Log($"🏃 Wallrun läuft... Speed: {currentSpeed:F1} m/s | Zeit: {wallrunTimer:F2}/{maxWallrunDuration:F2}s");
        }
    }

    void EndWallrun()
    {
        isWallrunning = false;
        lastWallrunEndTime = Time.time;

        if (groundMovement != null)
            groundMovement.movementBlocked = false;

        if (showDebugInfo)
            Debug.Log("🏃 Wallrun beendet");
    }

    /// <summary>
    /// Wird von WalljumpHandler aufgerufen, wenn der Spieler während eines aktiven
    /// Wallruns die Action-Taste drückt - beendet den Wallrun sofort, damit der
    /// anschließende Walljump-Impuls nicht durch die Wallrun-Logik überschrieben wird.
    /// </summary>
    public void InterruptForWalljump()
    {
        if (isWallrunning)
        {
            EndWallrun();
        }
    }

    public bool IsWallrunning() => isWallrunning;
    public Vector3 GetCurrentWallNormal() => currentWallNormal;
}
