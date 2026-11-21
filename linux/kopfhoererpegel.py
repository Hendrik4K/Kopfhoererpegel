#!/usr/bin/env python3
"""Start: python3 kopfhoererpegel.py [--quelle kopfhoerer|mikrofon|ausgang|extern|demo] … (see README.md)."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from pegel.oberflaeche.app import main  # noqa: E402

if __name__ == "__main__":
    sys.exit(main())
