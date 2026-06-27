using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Bindet das TimerHUD.uxml an den TimerController. Benötigt ein UIDocument mit
/// zugewiesenem TimerHUD.uxml als Source Asset auf demselben GameObject.
///
/// Zeigt die Live-Zeit + genau EIN "nächstes Ziel" (Diamant > Gold > Silber > Bronze)
/// an, statt aller Stufen gleichzeitig. Reagiert nur auf TimerController.OnNextTargetChanged
/// statt jeden Frame USS-Klassen umzuschalten - das war die Ursache für das Flackern/
/// Vibrieren (ständiges Re-Layout durch Klassen-Toggles + Transitions bei jedem Frame).
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class TimerHUDController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Referenz zum TimerController (auto-detected wenn leer)")]
    public TimerController timerController;

    [Header("Result-Overlay")]
    public bool showResultOverlay = true;

    [Header("Debug")]
    public bool showDebugInfo = true;

    private UIDocument uiDocument;
    private VisualElement root;

    // HUD-Elemente
    private Label currentTimeLabel;

    private VisualElement targetRow;
    private VisualElement targetIcon;
    private Label targetNameLabel;
    private Label targetTimeLabel;
    private VisualElement targetCheck;

    // Result-Overlay-Elemente
    private VisualElement resultOverlay;
    private VisualElement resultMedalIcon;
    private Label resultTimeLabel;
    private Label resultMedalNameLabel;

    // Cache, um das Zeit-Label nur zu aktualisieren wenn sich der angezeigte
    // Text tatsächlich ändert (vermeidet unnötige Re-Layout-Passes pro Frame)
    private string lastDisplayedTime = "";

    void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
        root = uiDocument.rootVisualElement;

        if (timerController == null)
            timerController = FindObjectOfType<TimerController>();

        if (timerController == null && showDebugInfo)
        {
            Debug.LogError("❌ TimerHUDController: Kein TimerController in der Scene gefunden!");
        }

        QueryElements();
    }

    void QueryElements()
    {
        currentTimeLabel = root.Q<Label>("current-time-label");

        targetRow = root.Q<VisualElement>("target-row");
        targetIcon = root.Q<VisualElement>("target-icon");
        targetNameLabel = root.Q<Label>("target-name-label");
        targetTimeLabel = root.Q<Label>("target-time-label");
        targetCheck = root.Q<VisualElement>("target-check");

        resultOverlay = root.Q<VisualElement>("result-overlay");
        resultMedalIcon = root.Q<VisualElement>("result-medal-icon");
        resultTimeLabel = root.Q<Label>("result-time-label");
        resultMedalNameLabel = root.Q<Label>("result-medal-name-label");

        if (showDebugInfo)
        {
            bool allFound = currentTimeLabel != null && targetRow != null &&
                             targetIcon != null && resultOverlay != null;

            Debug.Log(allFound
                ? "✅ TimerHUDController: Alle UXML-Elemente erfolgreich gefunden."
                : "⚠️ TimerHUDController: Mindestens ein UXML-Element wurde nicht gefunden. Prüfe, ob TimerHUD.uxml korrekt zugewiesen ist.");
        }
    }

    void OnEnable()
    {
        if (timerController != null)
        {
            timerController.OnTimeUpdated += HandleTimeUpdated;
            timerController.OnTimerStopped += HandleTimerStopped;
            timerController.OnTimerReset += HandleTimerReset;
            timerController.OnNextTargetChanged += HandleNextTargetChanged;
        }
    }

    void OnDisable()
    {
        if (timerController != null)
        {
            timerController.OnTimeUpdated -= HandleTimeUpdated;
            timerController.OnTimerStopped -= HandleTimerStopped;
            timerController.OnTimerReset -= HandleTimerReset;
            timerController.OnNextTargetChanged -= HandleNextTargetChanged;
        }
    }

    void Start()
    {
        if (resultOverlay != null)
            resultOverlay.RemoveFromClassList("result-overlay--visible");

        if (timerController == null)
            return;

        HandleTimeUpdated(timerController.GetElapsedTime());
        HandleNextTargetChanged(
            timerController.GetNextTargetRank(),
            timerController.GetTimeForRank(timerController.GetNextTargetRank()),
            timerController.GetElapsedTime() <= timerController.GetTimeForRank(timerController.GetNextTargetRank())
        );
    }

    // ---------------- Live-Updates (läuft jeden Frame, aber sehr leichtgewichtig) ----------------

    void HandleTimeUpdated(float elapsedTime)
    {
        if (currentTimeLabel == null)
            return;

        string formatted = TimerController.FormatTime(elapsedTime);

        // Nur schreiben, wenn sich der Text wirklich geändert hat. .text setzen
        // löst auch bei identischem Wert intern ein Re-Layout aus - das war
        // mit ein Faktor für das wahrgenommene "Vibrieren".
        if (formatted != lastDisplayedTime)
        {
            currentTimeLabel.text = formatted;
            lastDisplayedTime = formatted;
        }
    }

    // ---------------- Nächstes Ziel wechselt (selten, nicht pro Frame) ----------------

    void HandleNextTargetChanged(TimerController.MedalRank rank, float targetTime, bool alreadyReached)
    {
        if (showDebugInfo)
            Debug.Log($"🎯 TimerHUDController: Neues Ziel = {rank} (< {TimerController.FormatTime(targetTime)})");

        ApplyTargetVisuals(rank);

        if (targetTimeLabel != null)
            targetTimeLabel.text = $"< {TimerController.FormatTime(targetTime)}";

        if (targetRow != null)
            targetRow.EnableInClassList("target-row--reached", alreadyReached);

        if (targetCheck != null)
            targetCheck.EnableInClassList("medal-check--done", alreadyReached);
    }

    void ApplyTargetVisuals(TimerController.MedalRank rank)
    {
        string rankLabel = rank switch
        {
            TimerController.MedalRank.Diamond => "Diamant",
            TimerController.MedalRank.Gold => "Gold",
            TimerController.MedalRank.Silver => "Silber",
            TimerController.MedalRank.Bronze => "Bronze",
            _ => "Bronze"
        };

        if (targetNameLabel != null)
        {
            targetNameLabel.text = rankLabel;
            SetRankClass(targetNameLabel, "medal-name", rank);
        }

        if (targetIcon != null)
        {
            SetRankClass(targetIcon, "medal-icon", rank);
        }
    }

    /// <summary>
    /// Entfernt alle bekannten Rank-Suffix-Klassen von einem Element und setzt
    /// die passende neu (z.B. "medal-icon--diamond"). Hält Diamond/Gold/Silber/Bronze
    /// konsistent zwischen Icon und Label.
    /// </summary>
    void SetRankClass(VisualElement element, string baseClass, TimerController.MedalRank rank)
    {
        element.RemoveFromClassList($"{baseClass}--diamond");
        element.RemoveFromClassList($"{baseClass}--gold");
        element.RemoveFromClassList($"{baseClass}--silver");
        element.RemoveFromClassList($"{baseClass}--bronze");
        element.RemoveFromClassList($"{baseClass}--inactive");

        string suffix = rank switch
        {
            TimerController.MedalRank.Diamond => "diamond",
            TimerController.MedalRank.Gold => "gold",
            TimerController.MedalRank.Silver => "silver",
            TimerController.MedalRank.Bronze => "bronze",
            _ => "inactive"
        };

        element.AddToClassList($"{baseClass}--{suffix}");
    }

    // ---------------- Finaler Abschluss ----------------

    void HandleTimerStopped(float finalTime, TimerController.MedalRank finalRank)
    {
        if (showDebugInfo)
            Debug.Log($"🏁 TimerHUDController: HUD finalisiert für {TimerController.FormatTime(finalTime)} ({finalRank})");

        string formatted = TimerController.FormatTime(finalTime);
        if (currentTimeLabel != null)
            currentTimeLabel.text = formatted;

        bool reachedAnything = finalRank != TimerController.MedalRank.None;

        if (targetRow != null)
            targetRow.EnableInClassList("target-row--reached", reachedAnything);

        if (targetCheck != null)
            targetCheck.EnableInClassList("medal-check--done", reachedAnything);

        // Zielzeile zeigt final die tatsächlich erreichte (oder knapp verpasste) Stufe
        ApplyTargetVisuals(finalRank == TimerController.MedalRank.None ? TimerController.MedalRank.Bronze : finalRank);

        if (targetNameLabel != null && finalRank == TimerController.MedalRank.None)
            targetNameLabel.text = "Keine Medaille";

        if (targetTimeLabel != null)
            targetTimeLabel.text = $"Endzeit: {formatted}";

        if (showResultOverlay)
            ShowResult(finalTime, finalRank);
    }

    void ShowResult(float finalTime, TimerController.MedalRank rank)
    {
        if (resultOverlay == null)
            return;

        resultOverlay.AddToClassList("result-overlay--visible");

        if (resultTimeLabel != null)
            resultTimeLabel.text = TimerController.FormatTime(finalTime);

        string rankLabel = rank switch
        {
            TimerController.MedalRank.Diamond => "Diamant",
            TimerController.MedalRank.Gold => "Gold",
            TimerController.MedalRank.Silver => "Silber",
            TimerController.MedalRank.Bronze => "Bronze",
            _ => "Keine Medaille"
        };

        if (resultMedalNameLabel != null)
        {
            resultMedalNameLabel.text = rankLabel;
            SetRankClass(resultMedalNameLabel, "medal-name", rank);
        }

        if (resultMedalIcon != null)
        {
            SetRankClass(resultMedalIcon, "medal-icon", rank);
        }
    }

    // ---------------- Reset ----------------

    void HandleTimerReset()
    {
        lastDisplayedTime = "";

        if (currentTimeLabel != null)
            currentTimeLabel.text = TimerController.FormatTime(0f);

        if (targetRow != null)
            targetRow.RemoveFromClassList("target-row--reached");

        if (targetCheck != null)
            targetCheck.RemoveFromClassList("medal-check--done");

        if (resultOverlay != null)
            resultOverlay.RemoveFromClassList("result-overlay--visible");
    }
}
