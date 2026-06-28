using UnityEngine;

// Auf dasselbe GameObject wie SC_FPSController packen.
// Liest die horizontale Geschwindigkeit (gleiches Pattern wie UpdateFOV() in
// SC_FPSController) und setzt darauf basierend die globale Shader-Property
// "_Intensity" fuer den Speedlines-Fullscreen-Effekt.
[RequireComponent(typeof(CharacterController))]
public class SpeedlinesController : MonoBehaviour
{
    [Header("Speed Thresholds")]
    [Tooltip("Ab dieser horizontalen Geschwindigkeit beginnen Speedlines sichtbar zu werden")]
    public float speedThresholdMin = 12f;
    [Tooltip("Ab dieser Geschwindigkeit ist die Intensitaet auf Maximum (1.0)")]
    public float speedThresholdMax = 22f;

    [Header("Smoothing")]
    [Tooltip("Wie schnell die Intensitaet dem Zielwert folgt (hoeher = direkter)")]
    public float intensityLerpSpeed = 6f;

    [Header("Shader Look")]
    public float lineCount = 24f;
    public float lineScrollSpeed = 1.2f;
    public float innerRadius = 0.35f;
    public Color lineColor = new Color(1f, 1f, 1f, 0.6f);

    [Header("Optional: VelocityMultiplier Boost staerker gewichten")]
    [Tooltip("Falls true, fliesst der Jump-Combo-Multiplier zusaetzlich in die Intensitaet ein")]
    public bool factorInJumpBoost = true;
    public float jumpBoostInfluence = 0.5f;

    private CharacterController characterController;
    private VelocityMultiplier velocityMultiplier;
    private float currentIntensity = 0f;

    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int LineCountId = Shader.PropertyToID("_LineCount");
    private static readonly int LineSpeedId = Shader.PropertyToID("_LineSpeed");
    private static readonly int InnerRadiusId = Shader.PropertyToID("_InnerRadius");
    private static readonly int LineColorId = Shader.PropertyToID("_LineColor");

    void Start()
    {
        characterController = GetComponent<CharacterController>();
        velocityMultiplier = GetComponent<VelocityMultiplier>();

        // Statische Look-Parameter einmalig global setzen
        Shader.SetGlobalFloat(LineCountId, lineCount);
        Shader.SetGlobalFloat(LineSpeedId, lineScrollSpeed);
        Shader.SetGlobalFloat(InnerRadiusId, innerRadius);
        Shader.SetGlobalColor(LineColorId, lineColor);
        Shader.SetGlobalFloat(IntensityId, 0f);
    }

    void Update()
    {
        if (characterController == null) return;

        Vector3 velocity = characterController.velocity;
        Vector3 flatVelocity = new Vector3(velocity.x, 0f, velocity.z);
        float horizontalSpeed = flatVelocity.magnitude;

        float targetIntensity = Mathf.InverseLerp(speedThresholdMin, speedThresholdMax, horizontalSpeed);
        targetIntensity = Mathf.Clamp01(targetIntensity);

        if (factorInJumpBoost && velocityMultiplier != null && velocityMultiplier.IsBoosting())
        {
            float boostNormalized = Mathf.InverseLerp(1f, velocityMultiplier.maxComboMultiplier, velocityMultiplier.GetCurrentMultiplier());
            targetIntensity = Mathf.Max(targetIntensity, boostNormalized * jumpBoostInfluence);
            targetIntensity = Mathf.Clamp01(targetIntensity);
        }

        currentIntensity = Mathf.Lerp(currentIntensity, targetIntensity, Time.deltaTime * intensityLerpSpeed);

        Shader.SetGlobalFloat(IntensityId, currentIntensity);
    }
}
