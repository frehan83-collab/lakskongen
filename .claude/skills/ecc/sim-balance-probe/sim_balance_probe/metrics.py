"""
Sim Balance Probe — Metrics Extraction
Extracts and computes additional metrics from engine state.
"""

from typing import Dict, Any, List, Optional
from dataclasses import dataclass


@dataclass
class CareerMetrics:
    """Normalized metrics for a single career run."""
    # Core metrics from PlayerScriptProbe.CareerResult
    scenario: str
    seed: int
    harvested_kg: float
    revenue_nok: float
    realized_price_per_kg: float
    fcr: float
    mortality_pct_per_cycle: float
    peak_lice_per_fish: float
    days_to_harvest: int
    event_count: int
    save_load_ok: bool
    harvested_all_sites: bool
    ending_balance_nok: float
    note_kinds: List[str]

    # Additional engine metrics (computed)
    storm_escape_events: float = 0.0
    sustainability_score: float = 0.0
    total_loan_principal_nok: float = 0.0
    ending_equity_nok: float = 0.0

    # Derived metrics
    lice_peak_category: str = ""  # "low", "medium", "high", "critical"
    storm_episodes: int = 0
    price_trajectory: List[float] = None  # daily spot prices

    def __post_init__(self):
        if self.price_trajectory is None:
            self.price_trajectory = []
        self._categorize_lice()

    def _categorize_lice(self):
        """Categorize peak lice level."""
        if self.peak_lice_per_fish < 0.1:
            self.lice_peak_category = "low"
        elif self.peak_lice_per_fish < 0.3:
            self.lice_peak_category = "medium"
        elif self.peak_lice_per_fish < 0.5:
            self.lice_peak_category = "high"
        else:
            self.lice_peak_category = "critical"

    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary for DataFrame/CSV."""
        return {
            "scenario": self.scenario,
            "seed": self.seed,
            "harvested_kg": self.harvested_kg,
            "revenue_nok": self.revenue_nok,
            "realized_price_per_kg": self.realized_price_per_kg,
            "fcr": self.fcr,
            "mortality_pct_per_cycle": self.mortality_pct_per_cycle,
            "peak_lice_per_fish": self.peak_lice_per_fish,
            "lice_peak_category": self.lice_peak_category,
            "days_to_harvest": self.days_to_harvest,
            "event_count": self.event_count,
            "save_load_ok": self.save_load_ok,
            "harvested_all_sites": self.harvested_all_sites,
            "ending_balance_nok": self.ending_balance_nok,
            "ending_equity_nok": self.ending_equity_nok,
            "total_loan_principal_nok": self.total_loan_principal_nok,
            "storm_escape_events": self.storm_escape_events,
            "storm_episodes": self.storm_episodes,
            "sustainability_score": self.sustainability_score,
            "note_kinds": ";".join(self.note_kinds) if self.note_kinds else "",
        }


def extract_metrics(
    scenario: str,
    seed: int,
    career_result: Dict[str, Any],
    engine_state: Optional[Dict[str, Any]] = None,
) -> CareerMetrics:
    """
    Extract normalized metrics from PlayerScriptProbe.CareerResult JSON.
    
    Args:
        scenario: Scenario ID
        seed: Random seed used
        career_result: Parsed JSON from PlayerScriptProbe.RunCareer()
        engine_state: Optional additional engine state (if available)
    
    Returns:
        CareerMetrics with all fields populated
    """
    # Map from CareerResult JSON (camelCase) to our fields
    metrics = CareerMetrics(
        scenario=scenario,
        seed=seed,
        harvested_kg=career_result.get("harvestedKg", 0.0),
        revenue_nok=career_result.get("revenueNok", 0.0),
        realized_price_per_kg=career_result.get("realizedPricePerKg", 0.0),
        fcr=career_result.get("economicFcr", 0.0),
        mortality_pct_per_cycle=career_result.get("seaMortalityPct", 0.0),
        peak_lice_per_fish=career_result.get("peakAdultFemaleLice", 0.0),
        days_to_harvest=career_result.get("daysRun", 0),
        event_count=career_result.get("eventLogEntries", 0),
        save_load_ok=career_result.get("saveLoadRoundTripped", False),
        harvested_all_sites=career_result.get("harvestedAllSites", False),
        ending_balance_nok=float(career_result.get("endingBalance", 0)),
        note_kinds=career_result.get("noteKinds", []),
    )

    # Extract additional metrics from engine_state if provided
    if engine_state:
        metrics.storm_escape_events = engine_state.get("stormEscapeEvents", 0.0)
        metrics.sustainability_score = engine_state.get("sustainabilityScore", 0.0)
        
        # Compute equity = balance - loan principal
        active_loans = engine_state.get("activeLoans", [])
        total_principal = sum(float(loan.get("principal", 0)) for loan in active_loans)
        metrics.total_loan_principal_nok = total_principal
        metrics.ending_equity_nok = metrics.ending_balance_nok - total_principal
        
        # Count storm episodes from weather history if available
        weather_history = engine_state.get("weatherHistory", [])
        metrics.storm_episodes = sum(1 for w in weather_history if w.get("state") == "Severe")
        
        # Price trajectory from market simulation
        price_history = engine_state.get("priceHistory", [])
        metrics.price_trajectory = price_history

    return metrics


def aggregate_scenario_metrics(metrics_list: List[CareerMetrics]) -> Dict[str, Any]:
    """Compute aggregate statistics for a scenario across seeds."""
    if not metrics_list:
        return {}

    import numpy as np

    def percentile(data: List[float], p: float) -> float:
        if not data:
            return float("nan")
        return float(np.percentile(data, p))

    def mean(data: List[float]) -> float:
        if not data:
            return float("nan")
        return float(np.mean(data))

    def std(data: List[float]) -> float:
        if not data:
            return float("nan")
        return float(np.std(data))

    # Extract numeric fields
    fields = [
        "harvested_kg", "revenue_nok", "realized_price_per_kg", "fcr",
        "mortality_pct_per_cycle", "peak_lice_per_fish", "days_to_harvest",
        "event_count", "ending_balance_nok", "ending_equity_nok",
        "total_loan_principal_nok", "storm_escape_events", "sustainability_score",
    ]

    agg = {"scenario": metrics_list[0].scenario, "n_seeds": len(metrics_list)}

    for field in fields:
        values = [getattr(m, field) for m in metrics_list]
        agg[f"{field}_mean"] = mean(values)
        agg[f"{field}_std"] = std(values)
        agg[f"{field}_p50"] = percentile(values, 50)
        agg[f"{field}_p95"] = percentile(values, 95)
        agg[f"{field}_min"] = min(values) if values else float("nan")
        agg[f"{field}_max"] = max(values) if values else float("nan")

    # Categorical: lice peak distribution
    lice_cats = [m.lice_peak_category for m in metrics_list]
    for cat in ["low", "medium", "high", "critical"]:
        agg[f"lice_peak_{cat}_count"] = lice_cats.count(cat)
        agg[f"lice_peak_{cat}_pct"] = lice_cats.count(cat) / len(lice_cats) * 100

    # Boolean rates
    agg["save_load_success_rate"] = sum(m.save_load_ok for m in metrics_list) / len(metrics_list) * 100
    agg["harvest_all_sites_rate"] = sum(m.harvested_all_sites for m in metrics_list) / len(metrics_list) * 100

    # Note kinds frequency
    all_notes = []
    for m in metrics_list:
        all_notes.extend(m.note_kinds)
    from collections import Counter
    note_counts = Counter(all_notes)
    agg["note_kind_frequencies"] = dict(note_counts)

    return agg


def check_ci_gate(metrics_list: List[CareerMetrics], bands: Dict[str, tuple]) -> Dict[str, Any]:
    """
    Check if scenario passes CI gate at p95.
    
    Returns dict with 'passed' (bool) and 'violations' (list of dicts).
    """
    import numpy as np

    violations = []

    # Map band keys to metric fields
    band_mapping = {
        "mortality_pct_per_cycle": "mortality_pct_per_cycle",
        "fcr": "fcr",
        "realized_price_per_kg": "realized_price_per_kg",
        "ending_equity_nok": "ending_equity_nok",
    }

    for band_key, (lower, upper) in bands.items():
        field = band_mapping.get(band_key)
        if not field:
            continue

        values = [getattr(m, field) for m in metrics_list]
        if not values:
            violations.append({
                "metric": band_key,
                "issue": "no_data",
                "p95": None,
                "band": [lower, upper],
            })
            continue

        p95 = float(np.percentile(values, 95))

        if p95 < lower or p95 > upper:
            violations.append({
                "metric": band_key,
                "issue": "band_violation",
                "p95": p95,
                "band": [lower, upper],
                "direction": "below" if p95 < lower else "above",
            })

    return {
        "passed": len(violations) == 0,
        "violations": violations,
        "n_seeds": len(metrics_list),
    }