"""Makes the README pictures from the frames of `Kopfhoererpegel.exe --render-shots <dir> <wallpaper>`.

python tools/gifs.py <dir> <target>   ->  <target>/pegel-ablauf.gif, einstellungen-ablauf.gif, titelbild.png, einstellungen.png
"""
import shutil
import sys
from pathlib import Path

from PIL import Image

src, dst = Path(sys.argv[1]), Path(sys.argv[2])
dst.mkdir(parents=True, exist_ok=True)
for name in ("pegel", "einstellungen"):
    files = sorted((src / f"frames-{name}").glob("*.png"))
    frames = [Image.open(f).convert("RGB").resize((900, 507), Image.LANCZOS) for f in files]
    # One palette for all frames (from a strip of samples): unchanged pixels stay identical, so GIF only stores the changes.
    # The card and the tray get extra weight, so green, yellow and red keep their full colour.
    samples = frames[:: max(1, len(frames) // 8)]
    cards = [f.crop((440, 230, 900, 507)) for f in frames[:: max(1, len(frames) // 24)]]
    strip = Image.new("RGB", (900, 507 * len(samples) + 277 * len(cards) * 2))
    for i, f in enumerate(samples):
        strip.paste(f, (0, 507 * i))
    y = 507 * len(samples)
    for c in cards * 2:
        strip.paste(c, (0, y))
        y += 277
    # Plus swatches of the card colours (green, yellow, red, the red button on the dark card) in a few shades.
    swatches = [(0x30, 0xD1, 0x58), (0xFF, 0xD6, 0x0A), (0xFF, 0x45, 0x3A), (0x6E, 0x34, 0x33), (0x8F, 0x3A, 0x36), (0x2E, 0x8A, 0x45)]
    sw = Image.new("RGB", (900, 120 * len(swatches)))
    for i, c in enumerate(swatches):
        for k in range(6):
            t = k / 5 * 0.45
            shade = tuple(round(v + (0x3A - v) * t) for v in c)
            sw.paste(shade, (k * 150, 120 * i, (k + 1) * 150, 120 * (i + 1)))
    full = Image.new("RGB", (900, strip.height + sw.height))
    full.paste(strip, (0, 0))
    full.paste(sw, (0, strip.height))
    strip = full
    palette = strip.quantize(colors=255, method=Image.Quantize.MEDIANCUT)
    frames = [f.quantize(palette=palette, dither=Image.Dither.NONE) for f in frames]
    frames[0].save(dst / f"{name}-ablauf.gif", save_all=True, append_images=frames[1:], duration=50, loop=0, optimize=True)
for still in ("titelbild.png", "einstellungen.png"):
    Image.open(src / still).convert("RGB").resize((1350, 760), Image.LANCZOS).save(dst / still, optimize=True)
