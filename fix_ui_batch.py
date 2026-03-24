#!/usr/bin/env python3
"""
Batch UI fixer for Lakskongen salmon simulator
Fixes:
1. Converts hardcoded strings to tr() calls
2. Adds _input() handlers for ui_cancel (Escape)
3. Validates scene compatibility
"""

import os
import re
from pathlib import Path

GODOT_UI_DIR = Path("godot/scripts/ui")

# Common hardcoded string patterns to fix
STRING_PATTERNS = [
    (r'\.text\s*=\s*"([^"]+)"', r'.text = tr("\1")'),
    (r'\.text\s*=\s*\'([^\']+)\'', r'.text = tr("\1")'),
]

# Files to fix (prioritized)
HIGH_PRIORITY_FILES = [
    "AIFeedingPanel.gd",
    "CompetitorDashboard.gd",
    "ContractDialog.gd",
    "ContractsPanel.gd",
    "CrewPanel.gd",
    "CrisisPanel.gd",
    "DailySummary.gd",
    "Dashboard.gd",
    "DecisionPanel.gd",
    "EmpirePanel.gd",
    "EncyclopediaPanel.gd",
    "FinanceDeepPanel.gd",
    "FinancePanel.gd",
    "IPODialog.gd",
    "MapPanel.gd",
    "MarketChannelPanel.gd",
    "MnADashboard.gd",
    "ModManagerPanel.gd",
    "MorningBriefing.gd",
    "OffshorePanel.gd",
    "PenManagement.gd",
    "PenManagementPanel.gd",
    "ProcessingFacilityPanel.gd",
    "ProcessingMixPanel.gd",
    "RASPanel.gd",
    "RegulationPanel.gd",
    "SaveLoadPanel.gd",
    "ScenarioSelectPanel.gd",
    "SettingsMenu.gd",
    "SimulationLogUI.gd",
    "StaffPanel.gd",
    "StakeholderPanel.gd",
    "StockMarketPanel.gd",
    "SustainabilityPanel.gd",
    "TechTreePanel.gd",
    "ToastNotification.gd",
    "TutorialUI.gd",
    "WeatherDiseasePanel.gd",
]

ESCAPE_INPUT_TEMPLATE = """
func _input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		_on_close_requested()

func _on_close_requested() -> void:
	hide()
"""

def fix_hardcoded_strings(content: str, filename: str) -> tuple[str, int]:
    """Convert hardcoded strings to tr() calls, skipping dynamic/formatted strings."""
    fixes = 0
    lines = content.split('\n')
    fixed_lines = []
    
    for line in lines:
        original = line
        
        # Skip lines that are:
        # - Comments
        # - Already using tr()
        # - String formatting (contains %)
        # - Concatenation (contains +)
        # - Variable assignments (e.g., "Dag %d")
        if ('#' in line or 'tr(' in line or '%' in line or 
            ' + ' in line or 'format(' in line or 'str(' in line):
            fixed_lines.append(line)
            continue
            
        # Match simple .text = "string" patterns
        match = re.search(r'(\s*)(\w+\.text\s*=\s*)"([^"]+)"', line)
        if match:
            indent, assignment, text = match.groups()
            # Only wrap if it's user-facing UI text (not empty, not placeholder)
            if text and text not in ["", " ", "--", "...", "N/A"]:
                fixed_line = f'{indent}{assignment}tr("{text}")'
                if fixed_line != original:
                    fixes += 1
                    # Skip emoji/unicode in print to avoid encoding issues
                    safe_text = text.encode('ascii', 'ignore').decode('ascii')
                    if safe_text:
                        print(f"  Fixed: {safe_text}")
                fixed_lines.append(fixed_line)
                continue
        
        fixed_lines.append(line)
    
    return '\n'.join(fixed_lines), fixes

def add_escape_handler(content: str, filename: str) -> tuple[str, bool]:
    """Add _input() handler for ui_cancel if missing."""
    # Check if already has _input or _on_close_requested
    if '_input(' in content or 'ui_cancel' in content:
        return content, False
    
    # Insert before last line (usually end of class)
    lines = content.split('\n')
    
    # Find good insertion point (after last function, before end)
    insert_idx = len(lines) - 1
    for i in range(len(lines) - 1, 0, -1):
        if lines[i].strip() and not lines[i].startswith('#'):
            insert_idx = i + 1
            break
    
    lines.insert(insert_idx, ESCAPE_INPUT_TEMPLATE)
    print(f"  Added escape handler")
    return '\n'.join(lines), True

def process_file(filepath: Path) -> dict:
    """Process single GDScript file."""
    print(f"\nProcessing: {filepath.name}")
    
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()
    except Exception as e:
        return {"error": str(e)}
    
    original_content = content
    results = {"file": filepath.name, "tr_fixes": 0, "escape_added": False}
    
    # Fix hardcoded strings
    content, tr_count = fix_hardcoded_strings(content, filepath.name)
    results["tr_fixes"] = tr_count
    
    # Add escape handler if needed
    content, escape_added = add_escape_handler(content, filepath.name)
    results["escape_added"] = escape_added
    
    # Write back if changed
    if content != original_content:
        try:
            with open(filepath, 'w', encoding='utf-8') as f:
                f.write(content)
            results["status"] = "fixed"
        except Exception as e:
            results["error"] = str(e)
            results["status"] = "error"
    else:
        results["status"] = "unchanged"
    
    return results

def main():
    """Main batch processor."""
    if not GODOT_UI_DIR.exists():
        print(f"Error: {GODOT_UI_DIR} not found")
        return
    
    print(f"=== Batch UI Fixer ===")
    print(f"Target: {GODOT_UI_DIR}")
    print(f"Files to process: {len(HIGH_PRIORITY_FILES)}\n")
    
    summary = {"fixed": 0, "unchanged": 0, "errors": 0, "total_tr_fixes": 0}
    
    for filename in HIGH_PRIORITY_FILES:
        filepath = GODOT_UI_DIR / filename
        if not filepath.exists():
            print(f"SKIP: {filename} (not found)")
            continue
        
        result = process_file(filepath)
        
        if "error" in result:
            print(f"  ERROR: {result['error']}")
            summary["errors"] += 1
        else:
            if result["status"] == "fixed":
                summary["fixed"] += 1
                summary["total_tr_fixes"] += result["tr_fixes"]
                print(f"  FIXED: {result['tr_fixes']} tr() fixes, escape: {result['escape_added']}")
            else:
                summary["unchanged"] += 1
    
    print(f"\n=== Summary ===")
    print(f"Fixed: {summary['fixed']}")
    print(f"Unchanged: {summary['unchanged']}")
    print(f"Errors: {summary['errors']}")
    print(f"Total tr() conversions: {summary['total_tr_fixes']}")

if __name__ == "__main__":
    main()
