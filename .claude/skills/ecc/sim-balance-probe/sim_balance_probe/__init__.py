"""
Sim Balance Probe — Multi-seed balance sweep for Lakskongen simulation.
"""

__version__ = "0.1.0"
__author__ = "Lakskongen Team"

from .config import (
    SCENARIOS,
    DEFAULT_SEEDS,
    CI_BANDS,
    SOBOL_PARAMS,
    get_out_dir,
    get_scenarios,
    get_ci_bands,
)
from .runner import run_all_scenarios, run_scenario_seeds, RunResult
from .metrics import CareerMetrics, extract_metrics, aggregate_scenario_metrics, check_ci_gate
from .report import generate_csv, generate_html_report, generate_all_reports
from .ci_gate import run_ci_gate, run_ci_gate_multi
from .sensitivity import (
    run_sensitivity_analysis,
    run_scenario_sensitivity,
    SobolResult,
    create_sobol_problem,
    save_sobol_result,
    load_sobol_result,
)

__all__ = [
    # Config
    "SCENARIOS",
    "DEFAULT_SEEDS",
    "CI_BANDS",
    "SOBOL_PARAMS",
    "get_out_dir",
    "get_scenarios",
    "get_ci_bands",
    # Runner
    "run_all_scenarios",
    "run_scenario_seeds",
    "RunResult",
    # Metrics
    "CareerMetrics",
    "extract_metrics",
    "aggregate_scenario_metrics",
    "check_ci_gate",
    # Report
    "generate_csv",
    "generate_html_report",
    "generate_all_reports",
    # CI Gate
    "run_ci_gate",
    "run_ci_gate_multi",
    # Sensitivity
    "run_sensitivity_analysis",
    "run_scenario_sensitivity",
    "SobolResult",
    "create_sobol_problem",
    "save_sobol_result",
    "load_sobol_result",
]