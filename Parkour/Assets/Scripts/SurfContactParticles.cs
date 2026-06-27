using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class SurfContactParticles : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Referenz zum SurfController (auto-detected wenn leer)")]
    public SurfController surfController;

    [Tooltip("Referenz zum FPSController (auto-detected wenn leer)")]
    public SC_FPSController fpsController;

    [Tooltip("CharacterController des Spielers (auto-detected wenn leer)")]
    public CharacterController characterController;

    [Header("Collision Detection")]
    [Tooltip("Layer-Mask für Wände (was zählt als surf-bare Oberfläche)")]
    public LayerMask wallLayerMask = ~0;

    [Tooltip("Wie viele Raycasts pro Frame (mehr = genauer aber teurer)")]
    public int raycastCount = 5;

    [Tooltip("Maximale Distanz für Raycast")]
    public float maxRaycastDistance = 1.5f;

    [Tooltip("Raycast-Richtungen (relativ zum Spieler)")]
    public Vector3[] raycastDirections = new Vector3[]
    {
        new Vector3(-1, 0, 0),    // Links
        new Vector3(1, 0, 0),     // Rechts
        new Vector3(0, 0, -1),    // Hinten
        new Vector3(0, 0, 1),     // Vorne
        new Vector3(-0.7f, 0, -0.7f) // Diagonal
    };

    [Header("Particle Positioning")]
    [Tooltip("Offset von der Wand (damit Partikel nicht in der Wand spawnen)")]
    public float wallOffset = 0.1f;

    [Tooltip("Smooth-Faktor für Position Updates")]
    public float positionSmoothness = 10f;

    [Tooltip("Smooth-Faktor für Rotation Updates")]
    public float rotationSmoothness = 5f;

    [Tooltip("Partikel nach oben verschieben (für Funken-Effekt)")]
    public float verticalOffset = 0.2f;

    [Header("Particle Control")]
    [Tooltip("Emission Rate basierend auf Surf-Speed")]
    public float minEmissionRate = 10f;
    public float maxEmissionRate = 50f;

    [Tooltip("Partikel-Geschwindigkeit basierend auf Surf-Speed")]
    public float minParticleSpeed = 1f;
    public float maxParticleSpeed = 5f;

    [Tooltip("Minimale Surf-Speed für Partikel")]
    public float minSurfSpeedForParticles = 10f;

    [Header("Visual Settings")]
    [Tooltip("Partikel-Farbe basierend auf Speed")]
    public Gradient speedColorGradient;

    [Tooltip("Partikel-Größe basierend auf Speed")]
    public AnimationCurve speedSizeCurve = AnimationCurve.Linear(0, 0.5f, 1, 1.5f);

    [Header("Audio")]
    [Tooltip("Sound beim Surfen (optional)")]
    public AudioSource scrapeAudioSource;

    [Tooltip("Maximale Lautstärke")]
    public float maxScrapeVolume = 0.4f;

    [Tooltip("Pitch-Range basierend auf Speed")]
    public Vector2 pitchRange = new Vector2(0.8f, 1.5f);

    [Header("Debug")]
    public bool showDebugRays = true;
    public bool showDebugInfo = true;
    public Color debugRayColor = Color.red;
    public Color debugHitColor = Color.green;

    private ParticleSystem particleSystem;
    private ParticleSystem.EmissionModule emissionModule;
    private ParticleSystem.MainModule mainModule;

    private Vector3 currentContactPoint;
    private Vector3 targetContactPoint;
    private Vector3 currentContactNormal;
    private Vector3 targetContactNormal;

    private bool hasContact = false;
    private bool wasEmitting = false;

    private Transform playerTransform;
    private float lastContactTime = 0f;

    void Start()
    {
        particleSystem = GetComponent<ParticleSystem>();
        emissionModule = particleSystem.emission;
        mainModule = particleSystem.main;

        if (surfController == null)
            surfController = GetComponentInParent<SurfController>();

        if (fpsController == null)
            fpsController = GetComponentInParent<SC_FPSController>();

        if (characterController == null)
            characterController = GetComponentInParent<CharacterController>();

        if (fpsController != null)
            playerTransform = fpsController.transform;
        else if (surfController != null)
            playerTransform = surfController.transform;

        emissionModule.enabled = false;

        if (scrapeAudioSource != null)
        {
            scrapeAudioSource.loop = true;
            scrapeAudioSource.volume = 0f;
            scrapeAudioSource.Play();
        }

        if (speedColorGradient == null || speedColorGradient.colorKeys.Length == 0)
        {
            speedColorGradient = new Gradient();
            speedColorGradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(Color.yellow, 0f),
                    new GradientColorKey(Color.red, 0.5f),
                    new GradientColorKey(Color.white, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                }
            );
        }

        ValidateSetup();
    }

    void ValidateSetup()
    {
        if (surfController == null)
        {
            Debug.LogError("❌ SurfContactParticles: SurfController nicht gefunden!");
        }

        if (playerTransform == null)
        {
            Debug.LogError("❌ SurfContactParticles: Player Transform nicht gefunden!");
        }

        if (showDebugInfo)
        {
            Debug.Log("✅ SurfContactParticles Setup complete");
            Debug.Log($"   Raycast Count: {raycastCount}");
            Debug.Log($"   Max Distance: {maxRaycastDistance}");
        }
    }

    void Update()
    {
        if (surfController == null || playerTransform == null)
            return;

        bool isSurfing = surfController.IsSurfing();
        float surfSpeed = surfController.GetCurrentSurfSpeed();

        if (isSurfing && surfSpeed >= minSurfSpeedForParticles)
        {
            bool foundContact = DetectWallContact();

            if (foundContact)
            {
                hasContact = true;
                lastContactTime = Time.time;

                UpdateParticlePosition();
                UpdateParticleEmission(surfSpeed);
                UpdateParticleProperties(surfSpeed);
                UpdateScrapeAudio(surfSpeed);

                if (!wasEmitting)
                {
                    StartEmission();
                }

                wasEmitting = true;
            }
            else
            {
                if (Time.time - lastContactTime > 0.1f)
                {
                    hasContact = false;
                    StopEmission();
                    wasEmitting = false;
                }
            }
        }
        else
        {
            hasContact = false;
            StopEmission();
            wasEmitting = false;
        }
    }

    bool DetectWallContact()
    {
        Vector3 bestHitPoint = Vector3.zero;
        Vector3 bestHitNormal = Vector3.up;
        float closestDistance = float.MaxValue;
        bool hitDetected = false;

        Vector3 playerPosition = playerTransform.position;

        if (characterController != null)
        {
            playerPosition += new Vector3(0, characterController.height * 0.5f, 0);
        }

        for (int i = 0; i < raycastDirections.Length; i++)
        {
            Vector3 worldDirection = playerTransform.TransformDirection(raycastDirections[i]);

            RaycastHit hit;
            if (Physics.Raycast(playerPosition, worldDirection, out hit, maxRaycastDistance, wallLayerMask))
            {
                if (hit.normal.y < 0.5f && hit.normal.y > -0.5f)
                {
                    if (hit.distance < closestDistance)
                    {
                        closestDistance = hit.distance;
                        bestHitPoint = hit.point;
                        bestHitNormal = hit.normal;
                        hitDetected = true;
                    }
                }

                if (showDebugRays)
                {
                    Debug.DrawRay(playerPosition, worldDirection * hit.distance, debugHitColor);
                }
            }
            else if (showDebugRays)
            {
                Debug.DrawRay(playerPosition, worldDirection * maxRaycastDistance, debugRayColor);
            }
        }

        if (hitDetected)
        {
            targetContactPoint = bestHitPoint + bestHitNormal * wallOffset;
            targetContactPoint.y += verticalOffset;
            targetContactNormal = bestHitNormal;
        }

        return hitDetected;
    }

    void UpdateParticlePosition()
    {
        currentContactPoint = Vector3.Lerp(
            currentContactPoint,
            targetContactPoint,
            Time.deltaTime * positionSmoothness
        );

        currentContactNormal = Vector3.Lerp(
            currentContactNormal,
            targetContactNormal,
            Time.deltaTime * rotationSmoothness
        );

        transform.position = currentContactPoint;

        Quaternion targetRotation = Quaternion.LookRotation(-currentContactNormal, Vector3.up);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            Time.deltaTime * rotationSmoothness
        );
    }

    void UpdateParticleEmission(float surfSpeed)
    {
        float speedRatio = Mathf.Clamp01((surfSpeed - minSurfSpeedForParticles) /
                                        (surfController.maxSurfSpeed - minSurfSpeedForParticles));

        float emissionRate = Mathf.Lerp(minEmissionRate, maxEmissionRate, speedRatio);
        emissionModule.rateOverTime = emissionRate;
    }

    void UpdateParticleProperties(float surfSpeed)
    {
        float speedRatio = Mathf.Clamp01(surfSpeed / surfController.maxSurfSpeed);

        Color particleColor = speedColorGradient.Evaluate(speedRatio);
        mainModule.startColor = particleColor;

        float particleSize = speedSizeCurve.Evaluate(speedRatio);
        mainModule.startSize = particleSize;

        float particleSpeed = Mathf.Lerp(minParticleSpeed, maxParticleSpeed, speedRatio);
        mainModule.startSpeed = particleSpeed;
    }

    void UpdateScrapeAudio(float surfSpeed)
    {
        if (scrapeAudioSource == null)
            return;

        float speedRatio = Mathf.Clamp01((surfSpeed - minSurfSpeedForParticles) /
                                        (surfController.maxSurfSpeed - minSurfSpeedForParticles));

        float targetVolume = maxScrapeVolume * speedRatio;
        scrapeAudioSource.volume = Mathf.Lerp(scrapeAudioSource.volume, targetVolume, Time.deltaTime * 5f);

        float targetPitch = Mathf.Lerp(pitchRange.x, pitchRange.y, speedRatio);
        scrapeAudioSource.pitch = Mathf.Lerp(scrapeAudioSource.pitch, targetPitch, Time.deltaTime * 5f);
    }

    void StartEmission()
    {
        if (!emissionModule.enabled)
        {
            emissionModule.enabled = true;

            if (showDebugInfo)
            {
                Debug.Log("🔥 Surf Contact Particles: Started");
            }
        }
    }

    void StopEmission()
    {
        if (emissionModule.enabled)
        {
            emissionModule.enabled = false;

            if (scrapeAudioSource != null)
            {
                scrapeAudioSource.volume = 0f;
            }

            if (showDebugInfo)
            {
                Debug.Log("🔥 Surf Contact Particles: Stopped");
            }
        }
    }

    void OnGUI()
    {
        if (!showDebugInfo)
            return;

        float yOffset = 400f;

        GUI.color = Color.yellow;
        GUI.Label(new Rect(10, yOffset, 400, 20), "=== SURF CONTACT PARTICLES ===");
        yOffset += 20f;

        GUI.color = Color.white;

        if (surfController != null)
        {
            bool isSurfing = surfController.IsSurfing();
            float surfSpeed = surfController.GetCurrentSurfSpeed();

            GUI.Label(new Rect(10, yOffset, 400, 20),
                $"Surfing: {(isSurfing ? "YES" : "NO")} | Speed: {surfSpeed:F1} m/s");
            yOffset += 20f;

            GUI.Label(new Rect(10, yOffset, 400, 20),
                $"Wall Contact: {(hasContact ? "YES" : "NO")}");
            yOffset += 20f;

            if (hasContact)
            {
                GUI.color = Color.green;
                GUI.Label(new Rect(10, yOffset, 400, 20),
                    $"Emission Rate: {emissionModule.rateOverTime.constant:F0}/s");
                yOffset += 20f;

                GUI.Label(new Rect(10, yOffset, 400, 20),
                    $"Contact Position: {currentContactPoint}");
                yOffset += 20f;
            }

            if (scrapeAudioSource != null && scrapeAudioSource.volume > 0.01f)
            {
                GUI.color = Color.cyan;
                GUI.Label(new Rect(10, yOffset, 400, 20),
                    $"Scrape Audio: Vol={scrapeAudioSource.volume:F2} | Pitch={scrapeAudioSource.pitch:F2}");
            }
        }

        GUI.color = Color.white;
    }

    void OnDrawGizmos()
    {
        if (!showDebugRays || !Application.isPlaying)
            return;

        if (hasContact)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(currentContactPoint, 0.2f);

            Gizmos.color = Color.green;
            Gizmos.DrawRay(currentContactPoint, currentContactNormal * 0.5f);

            Gizmos.color = Color.blue;
            Gizmos.DrawLine(
                playerTransform != null ? playerTransform.position : transform.position,
                currentContactPoint
            );
        }
    }

    void OnDestroy()
    {
        if (scrapeAudioSource != null && scrapeAudioSource.isPlaying)
        {
            scrapeAudioSource.Stop();
        }
    }
}