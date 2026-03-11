import math
import random

def compute_zone_state(seed, temp_base=8.0, capacity=1000.0):
    rnd = random.Random(seed)
    seasonal = 4.0 * math.sin(0 / 365.0 * 2.0 * math.pi)
    temperature = temp_base + seasonal + rnd.uniform(-0.5,0.5)
    oxygen = max(2.0, min(12.0, 9.0 - 0.2 * (temperature - 8.0) - rnd.uniform(0.0,0.5)))
    pressure = max(0.1, min(3.0, capacity / 1000.0))
    sea_lice_risk = max(0.0, min(1.0, 0.005 + 0.02 * max(0.0, temperature - 7.0) + 0.01 * (pressure - 1.0) + rnd.uniform(-0.002,0.002)))
    oxygen_stress = max(0.0, min(1.0, (6.0 - oxygen) / 6.0))
    disease_risk = max(0.0, min(1.0, 0.0005 + 0.01 * oxygen_stress + 0.05 * sea_lice_risk + rnd.uniform(-0.001,0.001)))
    return {
        'temperature': temperature,
        'oxygen': oxygen,
        'sea_lice_risk': sea_lice_risk,
        'disease_risk': disease_risk,
    }

def test_deterministic_zone_state():
    s1 = compute_zone_state(42)
    s2 = compute_zone_state(42)
    assert s1 == s2, "Zone state should be deterministic for same seed"
    # different seed => different state very likely
    s3 = compute_zone_state(43)
    assert s1 != s3

def test_event_sampling_deterministic():
    seed = 2026
    rnd = random.Random(seed)
    # compute probabilities using deterministic helper
    state = compute_zone_state(99)
    bloom_prob = max(0.0, min(1.0, 0.001 + 0.05 * max(0.0, state['temperature'] - 10.0) + 0.1 * max(0.0, (7.0 - state['oxygen']) / 7.0)))
    disease_prob = max(0.0, min(1.0, state['disease_risk'] * 0.1 + 0.05 * state['sea_lice_risk']))
    events = []
    # sample deterministically
    if rnd.random() < bloom_prob:
        events.append('algae_bloom')
    if rnd.random() < disease_prob:
        events.append('disease_outbreak')
    # repeat with same seed should give same events
    rnd2 = random.Random(seed)
    events2 = []
    if rnd2.random() < bloom_prob:
        events2.append('algae_bloom')
    if rnd2.random() < disease_prob:
        events2.append('disease_outbreak')
    assert events == events2
