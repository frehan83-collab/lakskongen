extends Node3D
class_name CameraPath

# Camera Path Recording & Playback System
# Records camera movements and plays them back as cinematics
# Author: Ingrid Lunde, UI/UX Designer

signal recording_started()
signal recording_stopped(path_data: Dictionary)
signal playback_started()
signal playback_finished()

# Recording state
var is_recording := false
var is_playing := false
var recorded_frames := []
var current_frame := 0

# Recording settings
var recording_fps := 60
var max_recording_time := 60.0  # seconds
var record_interval := 1.0 / recording_fps

# Playback settings
var playback_speed := 1.0
var loop_playback := false

# Camera reference
var target_camera: Camera3D

# Path data structure
class CameraKeyframe:
	var timestamp: float
	var position: Vector3
	var rotation: Vector3
	var fov: float
	
	func _init(time: float, pos: Vector3, rot: Vector3, f: float = 75.0):
		timestamp = time
		position = pos
		rotation = rot
		fov = f

func _ready():
	print("🎥 Camera Path system ready")

func start_recording(camera: Camera3D):
	if is_recording:
		print("⚠️ Already recording")
		return
	
	if is_playing:
		stop_playback()
	
	target_camera = camera
	is_recording = true
	recorded_frames = []
	current_frame = 0
	
	print("🔴 Recording started")
	recording_started.emit()
	
	# Start recording loop
	var timer = Timer.new()
	add_child(timer)
	timer.timeout.connect(_record_frame)
	timer.wait_time = record_interval
	timer.one_shot = false
	timer.start()
	
	# Auto-stop after max time
	get_tree().create_timer(max_recording_time).timeout.connect(func():
		if is_recording:
			stop_recording()
	)

func stop_recording():
	if not is_recording:
		return
	
	is_recording = false
	
	# Stop timer
	for child in get_children():
		if child is Timer:
			child.stop()
			child.queue_free()
	
	print("⏹️ Recording stopped - %d frames captured" % recorded_frames.size())
	
	var path_data = {
		"frames": recorded_frames,
		"duration": recorded_frames.size() * record_interval,
		"fps": recording_fps
	}
	
	recording_stopped.emit(path_data)

func _record_frame():
	if not is_recording or not target_camera:
		return
	
	var timestamp = recorded_frames.size() * record_interval
	var keyframe = CameraKeyframe.new(
		timestamp,
		target_camera.global_position,
		target_camera.global_rotation,
		target_camera.fov
	)
	
	recorded_frames.append(keyframe)

func start_playback(camera: Camera3D, speed: float = 1.0, loop: bool = false):
	if recorded_frames.is_empty():
		print("⚠️ No recorded path to play")
		return
	
	if is_recording:
		stop_recording()
	
	target_camera = camera
	is_playing = true
	current_frame = 0
	playback_speed = speed
	loop_playback = loop
	
	print("▶️ Playback started (%d frames)" % recorded_frames.size())
	playback_started.emit()

func stop_playback():
	if not is_playing:
		return
	
	is_playing = false
	current_frame = 0
	
	print("⏹️ Playback stopped")
	playback_finished.emit()

func _process(delta):
	if not is_playing or not target_camera or recorded_frames.is_empty():
		return
	
	# Calculate frame index based on time
	var frame_time = current_frame * record_interval
	var target_time = frame_time + (delta * playback_speed)
	var target_frame = int(target_time / record_interval)
	
	if target_frame >= recorded_frames.size():
		if loop_playback:
			current_frame = 0
			target_frame = 0
		else:
			stop_playback()
			return
	
	current_frame = target_frame
	
	# Get current and next frame for interpolation
	var current_keyframe: CameraKeyframe = recorded_frames[current_frame]
	
	if current_frame + 1 < recorded_frames.size():
		var next_keyframe: CameraKeyframe = recorded_frames[current_frame + 1]
		
		# Interpolate between frames for smooth playback
		var frame_progress = fmod(target_time, record_interval) / record_interval
		
		target_camera.global_position = current_keyframe.position.lerp(
			next_keyframe.position,
			frame_progress
		)
		
		# Slerp for smooth rotation
		var current_basis = Basis.from_euler(current_keyframe.rotation)
		var next_basis = Basis.from_euler(next_keyframe.rotation)
		var interpolated_basis = current_basis.slerp(next_basis, frame_progress)
		target_camera.global_rotation = interpolated_basis.get_euler()
		
		target_camera.fov = lerp(current_keyframe.fov, next_keyframe.fov, frame_progress)
	else:
		# Last frame
		target_camera.global_position = current_keyframe.position
		target_camera.global_rotation = current_keyframe.rotation
		target_camera.fov = current_keyframe.fov

func save_path(path_name: String):
	"""Save recorded path to file for reuse"""
	if recorded_frames.is_empty():
		print("⚠️ No recorded path to save")
		return false
	
	var save_data = {
		"name": path_name,
		"fps": recording_fps,
		"frames": []
	}
	
	for keyframe in recorded_frames:
		save_data["frames"].append({
			"time": keyframe.timestamp,
			"pos": [keyframe.position.x, keyframe.position.y, keyframe.position.z],
			"rot": [keyframe.rotation.x, keyframe.rotation.y, keyframe.rotation.z],
			"fov": keyframe.fov
		})
	
	var file_path = "user://camera_paths/%s.json" % path_name
	
	# Create directory if needed
	var dir = DirAccess.open("user://")
	if not dir.dir_exists("camera_paths"):
		dir.make_dir("camera_paths")
	
	# Save as JSON
	var file = FileAccess.open(file_path, FileAccess.WRITE)
	if file:
		file.store_string(JSON.stringify(save_data, "\t"))
		file.close()
		print("💾 Camera path saved: " + file_path)
		return true
	else:
		print("❌ Failed to save camera path")
		return false

func load_path(path_name: String) -> bool:
	"""Load a previously saved camera path"""
	var file_path = "user://camera_paths/%s.json" % path_name
	
	var file = FileAccess.open(file_path, FileAccess.READ)
	if not file:
		print("⚠️ Camera path not found: " + file_path)
		return false
	
	var json_text = file.get_as_text()
	file.close()
	
	var json = JSON.new()
	var parse_result = json.parse(json_text)
	
	if parse_result != OK:
		print("❌ Failed to parse camera path JSON")
		return false
	
	var data = json.data
	
	# Reconstruct frames
	recorded_frames = []
	recording_fps = data.get("fps", 60)
	record_interval = 1.0 / recording_fps
	
	for frame_data in data["frames"]:
		var pos_array = frame_data["pos"]
		var rot_array = frame_data["rot"]
		
		var keyframe = CameraKeyframe.new(
			frame_data["time"],
			Vector3(pos_array[0], pos_array[1], pos_array[2]),
			Vector3(rot_array[0], rot_array[1], rot_array[2]),
			frame_data.get("fov", 75.0)
		)
		recorded_frames.append(keyframe)
	
	print("📂 Camera path loaded: %s (%d frames)" % [path_name, recorded_frames.size()])
	return true

func export_as_video_frames(output_dir: String) -> bool:
	"""Export path as sequence of images for video encoding"""
	if recorded_frames.is_empty():
		print("⚠️ No recorded path to export")
		return false
	
	# Create output directory
	var dir = DirAccess.open("user://")
	if not dir.dir_exists(output_dir):
		dir.make_dir_recursive(output_dir)
	
	print("🎬 Exporting %d frames to %s" % [recorded_frames.size(), output_dir])
	
	# This would require rendering each frame
	# Placeholder for future implementation
	print("📝 Video export not yet implemented - use OBS/screen recorder for now")
	
	return false

func get_recording_duration() -> float:
	"""Get total duration of recorded path in seconds"""
	if recorded_frames.is_empty():
		return 0.0
	return recorded_frames.size() * record_interval

func get_frame_count() -> int:
	return recorded_frames.size()

func clear_recording():
	"""Clear current recording"""
	recorded_frames = []
	current_frame = 0
	print("🗑️ Recording cleared")
