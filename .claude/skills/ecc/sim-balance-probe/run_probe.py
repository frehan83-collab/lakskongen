#!/usr/bin/env python3
"""
Sim Balance Probe — Thin wrapper for `python -m sim_balance_probe`
"""

import sys
from sim_balance_probe.cli import main

if __name__ == "__main__":
    sys.exit(main())