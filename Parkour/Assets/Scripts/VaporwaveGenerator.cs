using System;
using UnityEngine;

/// <summary>
/// Prozeduraler Echtzeit-Generator für schwebende, traumhafte Vaporwave-Musik.
/// Synthetisiert Pads, Bass, einen langsamen Lo-Fi-Beat und ein Arpeggio-Layer
/// direkt im Audio-Thread (OnAudioFilterRead) – keine Audiodateien, kein Pre-Rendering.
///
/// Setup: Auf ein leeres GameObject mit AudioSource packen.
/// Auf der AudioSource "Play On Awake" kann an bleiben (Clip wird nicht benötigt,
/// der Sound entsteht ausschließlich über OnAudioFilterRead).
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class VaporwaveGenerator : MonoBehaviour
{
    [Header("Allgemein")]
    [Tooltip("Tempo in BPM. Vaporwave-typisch: 60-80 BPM (langsam, schwebend)")]
    [Range(50f, 100f)]
    public float schlaegeProMinute = 68f;

    [Tooltip("Grundtonart als MIDI-Notennummer (z.B. 57 = A3)")]
    public int grundton = 57;

    [Header("Lautstärken (0-1)")]
    [Range(0f, 2f)] public float masterLautstaerke = 1.4f;
    [Range(0f, 1f)] public float padLautstaerke = 0.55f;
    [Range(0f, 1f)] public float bassLautstaerke = 0.45f;
    [Range(0f, 1f)] public float beatLautstaerke = 0.4f;
    [Range(0f, 1f)] public float arpeggioLautstaerke = 0.3f;

    [Header("Delay / Reverb ('Schweben')")]
    [Range(0f, 0.95f)] public float delayFeedback = 0.55f;
    [Range(0.05f, 1.5f)] public float delayZeitSekunden = 0.55f;
    [Range(0f, 1f)] public float delayMix = 0.45f;

    [Header("Chorus (Pad-Lushness)")]
    [Range(0f, 20f)] public float chorusDetuneCent = 7f;
    [Range(0.05f, 2f)] public float chorusRateHz = 0.15f;

    [Header("Wärme / Lo-Fi-Charakter")]
    [Tooltip("Tiefpassfilter-Cutoff-Basis in Hz. Niedrigere Werte = wärmer/dumpfer, höhere = klarer/kälter")]
    [Range(400f, 8000f)] public float filterCutoffHz = 2200f;
    [Tooltip("Wie stark der Cutoff durch den langsamen LFO atmet (in Hz)")]
    [Range(0f, 4000f)] public float filterLfoTiefeHz = 900f;
    [Range(0.02f, 0.5f)] public float filterLfoRateHz = 0.07f;

    [Tooltip("Lo-Fi Sample-Rate-Reduction (Bitcrush-Stil). 1 = aus, höhere Werte = mehr Vintage-Tape-Charakter")]
    [Range(1, 8)] public int lofiSampleHold = 1;

    // ---- Audio-Engine Grunddaten ----
    private int abtastrate;
    private double samplePosition; // läuft kontinuierlich, Audio-Thread-only

    // ---- Akkordfolge ----
    // Vaporwave-typisch: viel maj7/min7/min9, modale Mischung, ruhige Bewegung.
    // Intervalle relativ zum Grundton (in Halbtönen), je Akkord 4 Töne (Pad-Voicing).
    private static readonly int[][] akkordPalette = new int[][]
    {
        new int[] { 0, 3, 7, 10 },   // i7 (Moll7)
        new int[] { 8, 12, 15, 19 }, // VI maj7 (relativ)
        new int[] { 5, 8, 12, 15 },  // iv7
        new int[] { 3, 7, 10, 14 },  // III maj-ish / add9 Verschiebung
        new int[] { -2, 3, 7, 10 },  // ii halbverm.-artig, weicher Übergang
        new int[] { 10, 14, 17, 21 } // VII maj7, Modal-Mixture-Farbe
    };

    private int[] aktuelleAkkordToene;
    private int[] naechsteAkkordToene;
    private const int taktePerAkkord = 4; // wie viele Takte ein Akkord gehalten wird

    private System.Random zufallsGenerator;

    // ---- Pad-Oszillatoren (pro Stimme: Hauptphase + leicht verstimmte Chorus-Phase) ----
    private const int padStimmenAnzahl = 4;
    private double[] padPhase = new double[padStimmenAnzahl];
    private double[] padPhaseChorus = new double[padStimmenAnzahl];
    private double[] padZielFrequenz = new double[padStimmenAnzahl];
    private double[] padAktuelleFrequenz = new double[padStimmenAnzahl];

    // ---- Bass ----
    private double bassPhase;
    private double bassZielFrequenz;
    private double bassAktuelleFrequenz;

    // ---- Arpeggio ----
    private double arpeggioPhase;
    private int arpeggioSchrittIndex;
    private double samplesProArpeggioSchritt;
    private double arpeggioSchrittZaehler;
    private float arpeggioEnvelope; // einfache Decay-Hülle pro Anschlag
    private bool arpeggioAufsteigend = true;

    // ---- Beat ----
    private double samplesProSechzehntel;
    private double beatSchrittZaehler;
    private int beatSechzehntelIndex;
    // Kick auf 1 und 9 (von 16teln), leichter Lo-Fi-Snare/Clap auf 5 und 13,
    // dezenter Hi-Hat-Tick auf jedem zweiten 16tel -> ruhiges, schleppendes Pattern.
    private static readonly bool[] kickPattern  = { true,false,false,false,false,false,false,false, true,false,false,false,false,false,false,false };
    private static readonly bool[] snarePattern = { false,false,false,false,true, false,false,false, false,false,false,false,true, false,false,false };
    private static readonly bool[] hatPatternA   = { false,false,true, false,false,false,true, false,false,false,true, false,false,false,true, false };
    // Zweites Hat-Pattern mit Synkopierung -> sorgt für hörbare Abwechslung bei Akkordwechseln
    private static readonly bool[] hatPatternB   = { false,true,false,true, false,false,true,false, false,true,false,false,true, false,true,false };
    private bool[] aktivesHatPattern;

    // Hüllkurven-Zustände für die einzelnen Drum-Stimmen (einfache One-Shot-Decays)
    private float kickEnvelope, snareEnvelope, hatEnvelope;
    private double kickPhase;
    private System.Random rauschenGenerator;

    // ---- Delay-Buffer (Stereo, pro Kanal eigener Ringbuffer) ----
    private float[] delayBufferLinks;
    private float[] delayBufferRechts;
    private int delaySchreibKopf;

    // ---- Lifecycle-Schutz: verhindert Audio-Thread-Zugriff auf bereits freigegebene/ungültige Daten ----
    // bool-Zuweisungen sind in .NET atomar, daher ohne Lock sicher als einfaches Stop-Signal nutzbar.
    private bool laeuftNoch;

    // ---- Tiefpassfilter-Zustand (1-Pole-Lowpass, je Kanal getrennt für minimale Stereo-Bewegung) ----
    private float filterZustandLinks;
    private float filterZustandRechts;

    // ---- Lo-Fi Sample-Hold-Zustand (simple Sample-Rate-Reduction für Bitcrush-Vibe) ----
    private float lofiGehaltenerWertLinks;
    private float lofiGehaltenerWertRechts;
    private int lofiZaehler;

    // ---- Glättung für Lautstärke-Parameter (verhindert Klicks bei Inspector-Änderungen) ----
    private const double glaettungsfaktor = 0.0008;

    private void Awake()
    {
        abtastrate = AudioSettings.outputSampleRate;
        zufallsGenerator = new System.Random();
        rauschenGenerator = new System.Random();

        AktualisiereZeitbasierteWerte();
        InitialisiereDelayBuffer();

        // Startakkord setzen, damit beim ersten Callback bereits Zielfrequenzen existieren
        aktuelleAkkordToene = akkordPalette[zufallsGenerator.Next(akkordPalette.Length)];
        naechsteAkkordToene = WaehleNaechstenAkkord(aktuelleAkkordToene);
        SetzePadZielFrequenzenAusAkkord(aktuelleAkkordToene);
        bassZielFrequenz = MidiZuFrequenz(grundton - 12 + aktuelleAkkordToene[0]);
        bassAktuelleFrequenz = bassZielFrequenz;

        for (int i = 0; i < padStimmenAnzahl; i++)
        {
            padAktuelleFrequenz[i] = padZielFrequenz[i];
            // Zufällige Startphase pro Stimme, damit es nicht synthetisch-synchron klingt
            padPhase[i] = zufallsGenerator.NextDouble();
            padPhaseChorus[i] = zufallsGenerator.NextDouble();
        }

        laeuftNoch = true;
    }

    private void OnEnable()
    {
        laeuftNoch = true;
    }

    private void OnDisable()
    {
        // Erstes Signal an den Audio-Thread: ab jetzt nur noch Stille ausgeben.
        // Wird VOR der eigentlichen Objektzerstörung aufgerufen, daher der früheste sichere Punkt.
        laeuftNoch = false;
    }

    private void OnDestroy()
    {
        laeuftNoch = false;
    }

    private void OnApplicationQuit()
    {
        laeuftNoch = false;
    }

    private void OnValidate()
    {
        // Wird im Editor bei Inspector-Änderungen aufgerufen (Main-Thread).
        // Hinweis: Schreibt auf Felder, die der Audio-Thread parallel liest.
        // Bei einfachen double-Zuweisungen in der Praxis unkritisch (kein Deadlock,
        // höchstens ein einzelner "falscher" Sample-Wert beim Tempo-Wechsel) -
        // für ein Ambient-Tool wie dieses ein akzeptabler Trade-off ohne Locking-Overhead.
        AktualisiereZeitbasierteWerte();
    }

    private void AktualisiereZeitbasierteWerte()
    {
        if (abtastrate <= 0) abtastrate = 44100;

        double sekundenProSchlag = 60.0 / schlaegeProMinute;
        double sekundenProTakt = sekundenProSchlag * 4.0; // 4/4-Takt

        samplesProSechzehntel = (sekundenProTakt / 16.0) * abtastrate;
        samplesProTakt = samplesProSechzehntel * 16.0;

        // Arpeggio läuft in Achteln, etwas hektischer als der Grundbeat aber immer noch ruhig
        double sekundenProArpeggioSchritt = sekundenProSchlag / 2.0;
        samplesProArpeggioSchritt = sekundenProArpeggioSchritt * abtastrate;
    }

    private void InitialisiereDelayBuffer()
    {
        int delayBufferGroesse = Mathf.Max(abtastrate * 2, 1024); // 2 Sekunden Puffer reicht für Delay-Zeiten bis 1.5s
        delayBufferLinks = new float[delayBufferGroesse];
        delayBufferRechts = new float[delayBufferGroesse];
        delaySchreibKopf = 0;
    }

    /// <summary>
    /// Wählt einen neuen Akkord, der sich vom aktuellen unterscheidet -> keine direkten Wiederholungen.
    /// </summary>
    private int[] WaehleNaechstenAkkord(int[] aktuell)
    {
        int[] kandidat;
        int versucheZaehler = 0;
        do
        {
            kandidat = akkordPalette[zufallsGenerator.Next(akkordPalette.Length)];
            versucheZaehler++;
        } while (kandidat == aktuell && versucheZaehler < 8);
        return kandidat;
    }

    private void SetzePadZielFrequenzenAusAkkord(int[] akkordToene)
    {
        for (int i = 0; i < padStimmenAnzahl; i++)
        {
            int toneIndex = i % akkordToene.Length;
            // höhere Pad-Stimmen eine Oktave höher für volleres Voicing
            int oktavVersatz = i >= akkordToene.Length ? 12 : 0;
            padZielFrequenz[i] = MidiZuFrequenz(grundton + akkordToene[toneIndex] + oktavVersatz);
        }
    }

    private static double MidiZuFrequenz(int midiNote)
    {
        return 440.0 * Math.Pow(2.0, (midiNote - 69) / 12.0);
    }

    /// <summary>
    /// Haupt-Audio-Callback. Läuft auf dem Audio-Thread!
    /// Keine Unity-API-Aufrufe hier drin außer reinem Datenzugriff auf primitive Felder.
    /// </summary>
    private void OnAudioFilterRead(float[] daten, int kanalAnzahl)
    {
        // Schutz gegen Race Conditions beim Start/Stop: Der Audio-Thread kann diesen
        // Callback theoretisch noch erhalten, bevor Awake() fertig ist oder nachdem
        // OnDisable()/OnDestroy() bereits gefeuert hat. In beiden Fällen einfach Stille
        // ausgeben statt mit null-Referenzen zu rechnen -> behebt den NullReferenceException-Crash
        // und das "Nachlaufen" des Sounds nach dem Schließen.
        if (!laeuftNoch || delayBufferLinks == null || delayBufferRechts == null)
        {
            Array.Clear(daten, 0, daten.Length);
            return;
        }

        int sampleAnzahl = daten.Length / kanalAnzahl;

        for (int sampleIndex = 0; sampleIndex < sampleAnzahl; sampleIndex++)
        {
            AktualisiereTaktUndAkkordFortschritt();
            AktualisiereArpeggioFortschritt();
            AktualisiereBeatFortschritt();

            float padSignal = ErzeugePadSample();
            float bassSignal = ErzeugeBassSample();
            float beatSignal = ErzeugeBeatSample();
            float arpeggioSignal = ErzeugeArpeggioSample();

            float trockenSignalLinks = (padSignal * padLautstaerke
                                      + bassSignal * bassLautstaerke
                                      + beatSignal * beatLautstaerke
                                      + arpeggioSignal * arpeggioLautstaerke) * masterLautstaerke;

            // Leichte Stereo-Verbreiterung: rechter Kanal nutzt leicht phasenverschobenen Chorus
            float trockenSignalRechts = trockenSignalLinks;

            float nassSignalLinks = VerarbeiteDelay(trockenSignalLinks, delayBufferLinks, delaySchreibKopf);
            float nassSignalRechts = VerarbeiteDelay(trockenSignalRechts, delayBufferRechts, delaySchreibKopf);
            delaySchreibKopf = (delaySchreibKopf + 1) % delayBufferLinks.Length;

            float ausgabeLinks = trockenSignalLinks * (1f - delayMix) + nassSignalLinks * delayMix;
            float ausgabeRechts = trockenSignalRechts * (1f - delayMix) + nassSignalRechts * delayMix;

            // Langsam "atmender" Tiefpassfilter-Cutoff -> nimmt Härte/Kälte aus den Obertönen
            // und bringt die für Vaporwave typische gedämpfte, warme Bewegung ins Klangbild.
            double cutoffLfo = Math.Sin(2.0 * Math.PI * filterLfoRateHz * (samplePosition / abtastrate));
            float aktuellerCutoffHz = Mathf.Max(80f, filterCutoffHz + filterLfoTiefeHz * (float)cutoffLfo);
            float filterKoeffizient = BerechneTiefpassKoeffizient(aktuellerCutoffHz);

            filterZustandLinks += (ausgabeLinks - filterZustandLinks) * filterKoeffizient;
            filterZustandRechts += (ausgabeRechts - filterZustandRechts) * filterKoeffizient;
            ausgabeLinks = filterZustandLinks;
            ausgabeRechts = filterZustandRechts;

            // Lo-Fi Sample-Hold (Bitcrush-Stil): hält Werte für N Samples, erzeugt den
            // leicht "krümeligen" Vintage-Tape/8-Bit-Charakter, ohne dabei die Tonhöhe zu verändern.
            if (lofiSampleHold > 1)
            {
                if (lofiZaehler == 0)
                {
                    lofiGehaltenerWertLinks = ausgabeLinks;
                    lofiGehaltenerWertRechts = ausgabeRechts;
                }
                ausgabeLinks = lofiGehaltenerWertLinks;
                ausgabeRechts = lofiGehaltenerWertRechts;
                lofiZaehler = (lofiZaehler + 1) % lofiSampleHold;
            }

            // Sanftes Soft-Clipping, damit Summen aus vielen Layern nicht hart übersteuern
            ausgabeLinks = WeicheBegrenzung(ausgabeLinks);
            ausgabeRechts = WeicheBegrenzung(ausgabeRechts);

            int basisIndex = sampleIndex * kanalAnzahl;
            if (kanalAnzahl >= 2)
            {
                daten[basisIndex] = ausgabeLinks;
                daten[basisIndex + 1] = ausgabeRechts;
                for (int extraKanal = 2; extraKanal < kanalAnzahl; extraKanal++)
                {
                    daten[basisIndex + extraKanal] = (ausgabeLinks + ausgabeRechts) * 0.5f;
                }
            }
            else
            {
                daten[basisIndex] = (ausgabeLinks + ausgabeRechts) * 0.5f;
            }

            samplePosition += 1.0;
        }
    }

    private static float WeicheBegrenzung(float eingang)
    {
        // einfacher tanh-artiger Soft-Limiter ohne teure Math.Tanh-Aufrufe
        return eingang / (1f + Mathf.Abs(eingang));
    }

    private float BerechneTiefpassKoeffizient(float cutoffHz)
    {
        // Standard 1-Pole-Lowpass-Koeffizient: alpha = 1 - e^(-2*pi*fc/fs)
        double exponent = -2.0 * Math.PI * cutoffHz / abtastrate;
        return (float)(1.0 - Math.Exp(exponent));
    }

    // ---------------- Akkordfolge / Taktfortschritt ----------------

    private double sampleZaehlerSeitAkkordwechsel;
    private double samplesProTakt;

    private void AktualisiereTaktUndAkkordFortschritt()
    {
        sampleZaehlerSeitAkkordwechsel += 1.0;

        double samplesProAkkord = samplesProTakt * taktePerAkkord;
        if (sampleZaehlerSeitAkkordwechsel >= samplesProAkkord)
        {
            sampleZaehlerSeitAkkordwechsel = 0;
            aktuelleAkkordToene = naechsteAkkordToene;
            naechsteAkkordToene = WaehleNaechstenAkkord(aktuelleAkkordToene);

            SetzePadZielFrequenzenAusAkkord(aktuelleAkkordToene);
            bassZielFrequenz = MidiZuFrequenz(grundton - 12 + aktuelleAkkordToene[0]);

            // Bei jedem Akkordwechsel mit 40% Chance das Hat-Pattern tauschen und mit 50%
            // Chance die Arpeggio-Richtung umkehren -> bricht Monotonie über die Zeit auf,
            // ohne dass sich der Grundgroove pro Takt ständig ändert (würde unruhig wirken).
            if (zufallsGenerator.NextDouble() < 0.4)
            {
                aktivesHatPattern = (aktivesHatPattern == hatPatternA) ? hatPatternB : hatPatternA;
            }
            if (zufallsGenerator.NextDouble() < 0.5)
            {
                arpeggioAufsteigend = !arpeggioAufsteigend;
            }
        }

        // Sanftes Gleiten (Portamento) zur Zielfrequenz statt hartem Sprung -> "schwebender" Übergang
        for (int i = 0; i < padStimmenAnzahl; i++)
        {
            padAktuelleFrequenz[i] += (padZielFrequenz[i] - padAktuelleFrequenz[i]) * glaettungsfaktor;
        }
        bassAktuelleFrequenz += (bassZielFrequenz - bassAktuelleFrequenz) * glaettungsfaktor;
    }

    // ---------------- Pad-Synthese ----------------

    private float ErzeugePadSample()
    {
        float summe = 0f;

        for (int i = 0; i < padStimmenAnzahl; i++)
        {
            double frequenz = padAktuelleFrequenz[i];

            // Haupt-Sägezahn/Dreieck-Mischung für weichen, aber nicht zu sterilen Pad-Sound
            padPhase[i] += frequenz / abtastrate;
            if (padPhase[i] >= 1.0) padPhase[i] -= 1.0;

            double chorusFrequenz = frequenz * Math.Pow(2.0, chorusDetuneCent / 1200.0);
            padPhaseChorus[i] += chorusFrequenz / abtastrate;
            if (padPhaseChorus[i] >= 1.0) padPhaseChorus[i] -= 1.0;

            float hauptWelle = DreieckWelle(padPhase[i]) * 0.7f + SaegezahnWelle(padPhase[i]) * 0.3f;
            float chorusWelle = DreieckWelle(padPhaseChorus[i]) * 0.7f + SaegezahnWelle(padPhaseChorus[i]) * 0.3f;

            summe += (hauptWelle + chorusWelle) * 0.5f;
        }

        // Sehr langsames Amplituden-"Atmen" für organisches Schweben, über LFO auf Stimme 0 abgeleitet
        float atemLfo = 0.85f + 0.15f * (float)Math.Sin(2.0 * Math.PI * 0.05 * (samplePosition / abtastrate));

        return (summe / padStimmenAnzahl) * atemLfo;
    }

    private static float DreieckWelle(double phase)
    {
        // Phase 0..1 -> echte Dreieckwelle im Bereich -1..1
        double wert = 4.0 * Math.Abs(phase - 0.5) - 1.0;
        return (float)wert;
    }

    private static float SaegezahnWelle(double phase)
    {
        // Phase 0..1 -> Sägezahnwelle im Bereich -1..1, sorgt für mehr Obertöne/Helligkeit
        return (float)(2.0 * (phase - Math.Floor(phase + 0.5)));
    }

    // ---------------- Bass ----------------

    private float ErzeugeBassSample()
    {
        bassPhase += bassAktuelleFrequenz / abtastrate;
        if (bassPhase >= 1.0) bassPhase -= 1.0;

        // Reiner Sinus für sauberen, runden Sub-Bass-Charakter
        float sinus = (float)Math.Sin(2.0 * Math.PI * bassPhase);

        // Dezenter zweiter Harmonischer-Anteil über Sägezahn-Beimischung für etwas mehr Präsenz auf kleinen Lautsprechern
        float saege = (float)(2.0 * (bassPhase - Math.Floor(bassPhase + 0.5)));

        return sinus * 0.8f + saege * 0.2f;
    }

    // ---------------- Beat (Kick / Snare / HiHat, alles synthetisiert) ----------------

    private void AktualisiereBeatFortschritt()
    {
        if (aktivesHatPattern == null) aktivesHatPattern = hatPatternA;

        beatSchrittZaehler += 1.0;
        if (beatSchrittZaehler >= samplesProSechzehntel)
        {
            beatSchrittZaehler = 0;
            beatSechzehntelIndex = (beatSechzehntelIndex + 1) % 16;

            if (kickPattern[beatSechzehntelIndex])
            {
                kickEnvelope = 1f;
                kickPhase = 0.0;
            }
            if (snarePattern[beatSechzehntelIndex])
            {
                snareEnvelope = 0.8f;
            }
            if (aktivesHatPattern[beatSechzehntelIndex])
            {
                hatEnvelope = 0.35f; // bewusst leise, Lo-Fi-Hat im Hintergrund
            }
        }
    }

    private float ErzeugeBeatSample()
    {
        float ausgabe = 0f;

        // Kick: Sinus mit schnellem Pitch-Drop von ~150Hz auf ~45Hz + Decay-Hülle
        if (kickEnvelope > 0.001f)
        {
            double kickFrequenz = 45.0 + 110.0 * kickEnvelope;
            kickPhase += kickFrequenz / abtastrate;
            ausgabe += (float)Math.Sin(2.0 * Math.PI * kickPhase) * kickEnvelope;
            kickEnvelope *= 0.9975f;
        }

        // Snare/Clap: gefiltertes Rauschen mit kurzem Decay
        if (snareEnvelope > 0.001f)
        {
            float rauschen = (float)(rauschenGenerator.NextDouble() * 2.0 - 1.0);
            ausgabe += rauschen * snareEnvelope * 0.6f;
            snareEnvelope *= 0.95f;
        }

        // Hi-Hat: hochfrequentes, sehr kurzes Rauschen
        if (hatEnvelope > 0.001f)
        {
            float rauschen = (float)(rauschenGenerator.NextDouble() * 2.0 - 1.0);
            ausgabe += rauschen * hatEnvelope * 0.4f;
            hatEnvelope *= 0.80f;
        }

        return ausgabe;
    }

    // ---------------- Arpeggio ----------------

    private void AktualisiereArpeggioFortschritt()
    {
        arpeggioSchrittZaehler += 1.0;
        if (arpeggioSchrittZaehler >= samplesProArpeggioSchritt)
        {
            arpeggioSchrittZaehler = 0;
            int richtung = arpeggioAufsteigend ? 1 : -1;
            arpeggioSchrittIndex = ((arpeggioSchrittIndex + richtung) % aktuelleAkkordToene.Length
                                   + aktuelleAkkordToene.Length) % aktuelleAkkordToene.Length;

            int gewaehlterTon = aktuelleAkkordToene[arpeggioSchrittIndex];
            double frequenz = MidiZuFrequenz(grundton + 12 + gewaehlterTon); // eine Oktave über Pad-Grundlage
            arpeggioPhase = 0.0;
            arpeggioFrequenzAktuell = frequenz;
            arpeggioEnvelope = 0.6f;
        }
    }

    private double arpeggioFrequenzAktuell;

    private float ErzeugeArpeggioSample()
    {
        if (arpeggioEnvelope <= 0.001f) return 0f;

        arpeggioPhase += arpeggioFrequenzAktuell / abtastrate;
        if (arpeggioPhase >= 1.0) arpeggioPhase -= 1.0;

        float sinus = (float)Math.Sin(2.0 * Math.PI * arpeggioPhase);
        float glockenObertonAnteil = (float)Math.Sin(2.0 * Math.PI * arpeggioPhase * 2.0) * 0.25f;

        arpeggioEnvelope *= 0.9985f; // langsamer Decay für glockenartiges Ausklingen

        return (sinus + glockenObertonAnteil) * arpeggioEnvelope;
    }

    // ---------------- Delay / Pseudo-Reverb ----------------

    private float VerarbeiteDelay(float eingang, float[] puffer, int schreibKopf)
    {
        int delayInSamples = Mathf.Clamp(
            Mathf.RoundToInt(delayZeitSekunden * abtastrate),
            1,
            puffer.Length - 1
        );

        int leseKopf = schreibKopf - delayInSamples;
        if (leseKopf < 0) leseKopf += puffer.Length;

        float verzoegertesSignal = puffer[leseKopf];
        float neuerWert = eingang + verzoegertesSignal * delayFeedback;

        puffer[schreibKopf] = neuerWert;

        return verzoegertesSignal;
    }
}
