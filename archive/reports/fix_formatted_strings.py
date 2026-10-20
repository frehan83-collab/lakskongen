#!/usr/bin/env python3
"""
Fix formatted hardcoded strings (strings with % or format())
These need manual wrapping: tr("Base %s").format() or tr("Base %s") % value
"""

import re
from pathlib import Path

GODOT_UI_DIR = Path("godot/scripts/ui")

FIXES = {
    "ContractsPanel.gd": [
        (r'l\.text = "U%d" % \(i \+ 1\)',
         r'l.text = tr("U%d") % (i + 1)'),
        (r'_kpi_spot\.text = "%.2f NOK/kg" % spot',
         r'_kpi_spot.text = tr("%.2f NOK/kg") % spot'),
        (r'_kpi_contract\.text = "%.2f NOK/kg" % \(weekly \* 0\.93\)',
         r'_kpi_contract.text = tr("%.2f NOK/kg") % (weekly * 0.93)'),
        (r'_kpi_volume\.text = "%.0f tonn" % \(_sim\.fish_biomass_kg / 1000\.0 \* 0\.2\)',
         r'_kpi_volume.text = tr("%.0f tonn") % (_sim.fish_biomass_kg / 1000.0 * 0.2)'),
    ],
    "CrewPanel.gd": [
        (r'_kpi_payroll\.text = "%.0f kNOK" % \(total_salary / 1000\.0\) if total_salary > 0 else "--"',
         r'_kpi_payroll.text = tr("%.0f kNOK") % (total_salary / 1000.0) if total_salary > 0 else "--"'),
        (r'_kpi_morale\.text = "%d%%" % int\(total_morale / max\(crew\.size\(\), 1\)\)',
         r'_kpi_morale.text = tr("%d%%") % int(total_morale / max(crew.size(), 1))'),
        (r'xp_lbl\.text = "%d XP" % w\.get\("xp", 0\)',
         r'xp_lbl.text = tr("%d XP") % w.get("xp", 0)'),
        (r'morale_lbl\.text = "%d%%" % morale',
         r'morale_lbl.text = tr("%d%%") % morale'),
    ],
    "Dashboard.gd": [
        (r'_weather_temp\.text = "--C"',
         r'_weather_temp.text = tr("--C")'),
        (r'_kpi_health_value\.text = "%d%%" % health',
         r'_kpi_health_value.text = tr("%d%%") % health'),
        (r'pen\.weight_value\.text = "%.1f kg" % \(avg_weight \* \(0\.9 \+ randf\(\) \* 0\.2\)\)',
         r'pen.weight_value.text = tr("%.1f kg") % (avg_weight * (0.9 + randf() * 0.2))'),
        (r'pen\.temp_value\.text = "%.1fC" % \(temp \+ randf\(\) \* 0\.5 - 0\.25\)',
         r'pen.temp_value.text = tr("%.1fC") % (temp + randf() * 0.5 - 0.25)'),
        (r'pen\.count_value\.text = "Tom"',
         r'pen.count_value.text = tr("Tom")'),
    ],
    "DecisionPanel.gd": [
        (r'timer\.text = " %d days to decide" % days_remaining',
         r'timer.text = tr(" %d days to decide") % days_remaining'),
    ],
    "EncyclopediaPanel.gd": [
        (r'_entry_count_label\.text = "  %d  " % visible\.size\(\)',
         r'_entry_count_label.text = tr("  %d  ") % visible.size()'),
    ],
    "FinancePanel.gd": [
        (r'd_lbl\.text = "Dag %d" % day',
         r'd_lbl.text = tr("Dag %d") % day'),
    ],
    "MapPanel.gd": [
        (r'optimal_lbl\.text = "Optimal: 1216 C  \| Max tolerance: 20 C"',
         r'optimal_lbl.text = tr("Optimal: 12-16 C | Max tolerance: 20 C")'),
    ],
    "TechTreePanel.gd": [
        (r'cost_lbl\.text = "%s    %dd" % \[cost_str, research_days\]',
         r'cost_lbl.text = tr("%s    %dd") % [cost_str, research_days]'),
    ],
    "WeatherDiseasePanel.gd": [
        (r'weather_status_label\.text = "%s %s" % \[weather_icon, weather_state\]',
         r'weather_status_label.text = "%s %s" % [weather_icon, weather_state]'),
        (r'temperature_label\.text = "Sea Temperature: %.1fC" % temp',
         r'temperature_label.text = tr("Sea Temperature: %.1fC") % temp'),
        (r'site_label\.text = "Location: %s" % \("All sites" if site_id == "all_sites" else site_id\)',
         r'site_label.text = tr("Location: %s") % ("All sites" if site_id == "all_sites" else site_id)'),
    ],
}

def apply_fixes():
    """Apply targeted fixes to formatted strings."""
    print("=== Fixing Formatted Strings ===\n")
    
    total_fixes = 0
    for filename, patterns in FIXES.items():
        filepath = GODOT_UI_DIR / filename
        if not filepath.exists():
            print(f"SKIP: {filename} (not found)")
            continue
        
        print(f"Processing: {filename}")
        
        try:
            with open(filepath, 'r', encoding='utf-8') as f:
                content = f.read()
        except Exception as e:
            print(f"  ERROR reading: {e}")
            continue
        
        original = content
        fixes_applied = 0
        
        for pattern, replacement in patterns:
            count = len(re.findall(pattern, content))
            if count > 0:
                content = re.sub(pattern, replacement, content)
                fixes_applied += count
                print(f"  - Applied {count}x: {pattern[:50]}...")
        
        if content != original:
            try:
                with open(filepath, 'w', encoding='utf-8') as f:
                    f.write(content)
                print(f"  SAVED: {fixes_applied} fixes\n")
                total_fixes += fixes_applied
            except Exception as e:
                print(f"  ERROR writing: {e}\n")
        else:
            print(f"  No changes\n")
    
    print(f"\n=== Total Formatted String Fixes: {total_fixes} ===")

if __name__ == "__main__":
    apply_fixes()
