#!/usr/bin/env python3
"""Use the existing cooperative command-bound recorder without changing responses."""
from pathlib import Path
import runpy

runpy.run_path(Path(__file__).resolve().parent.parent / "command-bound" / "runner.py",
              run_name="__main__")
