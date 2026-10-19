#!/usr/bin/env python3
"""Run the shared cooperative recorder without projecting product output."""
from pathlib import Path
import runpy
runpy.run_path(str(Path(__file__).resolve().parent.parent / "command-bound" / "runner.py"), run_name="__main__")
