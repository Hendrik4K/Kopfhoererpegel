"""Tests without Qt and without audio hardware: python3 -m unittest discover -s tests (from the linux folder)."""
import math
import sys
import time
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from pegel import werte  # noqa: E402
from pegel.messung import AWeighting, Mittel, Pegelfenster, dbfs, prozent_aus_dbfs  # noqa: E402
from pegel.quellen import linux_pulse  # noqa: E402
from pegel.quellen.extern import ExternQuelle, zeile_lesen  # noqa: E402


def sinus(freq, rate, sekunden, amplitude=1.0):
    return [amplitude * math.sin(2 * math.pi * freq * i / rate) for i in range(int(rate * sekunden))]


class Messung(unittest.TestCase):
    def a_gewicht_db(self, freq, rate=48000):
        f = AWeighting(rate)
        f.energie(sinus(freq, rate, 0.5))            # settle
        e = f.energie(sinus(freq, rate, 0.5))
        return dbfs(e / (rate * 0.5)) - dbfs(0.5)     # relative to the unweighted sine

    def test_a_bewertung_normwerte(self):
        # IEC 61672: 1 kHz 0 dB, 100 Hz −19.1 dB, 4 kHz +1.0 dB
        self.assertAlmostEqual(self.a_gewicht_db(1000), 0.0, delta=0.1)
        self.assertAlmostEqual(self.a_gewicht_db(100), -19.1, delta=0.3)
        self.assertAlmostEqual(self.a_gewicht_db(4000), 1.0, delta=0.3)

    def test_pegelfenster_sinus_und_stille(self):
        rate = 16000
        fenster = Pegelfenster(rate)
        for _ in range(8):
            fenster.hinzu(sum(x * x for x in sinus(1000, rate, 0.125)))
            sekunde, block = fenster.tick()
        self.assertAlmostEqual(sekunde, -3.01, delta=0.05)   # full-scale sine
        self.assertAlmostEqual(block, -3.01, delta=0.05)
        for _ in range(8):
            sekunde, block = fenster.tick()                    # no samples = silence
        self.assertEqual(sekunde, -math.inf)

    def test_prozent(self):
        self.assertEqual(prozent_aus_dbfs(-math.inf), 0)
        self.assertEqual(prozent_aus_dbfs(0), 100)
        self.assertAlmostEqual(prozent_aus_dbfs(-30), 50)

    def test_mittel_db_energetisch(self):
        m = Mittel(energetisch=True)
        m.hinzu(0.0, 70)
        m.hinzu(0.1, 80)
        self.assertAlmostEqual(m.wert(0.2), 77.4, delta=0.1)
        self.assertTrue(math.isnan(m.wert(5)))


class Werte(unittest.TestCase):
    def test_kopfhoerer_skala_wie_windows(self):
        s = werte.KOPFHOERER_DB
        self.assertEqual(s.schritt, 5)
        self.assertEqual(s.marke, 12)       # 12 segments below 80, the 13th is the tall one
        self.assertEqual(s.text(73.4), "73 dB")

    def test_status_und_einheiten(self):
        m = werte.Messwert(verbunden=True, wert=85, skala=werte.KOPFHOERER_DB)
        self.assertEqual(werte.status(m), werte.LAUT)
        m.wert = 91
        self.assertEqual(werte.status(m), werte.SEHR_LAUT)
        p = werte.Messwert(verbunden=True, wert=42, skala=werte.prozent("Mikrofonpegel"))
        self.assertEqual(werte.status(p), werte.OK)
        self.assertEqual(p.skala.text(42), "42 %")
        r = werte.roh("Pegel", 0, 1023)
        self.assertEqual(r.text(512), "512")
        self.assertAlmostEqual(r.laut, 818.4)

    def test_beat_gleich_fuer_alle_einheiten(self):
        d = werte.Messwert(verbunden=True, wert=70, schnell=74, skala=werte.KOPFHOERER_DB)
        p = werte.Messwert(verbunden=True, wert=50, schnell=50 + 4 / 90 * 100, skala=werte.prozent("x"))
        self.assertAlmostEqual(werte.beat_anteil(d), 1.0)
        self.assertAlmostEqual(werte.beat_anteil(p), 1.0)
        self.assertEqual(werte.beat_anteil(werte.Messwert()), 0.0)


SINK = {
    "name": "bluez_output.AA_BB_CC_DD_EE_FF.1", "description": "AirPods Pro 3 von Max - Find My", "mute": False,
    "volume": {"front-left": {"value": 42000, "value_percent": "64%", "db": "-11.60 dB"},
               "front-right": {"value": 42000, "value_percent": "64%", "db": "-11.60 dB"}},
    "monitor_source": "bluez_output.AA_BB_CC_DD_EE_FF.1.monitor",
    "flags": ["HARDWARE", "HW_VOLUME_CTRL", "DECIBEL_VOLUME", "LATENCY"],
}
INTERN = {"name": "alsa_output.pci-0000_00_1f.3.analog-stereo", "description": "Built-in Audio Analog Stereo",
          "mute": True, "volume": {"mono": {"value_percent": "0%", "db": "-inf dB"}}, "flags": ["HARDWARE"]}


class LinuxPulse(unittest.TestCase):
    def test_geraet_aus_json(self):
        g = linux_pulse.geraet_aus_json(SINK, ausgang=True)
        self.assertEqual(g.aufnahme_name, "bluez_output.AA_BB_CC_DD_EE_FF.1.monitor")
        self.assertAlmostEqual(g.lautstaerke_db, -11.6)
        self.assertEqual(g.lautstaerke_prozent, 64)
        self.assertTrue(g.hw_lautstaerke)
        stumm = linux_pulse.geraet_aus_json(INTERN, ausgang=True)
        self.assertEqual(stumm.lautstaerke_db, -math.inf)
        self.assertTrue(stumm.stumm)
        self.assertEqual(stumm.aufnahme_name, INTERN["name"] + ".monitor")
        mic = linux_pulse.geraet_aus_json({"name": "alsa_input.usb-mic", "description": "USB Mic"}, ausgang=False)
        self.assertEqual(mic.aufnahme_name, "alsa_input.usb-mic")
        self.assertIsNone(mic.lautstaerke_db)

    def test_waehlen(self):
        geraete = [linux_pulse.geraet_aus_json(INTERN, True), linux_pulse.geraet_aus_json(SINK, True)]
        self.assertEqual(linux_pulse.waehlen(geraete, "airpods", None).name, SINK["name"])
        self.assertIsNone(linux_pulse.waehlen(geraete, "beats", None))
        self.assertEqual(linux_pulse.waehlen(geraete, "", INTERN["name"]).name, INTERN["name"])

    def test_kurzname(self):
        self.assertEqual(linux_pulse._kurzname("AirPods Pro 3 von Max - Find My"), "AirPods Pro 3")

    def test_db_wert_mit_hardwarelautstaerke(self):
        q = linux_pulse.KopfhoererQuelle.__new__(linux_pulse.KopfhoererQuelle)
        q.kalibrierung, q._addieren, q._pipewire = 106, None, False
        m = q._messwert(linux_pulse.geraet_aus_json(SINK, True), -20.0, -16.0)
        self.assertAlmostEqual(m.wert, 106 - 20 - 11.6)     # hardware volume is added
        self.assertAlmostEqual(m.schnell, 106 - 16 - 11.6)
        self.assertEqual(m.geraet, "AirPods Pro 3")


class Extern(unittest.TestCase):
    def test_zeilen(self):
        self.assertEqual(zeile_lesen("73,5 dB"), (73.5, "dB"))
        self.assertEqual(zeile_lesen("70 dB(A)"), (70.0, "dB"))
        self.assertEqual(zeile_lesen("42%"), (42.0, "%"))
        self.assertEqual(zeile_lesen("512"), (512.0, ""))
        self.assertIsNone(zeile_lesen("hallo"))

    def test_befehl_mit_rohwerten(self):
        befehl = f'"{sys.executable}" -c "import time\nfor v in (500, 520, 540):\n print(v, flush=True); time.sleep(0.1)\ntime.sleep(2)"'
        q = ExternQuelle(befehl, bereich=(0, 1023))
        q.start()
        time.sleep(0.9)
        m = q.messwert()
        q.stop()
        self.assertTrue(m.verbunden)
        self.assertEqual(m.skala.einheit, "")
        self.assertEqual(m.skala.maximum, 1023)
        self.assertAlmostEqual(m.wert, 520, delta=0.1)   # shown as delivered (1 s mean), not converted
        self.assertEqual(m.schnell, 540)
        self.assertEqual(m.skala.text(519.6), "520")       # integers in, integers out
        self.assertEqual(m.skala.beschriftung(m.skala.laut), "818")


if __name__ == "__main__":
    unittest.main()
