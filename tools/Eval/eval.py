#!/usr/bin/env python3
"""Entry point: python3 tools/Eval/eval.py {run|judge|report|all} --run-name NAME ..."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from geef_eval.cli import main  # noqa: E402

if __name__ == "__main__":
    sys.exit(main())
