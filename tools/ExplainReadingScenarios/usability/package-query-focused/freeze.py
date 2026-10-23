#!/usr/bin/env python3
"""Use the existing answer/log freeze before any separate retrospective."""
from pathlib import Path
import runpy

runpy.run_path(Path(__file__).resolve().parent.parent / "command-bound" / "freeze.py",
              run_name="__main__")
