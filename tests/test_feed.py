# Tests for feed/FCR/cost model (Python re-implementation to validate logic)

import math

# Re-implement key formulas to validate expected behavior
BASE_FCR = 1.2
OPT_TEMP = 14.0
TEMP_TOL = 4.0
TEMP_SENS = 0.03
QUALITY_SENS = 0.4
BASE_PRICE = 1.5


def compute_fcr_py(feed_quality, water_temp_c):
    temp_diff = abs(water_temp_c - OPT_TEMP)
    temp_penalty = 0.0
    if temp_diff > TEMP_TOL:
        temp_penalty = (temp_diff - TEMP_TOL) * TEMP_SENS
    quality_penalty = (1.0 - feed_quality) * QUALITY_SENS
    fcr = BASE_FCR * (1.0 + temp_penalty + quality_penalty)
    return max(fcr, 0.1)


def dynamic_price_py(feed_quality, demand_index=1.0):
    quality_premium = feed_quality * 0.15
    quality_discount = (1.0 - feed_quality) * 0.10
    price = BASE_PRICE * (1.0 + (demand_index - 1.0)*0.2 + quality_premium - quality_discount)
    return max(price, 0.01)


def feed_needed_for_growth_py(gain_g, feed_quality, water_temp_c):
    fcr = compute_fcr_py(feed_quality, water_temp_c)
    return (gain_g * fcr) / 1000.0


def daily_cost_py(cohort_count, avg_daily_growth_g, feed_quality, water_temp_c, demand_index=1.0):
    if cohort_count <= 0 or avg_daily_growth_g <= 0.0:
        return 0.0
    feed_per = feed_needed_for_growth_py(avg_daily_growth_g, feed_quality, water_temp_c)
    total_feed = feed_per * cohort_count
    return total_feed * dynamic_price_py(feed_quality, demand_index)


# Tests

def test_fcr_increases_with_low_quality():
    f_high = compute_fcr_py(1.0, OPT_TEMP)
    f_low = compute_fcr_py(0.2, OPT_TEMP)
    assert f_low > f_high
    print('PASS quality affects FCR')


def test_fcr_increases_with_temp_outside_tolerance():
    f_opt = compute_fcr_py(0.8, OPT_TEMP)
    f_cold = compute_fcr_py(0.8, OPT_TEMP - 10)
    assert f_cold > f_opt
    print('PASS temp affects FCR')


def test_price_reflects_quality_and_demand():
    p_high = dynamic_price_py(1.0, demand_index=1.0)
    p_low = dynamic_price_py(0.2, demand_index=1.0)
    assert p_high > p_low
    p_demand = dynamic_price_py(0.8, demand_index=1.5)
    assert p_demand > dynamic_price_py(0.8, demand_index=1.0)
    print('PASS price model')


def test_daily_cost_for_cohort_edge_cases():
    assert daily_cost_py(0, 1.0, 0.8, OPT_TEMP) == 0.0
    assert daily_cost_py(100, 0.0, 0.8, OPT_TEMP) == 0.0
    # sanity: positive
    cost = daily_cost_py(100, 5.0, 0.8, OPT_TEMP)
    assert cost > 0.0
    print('PASS cohort cost edge cases')


if __name__ == '__main__':
    test_fcr_increases_with_low_quality()
    test_fcr_increases_with_temp_outside_tolerance()
    test_price_reflects_quality_and_demand()
    test_daily_cost_for_cohort_edge_cases()
    print('ALL FEED TESTS PASS')
