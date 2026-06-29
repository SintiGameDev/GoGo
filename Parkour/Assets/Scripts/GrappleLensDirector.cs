using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Choreografiert den Kamera-/Linseneffekt waehrend eines Grapples, um das
/// Game Feel eines fast-paced Movement Shooters (Neon White) zu erreichen.
///
/// Cinematographie-Idee - der Grapple hat drei "Beats":
///   1) ANZUG  (Snap-In)  : kurzes FOV-Rein + Distortion-Kick -> Kompression vor dem Schub
///   2) FLUG   (Sustain)  : FOV raus, Chromatic Aberration + Distortion skaliert mit Tempo
///   3) AUFPRALL (Impact) : harter Aberration/Vignette/Distortion-Punch, federt zurueck
///
/// Single Responsibility: dieses Script LIEST nur Zustaende (grappleProgress,
/// Combo-Multiplier) und SCHREIBT ausschliesslich visuelle Post-Processing-Werte
/// + FOV. Es greift NICHT in die Movement-Logik ein. Angesteuert wird es ueber
/// drei Hooks aus EnemyOperator: OnGrappleStart / OnGrappleProgress / OnGrappleImpact.
///
/// Voraussetzung in der Szene:
///   - Ein aktives URP Volume (global) mit den Overrides:
///       Lens Distortion, Chromatic Aberration, Vignette
///     -> entweder hier per "autoCreateVolume" automatisch anlegen lassen,
///        oder ein bestehendes Volume per "targetVolume" zuweisen.
///   - Post Processing am Camera-Renderer aktiviert (hast du bereits).
///
/// Alle visuellen Kurven sind AnimationCurves im Inspector -> Game Feel ohne Recompile tunen.
/// </summary>
[DisallowMultipleComponent]
public class GrappleLensDirector : MonoBehaviour
{
    public enum GrapplePhase { Idle, Charging, Flying, Impacting }

    [Header("Referenzen")]
    [Tooltip("Kamera, deren FOV moduliert wird. Leer = playerCamera vom SC_FPSController bzw. Camera.main.")]
    public Camera targetCamera;
    [Tooltip("Optional: VelocityMultiplier fuer Combo-Kopplung (hoeherer Streak = staerkerer Effekt). Leer = wird automatisch gesucht.")]
    public VelocityMultiplier velocityMultiplier;
    [Tooltip("Optional: bestehendes globales URP-Volume verwenden. Leer + autoCreateVolume = es wird eines erzeugt.")]
    public Volume targetVolume;
    [Tooltip("Wenn kein targetVolume gesetzt ist, automatisch ein dediziertes globales Volume anlegen.")]
    public bool autoCreateVolume = true;

    // ----------------------------------------------------------------------
    [Header("FOV - Beat 1: Anzug (Snap-In)")]
    [Tooltip("Wie viel Grad das FOV im allerersten Moment des Grapples NACH INNEN gezogen wird (Kompressionsgefuehl). Wird additiv auf das Basis-FOV gelegt und sofort wieder rausgefedert.")]
    public float snapInFovDip = 6f;
    [Tooltip("Dauer (Sekunden, ungescaled) des Snap-In-Dips, bevor er in die Flugphase uebergeht.")]
    public float snapInDuration = 0.08f;
    [Tooltip("Kurvenform des Snap-In: X = 0..1 normalisierte snapInDuration, Y = 0..1 Anteil des Dips. Empfehlung: schnell auf 1 hoch, dann zurueck auf 0.")]
    public AnimationCurve snapInCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0f));

    [Header("FOV - Beat 2: Flug (Sustain)")]
    [Tooltip("Wie viel Grad das FOV waehrend des Flugs zusaetzlich AUFGEHT (Speed-Gefuehl), additiv zum vorhandenen Speed-FOV des Controllers.")]
    public float flightFovPush = 10f;
    [Tooltip("Kurve: X = grappleProgress (0..1, dein speedFactor), Y = 0..1 Anteil von flightFovPush. <1 am Anfang fuer spuerbaren Schub Richtung Ende.")]
    public AnimationCurve flightFovCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(1f, 1f));

    // ----------------------------------------------------------------------
    [Header("Lens Distortion (Beat 2 + 3)")]
    [Tooltip("Max. negative Lens Distortion waehrend des Flugs (negativ = pincushion / nach innen gezogen, verstaerkt Tunnel-/Speedgefuehl). Bereich URP: -1..0.")]
    [Range(-1f, 0f)] public float flightDistortion = -0.25f;
    [Tooltip("Kurve: X = grappleProgress, Y = 0..1 Anteil von flightDistortion.")]
    public AnimationCurve distortionCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(1f, 1f));
    [Tooltip("Zusaetzlicher kurzer Distortion-Kick im Impact-Moment (wird zu flightDistortion addiert, dann ausgefedert).")]
    [Range(-1f, 0f)] public float impactDistortionKick = -0.35f;

    [Header("Chromatic Aberration (Beat 2 + 3)")]
    [Tooltip("Max. Chromatic Aberration waehrend des Flugs (0..1). Der 'cheap lens'-Look an den Raendern, der Speed signalisiert.")]
    [Range(0f, 1f)] public float flightAberration = 0.35f;
    [Tooltip("Kurve: X = grappleProgress, Y = 0..1 Anteil von flightAberration.")]
    public AnimationCurve aberrationCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(1f, 1f));
    [Tooltip("Aberration-Spike im Impact-Moment (0..1), federt zurueck.")]
    [Range(0f, 1f)] public float impactAberrationSpike = 0.8f;

    [Header("Vignette (Beat 2 + 3)")]
    [Tooltip("Max. zusaetzliche Vignette waehrend des Flugs (Tunnelblick, 0..1).")]
    [Range(0f, 1f)] public float flightVignette = 0.2f;
    [Tooltip("Kurve: X = grappleProgress, Y = 0..1 Anteil von flightVignette.")]
    public AnimationCurve vignetteCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(1f, 1f));
    [Tooltip("Vignette-Punch im Impact-Moment (0..1), federt zurueck -> liest sich als Schlag.")]
    [Range(0f, 1f)] public float impactVignettePunch = 0.45f;

    // ----------------------------------------------------------------------
    [Header("Impact-Federung (Beat 3)")]
    [Tooltip("Wie schnell der Impact-Effekt aufschlaegt (Sekunden, ungescaled). Sehr kurz = harter Punch.")]
    public float impactAttack = 0.04f;
    [Tooltip("Wie lange der Impact-Effekt zurueckfedert (Sekunden, ungescaled).")]
    public float impactRelease = 0.3f;
    [Tooltip("Federkurve des Impact-Release: X = 0..1 (0 = Punch-Peak, 1 = abgeklungen), Y = 0..1 Restanteil.")]
    public AnimationCurve impactReleaseCurve = new AnimationCurve(
        new Keyframe(0f, 1f), new Keyframe(0.4f, 0.15f), new Keyframe(1f, 0f));

    // ----------------------------------------------------------------------
    [Header("Combo-Kopplung (optional)")]
    [Tooltip("Wenn an: alle Effektstaerken werden mit dem Combo-Multiplier skaliert (1x..maxComboMultiplier -> comboInfluenceMin..comboInfluenceMax).")]
    public bool scaleWithCombo = true;
    [Tooltip("Effekt-Skalierung bei Combo-Multiplier = 1.0 (kein Streak).")]
    [Range(0f, 2f)] public float comboInfluenceMin = 0.75f;
    [Tooltip("Effekt-Skalierung bei maximalem Combo-Multiplier.")]
    [Range(0f, 2f)] public float comboInfluenceMax = 1.5f;

    [Header("Aufloesung / Wiederherstellung")]
    [Tooltip("Wie schnell die Flug-Effekte nach Grapple-Ende auf 0 zurueckgefahren werden (pro Sekunde, ungescaled), falls kein Impact stattfand (Abbruch).")]
    public float releaseSpeedOnAbort = 6f;

    [Header("Debug")]
    public bool logHooks = false;

    // ------------------------- interne State -------------------------------
    private GrapplePhase phase = GrapplePhase.Idle;

    private float baseFovCache = 60f;          // Basis-FOV (vom Controller verwaltet, hier nur als Referenz fuer additive Modulation)
    private float snapInTimer = 0f;            // laeuft 0..snapInDuration
    private float currentProgress = 0f;        // Flug: 0..1 (dein speedFactor)

    private float comboScale = 1f;             // beim Grapple-Start eingefrorener Combo-Faktor

    // Impact-Federung
    private bool impactActive = false;
    private float impactTimer = 0f;            // laeuft 0..(impactAttack+impactRelease)

    // Sanfter Abbau bei Abbruch
    private float abortFade = 0f;              // 0..1, wird bei Abort von 1 -> 0 gezogen

    // URP Volume Overrides
    private Volume volume;
    private VolumeProfile profile;
    private LensDistortion lensDistortion;
    private ChromaticAberration chromaticAberration;
    private Vignette vignette;
    private bool volumeReady = false;

    // Cache der Basiswerte des Volumes, damit wir additiv arbeiten und beim
    // Idle EXAKT auf die vom Designer eingestellten Profil-Werte zurueckkehren.
    private float baseDistortion = 0f;
    private float baseAberration = 0f;
    private float baseVignette = 0f;

    void Awake()
    {
        if (targetCamera == null)
        {
            SC_FPSController fps = GetComponent<SC_FPSController>();
            if (fps != null && fps.playerCamera != null) targetCamera = fps.playerCamera;
            if (targetCamera == null) targetCamera = Camera.main;
        }

        if (velocityMultiplier == null)
            velocityMultiplier = GetComponent<VelocityMultiplier>();

        if (targetCamera != null)
            baseFovCache = targetCamera.fieldOfView;

        SetupVolume();
    }

    void SetupVolume()
    {
        volume = targetVolume;

        if (volume == null && autoCreateVolume)
        {
            GameObject volGO = new GameObject("GrappleLensDirector_Volume");
            volGO.transform.SetParent(transform, false);
            volume = volGO.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f; // ueber typischen Szenen-Volumes
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.profile = profile;
        }

        if (volume == null)
        {
            Debug.LogWarning("GrappleLensDirector: Kein Volume gefunden/erzeugt. Linseneffekte (Distortion/Aberration/Vignette) deaktiviert. FOV-Beats laufen trotzdem.");
            volumeReady = false;
            return;
        }

        profile = volume.profile;

        // Overrides holen oder anlegen
        if (!profile.TryGet(out lensDistortion))
            lensDistortion = profile.Add<LensDistortion>(true);
        if (!profile.TryGet(out chromaticAberration))
            chromaticAberration = profile.Add<ChromaticAberration>(true);
        if (!profile.TryGet(out vignette))
            vignette = profile.Add<Vignette>(true);

        // overrideState aktivieren, damit wir die Werte zur Laufzeit setzen duerfen
        lensDistortion.intensity.overrideState = true;
        chromaticAberration.intensity.overrideState = true;
        vignette.intensity.overrideState = true;

        // Basiswerte cachen (Designer-Defaults respektieren)
        baseDistortion = lensDistortion.intensity.value;
        baseAberration = chromaticAberration.intensity.value;
        baseVignette = vignette.intensity.value;

        volumeReady = true;
    }

    // =====================================================================
    //  HOOKS - werden aus EnemyOperator aufgerufen
    // =====================================================================

    /// <summary>Beat 1 anstossen: Grapple beginnt. Friert den Combo-Faktor ein.</summary>
    public void OnGrappleStart()
    {
        phase = GrapplePhase.Charging;
        snapInTimer = 0f;
        currentProgress = 0f;
        abortFade = 0f;
        impactActive = false;
        impactTimer = 0f;

        comboScale = ComputeComboScale();

        if (logHooks) Debug.Log($"[LensDirector] OnGrappleStart  comboScale={comboScale:F2}");
    }

    /// <summary>
    /// Beat 2 fuettern: jeden Frame mit dem aktuellen Fortschritt (0..1).
    /// Uebergib hier deinen vorhandenen speedFactor aus UpdateGrapple.
    /// </summary>
    public void OnGrappleProgress(float progress01)
    {
        if (phase == GrapplePhase.Idle) phase = GrapplePhase.Charging;
        currentProgress = Mathf.Clamp01(progress01);
        if (phase == GrapplePhase.Charging && snapInTimer >= snapInDuration)
            phase = GrapplePhase.Flying;
    }

    /// <summary>Beat 3 anstossen: exakter Kill-/Impact-Moment.</summary>
    public void OnGrappleImpact()
    {
        phase = GrapplePhase.Impacting;
        impactActive = true;
        impactTimer = 0f;
        if (logHooks) Debug.Log("[LensDirector] OnGrappleImpact");
    }

    /// <summary>Grapple ohne Kill beendet (Abbruch) -> Flug-Effekte sanft ausblenden.</summary>
    public void OnGrappleAbort()
    {
        phase = GrapplePhase.Idle;
        abortFade = 1f;
        if (logHooks) Debug.Log("[LensDirector] OnGrappleAbort");
    }

    // =====================================================================
    //  UPDATE - rechnet jeden Frame die finalen Werte und schreibt sie
    // =====================================================================
    void LateUpdate()
    {
        // unscaled, damit Slow-Motion (Time.timeScale < 1) die Linsen-Choreografie
        // nicht zaeh macht - konsistent mit deinem Grapple, der auch unscaled laeuft.
        float dt = Time.unscaledDeltaTime;

        // --- Snap-In-Timer (Beat 1) ---
        if (phase == GrapplePhase.Charging && snapInTimer < snapInDuration)
        {
            snapInTimer += dt;
            if (snapInTimer >= snapInDuration && currentProgress > 0f)
                phase = GrapplePhase.Flying;
        }

        // --- Abort-Fade ---
        if (abortFade > 0f)
            abortFade = Mathf.MoveTowards(abortFade, 0f, releaseSpeedOnAbort * dt);

        // --- Impact-Timer (Beat 3) ---
        float impactWeight = 0f; // 0..1, Punch -> ausgefedert
        if (impactActive)
        {
            impactTimer += dt;
            float total = Mathf.Max(0.0001f, impactAttack + impactRelease);

            if (impactTimer <= impactAttack)
            {
                // Attack: 0 -> 1 (sehr kurz, harter Anschlag)
                impactWeight = impactAttack > 0.0001f ? (impactTimer / impactAttack) : 1f;
            }
            else
            {
                // Release: 1 -> 0 ueber impactReleaseCurve
                float relT = Mathf.Clamp01((impactTimer - impactAttack) / Mathf.Max(0.0001f, impactRelease));
                impactWeight = impactReleaseCurve.Evaluate(relT);
            }

            if (impactTimer >= total)
            {
                impactActive = false;
                impactWeight = 0f;
                if (phase == GrapplePhase.Impacting) phase = GrapplePhase.Idle;
            }
        }

        // --- Flug-Gewicht (Beat 2) ---
        // Aktiv waehrend Charging/Flying; bei Abort ueber abortFade ausgeblendet.
        float flightActive = (phase == GrapplePhase.Charging || phase == GrapplePhase.Flying) ? 1f : abortFade;
        float progressEval = currentProgress;

        // --- FOV (Beat 1 + 2) ---
        ApplyFov(flightActive, progressEval, impactWeight);

        // --- Post Processing (Beat 2 + 3) ---
        if (volumeReady)
            ApplyPostProcessing(flightActive, progressEval, impactWeight);
    }

    void ApplyFov(float flightActive, float progress, float impactWeight)
    {
        if (targetCamera == null) return;

        // Basis-FOV: der SC_FPSController setzt playerCamera.fieldOfView jeden Frame
        // (Speed-FOV). Wir lesen den AKTUELLEN Wert als Basis und legen unsere
        // Beats ADDITIV drauf -> kein Konflikt mit dem bestehenden FOV-Tween,
        // weil wir in LateUpdate NACH dem Controller laufen.
        float fov = targetCamera.fieldOfView;

        // Beat 1: Snap-In-Dip (negativ -> FOV nach innen)
        if (phase == GrapplePhase.Charging && snapInDuration > 0.0001f)
        {
            float t = Mathf.Clamp01(snapInTimer / snapInDuration);
            float dip = snapInCurve.Evaluate(t) * snapInFovDip * comboScale;
            fov -= dip;
        }

        // Beat 2: Flight-Push (positiv -> FOV auf)
        float push = flightFovCurve.Evaluate(progress) * flightFovPush * flightActive * comboScale;
        fov += push;

        targetCamera.fieldOfView = fov;
        // Hinweis: der bestehende Impact-FOV-Bounce im SC_FPSController bleibt
        // erhalten und ueberlagert sich konstruktiv mit Beat 3 (Aberration/Vignette).
    }

    void ApplyPostProcessing(float flightActive, float progress, float impactWeight)
    {
        // ---- Lens Distortion ----
        float distFlight = distortionCurve.Evaluate(progress) * flightDistortion * flightActive * comboScale;
        float distImpact = impactDistortionKick * impactWeight * comboScale;
        lensDistortion.intensity.value = Mathf.Clamp(baseDistortion + distFlight + distImpact, -1f, 1f);

        // ---- Chromatic Aberration ----
        float abFlight = aberrationCurve.Evaluate(progress) * flightAberration * flightActive * comboScale;
        float abImpact = impactAberrationSpike * impactWeight * comboScale;
        chromaticAberration.intensity.value = Mathf.Clamp01(baseAberration + abFlight + abImpact);

        // ---- Vignette ----
        float vgFlight = vignetteCurve.Evaluate(progress) * flightVignette * flightActive * comboScale;
        float vgImpact = impactVignettePunch * impactWeight * comboScale;
        vignette.intensity.value = Mathf.Clamp01(baseVignette + vgFlight + vgImpact);
    }

    float ComputeComboScale()
    {
        if (!scaleWithCombo || velocityMultiplier == null) return 1f;

        float mult = velocityMultiplier.GetCurrentMultiplier(); // 1.0 .. maxComboMultiplier
        float maxMult = Mathf.Max(1.01f, velocityMultiplier.maxComboMultiplier);
        float norm = Mathf.InverseLerp(1f, maxMult, mult); // 0..1
        return Mathf.Lerp(comboInfluenceMin, comboInfluenceMax, norm);
    }

    void OnDisable()
    {
        // Sicherheits-Reset, damit kein Effekt "haengen" bleibt, falls das Script
        // mitten im Grapple deaktiviert wird.
        if (volumeReady)
        {
            lensDistortion.intensity.value = baseDistortion;
            chromaticAberration.intensity.value = baseAberration;
            vignette.intensity.value = baseVignette;
        }
    }
}
