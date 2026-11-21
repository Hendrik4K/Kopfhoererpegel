"""The frosted card ("Liquid Bubble"), drawn like the WPF card of the Windows app (src/Popup.cs).

Layout and behaviour are the same: title, status icon + text, value right, 18 segments with a taller one at the
"Laut" mark, fixed labels underneath. The bar fills segment by segment with the 1-second value; only the two front
segments pulse with the beat in fixed steps (4 and 5 levels).
"""
from __future__ import annotations

import math

from PySide6.QtCore import QEasingCurve, QPoint, QPropertyAnimation, QRect, QRectF, Qt
from PySide6.QtGui import QColor, QFont, QGuiApplication, QImage, QPainter, QPainterPath, QPen
from PySide6.QtWidgets import QWidget

from .. import werte
from . import zeichnen

RAND_X, OBEN, UNTEN = 28, 18, 16
SEG_BREITE, SEG_ABSTAND, SEG_HOEHE, MARKE_HOEHE = 10, 8, 26, 34
ECKE = 49                      # the user's corner radius from the Windows Feintuning round
STUFEN_VORNE, STUFEN_DAHINTER = 4, 5
UNBELEUCHTET = QColor(255, 255, 255, 0x24)
TOENUNG = QColor(0x1C, 0x1C, 0x1E, 0xB4)
TOENUNG_OHNE_BILD = QColor(0x1C, 0x1C, 0x1E, 0xEC)  # Wayland: no picture of the background available
UNSCHAERFE = 36


def _schrift(px: int, fett: bool = True) -> QFont:
    f = QFont()
    f.setFamilies(["Segoe UI Variable Display", "Inter", "Cantarell", "Noto Sans", "DejaVu Sans"])
    f.setPixelSize(px)
    f.setWeight(QFont.DemiBold if fett else QFont.Normal)
    return f


def _stufe(v: float, stufen: int) -> float:
    return round(v * (stufen - 1)) / (stufen - 1)


class Karte(QWidget):
    def __init__(self, skala: werte.Skala):
        super().__init__(None, Qt.Tool | Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint
                         | Qt.WindowDoesNotAcceptFocus | Qt.WindowTransparentForInput)
        self.setAttribute(Qt.WA_TranslucentBackground)
        self.setAttribute(Qt.WA_ShowWithoutActivating)
        self.balken_breite = 18 * SEG_BREITE + 17 * SEG_ABSTAND
        self.setFixedSize(2 * RAND_X + self.balken_breite, OBEN + 20 + 6 + 28 + 12 + MARKE_HOEHE + 6 + 18 + UNTEN)
        self.messwert = werte.Messwert(skala=skala)
        self.hintergrund: QImage | None = None
        self.angepinnt = False
        self.blendet_aus = False
        # animation state
        self.gezeigt = skala.minimum - skala.schritt * 2
        self.vorne = -1
        self.beat = 0.5
        self.an = [0.0] * 18
        self._ein = QPropertyAnimation(self, b"windowOpacity", self)
        self._ein.finished.connect(self._ausblenden_fertig)

    # ---- data ---------------------------------------------------------------------------------------------------
    def zeigen_mit(self, m: werte.Messwert) -> None:
        self.messwert = m
        self.update()

    def animieren(self, m: werte.Messwert) -> None:
        """~30x per second: calm fill with hysteresis, stepped beat pulse on the two front segments."""
        self.messwert = m
        skala = m.skala
        s = werte.status(m)
        spielt = s not in (werte.AUS, werte.STILL)
        ziel = m.wert if spielt else skala.minimum - skala.schritt * 2
        self.gezeigt += (ziel - self.gezeigt) * (0.25 if ziel > self.gezeigt else 0.10)

        if not spielt and self.gezeigt < skala.minimum:
            self.vorne = -1
        else:
            puffer = skala.schritt * 0.2  # 1 dB on the 5 dB segments
            self.vorne = max(self.vorne, 0)
            while self.vorne < skala.segmente - 1 and self.gezeigt > skala.minimum + (self.vorne + 1) * skala.schritt + puffer:
                self.vorne += 1
            while self.vorne > 0 and self.gezeigt < skala.minimum + self.vorne * skala.schritt - puffer:
                self.vorne -= 1

        k = werte.beat_anteil(m)
        self.beat += (k - self.beat) * (0.9 if k > self.beat else 0.45)
        for i in range(skala.segmente):
            self.an[i] += ((1.0 if i <= self.vorne else 0.0) - self.an[i]) * 0.3
        self.update()

    # ---- showing ------------------------------------------------------------------------------------------------
    def zeigen_bei(self, anker: QPoint) -> None:
        """Opens near the tray icon / click position, above a bottom panel or below a top panel."""
        frisch = not self.isVisible() or self.windowOpacity() < 0.05
        bildschirm = QGuiApplication.screenAt(anker) or QGuiApplication.primaryScreen()
        flaeche = bildschirm.availableGeometry()
        x = max(flaeche.left() + 8, min(flaeche.right() - self.width() - 8, anker.x() - self.width() // 2))
        unten = anker.y() > flaeche.center().y()
        y = flaeche.bottom() - self.height() - 12 if unten else flaeche.top() + 12
        self.move(x, y)
        self.blendet_aus = False
        if frisch:
            self.hintergrund = self._hintergrund_aufnehmen(bildschirm, QRect(x, y, self.width(), self.height()))
            self.setWindowOpacity(0.0)
            self.show()
        self._ein.stop()
        self._ein.setDuration(180)
        self._ein.setStartValue(self.windowOpacity())
        self._ein.setEndValue(1.0)
        self._ein.setEasingCurve(QEasingCurve.OutCubic)
        self._ein.start()

    def ausblenden(self) -> None:
        if not self.isVisible() or self.blendet_aus:
            return
        self.blendet_aus = True
        self.angepinnt = False
        self._ein.stop()
        self._ein.setDuration(450)  # slow, never abrupt
        self._ein.setStartValue(self.windowOpacity())
        self._ein.setEndValue(0.0)
        self._ein.setEasingCurve(QEasingCurve.InCubic)
        self._ein.start()

    def _ausblenden_fertig(self) -> None:
        if self.blendet_aus:
            self.hide()
            self.blendet_aus = False

    def _hintergrund_aufnehmen(self, bildschirm, rect: QRect) -> QImage | None:
        """Picture of what lies behind the card, blurred. Taken while the card is invisible (X11, Windows);
        Wayland does not allow it, then the card gets a denser tint instead."""
        rand = UNSCHAERFE
        try:
            bild = bildschirm.grabWindow(0, rect.x() - rand, rect.y() - rand,
                                         rect.width() + 2 * rand, rect.height() + 2 * rand).toImage()
        except Exception:  # noqa: BLE001 - no screen access is fine, the tint covers it
            return None
        if bild.isNull() or bild.width() < 4 or all(QColor(bild.pixel(x, y)).value() == 0
                                                    for x in range(0, bild.width(), 37) for y in range(0, bild.height(), 29)):
            return None
        return weichzeichnen(bild)

    # ---- drawing ------------------------------------------------------------------------------------------------
    def paintEvent(self, _event) -> None:
        g = QPainter(self)
        g.setRenderHint(QPainter.Antialiasing)
        g.setRenderHint(QPainter.TextAntialiasing)
        w, h = self.width(), self.height()
        ecke = min(ECKE, h / 2)
        form = QPainterPath()
        form.addRoundedRect(QRectF(0.5, 0.5, w - 1, h - 1), ecke, ecke)
        g.setClipPath(form)
        if self.hintergrund is not None:
            g.drawImage(QRectF(-UNSCHAERFE, -UNSCHAERFE, w + 2 * UNSCHAERFE, h + 2 * UNSCHAERFE), self.hintergrund)
            g.fillRect(self.rect(), TOENUNG)
        else:
            g.fillRect(self.rect(), TOENUNG_OHNE_BILD)
        g.setClipping(False)
        g.setPen(QPen(QColor(255, 255, 255, 0x38), 1))
        g.setBrush(Qt.NoBrush)
        g.drawPath(form)
        self._inhalt(g)
        g.end()

    def _inhalt(self, g: QPainter) -> None:
        m = self.messwert
        skala = m.skala
        s = werte.status(m)
        c = zeichnen.farbe(s)
        x0, y = RAND_X, OBEN

        g.setFont(_schrift(15))
        g.setPen(zeichnen.GEDAEMPFT)
        g.drawText(QRectF(x0, y, self.balken_breite, 20), Qt.AlignLeft | Qt.AlignVCenter, skala.titel)
        y += 20 + 6

        zeichnen.statuszeichen(g, x0, y + 3, s)
        g.setFont(_schrift(21))
        g.setPen(Qt.white)
        g.drawText(QRectF(x0 + 31, y, 220, 28), Qt.AlignLeft | Qt.AlignVCenter, werte.statustext(m))
        g.setPen(c if s in (werte.LAUT, werte.SEHR_LAUT) else QColor(Qt.white))
        wert = m.wert if s not in (werte.AUS, werte.STILL) else math.nan
        g.drawText(QRectF(x0, y, self.balken_breite, 28), Qt.AlignRight | Qt.AlignVCenter, skala.text(wert))
        y += 28 + 12

        grund = zeichnen.farbe(s if s not in (werte.AUS, werte.STILL) else werte.OK)
        g.setPen(Qt.NoPen)
        for i in range(skala.segmente):
            hoehe = MARKE_HOEHE if i == skala.marke else SEG_HOEHE
            if i == self.vorne:
                staerke = 0.30 + 0.70 * _stufe(self.beat, STUFEN_VORNE)
            elif i == self.vorne - 1:
                staerke = 0.72 + 0.28 * _stufe(self.beat, STUFEN_DAHINTER)
            else:
                staerke = 1.0
            g.setBrush(zeichnen.mischen(UNBELEUCHTET, zeichnen.saettigung(grund, staerke), self.an[i]))
            g.drawRoundedRect(QRectF(x0 + i * (SEG_BREITE + SEG_ABSTAND), y + (MARKE_HOEHE - hoehe) / 2,
                                     SEG_BREITE, hoehe), 2, 2)
        y += MARKE_HOEHE + 6

        # Fixed labels: minimum, the "Laut" mark, maximum
        g.setFont(_schrift(13, fett=False))
        g.setPen(zeichnen.GEDAEMPFT)
        mitte = x0 + skala.marke * (SEG_BREITE + SEG_ABSTAND) + SEG_BREITE / 2
        g.drawText(QRectF(x0, y, 60, 18), Qt.AlignLeft | Qt.AlignVCenter, skala.beschriftung(skala.minimum))
        g.drawText(QRectF(mitte - 30, y, 60, 18), Qt.AlignHCenter | Qt.AlignVCenter, skala.beschriftung(skala.laut))
        g.drawText(QRectF(x0 + self.balken_breite - 60, y, 60, 18), Qt.AlignRight | Qt.AlignVCenter,
                   skala.beschriftung(skala.maximum))


def weichzeichnen(bild: QImage) -> QImage:
    """Cheap strong blur: shrink a lot and enlarge smoothly, twice."""
    w, h = bild.width(), bild.height()
    for faktor in (10, 6):
        klein = bild.scaled(max(1, w // faktor), max(1, h // faktor), Qt.IgnoreAspectRatio, Qt.SmoothTransformation)
        bild = klein.scaled(w, h, Qt.IgnoreAspectRatio, Qt.SmoothTransformation)
    return bild
