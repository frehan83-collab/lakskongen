"""
Sim Balance Probe — CLI Entry Point
Main command-line interface for the balance probe.
"""

import sys
import argparse
from pathlib import Path
from typing import List, Optional
from datetime import datetime

from .config import (
    get_scenarios,
    DEFAULT_SEEDS,
    SEED_START,
    CAREER_HORIZON_DAYS,
    DEFAULT_PARALLEL_JOBS,
    get_out_dir,
    get_ci_bands,
)
from .runner import run_all_scenarios, RunResult
from .metrics import extract_metrics, CareerMetrics, aggregate_scenario_metrics
from .report import generate_all_reports
from .ci_gate import run_ci_gate, run_ci_gate_multi, main_ci_gate
from .sensitivity import run_scenario_sensitivity, create_model_function


def convert_run_results(results: List[RunResult]) -> List[CareerMetrics]:
    """Convert RunResult list to CareerMetrics list."""
    metrics_list = []
    for r in results:
        if r.success and r.metrics:
            m = extract_metrics(r.scenario, r.seed, r.metrics)
            metrics_list.append(m)
        else:
            # Create a failed metric entry
            m = CareerMetrics(
                scenario=r.scenario,
                seed=r.seed,
                harvested_kg=0.0,
                revenue_nok=0.0,
                realized_price_per_kg=0.0,
                fcr=0.0,
                mortality_pct_per_cycle=0.0,
                peak_lice_per_fish=0.0,
                days_to_harvest=0,
                event_count=0,
                save_load_ok=False,
                harvested_all_sites=False,
                ending_balance_nok=0.0,
                ending_equity_nok=0.0,
                total_loan_principal_nok=0.0,
                storm_escape_events=0.0,
                storm_episodes=0,
                sustainability_score=0.0,
                note_kinds=[],
            )
            metrics_list.append(m)
    return metrics_list


def cmd_run(args: argparse.Namespace) -> int:
    """Run the balance probe sweep."""
    scenarios = args.scenarios or get_scenarios()
    seeds = args.seeds
    seed_start = args.seed_start
    horizon_days = args.horizon_days
    save_load = not args.no_save_load
    parallel_jobs = args.parallel
    out_dir = Path(args.out_dir) if args.out_dir else get_out_dir()
    configuration = args.configuration

    print(f"[sim-balance-probe] Starting balance sweep")
    print(f"  Scenarios: {scenarios}")
    print(f"  Seeds per scenario: {seeds} (starting at {seed_start})")
    print(f"  Horizon days: {horizon_days}")
    print(f"  Save/load round trip: {save_load}")
    print(f"  Parallel jobs: {parallel_jobs}")
    print(f"  Output dir: {out_dir}")
    print(f"  Build config: {configuration}")

    # Run all scenarios
    all_run_results = run_all_scenarios(
        scenarios=scenarios,
        seeds_per_scenario=seeds,
        seed_start=seed_start,
        horizon_days=horizon_days,
        save_load=save_load,
        parallel_jobs=parallel_jobs,
        configuration=configuration,
    )

    # Convert to metrics
    all_metrics = {}
    for scenario, results in all_run_results.items():
        all_metrics[scenario] = convert_run_results(results)

    # Print summary
    print(f"\n[sim-balance-probe] Sweep complete. Summary:")
    for scenario, metrics in all_metrics.items():
        successful = sum(1 for m in metrics if m.harvested_kg > 0)
        print(f"  {scenario}: {successful}/{len(metrics)} successful")

    # Generate reports
    bands = get_ci_bands()
    outputs = generate_all_reports(all_metrics, bands, out_dir)

    # Run CI gate
    print(f"\n[sim-balance-probe] Running CI gate...")
    csv_paths = [o["csv"] for o in outputs.values()]
    gate_result = run_ci_gate_multi(csv_paths, bands, verbose=True)

    if gate_result["passed"]:
        print(f"\n[sim-balance-probe] ✅ ALL SCENARIOS PASSED CI GATE")
        return 0
    else:
        print(f"\n[sim-balance-probe] ❌ CI GATE FAILED")
        return 1


def cmd_sensitivity(args: argparse.Namespace) -> int:
    """Run Sobol sensitivity analysis."""
    scenario = args.scenario
    n_samples = args.samples
    metric = args.metric
    out_dir = Path(args.out_dir) if args.out_dir else get_out_dir()

    print(f"[sim-balance-probe] Sensitivity analysis for {scenario}")
    print(f"  Samples: {n_samples}")
    print(f"  Target metric: {metric}")

    # This is a placeholder - needs actual engine integration
    result = run_scenario_sensitivity(
        scenario=scenario,
        n_samples=n_samples,
        metric=metric,
        out_dir=out_dir,
    )

    return 0


def cmd_report(args: argparse.Namespace) -> int:
    """Regenerate HTML report from CSV."""
    csv_path = Path(args.csv)
    out_dir = Path(args.out_dir) if args.out_dir else get_out_dir()
    bands = get_ci_bands()

    if not csv_path.exists():
        print(f"[report] CSV not found: {csv_path}")
        return 1

    # Load metrics
    from .ci_gate import load_metrics_from_csv
    metrics = load_metrics_from_csv(csv_path)
    scenario = metrics[0].scenario if metrics else "unknown"

    # Generate HTML
    from .report import generate_html_report
    html_path = generate_html_report(metrics, scenario, bands, out_dir)

    print(f"[report] HTML report: {html_path}")
    return 0


def cmd_ci_gate(args: argparse.Namespace) -> int:
    """Run CI gate check."""
    return main_ci_gate(sys.argv[2:])


def main() -> int:
    parser = argparse.ArgumentParser(
        prog="sim-balance-probe",
        description="Lakskongen multi-seed balance sweep and analysis",
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    # run command
    run_parser = subparsers.add_parser("run", help="Run multi-seed balance sweep")
    run_parser.add_argument("--scenarios", nargs="+", help="Scenario IDs to run")
    run_parser.add_argument("--seeds", type=int, default=DEFAULT_SEEDS, help="Seeds per scenario")
    run_parser.add_argument("--seed-start", type=int, default=SEED_START, help="Starting seed")
    run_parser.add_argument("--horizon-days", type=int, default=CAREER_HORIZON_DAYS, help="Career horizon days")
    run_parser.add_argument("--no-save-load", action="store_true", help="Disable mid-run save/load")
    run_parser.add_argument("--parallel", type=int, default=DEFAULT_PARALLEL_JOBS, help="Parallel jobs")
    run_parser.add_argument("--out-dir", type=str, help="Output directory")
    run_parser.add_argument("--configuration", type=str, default="Release", help="Build configuration")
    run_parser.set_defaults(func=cmd_run)

    # sensitivity command
    sens_parser = subparsers.add_parser("sensitivity", help="Run Sobol sensitivity analysis")
    sens_parser.add_argument("--scenario", required=True, help="Scenario ID")
    sens_parser.add_argument("--samples", type=int, default=1000, help="Base sample count")
    sens_parser.add_argument("--metric", default="mortality_pct_per_cycle", help="Target metric")
    sens_parser.add_argument("--out-dir", type=str, help="Output directory")
    sens_parser.set_defaults(func=cmd_sensitivity)

    # report command
    report_parser = subparsers.add_parser("report", help="Generate HTML report from CSV")
    report_parser.add_argument("--csv", required=True, help="Input CSV file")
    report_parser.add_argument("--out-dir", type=str, help="Output directory")
    report_parser.set_defaults(func=cmd_report)

    # ci-gate command
    gate_parser = subparsers.add_parser("ci-gate", help="Check CI gate bands")
    gate_parser.add_argument("--csv", type=Path, help="Single CSV file")
    gate_parser.add_argument("--csv-dir", type=Path, help="Directory of CSV files")
    gate_parser.add_argument("--band-mortality-min", type=float)
    gate_parser.add_argument("--band-mortality-max", type=float)
    gate_parser.add_argument("--band-fcr-min", type=float)
    gate_parser.add_argument("--band-fcr-max", type=float)
    gate_parser.add_argument("--band-price-min", type=float)
    gate_parser.add_argument("--band-price-max", type=float)
    gate_parser.add_argument("--band-equity-min", type=float)
    gate_parser.add_argument("--quiet", action="store_true")
    gate_parser.set_defaults(func=cmd_ci_gate)

    args = parser.parse_args()
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())