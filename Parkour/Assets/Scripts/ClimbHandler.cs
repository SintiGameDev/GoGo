using System.Collections;
using UnityEngine;

/// <summary>
/// Climb für Kanten, die zu hoch zum Vaulten sind (oberhalb maxVaultHeight,
/// bis zu maxClimbHeight). Höchste Priorität im PlayerActionResolver, da eine
/// Kante in dieser Höhe nicht gleichzeitig als Vault-Hindernis erkannt wird
/// (Vaults oberer Check-Ray würde dort treffen und den Vault selbst ablehnen) -
/// die beiden Bereiche sind dadurch bereits durch die Höhenwerte getrennt,
/// die Priorität ist eher fürs Verhalten an der Übergangszone relevant.
/// Erkennung: ein Ray prüft auf eine Wand/Kante vor dem Spieler im Climb-
/// Höhenbereich, ein zweiter Ray (von oberhalb nach unten) prüft, ob darüber
/// eine begehbare Fläche existiert (sonst wäre es nur eine hohe Wand ohne
/// erreichbare Kante obendrauf, z.B. eine durchgehende hohe Mauer).
/// </summary>
public class ClimbHandler : MonoBehaviour, IActionHandler
{
    [Header("References")]
    public PlayerMotor motor;
    public PlayerLook look;
    public PlayerMomentum momentum;
    public GroundMovement groundMovement;

    [Header("Erkennung")]
    public LayerMask climbLayerMask = ~0;
    public float climbCheckDistance = 0.8f;

    [Tooltip("Untere Grenze des Climb-Bereichs - sollte zu VaultHandler.maxVaultHeight passen, damit es keine Lücke/Überlappung gibt")]
    public float minClimbHeight = 1.2f;

    [Tooltip("Obere Grenze: Kanten höher als das können nicht erklettert werden")]
    public float maxClimbHeight = 2.2f;

    [Tooltip("Wie weit über der erkannten Kante geprüft wird, ob dort eine begehbare Fläche existiert")]
    public float ledgeFlatnessCheckHeight = 0.3f;

    [Header("Bewegung")]
    public float climbDuration = 0.5f;

    [Tooltip("Wie viel horizontale Geschwindigkeit nach Abschluss des Climbs erhalten bleibt")]
    public float exitSpeedMultiplier = 0.6f;

    [Header("Momentum")]
    [Tooltip("Climb ist eine Kontroll-Aktion, kein Flow-Booster wie Vault/Walljump - daher standardmäßig 0")]
    public float momentumGainPerClimb = 0f;

    [Header("Debug")]
    public bool showDebugInfo = true;
    public bool showDebugRays = true;

    private bool isClimbing = false;

    void Awake()
    {
        if (motor == null) motor = GetComponent<PlayerMotor>();
        if (look == null) look = GetComponent<PlayerLook>();
        if (momentum == null) momentum = GetComponent<PlayerMomentum>();
        if (groundMovement == null) groundMovement = GetComponent<GroundMovement>();
    }

    public bool TryPerform()
    {
        if (isClimbing)
            return false;

        if (!TryDetectClimbableLedge(out Vector3 ledgeTopPoint))
            return false;

        StartCoroutine(PerformClimb(ledgeTopPoint));
        return true;
    }

    bool TryDetectClimbableLedge(out Vector3 ledgeTopPoint)
    {
        ledgeTopPoint = Vector3.zero;

        Vector3 forward = look != null ? look.GetFlatForward() : transform.forward;
        Vector3 basePos = transform.position;

        // Wand/Kante im Climb-Höhenbereich suchen (mittig zwischen min/max, damit
        // sowohl niedrige als auch hohe Climb-Kanten zuverlässig getroffen werden)
        float probeHeight = (minClimbHeight + maxClimbHeight) * 0.5f;
        Vector3 wallProbeOrigin = basePos + Vector3.up * probeHeight;

        bool wallHit = Physics.Raycast(wallProbeOrigin, forward, out RaycastHit wallHitInfo, climbCheckDistance, climbLayerMask);

        if (showDebugRays)
            Debug.DrawRay(wallProbeOrigin, forward * climbCheckDistance, wallHit ? Color.magenta : Color.gray);

        if (!wallHit)
            return false;

        // Tatsächliche Oberkante per Vertikal-Raycast von oberhalb maxClimbHeight
        // nach unten finden (genau wie bei Vault, aber im höheren Bereich)
        Vector3 topProbeOrigin = wallHitInfo.point + forward * 0.15f + Vector3.up * maxClimbHeight;
        bool topHit = Physics.Raycast(topProbeOrigin, Vector3.down, out RaycastHit topHitInfo,
            maxClimbHeight - minClimbHeight + 0.5f, climbLayerMask);

        if (showDebugRays)
            Debug.DrawRay(topProbeOrigin, Vector3.down * (maxClimbHeight - minClimbHeight + 0.5f), topHit ? Color.green : Color.red);

        if (!topHit)
            return false;

        float ledgeHeightAboveFeet = topHitInfo.point.y - basePos.y;
        if (ledgeHeightAboveFeet < minClimbHeight || ledgeHeightAboveFeet > maxClimbHeight)
            return false;

        // Prüfen, ob über der Kante tatsächlich begehbare Fläche ist (kein
        // weiteres Hindernis direkt darüber, sonst bliebe der Spieler stecken)
        Vector3 clearanceCheckOrigin = topHitInfo.point + Vector3.up * ledgeFlatnessCheckHeight;
        bool blocked = Physics.Raycast(clearanceCheckOrigin, Vector3.up, 1.5f, climbLayerMask);

        if (blocked)
            return false;

        ledgeTopPoint = topHitInfo.point + forward * 0.25f; // etwas auf die Fläche hineinsetzen, nicht exakt auf die Kante
        return true;
    }

    IEnumerator PerformClimb(Vector3 ledgeTopPoint)
    {
        isClimbing = true;

        if (groundMovement != null)
            groundMovement.movementBlocked = true;

        Vector3 startPos = transform.position;

        // Zwischenpunkt: erst hoch auf Kantenhöhe (noch an der Wand), dann
        // horizontal auf die Fläche - vermeidet, dass der Spieler "durch" die
        // Wand zu fliegen scheint.
        Vector3 midPos = new Vector3(startPos.x, ledgeTopPoint.y, startPos.z);

        float elapsed = 0f;
        float phase1Duration = climbDuration * 0.55f;

        while (elapsed < phase1Duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / phase1Duration);
            motor.Teleport(Vector3.Lerp(startPos, midPos, EaseOutQuad(t)));
            motor.SetVelocity(Vector3.zero);
            yield return null;
        }

        elapsed = 0f;
        float phase2Duration = climbDuration * 0.45f;

        while (elapsed < phase2Duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / phase2Duration);
            motor.Teleport(Vector3.Lerp(midPos, ledgeTopPoint, EaseInQuad(t)));
            motor.SetVelocity(Vector3.zero);
            yield return null;
        }

        Vector3 forward = look != null ? look.GetFlatForward() : transform.forward;
        float exitSpeed = momentum.GetCurrentMoveSpeed() * exitSpeedMultiplier;
        motor.SetHorizontalVelocity(forward * exitSpeed);
        motor.SetVerticalVelocity(0f);

        if (momentum != null && momentumGainPerClimb > 0f)
            momentum.AddMomentum(momentumGainPerClimb);

        if (groundMovement != null)
            groundMovement.movementBlocked = false;

        isClimbing = false;

        if (showDebugInfo)
            Debug.Log("🧗 Climb abgeschlossen");
    }

    float EaseOutQuad(float t) => 1f - (1f - t) * (1f - t);
    float EaseInQuad(float t) => t * t;

    public bool IsClimbing() => isClimbing;
}
