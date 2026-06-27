using UnityEngine;

/// <summary>
/// Sammelt rohen Input an einer Stelle. States lesen Input ausschließlich über
/// diese Klasse statt direkt Input.GetAxis/GetKeyDown aufzurufen - das hält
/// Eingabe-Logik von Bewegungs-Logik getrennt und macht spätere Anpassungen
/// (Rebinding, Controller-Support) einfacher, ohne jedes State-Skript anfassen
/// zu müssen.
/// </summary>
public class PlayerInputContext : MonoBehaviour
{
    [Header("Key Bindings")]
    public KeyCode actionKey = KeyCode.Space;
    public KeyCode sprintKey = KeyCode.LeftShift;
    public KeyCode slideKey = KeyCode.LeftControl;

    [Header("Maus")]
    public float mouseSensitivityX = 2.0f;
    public float mouseSensitivityY = 2.0f;

    // Gecachte Werte, einmal pro Frame in Update() gelesen, damit mehrere States
    // im selben Frame denselben Wert sehen (Input.GetAxis kann sich theoretisch
    // zwischen mehreren Aufrufen im selben Frame leicht unterscheiden).
    private float moveForwardInput;
    private float moveRightInput;
    private float mouseXInput;
    private float mouseYInput;
    private bool actionKeyDownThisFrame;
    private bool sprintHeld;
    private bool slideKeyDownThisFrame;
    private bool slideKeyHeld;

    void Update()
    {
        moveForwardInput = Input.GetAxisRaw("Vertical");
        moveRightInput = Input.GetAxisRaw("Horizontal");

        mouseXInput = Input.GetAxis("Mouse X") * mouseSensitivityX;
        mouseYInput = Input.GetAxis("Mouse Y") * mouseSensitivityY;

        actionKeyDownThisFrame = Input.GetKeyDown(actionKey);
        sprintHeld = Input.GetKey(sprintKey);
        slideKeyDownThisFrame = Input.GetKeyDown(slideKey);
        slideKeyHeld = Input.GetKey(slideKey);
    }

    // ---------------- Public Read-API ----------------

    /// <summary>Rohe Bewegungseingabe als Vector2 (x = rechts/links, y = vor/zurück), NICHT normalisiert.</summary>
    public Vector2 GetMoveInput() => new Vector2(moveRightInput, moveForwardInput);

    public float GetMouseX() => mouseXInput;
    public float GetMouseY() => mouseYInput;

    /// <summary>
    /// Die "eine Taste" für Jump/Walljump/Vault/Climb (siehe PlayerActionResolver
    /// für die Prioritäten-Auswertung, welcher Kontext bei Druck greift).
    /// </summary>
    public bool ActionPressedThisFrame() => actionKeyDownThisFrame;

    public bool IsSprintHeld() => sprintHeld;
    public bool SlidePressedThisFrame() => slideKeyDownThisFrame;
    public bool IsSlideHeld() => slideKeyHeld;
}
