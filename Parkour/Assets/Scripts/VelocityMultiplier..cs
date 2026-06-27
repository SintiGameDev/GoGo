using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VelocityMultiplier : MonoBehaviour
{
    [Header("Speed Boost Settings")]
    public float maxSpeedBoost = 2.5f;
    public float minSpeedBoost = 1.0f;
    public float boostDuration = 1.5f;

    [Header("Timing Settings")]
    public float optimalReactionTime = 0.1f;
    public AnimationCurve boostCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("Bunny Hop Settings")]
    public float bunnyHopWindow = 0.3f;
    public float bunnyHopMaxBoost = 2.0f;
    public float bunnyHopMinBoost = 1.0f;
    public float bunnyHopOptimalTime = 0.05f;
    public bool bunnyHopStacksWithWallJump = true;

    // NEUE: Speed Preservation
    [Header("Speed Preservation")]
    public bool preserveCurrentSpeed = true; // Behält aktuelle Speed bei Bunny Hop
    public float speedPreservationBonus = 1.1f; // 10% Speed-Boost zusätzlich
    public float minSpeedForPreservation = 10f; // Ab dieser Speed wird preserved
    public bool alwaysUseHigherSpeed = true; // Nimmt immer die höhere von beiden Speeds

    [Header("Smooth Transition Settings")]
    public bool useSmoothTransitions = true;
    public float boostFadeOutDuration = 0.5f;
    public AnimationCurve fadeOutCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    public bool preventBoostEndInAir = true;

    private SC_FPSController fpsController;
    private SurfController surfController; // NEUE Referenz
    private CharacterController characterController; // NEUE Referenz

    private float currentSpeedMultiplier = 1.0f;
    private float previousSpeedMultiplier = 1.0f;
    private float boostTimer = 0f;
    private bool isBoosting = false;

    private bool isFadingOut = false;
    private float fadeOutTimer = 0f;
    private float fadeStartMultiplier = 1.0f;

    private float wallGrabStartTime = 0f;
    private bool isTrackingWallGrab = false;

    private float landingTime = 0f;
    private bool isInBunnyHopWindow = false;
    private bool wasInAir = false;

    // NEUE: Speed Tracking
    private float currentActualSpeed = 0f; // Aktuelle echte Geschwindigkeit
    private float speedAtLanding = 0f; // Geschwindigkeit beim Landen

    private float originalWalkingSpeed;
    private float originalRunningSpeed;

    void Start()
    {
        fpsController = GetComponent<SC_FPSController>();
        surfController = GetComponent<SurfController>();
        characterController = GetComponent<CharacterController>();

        if (fpsController == null)
        {
            Debug.LogError("VelocityMultiplier benötigt SC_FPSController Component!");
            return;
        }

        originalWalkingSpeed = fpsController.walkingSpeed;
        originalRunningSpeed = fpsController.runningSpeed;
    }

    void Update()
    {
        if (fpsController == null) return;

        CharacterController cc = characterController != null ? characterController : fpsController.GetComponent<CharacterController>();
        if (cc == null) return;

        // NEUE: Tracking der aktuellen Geschwindigkeit
        UpdateCurrentSpeed(cc);

        // Fade Out Logic
        if (isFadingOut)
        {
            fadeOutTimer += Time.deltaTime;
            float fadeProgress = Mathf.Clamp01(fadeOutTimer / boostFadeOutDuration);
            float curveValue = fadeOutCurve.Evaluate(fadeProgress);

            currentSpeedMultiplier = Mathf.Lerp(fadeStartMultiplier, 1.0f, curveValue);
            ApplySpeedBoost();

            if (fadeProgress >= 1.0f)
            {
                CompleteFadeOut();
            }
        }
        else if (isBoosting)
        {
            bool shouldCountDown = cc.isGrounded || !preventBoostEndInAir;

            if (shouldCountDown)
            {
                boostTimer -= Time.deltaTime;

                if (boostTimer <= 0f)
                {
                    if (useSmoothTransitions)
                    {
                        StartFadeOut();
                    }
                    else
                    {
                        EndSpeedBoost();
                    }
                }
            }
        }

        CheckBunnyHopWindow();
    }

    // NEUE: Berechnet aktuelle echte Geschwindigkeit
    void UpdateCurrentSpeed(CharacterController cc)
    {
        Vector3 horizontalVelocity = new Vector3(cc.velocity.x, 0, cc.velocity.z);
        currentActualSpeed = horizontalVelocity.magnitude;
    }

    void CheckBunnyHopWindow()
    {
        CharacterController cc = characterController != null ? characterController : fpsController.GetComponent<CharacterController>();

        if (cc != null)
        {
            if (!cc.isGrounded && !wasInAir)
            {
                wasInAir = true;
            }

            if (cc.isGrounded && wasInAir)
            {
                OnPlayerLanded();
                wasInAir = false;
            }

            if (isInBunnyHopWindow)
            {
                float timeSinceLanding = Time.time - landingTime;

                if (timeSinceLanding > bunnyHopWindow)
                {
                    isInBunnyHopWindow = false;
                }
            }
        }
    }

    void OnPlayerLanded()
    {
        landingTime = Time.time;
        isInBunnyHopWindow = true;
        speedAtLanding = currentActualSpeed; // NEUE: Speed beim Landen speichern

        Debug.Log($"Player landed - Speed: {speedAtLanding:F1} m/s | Bunny Hop window active");
    }

    public void OnJump()
    {
        if (isInBunnyHopWindow)
        {
            float reactionTime = Time.time - landingTime;
            CalculateAndApplyBunnyHopBoost(reactionTime);
            isInBunnyHopWindow = false;
        }
    }

    public void OnWallGrabStart()
    {
        wallGrabStartTime = Time.time;
        isTrackingWallGrab = true;
        Debug.Log("VelocityMultiplier: Wall Grab tracking started");
    }

    public void OnWallJump()
    {
        if (isTrackingWallGrab)
        {
            float deltaTime = Time.time - wallGrabStartTime;
            CalculateAndApplyWallJumpBoost(deltaTime);
            isTrackingWallGrab = false;
        }
    }

    public void OnWallGrabEnd()
    {
        isTrackingWallGrab = false;
        Debug.Log("VelocityMultiplier: Wall Grab ended without jump");
    }

    void CalculateAndApplyBunnyHopBoost(float reactionTime)
    {
        float normalizedTime = Mathf.Clamp01(reactionTime / bunnyHopWindow);
        float boostStrength = 1f - normalizedTime;
        boostStrength = boostCurve.Evaluate(boostStrength);

        if (reactionTime <= bunnyHopOptimalTime)
        {
            boostStrength = 1f;
        }

        // Basis Bunny Hop Multiplier berechnen
        float bunnyHopMultiplier = Mathf.Lerp(bunnyHopMinBoost, bunnyHopMaxBoost, boostStrength);
        float newMultiplier = bunnyHopMultiplier;

        // NEUE: Speed Preservation Logic
        if (preserveCurrentSpeed && speedAtLanding >= minSpeedForPreservation)
        {
            // Berechne was der neue Multiplier ergeben würde
            float bunnyHopTargetSpeed = originalWalkingSpeed * bunnyHopMultiplier;

            // Berechne aktuellen effektiven Multiplier basierend auf echter Speed
            float currentEffectiveMultiplier = speedAtLanding / originalWalkingSpeed;

            Debug.Log($"Bunny Hop Analysis:");
            Debug.Log($"  Landing Speed: {speedAtLanding:F1} m/s");
            Debug.Log($"  Current Effective Multiplier: {currentEffectiveMultiplier:F2}x");
            Debug.Log($"  Bunny Hop would give: {bunnyHopTargetSpeed:F1} m/s ({bunnyHopMultiplier:F2}x)");

            if (alwaysUseHigherSpeed)
            {
                // Nimm die höhere von beiden Speeds
                if (currentEffectiveMultiplier > bunnyHopMultiplier)
                {
                    // Aktuelle Speed ist höher - behalte sie und füge Bonus hinzu
                    newMultiplier = currentEffectiveMultiplier * speedPreservationBonus;
                    Debug.Log($"  → Preserving higher speed: {newMultiplier:F2}x (+{speedPreservationBonus:F2}x bonus)");
                }
                else
                {
                    // Bunny Hop Speed ist höher - nutze sie
                    newMultiplier = bunnyHopMultiplier;
                    Debug.Log($"  → Using Bunny Hop multiplier: {newMultiplier:F2}x");
                }
            }
            else
            {
                // Immer preservieren und Bonus addieren
                newMultiplier = currentEffectiveMultiplier * speedPreservationBonus;
                Debug.Log($"  → Preserving and boosting: {newMultiplier:F2}x");
            }

            // Cap auf Maximum
            newMultiplier = Mathf.Min(newMultiplier, maxSpeedBoost * 2f); // Höherer Cap für Preservation
        }

        // Stacking Logic (falls bereits ein Boost aktiv ist)
        if (bunnyHopStacksWithWallJump && isBoosting)
        {
            // Nur stacken wenn es die Speed erhöht
            float combinedMultiplier = currentSpeedMultiplier * bunnyHopMultiplier;

            if (combinedMultiplier > newMultiplier)
            {
                newMultiplier = Mathf.Min(combinedMultiplier, maxSpeedBoost * 2f);
                Debug.Log($"  → Stacking boost: {newMultiplier:F2}x");
            }
        }

        currentSpeedMultiplier = newMultiplier;

        // Cancel Fade Out
        if (isFadingOut)
        {
            CancelFadeOut();
        }

        isBoosting = true;
        boostTimer = boostDuration;
        ApplySpeedBoost();

        // Final Speed ausgeben
        float finalSpeed = originalWalkingSpeed * currentSpeedMultiplier;
        Debug.Log($"✅ Bunny Hop Complete! Multiplier: {currentSpeedMultiplier:F2}x | Speed: {finalSpeed:F1} m/s");
    }

    void CalculateAndApplyWallJumpBoost(float reactionTime)
    {
        float normalizedTime = Mathf.Clamp01(reactionTime / fpsController.wallGrabDuration);
        float boostStrength = 1f - normalizedTime;
        boostStrength = boostCurve.Evaluate(boostStrength);

        if (reactionTime <= optimalReactionTime)
        {
            boostStrength = 1f;
        }

        float wallJumpMultiplier = Mathf.Lerp(minSpeedBoost, maxSpeedBoost, boostStrength);
        float newMultiplier = wallJumpMultiplier;

        // NEUE: Speed Preservation auch beim Wall Jump
        if (preserveCurrentSpeed && currentActualSpeed >= minSpeedForPreservation)
        {
            float currentEffectiveMultiplier = currentActualSpeed / originalWalkingSpeed;

            Debug.Log($"Wall Jump Analysis:");
            Debug.Log($"  Current Speed: {currentActualSpeed:F1} m/s");
            Debug.Log($"  Current Effective Multiplier: {currentEffectiveMultiplier:F2}x");
            Debug.Log($"  Wall Jump Multiplier: {wallJumpMultiplier:F2}x");

            if (alwaysUseHigherSpeed)
            {
                if (currentEffectiveMultiplier > wallJumpMultiplier)
                {
                    newMultiplier = currentEffectiveMultiplier * speedPreservationBonus;
                    Debug.Log($"  → Preserving higher speed: {newMultiplier:F2}x");
                }
                else
                {
                    newMultiplier = wallJumpMultiplier;
                    Debug.Log($"  → Using Wall Jump multiplier: {newMultiplier:F2}x");
                }
            }
            else
            {
                newMultiplier = currentEffectiveMultiplier * speedPreservationBonus;
            }

            newMultiplier = Mathf.Min(newMultiplier, maxSpeedBoost * 2f);
        }

        // Stacking
        if (bunnyHopStacksWithWallJump && isBoosting)
        {
            float combinedMultiplier = currentSpeedMultiplier * wallJumpMultiplier;

            if (combinedMultiplier > newMultiplier)
            {
                newMultiplier = Mathf.Min(combinedMultiplier, maxSpeedBoost * 2.5f);
                Debug.Log($"  → Stacking: {newMultiplier:F2}x");
            }
        }

        currentSpeedMultiplier = newMultiplier;

        if (isFadingOut)
        {
            CancelFadeOut();
        }

        isBoosting = true;
        boostTimer = boostDuration;
        ApplySpeedBoost();

        float finalSpeed = originalWalkingSpeed * currentSpeedMultiplier;
        Debug.Log($"✅ Wall Jump Complete! Multiplier: {currentSpeedMultiplier:F2}x | Speed: {finalSpeed:F1} m/s");
    }

    void ApplySpeedBoost()
    {
        if (fpsController == null) return;

        float boostedWalkSpeed = originalWalkingSpeed * currentSpeedMultiplier;
        float boostedRunSpeed = originalRunningSpeed * currentSpeedMultiplier;

        fpsController.SetTargetWalkingSpeed(boostedWalkSpeed);
        fpsController.SetTargetRunningSpeed(boostedRunSpeed);

        previousSpeedMultiplier = currentSpeedMultiplier;
    }

    void StartFadeOut()
    {
        isFadingOut = true;
        fadeOutTimer = 0f;
        fadeStartMultiplier = currentSpeedMultiplier;
        isBoosting = false;

        Debug.Log($"Starting smooth fade out from {fadeStartMultiplier:F2}x to 1.0x");
    }

    void CancelFadeOut()
    {
        isFadingOut = false;
        fadeOutTimer = 0f;
        Debug.Log("Fade out cancelled - new boost applied");
    }

    void CompleteFadeOut()
    {
        isFadingOut = false;
        currentSpeedMultiplier = 1.0f;
        previousSpeedMultiplier = 1.0f;

        fpsController.SetTargetWalkingSpeed(originalWalkingSpeed);
        fpsController.SetTargetRunningSpeed(originalRunningSpeed);

        Debug.Log("Fade out completed - speed reset to normal");
    }

    void EndSpeedBoost()
    {
        if (fpsController != null)
        {
            fpsController.SetTargetWalkingSpeed(originalWalkingSpeed);
            fpsController.SetTargetRunningSpeed(originalRunningSpeed);

            currentSpeedMultiplier = 1.0f;
            previousSpeedMultiplier = 1.0f;
            isBoosting = false;

            Debug.Log("Speed Boost ended (instant)");
        }
    }

    public float GetCurrentMultiplier()
    {
        return isBoosting || isFadingOut ? currentSpeedMultiplier : 1.0f;
    }

    public float GetRemainingBoostTime()
    {
        if (isFadingOut)
        {
            return boostFadeOutDuration - fadeOutTimer;
        }
        return isBoosting ? boostTimer : 0f;
    }

    public bool IsBoosting()
    {
        return isBoosting || isFadingOut;
    }

    public float GetBunnyHopWindowTime()
    {
        if (isInBunnyHopWindow)
        {
            return bunnyHopWindow - (Time.time - landingTime);
        }
        return 0f;
    }

    // NEUE: Public Getter für andere Scripts
    public float GetCurrentActualSpeed()
    {
        return currentActualSpeed;
    }

    public float GetSpeedAtLanding()
    {
        return speedAtLanding;
    }

    void OnGUI()
    {
        // Aktueller Boost Status
        if (isBoosting || isFadingOut)
        {
            string status = isFadingOut ? "FADING OUT" : "ACTIVE";
            GUI.color = isFadingOut ? Color.yellow : Color.green;
            GUI.Label(new Rect(10, 40, 500, 20),
                $"Speed Boost: {currentSpeedMultiplier:F2}x [{status}] | Time: {GetRemainingBoostTime():F2}s");
            GUI.color = Color.white;
        }

        // NEUE: Aktuelle Speed Anzeige
        GUI.Label(new Rect(10, 60, 400, 20),
            $"Current Speed: {currentActualSpeed:F1} m/s | Effective Multiplier: {(currentActualSpeed / originalWalkingSpeed):F2}x");

        if (isTrackingWallGrab)
        {
            float currentDelta = Time.time - wallGrabStartTime;
            GUI.Label(new Rect(10, 80, 400, 20), $"Wall Grab Time: {currentDelta:F3}s");
        }

        if (isInBunnyHopWindow)
        {
            float remainingWindow = GetBunnyHopWindowTime();
            GUI.color = Color.cyan;
            GUI.Label(new Rect(10, 100, 500, 20),
                $"Bunny Hop Window: {remainingWindow:F3}s | Landing Speed: {speedAtLanding:F1} m/s");
            GUI.color = Color.white;

            // Zeige was der Bunny Hop ergeben würde
            if (preserveCurrentSpeed && speedAtLanding >= minSpeedForPreservation)
            {
                float currentEffectiveMultiplier = speedAtLanding / originalWalkingSpeed;
                float potentialMultiplier = currentEffectiveMultiplier * speedPreservationBonus;
                float potentialSpeed = originalWalkingSpeed * potentialMultiplier;

                GUI.color = Color.green;
                GUI.Label(new Rect(10, 120, 500, 20),
                    $"Bunny Hop will give: {potentialMultiplier:F2}x ({potentialSpeed:F1} m/s) [PRESERVED +{(speedPreservationBonus - 1f) * 100f:F0}%]");
                GUI.color = Color.white;
            }
        }
    }

    // NEUE: Force Reset für alle Boosts (wird vom FPSController aufgerufen)
    public void ForceResetBoosts()
    {
        if (!isBoosting && !isFadingOut)
            return; // Nichts zu resetten

        Debug.Log("💥 VelocityMultiplier: Force Reset - Alle Boosts zurückgesetzt!");

        // Stoppe alle aktiven Boosts sofort
        isBoosting = false;
        isFadingOut = false;
        fadeOutTimer = 0f;
        boostTimer = 0f;

        currentSpeedMultiplier = 1.0f;
        previousSpeedMultiplier = 1.0f;

        // Reset zu Original Speeds
        if (fpsController != null)
        {
            fpsController.SetTargetWalkingSpeed(originalWalkingSpeed);
            fpsController.SetTargetRunningSpeed(originalRunningSpeed);
        }

        // Reset Bunny Hop Window
        isInBunnyHopWindow = false;

        // Reset Wall Jump Tracking
        isTrackingWallGrab = false;
    }
}