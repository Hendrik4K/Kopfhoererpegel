"""Colours and the tray icon, drawn exactly like the Windows app (64-unit grid, 4x oversampled)."""
from __future__ import annotations

from PySide6.QtCore import QPointF, QRectF, Qt
from PySide6.QtGui import QColor, QIcon, QImage, QPainter, QPainterPath, QPen, QPixmap, QPolygonF

from .. import werte

# iOS system colours (dark), as in Popup.cs
GRUEN = QColor(0x30, 0xD1, 0x58)
GELB = QColor(0xFF, 0xD6, 0x0A)
ROT = QColor(0xFF, 0x45, 0x3A)
GEDAEMPFT = QColor(0x9E, 0x9E, 0x9E)
DUNKEL = QColor(0x1C, 0x1C, 0x1C)


def farbe(status: str) -> QColor:
    return {werte.SEHR_LAUT: ROT, werte.LAUT: GELB, werte.OK: GRUEN}.get(status, GEDAEMPFT)


def mischen(a: QColor, b: QColor, t: float) -> QColor:
    t = max(0.0, min(1.0, t))
    return QColor(round(a.red() + (b.red() - a.red()) * t), round(a.green() + (b.green() - a.green()) * t),
                  round(a.blue() + (b.blue() - a.blue()) * t), round(a.alpha() + (b.alpha() - a.alpha()) * t))


def saettigung(c: QColor, staerke: float) -> QColor:
    """staerke 1 = full colour, lower = less saturated and a little darker (the beat pulse)."""
    lum = round((0.299 * c.red() + 0.587 * c.green() + 0.114 * c.blue()) * 0.6)
    return mischen(QColor(lum, lum, lum), c, staerke)


def stift(c: QColor, breite: float) -> QPen:
    p = QPen(c, breite)
    p.setCapStyle(Qt.RoundCap)
    p.setJoinStyle(Qt.RoundJoin)
    return p


def _ohr(g: QPainter, tinte: QColor, klein: bool) -> None:
    # GDI arcs (clockwise, y down) become Qt arcs with negated angles.
    g.save()
    g.translate(30, 26)
    g.scale(1.1, 1.1)
    g.translate(-30, -26)
    aussen = QPainterPath(QPointF(15, 30))
    aussen.lineTo(15, 21)
    aussen.arcTo(QRectF(15, 6, 30, 30), -180, -200)
    aussen.cubicTo(42, 32, 36, 34, 34, 40)
    aussen.cubicTo(32, 46, 26, 48, 21, 44)
    innen = QPainterPath()
    innen.arcMoveTo(QRectF(23, 14, 14, 14), -185)
    innen.arcTo(QRectF(23, 14, 14, 14), -185, -195)
    innen.cubicTo(35.5, 27, 32, 27, 30.5, 31)
    g.setBrush(Qt.NoBrush)
    g.setPen(stift(tinte, 6.4 if klein else 5.2))
    g.drawPath(aussen)
    g.drawPath(innen)
    g.restore()


def _symbol(g: QPainter, status: str, punkte: list[float], tinte: QColor, klein: bool) -> None:
    _ohr(g, tinte, klein)
    c = farbe(status if status not in (werte.AUS, werte.STILL) else werte.OK)
    if status == werte.SEHR_LAUT:
        # Red warning triangle; a transparent margin cut into the ear keeps it apart on any panel.
        dreieck = QPolygonF([QPointF(47, 26.5), QPointF(60, 50), QPointF(34, 50)])
        g.setCompositionMode(QPainter.CompositionMode_Clear)
        g.setPen(stift(Qt.black, 9))
        g.setBrush(Qt.black)
        g.drawPolygon(dreieck)
        g.setCompositionMode(QPainter.CompositionMode_SourceOver)
        g.setPen(stift(c, 3))
        g.setBrush(c)
        g.drawPolygon(dreieck)
        g.setPen(stift(DUNKEL, 3.8))
        g.drawLine(QPointF(47, 34.5), QPointF(47, 41.5))
        g.drawLine(QPointF(47, 46), QPointF(47, 46.2))
    elif status in (werte.OK, werte.LAUT):
        g.setCompositionMode(QPainter.CompositionMode_Clear)
        g.setPen(Qt.NoPen)
        g.setBrush(Qt.black)
        g.drawEllipse(QRectF(32, 24, 30, 30))
        g.setCompositionMode(QPainter.CompositionMode_SourceOver)
        g.setBrush(c)
        g.drawEllipse(QRectF(35.5, 27.5, 23, 23))
        g.setPen(stift(DUNKEL, 4.2))
        if status == werte.OK:
            g.drawPolyline([QPointF(41.5, 39.5), QPointF(45.5, 43.5), QPointF(52.5, 35)])
        else:
            g.drawLine(QPointF(47, 33), QPointF(47, 40.5))
            g.drawLine(QPointF(47, 45.2), QPointF(47, 45.4))
    # Three dots under the ear, dim -> full colour
    g.setPen(Qt.NoPen)
    schwach = QColor(c)
    schwach.setAlpha(0x55)
    for i, p in enumerate(punkte):
        g.setBrush(mischen(schwach, c, p))
        g.drawEllipse(QRectF(9 + i * 17, 52, 12, 12))


def symbol_bild(groesse: int, status: str, punkte: list[float], hell: bool) -> QImage:
    k = 4
    gross = QImage(groesse * k, groesse * k, QImage.Format_ARGB32_Premultiplied)
    gross.fill(Qt.transparent)
    g = QPainter(gross)
    g.setRenderHint(QPainter.Antialiasing)
    g.scale(groesse * k / 64, groesse * k / 64)
    _symbol(g, status, punkte, QColor(0x1A, 0x1A, 0x1A) if hell else QColor(Qt.white), groesse <= 20)
    g.end()
    return gross.scaled(groesse, groesse, Qt.IgnoreAspectRatio, Qt.SmoothTransformation)


def symbol(status: str, punkte: list[float], hell: bool) -> QIcon:
    icon = QIcon()
    for groesse in (16, 22, 24, 32, 48, 64):
        icon.addPixmap(QPixmap.fromImage(symbol_bild(groesse, status, punkte, hell)))
    return icon


def statuszeichen(g: QPainter, x: float, y: float, status: str) -> None:
    """The 22 x 22 round status icon of the card: check mark, exclamation mark or dash."""
    g.save()
    g.translate(x, y)
    g.setPen(Qt.NoPen)
    g.setBrush(farbe(status))
    g.drawEllipse(QRectF(0, 0, 22, 22))
    g.setPen(stift(DUNKEL, 2.2))
    g.setBrush(Qt.NoBrush)
    if status == werte.OK:
        g.drawPolyline([QPointF(6.4, 11.4), QPointF(9.6, 14.4), QPointF(15.6, 7.8)])
    elif status in (werte.LAUT, werte.SEHR_LAUT):
        g.drawLine(QPointF(11, 5.8), QPointF(11, 12.2))
        g.drawLine(QPointF(11, 16), QPointF(11, 16.1))
    else:
        g.drawLine(QPointF(7, 11), QPointF(15, 11))
    g.restore()
