"""External receivers: anything that prints levels as text, one value per line (any platform).

    73.5 dB      -> shown as dB (also "dBA", "dB(A)")
    42 %         -> shown as percent
    512          -> raw value, shown as it is on the scale given with --bereich MIN:MAX

The value is shown unchanged; only the 1-second average and the beat are derived from it.
Lines come from a command (--befehl "...") or from standard input (--befehl -).
"""
from __future__ import annotations

import math
import re
import shlex
import subprocess
import sys
import threading
import time

from ..messung import TICK, Mittel
from ..werte import Messwert, Skala, db, prozent, roh
from .basis import Quelle

_ZEILE = re.compile(r"^\s*(-?\d+(?:[.,]\d+)?)\s*(dB\s*\(?A?\)?|%)?\s*$", re.IGNORECASE)


def zeile_lesen(zeile: str) -> tuple[float, str] | None:
    """'73,5 dB' -> (73.5, 'dB'), '42%' -> (42.0, '%'), '512' -> (512.0, ''); anything else -> None."""
    treffer = _ZEILE.match(zeile)
    if not treffer:
        return None
    einheit = treffer.group(2) or ""
    return float(treffer.group(1).replace(",", ".")), ("dB" if einheit.lower().startswith("db") else einheit)


def stellen(zeile: str) -> int:
    """Decimals the receiver writes ('12.50' -> 2), so raw values are shown with the same precision."""
    treffer = _ZEILE.match(zeile)
    zahl = treffer.group(1).replace(",", ".") if treffer else ""
    return len(zahl.split(".")[1]) if "." in zahl else 0


class ExternQuelle(Quelle):
    def __init__(self, befehl: str = "-", bereich: tuple[float, float] | None = None, titel: str | None = None,
                 geraet: str = ""):
        super().__init__()
        self.befehl = befehl
        self.bereich = bereich
        self.titel = titel
        self.geraet = geraet or ("Eingabe" if befehl == "-" else shlex.split(befehl)[0])
        self.skala: Skala | None = None
        self._mittel: Mittel | None = None
        self._letzter = math.nan
        self._letzte_zeit = 0.0
        self._ende = False
        self._datenlock = threading.Lock()

    def _skala_fuer(self, einheit: str, nachkomma: int = 0) -> Skala:
        if einheit == "dB":
            lo, hi = self.bereich or (20, 110)
            return db(self.titel or "Pegel", lo, hi)
        if einheit == "%":
            return prozent(self.titel or "Pegel")
        lo, hi = self.bereich or (0, 100)
        return roh(self.titel or "Pegel", lo, hi, nachkomma)

    def _lesen(self) -> None:
        if self.befehl == "-":
            strom = sys.stdin
            prozess = None
        else:
            prozess = subprocess.Popen(shlex.split(self.befehl), stdout=subprocess.PIPE, text=True, bufsize=1)
            strom = prozess.stdout
        for zeile in strom:
            if not self._laeuft:
                break
            gelesen = zeile_lesen(zeile)
            if gelesen is None:
                continue
            wert, einheit = gelesen
            with self._datenlock:
                if self.skala is None:  # the first value decides the unit
                    self.skala = self._skala_fuer(einheit, stellen(zeile))
                    self._mittel = Mittel(energetisch=einheit == "dB")
                self._mittel.hinzu(time.monotonic(), wert)
                self._letzter = wert
                self._letzte_zeit = time.monotonic()
        self._ende = True
        if prozess:
            prozess.terminate()

    def _laufen(self) -> None:
        threading.Thread(target=self._lesen, name="ExternLesen", daemon=True).start()
        while self._laeuft:
            time.sleep(TICK)
            jetzt = time.monotonic()
            with self._datenlock:
                if self.skala is None:
                    self._setzen(Messwert(verbunden=not self._ende, geraet=self.geraet,
                                          skala=self._skala_fuer("dB" if not self.bereich else "")))
                    continue
                # Without a value for 5 s the receiver counts as gone.
                frisch = jetzt - self._letzte_zeit < 5
                wert = self._mittel.wert(jetzt) if frisch else math.nan
                self._setzen(Messwert(verbunden=frisch or not self._ende, geraet=self.geraet, wert=wert,
                                      schnell=self._letzter if frisch else math.nan, skala=self.skala))
            if self._ende and jetzt - self._letzte_zeit >= 5:
                return
