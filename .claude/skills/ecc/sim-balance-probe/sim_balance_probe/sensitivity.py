"""
Sim Balance Probe — Sensitivity Analysis
Sobol sensitivity indices using SALib.
"""

import json
import numpy as np
from pathlib import Path
from datetime import datetime
from typing import Dict, List, Tuple, Any, Optional, Callable
from dataclasses import dataclass

try:
    from SALib.sample import saltelli
    from SALib.analyze import sobol
    SALIB_AVAILABLE = True
except ImportError:
    SALIB_AVAILABLE = False
    saltelli = None
    sobol = None

from .config import SOBOL_PARAMS, SOBOL_PATTERN, get_out_dir


@dataclass
class SobolResult:
    """Sobol sensitivity analysis result."""
    scenario: str
    n_samples: int
    param_names: List[str]
    param_bounds: List[Tuple[float, float]]
    S1: Dict[str, float]        # First-order indices
    S1_conf: Dict[str, float]   # Confidence intervals
    ST: Dict[str, float]        # Total-order indices
    ST_conf: Dict[str, float]   # Confidence intervals
    metadata: Dict[str, Any]

    def to_dict(self) -> Dict[str, Any]:
        return {
            "scenario": self.scenario,
            "n_samples": self.n_samples,
            "param_names": self.param_names,
            "param_bounds": self.param_bounds,
            "S1": self.S1,
            "S1_conf": self.S1_conf,
            "ST": self.ST,
            "ST_conf": self.ST_conf,
            "metadata": self.metadata,
        }

    def to_json(self, path: Path):
        path.write_text(json.dumps(self.to_dict(), indent=2))


def create_sobol_problem(param_names: List[str], param_bounds: List[Tuple[float, float]]) -> Dict[str, Any]:
    """Create SALib problem definition."""
    return {
        "num_vars": len(param_names),
        "names": param_names,
        "bounds": param_bounds,
    }


def generate_sobol_samples(
    problem: Dict[str, Any],
    n_samples: int,
    calc_second_order: bool = True,
) -> np.ndarray:
    """Generate Saltelli samples for Sobol analysis."""
    if not SALIB_AVAILABLE:
        raise RuntimeError("SALib not installed. Install with: pip install SALib")
    return saltelli.sample(problem, n_samples, calc_second_order=calc_second_order)


def run_sensitivity_analysis(
    scenario: str,
    model_func: Callable[[np.ndarray], np.ndarray],
    param_names: Optional[List[str]] = None,
    param_bounds: Optional[List[Tuple[float, float]]] = None,
    n_samples: int = 1000,
    calc_second_order: bool = True,
) -> SobolResult:
    """
    Run Sobol sensitivity analysis.
    
    Args:
        scenario: Scenario ID
        model_func: Function that takes parameter matrix (n_samples × n_params) 
                    and returns output array (n_samples,)
        param_names: Parameter names (default from config)
        param_bounds: Parameter bounds as (min, max) tuples (default from config)
        n_samples: Base sample count (Saltelli generates n_samples * (2n + 2) runs)
        calc_second_order: Whether to compute second-order indices
    
    Returns:
        SobolResult with S1 and ST indices
    """
    if not SALIB_AVAILABLE:
        raise RuntimeError("SALib not installed. Install with: pip install SALib")

    # Use defaults from config if not provided
    if param_names is None:
        param_names = list(SOBOL_PARAMS.keys())
    if param_bounds is None:
        param_bounds = list(SOBOL_PARAMS.values())

    print(f"[sensitivity] Running Sobol analysis for {scenario}")
    print(f"  Parameters: {param_names}")
    print(f"  Bounds: {param_bounds}")
    print(f"  Base samples: {n_samples} → Total runs: {n_samples * (2 * len(param_names) + 2)}")

    # Create problem definition
    problem = create_sobol_problem(param_names, param_bounds)

    # Generate samples
    param_values = generate_sobol_samples(problem, n_samples, calc_second_order)
    print(f"  Generated {len(param_values)} parameter sets")

    # Run model for each parameter set
    print(f"  Evaluating model...")
    outputs = model_func(param_values)
    print(f"  Model evaluations complete")

    # Analyze
    print(f"  Computing Sobol indices...")
    Si = sobol.analyze(problem, outputs, calc_second_order=calc_second_order, print_to_console=False)

    # Extract results
    S1 = {name: float(Si["S1"][i]) for i, name in enumerate(param_names)}
    S1_conf = {name: float(Si["S1_conf"][i]) for i, name in enumerate(param_names)}
    ST = {name: float(Si["ST"][i]) for i, name in enumerate(param_names)}
    ST_conf = {name: float(Si["ST_conf"][i]) for i, name in enumerate(param_names)}

    result = SobolResult(
        scenario=scenario,
        n_samples=n_samples,
        param_names=param_names,
        param_bounds=param_bounds,
        S1=S1,
        S1_conf=S1_conf,
        ST=ST,
        ST_conf=ST_conf,
        metadata={
            "timestamp": datetime.now().isoformat(),
            "total_runs": len(param_values),
            "output_mean": float(np.mean(outputs)),
            "output_std": float(np.std(outputs)),
        },
    )

    print(f"  First-order (S1): {S1}")
    print(f"  Total-order (ST): {ST}")

    return result


def save_sobol_result(result: SobolResult, out_dir: Optional[Path] = None) -> Path:
    """Save Sobol result to JSON."""
    out_dir = out_dir or get_out_dir()
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    path = out_dir / SOBOL_PATTERN.format(scenario=result.scenario, timestamp=timestamp)
    result.to_json(path)
    print(f"[sensitivity] Results saved: {path}")
    return path


def load_sobol_result(path: Path) -> SobolResult:
    """Load Sobol result from JSON."""
    data = json.loads(path.read_text())
    return SobolResult(
        scenario=data["scenario"],
        n_samples=data["n_samples"],
        param_names=data["param_names"],
        param_bounds=data["param_bounds"],
        S1=data["S1"],
        S1_conf=data["S1_conf"],
        ST=data["ST"],
        ST_conf=data["ST_conf"],
        metadata=data["metadata"],
    )


def print_sobol_summary(result: SobolResult):
    """Print formatted Sobol summary."""
    print(f"\n{'='*60}")
    print(f"Sobol Sensitivity Analysis: {result.scenario}")
    print(f"{'='*60}")
    print(f"Parameters: {result.n_samples} base samples → {result.metadata['total_runs']} total runs")
    print(f"Output: mean={result.metadata['output_mean']:.3f}, std={result.metadata['output_std']:.3f}")
    print()
    print(f"{'Parameter':<20} {'S1':>10} {'S1_conf':>10} {'ST':>10} {'ST_conf':>10}")
    print(f"{'-'*60}")
    for name in result.param_names:
        print(f"{name:<20} {result.S1[name]:>10.4f} {result.S1_conf[name]:>10.4f} "
              f"{result.ST[name]:>10.4f} {result.ST_conf[name]:>10.4f}")
    print(f"{'='*60}\n")


def create_model_function(
    scenario: str,
    base_seed: int = 1,
    horizon_days: int = 1200,
) -> Callable[[np.ndarray], np.ndarray]:
    """
    Create a model function for sensitivity analysis.
    
    This is a placeholder - the actual implementation would:
    1. Take parameter multipliers (sea_mortality, feed_cost, etc.)
    2. Modify SimulationEngine calibration/balancing accordingly
    3. Run the career
    4. Return the metric of interest (e.g., mortality_pct_per_cycle)
    
    For now, returns a dummy function that needs to be wired to the engine.
    """
    def model_func(param_matrix: np.ndarray) -> np.ndarray:
        # param_matrix shape: (n_runs, n_params)
        # n_params = len(SOBOL_PARAMS) = 4
        # Each column is a multiplier for: sea_mortality, feed_cost, lice_pressure, storm_loss
        
        n_runs = param_matrix.shape[0]
        outputs = np.zeros(n_runs)
        
        for i in range(n_runs):
            multipliers = param_matrix[i]
            # TODO: Actually run simulation with these multipliers
            # For now, return a deterministic function of inputs for testing
            sea_mort, feed_cost, lice_press, storm_loss = multipliers
            # Dummy model: mortality increases with sea_mort and lice_press
            outputs[i] = 10.0 * sea_mort + 5.0 * lice_press + 2.0 * feed_cost + 1.0 * storm_loss
            
        return outputs
    
    return model_func


def run_scenario_sensitivity(
    scenario: str,
    n_samples: int = 1000,
    metric: str = "mortality_pct_per_cycle",
    out_dir: Optional[Path] = None,
) -> SobolResult:
    """
    Run sensitivity analysis for a scenario.
    
    This is the high-level entry point that wires up the actual simulation.
    """
    # Create model function (placeholder - needs engine integration)
    model_func = create_model_function(scenario)
    
    # Run analysis
    result = run_sensitivity_analysis(
        scenario=scenario,
        model_func=model_func,
        n_samples=n_samples,
    )
    
    # Save
    save_sobol_result(result, out_dir)
    print_sobol_summary(result)
    
    return result