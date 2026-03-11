# scripts/FeedManager.gd
# Manages feed quality, consumption and FCR
extends Node

var feed_quality: float = 1.0 # 0.5 - 1.5
var fcr: float = 1.2 # feed per kg gain
var feed_cost_per_kg: float = 15.0 # NOK per kg feed

func set_recipe(quality:float):
	feed_quality = clamp(quality, 0.5, 1.5)
	# quality influences fcr
	fcr = lerp(1.5, 1.05, (feed_quality-0.5)/1.0)

func feed_cost_for_growth(gain_kg:float)->float:
	var feed_needed = gain_kg * fcr
	return feed_needed * feed_cost_per_kg

func get_feed_quality():
	return feed_quality
