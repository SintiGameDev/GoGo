# Mirror's-Edge-Style Movement System — Setup-Anleitung

## Überblick: alle 12 Skripte

| Skript | Aufgabe |
|---|---|
| `PlayerMotor` | Zentrale Velocity + CharacterController-Bewegung + Schwerkraft |
| `PlayerInputContext` | Sammelt rohen Input (Tasten, Maus, Achsen) an einer Stelle |
| `PlayerLook` | FPS-Kamera (Pitch/Yaw) + Auto-Rotation bei Walljump |
| `PlayerMomentum` | Flow-Speed-Multiplikator (steigt durch Aktionen, klingt über Zeit ab) |
| `GroundMovement` | Laufen (WASD) + normaler Sprung |
| `WallDetector` | Reine Erkennung: ist eine Wand vor dem Spieler? |
| `WalljumpHandler` | Walljump per Tastendruck, Richtung = Blickrichtung (POV) |
| `WallrunHandler` | Automatischer Wallrun bei seitlichem Anlauf mit Tempo |
| `VaultHandler` | Drüberspringen niedriger Hindernisse (0.35–1.2 m) |
| `ClimbHandler` | Hochziehen an Kanten (1.2–2.2 m) |
| `SlideHandler` | Bodenrutschen mit tempo-abhängigem Momentum-Boost |
| `PlayerActionResolver` | Die EINE Stelle, die die Action-Taste auswertet (Climb > Vault > Walljump > Jump) |

Alle Skripte liegen in einem Ordner, z. B. `Assets/Scripts/Movement/`.

---

## Schritt 1 — Player-GameObject erstellen

1. **Leeres GameObject** erstellen, nennen z. B. `Player`.
2. Position auf den gewünschten Levelstart setzen.

### Component: `CharacterController`
- **Height**: `1.8`
- **Radius**: `0.4`
- **Center**: `(0, 0.9, 0)` (Mitte auf halbe Höhe)
- **Slope Limit**: `45`
- **Step Offset**: `0.3`

### Components auf dem `Player`-Objekt (alle 12 Skripte draufziehen)
Alle Skripte kommen auf **dasselbe** `Player`-GameObject:

- `PlayerMotor`
- `PlayerInputContext`
- `PlayerLook`
- `PlayerMomentum`
- `GroundMovement`
- `WallDetector`
- `WalljumpHandler`
- `WallrunHandler`
- `VaultHandler`
- `ClimbHandler`
- `SlideHandler`
- `PlayerActionResolver`

Da fast alle Skripte ihre Referenzen über `GetComponent<...>()` in `Awake()` automatisch finden (wenn die Felder im Inspector leer gelassen werden), reicht es in den meisten Fällen, sie einfach draufzuziehen — **eine Ausnahme** ist unten in Schritt 4 genannt.

---

## Schritt 2 — Kamera als Child

1. **Leeres GameObject** als Child von `Player` erstellen, nennen `CameraHolder` (Position `(0, 1.6, 0)` — Augenhöhe).
2. Darin die **Kamera** (Child von `CameraHolder`, Position `(0,0,0)`).
3. `PlayerLook.playerCamera` im Inspector auf diese Kamera ziehen (Auto-Detect via `GetComponentInChildren<Camera>()` funktioniert auch automatisch, wenn die Kamera irgendwo unter `Player` liegt).

```
Player (CharacterController, alle Movement-Skripte)
 └── CameraHolder
      └── Main Camera
```

> Hinweis: `PlayerLook` rotiert aktuell `transform` (also das `Player`-Root-Objekt) für Yaw und `playerCamera.transform.localRotation` für Pitch. Die Kamera kann daher direkt als Child der `CameraHolder` liegen, ohne dass `CameraHolder` selbst eine eigene Rotation braucht.

---

## Schritt 3 — Tags & Layer

- Stelle sicher, dass Wände/Levelgeometrie auf einem Layer liegen, der in den `LayerMask`-Feldern (`WallDetector.wallLayerMask`, `VaultHandler.vaultLayerMask`, `ClimbHandler.climbLayerMask`, `SlideHandler.standUpCheckLayerMask`) eingeschlossen ist. Standardmäßig sind alle auf `Everything` (`~0`) gesetzt — funktioniert sofort, aber schränke es später sinnvoll ein (z. B. einen eigenen Layer `Level` anlegen), damit z. B. der Spieler-Collider selbst sich nicht versehentlich selbst trifft.

---

## Schritt 4 — Inspector-Verkabelung (wichtig!)

Die meisten Referenzen finden sich automatisch über `GetComponent` auf demselben GameObject. **Eine Verkabelung ist aber Pflicht**, da sie zwischen zwei verschiedenen Skripten verläuft, die sich nicht von selbst kennen:

### `WalljumpHandler` → `wallrunHandler`
Im Inspector auf dem `Player`-Objekt: bei `WalljumpHandler` das Feld **`Wallrun Handler`** auf die `WallrunHandler`-Komponente desselben Objekts ziehen.

*(Technisch würde `GetComponent<WallrunHandler>()` das auch automatisch finden, da beide auf demselben Objekt liegen — aber prüfe es einmal im Inspector nach dem ersten Play-Test, falls Walljump aus dem Wallrun heraus nicht funktioniert.)*

### `PlayerActionResolver` — alle Felder
Falls die Auto-Detection beim ersten Start nicht greift (z. B. Skript-Ausführungsreihenfolge), im Inspector manuell nachziehen:
- `Input` → `PlayerInputContext`
- `Ground Movement` → `GroundMovement`
- `Walljump Handler` → `WalljumpHandler`
- `Vault Handler` → `VaultHandler`
- `Climb Handler` → `ClimbHandler`

---

## Schritt 5 — Tasten-Belegung prüfen

In `PlayerInputContext`:
- **Action Key**: `Space` (Jump / Walljump / Vault / Climb — kontextabhängig)
- **Sprint Key**: `Left Shift` (aktuell nicht aktiv verdrahtet, siehe Hinweis unten)
- **Slide Key**: `Left Control`

> Hinweis: `PlayerMomentum` hat aktuell **keine feste Sprint-Stufe** mehr (das war eine bewusste Designentscheidung — Momentum-System statt Walk/Run). Das `sprintKey`-Feld in `PlayerInputContext` ist vorbereitet, aber noch nicht in `GroundMovement` verdrahtet. Falls du Sprint zusätzlich zum Momentum-System willst, sag Bescheid.

---

## Schritt 6 — Erste Test-Werte (Defaults sind bereits sinnvoll vorbelegt)

| Skript | Wichtigster Wert | Default |
|---|---|---|
| `PlayerMomentum` | `baseMoveSpeed` | 7.5 m/s |
| `PlayerMomentum` | `maxMultiplier` | 2.2x |
| `WalljumpHandler` | `wallJumpForwardSpeed` / `wallJumpUpSpeed` | 8 / 9 m/s |
| `WallrunHandler` | `minSpeedToStartWallrun` | 5 m/s |
| `VaultHandler` | `maxVaultHeight` | 1.2 m |
| `ClimbHandler` | `minClimbHeight` / `maxClimbHeight` | 1.2 / 2.2 m |
| `SlideHandler` | `minSpeedToStartSlide` | 3 m/s |

Alle Werte sind über den Inspector live im Play-Mode anpassbar — am besten direkt im Test-Level gegen eine einfache Wand/Kiste/Mauer ausprobieren und nachjustieren.

---

## Schritt 7 — Debug-Logs

Jedes Skript hat ein `showDebugInfo`-Toggle (Default meist `true`). Beim ersten Testen helfen die Console-Logs (`🧱 Walljump!`, `🏃 Wallrun gestartet!`, `🤸 Vault abgeschlossen`, `🧗 Climb abgeschlossen`, `🛷 Slide gestartet!`) enorm, um zu sehen, welcher Kontext gerade greift. Nach dem Fein-Tuning auf `false` setzen, um die Console sauber zu halten.

---

## Bekannte Lücken / Was als Nächstes sinnvoll wäre

- **Kein Sprint-Stufensystem** — bewusst durch Momentum ersetzt (siehe Schritt 5).
- **Keine Landing-/Fall-Damage-Logik.**
- **Keine Kamera-Effekte** (FOV-Zoom bei Tempo, Head-Bob, Walljump-Screenshake) — das alte System hatte das (`SC_FPSController.UpdateFOV`, `HeadBang`), im neuen System bisher nicht nachgebaut.
- **Vault/Climb nutzen `Teleport()`** statt reiner Physik-Velocity — fühlt sich kontrolliert/präzise an, aber nicht ganz physikalisch. Falls das zu steif wirkt, können wir es durch eine velocity-basierte Parabel ersetzen.
