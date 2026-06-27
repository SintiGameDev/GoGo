using UnityEngine;

/// <summary>
/// Walljump NICHT automatisch (anders als das alte System) - der Spieler muss
/// in der Luft, während er an einer Wand ist, die Action-Taste erneut drücken.
/// Die Sprungrichtung kommt von der BLICKRICHTUNG (POV), nicht von Reflexion:
/// der Spieler schaut dahin, wo er hinspringen will, drückt Leertaste, und
/// springt in genau diese Richtung ab - das ist der klassische Mirror's-Edge-
/// Stil (Spielerkontrolle über die Richtung, kein physikalischer Automatismus).
/// </summary>
public class WalljumpHandler : MonoBehaviour
{
    [Header("References")]
    public PlayerMotor motor;
    public PlayerInputContext input;
    public PlayerLook look;
    public WallDetector wallDetector;
    public PlayerMomentum momentum;

    [Tooltip("Optional - falls vorhanden, wird ein aktiver Wallrun beim Absprung korrekt unterbrochen, damit dessen Velocity-Override den Walljump-Impuls nicht im selben Frame überschreibt")]
    public WallrunHandler wallrunHandler;

    [Header("Walljump")]
    [Tooltip("Vertikale Sprunggeschwindigkeit des Walljumps")]
    public float wallJumpUpSpeed = 9.0f;

    [Tooltip("Horizontale Sprunggeschwindigkeit in Blickrichtung")]
    public float wallJumpForwardSpeed = 8.0f;

    [Tooltip("Wie lange nach einem Walljump kein erneuter Walljump ausgelöst werden kann (verhindert Spam/Chain an derselben Wand)")]
    public float wallJumpCooldown = 0.25f;

    [Tooltip("Mindestzeit in der Luft (seit letztem Bodenkontakt), bevor ein Walljump überhaupt möglich ist - verhindert, dass ein normaler Bodensprung direkt neben einer Wand als Walljump gewertet wird")]
    public float minAirTimeForWalljump = 0.08f;

    [Header("Momentum-Belohnung")]
    [Tooltip("Wie viel Momentum-Multiplikator ein erfolgreicher Walljump gibt (für Walljump-Chains/Flow)")]
    public float momentumGainPerWalljump = 0.15f;

    [Header("Kamera")]
    public bool rotateCameraToJumpDirection = true;

    [Header("Debug")]
    public bool showDebugInfo = true;

    private float lastWalljumpTime = -999f;

    void Awake()
    {
        if (motor == null) motor = GetComponent<PlayerMotor>();
        if (input == null) input = GetComponent<PlayerInputContext>();
        if (look == null) look = GetComponent<PlayerLook>();
        if (wallDetector == null) wallDetector = GetComponent<WallDetector>();
        if (momentum == null) momentum = GetComponent<PlayerMomentum>();
        if (wallrunHandler == null) wallrunHandler = GetComponent<WallrunHandler>();
    }

    /// <summary>
    /// Wird vom PlayerActionResolver aufgerufen, wenn die Action-Taste gedrückt
    /// wurde und kein höher priorisierter Kontext (Climb/Vault) greift. Gibt
    /// true zurück, wenn ein Walljump tatsächlich ausgeführt wurde, sonst false
    /// (der Resolver fällt dann auf den nächsten Kontext - normalen Jump - zurück).
    /// </summary>
    public bool TryWalljump()
    {
        if (motor.IsGrounded())
            return false; // Walljump nur in der Luft - am Boden übernimmt GroundMovement

        if (motor.GetTimeSinceUngrounded() < minAirTimeForWalljump)
            return false;

        if (Time.time - lastWalljumpTime < wallJumpCooldown)
            return false;

        // Während eines aktiven Wallruns blickt der Spieler oft seitlich an der
        // Wand vorbei statt direkt drauf - der normale Vorwärts-Raycast könnte
        // die Wand dann verfehlen. In diesem Fall reicht "Wallrun ist aktiv"
        // bereits als Beweis, dass eine Wand da ist, ohne erneut zu suchen.
        bool hasWall;
        if (wallrunHandler != null && wallrunHandler.IsWallrunning())
        {
            hasWall = true;
        }
        else
        {
            hasWall = wallDetector.TryGetWallAheadWide(out _);
        }

        if (!hasWall)
            return false;

        // Aktiven Wallrun zuerst beenden, BEVOR die Sprung-Velocity gesetzt
        // wird - sonst würde WallrunHandler.UpdateWallrun() im selben Frame
        // die hier gesetzte Geschwindigkeit sofort wieder überschreiben
        // (dasselbe Strukturproblem wie beim alten System, nur zwischen zwei
        // neuen Skripten statt einem).
        if (wallrunHandler != null)
            wallrunHandler.InterruptForWalljump();

        // Sprungrichtung = Blickrichtung (POV), NICHT die Wandnormale/Reflexion.
        // Der Spieler schaut dahin, wo er hinspringen will.
        Vector3 jumpDirection = look.GetFlatForward();

        Vector3 horizontalVelocity = jumpDirection * wallJumpForwardSpeed;
        motor.SetHorizontalVelocity(horizontalVelocity);
        motor.SetVerticalVelocity(wallJumpUpSpeed);

        lastWalljumpTime = Time.time;

        if (momentum != null)
            momentum.AddMomentum(momentumGainPerWalljump);

        if (rotateCameraToJumpDirection && look != null)
            look.StartAutoYaw(jumpDirection);

        if (showDebugInfo)
            Debug.Log($"🧱 Walljump! Richtung (Blick): {jumpDirection} | Horizontal: {wallJumpForwardSpeed:F1} m/s | Vertikal: {wallJumpUpSpeed:F1} m/s");

        return true;
    }

    /// <summary>
    /// Für den PlayerActionResolver: zeigt an, ob ein Walljump gerade grundsätzlich
    /// möglich wäre (ohne ihn auszuführen) - relevant für UI-Hinweise o.ä.
    /// </summary>
    public bool CanWalljump()
    {
        if (motor.IsGrounded())
            return false;

        if (motor.GetTimeSinceUngrounded() < minAirTimeForWalljump)
            return false;

        if (Time.time - lastWalljumpTime < wallJumpCooldown)
            return false;

        if (wallrunHandler != null && wallrunHandler.IsWallrunning())
            return true;

        return wallDetector.TryGetWallAheadWide(out _);
    }
}
