#!/usr/bin/env python3
"""T66: Fix final batch of hardcoded strings."""

import re
from pathlib import Path

UI_DIR = Path("godot/scripts/ui")

FINAL_FIXES = {
    "ContractsPanel.gd": [
        ('spot_price_label.text = "Spot price: %s NOK/kg" % sim_bridge.format_nok(spot_price)',
         'spot_price_label.text = tr("ui.contracts.spot_price") % sim_bridge.format_nok(spot_price)'),
        ('spot_vs_contract_label.text = "Avg contract: %s NOK/kg (%+.1f%%)" % [sim_bridge.format_nok(avg_contract_price), diff_pct]',
         'spot_vs_contract_label.text = tr("ui.contracts.avg_contract") % [sim_bridge.format_nok(avg_contract_price), diff_pct]'),
        ('spot_vs_contract_label.text = "No active contracts"',
         'spot_vs_contract_label.text = tr("ui.contracts.no_active")'),
        ('label.text = "No active contracts"',
         'label.text = tr("ui.contracts.no_active")'),
        ('quantity_label.text = "%s/%s kg (%d%%)" % [',
         'quantity_label.text = tr("ui.contracts.quantity_format") % ['),
    ],
    
    "EncyclopediaPanel.gd": [
        ('_entry_count_label.text = "📋 %d 📋" % visible.size()',
         '_entry_count_label.text = tr("ui.encyclopedia.entry_count") % visible.size()'),
        ('lbl.text = "Select a category from the sidebar, then choose an entry to read detailed information about salmon farming in Norway."',
         'lbl.text = tr("ui.encyclopedia.select_info")'),
    ],
    
    "FinancePanel.gd": [
        ('bal_lbl.text = "Bal: %s" % _format_nok_short(entry_balance)',
         'bal_lbl.text = tr("ui.finance.balance") % _format_nok_short(entry_balance)'),
    ],
    
    "MapPanel.gd": [
        ('_detail_name.text = "Select a region"',
         '_detail_name.text = tr("ui.map.select_region")'),
        ('_detail_zone.text = "Click a region on the map"',
         '_detail_zone.text = tr("ui.map.click_prompt")'),
        ('_detail_desc.text = "Choose a region from the map to see licensing availability, water conditions, disease risk, and logistics information."',
         '_detail_desc.text = tr("ui.map.region_desc")'),
    ],
    
    "ModManagerPanel.gd": [
        ('status_lbl.text = "⚪ Deaktivert"',
         'status_lbl.text = tr("ui.modmanager.status_disabled")'),
        ('_detail_author.text  = "Forfatter: " + mod.get("author", "Ukjent")',
         '_detail_author.text  = tr("ui.modmanager.author_format") % mod.get("author", tr("ui.modmanager.author_unknown"))'),
    ],
    
    "ScenarioSelectPanel.gd": [
        ('win_lbl.text = "🎯 " + s["win"]',
         'win_lbl.text = tr("ui.scenario.win_icon") + " " + s["win"]'),
        ('btn.text = "🔒 LÅST"',
         'btn.text = tr("ui.scenario.locked_btn")'),
        ('btn.text = "▶️ START SCENARIO"',
         'btn.text = tr("ui.scenario.start_btn")'),
    ],
    
    "SettingsMenu.gd": [
        ('speed_val.text = "%.1fx" % speed_slider.value',
         'speed_val.text = tr("ui.settings.speed_format") % speed_slider.value'),
        ('apply_button.text = "Lagret ✓"',
         'apply_button.text = tr("ui.settings.saved")'),
        ('tween.tween_callback(func(): apply_button.text = "Bruk")',
         'tween.tween_callback(func(): apply_button.text = tr("ui.settings.apply"))'),
    ],
    
    "TechTreePanel.gd": [
        ('cost_lbl.text = "%s 🕐 %dd" % [cost_str, research_days]',
         'cost_lbl.text = tr("ui.tech.cost_format") % [cost_str, research_days]'),
    ],
    
    "TutorialUI.gd": [
        ('step_label.text = "Fullført ✓"',
         'step_label.text = tr("ui.tutorial.completed")'),
    ],
}

def apply_final_fixes():
    """Apply final replacements."""
    total = 0
    
    for filename, fixes in FINAL_FIXES.items():
        filepath = UI_DIR / filename
        
        if not filepath.exists():
            print(f"!! {filename} not found")
            continue
        
        content = filepath.read_text(encoding='utf-8')
        count = 0
        
        for old, new in fixes:
            if old in content:
                content = content.replace(old, new)
                count += 1
        
        if count > 0:
            filepath.write_text(content, encoding='utf-8')
            print(f"OK {filename}: {count} fixes")
            total += count
        else:
            print(f"-- {filename}: already fixed")
    
    print(f"\nTotal: {total} fixes")

if __name__ == "__main__":
    apply_final_fixes()
