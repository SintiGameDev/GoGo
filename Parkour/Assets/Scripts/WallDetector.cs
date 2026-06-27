using UnityEngine;

/// <summary>
/// Reine Erkennungs-Komponente: tastet per Raycasts ab, ob sich eine Wand vor
/// dem Spieler befindet (für Walljump), und liefert Hit-Infos (Punkt, Normale,
/// Distanz). Führt selbst KEINE Bewegung aus - WalljumpHandler, VaultHandler
/// und ClimbHandler fragen hier nur Informationen ab.
/// </summary>
public class WallDetector : MonoBehaviour
{
    [Header("References")]
    public PlayerLook look;

    [Header("Wand-Erkennung")]
    [Tooltip("Layer, die als 'Wand' für Walljump zählen")]
    public LayerMask wallLayerMask = ~0;

    [Tooltip("Maximale Distanz für den Wand-Check vor dem Spieler")]
    public float wallCheckDistance = 0.8f;

    [Tooltip("Höhe über der Spielerbasis, von der aus der Wand-Raycast startet")]
    public float wallCheckHeight = 1.0f;

    [Tooltip("Maximaler Y-Anteil der Normale, damit eine Fläche noch als 'Wand' (nicht Boden/Decke) zählt")]
    public float maxWallNormalY = 0.35f;

    [Header("Debug")]
    public bool showDebugRays = true;

    private CharacterController characterController;

    void Awake()
    {
        characterController = GetComponent<CharacterController>();

        if (look == null)
            look = GetComponent<PlayerLook>();
    }

    /// <summary>
    /// Prüft, ob direkt vor dem Spieler (Blickrichtung, horizontal) eine Wand ist.
    /// </summary>
    public bool TryGetWallAhead(out RaycastHit hit)
    {
        Vector3 origin = transform.position + Vector3.up * wallCheckHeight;
        Vector3 direction = look != null ? look.GetFlatForward() : transform.forward;

        bool result = Physics.Raycast(origin, direction, out hit, wallCheckDistance, wallLayerMask);

        if (showDebugRays)
        {
            Debug.DrawRay(origin, direction * wallCheckDistance, result ? Color.green : Color.red);
        }

        if (!result)
            return false;

        return IsValidWallNormal(hit.normal);
    }

    /// <summary>
    /// Prüft mehrere Richtungen (vorne, leicht links/rechts) für robustere
    /// Walljump-Erkennung, auch wenn der Spieler nicht exakt frontal anläuft.
    /// Gibt den nächsten gültigen Treffer zurück.
    /// </summary>
    public bool TryGetWallAheadWide(out RaycastHit bestHit, float halfAngle = 25f)
    {
        Vector3 origin = transform.position + Vector3.up * wallCheckHeight;
        Vector3 forward = look != null ? look.GetFlatForward() : transform.forward;

        Vector3[] directions = new Vector3[]
        {
            forward,
            Quaternion.Euler(0f, halfAngle, 0f) * forward,
            Quaternion.Euler(0f, -halfAngle, 0f) * forward,
        };

        bool found = false;
        float closestDistance = float.MaxValue;
        bestHit = default;

        foreach (Vector3 dir in directions)
        {
            if (Physics.Raycast(origin, dir, out RaycastHit hit, wallCheckDistance, wallLayerMask))
            {
                if (showDebugRays)
                    Debug.DrawRay(origin, dir * hit.distance, Color.green);

                if (IsValidWallNormal(hit.normal) && hit.distance < closestDistance)
                {
                    closestDistance = hit.distance;
                    bestHit = hit;
                    found = true;
                }
            }
            else if (showDebugRays)
            {
                Debug.DrawRay(origin, dir * wallCheckDistance, Color.red);
            }
        }

        return found;
    }

    bool IsValidWallNormal(Vector3 normal)
    {
        return normal.y < maxWallNormalY && normal.y > -maxWallNormalY;
    }
}
