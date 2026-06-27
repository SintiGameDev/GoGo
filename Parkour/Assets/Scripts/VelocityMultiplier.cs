using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VelocityMultiplier : MonoBehaviour
{
    [Header("Wall Jump Combo Settings")]
    [Tooltip("Wie viel Speed-Multiplier pro Walljump in der Combo addiert wird")]
    public float comboBoostPerJump = 0.25f;
    [Tooltip("Maximaler Speed-Multiplier durch Combo")]
    public float maxComboMultiplier = 3.0f;
    [Tooltip("Zeitfenster nach einem Walljump, in dem der nächste Walljump die Combo fortsetzt")]
    public float comboWindow = 1.0f;

    [Header("Decay Settings")]
    [Tooltip("Wie lange der aktuelle Multiplier nach dem letzten Walljump aktiv bleibt, bevor er abgebaut wird")]
    public float boostHoldDuration = 1.0f;
    [Tooltip("Wie schnell der Multiplier nach Ablauf der Hold-Zeit auf 1.0 zurückfällt (pro Sekunde)")]
    public float decayRate = 2.0f;

    private SC_FPSController fpsController;

    private float currentSpeedMultiplier = 1.0f;
    private float lastWallJumpTime = -999f;
    private float holdTimer = 0f;

    private float originalWalkingSpeed;
    private float originalRunningSpeed;

    void Start()
    {
        fpsController = GetComponent<SC_FPSController>();

        if (fpsController == null)
        {
            Debug.LogError("VelocityMultiplier benoetigt SC_FPSController Component!");
            return;
        }

        originalWalkingSpeed = fpsController.walkingSpeed;
        originalRunningSpeed = fpsController.runningSpeed;
    }

    void Update()
    {
        if (fpsController == null) return;

        if (currentSpeedMultiplier > 1.0f)
        {
            holdTimer += Time.deltaTime;

            if (holdTimer >= boostHoldDuration)
            {
                currentSpeedMultiplier = Mathf.MoveTowards(currentSpeedMultiplier, 1.0f, decayRate * Time.deltaTime);
                ApplySpeedBoost();

                if (Mathf.Approximately(currentSpeedMultiplier, 1.0f))
                {
                    currentSpeedMultiplier = 1.0f;
                }
            }
        }
    }

    public void OnWallJump()
    {
        bool isComboContinuing = (Time.time - lastWallJumpTime) <= comboWindow;

        if (isComboContinuing)
        {
            currentSpeedMultiplier += comboBoostPerJump;
        }
        else
        {
            currentSpeedMultiplier = 1.0f + comboBoostPerJump;
        }

        currentSpeedMultiplier = Mathf.Clamp(currentSpeedMultiplier, 1.0f, maxComboMultiplier);

        lastWallJumpTime = Time.time;
        holdTimer = 0f;

        ApplySpeedBoost();

        Debug.Log($"Wall Jump Combo! Multiplier: {currentSpeedMultiplier:F2}x");
    }

    void ApplySpeedBoost()
    {
        if (fpsController == null) return;

        fpsController.SetTargetWalkingSpeed(originalWalkingSpeed * currentSpeedMultiplier);
        fpsController.SetTargetRunningSpeed(originalRunningSpeed * currentSpeedMultiplier);
    }

    public float GetCurrentMultiplier() => currentSpeedMultiplier;
    public bool IsBoosting() => currentSpeedMultiplier > 1.0f;

    public void ForceResetBoosts()
    {
        if (currentSpeedMultiplier == 1.0f) return;

        Debug.Log("VelocityMultiplier: Force Reset");

        currentSpeedMultiplier = 1.0f;
        holdTimer = 0f;
        lastWallJumpTime = -999f;

        if (fpsController != null)
        {
            fpsController.SetTargetWalkingSpeed(originalWalkingSpeed);
            fpsController.SetTargetRunningSpeed(originalRunningSpeed);
        }
    }

    void OnGUI()
    {
        if (currentSpeedMultiplier > 1.0f)
        {
            GUI.Label(new Rect(10, 40, 500, 20), $"Wall Jump Combo: {currentSpeedMultiplier:F2}x");
        }
    }
}
