"""The interface every level source implements. The UI only ever talks to this."""
from __future__ import annotations

import threading
import time

from ..werte import Messwert


class Quelle:
    """A level source. Subclasses fill `aktuell` from their own thread; `messwert()` is safe from the UI thread."""

    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._aktuell = Messwert()
        self._laeuft = False
        self._thread: threading.Thread | None = None

    # -- for the UI ------------------------------------------------------------------------------------------------
    def start(self) -> None:
        self._laeuft = True
        self._thread = threading.Thread(target=self._sicher_laufen, name=type(self).__name__, daemon=True)
        self._thread.start()

    def stop(self) -> None:
        self._laeuft = False

    def messwert(self) -> Messwert:
        with self._lock:
            return self._aktuell

    # Calibration only means something for calibrated dB sources; others ignore it.
    kalibrierbar = False
    kalibrierung = 0.0

    # -- for subclasses --------------------------------------------------------------------------------------------
    def _setzen(self, m: Messwert) -> None:
        with self._lock:
            self._aktuell = m

    def _sicher_laufen(self) -> None:
        # A crashing backend must not take the tray down; it reports "nicht verbunden" and retries.
        while self._laeuft:
            try:
                self._laufen()
            except Exception as fehler:  # noqa: BLE001 - logged, then retried
                from ..protokoll import fehler_schreiben
                fehler_schreiben(fehler)
                self._setzen(Messwert(skala=self._aktuell.skala))
                time.sleep(3)

    def _laufen(self) -> None:
        raise NotImplementedError
