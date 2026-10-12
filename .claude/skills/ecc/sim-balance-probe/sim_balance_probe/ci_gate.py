"""
Sim Balance Probe — CI Gate
Enforces p95 bands for balance validation.
"""

import json
import sys
from pathlib import Path
from typing import Dict, Any, List, Tuple, Optional
import pandas as pd
import numpy as np

from .config import CI_BANDS, get_out_dir, get_ci_bands
from .metrics import CareerMetrics, check_ci_gate, extract_metrics
from .report import generate_csv


def load_metrics_from_csv(csv_path: Path) -> List[CareerMetrics]:
    """Load CareerMetrics from CSV file."""
    df = pd.read_csv(csv_path)
    metrics_list = []
    
    for _, row in df.iterrows():
        m = CareerMetrics(
            scenario=row.get("scenario", ""),
            seed=int(row.get("seed", 0)),
            harvested_kg=float(row.get("harvested_kg", 0)),
            revenue_nok=float(row.get("revenue_nok", 0)),
            realized_price_per_kg=float(row.get("realized_price_per_kg", 0)),
            fcr=float(row.get("fcr", 0)),
            mortality_pct_per_cycle=float(row.get("mortality_pct_per_cycle", 0)),
            peak_lice_per_fish=float(row.get("peak_lice_per_fish", 0)),
            days_to_harvest=int(row.get("days_to_harvest", 0)),
            event_count=int(row.get("event_count", 0)),
            save_load_ok=bool(row.get("save_load_ok", False)),
            harvested_all_sites=bool(row.get("harvested_all_sites", False)),
            ending_balance_nok=float(row.get("ending_balance_nok", 0)),
            ending_equity_nok=float(row.get("ending_equity_nok", 0)),
            total_loan_principal_nok=float(row.get("total_loan_principal_nok", 0)),
            storm_escape_events=float(row.get("storm_escape_events", 0)),
            storm_episodes=int(row.get("storm_episodes", 0)),
            sustainability_score=float(row.get("sustainability_score", 0)),
            note_kinds=(
                str(row.get("note_kinds", "")).split(";") 
                if pd.notna(row.get("note_kinds")) and str(row.get("note_kinds", "")).strip() 
                else []
            ),
        )
        metrics_list.append(m)
    
    return metrics_list


def run_ci_gate(
    csv_path: Path,
    bands: Optional[Dict[str, Tuple[float, float]]] = None,
    verbose: bool = True,
) -> Dict[str, Any]:
    """
    Run CI gate check on a CSV file.
    
    Returns dict with 'passed', 'violations', 'summary'.
    Exit code: 0 = PASS, 1 = FAIL
    """
    bands = bands or get_ci_bands()
    
    if verbose:
        print(f"[ci-gate] Checking {csv_path}")
        print(f"[ci-gate] Bands: {bands}")

    metrics = load_metrics_from_csv(csv_path)
    
    if not metrics:
        return {
            "passed": False,
            "violations": [{"metric": "all", "issue": "no_data", "message": "No metrics in CSV"}],
            "summary": {"n_seeds": 0},
        }

    result = check_ci_gate(metrics, bands)
    
    # Add summary stats
    result["summary"] = {
        "n_seeds": len(metrics),
        "scenario": metrics[0].scenario if metrics else "unknown",
    }
    
    if verbose:
        print(f"[ci-gate] Seeds: {len(metrics)}")
        print(f"[ci-gate] Scenario: {result['summary']['scenario']}")
        
        if result["passed"]:
            print(f"[ci-gate] ✅ PASS — all p95 bands satisfied")
        else:
            print(f"[ci-gate] ❌ FAIL — {len(result['violations'])} band violation(s):")
            for v in result["violations"]:
                print(f"  - {v['metric']}: p95={v['p95']:.2f} "
                      f"(band: {v['band'][0]:.2f}–{v['band'][1]:.2f}) {v['direction']}")
    
    return result


def run_ci_gate_multi(
    csv_paths: List[Path],
    bands: Optional[Dict[str, Tuple[float, float]]] = None,
    verbose: bool = True,
) -> Dict[str, Any]:
    """Run CI gate on multiple CSVs (one per scenario)."""
    all_results = {}
    overall_passed = True
    
    for csv_path in csv_paths:
        scenario = csv_path.stem.replace("balance_probe_", "").rsplit("_", 2)[0]
        result = run_ci_gate(csv_path, bands, verbose=verbose)
        all_results[scenario] = result
        if not result["passed"]:
            overall_passed = False
    
    return {
        "passed": overall_passed,
        "scenarios": all_results,
    }


def main_ci_gate(args: List[str]) -> int:
    """CLI entry point for ci-gate command."""
    import argparse
    
    parser = argparse.ArgumentParser(description="Sim Balance Probe CI Gate")
    parser.add_argument("--csv", type=Path, required=True, help="CSV file to check")
    parser.add_argument("--csv-dir", type=Path, help="Directory with CSV files (check all)")
    parser.add_argument("--band-mortality-min", type=float, help="Override mortality min")
    parser.add_argument("--band-mortality-max", type=float, help="Override mortality max")
    parser.add_argument("--band-fcr-min", type=float, help="Override FCR min")
    parser.add_argument("--band-fcr-max", type=float, help="Override FCR max")
    parser.add_argument("--band-price-min", type=float, help="Override price min")
    parser.add_argument("--band-price-max", type=float, help="Override price max")
    parser.add_argument("--band-equity-min", type=float, help="Override equity min")
    parser.add_argument("--quiet", action="store_true", help="Suppress output")
    
    parsed = parser.parse_args(args)
    
    # Build custom bands from args
    bands = get_ci_bands()
    if parsed.band_mortality_min is not None:
        bands["mortality_pct_per_cycle"] = (parsed.band_mortality_min, bands["mortality_pct_per_cycle"][1])
    if parsed.band_mortality_max is not None:
        bands["mortality_pct_per_cycle"] = (bands["mortality_pct_per_cycle"][0], parsed.band_mortality_max)
    if parsed.band_fcr_min is not None:
        bands["fcr"] = (parsed.band_fcr_min, bands["fcr"][1])
    if parsed.band_fcr_max is not None:
        bands["fcr"] = (bands["fcr"][0], parsed.band_fcr_max)
    if parsed.band_price_min is not None:
        bands["realized_price_per_kg"] = (parsed.band_price_min, bands["realized_price_per_kg"][1])
    if parsed.band_price_max is not None:
        bands["realized_price_per_kg"] = (bands["realized_price_per_kg"][0], parsed.band_price_max)
    if parsed.band_equity_min is not None:
        bands["ending_equity_nok"] = (parsed.band_equity_min, bands["ending_equity_nok"][1])
    
    if parsed.csv_dir:
        csv_files = list(parsed.csv_dir.glob("balance_probe_*.csv"))
        if not csv_files:
            print(f"[ci-gate] No CSV files found in {parsed.csv_dir}")
            return 1
        result = run_ci_gate_multi(csv_files, bands, verbose=not parsed.quiet)
    else:
        result = run_ci_gate(parsed.csv, bands, verbose=not parsed.quiet)
    
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    sys.exit(main_ci_gate(sys.argv[1:]))