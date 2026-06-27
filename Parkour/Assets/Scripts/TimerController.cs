using System;
using UnityEngine;

/// <summary>
/// Stoppuhr-Logik für das Level. Läuft seit Levelstart, pausiert wenn das Goal erreicht wird.
/// Bewertet die Endzeit gegen Bronze/Silber/Gold-Zielzeiten.
/// Reine Logik-Komponente, keine UI - siehe TimerUI für die Anzeige.
/// </summary>
public class TimerController : MonoBehaviour
{
    public enum MedalRank
    {
        None,
        Bronze,
        Silver,
        Gold
    }

    [Header("Medal Zeiten (in Sekunden)")]
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

    [Header("Debug")]
    public bool showDebugInfo = true;

    // Events für UI / andere Listener
    public event Action<float> OnTimeUpdated;
    public event Action<float, MedalRank> OnTimerStopped;
    public event Action OnTimerReset;

    private float elapsedTime = 0f;
    private bool isRunning = false;
    private bool hasFinished = false;
    private MedalRank finalRank = MedalRank.None;

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
    /// Stoppt den Timer final und berechnet die erreichte Medaille.
    /// </summary>
    public void StopAndEvaluate()
    {
        if (hasFinished)
            return;

        isRunning = false;
        hasFinished = true;
        finalRank = EvaluateMedal(elapsedTime);

        if (showDebugInfo)
        {
            Debug.Log($"🏁 TimerController: Ziel erreicht! Zeit: {FormatTime(elapsedTime)} | Medaille: {finalRank}");
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

        OnTimerReset?.Invoke();
        OnTimeUpdated?.Invoke(elapsedTime);

        if (showDebugInfo)
            Debug.Log("🔄 TimerController: Timer zurückgesetzt");
    }

    /// <summary>
    /// Ermittelt anhand der Zeitschwellen, welche Medaille erreicht wurde
    /// </summary>
    public MedalRank EvaluateMedal(float time)
    {
        if (time <= goldTime)
            return MedalRank.Gold;
        if (time <= silverTime)
            return MedalRank.Silver;
        if (time <= bronzeTime)
            return MedalRank.Bronze;

        return MedalRank.None;
    }

    /// <summary>
    /// Prüft ob eine bestimmte Medaille mit der aktuellen (oder finalen) Zeit erreicht wurde/wird
    /// </summary>
    public bool HasReached(MedalRank rank)
    {
        float timeToCheck = hasFinished ? elapsedTime : elapsedTime;

        switch (rank)
        {
            case MedalRank.Gold:
                return timeToCheck <= goldTime;
            case MedalRank.Silver:
                return timeToCheck <= silverTime;
            case MedalRank.Bronze:
                return timeToCheck <= bronzeTime;
            default:
                return true;
        }
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
}
