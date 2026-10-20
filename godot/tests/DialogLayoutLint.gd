extends RefCounted

## DialogLayoutLint — the build dialog's overlap lint (roadmap F2-31
## acceptance 7): at a window size and UI scale, nothing in the dialog may
## leave the screen, leave the dialog, overlap a sibling in its row, column
## or grid, or have its one-line text cut. Controls scrolled out of the
## steps' view are skipped (the player scrolls to them); stacking containers
## (panels, margins, centres) overlap their children by design and are not
## checked as rows.


## ["what is wrong", …] for the open dialog (empty when the layout is clean).
static func problems(dialog: Control) -> PackedStringArray:
	var out := PackedStringArray()
	var screen := dialog.get_viewport_rect()
	var panel := dialog.get_node("%Dialog") as Control
	var scroll := dialog.get_node("%StepScroll") as ScrollContainer
	var box := panel.get_global_rect()
	if not screen.grow(0.5).encloses(box):
		out.append("dialog %s leaves the screen %s" % [str(box), str(screen.size)])
	var view := scroll.get_global_rect()
	for node in panel.find_children("*", "Control", true, false):
		var c := node as Control
		if not c.is_visible_in_tree() or c.size.x <= 0.0 or c.size.y <= 0.0:
			continue
		var r := c.get_global_rect()
		var in_steps := scroll.is_ancestor_of(c)
		if in_steps and not r.intersects(view):
			continue
		if not in_steps and not box.grow(0.5).encloses(r):
			out.append("%s leaves the dialog" % _path(c, panel))
		if in_steps and (r.position.x < view.position.x - 0.5 or r.end.x > view.end.x + 0.5):
			out.append("%s is wider than the steps" % _path(c, panel))
		if c is Label:
			var l := c as Label
			if l.autowrap_mode == TextServer.AUTOWRAP_OFF and not l.clip_text and l.text != "" \
					and l.get_minimum_size().x > l.size.x + 1.0:
				out.append("%s text cut" % _path(c, panel))
		if c is Button:
			var b := c as Button
			if b.text != "" and not b.clip_text and b.get_minimum_size().x > b.size.x + 1.0:
				out.append("%s caption cut" % _path(c, panel))
	for node in panel.find_children("*", "Container", true, false):
		var cont := node as Container
		if not cont.is_visible_in_tree() or not (cont is BoxContainer or cont is GridContainer or cont is FlowContainer):
			continue
		var kids: Array = cont.get_children().filter(func(k: Node) -> bool:
			return k is Control and (k as Control).is_visible_in_tree() and not (k as Control).top_level)
		for i in kids.size():
			var a := (kids[i] as Control).get_global_rect().grow(-0.5)
			if not a.has_area():
				continue
			for j in range(i + 1, kids.size()):
				var b2 := (kids[j] as Control).get_global_rect().grow(-0.5)
				if b2.has_area() and a.intersects(b2):
					out.append("%s × %s" % [_path(kids[i], panel), str(kids[j].name)])
	return out


static func _path(n: Node, root: Node) -> String:
	return str(root.get_path_to(n)).right(60)
