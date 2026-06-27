using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class SC_FPSController : MonoBehaviour
{
    public float walkingSpeed = 7.5f;
    public float runningSpeed = 11.5f;
    public float jumpSpeed = 8.0f;
    public float wallJumpSpeed = 10.0f;
    public float gravity = 20.0f;
    public Camera playerCamera;
    public float lookSpeed = 2.0f;
    public float lookXLimit = 45.0f;

    // Wall Grab Settings
    public float wallGrabDuration = 2.0f;
    public float normalMass = 1.0f;
    public float wallGrabMass = 0.1f;
    public LayerMask wallLayer;
    public GameObject colliderObject;

    // FOV-EINSTELLUNGEN
    public float baseFOV = 60f;
    public float maxFOV = 90f;
    public float fovChangeDuration = 0.3f;
    public float fovSpeedThreshold = 15f;

    // Vertikale Velocity Einstellungen
    [Header("Vertical Boost Settings")]
    public float maxVerticalBoostVelocity = 15f;
    public float verticalBoostDecayRate = 5f;
    public bool allowVerticalBoostInAir = true;

    // Momentum Preservation
    [Header("Momentum Preservation")]
    public bool preserveHorizontalMomentumOnLanding = true;
    public float landingMomentumRetention = 0.85f;
    public float maxSafeGroundImpactSpeed = 25f;
    public bool smoothSpeedTransitions = true;
    public float speedTransitionRate = 10f;

    // NEUE: Velocity Reset Settings
    [Header("Velocity Reset Settings")]
    [Tooltip("Minimale Velocity bevor alle Boosts zurückgesetzt werden")]
    public float minVelocityThreshold = 1.5f;
    [Tooltip("Wie lange Velocity unter Threshold sein muss (in Sekunden)")]
    public float velocityResetDelay = 0.3f;
    [Tooltip("Automatisch alle Boosts zurücksetzen bei niedriger Velocity")]
    public bool autoResetBoostsOnLowVelocity = true;

    CharacterController characterController;
    Rigidbody rb;
    Vector3 moveDirection = Vector3.zero;
    float rotationX = 0;

    [HideInInspector]
    public bool canMove = true;

    private bool isGrappling = false;
    private bool isWallGrabbing = false;
    private float wallGrabTimer = 0f;
    private Vector3 wallNormal;
    private WallGrabTrigger wallGrabTrigger;
    private VelocityMultiplier velocityMultiplier;
    private HeadBang headBang;
    private SurfController surfController;

    private LTDescr fovTween;
    private float lastTargetFOV = 0f;

    private float externalVerticalBoost = 0f;
    private float lastGroundedTime = 0f;
    private bool wasGroundedLastFrame = false;

    // Velocity Preservation State
    private Vector3 lastVelocity = Vector3.zero;
    private Vector3 preservedHorizontalVelocity = Vector3.zero;
    private bool hadHighSpeedLastFrame = false;
    private float targetWalkingSpeed;
    private float targetRunningSpeed;
    private float currentSmoothWalkSpeed;
    private float currentSmoothRunSpeed;

    // NEUE: Low Velocity Tracking
    private float lowVelocityTimer = 0f;
    private bool wasVelocityLowLastFrame = false;

    void Start()
    {
        characterController = GetComponent<CharacterController>();
        velocityMultiplier = GetComponent<VelocityMultiplier>();
        headBang = GetComponent<HeadBang>();
        surfController = GetComponent<SurfController>();

        if (colliderObject != null)
        {
            rb = colliderObject.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.mass = normalMass;
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            wallGrabTrigger = colliderObject.GetComponent<WallGrabTrigger>();
            if (wallGrabTrigger == null)
            {
                wallGrabTrigger = colliderObject.AddComponent<WallGrabTrigger>();
            }
            wallGrabTrigger.Initialize(this);
        }
        else
        {
            Debug.LogError("ColliderObject nicht zugewiesen!");
        }

        if (playerCamera != null)
        {
            playerCamera.fieldOfView = baseFOV;
            lastTargetFOV = baseFOV;
        }

        targetWalkingSpeed = walkingSpeed;
        targetRunningSpeed = runningSpeed;
        currentSmoothWalkSpeed = walkingSpeed;
        currentSmoothRunSpeed = runningSpeed;
    }

    void Update()
    {
        // Grounded State Tracking
        bool isCurrentlyGrounded = characterController.isGrounded;

        if (isCurrentlyGrounded && !wasGroundedLastFrame)
        {
            OnPlayerLanded();
            lastGroundedTime = Time.time;
        }

        wasGroundedLastFrame = isCurrentlyGrounded;

        // Wall Grab Timer
        if (isWallGrabbing)
        {
            wallGrabTimer -= Time.deltaTime;
            if (wallGrabTimer <= 0f)
            {
                EndWallGrab();
            }
        }

        // Externen vertikalen Boost holen
        if (surfController != null && allowVerticalBoostInAir)
        {
            float surfVerticalBoost = surfController.GetVerticalBoost();
            externalVerticalBoost = Mathf.Lerp(externalVerticalBoost, surfVerticalBoost, Time.deltaTime * 10f);
            externalVerticalBoost = Mathf.Clamp(externalVerticalBoost, 0f, maxVerticalBoostVelocity);
        }

        // Smooth Speed Transitions
        if (smoothSpeedTransitions)
        {
            currentSmoothWalkSpeed = Mathf.Lerp(currentSmoothWalkSpeed, targetWalkingSpeed, Time.deltaTime * speedTransitionRate);
            currentSmoothRunSpeed = Mathf.Lerp(currentSmoothRunSpeed, targetRunningSpeed, Time.deltaTime * speedTransitionRate);
        }
        else
        {
            currentSmoothWalkSpeed = targetWalkingSpeed;
            currentSmoothRunSpeed = targetRunningSpeed;
        }

        // Bewegung
        Vector3 forward = transform.TransformDirection(Vector3.forward);
        Vector3 right = transform.TransformDirection(Vector3.right);

        bool isRunning = Input.GetKey(KeyCode.LeftShift);
        float targetSpeed = isRunning ? currentSmoothRunSpeed : currentSmoothWalkSpeed;

        float inputX = Input.GetAxis("Vertical");
        float inputY = Input.GetAxis("Horizontal");

        float curSpeedX = canMove && !isGrappling ? targetSpeed * inputX : 0;
        float curSpeedY = canMove && !isGrappling ? targetSpeed * inputY : 0;

        float movementDirectionY = moveDirection.y;

        // Momentum Preservation
        if (preserveHorizontalMomentumOnLanding && preservedHorizontalVelocity.magnitude > 0.1f)
        {
            Vector3 inputDirection = (forward * curSpeedX) + (right * curSpeedY);
            float preservedInfluence = Mathf.Clamp01(preservedHorizontalVelocity.magnitude / walkingSpeed);
            moveDirection = Vector3.Lerp(inputDirection, preservedHorizontalVelocity, preservedInfluence * 0.5f);
            preservedHorizontalVelocity = Vector3.Lerp(preservedHorizontalVelocity, Vector3.zero, Time.deltaTime * 2f);
        }
        else
        {
            moveDirection = (forward * curSpeedX) + (right * curSpeedY);
        }

        // FOV Update
        UpdateFOV();

        // Jump Logik
        if (Input.GetButton("Jump") && canMove && !isGrappling && characterController.isGrounded)
        {
            moveDirection.y = jumpSpeed;

            if (velocityMultiplier != null)
            {
                velocityMultiplier.OnJump();
            }
        }
        else if (Input.GetButtonDown("Jump") && canMove && !isGrappling && isWallGrabbing)
        {
            PerformWallJump();
        }
        else
        {
            moveDirection.y = movementDirectionY;
        }

        // Vertikalen Boost anwenden
        if (!characterController.isGrounded && externalVerticalBoost > 0.1f)
        {
            moveDirection.y += externalVerticalBoost * Time.deltaTime * 10f;
            moveDirection.y = Mathf.Clamp(moveDirection.y, -gravity, maxVerticalBoostVelocity);
        }

        // Gravity
        if (!characterController.isGrounded && !isGrappling)
        {
            float currentGravity = isWallGrabbing ? gravity * 0.3f : gravity;

            if (externalVerticalBoost > 1f)
            {
                float gravityReduction = Mathf.Clamp01(externalVerticalBoost / maxVerticalBoostVelocity);
                currentGravity *= (1f - gravityReduction * 0.5f);
            }

            moveDirection.y -= currentGravity * Time.deltaTime;
        }

        // Externen Boost abbauen wenn am Boden
        if (characterController.isGrounded && externalVerticalBoost > 0f)
        {
            externalVerticalBoost = Mathf.Lerp(externalVerticalBoost, 0f, Time.deltaTime * verticalBoostDecayRate);
        }

        // Velocity vor dem Move speichern
        lastVelocity = characterController.velocity;

        // Controller bewegen
        characterController.Move(moveDirection * Time.deltaTime);

        // NEUE: Low Velocity Detection und Boost Reset
        CheckAndResetBoostsOnLowVelocity();

        // Collision Recovery
        DetectUnexpectedVelocityLoss();

        // Kamera und Rotation
        if (canMove && !isGrappling)
        {
            rotationX += -Input.GetAxis("Mouse Y") * lookSpeed;
            rotationX = Mathf.Clamp(rotationX, -lookXLimit, lookXLimit);
            playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0, 0);
            transform.rotation *= Quaternion.Euler(0, Input.GetAxis("Mouse X") * lookSpeed, 0);
        }
    }

    // NEUE: Prüft niedrige Velocity und resettet Boosts
    void CheckAndResetBoostsOnLowVelocity()
    {
        if (!autoResetBoostsOnLowVelocity)
            return;

        // Berechne Total Velocity (horizontal + vertikal)
        float totalVelocity = characterController.velocity.magnitude;
        bool isVelocityLow = totalVelocity < minVelocityThreshold;

        // Nur zählen wenn am Boden ODER in der Luft ohne Input
        bool shouldCount = characterController.isGrounded ||
                          (Mathf.Abs(Input.GetAxis("Vertical")) < 0.1f &&
                           Mathf.Abs(Input.GetAxis("Horizontal")) < 0.1f);

        if (isVelocityLow && shouldCount)
        {
            if (!wasVelocityLowLastFrame)
            {
                // Velocity ist gerade unter Threshold gefallen
                lowVelocityTimer = 0f;
                wasVelocityLowLastFrame = true;
            }
            else
            {
                // Velocity ist weiterhin niedrig
                lowVelocityTimer += Time.deltaTime;

                if (lowVelocityTimer >= velocityResetDelay)
                {
                    // Reset alle Boosts
                    ResetAllBoosts();
                    lowVelocityTimer = 0f; // Timer zurücksetzen
                }
            }
        }
        else
        {
            // Velocity ist wieder hoch
            wasVelocityLowLastFrame = false;
            lowVelocityTimer = 0f;
        }
    }

    // NEUE: Resettet alle Speed-Boosts
    void ResetAllBoosts()
    {
        Debug.Log($"🔄 Low Velocity detected ({characterController.velocity.magnitude:F2} m/s < {minVelocityThreshold:F2}) - Resetting all boosts!");

        // Reset VelocityMultiplier Boosts
        if (velocityMultiplier != null)
        {
            velocityMultiplier.ForceResetBoosts();
        }

        // Reset SurfController Speed
        if (surfController != null)
        {
            surfController.ResetSurfSpeed();
        }

        // Reset preserved momentum
        preservedHorizontalVelocity = Vector3.zero;
        externalVerticalBoost = 0f;

        // Reset zu Original Speeds
        targetWalkingSpeed = walkingSpeed;
        targetRunningSpeed = runningSpeed;
    }

    void OnPlayerLanded()
    {
        if (preserveHorizontalMomentumOnLanding)
        {
            Vector3 lastHorizontalVel = new Vector3(lastVelocity.x, 0, lastVelocity.z);
            float impactSpeed = lastHorizontalVel.magnitude;

            if (impactSpeed > walkingSpeed * 1.5f)
            {
                float retentionFactor = landingMomentumRetention;

                if (impactSpeed > maxSafeGroundImpactSpeed)
                {
                    float excessSpeed = impactSpeed - maxSafeGroundImpactSpeed;
                    float speedPenalty = Mathf.Clamp01(excessSpeed / maxSafeGroundImpactSpeed) * 0.3f;
                    retentionFactor -= speedPenalty;
                }

                preservedHorizontalVelocity = lastHorizontalVel * retentionFactor;

                Debug.Log($"Landing! Preserved: {preservedHorizontalVelocity.magnitude:F1} m/s (from {impactSpeed:F1})");
            }
        }

        moveDirection.y = Mathf.Max(moveDirection.y, -2f);
    }

    void DetectUnexpectedVelocityLoss()
    {
        Vector3 currentVelocity = characterController.velocity;
        Vector3 lastHorizontalVel = new Vector3(lastVelocity.x, 0, lastVelocity.z);
        Vector3 currentHorizontalVel = new Vector3(currentVelocity.x, 0, currentVelocity.z);

        float velocityLoss = lastHorizontalVel.magnitude - currentHorizontalVel.magnitude;

        if (velocityLoss > lastHorizontalVel.magnitude * 0.3f && lastHorizontalVel.magnitude > walkingSpeed)
        {
            Vector3 recoveredVelocity = lastHorizontalVel * 0.7f;

            if (!characterController.isGrounded)
            {
                moveDirection.x = recoveredVelocity.x;
                moveDirection.z = recoveredVelocity.z;

                Debug.LogWarning($"Velocity Loss Detected! Recovered: {recoveredVelocity.magnitude:F1} m/s");
            }
        }
    }

    void UpdateFOV()
    {
        if (canMove && playerCamera != null)
        {
            Vector3 flatVelocity = new Vector3(characterController.velocity.x, 0, characterController.velocity.z);
            float currentHorizontalSpeed = flatVelocity.magnitude;

            float totalSpeed = currentHorizontalSpeed;
            if (externalVerticalBoost > 0f)
            {
                totalSpeed += externalVerticalBoost * 0.5f;
            }

            float speedRatio = Mathf.Clamp01(totalSpeed / fovSpeedThreshold);
            float targetFOV = Mathf.Lerp(baseFOV, maxFOV, speedRatio);

            if (Mathf.Abs(lastTargetFOV - targetFOV) > 0.5f)
            {
                lastTargetFOV = targetFOV;

                if (LeanTween.isTweening(playerCamera.gameObject))
                {
                    LeanTween.cancel(playerCamera.gameObject);
                }

                fovTween = LeanTween.value(playerCamera.gameObject, playerCamera.fieldOfView, targetFOV, fovChangeDuration)
                    .setEase(LeanTweenType.easeOutQuad)
                    .setOnUpdate((float value) => {
                        playerCamera.fieldOfView = value;
                    })
                    .setOnComplete(() => {
                        fovTween = null;
                    });
            }
        }
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.normal.y > -0.3f && hit.normal.y < 0.3f && !characterController.isGrounded)
        {
            StartWallGrab(hit.normal);
        }
    }

    public void StartWallGrab(Vector3 normal)
    {
        if (!isWallGrabbing && !characterController.isGrounded)
        {
            isWallGrabbing = true;
            wallGrabTimer = wallGrabDuration;
            wallNormal = normal;

            if (rb != null)
            {
                rb.mass = wallGrabMass;
            }

            moveDirection.y = Mathf.Max(moveDirection.y, -2f);

            if (velocityMultiplier != null)
            {
                velocityMultiplier.OnWallGrabStart();
            }

            if (headBang != null)
            {
                headBang.OnWallGrabImpact();
            }

            Debug.Log("Wall Grab Started!");
        }
    }

    void EndWallGrab()
    {
        if (isWallGrabbing)
        {
            isWallGrabbing = false;

            if (rb != null)
            {
                rb.mass = normalMass;
            }

            if (velocityMultiplier != null)
            {
                velocityMultiplier.OnWallGrabEnd();
            }

            Debug.Log("Wall Grab Ended!");
        }
    }

    void PerformWallJump()
    {
        Vector3 jumpDirection = wallNormal.normalized;
        jumpDirection.y = 0;

        moveDirection = jumpDirection * walkingSpeed * 0.5f;
        moveDirection.y = wallJumpSpeed;

        if (externalVerticalBoost > 1f)
        {
            moveDirection.y += externalVerticalBoost * 0.3f;
        }

        if (velocityMultiplier != null)
        {
            velocityMultiplier.OnWallJump();
        }

        EndWallGrab();
        Debug.Log("Wall Jump!");
    }

    void OnGUI()
    {
        if (isWallGrabbing)
        {
            GUI.Label(new Rect(10, 10, 300, 20), $"Wall Grab Time: {wallGrabTimer:F2}s");
        }

        if (isGrappling)
        {
            GUI.Label(new Rect(10, 30, 300, 20), "GRAPPLING - Movement Disabled");
        }

        if (externalVerticalBoost > 0.1f)
        {
            GUI.color = Color.cyan;
            GUI.Label(new Rect(10, 50, 400, 20), $"Vertical Boost: {externalVerticalBoost:F2} m/s");

            float boostRatio = externalVerticalBoost / maxVerticalBoostVelocity;
            GUI.color = Color.Lerp(Color.yellow, Color.green, boostRatio);
            GUI.Box(new Rect(10, 70, 200 * boostRatio, 10), "");
            GUI.color = Color.white;
        }

        if (characterController != null)
        {
            Vector3 velocity = characterController.velocity;
            float totalSpeed = velocity.magnitude;
            GUI.Label(new Rect(10, 90, 400, 20), $"Total Velocity: {totalSpeed:F1} m/s (Y: {velocity.y:F1})");

            if (preservedHorizontalVelocity.magnitude > 0.1f)
            {
                GUI.color = Color.yellow;
                GUI.Label(new Rect(10, 110, 400, 20), $"Preserved Momentum: {preservedHorizontalVelocity.magnitude:F1} m/s");
                GUI.color = Color.white;
            }

            // NEUE: Low Velocity Warning
            if (wasVelocityLowLastFrame && autoResetBoostsOnLowVelocity)
            {
                GUI.color = Color.red;
                float timeRemaining = velocityResetDelay - lowVelocityTimer;
                GUI.Label(new Rect(10, 130, 400, 20),
                    $"⚠️ LOW VELOCITY - Reset in {timeRemaining:F1}s");
                GUI.color = Color.white;
            }
        }
    }

    public void SetGrapplingState(bool grappling)
    {
        isGrappling = grappling;
    }

    public void ForceJump(float jumpForce)
    {
        moveDirection.y = jumpForce;
    }

    public void AddVerticalBoost(float boostAmount)
    {
        externalVerticalBoost += boostAmount;
        externalVerticalBoost = Mathf.Clamp(externalVerticalBoost, 0f, maxVerticalBoostVelocity);
    }

    public void SetVerticalBoost(float boostAmount)
    {
        externalVerticalBoost = Mathf.Clamp(boostAmount, 0f, maxVerticalBoostVelocity);
    }

    public void SetTargetWalkingSpeed(float speed)
    {
        targetWalkingSpeed = speed;
        if (!smoothSpeedTransitions)
        {
            walkingSpeed = speed;
        }
    }

    public void SetTargetRunningSpeed(float speed)
    {
        targetRunningSpeed = speed;
        if (!smoothSpeedTransitions)
        {
            runningSpeed = speed;
        }
    }

    public float GetVerticalBoost() => externalVerticalBoost;
    public bool IsMovingUpward() => moveDirection.y > 0f || externalVerticalBoost > 0.5f;
    public bool IsWallGrabbing() => isWallGrabbing;
    public bool IsGrappling() => isGrappling;
    public float GetTimeSinceGrounded() => characterController.isGrounded ? 0f : Time.time - lastGroundedTime;
    public Vector3 GetMoveDirection() => moveDirection;
    public void ModifyMoveDirection(Vector3 newDirection) => moveDirection = newDirection;
}

public class WallGrabTrigger : MonoBehaviour
{
    private SC_FPSController controller;

    public void Initialize(SC_FPSController fpsController)
    {
        controller = fpsController;
    }

    void OnTriggerEnter(Collider other)
    {
        Vector3 direction = transform.position - other.ClosestPoint(transform.position);
        direction.y = 0;

        if (direction.magnitude > 0.01f)
        {
            Vector3 normal = direction.normalized;
            controller.StartWallGrab(normal);
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (collision.contacts.Length > 0)
        {
            Vector3 normal = collision.contacts[0].normal;
            if (normal.y > -0.3f && normal.y < 0.3f)
            {
                controller.StartWallGrab(normal);
            }
        }
    }
}