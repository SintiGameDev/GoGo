using UnityEngine;

/// <summary>
/// Sliding: eigene Taste (PlayerInputContext.slideKey, NICHT die Action-Taste -
/// Sliding ist kein Kontext der Climb/Vault/Walljump/Jump-Priorität, sondern
/// parallel dazu nutzbar). Je höher das Tempo beim Start des Slides, desto
/// größer der Momentum-Boost - das belohnt Sliding aus vollem Sprint/nach
/// Walljump-Chains stärker als Sliding aus dem Stand.
/// </summary>
public class SlideHandler : MonoBehaviour
{
    [Header("References")]
    public PlayerMotor motor;
    public PlayerInputContext input;
    public PlayerMomentum momentum;
    public GroundMovement groundMovement;
    public PlayerLook look;

    [Header("Slide-Bedingungen")]
    [Tooltip("Mindesttempo (m/s), um überhaupt sliden zu können - verhindert Sliding aus dem Stillstand")]
    public float minSpeedToStartSlide = 3.0f;

    [Header("Slide-Verhalten")]
    public float slideDuration = 0.8f;
    [Tooltip("Wie stark die Geschwindigkeit während des Slides über die Dauer abklingt (0 = kein Abklingen, 1 = volles Abklingen auf 0)")]
    public float slideSpeedDecay = 0.4f;

    [Tooltip("Höhenreduktion des Colliders während des Slides (für 'unter Hindernissen durchrutschen')")]
    public float slideHeightReduction = 0.7f;

    [Header("Momentum-Boost (tempo-abhängig)")]
    [Tooltip("Boost bei minSpeedToStartSlide (untere Grenze)")]
    public float minMomentumBoost = 0.05f;

    [Tooltip("Boost bei maximal möglichem Tempo (PlayerMomentum.maxMultiplier × baseMoveSpeed)")]
    public float maxMomentumBoost = 0.4f;

    [Header("Debug")]
    public bool showDebugInfo = true;

    [Header("Sicherheit")]
    [Tooltip("Layer, die beim Aufstehen aus dem Slide als Hindernis über dem Kopf zählen")]
    public LayerMask standUpCheckLayerMask = ~0;

    private bool isSliding = false;
    private float slideTimer = 0f;
    private float slideStartSpeed = 0f;
    private Vector3 slideDirection = Vector3.forward;

    private CharacterController characterController;
    private float originalControllerHeight;
    private Vector3 originalControllerCenter;

    void Awake()
    {
        if (motor == null) motor = GetComponent<PlayerMotor>();
        if (input == null) input = GetComponent<PlayerInputContext>();
        if (momentum == null) momentum = GetComponent<PlayerMomentum>();
        if (groundMovement == null) groundMovement = GetComponent<GroundMovement>();
        if (look == null) look = GetComponent<PlayerLook>();

        characterController = motor.GetCharacterController();
        originalControllerHeight = characterController.height;
        originalControllerCenter = characterController.center;
    }

    void Update()
    {
        if (isSliding)
        {
            UpdateSlide();
            return;
        }

        if (input.SlidePressedThisFrame())
        {
            TryStartSlide();
        }
    }

    void TryStartSlide()
    {
        if (!motor.IsGrounded())
            return;

        float currentSpeed = motor.GetHorizontalSpeed();
        if (currentSpeed < minSpeedToStartSlide)
            return;

        isSliding = true;
        slideTimer = 0f;
        slideStartSpeed = currentSpeed;

        Vector3 horizontalVel = motor.GetHorizontalVelocity();
        slideDirection = horizontalVel.sqrMagnitude > 0.01f
            ? horizontalVel.normalized
            : (look != null ? look.GetFlatForward() : transform.forward);

        if (groundMovement != null)
            groundMovement.movementBlocked = true;

        ApplyCrouchCollider(true);

        // Momentum-Boost skaliert mit dem Tempo beim Start: höheres Tempo = mehr Boost
        float maxPossibleSpeed = momentum != null ? momentum.baseMoveSpeed * momentum.maxMultiplier : slideStartSpeed;
        float speedRatio = Mathf.Clamp01(slideStartSpeed / Mathf.Max(maxPossibleSpeed, 0.01f));
        float boost = Mathf.Lerp(minMomentumBoost, maxMomentumBoost, speedRatio);

        if (momentum != null)
            momentum.AddMomentum(boost);

        if (showDebugInfo)
            Debug.Log($"🛷 Slide gestartet! Start-Speed: {slideStartSpeed:F1} m/s | Momentum-Boost: +{boost:F2}");
    }

    void UpdateSlide()
    {
        slideTimer += Time.deltaTime;
        float t = Mathf.Clamp01(slideTimer / slideDuration);

        float currentTargetSpeed = Mathf.Lerp(slideStartSpeed, slideStartSpeed * (1f - slideSpeedDecay), t);
        motor.SetHorizontalVelocity(slideDirection * currentTargetSpeed);

        bool slideKeyReleased = !input.IsSlideHeld();
        bool slideTimeUp = slideTimer >= slideDuration;
        bool noLongerGrounded = !motor.IsGrounded();

        if (slideKeyReleased || slideTimeUp || noLongerGrounded)
        {
            // Nur aufstehen, wenn über dem Kopf genug Platz ist - sonst Slide
            // erzwungen fortsetzen (verhindert, dass der vergrößerte Collider
            // beim Aufstehen in ein niedriges Hindernis hineinclippt, unter dem
            // gerade durchgeslided wird).
            if (HasRoomToStandUp() || noLongerGrounded)
            {
                EndSlide();
            }
        }
    }

    bool HasRoomToStandUp()
    {
        float heightDifference = originalControllerHeight - characterController.height;
        if (heightDifference <= 0.01f)
            return true;

        Vector3 origin = transform.position + Vector3.up * characterController.height;
        return !Physics.Raycast(origin, Vector3.up, heightDifference + 0.05f, standUpCheckLayerMask);
    }

    void EndSlide()
    {
        isSliding = false;
        ApplyCrouchCollider(false);

        if (groundMovement != null)
            groundMovement.movementBlocked = false;

        if (showDebugInfo)
            Debug.Log("🛷 Slide beendet");
    }

    void ApplyCrouchCollider(bool crouching)
    {
        if (characterController == null)
            return;

        if (crouching)
        {
            characterController.height = originalControllerHeight * (1f - slideHeightReduction);
            characterController.center = new Vector3(
                originalControllerCenter.x,
                originalControllerCenter.y * (1f - slideHeightReduction),
                originalControllerCenter.z
            );
        }
        else
        {
            characterController.height = originalControllerHeight;
            characterController.center = originalControllerCenter;
        }
    }

    public bool IsSliding() => isSliding;
}
