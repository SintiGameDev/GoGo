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

    [Header("Auto Wall Jump")]
    [Tooltip("Wenn aktiv: Spieler prallt bei Wandkontakt sofort automatisch ab (kein Wall-Grab/Hängen mehr). Wenn deaktiviert: altes Wall-Grab-Verhalten mit Timer + manuellem Jump-Input bleibt erhalten.")]
    public bool useAutoWallJump = true;

    [Tooltip("Zusätzlicher Speed-Bonus (Gesamt-3D-Geschwindigkeit), der ADDITIV auf den ursprünglichen Einfalls-Speed des Spielers aufgeschlagen wird (z.B. war man mit 12 m/s da, kommt man mit 12+X m/s aus dem Walljump raus). Die RICHTUNG ist eine echte 3D-Reflexion (Einfallswinkel = Ausfallswinkel) an der Wandnormale.")]
    public float autoWallJumpExtraSpeed = 4.0f;

    [Tooltip("Minimale horizontale Einfallsgeschwindigkeit, damit überhaupt reflektiert wird. Darunter (z.B. Spieler steht fast bewegungslos an der Wand) wird die Wandnormale direkt als Richtung genutzt, da eine Reflexion von ~0 keine sinnvolle Richtung ergibt.")]
    public float minIncomingSpeedForReflection = 0.5f;

    [Header("Auto Wall Jump - Steilerer Ausfallswinkel")]
    [Tooltip("Mindest-Steigungswinkel (in Grad, 0-90) der vor der Reflexion künstlich in die Einfallsrichtung eingemischt wird. Sorgt dafür, dass der Spieler steiler nach oben abprallt statt nur flach reflektiert zu werden, auch bei flachem Anlaufwinkel. 0 = reine, ungeänderte Reflexion.")]
    [Range(0f, 89f)]
    public float minLaunchAngle = 35f;

    [Header("Auto Wall Jump - Extra Höhenboost")]
    [Tooltip("Zusätzlicher Aufwärts-Boost (separat von der Reflexion), der nochmal oben auf die finale Y-Komponente jedes automatischen Walljumps aufgeschlagen wird")]
    public float wallJumpHeightBoost = 7.0f;

    [Tooltip("Mindestabstand zwischen zwei automatischen Walljumps (verhindert Mehrfachauslösung durch mehrere Collider-Hits im selben Frame/derselben Kontaktserie)")]
    public float autoWallJumpCooldown = 0.2f;

    [Tooltip("Wie lange der Abprall-Impuls über die normale Bewegungseingabe hinweg erhalten bleibt, bevor der Spieler wieder volle Kontrolle über die Bewegungsrichtung hat. Kurz halten für maximalen Flow/schnelle Kontrollübergabe.")]
    public float autoWallJumpMomentumDuration = 0.15f;

    [Header("Auto Wall Jump - Kamera-Rotation")]
    [Tooltip("Kamera/Blickrichtung beim Walljump automatisch zur neuen Sprungrichtung drehen")]
    public bool autoRotateCameraOnWallJump = true;

    [Tooltip("Dauer der automatischen Dreh-Animation zur neuen Sprungrichtung. Kurz halten, damit der Spieler schnell wieder selbst die Kamera übernehmen kann.")]
    public float autoWallJumpCameraTurnDuration = 0.1f;

    // FOV-EINSTELLUNGEN
    public float baseFOV = 60f;
    public float maxFOV = 90f;
    public float fovChangeDuration = 0.3f;
    public float fovSpeedThreshold = 15f;

    [Header("Speed Settings")]
    public float speedTransitionRate = 10f;
    [Tooltip("Maximale Gesamtgeschwindigkeit (x/y/z kombiniert), auf die moveDirection vor jedem Move gecapt wird")]
    public float maxTotalVelocity = 30f;

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

    private float lastAutoWallJumpTime = -999f;

    // Horizontaler Boost-Vektor des Auto-Walljumps, der über autoWallJumpMomentumDuration
    // ausklingt und additiv zur normalen Input-Bewegung gelegt wird (siehe Update()).
    private Vector3 autoWallJumpBoostVector = Vector3.zero;
    private float autoWallJumpBoostTimer = 0f;

    // Kamera-Auto-Rotation zur neuen Sprungrichtung nach einem Auto-Walljump.
    // Läuft parallel zur normalen Maussteuerung in Update() und blendet sich
    // über autoWallJumpCameraTurnDuration sanft ein bzw. wird von ihr überschrieben,
    // sobald sie abgelaufen ist - der Spieler übernimmt danach nahtlos wieder per Maus.
    private bool isAutoRotatingCamera = false;
    private float autoRotateYawStart = 0f;
    private float autoRotateYawTarget = 0f;
    private float autoRotateTimer = 0f;

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

        // Auto-Walljump-Boost: läuft additiv über die normale Input-Bewegung,
        // solange er noch nicht ausgeklungen ist. Klingt linear über
        // autoWallJumpMomentumDuration ab, statt abrupt zu verschwinden.
        if (autoWallJumpBoostTimer > 0f)
        {
            float fadeRatio = autoWallJumpMomentumDuration > 0f
                ? Mathf.Clamp01(autoWallJumpBoostTimer / autoWallJumpMomentumDuration)
                : 0f;

            moveDirection += autoWallJumpBoostVector * fadeRatio;

            autoWallJumpBoostTimer -= Time.deltaTime;
            if (autoWallJumpBoostTimer <= 0f)
            {
                autoWallJumpBoostTimer = 0f;
                autoWallJumpBoostVector = Vector3.zero;
            }
        }

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

            if (isAutoRotatingCamera)
            {
                // Während der kurzen Walljump-Dreh-Animation übernimmt
                // UpdateCameraAutoRotation() den Yaw-Anteil (inkl. Mausdelta),
                // um die normale additive Zeile unten nicht doppelt anzuwenden.
                UpdateCameraAutoRotation();
            }
            else
            {
                transform.rotation *= Quaternion.Euler(0, Input.GetAxis("Mouse X") * lookSpeed, 0);
            }
        }
    }

    /// <summary>
    /// Wertet die kurze Dreh-Animation zur Walljump-Sprungrichtung aus (siehe
    /// StartCameraAutoRotation). Interpoliert den Yaw-Anteil sanft zum Zielwinkel,
    /// ohne die Pitch-Rotation (rotationX) der Kamera zu beeinflussen.
    /// </summary>
    void UpdateCameraAutoRotation()
    {
        if (!isAutoRotatingCamera)
            return;

        // Mauseingabe während der Animation soll den Spieler tatsächlich mitsteuern
        // lassen statt überschrieben zu werden: das Maus-Yaw-Delta dieses Frames
        // (bereits oben auf transform.rotation angewendet) wird hier zusätzlich auf
        // Start- und Zielwinkel übertragen, sodass beide "mitwandern" und die
        // Interpolation relativ zur Mausbewegung konsistent bleibt.
        float mouseYawDelta = Input.GetAxis("Mouse X") * lookSpeed;
        autoRotateYawStart += mouseYawDelta;
        autoRotateYawTarget += mouseYawDelta;

        autoRotateTimer -= Time.deltaTime;

        float progress = autoWallJumpCameraTurnDuration > 0f
            ? 1f - Mathf.Clamp01(autoRotateTimer / autoWallJumpCameraTurnDuration)
            : 1f;

        float currentYaw = Mathf.LerpAngle(autoRotateYawStart, autoRotateYawTarget, progress);

        Vector3 currentEuler = transform.eulerAngles;
        transform.rotation = Quaternion.Euler(currentEuler.x, currentYaw, currentEuler.z);

        if (autoRotateTimer <= 0f)
        {
            isAutoRotatingCamera = false;
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
        if (useAutoWallJump)
        {
            // Neues Verhalten: sofortiger automatischer Abprall statt Hängen.
            // Cooldown verhindert, dass mehrere Collider-Hits derselben
            // Kontaktserie (z.B. Trigger + Collision im selben Frame) mehrfach
            // hintereinander einen Walljump auslösen.
            if (Time.time - lastAutoWallJumpTime < autoWallJumpCooldown)
                return;

            if (characterController.isGrounded)
                return;

            PerformAutoWallJump(normal);
            return;
        }

        // Altes Verhalten: Wall-Grab/Hängen mit Timer + manuellem Jump-Input
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

    /// <summary>
    /// Automatischer Abprall bei Wandkontakt (ersetzt das Wall-Grab/Hängen, solange
    /// useAutoWallJump aktiv ist). Die Richtung ist eine echte Reflexion der
    /// einfallenden horizontalen Bewegungsrichtung an der Wandnormale
    /// (Einfallswinkel = Ausfallswinkel, wie ein Ball): r = d - 2(d·n)n.
    /// Bei frontalem Aufprall (d zeigt direkt auf die Wand) ergibt das einen
    /// geraden Rückprall in die umgekehrte Anlaufrichtung. Bei seitlichem/
    /// streifendem Anlauf (d fast parallel zur Wand) wird die Bewegung fast
    /// unverändert fortgesetzt - genau das ermöglicht das seitliche Vorbeispringen.
    /// Zusätzlich gibt es bei JEDEM Auto-Walljump einen festen Extra-Höhenboost
    /// (wallJumpHeightBoost), unabhängig vom Aufprallwinkel.
    /// </summary>
    void PerformAutoWallJump(Vector3 normal)
    {
        Vector3 flatNormal = normal;
        flatNormal.y = 0f;
        flatNormal.Normalize();

        // Einfallende horizontale Bewegungsrichtung (vor dem Aufprall)
        Vector3 incomingHorizontal = new Vector3(moveDirection.x, 0f, moveDirection.z);
        float incomingSpeed = incomingHorizontal.magnitude;

        Vector3 incomingHorizontalDir;
        if (incomingSpeed >= minIncomingSpeedForReflection)
        {
            incomingHorizontalDir = incomingHorizontal / incomingSpeed;
        }
        else
        {
            // Spieler hatte kaum horizontale Geschwindigkeit (z.B. fast senkrecht
            // gegen die Wand gelaufen und sofort gestoppt) - eine Reflexion von
            // einem Nahezu-Null-Vektor ergibt keine sinnvolle Richtung, daher
            // stattdessen direkt von der Wand weg springen.
            incomingHorizontalDir = flatNormal;
        }

        // Mindest-Steigungswinkel künstlich in die Einfallsrichtung einmischen,
        // BEVOR reflektiert wird: die horizontale Richtung bleibt erhalten (kippt
        // nach oben), die Wandnormale selbst bleibt rein horizontal (vertikale
        // Wand). Da die Normale keine Y-Komponente hat, bleibt die nach oben
        // geneigte Y-Komponente der Einfallsrichtung bei der Reflexion erhalten -
        // das Ergebnis fliegt dadurch garantiert steiler nach oben heraus, auch
        // bei flachem/horizontalem Anlaufwinkel.
        float launchAngleRad = minLaunchAngle * Mathf.Deg2Rad;
        Vector3 tiltedIncoming = (incomingHorizontalDir * Mathf.Cos(launchAngleRad)) +
                                  (Vector3.up * Mathf.Sin(launchAngleRad));
        tiltedIncoming.Normalize();

        // Reflexion in 3D: r = d - 2(d·n)n (n bleibt horizontal, da vertikale Wand)
        float dot = Vector3.Dot(tiltedIncoming, flatNormal);
        Vector3 reflected = tiltedIncoming - 2f * dot * flatNormal;
        reflected.Normalize();

        // Gesamt-Speed = ursprünglicher Einfalls-Speed + fester Extra-Bonus
        // (additiv) - schnellerer Anlauf ergibt dadurch auch einen schnelleren,
        // weiteren Abprall, statt einem vom Speed unabhängigen Fixwert.
        float outgoingSpeed = incomingSpeed + autoWallJumpExtraSpeed;
        Vector3 launchVelocity = reflected * outgoingSpeed;

        // Zusätzlicher Höhenboost (separat von der Reflexion) oben auf die Y-Komponente
        launchVelocity.y += wallJumpHeightBoost;

        // Y-Komponente sofort setzen (läuft danach ganz normal über die bestehende
        // Schwerkraft-Logik in Update() aus, wie bei jedem anderen Sprung auch).
        moveDirection.y = launchVelocity.y;

        // Horizontale Komponente läuft über den ausklingenden Boost-Vektor, da
        // moveDirection.x/.z in Update() jeden Frame neu aus der Bewegungseingabe
        // berechnet wird und einen einmaligen Direktwert sofort überschreiben würde.
        autoWallJumpBoostVector = new Vector3(launchVelocity.x, 0f, launchVelocity.z);
        autoWallJumpBoostTimer = autoWallJumpMomentumDuration;

        lastAutoWallJumpTime = Time.time;

        if (autoRotateCameraOnWallJump)
        {
            StartCameraAutoRotation(reflected);
        }

        if (velocityMultiplier != null)
        {
            velocityMultiplier.OnJumpAction();
        }

        if (headBang != null)
        {
            headBang.OnWallGrabImpact();
        }

        Debug.Log($"🧱 Auto Wall Jump! Reflektierte Richtung: {reflected} | Einfalls-Speed: {incomingSpeed:F1} → Abprall-Speed: {outgoingSpeed:F1} | Höhenboost: +{wallJumpHeightBoost:F1} | Gesamt-Y: {moveDirection.y:F1}");
    }

    /// <summary>
    /// Startet eine kurze, smoothe Dreh-Animation der Blickrichtung zur neuen
    /// Sprungrichtung. Läuft parallel zur normalen Mausrotation in Update() und
    /// übergibt nach Ablauf nahtlos zurück an die Spielersteuerung.
    /// </summary>
    void StartCameraAutoRotation(Vector3 launchDirection)
    {
        // Nur die horizontale (X/Z) Komponente bestimmt den Yaw - eine eventuelle
        // Y-Komponente (z.B. durch den künstlichen Steigungswinkel) wird hier
        // automatisch ignoriert, da Atan2 nur X/Z auswertet.
        if (new Vector2(launchDirection.x, launchDirection.z).sqrMagnitude < 0.0001f)
            return;

        autoRotateYawStart = transform.eulerAngles.y;
        autoRotateYawTarget = Mathf.Atan2(launchDirection.x, launchDirection.z) * Mathf.Rad2Deg;
        autoRotateTimer = autoWallJumpCameraTurnDuration;
        isAutoRotatingCamera = autoWallJumpCameraTurnDuration > 0f;

        if (!isAutoRotatingCamera)
        {
            // Dauer 0 -> sofortiger Snap statt Animation
            transform.rotation = Quaternion.Euler(0f, autoRotateYawTarget, 0f);
        }
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
