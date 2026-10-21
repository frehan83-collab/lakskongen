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
					# The rects and the parent pin down the geometry when the
					# failure is on a machine we cannot look at (CI).
					out.append("%s %s × %s %s in %s %s sep %d" % [
							_path(kids[i], panel), str(a.grow(0.5)), str(kids[j].name), str(b2.grow(0.5)),
							cont.get_class(), str(cont.get_global_rect()),
							cont.get_theme_constant("separation") if cont is BoxContainer else -1])
	return out


## problems() once the layout has settled. Container sorts are deferred and
## flush once per frame, so a snapshot taken mid-cascade (a window resize or a
## scale change fans out over several frames) can show one child moved and its
## sibling not yet — an "overlap" no player ever sees. A wall-clock wait is not
## enough: on a loaded CI runner 0.3 s can be a single frame (PR #11's Quality
## gate measured PreviewRow over ExplainCallout inside a sorted VBox this way).
## Two identical consecutive frames mean the layout is the one on screen; the
## 12-frame cap keeps a pathological animation from hanging the check, and the
## last snapshot is then measured as-is.
static func settled_problems(dialog: Control, tree: SceneTree) -> PackedStringArray:
	var last := PackedInt64Array()
	for i in 12:
		await tree.process_frame
		var now := _rect_signature(dialog)
		if now == last:
			break
		last = now
	return problems(dialog)


## One int64 per visible control: its global rect at 1/8 px, so any moved or
## resized control changes the signature.
static func _rect_signature(dialog: Control) -> PackedInt64Array:
	var sig := PackedInt64Array()
	for node in dialog.find_children("*", "Control", true, false):
		var c := node as Control
		if not c.is_visible_in_tree():
			continue
		var r := c.get_global_rect()
		sig.append(int(r.position.x * 8.0) ^ (int(r.position.y * 8.0) << 16) \
				^ (int(r.size.x * 8.0) << 32) ^ (int(r.size.y * 8.0) << 48))
	return sig


static func _path(n: Node, root: Node) -> String:
	return str(root.get_path_to(n)).right(60)
