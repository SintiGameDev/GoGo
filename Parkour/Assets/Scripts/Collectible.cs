using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Collectible : MonoBehaviour
{
    [Header("Collectible Settings")]
    public string collectibleTag = "Player"; // Tag des Spielers
    public bool rotateCollectible = true; // Soll sich das Collectible drehen?
    public float rotationSpeed = 50f; // Rotationsgeschwindigkeit

    [Header("Animation")]
    [Tooltip("Animator Component (wird automatisch gesucht falls leer)")]
    public Animator animator;

    [Tooltip("Name des Animation Triggers")]
    public string collectTriggerName = "Collect";

    [Tooltip("Dauer der Collect-Animation (danach wird zerstört)")]
    public float animationDuration = 1f;

    [Tooltip("Automatische Dauer aus Animation Clip holen")]
    public bool autoDetectAnimationDuration = true;

    [Header("Effects")]
    public GameObject collectEffect; // Optional: Partikel-Effekt beim Einsammeln
    public AudioClip collectSound; // Optional: Sound beim Einsammeln

    [Tooltip("AudioSource des Spielers wird automatisch gesucht")]
    public AudioSource playerAudioSource;

    [Tooltip("Falls keine AudioSource gefunden wird, fallback zu PlayClipAtPoint")]
    public bool useFallbackAudio = true;

    [Header("Shepard Tone Settings")]
    [Tooltip("Shepard Tone Effekt aktivieren (immer steigender Pitch)")]
    public bool useShepardTone = true;

    [Tooltip("Start-Pitch (Standard: 1.0)")]
    public float startPitch = 0.8f;

    [Tooltip("Pitch-Erhöhung pro Collectible")]
    public float pitchIncrement = 0.1f;

    [Tooltip("Maximaler Pitch bevor Reset (Shepard Zyklus)")]
    public float maxPitch = 2.0f;

    [Tooltip("Minimaler Pitch nach Reset")]
    public float minPitch = 0.8f;

    [Tooltip("Anzahl der Oktaven für Shepard Effekt")]
    [Range(2, 4)]
    public int shepardOctaves = 3;

    private static float currentPitch = 0.8f;
    private static bool pitchInitialized = false;

    [Header("Destruction")]
    [Tooltip("Auch alle Child-Objekte zerstören")]
    public bool destroyChildren = true;

    [Tooltip("Collider sofort deaktivieren (verhindert mehrfaches Einsammeln)")]
    public bool disableColliderImmediately = true;

    [Header("Debug")]
    public bool showDebugInfo = false;

    private static int totalCollectibles = 0;
    private static int collectedCount = 0;
    private static bool isInitialized = false;
    private bool isCollected = false;

    void Awake()
    {
        // Beim ersten Collectible alle zählen
        if (!isInitialized)
        {
            CountAllCollectibles();
            isInitialized = true;
            collectedCount = 0;
        }

        // Pitch initialisieren
        if (!pitchInitialized)
        {
            currentPitch = startPitch;
            pitchInitialized = true;
        }

        // Animator automatisch finden
        if (animator == null)
        {
            animator = GetComponent<Animator>();

            // Auch in Children suchen
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            if (animator != null && showDebugInfo)
            {
                Debug.Log($"✅ Animator gefunden auf: {animator.gameObject.name}");
            }
        }

        // Automatische Dauer erkennen
        if (autoDetectAnimationDuration && animator != null)
        {
            DetectAnimationDuration();
        }
    }

    void Start()
    {
        // Sicherstellen, dass ein Collider vorhanden ist
        Collider col = GetComponent<Collider>();
        if (col == null)
        {
            Debug.LogWarning($"⚠️ Collectible '{gameObject.name}' hat keinen Collider! Füge einen Collider hinzu.");
        }
        else
        {
            col.isTrigger = true; // Collectibles sollten Trigger sein
        }

        // Validierung
        if (animator == null)
        {
            Debug.LogWarning($"⚠️ Collectible '{gameObject.name}': Kein Animator gefunden! Animation wird nicht abgespielt.");
        }
    }

    void Update()
    {
        // Optional: Collectible rotieren lassen (nur wenn nicht eingesammelt)
        if (rotateCollectible && !isCollected)
        {
            transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (isCollected) return;

        // Prüfen ob Spieler das Collectible berührt
        if (other.CompareTag(collectibleTag))
        {
            // Finde Player AudioSource falls noch nicht gefunden
            if (playerAudioSource == null)
            {
                FindPlayerAudioSource(other.gameObject);
            }

            CollectItem();
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (isCollected) return;

        // Fallback falls kein Trigger verwendet wird
        if (collision.gameObject.CompareTag(collectibleTag))
        {
            // Finde Player AudioSource falls noch nicht gefunden
            if (playerAudioSource == null)
            {
                FindPlayerAudioSource(collision.gameObject);
            }

            CollectItem();
        }
    }

    void FindPlayerAudioSource(GameObject playerObject)
    {
        // Suche AudioSource direkt am Player-Objekt
        playerAudioSource = playerObject.GetComponent<AudioSource>();

        // Falls nicht gefunden, suche in Children
        if (playerAudioSource == null)
        {
            playerAudioSource = playerObject.GetComponentInChildren<AudioSource>();
        }

        // Falls nicht gefunden, suche im Parent
        if (playerAudioSource == null && playerObject.transform.parent != null)
        {
            playerAudioSource = playerObject.GetComponentInParent<AudioSource>();
        }

        if (playerAudioSource != null && showDebugInfo)
        {
            Debug.Log($"🔊 Player AudioSource gefunden: {playerAudioSource.gameObject.name}");
        }
        else if (showDebugInfo)
        {
            Debug.LogWarning($"⚠️ Keine AudioSource am Player gefunden!");
        }
    }

    void PlayCollectSound()
    {
        if (collectSound == null) return;

        // Verwende Player AudioSource wenn vorhanden
        if (playerAudioSource != null)
        {
            if (useShepardTone)
            {
                PlayShepardToneSound();
            }
            else
            {
                // Normaler Sound ohne Pitch-Änderung
                playerAudioSource.PlayOneShot(collectSound);

                if (showDebugInfo)
                {
                    Debug.Log($"🔊 Sound über Player AudioSource abgespielt: {collectSound.name}");
                }
            }
        }
        // Fallback: PlayClipAtPoint verwenden
        else if (useFallbackAudio)
        {
            AudioSource.PlayClipAtPoint(collectSound, transform.position);

            if (showDebugInfo)
            {
                Debug.Log($"🔊 Sound via PlayClipAtPoint abgespielt (Fallback): {collectSound.name}");
            }
        }
        else if (showDebugInfo)
        {
            Debug.LogWarning($"⚠️ Keine AudioSource verfügbar und Fallback deaktiviert!");
        }
    }

    void PlayShepardToneSound()
    {
        if (playerAudioSource == null || collectSound == null) return;

        // Berechne aktuellen Pitch für Shepard Effekt
        float shepardPitch = CalculateShepardPitch();

        // Spiele mehrere Oktaven übereinander für echten Shepard-Effekt
        StartCoroutine(PlayShepardLayers(shepardPitch));

        // Erhöhe Pitch für nächstes Collectible
        currentPitch += pitchIncrement;

        // Shepard Cycle: Wenn Maximum erreicht, fade zurück zu Minimum
        if (currentPitch >= maxPitch)
        {
            currentPitch = minPitch;
            if (showDebugInfo)
            {
                Debug.Log($"🔄 Shepard Cycle Reset: Pitch zurück zu {minPitch}");
            }
        }

        if (showDebugInfo)
        {
            Debug.Log($"🎵 Shepard Tone: Pitch = {shepardPitch:F2}, Nächster Pitch = {currentPitch:F2}");
        }
    }

    float CalculateShepardPitch()
    {
        // Normalisiere Pitch im Shepard-Zyklus (0.0 - 1.0)
        float normalizedPitch = (currentPitch - minPitch) / (maxPitch - minPitch);

        // Verwende aktuellen Pitch als Basis
        return currentPitch;
    }

    IEnumerator PlayShepardLayers(float basePitch)
    {
        if (playerAudioSource == null) yield break;

        // Spiele Haupt-Sound mit aktuellem Pitch
        float mainVolume = 0.3f;

        // Berechne Position im Shepard-Zyklus (0.0 - 1.0)
        float cyclePosition = (currentPitch - minPitch) / (maxPitch - minPitch);

        // Erstelle temporäre AudioSources für zusätzliche Oktaven
        GameObject tempAudioHost = new GameObject("ShepardToneHost");
        tempAudioHost.transform.position = playerAudioSource.transform.position;

        List<AudioSource> layerSources = new List<AudioSource>();

        // Haupt-Layer (aktuelle Oktave)
        AudioSource mainLayer = tempAudioHost.AddComponent<AudioSource>();
        ConfigureAudioSource(mainLayer, playerAudioSource);
        mainLayer.pitch = basePitch;
        mainLayer.volume = mainVolume * CalculateLayerVolume(cyclePosition, 0);
        mainLayer.PlayOneShot(collectSound);
        layerSources.Add(mainLayer);

        // Zusätzliche Oktaven für Shepard-Effekt
        for (int i = 1; i < shepardOctaves; i++)
        {
            AudioSource layer = tempAudioHost.AddComponent<AudioSource>();
            ConfigureAudioSource(layer, playerAudioSource);

            // Jede Schicht ist eine Oktave tiefer
            layer.pitch = basePitch * Mathf.Pow(0.5f, i);

            // Volume basierend auf Cycle-Position (fading in/out)
            layer.volume = mainVolume * CalculateLayerVolume(cyclePosition, i);

            layer.PlayOneShot(collectSound);
            layerSources.Add(layer);

            if (showDebugInfo)
            {
                Debug.Log($"   Layer {i}: Pitch = {layer.pitch:F2}, Volume = {layer.volume:F2}");
            }
        }

        // Warte bis Sound fertig ist, dann cleanup
        yield return new WaitForSeconds(collectSound.length + 0.1f);

        Destroy(tempAudioHost);
    }

    void ConfigureAudioSource(AudioSource source, AudioSource template)
    {
        // Kopiere Einstellungen von Player AudioSource
        source.outputAudioMixerGroup = template.outputAudioMixerGroup;
        source.spatialBlend = template.spatialBlend;
        source.reverbZoneMix = template.reverbZoneMix;
        source.dopplerLevel = template.dopplerLevel;
        source.spread = template.spread;
        source.rolloffMode = template.rolloffMode;
        source.minDistance = template.minDistance;
        source.maxDistance = template.maxDistance;
    }

    float CalculateLayerVolume(float cyclePosition, int layerIndex)
    {
        // Shepard Tone Crossfade: Untere Oktaven faden rein wenn obere rausfaden
        // Dies erzeugt die Illusion einer endlos steigenden Tonhöhe

        if (layerIndex == 0)
        {
            // Hauptlayer: fade out wenn Cycle endet
            return Mathf.Lerp(1.0f, 0.0f, cyclePosition);
        }
        else if (layerIndex == 1)
        {
            // Erste untere Oktave: fade in während Cycle
            return Mathf.Lerp(0.0f, 1.0f, cyclePosition);
        }
        else
        {
            // Weitere Oktaven: moderate Lautstärke
            float fadeIn = Mathf.Clamp01(cyclePosition * 2f);
            float fadeOut = Mathf.Clamp01(2f - cyclePosition * 2f);
            return Mathf.Min(fadeIn, fadeOut) * 0.5f;
        }
    }

    void CollectItem()
    {
        if (isCollected) return;
        isCollected = true;

        collectedCount++;

        if (showDebugInfo)
        {
            Debug.Log($"🎯 Collectible gesammelt! {collectedCount}/{totalCollectibles}");
        }

        // Collider sofort deaktivieren
        if (disableColliderImmediately)
        {
            Collider[] colliders = GetComponentsInChildren<Collider>();
            foreach (Collider col in colliders)
            {
                col.enabled = false;
            }

            if (showDebugInfo)
            {
                Debug.Log($"🔒 Collider deaktiviert: {gameObject.name}");
            }
        }

        // Rotation stoppen
        if (rotateCollectible)
        {
            rotateCollectible = false;
        }

        // Optional: Effekte abspielen
        if (collectEffect != null)
        {
            Instantiate(collectEffect, transform.position, Quaternion.identity);
        }

        if (collectSound != null)
        {
            PlayCollectSound();
        }

        // Animation abspielen und dann zerstören
        if (animator != null)
        {
            PlayCollectAnimation();
        }
        else
        {
            // Keine Animation: Sofort zerstören
            DestroyCollectible();
        }
    }

    void PlayCollectAnimation()
    {
        if (animator == null)
        {
            Debug.LogWarning($"⚠️ Kein Animator vorhanden auf {gameObject.name}");
            DestroyCollectible();
            return;
        }

        // Prüfe ob Trigger existiert
        bool triggerExists = false;
        foreach (AnimatorControllerParameter param in animator.parameters)
        {
            if (param.name == collectTriggerName && param.type == AnimatorControllerParameterType.Trigger)
            {
                triggerExists = true;
                break;
            }
        }

        if (!triggerExists)
        {
            Debug.LogWarning($"⚠️ Animator Trigger '{collectTriggerName}' existiert nicht! Erstelle ihn im Animator Controller.");
            DestroyCollectible();
            return;
        }

        // Trigger setzen
        animator.SetTrigger(collectTriggerName);

        if (showDebugInfo)
        {
            Debug.Log($"🎬 Animation '{collectTriggerName}' getriggert, Zerstörung in {animationDuration}s");
        }

        // Nach Animation-Dauer zerstören
        StartCoroutine(DestroyAfterAnimation());
    }

    IEnumerator DestroyAfterAnimation()
    {
        // Warte bis Animation fertig ist
        yield return new WaitForSeconds(animationDuration);

        DestroyCollectible();
    }

    void DestroyCollectible()
    {
        if (showDebugInfo)
        {
            Debug.Log($"💥 Zerstöre Collectible: {gameObject.name}");
        }

        if (destroyChildren)
        {
            // Zerstöre Hauptobjekt (inkl. aller Children)
            Destroy(gameObject);
        }
        else
        {
            // Nur dieses Objekt zerstören, Children bleiben
            foreach (Transform child in transform)
            {
                child.SetParent(null);
            }
            Destroy(gameObject);
        }
    }

    void DetectAnimationDuration()
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            return;

        // Suche nach "Collect" Animation Clip
        AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;

        foreach (AnimationClip clip in clips)
        {
            // Suche nach Clip der "Collect" im Namen hat
            if (clip.name.ToLower().Contains("collect"))
            {
                animationDuration = clip.length;

                if (showDebugInfo)
                {
                    Debug.Log($"✅ Animation-Dauer automatisch erkannt: {animationDuration}s (Clip: {clip.name})");
                }
                return;
            }
        }

        if (showDebugInfo)
        {
            Debug.LogWarning($"⚠️ Keine 'Collect' Animation gefunden. Nutze manuelle Dauer: {animationDuration}s");
        }
    }

    void CountAllCollectibles()
    {
        // Alle Collectibles in der Szene zählen
        Collectible[] allCollectibles = FindObjectsOfType<Collectible>();
        totalCollectibles = allCollectibles.Length;

        Debug.Log($"🎯 Collectibles in Level: {totalCollectibles}");
    }

    // Statische Methoden für externen Zugriff
    public static int GetTotalCollectibles()
    {
        return totalCollectibles;
    }

    public static int GetCollectedCount()
    {
        return collectedCount;
    }

    public static int GetRemainingCollectibles()
    {
        return totalCollectibles - collectedCount;
    }

    public static void ResetCollectibles()
    {
        isInitialized = false;
        collectedCount = 0;
        totalCollectibles = 0;
        pitchInitialized = false;
        currentPitch = 0.8f; // Zurück zum Start-Pitch
    }

    // Wird automatisch aufgerufen wenn Szene gewechselt wird
    void OnDestroy()
    {
        // Wenn alle Collectibles gesammelt wurden
        if (collectedCount >= totalCollectibles && totalCollectibles > 0)
        {
            Debug.Log("🎉 Alle Collectibles gesammelt!");
            // Hier kannst du z.B. ein Event auslösen
        }
    }

    // Gizmo für Editor-Visualisierung
    void OnDrawGizmos()
    {
        if (isCollected) return;

        Gizmos.color = new Color(1f, 0.8f, 0f, 0.5f);
        Gizmos.DrawSphere(transform.position, 0.3f);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 0.3f);
    }
}

// UI Manager für Collectibles Display
public class CollectibleUI : MonoBehaviour
{
    [Header("UI Settings")]
    public Rect uiRect = new Rect(10, 100, 300, 30);
    public GUIStyle labelStyle;
    public bool showUI = true;

    void Start()
    {
        // Standard Style erstellen
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle();
            labelStyle.fontSize = 20;
            labelStyle.fontStyle = FontStyle.Bold;
            labelStyle.normal.textColor = Color.white;
        }
    }

    void OnGUI()
    {
        if (!showUI) return;

        int total = Collectible.GetTotalCollectibles();
        int collected = Collectible.GetCollectedCount();
        int remaining = Collectible.GetRemainingCollectibles();

        if (total > 0)
        {
            // Schwarzer Hintergrund für bessere Lesbarkeit
            GUI.Box(new Rect(uiRect.x - 5, uiRect.y - 5, uiRect.width + 10, uiRect.height + 10), "");

            string text = $"Collectibles: {collected}/{total}";
            if (remaining > 0)
            {
                text += $" (Remaining: {remaining})";
            }
            else
            {
                text += " ✓ ALL COLLECTED!";
            }

            GUI.Label(uiRect, text, labelStyle);
        }
    }
}