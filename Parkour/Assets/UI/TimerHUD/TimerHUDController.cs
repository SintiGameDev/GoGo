using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Bindet das TimerHUD.uxml an den TimerController. Benötigt ein UIDocument mit
/// zugewiesenem TimerHUD.uxml als Source Asset auf demselben GameObject.
/// Kein manuelles Verschieben von TMPro-Objekten nötig - Layout kommt komplett aus UXML/USS.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class TimerHUDController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Referenz zum TimerController (auto-detected wenn leer)")]
    public TimerController timerController;

    [Header("Result-Overlay")]
    [Tooltip("Wie lange das Result-Overlay nach Zielankunft sichtbar bleibt, bevor SceneDirector übernimmt (rein optisch, blockiert nichts)")]
    public bool showResultOverlay = true;

    [Header("Debug")]
    public bool showDebugInfo = true;

    private UIDocument uiDocument;
    private VisualElement root;

    // HUD-Elemente
    private Label currentTimeLabel;

    private VisualElement goldRow, silverRow, bronzeRow;
    private VisualElement goldIcon, silverIcon, bronzeIcon;
    private VisualElement goldCheck, silverCheck, bronzeCheck;
    private Label goldTargetLabel, silverTargetLabel, bronzeTargetLabel;

    // Result-Overlay-Elemente
    private VisualElement resultOverlay;
    private VisualElement resultMedalIcon;
    private Label resultTimeLabel;
    private Label resultMedalNameLabel;

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

        goldRow = root.Q<VisualElement>("gold-row");
        silverRow = root.Q<VisualElement>("silver-row");
        bronzeRow = root.Q<VisualElement>("bronze-row");

        goldIcon = root.Q<VisualElement>("gold-icon");
        silverIcon = root.Q<VisualElement>("silver-icon");
        bronzeIcon = root.Q<VisualElement>("bronze-icon");

        goldCheck = root.Q<VisualElement>("gold-check");
        silverCheck = root.Q<VisualElement>("silver-check");
        bronzeCheck = root.Q<VisualElement>("bronze-check");

        goldTargetLabel = root.Q<Label>("gold-target-label");
        silverTargetLabel = root.Q<Label>("silver-target-label");
        bronzeTargetLabel = root.Q<Label>("bronze-target-label");

        resultOverlay = root.Q<VisualElement>("result-overlay");
        resultMedalIcon = root.Q<VisualElement>("result-medal-icon");
        resultTimeLabel = root.Q<Label>("result-time-label");
        resultMedalNameLabel = root.Q<Label>("result-medal-name-label");

        if (showDebugInfo)
        {
            bool allFound = currentTimeLabel != null && goldRow != null && silverRow != null &&
                             bronzeRow != null && resultOverlay != null;

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
        }
    }

    void OnDisable()
    {
        if (timerController != null)
        {
            timerController.OnTimeUpdated -= HandleTimeUpdated;
            timerController.OnTimerStopped -= HandleTimerStopped;
            timerController.OnTimerReset -= HandleTimerReset;
        }
    }

    void Start()
    {
        if (timerController == null)
            return;

        SetTargetLabel(goldTargetLabel, timerController.goldTime);
        SetTargetLabel(silverTargetLabel, timerController.silverTime);
        SetTargetLabel(bronzeTargetLabel, timerController.bronzeTime);

        if (resultOverlay != null)
            resultOverlay.RemoveFromClassList("result-overlay--visible");

        HandleTimeUpdated(timerController.GetElapsedTime());
    }

    void SetTargetLabel(Label label, float time)
    {
        if (label != null)
            label.text = $"< {TimerController.FormatTime(time)}";
    }

    // ---------------- Live-Updates ----------------

    void HandleTimeUpdated(float elapsedTime)
    {
        if (currentTimeLabel != null)
            currentTimeLabel.text = TimerController.FormatTime(elapsedTime);

        if (timerController == null || timerController.HasFinished())
            return;

        UpdateRowState(goldRow, goldCheck, elapsedTime <= timerController.goldTime);
        UpdateRowState(silverRow, silverCheck, elapsedTime <= timerController.silverTime);
        UpdateRowState(bronzeRow, bronzeCheck, elapsedTime <= timerController.bronzeTime);
    }

    void UpdateRowState(VisualElement row, VisualElement check, bool reached)
    {
        if (row != null)
            row.EnableInClassList("medal-row--active", reached);

        if (check != null)
            check.EnableInClassList("medal-check--done", reached);
    }

    // ---------------- Finaler Abschluss ----------------

    void HandleTimerStopped(float finalTime, TimerController.MedalRank rank)
    {
        if (showDebugInfo)
            Debug.Log($"🏁 TimerHUDController: HUD finalisiert für {TimerController.FormatTime(finalTime)} ({rank})");

        if (currentTimeLabel != null)
            currentTimeLabel.text = TimerController.FormatTime(finalTime);

        bool gotBronze = rank == TimerController.MedalRank.Bronze ||
                          rank == TimerController.MedalRank.Silver ||
                          rank == TimerController.MedalRank.Gold;
        bool gotSilver = rank == TimerController.MedalRank.Silver ||
                          rank == TimerController.MedalRank.Gold;
        bool gotGold = rank == TimerController.MedalRank.Gold;

        FinalizeRow(bronzeRow, bronzeCheck, gotBronze);
        FinalizeRow(silverRow, silverCheck, gotSilver);
        FinalizeRow(goldRow, goldCheck, gotGold);

        if (showResultOverlay)
            ShowResult(finalTime, rank);
    }

    void FinalizeRow(VisualElement row, VisualElement check, bool reached)
    {
        if (row == null)
            return;

        row.EnableInClassList("medal-row--active", reached);
        row.EnableInClassList("medal-row--missed", !reached);

        if (check != null)
            check.EnableInClassList("medal-check--done", reached);
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
            TimerController.MedalRank.Gold => "Gold",
            TimerController.MedalRank.Silver => "Silber",
            TimerController.MedalRank.Bronze => "Bronze",
            _ => "Keine Medaille"
        };

        if (resultMedalNameLabel != null)
        {
            resultMedalNameLabel.text = rankLabel;
            resultMedalNameLabel.RemoveFromClassList("medal-name--gold");
            resultMedalNameLabel.RemoveFromClassList("medal-name--silver");
            resultMedalNameLabel.RemoveFromClassList("medal-name--bronze");

            switch (rank)
            {
                case TimerController.MedalRank.Gold:
                    resultMedalNameLabel.AddToClassList("medal-name--gold");
                    break;
                case TimerController.MedalRank.Silver:
                    resultMedalNameLabel.AddToClassList("medal-name--silver");
                    break;
                case TimerController.MedalRank.Bronze:
                    resultMedalNameLabel.AddToClassList("medal-name--bronze");
                    break;
            }
        }

        if (resultMedalIcon != null)
        {
            resultMedalIcon.RemoveFromClassList("medal-icon--inactive");
            resultMedalIcon.RemoveFromClassList("medal-icon--gold");
            resultMedalIcon.RemoveFromClassList("medal-icon--silver");
            resultMedalIcon.RemoveFromClassList("medal-icon--bronze");

            switch (rank)
            {
                case TimerController.MedalRank.Gold:
                    resultMedalIcon.AddToClassList("medal-icon--gold");
                    break;
                case TimerController.MedalRank.Silver:
                    resultMedalIcon.AddToClassList("medal-icon--silver");
                    break;
                case TimerController.MedalRank.Bronze:
                    resultMedalIcon.AddToClassList("medal-icon--bronze");
                    break;
                default:
                    resultMedalIcon.AddToClassList("medal-icon--inactive");
                    break;
            }
        }
    }

    // ---------------- Reset ----------------

    void HandleTimerReset()
    {
        if (currentTimeLabel != null)
            currentTimeLabel.text = TimerController.FormatTime(0f);

        ResetRow(goldRow, goldCheck);
        ResetRow(silverRow, silverCheck);
        ResetRow(bronzeRow, bronzeCheck);

        if (resultOverlay != null)
            resultOverlay.RemoveFromClassList("result-overlay--visible");
    }

    void ResetRow(VisualElement row, VisualElement check)
    {
        if (row != null)
        {
            row.RemoveFromClassList("medal-row--active");
            row.RemoveFromClassList("medal-row--missed");
        }

        if (check != null)
            check.RemoveFromClassList("medal-check--done");
    }
}
