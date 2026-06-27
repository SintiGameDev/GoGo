using UnityEngine;

/// <summary>
/// FPS-Kamerasteuerung (POV), komplett getrennt vom Movement. Verwaltet Pitch
/// (Kamera, lokal) und Yaw (Spieler-Transform, global) sowie eine optionale
/// kurze Auto-Rotation für Walljumps - dafür rufen WalljumpHandler o.ä. einfach
/// StartAutoYaw(richtung) auf, ohne selbst mit Transform/Quaternion zu hantieren.
/// </summary>
public class PlayerLook : MonoBehaviour
{
    [Header("References")]
    public Camera playerCamera;
    public PlayerInputContext input;

    [Header("Look Settings")]
    public float pitchLimitUp = -85f;
    public float pitchLimitDown = 85f;

    [Header("Auto-Yaw (z.B. für Walljump-Richtungswechsel)")]
    [Tooltip("Wie lange die automatische Dreh-Animation zu einer vorgegebenen Zielrichtung dauert")]
    public float autoYawDuration = 0.12f;

    private float currentPitch = 0f;

    private bool isAutoYawing = false;
    private float autoYawStart = 0f;
    private float autoYawTarget = 0f;
    private float autoYawTimer = 0f;

    void Awake()
    {
        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>();

        if (input == null)
            input = GetComponent<PlayerInputContext>();
    }

    void Update()
    {
        ApplyPitch();
        ApplyYaw();
    }

    void ApplyPitch()
    {
        if (playerCamera == null || input == null)
            return;

        currentPitch -= input.GetMouseY();
        currentPitch = Mathf.Clamp(currentPitch, pitchLimitUp, pitchLimitDown);

        playerCamera.transform.localRotation = Quaternion.Euler(currentPitch, 0f, 0f);
    }

    void ApplyYaw()
    {
        if (isAutoYawing)
        {
            // Mausdelta dieses Frames mit auf Start/Ziel übertragen, damit der
            // Spieler die Auto-Drehung jederzeit sanft mitsteuern kann, statt
            // währenddessen komplett entmündigt zu sein.
            float mouseDelta = input != null ? input.GetMouseX() : 0f;
            autoYawStart += mouseDelta;
            autoYawTarget += mouseDelta;

            autoYawTimer -= Time.deltaTime;
            float progress = autoYawDuration > 0f
                ? 1f - Mathf.Clamp01(autoYawTimer / autoYawDuration)
                : 1f;

            float yaw = Mathf.LerpAngle(autoYawStart, autoYawTarget, progress);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            if (autoYawTimer <= 0f)
            {
                isAutoYawing = false;
            }
        }
        else if (input != null)
        {
            transform.rotation *= Quaternion.Euler(0f, input.GetMouseX(), 0f);
        }
    }

    /// <summary>
    /// Startet eine kurze Dreh-Animation zur angegebenen horizontalen Richtung
    /// (Y-Komponente wird ignoriert). Für Walljumps: Richtung = reflektierter
    /// Sprungvektor, damit der Spieler optisch "in die neue Richtung schaut".
    /// </summary>
    public void StartAutoYaw(Vector3 horizontalDirection)
    {
        Vector2 flat = new Vector2(horizontalDirection.x, horizontalDirection.z);
        if (flat.sqrMagnitude < 0.0001f)
            return;

        autoYawStart = transform.eulerAngles.y;
        autoYawTarget = Mathf.Atan2(horizontalDirection.x, horizontalDirection.z) * Mathf.Rad2Deg;
        autoYawTimer = autoYawDuration;
        isAutoYawing = autoYawDuration > 0f;

        if (!isAutoYawing)
        {
            transform.rotation = Quaternion.Euler(0f, autoYawTarget, 0f);
        }
    }

    /// <summary>Reine, ungekippte Blickrichtung in der horizontalen Ebene (für Sprung-Richtungsberechnungen).</summary>
    public Vector3 GetFlatForward()
    {
        Vector3 forward = transform.forward;
        forward.y = 0f;
        return forward.normalized;
    }

    /// <summary>Volle 3D-Blickrichtung inklusive Pitch (Kamera-forward) - relevant für Vault/Climb-Zielerkennung.</summary>
    public Vector3 GetCameraForward()
    {
        return playerCamera != null ? playerCamera.transform.forward : transform.forward;
    }
}
