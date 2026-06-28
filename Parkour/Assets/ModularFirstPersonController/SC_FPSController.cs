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

    [Header("Speed Settings")]
    public float speedTransitionRate = 10f;
    [Tooltip("Maximale Gesamtgeschwindigkeit (x/y/z kombiniert), auf die moveDirection vor jedem Move gecapt wird")]
    public float maxTotalVelocity = 30f;

    [Header("Debug")]
    [Tooltip("Zeigt das OnGUI-Debug-Overlay (Wall Grab Timer, Grappling-Status, Total Velocity) - bei Bedarf ausschalten, um die Anzeige zu verstecken, ohne den Code zu entfernen")]
    public bool showDebugInfo = false;

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

    private LTDescr fovTween;
    private float lastTargetFOV = 0f;

    private float lastGroundedTime = 0f;
    private bool wasGroundedLastFrame = false;

    // Speed State
    private float targetWalkingSpeed;
    private float targetRunningSpeed;
    private float currentSmoothWalkSpeed;
    private float currentSmoothRunSpeed;

    void Start()
    {
        characterController = GetComponent<CharacterController>();
        velocityMultiplier = GetComponent<VelocityMultiplier>();
        headBang = GetComponent<HeadBang>();

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

        // Smooth Speed Transitions
        currentSmoothWalkSpeed = Mathf.Lerp(currentSmoothWalkSpeed, targetWalkingSpeed, Time.deltaTime * speedTransitionRate);
        currentSmoothRunSpeed = Mathf.Lerp(currentSmoothRunSpeed, targetRunningSpeed, Time.deltaTime * speedTransitionRate);

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

        moveDirection = (forward * curSpeedX) + (right * curSpeedY);

        // FOV Update
        UpdateFOV();

        // Jump Logik
        if (Input.GetButtonDown("Jump") && canMove && !isGrappling && characterController.isGrounded)
        {
            moveDirection.y = jumpSpeed;

            if (velocityMultiplier != null)
            {
                velocityMultiplier.OnJumpAction();
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

        // Gravity
        if (!characterController.isGrounded && !isGrappling)
        {
            float currentGravity = isWallGrabbing ? gravity * 0.3f : gravity;
            moveDirection.y -= currentGravity * Time.deltaTime;
        }

        // Maximale Total Velocity cappen (x/y/z kombiniert), unabhaengig von der Quelle
        if (moveDirection.magnitude > maxTotalVelocity)
        {
            moveDirection = moveDirection.normalized * maxTotalVelocity;
        }

        // Controller bewegen
        characterController.Move(moveDirection * Time.deltaTime);

        // Kamera und Rotation
        if (canMove && !isGrappling)
        {
            rotationX += -Input.GetAxis("Mouse Y") * lookSpeed;
            rotationX = Mathf.Clamp(rotationX, -lookXLimit, lookXLimit);
            playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0, 0);
            transform.rotation *= Quaternion.Euler(0, Input.GetAxis("Mouse X") * lookSpeed, 0);
        }
    }

    void OnPlayerLanded()
    {
        moveDirection.y = Mathf.Max(moveDirection.y, -2f);
    }

    void UpdateFOV()
    {
        if (canMove && playerCamera != null)
        {
            Vector3 flatVelocity = new Vector3(characterController.velocity.x, 0, characterController.velocity.z);
            float currentHorizontalSpeed = flatVelocity.magnitude;

            float speedRatio = Mathf.Clamp01(currentHorizontalSpeed / fovSpeedThreshold);
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

            Debug.Log("Wall Grab Ended!");
        }
    }

    void PerformWallJump()
    {
        Vector3 jumpDirection = wallNormal.normalized;
        jumpDirection.y = 0;

        moveDirection = jumpDirection * walkingSpeed * 0.5f;
        moveDirection.y = wallJumpSpeed;

        if (velocityMultiplier != null)
        {
            velocityMultiplier.OnJumpAction();
        }

        EndWallGrab();
        Debug.Log("Wall Jump!");
    }

    void OnGUI()
    {
        if (!showDebugInfo)
            return;

        if (isWallGrabbing)
        {
            GUI.Label(new Rect(10, 10, 300, 20), $"Wall Grab Time: {wallGrabTimer:F2}s");
        }

        if (isGrappling)
        {
            GUI.Label(new Rect(10, 30, 300, 20), "GRAPPLING - Movement Disabled");
        }

        if (characterController != null)
        {
            Vector3 velocity = characterController.velocity;
            float totalSpeed = velocity.magnitude;
            GUI.Label(new Rect(10, 90, 400, 20), $"Total Velocity: {totalSpeed:F1} m/s (Y: {velocity.y:F1})");
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

    public void SetTargetWalkingSpeed(float speed)
    {
        targetWalkingSpeed = speed;

        // Boost (Speed steigt) sofort uebernehmen, damit er nicht durch das
        // Lerp in Update() verzoegert/verschluckt wird. Decay (Speed faellt)
        // bleibt ueber das Lerp sanft.
        if (speed > currentSmoothWalkSpeed)
        {
            currentSmoothWalkSpeed = speed;
        }
    }

    public void SetTargetRunningSpeed(float speed)
    {
        targetRunningSpeed = speed;

        if (speed > currentSmoothRunSpeed)
        {
            currentSmoothRunSpeed = speed;
        }
    }

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
