using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HeadBang : MonoBehaviour
{
    [Header("Camera Reference")]
    public Camera playerCamera;

    [Header("Landing Impact Settings")]
    public float landingImpactAmount = 0.15f; // Wie weit die Kamera nach unten geht
    public float landingImpactDuration = 0.2f; // Dauer des gesamten Effekts
    public LeanTweenType landingEaseType = LeanTweenType.easeOutBounce; // Easing für Landing

    [Header("Wall Grab Impact Settings")]
    public float wallGrabImpactAmount = 0.1f; // Wie weit die Kamera nach unten geht
    public float wallGrabImpactDuration = 0.15f; // Dauer des gesamten Effekts
    public LeanTweenType wallGrabEaseType = LeanTweenType.easeOutQuad; // Easing für Wall Grab

    [Header("Fall Velocity Settings")]
    public float minFallVelocityForImpact = 5f; // Minimale Fallgeschwindigkeit für Impact
    public float maxFallVelocityForMaxImpact = 20f; // Fallgeschwindigkeit für maximalen Impact

    private SC_FPSController fpsController;
    private Vector3 originalCameraLocalPosition;
    private bool wasInAir = false;
    private float fallStartHeight = 0f;
    private LTDescr currentTween;

    void Start()
    {
        fpsController = GetComponent<SC_FPSController>();

        if (fpsController == null)
        {
            Debug.LogError("HeadBang benötigt SC_FPSController Component!");
        }

        if (playerCamera == null)
        {
            Debug.LogError("HeadBang benötigt eine Player Camera Referenz!");
        }
        else
        {
            // Ursprüngliche lokale Position der Kamera speichern
            originalCameraLocalPosition = playerCamera.transform.localPosition;
        }
    }

    void Update()
    {
        if (fpsController != null && playerCamera != null)
        {
            CheckLanding();
        }
    }

    void CheckLanding()
    {
        CharacterController cc = fpsController.GetComponent<CharacterController>();

        if (cc != null)
        {
            // Spieler ist in der Luft
            if (!cc.isGrounded && !wasInAir)
            {
                wasInAir = true;
                fallStartHeight = transform.position.y;
            }

            // Spieler ist gelandet
            if (cc.isGrounded && wasInAir)
            {
                // Berechne Fallgeschwindigkeit (vertikale Velocity beim Aufprall)
                float fallVelocity = Mathf.Abs(cc.velocity.y);

                // Nur Impact ausführen wenn Fallgeschwindigkeit hoch genug ist
                if (fallVelocity >= minFallVelocityForImpact)
                {
                    OnLanding(fallVelocity);
                }

                wasInAir = false;
            }
        }
    }

    // Wird aufgerufen wenn Spieler landet
    void OnLanding(float fallVelocity)
    {
        // Berechne Impact-Stärke basierend auf Fallgeschwindigkeit
        float velocityRatio = Mathf.Clamp01((fallVelocity - minFallVelocityForImpact) /
                                            (maxFallVelocityForMaxImpact - minFallVelocityForImpact));
        float impactAmount = Mathf.Lerp(landingImpactAmount * 0.3f, landingImpactAmount, velocityRatio);

        TriggerHeadBang(impactAmount, landingImpactDuration, landingEaseType);

        Debug.Log($"Landing Impact! Fall Velocity: {fallVelocity:F2} | Impact: {impactAmount:F3}");
    }

    // Wird vom FPSController aufgerufen wenn Wall Grab startet
    public void OnWallGrabImpact()
    {
        TriggerHeadBang(wallGrabImpactAmount, wallGrabImpactDuration, wallGrabEaseType);
        Debug.Log("Wall Grab Impact!");
    }

    void TriggerHeadBang(float impactAmount, float duration, LeanTweenType easeType)
    {
        if (playerCamera == null) return;

        // Aktuellen Tween abbrechen falls einer läuft
        if (currentTween != null)
        {
            LeanTween.cancel(playerCamera.gameObject);
        }

        // Zielposition berechnen (nach unten)
        Vector3 targetPosition = originalCameraLocalPosition + Vector3.down * impactAmount;

        // Sequenz: Runter und dann zurück zur Originalposition
        LTSeq sequence = LeanTween.sequence();

        // Phase 1: Kamera nach unten (50% der Zeit)
        sequence.append(LeanTween.moveLocal(playerCamera.gameObject, targetPosition, duration * 0.4f)
            .setEase(LeanTweenType.easeInQuad));

        // Phase 2: Kamera zurück zur Originalposition (50% der Zeit)
        sequence.append(LeanTween.moveLocal(playerCamera.gameObject, originalCameraLocalPosition, duration * 0.6f)
            .setEase(easeType));

        // Cleanup nach Abschluss
        sequence.append(() => {
            currentTween = null;
            // Sicherstellen dass Position exakt zurückgesetzt ist
            playerCamera.transform.localPosition = originalCameraLocalPosition;
        });
    }

    // Öffentliche Methode zum manuellen Zurücksetzen der Kameraposition
    public void ResetCameraPosition()
    {
        if (playerCamera != null)
        {
            if (currentTween != null)
            {
                LeanTween.cancel(playerCamera.gameObject);
                currentTween = null;
            }
            playerCamera.transform.localPosition = originalCameraLocalPosition;
        }
    }

    // Öffentliche Methode zum Aktualisieren der Original-Position (z.B. bei Camera-Bob-Systemen)
    public void UpdateOriginalPosition(Vector3 newPosition)
    {
        originalCameraLocalPosition = newPosition;
    }

    // Debug Visualisierung
    void OnGUI()
    {
        if (currentTween != null)
        {
            GUI.Label(new Rect(10, 120, 300, 20), "Head Bang Active!");
        }
    }
}