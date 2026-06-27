using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(AudioSource))]
public class FootstepController : MonoBehaviour
{
    // --- Konfigurierbare Einstellungen ---

    [Header("Sound-Einstellungen")]
    [Tooltip("Die AudioClips, die als Schrittgeräusche verwendet werden (werden nacheinander abgespielt).")]
    public List<AudioClip> footstepSounds;

    [Tooltip("Der minimale und maximale Pitch (Tonhöhe) für die Schrittgeräusche.")]
    public Vector2 pitchRange = new Vector2(0.9f, 1.1f);

    [Header("Timing-Einstellungen")]
    [Tooltip("Der Zeitabstand zwischen den Schritten beim GEHEN.")]
    public float timeBetweenWalkSteps = 0.5f;

    [Tooltip("Der Zeitabstand zwischen den Schritten beim LAUFEN (muss kleiner sein!).")]
    public float timeBetweenRunSteps = 0.3f;

    // --- Private Variablen ---

    private AudioSource audioSource;
    private SC_FPSController playerController;
    private CharacterController characterController;
    private float stepTimer;
    private float currentStepInterval; // Aktueller Intervall, entweder Gehen oder Laufen
    private int currentStepIndex = 0; // Index für das sequenzielle Abspielen der Sounds

    void Start()
    {
        // Komponenten abrufen
        audioSource = GetComponent<AudioSource>();
        playerController = GetComponent<SC_FPSController>();
        characterController = GetComponent<CharacterController>();

        // Sicherstellen, dass die Audio-Clips zugewiesen sind
        if (footstepSounds == null || footstepSounds.Count == 0)
        {
            Debug.LogError("Keine Schrittgeräusche zugewiesen! Bitte fügen Sie AudioClips im Inspector hinzu.");
            enabled = false;
            return;
        }

        // Initialen Schrittintervall setzen und Timer vorbereiten
        currentStepInterval = timeBetweenWalkSteps;
        stepTimer = currentStepInterval;

        // Kleine Vorbereitung für die AudioSource
        audioSource.loop = false; // Wir spielen manuell ab
        audioSource.playOnAwake = false; // Wir spielen manuell ab
    }

    void Update()
    {
        // 1. Bewegungs- und Bodenstatus prüfen
        // Prüfen, ob der Spieler am Boden ist UND sich bewegt
        // Wir nutzen characterController.velocity.sqrMagnitude, um echte Bewegung zu messen
        if (characterController.isGrounded && playerController.canMove && characterController.velocity.sqrMagnitude > 0.1f)
        {
            // 2. Laufstatus prüfen und Intervall anpassen
            // Abfragen, ob der Spieler die Shift-Taste zum Laufen gedrückt hält
            bool isRunning = Input.GetKey(KeyCode.LeftShift);

            // Den Schrittintervall basierend auf Gehen oder Laufen setzen
            currentStepInterval = isRunning ? timeBetweenRunSteps : timeBetweenWalkSteps;

            // 3. Schritt-Timer aktualisieren
            stepTimer -= Time.deltaTime;

            if (stepTimer <= 0)
            {
                PlayFootstepSoundSequentially();
                // Timer zurücksetzen auf den aktuellen Intervall
                stepTimer = currentStepInterval;
            }
        }
        else
        {
            // Wenn der Spieler nicht am Boden ist oder sich nicht bewegt,
            // setzen wir den Timer zurück, damit beim nächsten Start sofort ein Sound kommt.
            stepTimer = 0f;
            // Optional: Timer = currentStepInterval; // Wenn man möchte, dass der erste Schritt eine volle Zeit braucht
        }
    }

    /// <summary>
    /// Spielt das nächste Schrittgeräusch in der Liste mit zufälliger Tonhöhen-Varianz ab.
    /// </summary>
    private void PlayFootstepSoundSequentially()
    {
        // Tonhöhen-Varianz hinzufügen: 
        // Wählt eine zufällige Tonhöhe zwischen pitchRange.x (Min) und pitchRange.y (Max).
        audioSource.pitch = Random.Range(pitchRange.x, pitchRange.y);

        // Aktuellen AudioClip wählen
        AudioClip stepClip = footstepSounds[currentStepIndex];

        // Sound abspielen. PlayOneShot verhindert, dass ein aktuell laufender Sound unterbrochen wird.
        audioSource.PlayOneShot(stepClip);

        // Index für den nächsten Sound in der Liste erhöhen und "wrappen" (zurück zum Anfang)
        // Beispiel bei 4 Sounds: 0 -> 1 -> 2 -> 3 -> 0 -> 1 ...
        currentStepIndex = (currentStepIndex + 1) % footstepSounds.Count;

        // Kleine Warnung: Wenn die Liste nur ein Element enthält, wird es zwar abgespielt,
        // aber der sequenzielle Effekt ist natürlich nicht vorhanden.
    }
}