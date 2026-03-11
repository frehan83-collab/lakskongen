import math

# Re-implement the formulas used in GDScript to validate outputs

def von_bertalanffy_weight(age_days, Linf_g=4000.0, k=0.0025, t0_days=-30.0):
    t = max(age_days, 0.0)
    inner = 1.0 - math.exp(-k * (t - t0_days))
    return Linf_g * (inner ** 3)


def gompertz_weight(age_days, A_g=4000.0, k_g=0.0025, t_inf_days=365.0):
    t = max(age_days, 0.0)
    return A_g * math.exp(-math.exp(-k_g * (t - t_inf_days)))


def growth_environment_modifier(temp_c=10.0, feed_quality=1.0, Q10=2.0, ref_T=10.0):
    temp_factor = Q10 ** ((temp_c - ref_T) / 10.0)
    feed_factor = max(0.0, min(feed_quality, 2.0))
    return temp_factor * feed_factor


def expected_weight_at_age_vb(age_days, temp_c=10.0, feed_quality=1.0):
    base = von_bertalanffy_weight(age_days)
    return base * growth_environment_modifier(temp_c, feed_quality)


def expected_weight_at_age_gom(age_days, temp_c=10.0, feed_quality=1.0):
    base = gompertz_weight(age_days)
    return base * growth_environment_modifier(temp_c, feed_quality)


def test_vb_grows_and_is_monotonic():
    w0 = expected_weight_at_age_vb(0)
    w1 = expected_weight_at_age_vb(30)
    w2 = expected_weight_at_age_vb(365)
    assert w0 >= 0
    assert w1 > w0
    assert w2 > w1


def test_gompertz_grows_and_is_monotonic():
    w0 = expected_weight_at_age_gom(0)
    w1 = expected_weight_at_age_gom(30)
    w2 = expected_weight_at_age_gom(365)
    assert w0 >= 0
    assert w1 > w0
    assert w2 > w1


def test_temperature_and_feed_multiply():
    base = von_bertalanffy_weight(100)
    m1 = growth_environment_modifier(temp_c=10, feed_quality=1.0)
    m2 = growth_environment_modifier(temp_c=15, feed_quality=1.2)
    assert m2 > m1
    assert math.isclose(base * m1, expected_weight_at_age_vb(100, 10, 1.0), rel_tol=1e-6)


if __name__ == '__main__':
    # Simple runner
    test_vb_grows_and_is_monotonic()
    test_gompertz_grows_and_is_monotonic()
    test_temperature_and_feed_multiply()
    print('OK')
