using UnityEngine;

/// <summary>
/// Macht den AudioManager szenenübergreifend dauerhaft. Löst sich beim Start
/// aus dem Parent (Game Manager) heraus, da DontDestroyOnLoad nur auf
/// Root-Objekten funktioniert. Verhindert per Singleton-Pattern, dass beim
/// erneuten Laden einer Szene ein zweiter AudioManager entsteht.
/// </summary>
public class PersistentAudioManager : MonoBehaviour
{
    private static PersistentAudioManager instance;

    void Awake()
    {
        // Falls schon ein AudioManager aus einer früheren Szene existiert:
        // dieses Duplikat sofort zerstören und abbrechen.
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;

        // Aus dem Game Manager herauslösen -> zum Root-Objekt machen,
        // sonst würde DontDestroyOnLoad den ganzen Game Manager mitnehmen.
        transform.SetParent(null);

        DontDestroyOnLoad(gameObject);
    }
}