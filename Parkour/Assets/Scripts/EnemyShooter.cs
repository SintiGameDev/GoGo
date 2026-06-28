using UnityEngine;

public class EnemyShooter : MonoBehaviour
{
    [Header("Schuss Parameter")]
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform firePoint;
    [SerializeField] private float schussIntervall = 2f;
    
    [Header("Sicht und Ziel")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private float sichtweite = 15f;
    [Tooltip("Layer der Wände oder Böden, die die Sicht blockieren.")]
    [SerializeField] private LayerMask hindernisLayer; 
    
    [Header("Projektil Visuals")]
    [SerializeField] private Material projektilMaterial;

    private Transform spieler;
    private float letzterSchussZeit;

    private void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag(playerTag);
        if (playerObj != null)
        {
            spieler = playerObj.transform;
        }
        
        if (firePoint == null)
        {
            firePoint = transform; 
        }
    }

    private void Update()
    {
        if (spieler == null) return;

        if (Time.time >= letzterSchussZeit + schussIntervall)
        {
            if (HatSichtlinieZumSpieler())
            {
                Schiesse();
                letzterSchussZeit = Time.time;
            }
        }
    }

    private bool HatSichtlinieZumSpieler()
    {
        Vector3 richtungZumSpieler = spieler.position - firePoint.position;
        float distanz = richtungZumSpieler.magnitude;

        if (distanz > sichtweite) return false;

        // Echter 3D Raycast. Gibt true zurück, wenn ein Hindernis getroffen wird
        if (Physics.Raycast(firePoint.position, richtungZumSpieler.normalized, out RaycastHit hit, distanz, hindernisLayer))
        {
            return false; 
        }

        return true; 
    }

    private void Schiesse()
    {
        if (projectilePrefab == null) return;

        GameObject projObj = Instantiate(projectilePrefab, firePoint.position, Quaternion.identity);
        Projectile proj = projObj.GetComponent<Projectile>();
        
        if (proj != null)
        {
            Vector3 richtung = (spieler.position - firePoint.position).normalized;
            proj.Initialisiere(spieler, richtung);
            
            if (projektilMaterial != null)
            {
                proj.SetzeMaterial(projektilMaterial);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, sichtweite);
        
        if (firePoint != null && spieler != null)
        {
            Gizmos.color = HatSichtlinieZumSpieler() ? Color.red : Color.gray;
            Gizmos.DrawLine(firePoint.position, spieler.position);
        }
    }
}