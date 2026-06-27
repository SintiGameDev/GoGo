using UnityEngine;

/// <summary>
/// Fundament des neuen Movement-Systems. Verwaltet EINE zentrale Velocity (m/s)
/// und wendet sie über Unity's CharacterController an. Alle States (Ground, Air,
/// Walljump, Slide, Vault, Climb) lesen/schreiben ausschließlich über die API
/// dieser Klasse, statt direkt mit dem CharacterController zu hantieren - das
/// hält die Kollisionsauflösung an einer Stelle und verhindert, dass mehrere
/// Skripte sich um moveDirection "streiten" (das Kernproblem des alten Systems).
///
/// WICHTIG zum Vorgänger-Bug: Der alte SC_FPSController hat moveDirection.x/.z
/// JEDEN Frame komplett aus Input neu berechnet (movement = forward*x + right*y),
/// wodurch ein einmalig gesetzter Sprung-Impuls im nächsten Frame sofort wieder
/// überschrieben wurde. Dieses System vermeidet das, indem currentVelocity ein
/// einziger State ist, der von Input ADDITIV beeinflusst wird (siehe ApplyMove),
/// nicht jeden Frame komplett neu gesetzt wird.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMotor : MonoBehaviour
{
    [Header("Schwerkraft")]
    public float gravity = 25f;
    [Tooltip("Maximale Fallgeschwindigkeit (negativ), verhindert endloses Beschleunigen bei langen Fällen")]
    public float maxFallSpeed = -40f;

    [Header("Boden-Erkennung")]
    [Tooltip("Zusätzlicher Toleranzabstand unter dem Spieler, der noch als 'grounded' zählt (gegen Grounded-Flackern an Kanten/leichten Unebenheiten)")]
    public float groundedSkinWidth = 0.1f;

    [Header("Geschwindigkeits-Limits")]
    [Tooltip("Maximale Gesamtgeschwindigkeit (x/y/z kombiniert), auf die die Velocity vor jedem Move gecapt wird")]
    public float maxTotalSpeed = 40f;

    [Header("Debug")]
    public bool showDebugInfo = false;

    private CharacterController characterController;

    // Die EINE zentrale Velocity, die von allen States gemeinsam genutzt wird.
    private Vector3 currentVelocity = Vector3.zero;

    private bool isGrounded = false;
    private bool wasGroundedLastFrame = false;
    private float lastGroundedTime = -999f;
    private float lastUngroundedTime = -999f;

    void Awake()
    {
        characterController = GetComponent<CharacterController>();
    }

    void Update()
    {
        UpdateGroundedState();
        ApplyGravity();
        ApplyMove();
    }

    void UpdateGroundedState()
    {
        wasGroundedLastFrame = isGrounded;
        isGrounded = characterController.isGrounded;

        if (isGrounded && !wasGroundedLastFrame)
        {
            lastGroundedTime = Time.time;
            OnLanded?.Invoke();
        }
        else if (!isGrounded && wasGroundedLastFrame)
        {
            lastUngroundedTime = Time.time;
            OnLeftGround?.Invoke();
        }
    }

    void ApplyGravity()
    {
        if (isGrounded && currentVelocity.y < 0f)
        {
            // Kleine konstante Down-Force statt 0, damit isGrounded auf
            // unebenem Boden/Slopes nicht ständig flackert.
            currentVelocity.y = -2f;
            return;
        }

        currentVelocity.y -= gravity * Time.deltaTime;
        currentVelocity.y = Mathf.Max(currentVelocity.y, maxFallSpeed);
    }

    void ApplyMove()
    {
        if (currentVelocity.magnitude > maxTotalSpeed)
        {
            currentVelocity = currentVelocity.normalized * maxTotalSpeed;
        }

        characterController.Move(currentVelocity * Time.deltaTime);

        if (showDebugInfo)
        {
            Debug.Log($"[PlayerMotor] Velocity: {currentVelocity} | Speed: {currentVelocity.magnitude:F1} | Grounded: {isGrounded}");
        }
    }

    // ---------------- Public API für States ----------------

    /// <summary>Events für States, die auf Landung/Abheben reagieren wollen.</summary>
    public event System.Action OnLanded;
    public event System.Action OnLeftGround;

    /// <summary>Setzt die komplette Velocity direkt (z.B. für einen Sprung-Impuls).</summary>
    public void SetVelocity(Vector3 velocity) => currentVelocity = velocity;

    /// <summary>Setzt nur die horizontale (X/Z) Komponente, Y bleibt unverändert.</summary>
    public void SetHorizontalVelocity(Vector3 horizontal)
    {
        currentVelocity.x = horizontal.x;
        currentVelocity.z = horizontal.z;
    }

    /// <summary>Setzt nur die vertikale (Y) Komponente, X/Z bleiben unverändert.</summary>
    public void SetVerticalVelocity(float y) => currentVelocity.y = y;

    /// <summary>Addiert einen Impuls auf die aktuelle Velocity (z.B. Walljump-Boost).</summary>
    public void AddImpulse(Vector3 impulse) => currentVelocity += impulse;

    public Vector3 GetVelocity() => currentVelocity;
    public Vector3 GetHorizontalVelocity() => new Vector3(currentVelocity.x, 0f, currentVelocity.z);
    public float GetHorizontalSpeed() => GetHorizontalVelocity().magnitude;

    public bool IsGrounded() => isGrounded;
    public float GetTimeSinceGrounded() => isGrounded ? 0f : Time.time - lastGroundedTime;
    public float GetTimeSinceUngrounded() => isGrounded ? 0f : Time.time - lastUngroundedTime;
    public float GetTimeSinceLanded() => Time.time - lastGroundedTime;

    public CharacterController GetCharacterController() => characterController;

    /// <summary>
    /// Teleportiert den Spieler direkt (z.B. für Vault/Climb-Endposition), ohne
    /// über die normale Move()-Kollisionsauflösung zu gehen. Velocity wird NICHT
    /// automatisch verändert - der Aufrufer sollte sie passend setzen.
    /// </summary>
    public void Teleport(Vector3 worldPosition)
    {
        characterController.enabled = false;
        transform.position = worldPosition;
        characterController.enabled = true;
    }
}
