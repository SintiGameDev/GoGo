using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Zeigt den TimerController auf einem Canvas an: aktuelle Zeit, Bronze/Silber/Gold-Zielzeiten
/// und ob diese bereits erreicht wurden. Färbt die Live-Zeit ein, je nachdem welche Medaille
/// gerade "drin" wäre, und markiert am Ende die final erreichte Medaille.
/// </summary>
public class TimerUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Referenz zum TimerController (auto-detected wenn leer)")]
    public TimerController timerController;

    [Header("Haupt-Zeit Anzeige")]
    public TextMeshProUGUI currentTimeText;

    [Header("Medal Zeilen")]
    public TextMeshProUGUI bronzeTimeText;
    public TextMeshProUGUI silverTimeText;
    public TextMeshProUGUI goldTimeText;

    [Tooltip("Optionale Icons/Häkchen die ein-/ausgeblendet werden, sobald die jeweilige Medal erreicht ist")]
    public GameObject bronzeCheckmark;
    public GameObject silverCheckmark;
    public GameObject goldCheckmark;

    [Header("Farben")]
    public Color defaultColor = Color.white;
    public Color bronzeColor = new Color(0.80f, 0.50f, 0.20f);
    public Color silverColor = new Color(0.75f, 0.75f, 0.75f);
    public Color goldColor = new Color(1.0f, 0.84f, 0.0f);
    public Color missedColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);

    [Header("Ergebnis-Panel (optional)")]
    [Tooltip("Wird beim Erreichen des Goals eingeblendet (z.B. ein Result-Screen)")]
    public GameObject resultPanel;
    public TextMeshProUGUI resultTimeText;
    public TextMeshProUGUI resultMedalText;

    [Header("Format")]
    [Tooltip("Präfix vor der Zielzeit, z.B. 'Gold: '")]
    public string bronzeLabel = "Bronze";
    public string silverLabel = "Silber";
    public string goldLabel = "Gold";

    [Header("Debug")]
    public bool showDebugInfo = true;

    void Awake()
    {
        if (timerController == null)
            timerController = GetComponent<TimerController>();

        if (timerController == null)
            timerController = FindObjectOfType<TimerController>();

        if (timerController == null && showDebugInfo)
        {
            Debug.LogError("❌ TimerUI: Kein TimerController gefunden!");
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
        SetupMedalLabels();

        if (resultPanel != null)
            resultPanel.SetActive(false);

        // Initialzustand anzeigen (z.B. falls Timer noch nicht läuft)
        if (timerController != null)
        {
            HandleTimeUpdated(timerController.GetElapsedTime());
        }
    }

    void SetupMedalLabels()
    {
        if (timerController == null)
            return;

        if (bronzeTimeText != null)
        {
            bronzeTimeText.text = $"{bronzeLabel}: {TimerController.FormatTime(timerController.bronzeTime)}";
            bronzeTimeText.color = bronzeColor;
        }

        if (silverTimeText != null)
        {
            silverTimeText.text = $"{silverLabel}: {TimerController.FormatTime(timerController.silverTime)}";
            silverTimeText.color = silverColor;
        }

        if (goldTimeText != null)
        {
            goldTimeText.text = $"{goldLabel}: {TimerController.FormatTime(timerController.goldTime)}";
            goldTimeText.color = goldColor;
        }

        SetCheckmark(bronzeCheckmark, false);
        SetCheckmark(silverCheckmark, false);
        SetCheckmark(goldCheckmark, false);
    }

    void HandleTimeUpdated(float elapsedTime)
    {
        if (currentTimeText != null)
        {
            currentTimeText.text = TimerController.FormatTime(elapsedTime);
            currentTimeText.color = GetColorForCurrentPace(elapsedTime);
        }

        UpdateLiveMedalProgress(elapsedTime);
    }

    /// <summary>
    /// Während der Timer läuft: zeigt live an, welche Medaillen-Schwelle bereits
    /// unterschritten wurde (z.B. Häkchen erscheint sobald die Zeit noch unter Gold liegt)
    /// </summary>
    void UpdateLiveMedalProgress(float elapsedTime)
    {
        if (timerController == null || timerController.HasFinished())
            return;

        SetCheckmark(bronzeCheckmark, elapsedTime <= timerController.bronzeTime);
        SetCheckmark(silverCheckmark, elapsedTime <= timerController.silverTime);
        SetCheckmark(goldCheckmark, elapsedTime <= timerController.goldTime);
    }

    Color GetColorForCurrentPace(float elapsedTime)
    {
        if (timerController == null)
            return defaultColor;

        if (elapsedTime <= timerController.goldTime)
            return goldColor;
        if (elapsedTime <= timerController.silverTime)
            return silverColor;
        if (elapsedTime <= timerController.bronzeTime)
            return bronzeColor;

        return defaultColor;
    }

    void HandleTimerStopped(float finalTime, TimerController.MedalRank rank)
    {
        if (showDebugInfo)
            Debug.Log($"🏁 TimerUI: Anzeige aktualisiert für Endzeit {TimerController.FormatTime(finalTime)} ({rank})");

        // Finale Markierung der erreichten Medaillen (alles bis zur erreichten Stufe gilt als erreicht)
        SetCheckmark(bronzeCheckmark, rank == TimerController.MedalRank.Bronze ||
                                       rank == TimerController.MedalRank.Silver ||
                                       rank == TimerController.MedalRank.Gold);
        SetCheckmark(silverCheckmark, rank == TimerController.MedalRank.Silver ||
                                       rank == TimerController.MedalRank.Gold);
        SetCheckmark(goldCheckmark, rank == TimerController.MedalRank.Gold);

        // Nicht erreichte Zeilen optisch abdunkeln
        if (rank != TimerController.MedalRank.Bronze &&
            rank != TimerController.MedalRank.Silver &&
            rank != TimerController.MedalRank.Gold && bronzeTimeText != null)
        {
            bronzeTimeText.color = missedColor;
        }
        if (rank != TimerController.MedalRank.Silver &&
            rank != TimerController.MedalRank.Gold && silverTimeText != null)
        {
            silverTimeText.color = missedColor;
        }
        if (rank != TimerController.MedalRank.Gold && goldTimeText != null)
        {
            goldTimeText.color = missedColor;
        }

        if (currentTimeText != null)
        {
            currentTimeText.text = TimerController.FormatTime(finalTime);
            currentTimeText.color = GetColorForRank(rank);
        }

        ShowResultPanel(finalTime, rank);
    }

    void ShowResultPanel(float finalTime, TimerController.MedalRank rank)
    {
        if (resultPanel == null)
            return;

        resultPanel.SetActive(true);

        if (resultTimeText != null)
            resultTimeText.text = TimerController.FormatTime(finalTime);

        if (resultMedalText != null)
        {
            resultMedalText.text = GetRankLabel(rank);
            resultMedalText.color = GetColorForRank(rank);
        }
    }

    void HandleTimerReset()
    {
        SetupMedalLabels();

        if (currentTimeText != null)
        {
            currentTimeText.text = TimerController.FormatTime(0f);
            currentTimeText.color = defaultColor;
        }

        if (resultPanel != null)
            resultPanel.SetActive(false);
    }

    string GetRankLabel(TimerController.MedalRank rank)
    {
        switch (rank)
        {
            case TimerController.MedalRank.Gold:
                return goldLabel;
            case TimerController.MedalRank.Silver:
                return silverLabel;
            case TimerController.MedalRank.Bronze:
                return bronzeLabel;
            default:
                return "Keine Medaille";
        }
    }

    Color GetColorForRank(TimerController.MedalRank rank)
    {
        switch (rank)
        {
            case TimerController.MedalRank.Gold:
                return goldColor;
            case TimerController.MedalRank.Silver:
                return silverColor;
            case TimerController.MedalRank.Bronze:
                return bronzeColor;
            default:
                return missedColor;
        }
    }

    void SetCheckmark(GameObject checkmark, bool active)
    {
        if (checkmark != null)
            checkmark.SetActive(active);
    }
}
