using UnityEngine;
using System.Collections;

/// <summary>
/// Persistent AudioManager mit automatischem Crossfade zwischen zufälligen Tracks.
/// Verhindert per Singleton-Pattern, dass beim erneuten Laden einer Szene ein 
/// zweiter AudioManager entsteht.
/// </summary>
public class PersistentAudioManager : MonoBehaviour
{
    private static PersistentAudioManager instance;

    [Header("Playlist")]
    [Tooltip("Ziehe hier deine MP3s aus dem Assets/Music Ordner rein")]
    public AudioClip[] playlist;

    [Header("Crossfade Settings")]
    [Tooltip("Dauer des Übergangs in Sekunden")]
    public float crossfadeDuration = 3.0f;
    public float maxVolume = 1.0f;

    // Zwei AudioSources für den weichen Übergang (Deck A und Deck B)
    private AudioSource deckA;
    private AudioSource deckB;
    private bool isDeckAActive = true;
    private Coroutine fadeRoutine;

    void Awake()
    {
        // Falls schon ein AudioManager aus einer früheren Szene existiert:
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);

        InitializeAudioDecks();
    }

    void Start()
    {
        PlayNextTrack();
    }

    void Update()
    {
        // Das gerade aktive Deck ermitteln
        AudioSource activeDeck = isDeckAActive ? deckA : deckB;

        // Prüfen, ob der Track bald zu Ende ist, um rechtzeitig den Fade zu starten
        if (activeDeck.isPlaying && activeDeck.clip != null)
        {
            float timeRemaining = activeDeck.clip.length - activeDeck.time;
            if (timeRemaining <= crossfadeDuration)
            {
                PlayNextTrack();
            }
        }
        else if (!activeDeck.isPlaying && playlist.Length > 0)
        {
            // Fallback, falls die Musik unerwartet komplett gestoppt ist
            PlayNextTrack();
        }
    }

    private void InitializeAudioDecks()
    {
        deckA = gameObject.AddComponent<AudioSource>();
        deckB = gameObject.AddComponent<AudioSource>();

        deckA.loop = false;
        deckB.loop = false;
        deckA.playOnAwake = false;
        deckB.playOnAwake = false;
        deckA.volume = 0f;
        deckB.volume = 0f;
    }

    private void PlayNextTrack()
    {
        if (playlist == null || playlist.Length == 0)
        {
            Debug.LogWarning("Die Playlist ist leer! Bitte MP3s im Inspector zuweisen.");
            return;
        }

        // Zufälligen Track wählen
        AudioClip nextClip = playlist[Random.Range(0, playlist.Length)];

        AudioSource activeDeck = isDeckAActive ? deckA : deckB;
        AudioSource nextDeck = isDeckAActive ? deckB : deckA;

        nextDeck.clip = nextClip;
        nextDeck.Play();

        // Falls noch ein alter Übergang läuft, diesen stoppen
        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
        }

        fadeRoutine = StartCoroutine(CrossfadeMix(activeDeck, nextDeck));

        // Aktives Deck für den nächsten Durchlauf tauschen
        isDeckAActive = !isDeckAActive;
    }

    private IEnumerator CrossfadeMix(AudioSource fadingOut, AudioSource fadingIn)
    {
        float timeElapsed = 0f;

        while (timeElapsed < crossfadeDuration)
        {
            timeElapsed += Time.deltaTime;
            float normalizedTime = timeElapsed / crossfadeDuration;

            fadingOut.volume = Mathf.Lerp(maxVolume, 0f, normalizedTime);
            fadingIn.volume = Mathf.Lerp(0f, maxVolume, normalizedTime);

            yield return null;
        }

        fadingOut.volume = 0f;
        fadingOut.Stop();
        fadingIn.volume = maxVolume;
    }
}