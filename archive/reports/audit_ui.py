#!/usr/bin/env python3
"""Quick audit script to scan all UI files for common issues."""

import re
import sys
from pathlib import Path

UI_DIR = Path("godot/scripts/ui")

def audit_file(filepath):
    """Audit a single .gd file for UI polish issues."""
    issues = []
    
    with open(filepath, 'r', encoding='utf-8', errors='replace') as f:
        content = f.read()
        lines = content.split('\n')
    
    # 1. Check for hardcoded strings (not using tr())
    hardcoded = []
    for i, line in enumerate(lines, 1):
        # Look for .text = "something" that doesn't use tr()
        if re.search(r'\.text\s*=\s*"[^"]*"', line) and 'tr(' not in line:
            # Skip version strings, empty strings, and numeric/symbol strings
            if not re.search(r'\.text\s*=\s*""', line) and \
               not re.search(r'v\d+\.\d+\.\d+', line) and \
               not re.search(r'\.text\s*=\s*"[-+]?\d', line) and \
               not re.search(r'\.text\s*=\s*"[^\w\s]{1,3}"', line):
                hardcoded.append(f"Line {i}: {line.strip()}")
    
    if hardcoded:
        issues.append(("Hardcoded strings (not using tr())", hardcoded[:10]))  # Limit to 10
    
    # 2. Check for tooltip_text usage
    tooltip_count = content.count('tooltip_text')
    if tooltip_count < 3:
        issues.append(("Low tooltip count", [f"Only {tooltip_count} tooltip(s) found"]))
    
    # 3. Check for keyboard nav (_input, ui_cancel, etc.)
    has_input_handler = '_input(' in content or '_unhandled_input(' in content
    has_ui_cancel = 'ui_cancel' in content
    
    if not has_input_handler:
        issues.append(("Missing keyboard navigation", ["No _input() or _unhandled_input() handler"]))
    elif not has_ui_cancel:
        issues.append(("Missing Esc handling", ["No 'ui_cancel' handling found"]))
    
    return issues

def main():
    print("=== T60 UI Audit ===\n")
    
    all_files = sorted(UI_DIR.glob("*.gd"))
    total_issues = 0
    
    for filepath in all_files:
        issues = audit_file(filepath)
        
        if issues:
            print(f"FILE: {filepath.name}")
            for category, items in issues:
                print(f"  WARNING: {category}:")
                for item in items[:5]:  # Limit display
                    # Remove emoji/special chars for Windows console
                    clean_item = item.encode('ascii', 'ignore').decode('ascii')
                    print(f"      {clean_item}")
            print()
            total_issues += sum(len(items) for _, items in issues)
    
    print(f"\nAudit complete. Found {total_issues} potential issues across {len(all_files)} files.")

if __name__ == "__main__":
    main()
