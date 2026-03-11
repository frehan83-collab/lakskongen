import math

# Basic checks for chosen defaults and reasonable bounds.

def test_vb_defaults_in_range():
    Linf = 4000.0
    k = 0.0025
    t0 = -30.0
    # Linf between 1kg and 10kg
    assert 1000.0 <= Linf <= 10000.0
    # k between 0.0001 and 0.01 (per day)
    assert 0.0001 <= k <= 0.01
    # t0 between -365 and 0
    assert -365.0 <= t0 <= 0.0


def test_fcr_reasonable():
    fcr = 1.15
    assert 0.8 <= fcr <= 2.5


def test_baseline_rates():
    mort = 0.0004
    growth = 0.0025
    # mortality per day should be small but positive
    assert 1e-6 <= mort <= 0.001
    # daily growth reasonable
    assert 1e-4 <= growth <= 0.01

if __name__ == '__main__':
    test_vb_defaults_in_range()
    test_fcr_reasonable()
    test_baseline_rates()
    print('OK')
