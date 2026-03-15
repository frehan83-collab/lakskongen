#!/usr/bin/env python3
"""T66: Add tooltips to UI elements."""

import re
from pathlib import Path

UI_DIR = Path("godot/scripts/ui")

# Tooltip additions (find pattern, add tooltip after it)
TOOLTIP_ADDITIONS = {
    "EncyclopediaPanel.gd": [
        {
            'after': 'title_lbl.text = tr("ui.encyclopedia.title")',
            'add': '\ttitle_lbl.tooltip_text = tr("ui.encyclopedia.tooltip")',
        },
    ],
    "MapPanel.gd": [
        {
            'after': 'title.text = tr("ui.map.title")',
            'add': '\ttitle.tooltip_text = tr("ui.map.tooltip")',
        },
    ],
    "ModManagerPanel.gd": [
        {
            'after': 'title_lbl.text = tr("ui.modmanager.title")',
            'add': '\ttitle_lbl.tooltip_text = tr("ui.modmanager.tooltip")',
        },
    ],
    "SustainabilityPanel.gd": [
        {
            'after': 'header.text = tr("ui.sustainability.header")',
            'add': '\theader.tooltip_text = tr("ui.sustainability.tooltip")',
        },
    ],
}

def add_tooltips():
    """Add tooltip lines to UI files."""
    total_added = 0
    
    for filename, additions in TOOLTIP_ADDITIONS.items():
        filepath = UI_DIR / filename
        
        if not filepath.exists():
            print(f"!! {filename} not found, skipping")
            continue
        
        content = filepath.read_text(encoding='utf-8')
        lines = content.split('\n')
        added = 0
        
        for addition in additions:
            after_pattern = addition['after']
            tooltip_line = addition['add']
            
            # Find the line matching 'after' and insert tooltip after it
            for i, line in enumerate(lines):
                if after_pattern in line and tooltip_line not in '\n'.join(lines):
                    # Insert tooltip line after current line
                    lines.insert(i + 1, tooltip_line)
                    added += 1
                    break
        
        if added > 0:
            filepath.write_text('\n'.join(lines), encoding='utf-8')
            print(f"OK {filename}: {added} tooltips added")
            total_added += added
        else:
            print(f"-- {filename}: tooltips already added or pattern changed")
    
    print(f"\nTotal tooltips added: {total_added}")

if __name__ == "__main__":
    add_tooltips()
