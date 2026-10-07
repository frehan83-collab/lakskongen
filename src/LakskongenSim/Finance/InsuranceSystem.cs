using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Finance
{
    /// <summary>
    /// Catastrophe an insurance policy can be written against. Mirrors the
    /// incidents that actually destroy Norwegian salmon biomass: routine
    /// mortality, algal blooms, storm/escape, and ISA (which forces a cull).
    /// </summary>
    public enum InsuranceKind
    {
        /// <summary>Ordinary husbandry mortality (disease, handling, background loss).</summary>
        Mortality,

        /// <summary>Algal bloom / jellyfish / hypoxia mass-mortality event.</summary>
        Bloom,

        /// <summary>Storm damage, net failure and subsequent escape.</summary>
        Storm,

        /// <summary>Infectious salmon anaemia — detection mandates a full cull.</summary>
        ISA
    }

    /// <summary>
    /// A single, immutable insurance policy.
    ///
    /// <para>
    /// Semantics (documented so payouts are auditable):
    /// <list type="bullet">
    /// <item><description><c>PremiumNokPerYear</c> — flat annual premium paid regardless of losses.</description></item>
    /// <item><description><c>CoverageFraction</c> — share of the retained loss the insurer reimburses, in [0,1].</description></item>
    /// <item><description><c>DeductibleNok</c> — the retained loss the insured absorbs before cover applies.</description></item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="Kind">Catastrophe class this policy covers.</param>
    /// <param name="PremiumNokPerYear">Annual premium in NOK (>= 0).</param>
    /// <param name="CoverageFraction">Fraction of the loss above the deductible that is reimbursed, in [0,1].</param>
    /// <param name="DeductibleNok">Retained loss in NOK before cover applies (>= 0).</param>
    public sealed record InsurancePolicy(
        InsuranceKind Kind,
        decimal PremiumNokPerYear,
        double CoverageFraction,
        decimal DeductibleNok);

    /// <summary>
    /// Insurance model.
    ///
    /// <para>
    /// Two related capabilities live here:
    /// <list type="number">
    /// <item><description>Legacy single-value cover: <see cref="Insure"/>/<see cref="ComputePremium"/>/
    /// <see cref="PayClaim"/> — premium proportional to insured value and time.</description></item>
    /// <item><description>Policy-based cover: <see cref="Subscribe"/> a set of <see cref="InsurancePolicy"/>
    /// records and evaluate catastrophe payouts with <see cref="ComputePayout"/>.</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Payout rule per policy: <c>max(0, (lossNok − DeductibleNok)) × CoverageFraction</c>,
    /// capped at the loss (no policy ever pays more than the damage). When several
    /// policies of the same kind are held they stack, but the aggregate payout is
    /// still capped at the loss so the farm can never profit from a catastrophe.
    /// All methods are pure with respect to their inputs and deterministic.
    /// </para>
    /// </summary>
    public class InsuranceSystem
    {
        // ── Legacy single-value cover ─────────────────────────────────────

        /// <summary>Current insured asset value in NOK (legacy cover).</summary>
        public decimal InsuredValue { get; private set; }

        /// <summary>Annual premium rate as a fraction of insured value (legacy cover).</summary>
        public decimal AnnualPremiumRate { get; private set; } = 0.01m; // 1% default

#pragma warning disable CS0067
        /// <summary>Raised when a legacy premium is paid.</summary>
        public event Action<decimal>? OnPremiumPaid;
#pragma warning restore CS0067

        /// <summary>Raised when a claim is paid.</summary>
        public event Action<decimal>? OnClaimPaid;

        private readonly List<InsurancePolicy> _policies = new();

        /// <summary>All subscribed policies, in subscription order.</summary>
        public IReadOnlyList<InsurancePolicy> Policies => _policies;

        /// <summary>Sum of every subscribed policy's annual premium (NOK).</summary>
        public decimal TotalAnnualPremium => _policies.Sum(p => p.PremiumNokPerYear);

        /// <summary>Annual premium for one catastrophe kind (NOK).</summary>
        public decimal TotalAnnualPremiumFor(InsuranceKind kind) =>
            _policies.Where(p => p.Kind == kind).Sum(p => p.PremiumNokPerYear);

        /// <summary>
        /// Register a policy. Negative premiums/deductibles are clamped to zero by the
        /// insurer; coverage is clamped to [0,1]. Passing null throws.
        /// </summary>
        public void Subscribe(InsurancePolicy policy)
        {
            if (policy is null) throw new ArgumentNullException(nameof(policy));
            _policies.Add(policy with
            {
                PremiumNokPerYear = policy.PremiumNokPerYear > 0m ? policy.PremiumNokPerYear : 0m,
                CoverageFraction = Math.Clamp(policy.CoverageFraction, 0.0, 1.0),
                DeductibleNok = policy.DeductibleNok > 0m ? policy.DeductibleNok : 0m
            });
        }

        /// <summary>Remove every subscribed policy.</summary>
        public void ClearPolicies() => _policies.Clear();

        /// <summary>
        /// Payout for a catastrophe of <paramref name="kind"/> causing
        /// <paramref name="lossNok"/> of damage. Stacks all matching policies and
        /// caps the total at the loss. Returns zero for non-positive losses or when
        /// no matching policy exists.
        /// </summary>
        public decimal ComputePayout(InsuranceKind kind, decimal lossNok)
        {
            if (lossNok <= 0m) return 0m;

            decimal total = 0m;
            foreach (var policy in _policies)
            {
                if (policy.Kind != kind) continue;
                total += PayoutFor(policy, lossNok);
            }

            return Math.Min(total, lossNok);
        }

        /// <summary>
        /// Pure payout for a single policy:
        /// <c>max(0, (lossNok − DeductibleNok)) × CoverageFraction</c>, capped at the loss.
        /// </summary>
        public static decimal PayoutFor(InsurancePolicy policy, decimal lossNok)
        {
            if (policy is null || lossNok <= 0m) return 0m;

            var deductible = policy.DeductibleNok > 0m ? policy.DeductibleNok : 0m;
            var retained = lossNok - deductible;
            if (retained <= 0m) return 0m;

            var coverage = (decimal)Math.Clamp(policy.CoverageFraction, 0.0, 1.0);
            var payout = retained * coverage;
            return Math.Min(payout, lossNok);
        }

        // ── Legacy single-value cover ─────────────────────────────────────

        /// <summary>Set the legacy insured value and annual rate.</summary>
        public void Insure(decimal value, decimal annualRate)
        {
            InsuredValue = value;
            AnnualPremiumRate = annualRate;
        }

        /// <summary>Pro-rate the legacy premium for <paramref name="days"/> days.</summary>
        public decimal ComputePremium(int days)
        {
            var fraction = (decimal)days / 365m;
            return InsuredValue * AnnualPremiumRate * fraction;
        }

        /// <summary>Pay a legacy claim, capped at the insured value.</summary>
        public decimal PayClaim(decimal claimAmount, bool totalLoss = false)
        {
            var payout = Math.Min(claimAmount, InsuredValue);
            if (totalLoss) InsuredValue = 0m;
            OnClaimPaid?.Invoke(payout);
            return payout;
        }
    }
}
