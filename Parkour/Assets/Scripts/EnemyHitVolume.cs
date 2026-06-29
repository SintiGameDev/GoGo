using UnityEngine;

// Sitzt auf dem unsichtbaren Child-GameObject, das den SphereCollider und
// den "Grabbable"-Tag traegt. Faengt Raycasts/Grab-Logik ab, OHNE dass der
// Tess-Shader (Vertex-Displacement) auf dem Parent die Collision/Hit-Geometrie
// beeinflusst. enemyRoot zeigt zurueck auf das eigentliche Enemy-Objekt mit
// Renderer/Shader/Highlight - das ist es, was am Ende zerstoert wird.
public class EnemyHitVolume : MonoBehaviour
{
    [Tooltip("Das eigentliche Enemy-Parent-Objekt mit Mesh/Renderer/Shader. " +
             "Wird beim Destroy zerstoert (zerstoert dieses Child automatisch mit).")]
    public GameObject enemyRoot;

    void Reset()
    {
        // Komfort im Editor: falls leer, automatisch den Parent annehmen.
        if (enemyRoot == null && transform.parent != null)
        {
            enemyRoot = transform.parent.gameObject;
        }
    }
}
