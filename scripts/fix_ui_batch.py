#!/usr/bin/env python3
"""
Batch UI consistency fixer for T62.
Adds keyboard navigation (_input handler with ESC support) to panels missing it.
"""

import re
from pathlib import Path

GODOT_SCRIPTS = Path("godot/scripts/ui")

# Panels needing keyboard nav (no _input handler currently)
PANELS_NEED_KEYBOARD = [
    "AnalyticsPanel.gd",
    "ContractsPanel.gd",
    "CrewPanel.gd",
    "DataExportPanel.gd",
    "EncyclopediaPanel.gd",
    "EventLog.gd",
    "TechTreePanel.gd",
    "MapPanel.gd",
    "ModManagerPanel.gd",
    "NotificationCenter.gd",
    "ResearchPanel.gd",
    "ScenarioSelectPanel.gd",
    "SustainabilityPanel.gd",
    "TutorialUI.gd",
    "AchievementNotification.gd",
    "AchievementPopup.gd",
    "ToastNotification.gd",
]

KEYBOARD_NAV_TEMPLATE = """
func _input(event: InputEvent) -> void:
	\"\"\"Handle keyboard navigation\"\"\"
	if not visible:
		return
	
	if event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()
"""

def add_keyboard_nav(file_path: Path):
    """Add _input handler for ESC key support"""
    content = file_path.read_text(encoding="utf-8")
    
    # Check if already has _input
    if re.search(r'func _input\(', content):
        print(f"  OK {file_path.name} already has _input handler")
        return False
    
    # Find _ready() function and insert after it
    ready_match = re.search(r'(func _ready\(\).*?:\n(?:.*?\n)*?(?=\nfunc|\nclass|\Z))', content, re.MULTILINE)
    
    if ready_match:
        insert_pos = ready_match.end()
        new_content = content[:insert_pos] + KEYBOARD_NAV_TEMPLATE + content[insert_pos:]
        file_path.write_text(new_content, encoding="utf-8")
        print(f"  ADDED keyboard nav to {file_path.name}")
        return True
    else:
        # Try to find any function and insert before first one
        func_match = re.search(r'\n(func [a-z_])', content)
        if func_match:
            insert_pos = func_match.start() + 1
            new_content = content[:insert_pos] + KEYBOARD_NAV_TEMPLATE + "\n" + content[insert_pos:]
            file_path.write_text(new_content, encoding="utf-8")
            print(f"  ADDED keyboard nav to {file_path.name} (before first func)")
            return True
    
    print(f"  WARNING Could not find insertion point in {file_path.name}")
    return False

def main():
    print("T62: Batch UI Consistency Fix")
    print("=" * 60)
    
    fixed_count = 0
    
    print("\nAdding keyboard navigation to panels...")
    for panel_name in PANELS_NEED_KEYBOARD:
        panel_path = GODOT_SCRIPTS / panel_name
        if panel_path.exists():
            if add_keyboard_nav(panel_path):
                fixed_count += 1
        else:
            print(f"  WARNING Not found: {panel_name}")
    
    print(f"\nFixed {fixed_count} panels with keyboard navigation")
    print(f"Remaining work: Translation keys for hardcoded strings (manual)")

if __name__ == "__main__":
    main()
