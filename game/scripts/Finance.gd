# Finance.gd
# Financial tracking and simple loan system for the salmon farm.
# Tracks revenues (by product/market), costs (category breakdown), P&L, cash flow, loans, and KPIs.

extends Node
class_name Finance

# Cost breakdown (percentages) - should sum to ~1.0
const COST_SHARES = {
    "feed": 0.55,
    "labor": 0.11,
    "lice_treatment": 0.10,
    "smolt": 0.09,
    "depreciation": 0.07,
    "other": 0.08
}

# Financial state
var cash_balance: float = 1_000_000.0 # NOK starting cash
var total_revenue: float = 0.0
var total_costs: float = 0.0
var revenue_breakdown := {} # market/product -> amount
var cost_breakdown := {}

# Loans: array of {principal_remaining, interest_rate_annual, term_weeks, weekly_payment}
var loans: Array = []

# History for cashflow reporting
var cashflow_history: Array = [] # list of {week, inflow, outflow, net}

func _init():
    # initialize breakdowns
    for k in COST_SHARES.keys():
        cost_breakdown[k] = 0.0

# Record a sale
# quantity_kg, price_per_kg (NOK/kg), market_name, product_type, quality, week
func record_sale(quantity_kg: float, price_per_kg: float, market_name: String = "EU", product_type: String = "HOG", quality: String = "SUPERIOR", week: int = 1) -> Dictionary:
    var revenue = quantity_kg * price_per_kg
    total_revenue += revenue
    cash_balance += revenue

    # track breakdown key
    var key = "%s|%s" % [market_name, product_type]
    revenue_breakdown[key] = revenue_breakdown.get(key, 0.0) + revenue

    # Save to cashflow
    _record_cashflow(week, revenue, 0.0)

    return {"revenue": revenue, "quantity_kg": quantity_kg}

# Record byproduct revenue
func record_byproduct(amount: float, desc: String = "byproduct", week: int = 1) -> void:
    total_revenue += amount
    cash_balance += amount
    revenue_breakdown[desc] = revenue_breakdown.get(desc, 0.0) + amount
    _record_cashflow(week, amount, 0.0)

# Estimate and record production costs for a production batch (total_kg)
# This uses COST_SHARES to split costs. Assumes a production cost per kg baseline which can be scaled.
func record_production_costs(total_kg: float, baseline_cost_per_kg: float = 60.0, week: int = 1) -> Dictionary:
    var total_prod_cost = total_kg * baseline_cost_per_kg
    total_costs += total_prod_cost
    cash_balance -= total_prod_cost

    for k in COST_SHARES.keys():
        var amt = total_prod_cost * float(COST_SHARES[k])
        cost_breakdown[k] = cost_breakdown.get(k, 0.0) + amt
    _record_cashflow(week, 0.0, total_prod_cost)
    return {"total_production_cost": total_prod_cost}

# General expense (one-off)
func record_expense(amount: float, desc: String = "expense", category: String = "other", week: int = 1) -> void:
    total_costs += amount
    cash_balance -= amount
    cost_breakdown[category] = cost_breakdown.get(category, 0.0) + amount
    _record_cashflow(week, 0.0, amount)

# Simple loan creation: borrow principal, set annual interest rate and term in weeks
# weekly_payment computed using annuity formula
func take_loan(principal: float, annual_rate: float, term_weeks: int) -> Dictionary:
    var weekly_rate = annual_rate / 52.0
    # annuity payment formula for weekly payment
    var payment = 0.0
    if weekly_rate > 0.0:
        var r = weekly_rate
        var n = term_weeks
        payment = principal * (r / (1.0 - pow(1.0 + r, -n)))
    else:
        payment = principal / float(term_weeks)

    var loan = {
        "principal_remaining": principal,
        "interest_rate_annual": annual_rate,
        "term_weeks": term_weeks,
        "weekly_payment": payment
    }
    loans.append(loan)
    cash_balance += principal
    _record_cashflow(0, principal, 0.0)
    return loan

# Process weekly loan payments (call once per week)
func process_weekly_loans(week: int = 1) -> void:
    for i in range(loans.size() - 1, -1, -1):
        var loan = loans[i]
        var pr = float(loan["principal_remaining"])
        var annual = float(loan["interest_rate_annual"])
        var weekly_rate = annual / 52.0
        var payment = float(loan["weekly_payment"])

        # interest portion
        var interest = pr * weekly_rate
        var principal_pay = payment - interest
        if principal_pay > pr:
            principal_pay = pr
            payment = interest + pr
        pr -= principal_pay
        loan["principal_remaining"] = pr

        # cash movements
        cash_balance -= payment
        total_costs += payment
        cost_breakdown["other"] = cost_breakdown.get("other", 0.0) + payment
        _record_cashflow(week, 0.0, payment)

        # remove if paid off
        if pr <= 0.001:
            loans.remove_at(i)

# Quick KPI calculations
func production_cost_per_kg(total_kg: float) -> float:
    # approximate: sum of recorded production-related cost shares divided by kg
    var prod_cost = 0.0
    for k in ["feed", "labor", "lice_treatment", "smolt", "depreciation", "other"]:
        prod_cost += cost_breakdown.get(k, 0.0)
    if total_kg <= 0.0:
        return 0.0
    return prod_cost / total_kg

func revenue_per_kg(total_revenue_amount: float, total_kg: float) -> float:
    if total_kg <= 0.0:
        return 0.0
    return total_revenue_amount / total_kg

func ebit_per_kg(total_kg: float) -> float:
    # EBIT = revenue - operating costs (exclude interest)
    var interest_costs = _estimate_weekly_interest_costs() * 52.0
    var operating = total_costs - interest_costs
    if total_kg <= 0.0:
        return 0.0
    return (total_revenue - operating) / total_kg

# Estimates weekly interest costs (approx) from loans
func _estimate_weekly_interest_costs() -> float:
    var sum = 0.0
    for loan in loans:
        sum += float(loan["principal_remaining"]) * float(loan["interest_rate_annual"]) / 52.0
    return sum

# Cashflow recorder
func _record_cashflow(week: int, inflow: float, outflow: float) -> void:
    cashflow_history.append({"week": week, "inflow": inflow, "outflow": outflow, "net": inflow - outflow})

# Reporting helpers
func get_balance_sheet_snapshot() -> Dictionary:
    return {
        "cash_balance": cash_balance,
        "total_revenue": total_revenue,
        "total_costs": total_costs,
        "loans": loans.duplicate(),
        "revenue_breakdown": revenue_breakdown.duplicate(),
        "cost_breakdown": cost_breakdown.duplicate()
    }

# Reset (useful for simulations)
func reset_financials(starting_cash: float = 0.0) -> void:
    cash_balance = starting_cash
    total_revenue = 0.0
    total_costs = 0.0
    revenue_breakdown.clear()
    cost_breakdown.clear()
    loans.clear()
    cashflow_history.clear()
    for k in COST_SHARES.keys():
        cost_breakdown[k] = 0.0

# End of Finance.gd
