extends Control

# RecoveryDashboard - Progress Tracking Widget
# Position: Bottom-left HUD anchored above main dashboard

@onready var progress_bar: ProgressBar = $ProgressBar
@onready var milestones_label: RichTextLabel = $Milestones

var crisis_manager: Node = null

func _ready():
    crisis_manager = get_node("/root/CrisisResponseManager")
    crisis_manager.connect("recovery_progress_changed",Callable(self,"_on_recovery_progress"))
    hide()

func _on_recovery_progress(crisis_id: String, progress: float):
    var crisis = crisis_manager.get_crisis(crisis_id)
    progress_bar.value = progress * 100
    
    var milestone_text = ""
    for milestone in crisis.RecoveryMilestones:
        milestone_text += "[color=#" + LakskongenTheme.SUCCESS.to_html() + "]»[/color] " + milestone + "\n"
    
    milestones_label.text = milestone_text
    show()

func _on_crisis_resolved(_crisis_id: String, _success: bool):
    hide()
    progress_bar.value = 0
    milestones_label.text = ""tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
