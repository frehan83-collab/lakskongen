#!/usr/bin/env python3
"""
Simple von Bertalanffy parameter estimator (heuristic).
Usage: python scripts/vb_fit.py data.csv
CSV should have two columns: age_days, weight_g
Outputs suggested Linf (g), k (per day), t0 (days)
"""
import sys
import csv
import math

def fit_vb_from_csv(path, linf_scale=1.05, t0_days=-30.0):
    ages = []
    weights = []
    with open(path, newline='') as f:
        reader = csv.reader(f)
        for row in reader:
            if not row: continue
            try:
                a = float(row[0])
                w = float(row[1])
            except Exception:
                continue
            ages.append(a)
            weights.append(w)
    if not ages:
        raise ValueError('no data')
    maxw = max(weights)
    Linf = maxw * linf_scale
    t0 = t0_days
    k_values = []
    for a,w in zip(ages, weights):
        if w <= 0 or a - t0 <= 0:
            continue
        ratio = (w / Linf)
        if ratio >= 1.0:
            # skip points at or above Linf
            continue
        inner = ratio ** (1.0/3.0)
        s = 1.0 - inner
        if s <= 0.0 or s >= 1.0:
            continue
        try:
            k = -math.log(s) / (a - t0)
            if k > 0 and k < 1.0:
                k_values.append(k)
        except Exception:
            pass
    if not k_values:
        # fallback: very small k
        k = 0.0025
    else:
        # use median-like robust estimator
        k_values.sort()
        mid = len(k_values)//2
        k = k_values[mid] if len(k_values)%2==1 else 0.5*(k_values[mid-1]+k_values[mid])
    return {
        'Linf_g': Linf,
        'k_per_day': k,
        't0_days': t0,
        'n_points_used': len(k_values)
    }

if __name__ == '__main__':
    if len(sys.argv) < 2:
        print('Usage: vb_fit.py data.csv')
        sys.exit(2)
    out = fit_vb_from_csv(sys.argv[1])
    print('Suggested parameters:')
    print('Linf_g =', out['Linf_g'])
    print('k_per_day =', out['k_per_day'])
    print('t0_days =', out['t0_days'])
    print('points used for k estimate =', out['n_points_used'])
