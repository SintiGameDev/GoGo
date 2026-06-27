using UnityEngine;

/// <summary>
/// Die EINZIGE Stelle, die auf die Action-Taste (Leertaste) reagiert. Wertet
/// die Prioritätenkette aus: Climb > Vault > Walljump > normaler Jump - die
/// erste Fähigkeit, deren Bedingungen erfüllt sind, wird ausgeführt, der Rest
/// wird nicht einmal geprüft. Andere Skripte (GroundMovement, WalljumpHandler,
/// VaultHandler, ClimbHandler) lauschen selbst NICHT auf Input, sondern stellen
/// nur TryXyz()-Methoden bereit, die hier zentral aufgerufen werden - das
/// verhindert, dass mehrere States um dieselbe Taste konkurrieren oder sich
/// gegenseitig überschreiben.
/// </summary>
public class PlayerActionResolver : MonoBehaviour
{
    [Header("References")]
    public PlayerInputContext input;
    public GroundMovement groundMovement;
    public WalljumpHandler walljumpHandler;

    [Tooltip("Vault über niedrige Hindernisse (Priorität 2, nach Climb)")]
    public VaultHandler vaultHandler;

    [Tooltip("Climb an hohen Kanten (Priorität 1, höchste)")]
    public ClimbHandler climbHandler;

    [Header("Debug")]
    public bool showDebugInfo = true;

    void Awake()
    {
        if (input == null) input = GetComponent<PlayerInputContext>();
        if (groundMovement == null) groundMovement = GetComponent<GroundMovement>();
        if (walljumpHandler == null) walljumpHandler = GetComponent<WalljumpHandler>();
        if (vaultHandler == null) vaultHandler = GetComponent<VaultHandler>();
        if (climbHandler == null) climbHandler = GetComponent<ClimbHandler>();
    }

    void Update()
    {
        if (!input.ActionPressedThisFrame())
            return;

        ResolveAction();
    }

    void ResolveAction()
    {
        // Priorität 1: Climb (Kante in Reichweite)
        if (TryClimb())
        {
            Log("Climb ausgelöst");
            return;
        }

        // Priorität 2: Vault (niedriges Hindernis erkannt)
        if (TryVault())
        {
            Log("Vault ausgelöst");
            return;
        }

        // Priorität 3: Walljump (an Wand im Sprung)
        if (walljumpHandler != null && walljumpHandler.TryWalljump())
        {
            Log("Walljump ausgelöst");
            return;
        }

        // Priorität 4 (Fallback): normaler Jump (nur falls am Boden)
        if (groundMovement != null && groundMovement.TryJump())
        {
            Log("Normaler Jump ausgelöst");
            return;
        }

        Log("Action-Taste gedrückt, aber kein Kontext passend (in der Luft, keine Wand, kein Boden)");
    }

    bool TryClimb()
    {
        return climbHandler != null && climbHandler.TryPerform();
    }

    bool TryVault()
    {
        return vaultHandler != null && vaultHandler.TryPerform();
    }

    void Log(string message)
    {
        if (showDebugInfo)
            Debug.Log($"[PlayerActionResolver] {message}");
    }
}

/// <summary>
/// Gemeinsames Interface für Action-Taste-Handler (Vault, Climb), damit der
/// Resolver sie generisch aufrufen kann, ohne harte Abhängigkeit auf die
/// konkreten Klassen zu haben, bevor diese existieren.
/// </summary>
public interface IActionHandler
{
    bool TryPerform();
}
