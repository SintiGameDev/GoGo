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
    public float gravity = 30.0f;

    [Header("Camera Lean (Strafe Tilt)")]
    [Tooltip("Maximaler Roll-Winkel (Z-Achse) der Kamera bei vollem seitlichen Input")]
    public float maxLeanAngle = 4f;
    [Tooltip("Wie schnell die Kamera in den Lean-Winkel hinein/heraus lerpt")]
    public float leanSpeed = 8f;
    [Tooltip("Multiplier auf 'gravity' waehrend der Spieler nach oben fliegt (steigt)")]
    public float riseGravityMultiplier = 1.0f;
    [Tooltip("Multiplier auf 'gravity' waehrend der Spieler faellt -> hoeher = snappier, weniger floaty")]
    public float fallGravityMultiplier = 1.8f;
    [Tooltip("Zusaetzlicher Gravity-Multiplier, wenn die Jump-Taste frueh released wird (Variable Jump Height)")]
    public float lowJumpGravityMultiplier = 2.5f;

    [Header("Coyote Time / Jump Buffer")]
    [Tooltip("Sprung ist noch X Sekunden nach Verlassen des Bodens erlaubt")]
    public float coyoteTime = 0.12f;
    [Tooltip("Jump-Input wird X Sekunden vor Landung 'gemerkt' und beim Landen ausgefuehrt")]
    public float jumpBufferTime = 0.12f;

    [Header("Air Control")]
    [Tooltip("Beschleunigung in der Luft relativ zum Boden (1 = gleich, <1 = reduzierte Luft-Beschleunigung)")]
    [Range(0f, 1f)]
    public float airControlFactor = 0.7f;

    [Header("Momentum / Inertia")]
    [Tooltip("Wie schnell die horizontale Velocity dem Input-Ziel folgt (hoeher = direkter, niedriger = mehr Momentum-Erhalt)")]
    //[Tooltip("Beschleunigung in der Luft (absolut, vor airControlFactor)")]
    public float airAcceleration = 15f;

    [Header("Wall Jump Momentum")]
    [Tooltip("Wie viel der horizontalen Velocity vor dem Wall-Jump in den Wall-Jump-Impuls einfliesst (additiv)")]
    public float wallJumpMomentumRetain = 0.6f;

    [Header("Apex Hang Time")]
    [Tooltip("Geschwindigkeitsfenster um moveDirection.y == 0, in dem Hang Time greift")]
    public float apexThreshold = 1.5f;
    [Tooltip("Gravity-Multiplier waehrend der Apex Hang Time (z.B. 0.5 = halbe Gravity)")]
    public float apexGravityMultiplier = 0.5f;
    public Camera playerCamera;
    public float lookSpeed = 2.0f;
    public float lookXLimit = 45.0f;

    // Wall Grab Settings
    public float wallGrabDuration = 2.0f;
    public float normalMass = 1.0f;
    public float wallGrabMass = 0.1f;
    public LayerMask wallLayer;
    public GameObject colliderObject;
    [Tooltip("Wie stark der Input von der Wand weg zeigen muss (Dot-Product mit wallNormal), um den Grab aktiv zu verlassen")]
    [Range(0f, 1f)]
    public float wallGrabExitInputThreshold = 0.3f;
    [Tooltip("Sekunden nach aktivem Wall-Grab-Exit, in denen kein erneutes Einschnappen an derselben Wand moeglich ist")]
    public float wallGrabReentryCooldown = 0.25f;

    // FOV-EINSTELLUNGEN
    public float baseFOV = 60f;
    public float maxFOV = 90f;
    public float fovChangeDuration = 0.3f;
    public float fovSpeedThreshold = 15f;

    [Header("Speed Settings")]
    public float speedTransitionRate = 10f;
    [Tooltip("Maximale Gesamtgeschwindigkeit (x/y/z kombiniert), auf die moveDirection vor jedem Move gecapt wird. " +
             "Sollte klar ueber (runningSpeed * VelocityMultiplier.maxComboMultiplier) liegen, sonst killt der Cap " +
             "den Sinn des Combo-Systems. Dient nur als Sicherheitsnetz gegen Physik-Edge-Cases, nicht als Gameplay-Limit.")]
    public float maxTotalVelocity = 60f;

    CharacterController characterController;
    Rigidbody rb;
    Vector3 moveDirection = Vector3.zero;
    float rotationX = 0;
    float currentLeanAngle = 0f;

    [HideInInspector]
    public bool canMove = true;

    private bool isGrappling = false;
    private bool isWallGrabbing = false;
    private float wallGrabTimer = 0f;
    private Vector3 wallNormal;
    private float wallGrabReentryBlockedUntil = -999f;
    private WallGrabTrigger wallGrabTrigger;
    private VelocityMultiplier velocityMultiplier;
    private HeadBang headBang;

    private LTDescr fovTween;
    private float lastTargetFOV = 0f;

    private float lastGroundedTime = 0f;
    private bool wasGroundedLastFrame = false;

    // Variable Jump Height State
    private bool isJumpCut = false;
    private bool wasJumpingThisAirtime = false;

    // Jump Buffer State
    private float jumpBufferTimer = -999f;

    // Speed State
    private float targetWalkingSpeed;
    private float targetRunningSpeed;
    private float currentSmoothWalkSpeed;
    private float currentSmoothRunSpeed;

    void OnValidate()
    {
        // Verhindert, dass negative/0-Werte im Inspector den Spieler wieder schweben/fliegen lassen
        gravity = Mathf.Max(0.01f, gravity);
        riseGravityMultiplier = Mathf.Max(0f, riseGravityMultiplier);
        fallGravityMultiplier = Mathf.Max(0f, fallGravityMultiplier);
        lowJumpGravityMultiplier = Mathf.Max(0f, lowJumpGravityMultiplier);

        coyoteTime = Mathf.Max(0f, coyoteTime);
        jumpBufferTime = Mathf.Max(0f, jumpBufferTime);
        airControlFactor = Mathf.Clamp01(airControlFactor);
        airAcceleration = Mathf.Max(0.01f, airAcceleration);
        wallJumpMomentumRetain = Mathf.Clamp01(wallJumpMomentumRetain);
        apexThreshold = Mathf.Max(0f, apexThreshold);
        apexGravityMultiplier = Mathf.Max(0f, apexGravityMultiplier);

        maxLeanAngle = Mathf.Max(0f, maxLeanAngle);
        leanSpeed = Mathf.Max(0.01f, leanSpeed);

        wallGrabExitInputThreshold = Mathf.Clamp01(wallGrabExitInputThreshold);
        wallGrabReentryCooldown = Mathf.Max(0f, wallGrabReentryCooldown);
    }

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

        // Wall Grab: aktiv verlassen, wenn der Spieler per Input von der Wand weg steuert.
        // Dot-Product zwischen World-Space-Inputrichtung und wallNormal: negativ/kleiner als
        // -threshold heisst "zeigt von der Wand weg". Reine Maus-Drehung ohne Bewegungsinput
        // (inputX/Y nahe 0) zaehlt nicht als Exit-Absicht.
        if (isWallGrabbing)
        {
            Vector3 worldInputDir = (forward * inputX) + (right * inputY);
            if (worldInputDir.sqrMagnitude > 0.01f)
            {
                float awayFromWallDot = Vector3.Dot(worldInputDir.normalized, wallNormal);
                if (awayFromWallDot >= wallGrabExitInputThreshold)
                {
                    EndWallGrab();
                    wallGrabReentryBlockedUntil = Time.time + wallGrabReentryCooldown;
                }
            }
        }

        // Momentum/Inertia: horizontale Velocity folgt dem Input-Ziel statt hart gesetzt zu werden.
        // Am Boden schnellere Acceleration (direkter), in der Luft langsamer (Momentum-Erhalt + Air Control).
        // Wall-Grab: volle, ungebremste Air-Control, damit Maus/Input sofort durchschlaegt statt
        // "Panzer"-Gefuehl durch die normale Air-Acceleration-Bremse.
        Vector3 targetHorizontalVelocity = (forward * curSpeedX) + (right * curSpeedY);
        Vector3 currentHorizontalVelocity = new Vector3(moveDirection.x, 0f, moveDirection.z);

        Vector3 newHorizontalVelocity;
        if (characterController.isGrounded)
        {
            // Boden: instant, direkte Kontrolle -- kein Lerp/Trägheit beim normalen Laufen
            newHorizontalVelocity = targetHorizontalVelocity;
        }
        else if (isWallGrabbing)
        {
            // Wall Grab: volle, instante Kontrolle -- Maus/Input soll sofort durchschlagen,
            // kein "Panzer"-Gefuehl durch gebremste Air-Acceleration.
            newHorizontalVelocity = targetHorizontalVelocity;
        }
        else
        {
            // Luft: Momentum-Erhalt + Air Control, damit Sprünge/Wall-Jumps ihren Speed behalten
            float accel = airAcceleration * Mathf.Max(0.01f, airControlFactor);
            newHorizontalVelocity = Vector3.MoveTowards(currentHorizontalVelocity, targetHorizontalVelocity, accel * Time.deltaTime);
        }

        moveDirection = new Vector3(newHorizontalVelocity.x, movementDirectionY, newHorizontalVelocity.z);

        // FOV Update
        UpdateFOV();

        // Coyote Time: Sprung bleibt kurz nach Verlassen des Bodens erlaubt
        bool canUseCoyoteTime = !characterController.isGrounded && GetTimeSinceGrounded() <= coyoteTime;
        bool groundedOrCoyote = characterController.isGrounded || canUseCoyoteTime;

        // Jump Buffer: Input merken, falls Taste kurz vor dem Landen gedrueckt wurde
        if (Input.GetButtonDown("Jump"))
        {
            jumpBufferTimer = Time.time;
        }
        bool hasBufferedJump = (Time.time - jumpBufferTimer) <= jumpBufferTime;

        bool wantsToJump = Input.GetButtonDown("Jump") || hasBufferedJump;

        // Jump Logik
        if (wantsToJump && canMove && !isGrappling && groundedOrCoyote && !isWallGrabbing)
        {
            moveDirection.y = jumpSpeed;
            isJumpCut = false;
            wasJumpingThisAirtime = true;
            jumpBufferTimer = -999f;

            if (velocityMultiplier != null)
            {
                velocityMultiplier.OnJumpAction();
            }
        }
        else if (wantsToJump && canMove && !isGrappling && isWallGrabbing)
        {
            isJumpCut = false;
            wasJumpingThisAirtime = true;
            jumpBufferTimer = -999f;
            PerformWallJump();
        }
        else
        {
            moveDirection.y = movementDirectionY;
        }

        // Variable Jump Height: Taste fruehzeitig losgelassen waehrend Aufstieg -> Jump cutten.
        // wasJumpingThisAirtime verhindert, dass simples Fallen (z.B. von einer Plattform)
        // versehentlich als "early release" gewertet wird.
        if (Input.GetButtonUp("Jump") && wasJumpingThisAirtime && moveDirection.y > 0f)
        {
            isJumpCut = true;
        }

        if (characterController.isGrounded)
        {
            wasJumpingThisAirtime = false;
            isJumpCut = false;
        }

        // Gravity
        // Rise/Fall-Asymmetrie + Variable Jump Height + Apex Hang Time fuer "snappy" Platformer-Feel.
        // Wall-Grab behaelt seine eigene reduzierte Gravity und wird NICHT vom Fall-Multiplier ueberschrieben.
        if (!characterController.isGrounded && !isGrappling)
        {
            float currentGravity;
            bool isAtApex = Mathf.Abs(moveDirection.y) <= apexThreshold;

            if (isWallGrabbing)
            {
                currentGravity = gravity * 0.3f;
            }
            else if (isAtApex && !isJumpCut)
            {
                // Apex Hang Time: kurzes Zeitfenster um den Scheitelpunkt mit reduzierter Gravity
                // fuer praezisere Air-Korrekturen. Greift nicht bei Jump-Cut (soll dort schnell fallen).
                currentGravity = gravity * Mathf.Max(0f, apexGravityMultiplier);
            }
            else if (moveDirection.y > 0f)
            {
                // Steigend: normale (oder isJumpCut: verstaerkte) Gravity
                float riseMult = isJumpCut ? Mathf.Max(riseGravityMultiplier, lowJumpGravityMultiplier) : riseGravityMultiplier;
                currentGravity = gravity * Mathf.Max(0f, riseMult);
            }
            else
            {
                // Fallend: immer die staerkere Fall-Gravity
                currentGravity = gravity * Mathf.Max(0f, fallGravityMultiplier);
            }

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

            // Camera Lean: leichtes Tilt (Roll) in Strafe-Richtung fuer mehr Speed-Feedback.
            // Nutzt inputY (Horizontal-Achse) aus der Bewegungsberechnung dieses Frames.
            // Vorzeichen negativ, damit Lean bei A/Links nach links kippt (Roll-Konvention).
            float targetLean = -inputY * maxLeanAngle;
            currentLeanAngle = Mathf.Lerp(currentLeanAngle, targetLean, Time.deltaTime * leanSpeed);

            playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0, currentLeanAngle);
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
        if (Time.time < wallGrabReentryBlockedUntil)
        {
            // Spieler hat sich gerade erst aktiv von dieser Wand weg bewegt -> kein sofortiges Re-Snap
            return;
        }

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

        // Momentum-Erhalt: Ein Teil der horizontalen Geschwindigkeit VOR dem Wall-Jump
        // fliesst additiv in den Wall-Jump-Impuls ein, statt komplett verworfen zu werden.
        // Wichtig: vor jeglichem Reset auslesen, sonst ist die alte Velocity schon weg.
        Vector3 preJumpHorizontalVelocity = new Vector3(moveDirection.x, 0f, moveDirection.z);

        Vector3 wallJumpImpulse = jumpDirection * walkingSpeed * 0.5f;
        Vector3 retainedMomentum = preJumpHorizontalVelocity * Mathf.Clamp01(wallJumpMomentumRetain);

        moveDirection = wallJumpImpulse + retainedMomentum;
        moveDirection.y = wallJumpSpeed;

        // Gesamt-Cap bleibt respektiert, falls Momentum + Impuls ueber maxTotalVelocity hinausschiessen
        if (moveDirection.magnitude > maxTotalVelocity)
        {
            moveDirection = moveDirection.normalized * maxTotalVelocity;
        }

        if (velocityMultiplier != null)
        {
            velocityMultiplier.OnJumpAction();
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
