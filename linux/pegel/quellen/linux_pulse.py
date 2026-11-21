"""Linux backend: PulseAudio or PipeWire (through pipewire-pulse), via the standard command-line tools.

Everything Linux-specific lives in this file: finding devices (`pactl -f json`), reading their volume and
recording what goes to them (`pw-record` on PipeWire, `parec` otherwise). No extra Python packages needed.

Two sources:
  KopfhoererQuelle - output to the AirPods (or other matching headphones) as calibrated dB(A), like on Windows:
                     A-weighted 1-second level of the sink monitor + sink volume in dB + calibration.
  GeraetQuelle     - any microphone or output as a level in percent (−60 dBFS = 0 %, full scale = 100 %).
"""
from __future__ import annotations

import array
import json
import math
import os
import shutil
import subprocess
import threading
import time
from dataclasses import dataclass

from ..messung import TICK, AWeighting, Pegelfenster, prozent_aus_dbfs, ungewichtet
from ..werte import KOPFHOERER_DB, Messwert, Skala, prozent
from .basis import Quelle

RATE = 16000  # mono 16 kHz is plenty for a level meter and cheap in pure Python


# ---- device list ---------------------------------------------------------------------------------------------------

def _pactl(*args: str) -> str:
    return subprocess.run(["pactl", *args], capture_output=True, text=True, timeout=5, check=True).stdout


def _pactl_json(*args: str):
    return json.loads(_pactl("-f", "json", *args) or "[]")


def ist_pipewire() -> bool:
    try:
        return "PipeWire" in _pactl("info")
    except (OSError, subprocess.SubprocessError):
        return False


@dataclass
class Geraet:
    name: str
    beschreibung: str
    ausgang: bool               # True = sink (what we hear), False = microphone/input
    aufnahme_name: str          # what to record: the sink's monitor or the source itself
    lautstaerke_db: float | None
    lautstaerke_prozent: float | None
    stumm: bool
    hw_lautstaerke: bool        # volume is applied in the device (e.g. Bluetooth absolute volume)


def _db_wert(text: str) -> float | None:
    try:
        wert = text.replace("dB", "").strip()
        return -math.inf if wert in ("-inf", "-∞") else float(wert)
    except (AttributeError, ValueError):
        return None


def geraet_aus_json(eintrag: dict, ausgang: bool) -> Geraet:
    """One entry of `pactl -f json list sinks|sources` (separate so it can be tested without Linux)."""
    kanaele = (eintrag.get("volume") or {}).values()
    dbs = [d for d in (_db_wert(k.get("db", "")) for k in kanaele) if d is not None]
    prozente = []
    for k in kanaele:
        try:
            prozente.append(float(str(k.get("value_percent", "")).rstrip("%")))
        except ValueError:
            pass
    name = eintrag.get("name", "")
    return Geraet(
        name=name,
        beschreibung=eintrag.get("description", name),
        ausgang=ausgang,
        aufnahme_name=(eintrag.get("monitor_source") or name + ".monitor") if ausgang else name,
        lautstaerke_db=(sum(dbs) / len(dbs)) if dbs and all(d != -math.inf for d in dbs) else (-math.inf if dbs else None),
        lautstaerke_prozent=(sum(prozente) / len(prozente)) if prozente else None,
        stumm=bool(eintrag.get("mute", False)),
        hw_lautstaerke="HW_VOLUME_CTRL" in (eintrag.get("flags") or []),
    )


def ausgaenge() -> list[Geraet]:
    return [geraet_aus_json(e, True) for e in _pactl_json("list", "sinks")]


def eingaenge() -> list[Geraet]:
    liste = []
    for e in _pactl_json("list", "sources"):
        if e.get("name", "").endswith(".monitor") or e.get("monitor_of_sink") not in (None, "n/a", ""):
            continue  # monitors are reached through their sink
        liste.append(geraet_aus_json(e, False))
    return liste


def waehlen(geraete: list[Geraet], muster: str, standard: str | None) -> Geraet | None:
    """First device whose name or description contains `muster` (Bluetooth first); empty muster = default device."""
    if not muster:
        return next((g for g in geraete if g.name == standard), geraete[0] if geraete else None)
    treffer = [g for g in geraete if muster.lower() in (g.beschreibung + " " + g.name).lower()]
    treffer.sort(key=lambda g: not g.name.startswith(("bluez_output", "bluez_input")))
    return treffer[0] if treffer else None


# ---- recording -----------------------------------------------------------------------------------------------------

class Aufnahme:
    """Records one device as 16 kHz mono float and collects its energy for the level window."""

    def __init__(self, geraet: Geraet, gewichtet: bool):
        self.geraet = geraet
        self.fenster = Pegelfenster(RATE)
        self.filter = AWeighting(RATE) if gewichtet else None
        self._lock = threading.Lock()
        self.prozess = subprocess.Popen(self._befehl(geraet), stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
        threading.Thread(target=self._lesen, name="Aufnahme", daemon=True).start()

    @staticmethod
    def _befehl(g: Geraet) -> list[str]:
        if ist_pipewire() and shutil.which("pw-record"):
            # Passive: listening must not keep the headphones streaming (or wake a suspended sink).
            props = "{ node.passive = true" + (" stream.capture.sink = true" if g.ausgang else "") + " }"
            return ["pw-record", "--target", g.name, "-P", props, "--format", "f32", "--rate", str(RATE),
                    "--channels", "1", "--media-category", "Capture", "-"]
        if shutil.which("parec"):
            return ["parec", "--raw", "-d", g.aufnahme_name, "--format=float32le", f"--rate={RATE}",
                    "--channels=1", "--latency-msec=50", "--client-name=Kopfhoererpegel"]
        raise RuntimeError("Weder pw-record noch parec gefunden (Pakete pipewire-bin oder pulseaudio-utils).")

    def _lesen(self) -> None:
        rest = b""
        fd = self.prozess.stdout.fileno()
        while True:
            daten = os.read(fd, 8192)
            if not daten:
                return
            daten = rest + daten
            n = len(daten) // 4 * 4
            rest = daten[n:]
            samples = array.array("f")
            samples.frombytes(daten[:n])
            energie = self.filter.energie(samples) if self.filter else ungewichtet(samples)
            with self._lock:
                self.fenster.hinzu(energie)

    def tick(self) -> tuple[float, float]:
        with self._lock:
            return self.fenster.tick()

    def laeuft(self) -> bool:
        return self.prozess.poll() is None

    def beenden(self) -> None:
        try:
            self.prozess.terminate()
            self.prozess.wait(timeout=2)
        except (OSError, subprocess.SubprocessError):
            self.prozess.kill()


# ---- sources -------------------------------------------------------------------------------------------------------

class _PulseQuelle(Quelle):
    ausgang = True
    gewichtet = True
    skala: Skala = KOPFHOERER_DB

    def __init__(self, muster: str):
        super().__init__()
        self.muster = muster
        self._setzen(Messwert(skala=self.skala))

    def _standard(self) -> str | None:
        try:
            return _pactl("get-default-sink" if self.ausgang else "get-default-source").strip()
        except (OSError, subprocess.SubprocessError):
            return None

    def _laufen(self) -> None:
        aufnahme: Aufnahme | None = None
        geraet: Geraet | None = None
        letzte_suche = 0.0
        naechster = time.monotonic()
        try:
            while self._laeuft:
                if time.monotonic() - letzte_suche >= 0.5:  # also refreshes volume and mute
                    letzte_suche = time.monotonic()
                    geraete = ausgaenge() if self.ausgang else eingaenge()
                    geraet = waehlen(geraete, self.muster, self._standard() if not self.muster else None)
                    if aufnahme and (geraet is None or geraet.name != aufnahme.geraet.name or not aufnahme.laeuft()):
                        aufnahme.beenden()
                        aufnahme = None
                    if geraet and aufnahme is None:
                        aufnahme = Aufnahme(geraet, self.gewichtet)

                naechster += TICK
                warten = naechster - time.monotonic()
                if warten > 0:
                    time.sleep(warten)
                else:
                    naechster = time.monotonic()
                if aufnahme and geraet:
                    sekunde, block = aufnahme.tick()
                    self._setzen(self._messwert(geraet, sekunde, block))
                else:
                    self._setzen(Messwert(skala=self.skala))
        finally:
            if aufnahme:
                aufnahme.beenden()

    def _messwert(self, g: Geraet, sekunde: float, block: float) -> Messwert:
        raise NotImplementedError


class KopfhoererQuelle(_PulseQuelle):
    """Calibrated dB(A) for headphones, like the Windows app."""

    kalibrierbar = True

    def __init__(self, muster: str = "AirPods", kalibrierung: float = 106, lautstaerke_addieren: bool | None = None):
        super().__init__(muster)
        self.kalibrierung = kalibrierung
        # PipeWire records the sink monitor before the volume (monitor.channel-volumes = false), classic PulseAudio
        # after a software volume. Hardware volume (Bluetooth absolute volume) always comes after the monitor.
        self._addieren = lautstaerke_addieren
        self._pipewire = ist_pipewire()

    def _messwert(self, g: Geraet, sekunde: float, block: float) -> Messwert:
        m = Messwert(verbunden=True, geraet=_kurzname(g.beschreibung), stumm=g.stumm,
                     lautstaerke=g.lautstaerke_prozent, skala=self.skala)
        if g.stumm or g.lautstaerke_db == -math.inf:
            return m
        addieren = self._addieren if self._addieren is not None else (self._pipewire or g.hw_lautstaerke)
        plus = (g.lautstaerke_db or 0.0) if addieren else 0.0
        wert = sekunde + plus + self.kalibrierung
        schnell = block + plus + self.kalibrierung
        m.wert = wert if wert > 20 else math.nan  # below that: silence / noise floor
        m.schnell = schnell if schnell > 20 else math.nan
        return m


class GeraetQuelle(_PulseQuelle):
    """Any microphone or output as a level in percent (no calibration, no dB claim)."""

    gewichtet = False

    def __init__(self, muster: str = "", ausgang: bool = False, titel: str | None = None):
        self.ausgang = ausgang
        self.skala = prozent(titel or ("Ausgangspegel" if ausgang else "Mikrofonpegel"))
        super().__init__(muster)

    def _messwert(self, g: Geraet, sekunde: float, block: float) -> Messwert:
        m = Messwert(verbunden=True, geraet=_kurzname(g.beschreibung), stumm=g.stumm, skala=self.skala,
                     lautstaerke=g.lautstaerke_prozent if self.ausgang else None)
        wert, schnell = prozent_aus_dbfs(sekunde), prozent_aus_dbfs(block)
        m.wert = wert if wert > 0.5 else math.nan
        m.schnell = schnell
        return m


def _kurzname(beschreibung: str) -> str:
    """'AirPods Pro 3 von Max – Find My' -> 'AirPods Pro 3' (same rule as on Windows)."""
    for trenner in (" von ", " - ", " (", " – "):
        i = beschreibung.find(trenner)
        if i > 0:
            beschreibung = beschreibung[:i]
    return beschreibung.strip()
