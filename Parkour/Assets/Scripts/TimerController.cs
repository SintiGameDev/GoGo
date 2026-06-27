using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Stoppuhr-Logik für das Level. Läuft seit Levelstart, pausiert wenn das Goal erreicht wird.
/// Bewertet die Endzeit gegen Bronze/Silber/Gold/Diamant-Zielzeiten und verwaltet die
/// Bestzeit pro Szene (persistiert über PlayerPrefs).
/// Reine Logik-Komponente, keine UI - siehe TimerHUDController für die Anzeige.
/// </summary>
public class TimerController : MonoBehaviour
{
    public enum MedalRank
    {
        None,
        Bronze,
        Silver,
        Gold,
        Diamond
    }

    [Header("Medal Zeiten (in Sekunden)")]
    [Tooltip("Zeit, die für Diamant unterschritten werden muss (höchste Stufe)")]
    public float diamondTime = 20f;

    [Tooltip("Zeit, die für Gold unterschritten werden muss")]
    public float goldTime = 30f;

    [Tooltip("Zeit, die für Silber unterschritten werden muss")]
    public float silverTime = 45f;

    [Tooltip("Zeit, die für Bronze unterschritten werden muss")]
    public float bronzeTime = 60f;

    [Header("Verhalten")]
    [Tooltip("Timer startet automatisch in Start()")]
    public bool autoStartOnStart = true;

    [Tooltip("Timer zählt hoch (Stoppuhr) statt runter")]
    public bool countUp = true;

    [Header("Bestzeit (persistent pro Szene)")]
    [Tooltip("Eigener Key-Präfix falls mehrere Timer in derselben Szene unabhängige Bestzeiten brauchen sollen")]
    public string bestTimeKeyOverride = "";

    [Header("Debug")]
    public bool showDebugInfo = true;

    // Events für UI / andere Listener
    public event Action<float> OnTimeUpdated;
    public event Action<float, MedalRank> OnTimerStopped;
    public event Action OnTimerReset;

    /// <summary>
    /// Wird gefeuert, sobald sich das aktuell anzuvisierende nächste Ziel ändert
    /// (z.B. weil Bronze gerade verpasst wurde und jetzt Silber das neue Ziel ist).
    /// Übergibt: das Ziel-Rank, die Ziel-Zeit, und ob dieses Ziel bereits erreicht ist.
    /// </summary>
    public event Action<MedalRank, float, bool> OnNextTargetChanged;

    private float elapsedTime = 0f;
    private bool isRunning = false;
    private bool hasFinished = false;
    private MedalRank finalRank = MedalRank.None;
    private MedalRank currentNextTarget = MedalRank.None;

    private bool isNewBestTime = false;
    private float bestTimeBeforeThisRun = -1f;

    void Start()
    {
        if (autoStartOnStart)
        {
            StartTimer();
        }
    }

    void Update()
    {
        if (!isRunning)
            return;

        elapsedTime += Time.deltaTime;
        OnTimeUpdated?.Invoke(elapsedTime);

        CheckNextTargetChanged();
    }

    /// <summary>
    /// Prüft, ob sich das anzuvisierende nächste Ziel geändert hat, und feuert das Event
    /// nur in diesem Fall (verhindert unnötiges UI-Re-Layout/Flackern bei jedem Frame).
    /// </summary>
    void CheckNextTargetChanged()
    {
        MedalRank newTarget = GetNextTargetRank();

        if (newTarget != currentNextTarget)
        {
            currentNextTarget = newTarget;
            float targetTime = GetTimeForRank(newTarget);
            bool alreadyReached = elapsedTime <= targetTime;
            OnNextTargetChanged?.Invoke(newTarget, targetTime, alreadyReached);
        }
    }

    /// <summary>
    /// Ermittelt die aktuell relevante "nächste" Zielstufe relativ zur Laufzeit:
    /// die beste Medal-Stufe, die man JETZT noch bekommen würde, wenn man sofort
    /// ins Ziel laufen würde. Identisch zu EvaluateMedal(elapsedTime) - bei t=0
    /// ist das Diamant (da 0s jede Schwelle unterschreitet), und mit steigender
    /// Laufzeit wandert die Anzeige automatisch zu Gold, Silber, Bronze runter.
    /// </summary>
    public MedalRank GetNextTargetRank()
    {
        MedalRank current = EvaluateMedal(elapsedTime);

        // None (Bronze-Zeit bereits überschritten) wird in der Live-Anzeige als
        // Bronze dargestellt, damit immer eine Referenzstufe sichtbar bleibt.
        return current == MedalRank.None ? MedalRank.Bronze : current;
    }

    public float GetTimeForRank(MedalRank rank)
    {
        switch (rank)
        {
            case MedalRank.Diamond: return diamondTime;
            case MedalRank.Gold: return goldTime;
            case MedalRank.Silver: return silverTime;
            case MedalRank.Bronze: return bronzeTime;
            default: return bronzeTime;
        }
    }

    /// <summary>
    /// Startet bzw. setzt den Timer fort
    /// </summary>
    public void StartTimer()
    {
        if (hasFinished)
        {
            if (showDebugInfo)
                Debug.LogWarning("⚠️ TimerController: Timer bereits abgeschlossen. Erst ResetTimer() aufrufen.");
            return;
        }

        isRunning = true;
        CheckNextTargetChanged();

        if (showDebugInfo)
            Debug.Log("⏱️ TimerController: Timer gestartet");
    }

    /// <summary>
    /// Pausiert den Timer ohne ihn final abzuschließen (z.B. Pause-Menü)
    /// </summary>
    public void PauseTimer()
    {
        if (!isRunning)
            return;

        isRunning = false;

        if (showDebugInfo)
            Debug.Log($"⏸️ TimerController: Timer pausiert bei {FormatTime(elapsedTime)}");
    }

    /// <summary>
    /// Wird vom GoalTrigger aufgerufen, wenn der Spieler das Ziel erreicht.
    /// Stoppt den Timer final, berechnet die erreichte Medaille und aktualisiert
    /// ggf. die persistente Bestzeit für diese Szene.
    /// </summary>
    public void StopAndEvaluate()
    {
        if (hasFinished)
            return;

        isRunning = false;
        hasFinished = true;
        finalRank = EvaluateMedal(elapsedTime);

        bestTimeBeforeThisRun = GetBestTime();
        isNewBestTime = !HasBestTime() || elapsedTime < bestTimeBeforeThisRun;

        if (isNewBestTime)
        {
            SaveBestTime(elapsedTime);
        }

        if (showDebugInfo)
        {
            string bestInfo = isNewBestTime ? " 🏆 NEUE BESTZEIT!" : $" (Bestzeit: {FormatTime(GetBestTime())})";
            Debug.Log($"🏁 TimerController: Ziel erreicht! Zeit: {FormatTime(elapsedTime)} | Medaille: {finalRank}{bestInfo}");
        }

        OnTimerStopped?.Invoke(elapsedTime, finalRank);
    }

    /// <summary>
    /// Setzt den Timer komplett zurück (z.B. bei Level-Neustart)
    /// </summary>
    public void ResetTimer()
    {
        elapsedTime = 0f;
        isRunning = false;
        hasFinished = false;
        finalRank = MedalRank.None;
        currentNextTarget = MedalRank.None;
        isNewBestTime = false;

        OnTimerReset?.Invoke();
        OnTimeUpdated?.Invoke(elapsedTime);
        CheckNextTargetChanged();

        if (showDebugInfo)
            Debug.Log("🔄 TimerController: Timer zurückgesetzt");
    }

    /// <summary>
    /// Ermittelt anhand der Zeitschwellen, welche Medaille die ANGEGEBENE Zeit
    /// erreicht (z.B. für den finalen Stop bei Zielankunft).
    /// </summary>
    public MedalRank EvaluateMedal(float time)
    {
        if (time <= diamondTime)
            return MedalRank.Diamond;
        if (time <= goldTime)
            return MedalRank.Gold;
        if (time <= silverTime)
            return MedalRank.Silver;
        if (time <= bronzeTime)
            return MedalRank.Bronze;

        return MedalRank.None;
    }

    /// <summary>
    /// Prüft ob eine bestimmte Medaille mit der aktuellen Laufzeit erreicht wurde
    /// </summary>
    public bool HasReached(MedalRank rank)
    {
        switch (rank)
        {
            case MedalRank.Diamond:
                return elapsedTime <= diamondTime;
            case MedalRank.Gold:
                return elapsedTime <= goldTime;
            case MedalRank.Silver:
                return elapsedTime <= silverTime;
            case MedalRank.Bronze:
                return elapsedTime <= bronzeTime;
            default:
                return true;
        }
    }

    // ---------------- Bestzeit (PlayerPrefs, pro Szene) ----------------

    string GetBestTimeKey()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        string prefix = string.IsNullOrEmpty(bestTimeKeyOverride) ? sceneName : bestTimeKeyOverride;
        return $"BestTime_{prefix}";
    }

    /// <summary>
    /// Liefert die gespeicherte Bestzeit für die aktuelle Szene, oder -1 falls noch keine existiert.
    /// </summary>
    public float GetBestTime()
    {
        return PlayerPrefs.GetFloat(GetBestTimeKey(), -1f);
    }

    public bool HasBestTime()
    {
        return PlayerPrefs.HasKey(GetBestTimeKey());
    }

    void SaveBestTime(float time)
    {
        PlayerPrefs.SetFloat(GetBestTimeKey(), time);
        PlayerPrefs.Save();

        if (showDebugInfo)
            Debug.Log($"💾 TimerController: Neue Bestzeit gespeichert für Szene '{SceneManager.GetActiveScene().name}': {FormatTime(time)}");
    }

    /// <summary>
    /// Setzt die Bestzeit für die aktuelle Szene zurück (z.B. für Debug-Zwecke)
    /// </summary>
    public void ClearBestTime()
    {
        PlayerPrefs.DeleteKey(GetBestTimeKey());
        PlayerPrefs.Save();
    }

    public static string FormatTime(float time)
    {
        if (time < 0f)
            time = 0f;

        int minutes = Mathf.FloorToInt(time / 60f);
        float seconds = time % 60f;

        return $"{minutes:00}:{seconds:00.00}";
    }

    // Public Getters
    public float GetElapsedTime() => elapsedTime;
    public bool IsRunning() => isRunning;
    public bool HasFinished() => hasFinished;
    public MedalRank GetFinalRank() => finalRank;
    public bool IsNewBestTime() => isNewBestTime;
}
