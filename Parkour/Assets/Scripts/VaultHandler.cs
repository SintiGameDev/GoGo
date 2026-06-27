using System.Collections;
using UnityEngine;

/// <summary>
/// Vault über niedrige Hindernisse, ausgelöst per Action-Taste (siehe
/// PlayerActionResolver - Priorität 2, nach Climb). Erkennung über zwei
/// Raycasts auf fester Höhe: ein niedriger Ray MUSS ein Hindernis treffen,
/// ein hoher Ray DARF NICHT treffen - das beweist, dass das Hindernis niedriger
/// als maxVaultHeight ist (anders als Climb, wo die Kante hoch genug sein muss).
/// Implementiert IActionHandler, damit der PlayerActionResolver es generisch
/// aufrufen kann.
/// </summary>
public class VaultHandler : MonoBehaviour, IActionHandler
{
    [Header("References")]
    public PlayerMotor motor;
    public PlayerLook look;
    public PlayerMomentum momentum;

    [Header("Erkennung")]
    public LayerMask vaultLayerMask = ~0;

    [Tooltip("Maximale Distanz, in der ein Hindernis vor dem Spieler erkannt wird")]
    public float vaultCheckDistance = 1.0f;

    [Tooltip("Hindernisse bis zu dieser Höhe (über dem Boden) gelten als 'niedrig genug' für Vault")]
    public float maxVaultHeight = 1.2f;

    [Tooltip("Mindesthöhe, damit nicht jede winzige Bordsteinkante einen Vault auslöst")]
    public float minVaultHeight = 0.35f;

    [Tooltip("Wie tief das Hindernis mindestens sein darf (Strecke, über die der Spieler während des Vaults hinwegfliegt)")]
    public float vaultClearDistance = 1.2f;

    [Header("Bewegung")]
    [Tooltip("Dauer der Vault-Bewegung in Sekunden")]
    public float vaultDuration = 0.35f;

    [Tooltip("Zusätzliche Höhe über der Hindernis-Oberkante, mit der der Spieler während des Vaults darüber hinwegfliegt")]
    public float vaultClearanceHeight = 0.3f;

    [Tooltip("Wie viel horizontale Geschwindigkeit nach Abschluss des Vaults erhalten bleibt (Rest wird auf die normale Bewegungseingabe zurückgesetzt)")]
    public float exitSpeedMultiplier = 1.0f;

    [Header("Momentum-Belohnung")]
    public float momentumGainPerVault = 0.1f;

    [Header("Referenz auf GroundMovement (wird während des Vaults blockiert)")]
    public GroundMovement groundMovement;

    [Header("Debug")]
    public bool showDebugInfo = true;
    public bool showDebugRays = true;

    private bool isVaulting = false;

    void Awake()
    {
        if (motor == null) motor = GetComponent<PlayerMotor>();
        if (look == null) look = GetComponent<PlayerLook>();
        if (momentum == null) momentum = GetComponent<PlayerMomentum>();
        if (groundMovement == null) groundMovement = GetComponent<GroundMovement>();
    }

    /// <summary>IActionHandler-Implementierung für den PlayerActionResolver.</summary>
    public bool TryPerform()
    {
        if (isVaulting)
            return false;

        if (!motor.IsGrounded())
            return false; // Vault wird nur vom Boden aus eingeleitet

        if (!TryDetectVaultableObstacle(out Vector3 nearEdgePoint, out Vector3 farLandingPoint))
            return false;

        StartCoroutine(PerformVault(nearEdgePoint, farLandingPoint));
        return true;
    }

    /// <summary>
    /// Erkennung: niedriger Ray muss treffen (Hindernis vorhanden), hoher Ray
    /// darf nicht treffen (Hindernis ist niedrig genug). Daraus wird die
    /// ungefähre Oberkante und ein Landepunkt hinter dem Hindernis ermittelt.
    /// </summary>
    bool TryDetectVaultableObstacle(out Vector3 nearEdgePoint, out Vector3 farLandingPoint)
    {
        nearEdgePoint = Vector3.zero;
        farLandingPoint = Vector3.zero;

        Vector3 forward = look != null ? look.GetFlatForward() : transform.forward;
        Vector3 basePos = transform.position;

        Vector3 lowOrigin = basePos + Vector3.up * minVaultHeight;
        Vector3 highOrigin = basePos + Vector3.up * maxVaultHeight;

        bool lowHit = Physics.Raycast(lowOrigin, forward, out RaycastHit lowHitInfo, vaultCheckDistance, vaultLayerMask);
        bool highHit = Physics.Raycast(highOrigin, forward, out RaycastHit highHitInfo, vaultCheckDistance, vaultLayerMask);

        if (showDebugRays)
        {
            Debug.DrawRay(lowOrigin, forward * vaultCheckDistance, lowHit ? Color.yellow : Color.gray);
            Debug.DrawRay(highOrigin, forward * vaultCheckDistance, highHit ? Color.red : Color.cyan);
        }

        if (!lowHit || highHit)
            return false; // kein Hindernis ODER Hindernis ist zu hoch

        // Oberkante grob über einen zusätzlichen Vertikal-Raycast direkt über
        // dem getroffenen Punkt finden, statt die feste maxVaultHeight zu nutzen -
        // ergibt ein natürlicheres Anschmiegen an die tatsächliche Höhe.
        Vector3 topProbeOrigin = lowHitInfo.point + forward * 0.1f + Vector3.up * maxVaultHeight;
        float obstacleTopY;

        if (Physics.Raycast(topProbeOrigin, Vector3.down, out RaycastHit topHit, maxVaultHeight, vaultLayerMask))
        {
            obstacleTopY = topHit.point.y;
        }
        else
        {
            obstacleTopY = basePos.y + maxVaultHeight; // Fallback
        }

        nearEdgePoint = new Vector3(lowHitInfo.point.x, obstacleTopY, lowHitInfo.point.z);
        farLandingPoint = nearEdgePoint + forward * vaultClearDistance;

        // Sicherstellen, dass hinter dem Hindernis tatsächlich Platz/Boden ist
        if (Physics.Raycast(farLandingPoint + Vector3.up * 2f, Vector3.down, out RaycastHit landHit, 5f, vaultLayerMask))
        {
            farLandingPoint.y = landHit.point.y;
        }

        return true;
    }

    IEnumerator PerformVault(Vector3 nearEdgePoint, Vector3 farLandingPoint)
    {
        isVaulting = true;

        if (groundMovement != null)
            groundMovement.movementBlocked = true;

        Vector3 startPos = transform.position;
        Vector3 peakPos = nearEdgePoint + Vector3.up * vaultClearanceHeight;

        float elapsed = 0f;

        // Phase 1: hoch und über die Kante (40% der Zeit)
        float phase1Duration = vaultDuration * 0.4f;
        while (elapsed < phase1Duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / phase1Duration);
            Vector3 pos = Vector3.Lerp(startPos, peakPos, EaseOutQuad(t));
            motor.Teleport(pos);
            motor.SetVelocity(Vector3.zero);
            yield return null;
        }

        // Phase 2: über das Hindernis hinweg zur Landeposition (60% der Zeit)
        elapsed = 0f;
        float phase2Duration = vaultDuration * 0.6f;
        Vector3 phase2Start = peakPos;

        while (elapsed < phase2Duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / phase2Duration);
            Vector3 pos = Vector3.Lerp(phase2Start, farLandingPoint, EaseInQuad(t));
            motor.Teleport(pos);
            motor.SetVelocity(Vector3.zero);
            yield return null;
        }

        // Restgeschwindigkeit in Blickrichtung übergeben, damit der Spieler
        // nicht abrupt stehen bleibt, sondern den Vault-Schwung mitnimmt
        Vector3 forward = look != null ? look.GetFlatForward() : transform.forward;
        float exitSpeed = momentum.GetCurrentMoveSpeed() * exitSpeedMultiplier;
        motor.SetHorizontalVelocity(forward * exitSpeed);
        motor.SetVerticalVelocity(0f);

        if (momentum != null)
            momentum.AddMomentum(momentumGainPerVault);

        if (groundMovement != null)
            groundMovement.movementBlocked = false;

        isVaulting = false;

        if (showDebugInfo)
            Debug.Log("🤸 Vault abgeschlossen");
    }

    float EaseOutQuad(float t) => 1f - (1f - t) * (1f - t);
    float EaseInQuad(float t) => t * t;

    public bool IsVaulting() => isVaulting;
}
