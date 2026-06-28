using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Projectile : MonoBehaviour
{
    [Header("Flugeigenschaften")]
    [SerializeField] private float geschwindigkeit = 10f;
    [SerializeField] private float lebensdauer = 5f;

    [Header("Homing (Zielsuche)")]
    [Tooltip("Aktiviert zielsuchendes Verhalten.")]
    [SerializeField] private bool isHoming = false;
    [Tooltip("Stärke der Kurskorrektur. Höher bedeutet aggressiveres Homing.")]
    [SerializeField] private float homingStaerke = 5f;

    [Header("Visuals")]
    [Tooltip("Zuweisbarer Renderer für eigene Materials.")]
    [SerializeField] private MeshRenderer zielRenderer;

    private Transform zielTransform;
    private Rigidbody rb;
    private Vector3 festeFlugrichtung;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        // Verhindert, dass die Kugel direkt nach unten fällt
        rb.useGravity = false; 
        
        Destroy(gameObject, lebensdauer); 
        
        if (zielRenderer == null)
        {
            zielRenderer = GetComponentInChildren<MeshRenderer>();
        }
    }

    public void Initialisiere(Transform ziel, Vector3 startRichtung)
    {
        zielTransform = ziel;
        festeFlugrichtung = startRichtung.normalized;
        
        if (!isHoming)
        {
            transform.forward = festeFlugrichtung;
            rb.linearVelocity = festeFlugrichtung * geschwindigkeit;
        }
    }

    public void SetzeMaterial(Material mat)
    {
        if (zielRenderer != null && mat != null)
        {
            zielRenderer.material = mat;
        }
    }

    private void FixedUpdate()
    {
        if (isHoming && zielTransform != null)
        {
            Vector3 richtungZumZiel = (zielTransform.position - rb.position).normalized;
            Vector3 neueRichtung = Vector3.Slerp(transform.forward, richtungZumZiel, homingStaerke * Time.fixedDeltaTime);
            
            rb.linearVelocity = neueRichtung * geschwindigkeit;
            transform.forward = neueRichtung;
        }
        else if (isHoming && zielTransform == null)
        {
            rb.linearVelocity = transform.forward * geschwindigkeit;
        }
    }

    private void OnTriggerEnter(Collider anderer)
    {
        if (anderer.CompareTag("Enemy")) return;

        if (anderer.CompareTag("Player"))
        {
            SceneDirector director = FindFirstObjectByType<SceneDirector>();
            if (director != null)
            {
                director.ReloadCurrentScene();
            }
            else
            {
                GameManager gm = FindFirstObjectByType<GameManager>();
                if (gm != null)
                {
                    gm.RestartButton();
                }
            }
        }

        Destroy(gameObject);
    }
}