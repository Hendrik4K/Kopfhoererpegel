"""What a level source delivers and how the card scales it.

A source reports values in its own unit and the card shows them unchanged: calibrated dB (AirPods),
a level in percent (microphones, other devices) or a raw number from an external receiver.
"""
from __future__ import annotations

import math
from dataclasses import dataclass, field


@dataclass(frozen=True)
class Skala:
    einheit: str          # "dB", "%" or "" (raw value)
    minimum: float
    maximum: float
    laut: float           # from here the status is "Laut" (yellow); the taller segment sits here
    sehr_laut: float      # from here "Sehr laut" (red)
    titel: str
    segmente: int = 18
    stellen: int = 0      # decimals of raw values, as many as the receiver delivers

    @property
    def schritt(self) -> float:
        return (self.maximum - self.minimum) / self.segmente

    @property
    def marke(self) -> int:
        """Index of the taller segment (the one that starts at the "Laut" threshold)."""
        return max(0, min(self.segmente - 1, int((self.laut - self.minimum) / self.schritt + 1e-9)))

    def text(self, wert: float) -> str:
        if math.isnan(wert):
            return "–" + (" " + self.einheit if self.einheit else "")
        if self.einheit == "dB":
            return f"{round(wert):.0f} dB"
        if self.einheit == "%":
            return f"{round(wert):.0f} %"
        return f"{wert:.{self.stellen}f}"

    def beschriftung(self, wert: float) -> str:
        return f"{wert:.0f}" if abs(self.maximum - self.minimum) >= 20 else f"{wert:.3g}"


# The iPhone-like headphone scale of the Windows app: 18 segments of 5 dB, "Laut" from 80, "Sehr laut" from 90.
KOPFHOERER_DB = Skala("dB", 20, 110, 80, 90, "Kopfhörerpegel")


def prozent(titel: str) -> Skala:
    return Skala("%", 0, 100, 80, 95, titel)


def db(titel: str, minimum: float = 20, maximum: float = 110) -> Skala:
    return Skala("dB", minimum, maximum, 80, 90, titel)


def roh(titel: str, minimum: float, maximum: float, stellen: int = 0) -> Skala:
    spanne = maximum - minimum
    return Skala("", minimum, maximum, minimum + 0.8 * spanne, minimum + 0.9 * spanne, titel, stellen=stellen)


@dataclass
class Messwert:
    verbunden: bool = False
    geraet: str = ""
    wert: float = math.nan       # last second, in the unit of the scale; NaN = nothing playing
    schnell: float = math.nan    # last ~125 ms, same unit (drives the beat animation)
    lautstaerke: float | None = None  # output volume in percent, if the source knows it
    stumm: bool = False
    skala: Skala = field(default=KOPFHOERER_DB)


# Status names match the Windows app.
AUS, STILL, OK, LAUT, SEHR_LAUT = "aus", "still", "ok", "laut", "sehr_laut"


def status(m: Messwert) -> str:
    if not m.verbunden:
        return AUS
    if math.isnan(m.wert):
        return STILL
    if m.wert >= m.skala.sehr_laut:
        return SEHR_LAUT
    return LAUT if m.wert >= m.skala.laut else OK


def statustext(m: Messwert) -> str:
    s = status(m)
    return {AUS: "Nicht verbunden", STILL: "Stumm" if m.stumm else "Keine Wiedergabe",
            OK: "OK", LAUT: "Laut", SEHR_LAUT: "Sehr laut"}[s]


def beat_anteil(m: Messwert) -> float:
    """0..1: how far the last ~125 ms stick out above the 1-second level (0.5 = average).

    Normalised to the scale, so it behaves the same for dB, percent and raw values
    (8 dB on the 90 dB headphone scale = one half step).
    """
    if status(m) in (AUS, STILL) or math.isnan(m.schnell):
        return 0.0
    spanne = m.skala.maximum - m.skala.minimum
    abweichung = (m.schnell - m.wert) / (spanne * 8 / 90)
    return max(0.0, min(1.0, 0.5 + abweichung))
