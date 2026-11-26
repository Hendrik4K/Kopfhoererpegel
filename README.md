# <img src="docs/icon.png" height="40" alt=""> Kopfhörerpegel <a href="README.en.md"><img align="right" src="docs/flag-gb.svg" height="22" alt="English" title="English"></a><a href="README.md"><img align="right" src="docs/flag-de.svg" height="22" alt="Deutsch" title="Deutsch"></a><img align="right" src="docs/sprache.svg" height="22" alt="Sprache" title="Sprache wählen">

**Wie der Kopfhörerpegel auf dem iPhone, nur für Windows: Sieh eine funktional gleiche Dezibel-Anzeige statt Prozenten. Deine AirPods, berechnet wie auf deinem Handy.**

<p align="center"><img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6" alt="Windows 10 | 11"> <a href="https://github.com/Hendrik4K/Kopfhoererpegel/releases/latest"><img src="https://img.shields.io/github/v/release/Hendrik4K/Kopfhoererpegel?label=Version&color=30D158" alt="Version"></a> <img src="https://img.shields.io/badge/Stream%20Deck-Plugin-1F1F1F" alt="Stream Deck Plugin"> <img src="https://img.shields.io/badge/AirPods-alle%20Modelle-555" alt="AirPods"></p>

<p align="center"><img src="docs/pegel-ablauf.gif" width="820" alt="Maus auf das Ohr-Symbol in der Taskleiste: die Karte zeigt den Pegel live, von OK über Laut bis Sehr laut"></p>

Super verständliche Oberfläche mit kleinen Warnungen, falls es mal zu laut wird. Selbsterklärend, einfach bedienbar.

**⬇ [Kostenlos für Windows](https://github.com/Hendrik4K/Kopfhoererpegel/releases/latest/download/Kopfhoererpegel.exe)** · **[Kostenlos für Stream Deck](https://github.com/Hendrik4K/Kopfhoererpegel/releases/latest/download/Headphone-Level.streamDeckPlugin)** (Doppelklick installiert es in Stream Deck)  
**⭐ Gefällt’s dir? Lass einen Stern da!**
Lieber unterstützen? Die gleiche Stream-Deck-Version gibt es für 0,99 € im Elgato Marketplace.

<details>
<summary><b>🚀 Schnellstart</b></summary>

1. `Kopfhoererpegel.exe` unter **Releases** herunterladen und an einen festen Ort legen (z. B. `Dokumente\Kopfhoererpegel`). Meldet Windows SmartScreen „Der Computer wurde durch Windows geschützt“: **Weitere Informationen → Trotzdem ausführen** (die exe ist nicht code-signiert).
2. Starten. Sobald deine AirPods verbunden sind, erscheint das Ohr unten rechts in der Taskleiste (liegt es im ^-Überlauf, zieh es einfach in die Leiste).
3. Maus drauf: Pegel. Linksklick: Karte bleibt offen. Rechtsklick: Einstellungen, dort „Mit Windows starten“ anhaken. Fertig.

**Stream Deck:** `Headphone-Level.streamDeckPlugin` doppelklicken, dann „Pegel“ oder „Verlauf“ aus der Kategorie „Kopfhörerpegel“ auf eine Taste ziehen.

**Du bist ein entspannter Typ oder ein entspanntes Girl?** Dann gib einfach diesen Prompt an deinen KI-Agenten (z. B. Claude Code oder Codex auf deinem PC). Er schaut nach, was schon da ist, lädt die richtige Datei, prüft sie und fragt dich bei allem, was er ändern will.

```text
Hilf mir, Kopfhörerpegel (github.com/Hendrik4K/Kopfhoererpegel) auf diesem Windows-PC einzurichten, damit ich den Pegel meiner AirPods live in der Taskleiste sehe.
Prüfe zuerst nur und frag mich vor jeder Änderung:
1. Windows 10/11? Sind AirPods per Bluetooth gekoppelt und gerade verbunden (Audio-Endpunkt mit „AirPods“ im Namen)? Wenn nicht, sag mir, wie ich sie verbinde.
2. Lade `Kopfhoererpegel.exe` und `Kopfhoererpegel.exe.sha256` aus dem neuesten GitHub-Release und vergleiche die SHA256. Bei Abweichung abbrechen.
3. Schlag mir einen festen Ordner vor (z. B. `%USERPROFILE%\Documents\Kopfhoererpegel`), leg die exe nach meinem OK dort ab und entferne den Download-Vermerk (Unblock-File).
4. Starte die exe nach meinem OK über `explorer.exe` (nicht als Kindprozess deiner Shell).
5. Frag mich, ob sie mit Windows starten soll. Wenn ja: Rechtsklick aufs Ohr-Symbol → „Mit Windows starten“ anhaken (oder du legst nach meinem OK den Run-Eintrag „Kopfhoererpegel“ unter HKCU an, Wert: `"<Pfad zur exe>" --autostart`).
6. Frag mich, ob ich ein Elgato Stream Deck habe. Wenn ja: `Headphone-Level.streamDeckPlugin` samt .sha256 laden, Hash prüfen und erst nach meinem OK per Doppelklick installieren.
7. Sag mir am Ende, wo das Ohr in der Taskleiste sitzt (eventuell im ^-Überlauf, dann in die Leiste ziehen) und wie ich per Rechtsklick → Kalibrierung ±1 dB an mein iPhone angleiche.
Ändere nichts an meinen Audio-Einstellungen und installiere keine weitere Software.
```

</details>

<details>
<summary><b>💡 Was es kann</b></summary>

- **Live-Pegel in dB(A)**<br>Status mit Symbol (OK · Laut ab 80 dB · Sehr laut ab 90 dB), dB-Zahl und 18 Segmente von 20 bis 110 dB, die vordersten pulsieren mit dem Beat.
- **Milchglas-Karte wie auf dem iPhone**<br>Große runde Ecken, weich gezeichneter Hintergrund, blendet sanft ein und aus. Bleibt offen, solange die Maus auf Symbol oder Karte ist.
- **Tray-Symbol, das mitspielt**<br>Ohr mit grünem Haken, gelbem Ausrufezeichen oder rotem Warndreieck, darunter drei Punkte im Takt. Ohne AirPods verschwindet es einfach.
- **AirPods bleiben im Musikmodus**<br>Gemessen wird per Loopback, der Freisprech-Kanal wird nie geöffnet, also kein Umschalten auf Telefonqualität.
- **Stream Deck**<br>Dieselbe Anzeige auf einer Taste, Drücken zeigt die letzten 60 Sekunden, bei „Sehr laut“ blinkt die Taste rot.
- **Linux auch**<br>Eine Python/Qt-Version mit derselben Karte, siehe [`linux/`](linux/README.md).

<ins>Warum es das gibt:</ins> Am iPhone schaue ich ständig auf „Kopfhörerpegel“, am PC gab es das nicht. Jetzt schon.

</details>

<details>
<summary><b>⚙️ Einstellungen</b></summary>

<p align="center"><img src="docs/einstellungen-ablauf.gif" width="820" alt="Rechtsklick auf das Ohr-Symbol: die Karte zeigt die Einstellungen, Kalibrierung klappt aus, +1 dB"></p>

Rechtsklick aufs Ohr, und die Karte wird zu den Einstellungen:

- **Mit Windows starten** zum Anhaken.
- **Kalibrierung**: mit der Maus drüberfahren, dann klappen **−1 dB**, **+1 dB** und **↺** (Standard 106 dB) aus. Zum Abgleichen dasselbe Lied am iPhone und am PC bei gleicher Lautstärke vergleichen.
- **Beenden** (der große Knopf) und darunter **Neu starten**.

Maus weg von Karte und Symbol, und die Karte blendet sich aus. Die Einstellungen liegen in `%LOCALAPPDATA%\Kopfhoererpegel\einstellungen.txt`.

</details>

<details>
<summary><b>🎛️ Stream Deck</b></summary>

<p align="center"><img src="docs/streamdeck.png" width="820" alt="Headphone Level auf dem Stream Deck: OK, Laut und Sehr laut"></p>

Plugin `streamdeck/de.hendrik4k.kopfhoererpegel.sdPlugin`: Aktion „Kopfhörerpegel“ auf eine Taste ziehen.
Die Taste zeigt Status mit Badge, dB-Zahl (bei Laut/Sehr laut in der Statusfarbe) und die 18-Segment-Leiste, deren vorderste
Segmente wie auf der Karte mit dem Beat pulsieren. Drücken wechselt zum Verlauf der letzten 60 Sekunden (Balken in Statusfarbe,
gestrichelte Linie bei 80 dB) und zurück. Ohne AirPods: Ohr + „Nicht verbunden“.

Das Plugin misst selbst (`src/Meter.cs` + `src/CoreAudio.cs` werden mitkompiliert), läuft also auch ohne die Tray-App,
und liest dieselbe Kalibrierung aus `einstellungen.txt`. Gemessen wird nur, solange eine Taste sichtbar ist.
Fehler landen in `%LOCALAPPDATA%\Kopfhoererpegel\streamdeck.log`.

- `streamdeck\Bauen.ps1` baut EXE und Bilder in den Plugin-Ordner
- `-Installieren` kopiert ihn nach `%APPDATA%\Elgato\StreamDeck\Plugins` und startet Stream Deck neu
- `-Paket` erzeugt `Kopfhoererpegel.streamDeckPlugin` (Doppelklick installiert es auf jedem PC)
- `kopfhoererpegel-streamdeck.exe --render <ordner>` speichert alle Tastenzustände als PNG
- Zwei Aktionen: „Pegel“ und „Verlauf“ (Marketplace verlangt mindestens 2); bei „Sehr laut“ blinkt die Taste rot.
  Texte auf der Taste folgen der Stream-Deck-Sprache (Deutsch/Englisch).

**Elgato Marketplace**

- Prüfen und packen mit der Elgato-CLI (`npm install -g @elgato/cli`):
  `streamdeck validate de.hendrik4k.kopfhoererpegel.sdPlugin` und `streamdeck pack de.hendrik4k.kopfhoererpegel.sdPlugin`
- `kopfhoererpegel-streamdeck.exe --store streamdeck\marketplace` erzeugt Vorschaubild und 3 Galeriebilder (1920 × 960)
- Texte für Maker Console: `streamdeck/marketplace/listing.md`

</details>

<details>
<summary><b>📏 Wie gemessen wird</b></summary>

Wie beim iPhone **berechnet**, nicht im Ohr gemessen:
WASAPI-Loopback auf dem AirPods-Stereo-Endpunkt (A2DP) → A-Bewertung → 1-Sekunden-Mittel →
+ Windows-Lautstärke der AirPods (Bluetooth-Hardwarelautstärke, Loopback liegt davor) + Kalibrierung (Standard 106 dB).
Der Freisprech-Endpunkt (Hands-Free) wird absichtlich nicht geöffnet, damit die AirPods nicht in den Anrufmodus springen.

Kalibrieren: gleiches Lied am iPhone und am PC bei gleicher Lautstärke vergleichen und per Rechtsklick → Kalibrierung ±1 dB anpassen.
Einstellung liegt in `%LOCALAPPDATA%\Kopfhoererpegel\einstellungen.txt`.

</details>

<details>
<summary><b>🛠️ Selbst bauen (für Bastler)</b></summary>

Kein SDK nötig, der C#-Compiler von .NET Framework ist bei Windows dabei.

- `Bauen.ps1` → `Kopfhoererpegel.exe`
- `--probe <datei>` listet die Audio-Endpunkte und schreibt 4 s Messwerte in die Datei
- `--demo` zeigt Fantasiewerte, `--show` öffnet die Karte gleich gepinnt
- `--render-tray <ordner>` speichert alle Symbolzustände als PNG
- `--render-icon <datei.ico>` erzeugt das App-Icon (`Kopfhoererpegel.ico`, wird beim Bauen eingebettet)
- `--render-shots <ordner> <hintergrundbild>` rendert die README-Bilder und GIF-Einzelbilder (aufgeräumter Desktop,
  nachgebaute Taskleiste, Mauszeiger); `python tools/gifs.py <ordner> docs` macht daraus `docs/*.gif` und `docs/*.png`


### Alle Details der Windows-App

Tray-Symbol für die AirPods: Maus drüber → Karte mit Live-dB im Stil von „Kopfhörerpegel“ auf dem iPhone:
Status mit Symbol (OK grün · Laut gelb ab 80 dB · Sehr laut rot ab 90 dB), dB-Zahl (bei Laut/Sehr laut in der Statusfarbe)
und 18 Segmente à 5 dB von 20 bis 110 dB. Die Leiste füllt sich ruhig mit dem 1-Sekunden-Pegel; nur die beiden vordersten Segmente leben mit dem Beat (Sättigung folgt dem 125-ms-Pegel in festen Stufen: das äußerste 4 Stufen stark, das davor 5 Stufen dezent), alles links davon steht still; die Skala 20 / 80 / 110 bleibt immer gleich. Linksklick pinnt die Karte, nochmal klicken schließt sie.
Die Karte bleibt offen, solange die Maus auf dem Symbol oder auf der Karte ist, und blendet sich erst danach aus.

Rechtsklick macht aus der Karte die Einstellungen: links „Mit Windows starten“ zum Anhaken und „Kalibrierung“, das beim
Drüberfahren −1 dB / +1 dB / Standard (106 dB) ausklappt; rechts der große „Beenden“-Knopf, darunter „Neu starten“ als Link.

Die Karte ist milchig: Beim Öffnen fotografiert sie, was hinter ihr liegt, zeichnet es weich und legt eine dunkle Tönung darüber; große runde Ecken (49 px). Das Hintergrundbild wird nur beim Öffnen aufgenommen, weil Windows-Bildschirmkopien die Karte selbst mit einfangen würden.
Fehler landen in `%LOCALAPPDATA%\Kopfhoererpegel\fehler.log` statt in einem Dialog.

Tray-Symbol: Ohr mit Badge (grüner Haken, gelbes Ausrufezeichen, rotes Warndreieck) und drei Punkten, die mit dem Beat in Stufen pulsieren: OK = Punkt 1 dezent + Punkt 2 stark, Punkt 3 aus; Laut = Punkt 1 ruhig, 2 dezent, 3 stark; Sehr laut = Punkt 1 + 2 ruhig, 3 stark; ohne Wiedergabe kein Badge; AirPods nicht verbunden = kein Symbol (erscheint wieder, sobald sie verbunden sind).


| Plattform | Ordner | Technik | Quellen |
| --- | --- | --- | --- |
| Windows | `src/`, `Bauen.ps1` | C# / WPF, WASAPI | AirPods in dB |
| Stream Deck | `streamdeck/` | C#-Plugin, nutzt dieselbe Messung | AirPods in dB auf einer Taste |
| Linux | [`linux/`](linux/README.md) | Python / Qt, PulseAudio · PipeWire | AirPods/Kopfhörer in dB, Mikrofone und Ausgänge in %, Empfänger (dB, % oder Rohwert), Demo |

</details>

<details>
<summary><b>📜 Lizenz</b></summary>

Privat kostenlos nutzbar. Weitergeben, Verkaufen und Kopieren sind nicht erlaubt, siehe [LICENSE.md](LICENSE.md).

AirPods und iPhone sind Marken von Apple Inc., Stream Deck und Elgato von Corsair. Dieses Projekt ist mit keiner der Firmen verbunden.

</details>
