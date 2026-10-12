"""
Sim Balance Probe — Report Generation
CSV export + HTML report with box plots via matplotlib.
"""

import json
import pandas as pd
import numpy as np
from pathlib import Path
from datetime import datetime
from typing import List, Dict, Any, Optional
import matplotlib
matplotlib.use("Agg")  # Non-interactive backend
import matplotlib.pyplot as plt
import seaborn as sns
from jinja2 import Template

from .config import CSV_PATTERN, HTML_PATTERN, get_out_dir
from .metrics import CareerMetrics, aggregate_scenario_metrics, check_ci_gate


# ─── HTML Template ───
HTML_TEMPLATE = Template('''
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <title>Balance Probe Report: {{ scenario }}</title>
    <style>
        body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; max-width: 1200px; margin: 0 auto; padding: 20px; background: #fafafa; }
        h1 { color: #1a1a2e; border-bottom: 2px solid #e94560; padding-bottom: 10px; }
        h2 { color: #16213e; margin-top: 40px; }
        .meta { background: #fff; padding: 15px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); margin-bottom: 20px; }
        .meta-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 15px; }
        .meta-item { background: #f8f9fa; padding: 12px; border-radius: 6px; }
        .meta-label { font-size: 0.85rem; color: #666; text-transform: uppercase; letter-spacing: 0.5px; }
        .meta-value { font-size: 1.25rem; font-weight: 600; color: #1a1a2e; }
        .gate-pass { color: #28a745; }
        .gate-fail { color: #dc3545; }
        .plot-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(400px, 1fr)); gap: 20px; margin-top: 20px; }
        .plot-card { background: #fff; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); padding: 20px; }
        .plot-card img { width: 100%; height: auto; border-radius: 4px; }
        .violations { background: #fff5f5; border: 1px solid #fc8181; border-radius: 8px; padding: 15px; margin-top: 20px; }
        .violations h3 { color: #c53030; margin-top: 0; }
        .violation-item { padding: 8px 0; border-bottom: 1px solid #fed7d7; }
        .violation-item:last-child { border-bottom: none; }
        table { width: 100%; border-collapse: collapse; margin-top: 15px; }
        th, td { padding: 10px; text-align: left; border-bottom: 1px solid #e2e8f0; }
        th { background: #f7fafc; font-weight: 600; color: #2d3748; }
        .footer { margin-top: 40px; padding-top: 20px; border-top: 1px solid #e2e8f0; color: #718096; font-size: 0.875rem; }
    </style>
</head>
<body>
    <h1>🎯 Balance Probe Report: {{ scenario }}</h1>
    
    <div class="meta">
        <div class="meta-grid">
            <div class="meta-item">
                <div class="meta-label">Scenario</div>
                <div class="meta-value">{{ scenario }}</div>
            </div>
            <div class="meta-item">
                <div class="meta-label">Seeds Run</div>
                <div class="meta-value">{{ n_seeds }}</div>
            </div>
            <div class="meta-item">
                <div class="meta-label">Timestamp</div>
                <div class="meta-value">{{ timestamp }}</div>
            </div>
            <div class="meta-item">
                <div class="meta-label">CI Gate</div>
                <div class="meta-value {% if gate_passed %}gate-pass{% else %}gate-fail{% endif %}">
                    {% if gate_passed %}✅ PASS{% else %}❌ FAIL{% endif %}
                </div>
            </div>
        </div>
    </div>

    <h2>📊 Key Metrics (p50 / p95)</h2>
    <table>
        <thead>
            <tr><th>Metric</th><th>Mean ± Std</th><th>Median (p50)</th><th>p95</th><th>Min</th><th>Max</th><th>Band</th></tr>
        </thead>
        <tbody>
            {% for row in metric_rows %}
            <tr>
                <td>{{ row.name }}</td>
                <td>{{ row.mean }} ± {{ row.std }}</td>
                <td>{{ row.p50 }}</td>
                <td>{{ row.p95 }}</td>
                <td>{{ row.min }}</td>
                <td>{{ row.max }}</td>
                <td>{{ row.band }}</td>
            </tr>
            {% endfor %}
        </tbody>
    </table>

    <h2>🦠 Lice Peak Distribution</h2>
    <table>
        <thead><tr><th>Category</th><th>Count</th><th>Percentage</th></tr></thead>
        <tbody>
            {% for cat, count in lice_dist.items() %}
            <tr><td>{{ cat.capitalize() }}</td><td>{{ count }}</td><td>{{ count / n_seeds * 100 }}%</td></tr>
            {% endfor %}
        </tbody>
    </table>

    <h2>📈 Distribution Plots</h2>
    <div class="plot-grid">
        <div class="plot-card">
            <h3>Mortality % per Cycle</h3>
            <img src="data:image/png;base64,{{ plot_mortality }}" alt="Mortality box plot">
        </div>
        <div class="plot-card">
            <h3>FCR</h3>
            <img src="data:image/png;base64,{{ plot_fcr }}" alt="FCR box plot">
        </div>
        <div class="plot-card">
            <h3>Realized Price (NOK/kg)</h3>
            <img src="data:image/png;base64,{{ plot_price }}" alt="Price box plot">
        </div>
        <div class="plot-card">
            <h3>Ending Equity (NOK)</h3>
            <img src="data:image/png;base64,{{ plot_equity }}" alt="Equity box plot">
        </div>
        <div class="plot-card">
            <h3>Peak Lice per Fish</h3>
            <img src="data:image/png;base64,{{ plot_lice }}" alt="Lice box plot">
        </div>
        <div class="plot-card">
            <h3>Sustainability Score</h3>
            <img src="data:image/png;base64,{{ plot_sustainability }}" alt="Sustainability box plot">
        </div>
        <div class="plot-card">
            <h3>Storm Escape Events</h3>
            <img src="data:image/png;base64,{{ plot_storm }}" alt="Storm box plot">
        </div>
        <div class="plot-card">
            <h3>Days to Harvest</h3>
            <img src="data:image/png;base64,{{ plot_days }}" alt="Days box plot">
        </div>
    </div>

    {% if violations %}
    <div class="violations">
        <h3>⚠️ CI Gate Violations (p95)</h3>
        {% for v in violations %}
        <div class="violation-item">
            <strong>{{ v.metric }}</strong>: p95 = {{ "%.2f"|format(v.p95) }} 
            (band: {{ "%.2f"|format(v.band[0]) }} – {{ "%.2f"|format(v.band[1]) }}) 
            <span style="color: #dc3545;">{{ v.direction }}</span>
        </div>
        {% endfor %}
    </div>
    {% endif %}

    <div class="footer">
        Generated by sim-balance-probe • {{ timestamp }} • Lakskongen Sim
    </div>
</body>
</html>
''')


def generate_csv(metrics_list: List[CareerMetrics], scenario: str, out_dir: Optional[Path] = None) -> Path:
    """Write metrics to CSV."""
    out_dir = out_dir or get_out_dir()
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    csv_path = out_dir / CSV_PATTERN.format(scenario=scenario, timestamp=timestamp)

    df = pd.DataFrame([m.to_dict() for m in metrics_list])
    df.to_csv(csv_path, index=False)
    print(f"[report] CSV written: {csv_path}")
    return csv_path


def _create_box_plot(data: List[float], title: str, ylabel: str, band: Optional[tuple] = None) -> str:
    """Create a box plot and return as base64 PNG."""
    import base64
    from io import BytesIO

    fig, ax = plt.subplots(figsize=(6, 4))
    
    # Box plot
    bp = ax.boxplot(data, vert=True, patch_artist=True, widths=0.5)
    bp['boxes'][0].set_facecolor('#e94560')
    bp['boxes'][0].set_alpha(0.7)
    bp['medians'][0].set_color('white')
    bp['medians'][0].set_linewidth(2)
    
    # Band shading if provided
    if band:
        ax.axhspan(band[0], band[1], alpha=0.15, color='green', label=f'Band: {band[0]:.1f}–{band[1]:.1f}')
        ax.legend(loc='upper right', fontsize=8)
    
    # Individual points (jittered)
    y = np.array(data)
    x = np.random.normal(1, 0.04, size=len(y))
    ax.scatter(x, y, alpha=0.3, s=10, color='#1a1a2e', zorder=3)
    
    ax.set_title(title, fontsize=11, fontweight='bold')
    ax.set_ylabel(ylabel, fontsize=10)
    ax.set_xticks([])
    ax.grid(True, axis='y', alpha=0.3)
    ax.spines['top'].set_visible(False)
    ax.spines['right'].set_visible(False)
    
    plt.tight_layout()
    
    buf = BytesIO()
    fig.savefig(buf, format='png', dpi=150, bbox_inches='tight')
    plt.close(fig)
    buf.seek(0)
    return base64.b64encode(buf.read()).decode('utf-8')


def _create_violin_plot(data: List[float], title: str, ylabel: str, band: Optional[tuple] = None) -> str:
    """Create a violin plot and return as base64 PNG."""
    import base64
    from io import BytesIO

    fig, ax = plt.subplots(figsize=(6, 4))
    
    parts = ax.violinplot(data, positions=[1], widths=0.6, showmeans=True, showmedians=True)
    for pc in parts['bodies']:
        pc.set_facecolor('#e94560')
        pc.set_alpha(0.7)
        pc.set_edgecolor('#1a1a2e')
    
    if band:
        ax.axhspan(band[0], band[1], alpha=0.15, color='green')
    
    ax.set_title(title, fontsize=11, fontweight='bold')
    ax.set_ylabel(ylabel, fontsize=10)
    ax.set_xticks([])
    ax.grid(True, axis='y', alpha=0.3)
    ax.spines['top'].set_visible(False)
    ax.spines['right'].set_visible(False)
    
    plt.tight_layout()
    
    buf = BytesIO()
    fig.savefig(buf, format='png', dpi=150, bbox_inches='tight')
    plt.close(fig)
    buf.seek(0)
    return base64.b64encode(buf.read()).decode('utf-8')


def generate_html_report(
    metrics_list: List[CareerMetrics],
    scenario: str,
    bands: Dict[str, tuple],
    out_dir: Optional[Path] = None,
) -> Path:
    """Generate HTML report with box plots."""
    out_dir = out_dir or get_out_dir()
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    html_path = out_dir / HTML_PATTERN.format(scenario=scenario, timestamp=timestamp)

    # Aggregate stats
    agg = aggregate_scenario_metrics(metrics_list)
    gate_result = check_ci_gate(metrics_list, bands)

    # Prepare metric rows for table
    metric_defs = [
        ("Mortality %/cycle", "mortality_pct_per_cycle", "%", bands.get("mortality_pct_per_cycle")),
        ("FCR", "fcr", "", bands.get("fcr")),
        ("Realized Price (NOK/kg)", "realized_price_per_kg", "NOK/kg", bands.get("realized_price_per_kg")),
        ("Ending Equity (NOK)", "ending_equity_nok", "NOK", bands.get("ending_equity_nok")),
        ("Peak Lice/Fish", "peak_lice_per_fish", "", None),
        ("Sustainability Score", "sustainability_score", "/100", None),
        ("Storm Escape Events", "storm_escape_events", "", None),
        ("Days to Harvest", "days_to_harvest", "days", None),
        ("Harvested (kg)", "harvested_kg", "kg", None),
    ]

    metric_rows = []
    for name, field, unit, band in metric_defs:
        mean_key = f"{field}_mean"
        std_key = f"{field}_std"
        p50_key = f"{field}_p50"
        p95_key = f"{field}_p95"
        min_key = f"{field}_min"
        max_key = f"{field}_max"
        
        metric_rows.append({
            "name": name,
            "mean": f"{agg.get(mean_key, 0):.2f}" if mean_key in agg else "N/A",
            "std": f"{agg.get(std_key, 0):.2f}" if std_key in agg else "N/A",
            "p50": f"{agg.get(p50_key, 0):.2f}" if p50_key in agg else "N/A",
            "p95": f"{agg.get(p95_key, 0):.2f}" if p95_key in agg else "N/A",
            "min": f"{agg.get(min_key, 0):.2f}" if min_key in agg else "N/A",
            "max": f"{agg.get(max_key, 0):.2f}" if max_key in agg else "N/A",
            "band": f"{band[0]:.1f}–{band[1]:.1f} {unit}" if band else "—",
        })

    # Lice distribution
    lice_dist = {
        "low": agg.get("lice_peak_low_count", 0),
        "medium": agg.get("lice_peak_medium_count", 0),
        "high": agg.get("lice_peak_high_count", 0),
        "critical": agg.get("lice_peak_critical_count", 0),
    }

    # Generate plots
    print(f"[report] Generating plots for {scenario}...")
    plots = {
        "plot_mortality": _create_box_plot(
            [m.mortality_pct_per_cycle for m in metrics_list],
            "Mortality % per Cycle", "Mortality %",
            bands.get("mortality_pct_per_cycle"),
        ),
        "plot_fcr": _create_box_plot(
            [m.fcr for m in metrics_list],
            "FCR", "FCR",
            bands.get("fcr"),
        ),
        "plot_price": _create_box_plot(
            [m.realized_price_per_kg for m in metrics_list],
            "Realized Price (NOK/kg)", "NOK/kg",
            bands.get("realized_price_per_kg"),
        ),
        "plot_equity": _create_box_plot(
            [m.ending_equity_nok for m in metrics_list],
            "Ending Equity (NOK)", "NOK",
            bands.get("ending_equity_nok"),
        ),
        "plot_lice": _create_box_plot(
            [m.peak_lice_per_fish for m in metrics_list],
            "Peak Lice per Fish", "Adult ♀/fish",
            None,
        ),
        "plot_sustainability": _create_box_plot(
            [m.sustainability_score for m in metrics_list],
            "Sustainability Score", "Score /100",
            None,
        ),
        "plot_storm": _create_box_plot(
            [m.storm_escape_events for m in metrics_list],
            "Storm Escape Events", "Expected failures",
            None,
        ),
        "plot_days": _create_box_plot(
            [m.days_to_harvest for m in metrics_list],
            "Days to Harvest", "Days",
            None,
        ),
    }

    # Render HTML
    html = HTML_TEMPLATE.render(
        scenario=scenario,
        n_seeds=len(metrics_list),
        timestamp=datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
        gate_passed=gate_result["passed"],
        metric_rows=metric_rows,
        lice_dist=lice_dist,
        violations=gate_result["violations"],
        **plots,
    )

    html_path.write_text(html, encoding="utf-8")
    print(f"[report] HTML written: {html_path}")
    return html_path


def generate_all_reports(
    all_results: Dict[str, List[CareerMetrics]],
    bands: Dict[str, tuple],
    out_dir: Optional[Path] = None,
) -> Dict[str, Dict[str, Path]]:
    """Generate CSV + HTML for all scenarios."""
    out_dir = out_dir or get_out_dir()
    outputs = {}

    for scenario, metrics_list in all_results.items():
        if not metrics_list:
            continue
        csv_path = generate_csv(metrics_list, scenario, out_dir)
        html_path = generate_html_report(metrics_list, scenario, bands, out_dir)
        outputs[scenario] = {"csv": csv_path, "html": html_path}

    return outputs