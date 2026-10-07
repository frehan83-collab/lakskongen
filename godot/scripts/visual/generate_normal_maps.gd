#!/usr/bin/env godot -s

extends SceneTree

func _init():
	print("Generating water normal maps...")
	
	var dir = DirAccess.open("res://assets/textures/water")
	if not dir:
		DirAccess.make_dir_recursive_absolute("res://assets/textures/water")
	
	# Generate two normal maps with different frequencies
	_generate_normal_map("ocean_normal_1.png", 512, 512, 4.0, 0.8)
	_generate_normal_map("ocean_normal_2.png", 512, 512, 8.0, 0.5)
	
	print("Normal maps generated!")
	quit()

func _generate_normal_map(path: String, width: int, height: int, freq: float, strength: float):
	var img = Image.create_empty(width, height, false, Image.FORMAT_RGBA8)
	var noise = FastNoiseLite.new()
	noise.seed = hash(path)
	noise.fractal_type = FastNoiseLite.FRACTAL_FBM
	noise.fractal_octaves = 4
	noise.fractal_lacunarity = 2.0
	noise.fractal_gain = 0.5
	
	for y in range(height):
		for x in range(width):
			# Generate height value
			var nx = float(x) / width
			var ny = float(y) / height
			var h = noise.get_noise_2d(nx * freq * 10.0, ny * freq * 10.0)
			
			# Calculate derivatives for normal
			var eps = 1.0 / width
			var hx = noise.get_noise_2d((nx + eps) * freq * 10.0, ny * freq * 10.0) - h
			var hy = noise.get_noise_2d(nx * freq * 10.0, (ny + eps) * freq * 10.0) - h
			
			# Normal from height derivatives
			var normal = Vector3(-hx * strength, -hy * strength, 1.0).normalized()
			
			# Encode to [0,1] range
			var r = normal.x * 0.5 + 0.5
			var g = normal.y * 0.5 + 0.5
			var b = normal.z * 0.5 + 0.5
			
			img.set_pixel(x, y, Color(r, g, b, 1.0))
	
	img.save_png(path)
	print("Generated: %s" % path)