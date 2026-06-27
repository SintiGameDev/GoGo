using UnityEngine;

/// <summary>
/// Grundlegende Boden-Bewegung: WASD-Laufen relativ zur Blickrichtung, normaler
/// Sprung. Liest Input über PlayerInputContext, schreibt Velocity ausschließlich
/// über PlayerMotor (siehe dortige Kommentare zum Vorgänger-Bug). Andere States
/// (Slide, Vault, Climb, Walljump) können currentMoveBlocked setzen, um die
/// normale Bodenbewegung temporär zu unterbinden, während sie selbst aktiv sind.
/// </summary>
public class GroundMovement : MonoBehaviour
{
    [Header("References")]
    public PlayerMotor motor;
    public PlayerInputContext input;
    public PlayerMomentum momentum;

    [Header("Sprung")]
    public float jumpHeight = 1.6f;

    [Tooltip("Wie schnell sich die horizontale Geschwindigkeit an die Ziel-Richtung/Speed anpasst (höher = direkter/snappier, niedriger = mehr Trägheit)")]
    public float groundAcceleration = 18f;

    [Tooltip("Gleiche Beschleunigung gilt auch in der Luft, aber meist schwächer für weniger Kontrolle im Flug - siehe airControlMultiplier")]
    public float airControlMultiplier = 0.5f;

    [Header("Debug")]
    public bool showDebugInfo = false;

    // Von anderen States (Slide, Vault, Climb, Walljump) gesetzt, um die normale
    // WASD-Bewegung währenddessen zu unterbinden, ohne dass diese States selbst
    // mit PlayerMotor/Input direkt konkurrieren müssen.
    [HideInInspector] public bool movementBlocked = false;

    void Awake()
    {
        if (motor == null) motor = GetComponent<PlayerMotor>();
        if (input == null) input = GetComponent<PlayerInputContext>();
        if (momentum == null) momentum = GetComponent<PlayerMomentum>();
    }

    void Update()
    {
        if (movementBlocked)
            return;

        ApplyHorizontalMovement();
    }

    void ApplyHorizontalMovement()
    {
        Vector2 rawInput = input.GetMoveInput();
        Vector3 wishDir = (transform.forward * rawInput.y + transform.right * rawInput.x);

        if (wishDir.sqrMagnitude > 1f)
            wishDir.Normalize();

        float targetSpeed = momentum.GetCurrentMoveSpeed();
        Vector3 targetHorizontalVelocity = wishDir * targetSpeed;

        Vector3 currentHorizontal = motor.GetHorizontalVelocity();
        float accel = motor.IsGrounded() ? groundAcceleration : groundAcceleration * airControlMultiplier;

        Vector3 newHorizontal = Vector3.MoveTowards(
            currentHorizontal,
            targetHorizontalVelocity,
            accel * Time.deltaTime * Mathf.Max(targetSpeed, 1f) // skaliert mit Zielgeschwindigkeit, damit hohe Momentum-Speeds nicht "träger" wirken als niedrige
        );

        motor.SetHorizontalVelocity(newHorizontal);

        if (showDebugInfo)
        {
            Debug.Log($"[GroundMovement] WishDir: {wishDir} | Target: {targetSpeed:F1} m/s | Current: {currentHorizontal.magnitude:F1} m/s");
        }
    }

    /// <summary>
    /// Wird vom PlayerActionResolver aufgerufen, wenn die Action-Taste gedrückt
    /// wurde UND kein höher priorisierter Kontext (Climb/Vault/Walljump) greift.
    /// GroundMovement lauscht selbst NICHT auf Input, um doppelte/konkurrierende
    /// Reaktionen auf dieselbe Taste zu vermeiden - der Resolver ist die einzige
    /// Stelle, die ActionPressedThisFrame() konsumiert.
    /// </summary>
    public bool TryJump()
    {
        if (!motor.IsGrounded())
            return false;

        float jumpVelocity = Mathf.Sqrt(2f * motor.gravity * jumpHeight);
        motor.SetVerticalVelocity(jumpVelocity);

        if (showDebugInfo)
            Debug.Log($"[GroundMovement] Jump! Velocity: {jumpVelocity:F1} m/s");

        return true;
    }
}
