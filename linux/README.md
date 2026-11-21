# Kopfhörerpegel für Linux

Dieselbe milchige Karte und dasselbe Tray-Symbol wie die Windows-App, in Python mit Qt (PySide6).
Neben den AirPods gehen auch andere Mikrofone, Ausgänge und Empfänger:

- **dB**, wenn die Quelle es kann: AirPods/Kopfhörer (kalibrierte dB(A) wie unter Windows) oder ein Empfänger, der dB liefert
- sonst **Pegel in %** (Mikrofone, Ausgänge) oder der **Rohwert** eines Empfängers

Der Wert wird so angezeigt, wie die Quelle ihn liefert. Umgerechnet wird nichts; nur das 1-Sekunden-Mittel und der Beat werden daraus abgeleitet.
Die Skala passt sich an: dB 20 / 80 / 110, Prozent 0 / 80 / 100, Rohwerte nach `--bereich`. „Laut“ beginnt bei 80 % der Skala, „Sehr laut“ bei 90 %.

## Installieren (Debian / Raspberry Pi OS / Ubuntu)

```bash
sudo apt install python3-pyside6.qtwidgets pulseaudio-utils
```

Falls es die PySide6-Pakete in der Distribution nicht gibt: `pip install --user PySide6`.
`pulseaudio-utils` bringt `pactl`/`parec` und läuft auch mit PipeWire (über pipewire-pulse); unter PipeWire wird bevorzugt `pw-record` benutzt.

## Starten

```bash
python3 kopfhoererpegel.py                                   # AirPods in dB (Standard)
python3 kopfhoererpegel.py --geraet "Sony"                   # andere Kopfhörer in dB
python3 kopfhoererpegel.py --quelle mikrofon                 # Standard-Mikrofon in %
python3 kopfhoererpegel.py --quelle mikrofon --geraet "USB"  # bestimmtes Mikrofon
python3 kopfhoererpegel.py --quelle ausgang                  # Standard-Ausgang in %
python3 kopfhoererpegel.py --quelle extern --befehl "mein-empfaenger" --bereich 0:1023 --titel "Empfänger"
python3 kopfhoererpegel.py --quelle demo                     # Fantasiewerte zum Anschauen
```

**Empfänger (`--quelle extern`)**: ein Befehl (oder stdin mit `--befehl -`), der pro Zeile einen Wert ausgibt:
`73.5 dB` (auch `dBA`, `dB(A)`), `42 %` oder eine reine Zahl (Rohwert, Skala über `--bereich MIN:MAX`, sonst 0–100).
Der erste Wert legt die Einheit fest. Kommt 5 s lang nichts, gilt der Empfänger als getrennt.

**Weitergeben (`--ausgabe`)**: schreibt den angezeigten Wert jede Sekunde auf stdout, im selben Format, z. B.
`python3 kopfhoererpegel.py --ausgabe | python3 kopfhoererpegel.py --quelle extern --titel Kopie`.

Bedienung: Klick auf das Ohr öffnet die Karte, nächster Klick schließt sie (Linux-Infobereiche melden kein Hovern; der Tooltip zeigt den Wert immer).
Rechtsklick: Kalibrierung (nur bei dB-Kopfhörern), „Mit Anmeldung starten“ (`~/.config/autostart`), Neu starten, Beenden.
Ist das Gerät nicht da, verschwindet das Symbol, bis es wieder verbunden ist.

## Aufbau

| Datei | Inhalt |
| --- | --- |
| `pegel/werte.py` | Messwert mit Einheit und Skala, Status OK/Laut/Sehr laut, Beat-Anteil |
| `pegel/messung.py` | A-Bewertung, 1-s-Mittel, Prozent – identisch zur Windows-App |
| `pegel/quellen/basis.py` | Schnittstelle, die jede Quelle erfüllt; die Oberfläche kennt nur diese |
| `pegel/quellen/linux_pulse.py` | **alles Linux-Spezifische**: Geräte über `pactl -f json`, Aufnahme über `pw-record`/`parec` |
| `pegel/quellen/extern.py`, `demo.py` | Empfänger und Demo, laufen auf jedem System |
| `pegel/oberflaeche/` | Karte, Tray-Symbol, Menü (Qt) |

Ein macOS-Backend wäre ein weiteres Modul neben `linux_pulse.py`; die Windows-App (`../src`) ist eigenständig und bleibt unberührt.

**Wie dB gemessen wird** (wie unter Windows): Monitor des Ausgangs mitschneiden (passiv, hält die Kopfhörer nicht wach) → A-Bewertung → 1-Sekunden-Mittel
\+ Lautstärke des Ausgangs in dB + Kalibrierung (Standard 106). Die Lautstärke wird addiert, wenn der Monitor vor ihr liegt: immer bei PipeWire, bei
klassischem PulseAudio nur bei Hardware-Lautstärke (Bluetooth).

**Milchiger Hintergrund**: unter X11 (z. B. Cinnamon, Xfce) wie unter Windows ein weichgezeichnetes Bild dessen, was hinter der Karte liegt.
Wayland erlaubt dieses Bild nicht; dort bekommt die Karte eine dichtere Tönung.

## Tests

```bash
python3 -m unittest discover -s tests          # Logik, ohne Qt und ohne Audio
python3 kopfhoererpegel.py --quelle demo --render-karte karte.png
python3 kopfhoererpegel.py --render-tray tray/
```

Fehler landen in `~/.cache/kopfhoererpegel/fehler.log` statt in einem Dialog.
