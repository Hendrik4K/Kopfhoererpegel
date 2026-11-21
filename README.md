# Kopfhörerpegel

| Plattform | Ordner | Technik | Quellen |
| --- | --- | --- | --- |
| Windows | `src/`, `Bauen.ps1` | C# / WPF, WASAPI | AirPods in dB |
| Linux | [`linux/`](linux/README.md) | Python / Qt, PulseAudio · PipeWire | AirPods/Kopfhörer in dB, Mikrofone und Ausgänge in %, Empfänger (dB, % oder Rohwert), Demo |

Beide zeigen dieselbe Karte und dasselbe Tray-Symbol. Die Windows-App ist eigenständig; alles Plattformspezifische der
Linux-Version steckt in `linux/pegel/quellen/linux_pulse.py`.

## Windows

Tray-Symbol für die AirPods: Maus drüber → Karte mit Live-dB im Stil von „Kopfhörerpegel“ auf dem iPhone:
Status mit Symbol (OK grün · Laut gelb ab 80 dB · Sehr laut rot ab 90 dB), dB-Zahl (bei Laut/Sehr laut in der Statusfarbe)
und 18 Segmente à 5 dB von 20 bis 110 dB. Die Leiste füllt sich ruhig mit dem 1-Sekunden-Pegel; nur die beiden vordersten Segmente leben mit dem Beat (Sättigung folgt dem 125-ms-Pegel in festen Stufen: das äußerste 4 Stufen stark, das davor 5 Stufen dezent), alles links davon steht still; die Skala 20 / 80 / 110 bleibt immer gleich. Linksklick pinnt die Karte, nochmal klicken schließt sie.
Rechtsklick: Kalibrierung, „Mit Windows starten“, Neu starten, Beenden.
Die Karte ist milchig: Beim Öffnen fotografiert sie, was hinter ihr liegt, zeichnet es weich und legt eine dunkle Tönung darüber; große runde Ecken (49 px). Das Hintergrundbild wird nur beim Öffnen aufgenommen, weil Windows-Bildschirmkopien die Karte selbst mit einfangen würden.
Fehler landen in `%LOCALAPPDATA%\Kopfhoererpegel\fehler.log` statt in einem Dialog.

Tray-Symbol: Ohr mit Badge (grüner Haken, gelbes Ausrufezeichen, rotes Warndreieck) und drei Punkten, die mit dem Beat in Stufen pulsieren: OK = Punkt 1 dezent + Punkt 2 stark, Punkt 3 aus; Laut = Punkt 1 ruhig, 2 dezent, 3 stark; Sehr laut = Punkt 1 + 2 ruhig, 3 stark; ohne Wiedergabe kein Badge; AirPods nicht verbunden = kein Symbol (erscheint wieder, sobald sie verbunden sind).

### Wie gemessen wird

Wie beim iPhone **berechnet**, nicht im Ohr gemessen:
WASAPI-Loopback auf dem AirPods-Stereo-Endpunkt (A2DP) → A-Bewertung → 1-Sekunden-Mittel →
+ Windows-Lautstärke der AirPods (Bluetooth-Hardwarelautstärke, Loopback liegt davor) + Kalibrierung (Standard 106 dB).
Der Freisprech-Endpunkt (Hands-Free) wird absichtlich nicht geöffnet, damit die AirPods nicht in den Anrufmodus springen.

Kalibrieren: gleiches Lied am iPhone und am PC bei gleicher Lautstärke vergleichen und per Rechtsklick ±1 dB anpassen.
Einstellung liegt in `%LOCALAPPDATA%\Kopfhoererpegel\einstellungen.txt`.

### Bauen / Testen

- `Bauen.ps1` (C#-Compiler aus .NET Framework, kein SDK nötig) → `Kopfhoererpegel.exe`
- `--probe <datei>` listet die Audio-Endpunkte und schreibt 4 s Messwerte in die Datei
- `--demo` zeigt Fantasiewerte, `--show` öffnet die Karte gleich gepinnt
- `--render-tray <ordner>` speichert alle Symbolzustände als PNG
