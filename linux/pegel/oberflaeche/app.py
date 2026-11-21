"""Tray app: icon with badge and three pulsing dots, the card on click, right-click menu.

Linux panels have no hover event for tray icons, so a click opens (and pins) the card and the next click closes it;
the tooltip always shows the current value.
"""
from __future__ import annotations

import argparse
import math
import os
import shlex
import subprocess
import sys
from pathlib import Path

from PySide6.QtCore import QLockFile, QPoint, QTimer, Qt
from PySide6.QtGui import QAction, QCursor, QGuiApplication
from PySide6.QtWidgets import QApplication, QMenu, QSystemTrayIcon

from .. import quellen, werte
from ..protokoll import datenordner, fehler_schreiben
from . import zeichnen
from .karte import Karte


# ---- settings and autostart ----------------------------------------------------------------------------------------

def _einstellungsdatei() -> Path:
    basis = Path(os.environ.get("XDG_CONFIG_HOME", Path.home() / ".config"))
    return basis / "kopfhoererpegel" / "einstellungen.txt"


def einstellung_lesen(name: str, standard: float) -> float:
    try:
        for zeile in _einstellungsdatei().read_text(encoding="utf-8").splitlines():
            if zeile.startswith(name + "="):
                return float(zeile.split("=", 1)[1])
    except (OSError, ValueError):
        pass
    return standard


def einstellung_schreiben(name: str, wert: float) -> None:
    datei = _einstellungsdatei()
    datei.parent.mkdir(parents=True, exist_ok=True)
    zeilen = [z for z in (datei.read_text(encoding="utf-8").splitlines() if datei.exists() else [])
              if not z.startswith(name + "=")]
    zeilen.append(f"{name}={wert:g}")
    datei.write_text("\n".join(zeilen) + "\n", encoding="utf-8")


def _autostart_datei() -> Path:
    basis = Path(os.environ.get("XDG_CONFIG_HOME", Path.home() / ".config"))
    return basis / "autostart" / "kopfhoererpegel.desktop"


def _startbefehl(argumente: list[str]) -> list[str]:
    skript = Path(__file__).resolve().parents[2] / "kopfhoererpegel.py"
    return [sys.executable, str(skript), *argumente]


def autostart_setzen(an: bool, argumente: list[str]) -> None:
    datei = _autostart_datei()
    if not an:
        datei.unlink(missing_ok=True)
        return
    datei.parent.mkdir(parents=True, exist_ok=True)
    datei.write_text("[Desktop Entry]\nType=Application\nName=Kopfhörerpegel\n"
                     f"Exec={shlex.join(_startbefehl(argumente))}\nX-GNOME-Autostart-enabled=true\n", encoding="utf-8")


# ---- app -----------------------------------------------------------------------------------------------------------

def _punkte(status: str, beat: float) -> list[float]:
    """Which of the three tray dots pulse (same table as the Windows app)."""
    stark = 0.25 + 0.75 * round(beat * 3) / 3
    sanft = 0.6 + 0.4 * round(beat * 4) / 4
    return {werte.OK: [sanft, stark, 0.0], werte.LAUT: [1.0, sanft, stark],
            werte.SEHR_LAUT: [1.0, 1.0, stark]}.get(status, [0.0, 0.0, 0.0])


def _taskleiste_hell() -> bool:
    try:
        return QGuiApplication.styleHints().colorScheme() == Qt.ColorScheme.Light
    except AttributeError:
        return False


class App:
    def __init__(self, args: argparse.Namespace, weiter: list[str]):
        self.args = args
        self.weiter = weiter  # arguments to repeat for restart/autostart
        self.quelle = quellen.erstellen(args.quelle, args.geraet, einstellung_lesen("Kalibrierung", 106),
                                        args.befehl, args.bereich, args.titel, args.einheit)
        self.karte = Karte(self.quelle.messwert().skala)
        self.tray = QSystemTrayIcon()
        self.tray.activated.connect(self._geklickt)
        self.tray_schluessel = ""
        self.tray_beat = 0.5
        self._menue()
        self.quelle.start()
        self.bild = 0
        self.zeitgeber = QTimer(interval=33, timeout=self._takt)
        self.zeitgeber.start()
        if args.ausgabe:
            QTimer(self.zeitgeber, interval=1000, timeout=self._ausgeben).start()

    def _menue(self) -> None:
        menue = QMenu()
        if self.quelle.kalibrierbar:
            kalib = menue.addMenu("")

            def zeigen():
                kalib.setTitle(f"Kalibrierung: {self.quelle.kalibrierung:.0f} dB")

            def aendern(neu):
                self.quelle.kalibrierung = neu
                einstellung_schreiben("Kalibrierung", neu)
                zeigen()

            kalib.addAction("+1 dB (Anzeige höher)", lambda: aendern(self.quelle.kalibrierung + 1))
            kalib.addAction("−1 dB (Anzeige niedriger)", lambda: aendern(self.quelle.kalibrierung - 1))
            kalib.addAction("Zurücksetzen (106 dB)", lambda: aendern(106))
            zeigen()
        if sys.platform.startswith("linux"):
            auto = QAction("Mit Anmeldung starten", menue, checkable=True, checked=_autostart_datei().exists())
            auto.toggled.connect(lambda an: autostart_setzen(an, self.weiter))
            menue.addAction(auto)
        menue.addSeparator()
        menue.addAction("Neu starten", self._neu_starten)
        menue.addAction("Beenden", self._beenden)
        menue.aboutToShow.connect(self.karte.ausblenden)
        self._menue_objekt = menue  # keep a reference
        self.tray.setContextMenu(menue)

    def _geklickt(self, grund) -> None:
        if grund not in (QSystemTrayIcon.Trigger, QSystemTrayIcon.DoubleClick):
            return
        if self.karte.angepinnt and not self.karte.blendet_aus:
            self.karte.ausblenden()
            return
        self.oeffnen()

    def oeffnen(self) -> None:
        m = self.quelle.messwert()
        self.karte.animieren(m)
        geo = self.tray.geometry()
        anker = geo.center() if geo.isValid() and not geo.isEmpty() else QCursor.pos()
        self.karte.zeigen_bei(anker)
        self.karte.angepinnt = True

    def _takt(self) -> None:
        m = self.quelle.messwert()
        self._tray_zeichnen(m)
        if self.karte.isVisible():
            self.karte.animieren(m)
        self.bild += 1
        if self.bild % 30 == 0:
            self.tray.setToolTip(f"{m.skala.titel}: {m.skala.text(m.wert)} · {werte.statustext(m)}"
                                 + (f" ({m.geraet})" if m.geraet else ""))

    def _tray_zeichnen(self, m: werte.Messwert) -> None:
        s = werte.status(m)
        # Not connected: no icon at all; it comes back as soon as the device is there.
        sichtbar = s != werte.AUS
        if self.tray.isVisible() != sichtbar:
            self.tray.setVisible(sichtbar)
            if not sichtbar:
                self.karte.ausblenden()
        k = werte.beat_anteil(m)
        self.tray_beat += (k - self.tray_beat) * (0.9 if k > self.tray_beat else 0.45)
        punkte = _punkte(s, self.tray_beat)
        hell = _taskleiste_hell()
        schluessel = f"{s}|{','.join(str(round(p * 20)) for p in punkte)}|{hell}"
        if schluessel != self.tray_schluessel:
            self.tray_schluessel = schluessel
            self.tray.setIcon(zeichnen.symbol(s, punkte, hell))

    def _ausgeben(self) -> None:
        """--ausgabe: passes the value on, once per second, in the same format the 'extern' source reads."""
        m = self.quelle.messwert()
        if m.verbunden and not math.isnan(m.wert):
            einheit = f" {m.skala.einheit}" if m.skala.einheit else ""
            print(f"{m.wert:.1f}{einheit}", flush=True)

    def _neu_starten(self) -> None:
        subprocess.Popen(_startbefehl(["--restart", *self.weiter]), start_new_session=True)
        self._beenden()

    def _beenden(self) -> None:
        self.quelle.stop()
        self.tray.hide()
        QApplication.quit()


def _bereich(text: str) -> tuple[float, float]:
    lo, hi = text.split(":")
    return float(lo), float(hi)


def argumente(argv: list[str]) -> argparse.Namespace:
    p = argparse.ArgumentParser(prog="kopfhoererpegel", description="Kopfhörerpegel – Pegelanzeige im Tray")
    p.add_argument("--quelle", choices=quellen.ARTEN, default="kopfhoerer",
                   help="kopfhoerer = dB der AirPods/Kopfhörer, mikrofon/ausgang = Pegel in %%, extern = Werte aus "
                        "einem Befehl oder stdin, demo = Fantasiewerte")
    p.add_argument("--geraet", help="Teil des Gerätenamens (Standard bei kopfhoerer: AirPods; sonst Standardgerät)")
    p.add_argument("--befehl", default="-", help="für extern: Befehl, der pro Zeile einen Wert ausgibt (- = stdin)")
    p.add_argument("--bereich", type=_bereich, help="MIN:MAX der Skala, z. B. 0:1023 für Rohwerte")
    p.add_argument("--titel", help="Überschrift der Karte (Standard je nach Quelle)")
    p.add_argument("--einheit", choices=("dB", "%"), default="dB", help="nur für demo")
    p.add_argument("--ausgabe", action="store_true", help="Wert jede Sekunde auf stdout weitergeben")
    p.add_argument("--show", action="store_true", help="Karte beim Start gleich öffnen")
    p.add_argument("--restart", action="store_true", help=argparse.SUPPRESS)
    p.add_argument("--render-karte", metavar="PNG", help="Karte als Bild speichern (Test ohne Tray)")
    p.add_argument("--render-tray", metavar="ORDNER", help="alle Tray-Symbole als PNG speichern")
    return p.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    argv = sys.argv[1:] if argv is None else argv
    args = argumente(argv)
    weiter = [a for a in argv if a not in ("--restart", "--show")]
    sys.excepthook = lambda typ, wert, tb: fehler_schreiben(wert)

    app = QApplication(sys.argv[:1])
    app.setQuitOnLastWindowClosed(False)
    app.setApplicationName("Kopfhörerpegel")

    if args.render_tray:
        from .testbilder import tray_speichern
        tray_speichern(args.render_tray)
        return 0
    if args.render_karte:
        from .testbilder import karte_speichern
        return karte_speichern(args, app)

    # One instance per source (so `--ausgabe | --quelle extern` chains work); after "Neu starten" the new one waits
    # for the old one to let go.
    kennung = "".join(z if z.isalnum() else "_" for z in f"{args.quelle}-{args.geraet or ''}")
    sperre = QLockFile(str(datenordner() / f"instanz-{kennung}.lock"))
    if not sperre.tryLock(10000 if args.restart else 0):
        return 0
    if not QSystemTrayIcon.isSystemTrayAvailable():
        fehler_schreiben(RuntimeError("Kein Infobereich (System Tray) gefunden – Karte wird direkt gezeigt."))
        args.show = True

    fenster = App(args, weiter)
    if args.show:
        QTimer.singleShot(300, fenster.oeffnen)
    code = app.exec()
    sperre.unlock()
    return code
