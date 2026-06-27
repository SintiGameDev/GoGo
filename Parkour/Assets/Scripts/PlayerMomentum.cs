using UnityEngine;

/// <summary>
/// Verwaltet den Flow/Momentum-Speed-Multiplikator. Es gibt KEINE festen
/// Walk/Run-Stufen mehr - eine Basis-Laufgeschwindigkeit (baseMoveSpeed) wird
/// mit diesem Multiplikator skaliert. Aktionen wie Sliding, Walljump-Chains
/// oder Vault rufen AddMomentum() auf, um den Multiplikator zu erhöhen; ohne
/// neue Aktionen klingt er über Zeit von selbst zurück auf 1.0 ab.
///
/// Das ist die saubere Variante des alten VelocityMultiplier-Konzepts, jetzt
/// als zentraler Bestandteil des Movement-Systems statt Zusatz-Skript.
/// </summary>
public class PlayerMomentum : MonoBehaviour
{
    [Header("Basis-Geschwindigkeit")]
    [Tooltip("Grundlegende Lauf-Geschwindigkeit in m/s, BEVOR der Momentum-Multiplikator angewendet wird")]
    public float baseMoveSpeed = 7.5f;

    [Header("Momentum-Grenzen")]
    public float minMultiplier = 1.0f;
    public float maxMultiplier = 2.2f;

    [Header("Decay")]
    [Tooltip("Wie viel der Multiplikator pro Sekunde abklingt, sobald er über minMultiplier liegt")]
    public float decayRatePerSecond = 0.25f;

    [Tooltip("Kurze Verzögerung nach der letzten Momentum-Erhöhung, bevor der Decay einsetzt (gibt der Aktion Zeit zu wirken, bevor sie wieder abklingt)")]
    public float decayDelay = 0.3f;

    [Header("Debug")]
    public bool showDebugInfo = false;

    private float currentMultiplier;
    private float lastGainTime = -999f;

    void Awake()
    {
        currentMultiplier = minMultiplier;
    }

    void Update()
    {
        if (Time.time - lastGainTime < decayDelay)
            return;

        if (currentMultiplier > minMultiplier)
        {
            currentMultiplier -= decayRatePerSecond * Time.deltaTime;
            currentMultiplier = Mathf.Max(currentMultiplier, minMultiplier);
        }
    }

    /// <summary>
    /// Erhöht den Momentum-Multiplikator additiv (z.B. von Sliding oder einem
    /// Walljump aufgerufen). Wird auf maxMultiplier gecapt.
    /// </summary>
    public void AddMomentum(float amount)
    {
        if (amount <= 0f)
            return;

        currentMultiplier = Mathf.Min(currentMultiplier + amount, maxMultiplier);
        lastGainTime = Time.time;

        if (showDebugInfo)
            Debug.Log($"[PlayerMomentum] +{amount:F2} → Multiplikator: {currentMultiplier:F2}x");
    }

    /// <summary>Setzt den Multiplikator auf einen festen Wert (z.B. für Spezialfälle), statt additiv zu erhöhen.</summary>
    public void SetMultiplier(float value)
    {
        currentMultiplier = Mathf.Clamp(value, minMultiplier, maxMultiplier);
        lastGainTime = Time.time;
    }

    /// <summary>Setzt den Multiplikator sofort zurück auf den Minimalwert (z.B. nach hartem Stop/Tod).</summary>
    public void ResetMomentum()
    {
        currentMultiplier = minMultiplier;
    }

    public float GetMultiplier() => currentMultiplier;

    /// <summary>Aktuelle effektive Laufgeschwindigkeit (baseMoveSpeed × Multiplikator).</summary>
    public float GetCurrentMoveSpeed() => baseMoveSpeed * currentMultiplier;
}
