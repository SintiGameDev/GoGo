using UnityEngine;

public class SurfVFXOperator : MonoBehaviour
{
    [Header("References")]
    public Camera playerCamera;
    public SurfController surfController;

    [Header("Surf FOV Effect")]
    public float surfFOVBonus = 10f;
    public float fovTransitionSpeed = 2f;

    [Header("Debug")]
    public bool showDebugInfo = true;

    private float targetFOVAddition = 0f;
    private float currentFOVAddition = 0f;
    private bool wasSurfing = false;

    void Start()
    {
        if (playerCamera == null)
            playerCamera = Camera.main;

        if (surfController == null)
            surfController = GetComponentInParent<SurfController>();

        if (showDebugInfo)
        {
            Debug.Log("=== SurfVFXOperator Started ===");
            Debug.Log($"Camera: {(playerCamera != null ? playerCamera.name : "NULL")}");
            Debug.Log($"SurfController: {(surfController != null ? "Found" : "NULL")}");

            if (playerCamera != null)
            {
                Debug.Log($"Base FOV: {playerCamera.fieldOfView}°");
            }
        }
    }

    void LateUpdate()
    {
        if (surfController == null || playerCamera == null)
            return;

        bool isSurfing = surfController.IsSurfing();

        if (isSurfing && !wasSurfing)
        {
            Debug.Log("🌊 Surf STARTED - Increasing FOV");
            targetFOVAddition = surfFOVBonus;
        }
        else if (!isSurfing && wasSurfing)
        {
            Debug.Log("🌊 Surf ENDED - Resetting FOV");
            targetFOVAddition = 0f;
        }

        wasSurfing = isSurfing;

        float previousAddition = currentFOVAddition;
        currentFOVAddition = Mathf.Lerp(currentFOVAddition, targetFOVAddition, Time.deltaTime * fovTransitionSpeed);

        float fovDelta = currentFOVAddition - previousAddition;
        playerCamera.fieldOfView += fovDelta;

        if (showDebugInfo && Mathf.Abs(fovDelta) > 0.01f)
        {
            Debug.Log($"FOV Change: {fovDelta:F3}° | Current Addition: {currentFOVAddition:F1}° | Total FOV: {playerCamera.fieldOfView:F1}°");
        }
    }

    void OnGUI()
    {
        if (!showDebugInfo)
            return;

        float yOffset = 320f;

        GUI.color = Color.cyan;
        GUI.Label(new Rect(10, yOffset, 500, 20), "=== SURF VFX (FOV Only) ===");
        yOffset += 20f;

        GUI.color = Color.white;

        if (surfController != null)
        {
            bool isSurfing = surfController.IsSurfing();
            float surfSpeed = surfController.GetCurrentSurfSpeed();

            GUI.Label(new Rect(10, yOffset, 500, 20),
                $"Surfing: {(isSurfing ? "✓ YES" : "✗ NO")} | Speed: {surfSpeed:F1} m/s");
            yOffset += 20f;

            if (playerCamera != null)
            {
                GUI.color = currentFOVAddition > 0.1f ? Color.green : Color.gray;
                GUI.Label(new Rect(10, yOffset, 500, 20),
                    $"FOV: {playerCamera.fieldOfView:F1}° (Base + {currentFOVAddition:F1}° Addition)");
                yOffset += 20f;

                GUI.Label(new Rect(10, yOffset, 500, 20),
                    $"Target Addition: {targetFOVAddition:F1}°");
            }
        }
        else
        {
            GUI.color = Color.red;
            GUI.Label(new Rect(10, yOffset, 500, 20), "❌ SurfController not found!");
        }

        GUI.color = Color.white;
    }
}