using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VelocityMultiplier : MonoBehaviour
{
    [Header("Jump Tempo Settings")]
    [Tooltip("Airtime (Zeit zwischen Absprung und naechstem Jump) unter diesem Wert gibt den maximalen Boost-Zuwachs")]
    public float fastestJumpInterval = 0.15f;
    [Tooltip("Airtime ueber diesem Wert zaehlt nicht mehr als 'schnelle Folge' -> Reset")]
    public float slowestJumpInterval = 1.0f;

    [Header("Boost Amount Settings")]
    [Tooltip("Boost-Zuwachs bei schnellster moeglicher Sprungfolge (fastestJumpInterval)")]
    public float maxBoostPerJump = 0.4f;
    [Tooltip("Boost-Zuwachs bei langsamster noch zaehlender Sprungfolge (slowestJumpInterval)")]
    public float minBoostPerJump = 0.05f;
    [Tooltip("Maximaler Gesamt-Speed-Multiplier")]
    public float maxComboMultiplier = 3.0f;

    [Header("Decay Settings")]
    [Tooltip("Wie lange der Multiplier nach dem letzten Jump aktiv bleibt, bevor er abgebaut wird")]
    public float boostHoldDuration = 1.0f;
    [Tooltip("Wie schnell der Multiplier nach Ablauf der Hold-Zeit auf 1.0 zurueckfaellt (pro Sekunde)")]
    public float decayRate = 2.0f;

    [Header("Debug")]
    public bool showDebugInfo = false;

    private SC_FPSController fpsController;
    private CharacterController characterController;

    private float currentSpeedMultiplier = 1.0f;
    private float lastTakeoffTime = -999f;
    private float holdTimer = 0f;

    private float originalWalkingSpeed;
    private float originalRunningSpeed;

    void Start()
    {
        fpsController = GetComponent<SC_FPSController>();
        characterController = GetComponent<CharacterController>();

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

        bool isGrounded = characterController == null || characterController.isGrounded;

        // Kein Decay waehrend der Spieler in der Luft ist -- der Boost soll den
        // gesamten Sprung ueber erhalten bleiben, unabhaengig von der Airtime.
        if (currentSpeedMultiplier > 1.0f && isGrounded)
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

    // Wird von SC_FPSController in dem Moment aufgerufen, in dem der Spieler abspringt
    // (normaler Jump und Walljump). Das gemessene Intervall ist die Airtime seit dem
    // VORHERIGEN Absprung -- nicht die Zeit seit dem letzten Methodenaufruf an sich,
    // sondern bewusst die Luftzeit, damit hohe Geschwindigkeit (= weitere/laengere
    // Spruenge) den Boost nicht kuenstlich frueh capt.
    public void OnJumpAction()
    {
        float airtime = Time.time - lastTakeoffTime;
        holdTimer = 0f;

        bool isFastEnoughToCount = airtime <= slowestJumpInterval;

        if (!isFastEnoughToCount)
        {
            // Airtime war zu lang -> Combo beginnt neu, kein Boost diesmal
            currentSpeedMultiplier = 1.0f;
            ApplySpeedBoost();
            Debug.Log("Jump Combo Reset (Airtime zu lang)");
        }
        else
        {
            // Je kuerzer die Airtime, desto naeher an maxBoostPerJump
            float normalizedTempo = Mathf.InverseLerp(slowestJumpInterval, fastestJumpInterval, airtime);
            float boostThisJump = Mathf.Lerp(minBoostPerJump, maxBoostPerJump, normalizedTempo);

            currentSpeedMultiplier += boostThisJump;
            currentSpeedMultiplier = Mathf.Clamp(currentSpeedMultiplier, 1.0f, maxComboMultiplier);

            ApplySpeedBoost();

            Debug.Log($"Jump! Airtime: {airtime:F2}s | Boost: +{boostThisJump:F2} | Multiplier: {currentSpeedMultiplier:F2}x");
        }

        // Zeitpunkt DIESES Absprungs merken -> Basis fuer die Airtime-Messung beim naechsten Jump
        lastTakeoffTime = Time.time;
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
        lastTakeoffTime = -999f;

        if (fpsController != null)
        {
            fpsController.SetTargetWalkingSpeed(originalWalkingSpeed);
            fpsController.SetTargetRunningSpeed(originalRunningSpeed);
        }
    }

    void OnGUI()
    {
        if (!showDebugInfo)
            return;

        if (currentSpeedMultiplier > 1.0f)
        {
            GUI.Label(new Rect(10, 40, 500, 20), $"Jump Combo: {currentSpeedMultiplier:F2}x");
        }
    }
}
