extends Node

# Constants for disease probabilities
const ISA_PROB = 0.005 # Annual probability of ISA
const PD_PROB = 0.02   # 2-4% higher in warmer months
const CMS_PROB = 0.015 # 1-3% after 12 months at sea

# Vaccination effectiveness
const VACCINE_EFFECT_ISA = 0.9
const VACCINE_EFFECT_PD = 0.8
const VACCINE_EFFECT_CMS = 0.8

func _ready():
    var outbreak = simulate_disease_outbreak()
    if outbreak:
        print("Disease outbreak detected: " + outbreak)

func simulate_disease_outbreak() -> String:
    # Simple model assuming independent probabilities and water spread
    if randf() < ISA_PROB:
        return "ISA"
    if randf() < PD_PROB:
        return "PD"
    if randf() < CMS_PROB:
        return "CMS"
    return ""