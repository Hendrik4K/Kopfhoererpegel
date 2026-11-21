"""Pictures for checking the look without a desktop session (also runs with QT_QPA_PLATFORM=offscreen)."""
from __future__ import annotations

from pathlib import Path

from PySide6.QtCore import QPointF, QRectF, Qt, QTimer
from PySide6.QtGui import QColor, QImage, QLinearGradient, QPainter, QRadialGradient

from .. import quellen, werte
from . import zeichnen
from .karte import UNSCHAERFE, Karte, weichzeichnen


def tray_speichern(ordner: str) -> None:
    ziel = Path(ordner)
    ziel.mkdir(parents=True, exist_ok=True)
    beispiele = {werte.STILL: [0, 0, 0], werte.OK: [0.8, 0.5, 0], werte.LAUT: [1, 0.8, 0.4], werte.SEHR_LAUT: [1, 1, 0.6]}
    for status, punkte in beispiele.items():
        for groesse in (16, 22, 24, 32, 64):
            for hell in (False, True):
                zeichnen.symbol_bild(groesse, status, punkte, hell).save(
                    str(ziel / f"{status}-{'hell' if hell else 'dunkel'}-{groesse}.png"))


def _testhintergrund(w: int, h: int) -> QImage:
    """Colourful stand-in for the desktop behind the card (purple/orange like the iPhone reference)."""
    bild = QImage(w, h, QImage.Format_ARGB32_Premultiplied)
    g = QPainter(bild)
    verlauf = QLinearGradient(0, 0, w, h)
    verlauf.setColorAt(0, QColor(0x3A, 0x1F, 0x5C))
    verlauf.setColorAt(0.5, QColor(0xB8, 0x4A, 0x2A))
    verlauf.setColorAt(1, QColor(0x5A, 0x2D, 0x8A))
    g.fillRect(bild.rect(), verlauf)
    for x, y, r, farbe in ((0.3, 0.4, 0.35, QColor(0xF0, 0x9A, 0x50)), (0.75, 0.6, 0.3, QColor(0x9B, 0x59, 0xD6))):
        rg = QRadialGradient(QPointF(w * x, h * y), w * r)
        rg.setColorAt(0, farbe)
        rg.setColorAt(1, QColor(0, 0, 0, 0))
        g.fillRect(bild.rect(), rg)
    g.end()
    return bild


def karte_speichern(args, app) -> int:
    quelle = quellen.erstellen(args.quelle, args.geraet, 106, args.befehl, args.bereich, args.titel, args.einheit)
    quelle.start()
    karte = Karte(quelle.messwert().skala)
    w, h = karte.width(), karte.height()
    rand = 40
    szene = _testhintergrund(w + 2 * rand, h + 2 * rand)
    karte.hintergrund = weichzeichnen(szene.copy(rand - UNSCHAERFE, rand - UNSCHAERFE, w + 2 * UNSCHAERFE, h + 2 * UNSCHAERFE))

    def fertig():
        bild = karte.grab().toImage()
        g = QPainter(szene)
        g.drawImage(QRectF(rand, rand, w, h), bild)
        g.end()
        szene.save(args.render_karte)
        quelle.stop()
        app.quit()

    takt = QTimer(interval=33, timeout=lambda: karte.animieren(quelle.messwert()))
    takt.start()
    QTimer.singleShot(1800, fertig)
    app.exec()
    return 0
