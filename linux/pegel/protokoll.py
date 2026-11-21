"""Error log instead of dialogs (like fehler.log of the Windows app)."""
from __future__ import annotations

import datetime
import os
import sys
import traceback
from pathlib import Path


def datenordner() -> Path:
    if sys.platform.startswith("win"):
        basis = Path(os.environ.get("LOCALAPPDATA", Path.home()))
    else:
        basis = Path(os.environ.get("XDG_CACHE_HOME", Path.home() / ".cache"))
    ordner = basis / "kopfhoererpegel"
    ordner.mkdir(parents=True, exist_ok=True)
    return ordner


def fehler_schreiben(fehler: BaseException) -> None:
    try:
        text = "".join(traceback.format_exception(type(fehler), fehler, fehler.__traceback__))
        with open(datenordner() / "fehler.log", "a", encoding="utf-8") as f:
            f.write(f"{datetime.datetime.now():%Y-%m-%d %H:%M:%S}  {text}\n")
    except OSError:
        pass
