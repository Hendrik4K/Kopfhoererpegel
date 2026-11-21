"""Level sources. The platform-specific audio code is only imported for the platform that needs it.

    kopfhoerer  calibrated dB(A) of headphones (default: AirPods)   - Linux (PulseAudio/PipeWire)
    mikrofon    microphone level in percent                          - Linux
    ausgang     output level in percent                              - Linux
    extern      values from a command or stdin (dB, % or raw)        - any platform
    demo        made-up levels                                       - any platform

Windows has its own native app (../src, WASAPI) and is not routed through here; a macOS backend would be one more
module next to linux_pulse.py.
"""
from __future__ import annotations

import sys

from .basis import Quelle

ARTEN = ("kopfhoerer", "mikrofon", "ausgang", "extern", "demo")


def erstellen(art: str, geraet: str | None = None, kalibrierung: float = 106, befehl: str = "-",
              bereich: tuple[float, float] | None = None, titel: str | None = None,
              einheit: str = "dB") -> Quelle:
    if art == "demo":
        from .demo import DemoQuelle
        return DemoQuelle(einheit)
    if art == "extern":
        from .extern import ExternQuelle
        return ExternQuelle(befehl, bereich, titel, geraet or "")
    if not sys.platform.startswith("linux"):
        raise SystemExit(f"Die Quelle '{art}' gibt es in dieser Version nur unter Linux. Unter Windows läuft die "
                         "eigene App (Kopfhoererpegel.exe); hier gehen 'extern' und 'demo'.")
    from . import linux_pulse
    if art == "kopfhoerer":
        return linux_pulse.KopfhoererQuelle(geraet if geraet is not None else "AirPods", kalibrierung)
    if art in ("mikrofon", "ausgang"):
        return linux_pulse.GeraetQuelle(geraet or "", ausgang=art == "ausgang", titel=titel)
    raise SystemExit(f"Unbekannte Quelle '{art}'. Möglich: {', '.join(ARTEN)}")


__all__ = ["ARTEN", "Quelle", "erstellen"]
