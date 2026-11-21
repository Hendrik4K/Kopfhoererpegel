"""Made-up levels for looking at the design without audio (same curve as the Windows --demo)."""
from __future__ import annotations

import math
import time

from ..werte import KOPFHOERER_DB, Messwert, Skala, prozent
from .basis import Quelle


class DemoQuelle(Quelle):
    def __init__(self, einheit: str = "dB"):
        super().__init__()
        self.skala: Skala = prozent("Pegel") if einheit == "%" else KOPFHOERER_DB

    def _laufen(self) -> None:
        while self._laeuft:
            t = time.time() % 86400
            dbwert = 78 + 9 * math.sin(t / 6.0) + 3 * math.sin(t * 1.7) + 2 * math.sin(t * 4.3)
            kick = dbwert - 3 + 9 * max(0.0, math.sin(t * math.pi * 2.1)) ** 6  # about two beats per second
            if self.skala.einheit == "%":
                dbwert, kick = (dbwert - 20) / 90 * 100, (kick - 20) / 90 * 100
            self._setzen(Messwert(verbunden=True, geraet="AirPods Pro 3", wert=dbwert, schnell=kick,
                                  lautstaerke=62, skala=self.skala))
            time.sleep(0.05)
