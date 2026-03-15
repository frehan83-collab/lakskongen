#!/usr/bin/env python3
"""T66: Fix remaining 121 UI issues with translation keys and tooltips."""

import re
from pathlib import Path

UI_DIR = Path("godot/scripts/ui")

# Mapping of hardcoded strings to translation keys
REPLACEMENTS = {
    # AnalyticsPanel
    "AnalyticsPanel.gd": [
        ('title_lbl.text = "Analytics"', 'title_lbl.text = tr("ui.analytics.title")'),
    ],
    
    # ContractsPanel
    "ContractsPanel.gd": [
        ('header.text = "📋 Contracts & Market Depth"', 'header.text = tr("ui.contracts.header")'),
        ('active_header.text = "Active Contracts"', 'active_header.text = tr("ui.contracts.active_header")'),
        ('offers_header.text = "Available Offers"', 'offers_header.text = tr("ui.contracts.offers_header")'),
        ('export_header.text = "Export Markets"', 'export_header.text = tr("ui.contracts.export_header")'),
        ('refresh_button.text = "Refresh"', 'refresh_button.text = tr("ui.contracts.refresh")'),
    ],
    
    # DataExportPanel
    "DataExportPanel.gd": [
        ('log_label.text = "Eksportlogg"', 'log_label.text = tr("ui.export.log_label")'),
        ('title.text = "Dataeksport"', 'title.text = tr("ui.export.title")'),
        ('_status_label.text = "Klar"', '_status_label.text = tr("ui.export.status_ready")'),
        ('lbl.text = "Eksportmappe"', 'lbl.text = tr("ui.export.folder_label")'),
    ],
    
    # EncyclopediaPanel
    "EncyclopediaPanel.gd": [
        ('title_lbl.text = "📚 LAKSKONGEN ENCYCLOPEDIA"', 'title_lbl.text = tr("ui.encyclopedia.title")'),
        ('cat_header.text = "📑 CATEGORIES"', 'cat_header.text = tr("ui.encyclopedia.categories_header")'),
        ('entry_header.text = "📄 ENTRIES"', 'entry_header.text = tr("ui.encyclopedia.entries_header")'),
        ('related_label.text = "SEE ALSO"', 'related_label.text = tr("ui.encyclopedia.see_also")'),
        ('_title_label.text = "Welcome to the Encyclopedia"', '_title_label.text = tr("ui.encyclopedia.welcome")'),
    ],
    
    # EventLog
    "EventLog.gd": [
        ('welcome.text = "[color=gray]Event log cleared. New events will appear here.[/color]"',
         'welcome.text = tr("ui.eventlog.cleared")'),
    ],
    
    # FinancePanel
    "FinancePanel.gd": [
        ('loan_status_label.text = "Active" if has_loan else "No Active Loan"',
         'loan_status_label.text = tr("ui.finance.loan_active") if has_loan else tr("ui.finance.loan_none")'),
        ('loan_interest_label.text = "%.2f%%" % (loan_info.get("interest_rate", 0.0) * 100)',
         'loan_interest_label.text = tr("ui.finance.loan_interest_format") % (loan_info.get("interest_rate", 0.0) * 100)'),
        ('day_lbl.text = "Day %d" % day',
         'day_lbl.text = tr("ui.finance.history_day") % day'),
        ('rev_lbl.text = "Rev: %s" % _format_nok_short(entry_revenue)',
         'rev_lbl.text = tr("ui.finance.history_revenue") % _format_nok_short(entry_revenue)'),
        ('exp_lbl.text = "Exp: %s" % _format_nok_short(entry_expenses)',
         'exp_lbl.text = tr("ui.finance.history_expenses") % _format_nok_short(entry_expenses)'),
    ],
    
    # GeneticsPanel
    "GeneticsPanel.gd": [
        ('h2_label.text = "h² %.2f" % h2',
         'h2_label.text = tr("ui.genetics.h2_format") % h2'),
        ('bv_label.text = "%+.2f" % bv if bv != 0.0 else "0.00"',
         'bv_label.text = tr("ui.genetics.bv_format") % bv if bv != 0.0 else "0.00"'),
        ('cv_label.text = "%.2f" % trait_data.get("current_value", 0.0)',
         'cv_label.text = tr("ui.genetics.cv_format") % trait_data.get("current_value", 0.0)'),
        ('inbreeding_label.text = "Inbreeding: %.1f%%" % (coef * 100.0)',
         'inbreeding_label.text = tr("ui.genetics.inbreeding") % (coef * 100.0)'),
    ],
    
    # HUD
    "HUD.gd": [
        ('balance_value.text = "%s NOK" % no_data',
         'balance_value.text = tr("ui.hud.balance_format") % no_data'),
    ],
    
    # MapPanel
    "MapPanel.gd": [
        ('title.text = "SELECT REGION 🗺️ NORWEGIAN COASTLINE"',
         'title.text = tr("ui.map.title")'),
        ('subtitle.text = "Click a region to view details and place a new sea site"',
         'subtitle.text = tr("ui.map.subtitle")'),
        ('map_title.text = "NORWAY"',
         'map_title.text = tr("ui.map.norway")'),
        ('compass.text = "N\\n↑"',
         'compass.text = tr("ui.map.compass_n")'),
    ],
    
    # ModManagerPanel
    "ModManagerPanel.gd": [
        ('title_lbl.text = "Modbehandler"',
         'title_lbl.text = tr("ui.modmanager.title")'),
        ('_detail_name.text = "Ingen mod valgt"',
         '_detail_name.text = tr("ui.modmanager.no_selection")'),
        ('empty_lbl.text = "Ingen mods funnet.\\nPlasser mods i mappen mods/."',
         'empty_lbl.text = tr("ui.modmanager.empty")'),
        ('status_lbl.text = "❌ Feil"',
         'status_lbl.text = tr("ui.modmanager.status_error")'),
        ('status_lbl.text = "✅ Aktiv"',
         'status_lbl.text = tr("ui.modmanager.status_active")'),
    ],
    
    # NotificationCenter
    "NotificationCenter.gd": [
        ('day_lbl.text = "Dag %d" % entry.get("day", 0)',
         'day_lbl.text = tr("ui.notification.day_format") % entry.get("day", 0)'),
    ],
    
    # ProductionChainPanel
    "ProductionChainPanel.gd": [
        ('quantity_label.text = "%d markets" % int(quantity)',
         'quantity_label.text = tr("ui.production.markets_format") % int(quantity)'),
    ],
    
    # SaveLoadMenu
    "SaveLoadMenu.gd": [
        ('info_label.text = "%s - Day %d - $%d\\n%s" % [data.name, data.day, data.cash, data.date]',
         'info_label.text = tr("ui.saveload.info_format") % [data.name, data.day, data.cash, data.date]'),
    ],
    
    # ScenarioSelectPanel
    "ScenarioSelectPanel.gd": [
        ('title_lbl.text = "Velg Scenario"',
         'title_lbl.text = tr("ui.scenario.title")'),
        ('subtitle_lbl.text = "Fem norske eventyr venter – velg din utfordring"',
         'subtitle_lbl.text = tr("ui.scenario.subtitle")'),
        ('diff_prefix.text = "Vanskelighet 🎯"',
         'diff_prefix.text = tr("ui.scenario.difficulty")'),
        ('region_lbl.text = "📍" + s["region"]',
         'region_lbl.text = tr("ui.scenario.region_icon") + s["region"]'),
        ('cap_lbl.text = "💰" + s["capital"]',
         'cap_lbl.text = tr("ui.scenario.capital_icon") + s["capital"]'),
    ],
    
    # SettingsMenu
    "SettingsMenu.gd": [
        ('master_pct.text  = "%d%%" % int(master_slider.value)',
         'master_pct.text  = tr("ui.settings.volume_format") % int(master_slider.value)'),
        ('music_pct.text   = "%d%%" % int(music_slider.value)',
         'music_pct.text   = tr("ui.settings.volume_format") % int(music_slider.value)'),
        ('ambient_pct.text = "%d%%" % int(ambient_slider.value)',
         'ambient_pct.text = tr("ui.settings.volume_format") % int(ambient_slider.value)'),
        ('sfx_pct.text     = "%d%%" % int(sfx_slider.value)',
         'sfx_pct.text     = tr("ui.settings.volume_format") % int(sfx_slider.value)'),
    ],
    
    # SustainabilityPanel
    "SustainabilityPanel.gd": [
        ('header.text = "🌱 Sustainability Score"',
         'header.text = tr("ui.sustainability.header")'),
        ('breakdown_header.text = "Category Breakdown"',
         'breakdown_header.text = tr("ui.sustainability.breakdown")'),
        ('score_label.text = "%d/100" % score',
         'score_label.text = tr("ui.sustainability.score_format") % score'),
        ('score_text.text = "%d/%d" % [score, max_score]',
         'score_text.text = tr("ui.sustainability.category_score") % [score, max_score]'),
    ],
    
    # TechTreePanel
    "TechTreePanel.gd": [
        ('hdr.text = "%s %s" % [meta["icon"], meta["label"]]',
         'hdr.text = tr("ui.tech.tier_node") % [meta["icon"], meta["label"]]'),
        ('tier_lbl.text = "T%d" % tier',
         'tier_lbl.text = tr("ui.tech.tier_label") % tier'),
        ('done_lbl.text = "Fullført"',
         'done_lbl.text = tr("ui.tech.completed")'),
        ('prog_lbl.text = "Dag %d / %d" % [progress_days, research_days]',
         'prog_lbl.text = tr("ui.tech.progress_days") % [progress_days, research_days]'),
        ('cost_lbl.text = "%s 🕐 %dd" % [cost_str, research_days]',
         'cost_lbl.text = tr("ui.tech.cost_days") % [cost_str, research_days]'),
    ],
    
    # TutorialUI
    "TutorialUI.gd": [
        ('step_label.text = "Steg %d / %d" % [step_num, total]',
         'step_label.text = tr("ui.tutorial.step_format") % [step_num, total]'),
        ('reward_label.text    = "✨ Steg %d fullført! 💰 +%s NOK" % [step_number, _format_nok(reward_nok)]',
         'reward_label.text    = tr("ui.tutorial.step_complete") % [step_number, _format_nok(reward_nok)]'),
        ('reward_label.text    = "✨ %s" % title',
         'reward_label.text    = tr("ui.tutorial.achievement") % title'),
        ('title_label.text = "🎉 Tutorial fullført!"',
         'title_label.text = tr("ui.tutorial.complete")'),
        ('hint_label.text = "Utforsk Expansion, Genetics og Politics for å vokse videre."',
         'hint_label.text = tr("ui.tutorial.hint_next")'),
    ],
    
    # AchievementPopup
    "AchievementPopup.gd": [
        ('_reward_label.text    = "+ %s NOK" % _format_nok(reward_nok)',
         '_reward_label.text    = tr("ui.achievement.reward_format") % _format_nok(reward_nok)'),
        ('header.text = "ACHIEVEMENT UNLOCKED"',
         'header.text = tr("ui.achievement.unlocked")'),
    ],
    
    # Dashboard
    "Dashboard.gd": [
        ('lbl.text = "[Dag %d] %s (%s%s)" % [',
         'lbl.text = tr("ui.dashboard.event_format") % ['),
    ],
}

def apply_fixes():
    """Apply all replacements to UI files."""
    total_fixes = 0
    
    for filename, replacements in REPLACEMENTS.items():
        filepath = UI_DIR / filename
        
        if not filepath.exists():
            print(f"!! {filename} not found, skipping")
            continue
        
        content = filepath.read_text(encoding='utf-8')
        original_content = content
        fixes_applied = 0
        
        for old_text, new_text in replacements:
            if old_text in content:
                content = content.replace(old_text, new_text)
                fixes_applied += 1
        
        if fixes_applied > 0:
            filepath.write_text(content, encoding='utf-8')
            print(f"OK {filename}: {fixes_applied} fixes applied")
            total_fixes += fixes_applied
        else:
            print(f"-- {filename}: already fixed or patterns changed")
    
    print(f"\nTotal fixes applied: {total_fixes}")

if __name__ == "__main__":
    apply_fixes()
