"""Platform-independent level maths, the same as in the Windows app (src/Meter.cs)."""
from __future__ import annotations

import math
from collections import deque

TICK = 0.125          # seconds per block
FENSTER = 8           # 8 blocks = 1-second average


class AWeighting:
    """IEC 61672 A-weighting as six first-order sections (bilinear transform, pre-warped poles), 0 dB at 1 kHz."""

    POLE = (20.598997, 20.598997, 107.65265, 737.86223, 12194.217, 12194.217)

    def __init__(self, rate: int):
        k = 2.0 * rate
        omega = 2 * math.pi * 1000 / rate
        self.b0, self.b1, self.a1 = [], [], []
        gain_1k = 1.0
        for i, pole in enumerate(self.POLE):
            w = k * math.tan(math.pi * min(pole, rate * 0.45) / rate)
            hochpass = i < 4
            b0 = (k if hochpass else w) / (k + w)
            b1 = -b0 if hochpass else b0
            a1 = (w - k) / (k + w)
            self.b0.append(b0); self.b1.append(b1); self.a1.append(a1)
            nr, ni = b0 + b1 * math.cos(omega), -b1 * math.sin(omega)
            dr, di = 1 + a1 * math.cos(omega), -a1 * math.sin(omega)
            gain_1k *= math.sqrt((nr * nr + ni * ni) / (dr * dr + di * di))
        self.gain = 1 / gain_1k
        self.x1 = [0.0] * 6
        self.y1 = [0.0] * 6

    def energie(self, samples) -> float:
        """Sum of squared A-weighted samples (filter state carries over between blocks)."""
        b0, b1, a1, x1, y1 = self.b0, self.b1, self.a1, self.x1, self.y1
        g = self.gain
        summe = 0.0
        for x in samples:
            for i in range(6):
                y = b0[i] * x + b1[i] * x1[i] - a1[i] * y1[i]
                x1[i] = x
                y1[i] = y
                x = y
            x *= g
            summe += x * x
        # Flush denormals after long silence.
        for i in range(6):
            if abs(y1[i]) < 1e-30:
                y1[i] = 0.0
        return summe


def ungewichtet(samples) -> float:
    return sum(x * x for x in samples)


def dbfs(mean_square: float) -> float:
    """10·log10 of the mean square (a full-scale sine is −3 dBFS, as in the Windows app)."""
    return 10 * math.log10(mean_square) if mean_square > 1e-12 else -math.inf


class Pegelfenster:
    """Collects signal energy; every tick gives the 1-second and the last-block level in dBFS.

    Time without samples counts as silence, like on Windows (no packets = nothing playing).
    """

    def __init__(self, rate: int):
        self.rate = rate
        self.ring = deque([0.0] * FENSTER, maxlen=FENSTER)
        self.energie = 0.0

    def hinzu(self, energie: float) -> None:
        self.energie += energie

    def tick(self) -> tuple[float, float]:
        block, self.energie = self.energie, 0.0
        self.ring.append(block)
        sekunde = sum(self.ring) / (self.rate * TICK * FENSTER)
        return dbfs(sekunde), dbfs(block / (self.rate * TICK))


def prozent_aus_dbfs(wert: float, boden: float = -60.0) -> float:
    """Level meter mapping: −60 dBFS (and below) = 0 %, full scale = 100 %."""
    if wert == -math.inf:
        return 0.0
    return max(0.0, min(100.0, (wert - boden) / -boden * 100))


class Mittel:
    """Rolling 1-second mean of values that arrive as numbers (external receivers).

    dB values are averaged as power (like an Leq), percent and raw values arithmetically.
    """

    def __init__(self, energetisch: bool):
        self.energetisch = energetisch
        self.werte: deque[tuple[float, float]] = deque()

    def hinzu(self, zeit: float, wert: float) -> None:
        self.werte.append((zeit, wert))

    def wert(self, jetzt: float, dauer: float = 1.0) -> float:
        while self.werte and self.werte[0][0] < jetzt - dauer:
            self.werte.popleft()
        if not self.werte:
            return math.nan
        if self.energetisch:
            return 10 * math.log10(sum(10 ** (w / 10) for _, w in self.werte) / len(self.werte))
        return sum(w for _, w in self.werte) / len(self.werte)
