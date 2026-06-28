using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SurfController : MonoBehaviour
{
    [Header("Surf Acceleration Settings")]
    public float baseAcceleration = 2.0f;
    public float maxAccelerationDown = 8.0f; // Für horizontale Speed
    public float maxAccelerationUp = 6.0f; // Für aufwärts Bewegung (erhöht!)
    public float verticalBoostMultiplier = 0.3f; // Wie viel vertikale Velocity beim nach-oben-schauen
    public float maxSurfSpeed = 50f;
    public float softCapThreshold = 0.8f; // Bei 80% von maxSpeed beginnt Soft Cap
    public float surfSensitivity = 1.0f;

    [Header("Camera Angle Settings")]
    public float downAngleThreshold = 20f; // Früher triggern
    public float upAngleThreshold = -20f; // Früher triggern
    public float maxDownAngle = 80f;
    public float maxUpAngle = -80f;

    [Header("Air Strafe Settings")]
    public bool enableAirStrafe = true;
    public float airStrafeAcceleration = 3.0f;
    public float airStrafeMaxSpeed = 40f;

    [Header("Speed Decay Settings")]
    public bool enableSurfing = true;
    public float speedDecayRate = 2.5f;
    public float speedFloorPercentage = 0.3f; // 30% der max Speed bleiben länger erhalten
    public AnimationCurve decayCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("Stability Settings")]
    public float dragCoefficient = 0.02f; // Natürliche Speed-Dämpfung bei hohen Geschwindigkeiten
    public bool useAsymptoticSpeedCap = true;

    [Header("Debug")]
    public bool showDebugInfo = false;

    private SC_FPSController fpsController;
    private VelocityMultiplier velocityMultiplier;
    private Camera playerCamera;
    private CharacterController characterController;

    private float currentSurfSpeed = 0f;
    private float verticalBoostVelocity = 0f; // Separate vertikale Komponente
    private bool isSurfing = false;

    private float originalWalkingSpeed;
    private float originalRunningSpeed;

    // Air Strafe State
    private float lastYRotation = 0f;

    void Start()
    {
        fpsController = GetComponent<SC_FPSController>();
        velocityMultiplier = GetComponent<VelocityMultiplier>();
        characterController = GetComponent<CharacterController>();

        if (fpsController == null)
        {
            Debug.LogError("SurfController benötigt SC_FPSController Component!");
        }

        if (fpsController != null && fpsController.playerCamera != null)
        {
            playerCamera = fpsController.playerCamera;
            lastYRotation = transform.eulerAngles.y;
        }
        else
        {
            Debug.LogError("SurfController benötigt Player Camera Referenz!");
        }

        if (fpsController != null)
        {
            originalWalkingSpeed = fpsController.walkingSpeed;
            originalRunningSpeed = fpsController.runningSpeed;
        }
    }

    void Update()
    {
        if (!enableSurfing || fpsController == null || playerCamera == null)
            return;

        bool isWallGrabbing = IsWallGrabbing();
        bool isGrounded = characterController != null && characterController.isGrounded;

        if (isWallGrabbing)
        {
            if (!isSurfing)
            {
                StartSurfing();
            }

            UpdateSurfing();
        }
        else
        {
            if (isSurfing)
            {
                EndSurfing();
            }
            else if (currentSurfSpeed > 0f)
            {
                DecaySurfSpeed();
            }

            // Air Strafe wenn in der Luft (aber nicht am Wall Grab)
            if (!isGrounded && enableAirStrafe)
            {
                UpdateAirStrafe();
            }
        }

        // Vertikalen Boost anwenden (wenn vorhanden)
        if (verticalBoostVelocity != 0f && !isGrounded)
        {
            ApplyVerticalBoost();
        }
    }

    bool IsWallGrabbing()
    {
        return fpsController != null && fpsController.IsWallGrabbing();
    }

    void StartSurfing()
    {
        isSurfing = true;
        Debug.Log("Surfing started!");
    }

    void UpdateSurfing()
    {
        float cameraPitch = GetNormalizedCameraPitch();
        float acceleration = CalculateAcceleration(cameraPitch);

        // Beschleunigung anwenden
        currentSurfSpeed += acceleration * Time.deltaTime;

        // Soft Cap: Je näher an maxSpeed, desto weniger Beschleunigung
        if (useAsymptoticSpeedCap && currentSurfSpeed > maxSurfSpeed * softCapThreshold)
        {
            float excessSpeed = currentSurfSpeed - (maxSurfSpeed * softCapThreshold);
            float capRange = maxSurfSpeed - (maxSurfSpeed * softCapThreshold);
            float capFactor = 1f - Mathf.Clamp01(excessSpeed / capRange);

            currentSurfSpeed = Mathf.Lerp(currentSurfSpeed, maxSurfSpeed, Time.deltaTime * 2f);
        }

        // Hard Cap als Sicherheit
        currentSurfSpeed = Mathf.Clamp(currentSurfSpeed, 0f, maxSurfSpeed);

        // Natürlicher Drag bei hohen Geschwindigkeiten
        if (currentSurfSpeed > originalWalkingSpeed * 2f)
        {
            float drag = dragCoefficient * currentSurfSpeed * Time.deltaTime;
            currentSurfSpeed -= drag;
        }

        ApplySurfSpeed();
    }

    float CalculateAcceleration(float cameraPitch)
    {
        float totalAcceleration = baseAcceleration;

        // NEUE LOGIK: Nach UNTEN = Horizontale Speed, Nach OBEN = Vertikale + Horizontale Speed

        if (cameraPitch >= downAngleThreshold)
        {
            // Nach UNTEN schauen = Mehr horizontale Geschwindigkeit
            float normalizedAngle = Mathf.Clamp01((cameraPitch - downAngleThreshold) /
                                                  (maxDownAngle - downAngleThreshold));

            float extraAcceleration = maxAccelerationDown * normalizedAngle * surfSensitivity;
            totalAcceleration += extraAcceleration;

            // Vertikale Komponente reduzieren
            verticalBoostVelocity = Mathf.Lerp(verticalBoostVelocity, 0f, Time.deltaTime * 3f);
        }
        else if (cameraPitch <= upAngleThreshold)
        {
            // Nach OBEN schauen = Horizontale Speed + Vertikaler Boost
            float normalizedAngle = Mathf.Clamp01((upAngleThreshold - cameraPitch) /
                                                  (upAngleThreshold - maxUpAngle));

            // Horizontale Beschleunigung (etwas weniger als nach unten)
            float extraAcceleration = maxAccelerationUp * normalizedAngle * surfSensitivity;
            totalAcceleration += extraAcceleration;

            // WICHTIG: Vertikale Velocity hinzufügen (nach oben)
            float verticalBoost = maxAccelerationUp * normalizedAngle * verticalBoostMultiplier * Time.deltaTime;
            verticalBoostVelocity += verticalBoost;
            verticalBoostVelocity = Mathf.Clamp(verticalBoostVelocity, 0f, 15f); // Max upward velocity
        }
        else
        {
            // Neutral: Basis-Beschleunigung, vertikale Velocity decay
            verticalBoostVelocity = Mathf.Lerp(verticalBoostVelocity, 0f, Time.deltaTime * 2f);
        }

        return totalAcceleration;
    }

    void UpdateAirStrafe()
    {
        // Air Strafe: Seitliche Inputs + Kamera-Drehung = Extra Speed
        float horizontalInput = Input.GetAxis("Horizontal");

        if (Mathf.Abs(horizontalInput) > 0.1f)
        {
            float currentYRotation = transform.eulerAngles.y;
            float rotationDelta = Mathf.DeltaAngle(lastYRotation, currentYRotation);

            // Spieler dreht sich in Richtung der Bewegung
            bool turningRight = rotationDelta > 0;
            bool movingRight = horizontalInput > 0;

            // Wenn Bewegungsrichtung und Drehung übereinstimmen = Beschleunigung
            if ((turningRight && movingRight) || (!turningRight && !movingRight))
            {
                float strafeBoost = Mathf.Abs(rotationDelta) * airStrafeAcceleration * Time.deltaTime;
                currentSurfSpeed += strafeBoost;
                currentSurfSpeed = Mathf.Min(currentSurfSpeed, airStrafeMaxSpeed);

                ApplySurfSpeed();
            }

            lastYRotation = currentYRotation;
        }
    }

    void ApplyVerticalBoost()
    {
        if (fpsController == null) return;

        // Vertikale Velocity direkt an CharacterController übergeben
        Vector3 moveDirection = characterController.velocity;
        moveDirection.y += verticalBoostVelocity;

        // Decay über Zeit
        verticalBoostVelocity = Mathf.Lerp(verticalBoostVelocity, 0f, Time.deltaTime * 5f);

        // Note: Dies müsste idealerweise in SC_FPSController.Update() integriert werden
        // Für jetzt als Konzept-Demo
    }

    void ApplySurfSpeed()
    {
        if (fpsController == null) return;

        float surfMultiplier = 1.0f + (currentSurfSpeed / originalWalkingSpeed);

        float baseWalkingSpeed = originalWalkingSpeed;
        float baseRunningSpeed = originalRunningSpeed;

        if (velocityMultiplier != null && velocityMultiplier.IsBoosting())
        {
            float velocityBoost = velocityMultiplier.GetCurrentMultiplier();
            baseWalkingSpeed = originalWalkingSpeed * velocityBoost;
            baseRunningSpeed = originalRunningSpeed * velocityBoost;
        }

        fpsController.walkingSpeed = baseWalkingSpeed * surfMultiplier;
        fpsController.runningSpeed = baseRunningSpeed * surfMultiplier;
    }

    void DecaySurfSpeed()
    {
        // Speed Floor: 30% der Max-Speed bleiben länger erhalten
        float speedFloor = maxSurfSpeed * speedFloorPercentage;

        if (currentSurfSpeed > speedFloor)
        {
            // Über Speed Floor: Normale Decay
            float normalizedSpeed = Mathf.InverseLerp(speedFloor, maxSurfSpeed, currentSurfSpeed);
            float decayMultiplier = decayCurve.Evaluate(normalizedSpeed);

            currentSurfSpeed -= speedDecayRate * decayMultiplier * Time.deltaTime;
            currentSurfSpeed = Mathf.Max(currentSurfSpeed, speedFloor);
        }
        else
        {
            // Unter Speed Floor: Langsamer Decay
            currentSurfSpeed -= (speedDecayRate * 0.3f) * Time.deltaTime;
            currentSurfSpeed = Mathf.Max(0f, currentSurfSpeed);
        }

        if (currentSurfSpeed > 0f)
        {
            ApplySurfSpeed();
        }
        else
        {
            ResetToOriginalSpeed();
        }
    }

    void EndSurfing()
    {
        isSurfing = false;
        Debug.Log($"Surfing ended! Final speed: {currentSurfSpeed:F2}");
    }

    void ResetToOriginalSpeed()
    {
        if (fpsController == null) return;

        float baseWalkingSpeed = originalWalkingSpeed;
        float baseRunningSpeed = originalRunningSpeed;

        if (velocityMultiplier != null && velocityMultiplier.IsBoosting())
        {
            float velocityBoost = velocityMultiplier.GetCurrentMultiplier();
            baseWalkingSpeed = originalWalkingSpeed * velocityBoost;
            baseRunningSpeed = originalRunningSpeed * velocityBoost;
        }

        fpsController.walkingSpeed = baseWalkingSpeed;
        fpsController.runningSpeed = baseRunningSpeed;
    }

    float GetNormalizedCameraPitch()
    {
        float cameraPitch = playerCamera.transform.localEulerAngles.x;
        if (cameraPitch > 180f)
        {
            cameraPitch -= 360f;
        }
        return cameraPitch;
    }

    // Public Getters
    public bool IsSurfing() => isSurfing;
    public float GetCurrentSurfSpeed() => currentSurfSpeed;
    public float GetCurrentSpeedMultiplier() => 1.0f + (currentSurfSpeed / originalWalkingSpeed);
    public float GetVerticalBoost() => verticalBoostVelocity;

    void OnGUI()
    {
        if (!showDebugInfo)
            return;

        if ((isSurfing || currentSurfSpeed > 0) && enableSurfing)
        {
            float cameraPitch = GetNormalizedCameraPitch();
            float currentAcceleration = isSurfing ? CalculateAcceleration(cameraPitch) : 0f;
            float speedMultiplier = GetCurrentSpeedMultiplier();

            GUI.Label(new Rect(10, 200, 500, 20), $"SURF SPEED: {currentSurfSpeed:F1} m/s | Multiplier: {speedMultiplier:F2}x");

            if (isSurfing)
            {
                GUI.Label(new Rect(10, 220, 500, 20), $"Angle: {cameraPitch:F1}° | Acceleration: {currentAcceleration:F1} m/s²");

                string surfStatus = "BASE ACCELERATION";
                Color statusColor = Color.yellow;

                if (cameraPitch >= downAngleThreshold)
                {
                    surfStatus = "HORIZONTAL SPEED ↓↓ (DOWN)";
                    statusColor = Color.green;
                }
                else if (cameraPitch <= upAngleThreshold)
                {
                    surfStatus = "UPWARD BOOST ↑↑ (UP)";
                    statusColor = Color.cyan;
                }

                GUI.color = statusColor;
                GUI.Label(new Rect(10, 240, 400, 20), surfStatus);
                GUI.color = Color.white;

                if (verticalBoostVelocity > 0.1f)
                {
                    GUI.color = Color.magenta;
                    GUI.Label(new Rect(10, 260, 400, 20), $"Vertical Boost: {verticalBoostVelocity:F2} m/s");
                    GUI.color = Color.white;
                }
            }
            else
            {
                GUI.color = Color.red;
                GUI.Label(new Rect(10, 220, 400, 20), "SPEED DECAYING");
                GUI.color = Color.white;
            }

            // Air Strafe Indicator
            if (!characterController.isGrounded && !isSurfing && enableAirStrafe)
            {
                GUI.color = Color.yellow;
                GUI.Label(new Rect(10, 280, 400, 20), "AIR STRAFE ACTIVE");
                GUI.color = Color.white;
            }
        }
    }

    void OnDrawGizmos()
    {
        if (!enableSurfing || playerCamera == null) return;

        if (isSurfing || currentSurfSpeed > 0)
        {
            float speedRatio = currentSurfSpeed / maxSurfSpeed;
            Vector3 velocityDirection = characterController != null ?
                characterController.velocity.normalized : Vector3.forward;

            Gizmos.color = Color.Lerp(Color.yellow, Color.green, speedRatio);
            Gizmos.DrawRay(transform.position, velocityDirection * (2f + speedRatio * 3f));
        }

        // Vertikaler Boost Visualisierung
        if (verticalBoostVelocity > 0.1f)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawRay(transform.position, Vector3.up * verticalBoostVelocity * 0.5f);
        }

        if (isSurfing)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawRay(transform.position + Vector3.up * 0.5f, playerCamera.transform.forward * 2f);
        }
    }

    // Public Reset Methode (wird vom FPSController aufgerufen)
    public void ResetSurfSpeed()
    {
        if (currentSurfSpeed <= 0f && verticalBoostVelocity <= 0f)
            return; // Nichts zu resetten

        Debug.Log($"💥 SurfController: Reset - Speed war {currentSurfSpeed:F1} m/s");

        currentSurfSpeed = 0f;
        verticalBoostVelocity = 0f;
        isSurfing = false;

        ResetToOriginalSpeed();
    }

}