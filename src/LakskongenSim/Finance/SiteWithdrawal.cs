using System.Text.Json.Serialization;

namespace LakskongenSim.Finance
{
    /// <summary>Where a site-withdrawal case stands (F2-08, spec §3.3 "Withdrawal").</summary>
    public enum SiteWithdrawalStatus
    {
        /// <summary>Decided: the approval is withdrawn from <see cref="SiteWithdrawalCase.EffectiveDay"/> unless an appeal lifts it.</summary>
        Decided = 0,
        /// <summary>Appealed: the appeal is decided on the effective day, on the handling done by then.</summary>
        Appealed = 1,
        /// <summary>The appeal lifted the withdrawal: the approval stands.</summary>
        Lifted = 2,
        /// <summary>The withdrawal took effect: the plot's approval is lost and the unit's licences are released, unplaced.</summary>
        Effective = 3,
    }

    /// <summary>
    /// Mattilsynet's withdrawal of an offshore unit's site approval (F2-08, spec
    /// §3.3: "75 d → a site-withdrawal case opens"; V:5, AOF's site approval was
    /// withdrawn from 1 January 2026 because the site was unreachable for up to
    /// three months). On its effective day the plot's approval is lost, any
    /// fish left are harvested by order at the first sea that allows it, and
    /// the licences behind the unit stay with the company as unplaced MTB
    /// (never deleted: V:2, AOF's licences were converted and must be placed in
    /// a production area). An appeal (one, before the effective day) is decided
    /// on that day: lifted when the unit has done the handling by then.
    /// Saved in <c>GameState.SiteWithdrawals</c>.
    /// </summary>
    public sealed class SiteWithdrawalCase
    {
        /// <summary>Deterministic id "sw_N".</summary>
        public string Id { get; set; } = "";

        /// <summary>The offshore unit.</summary>
        public string UnitId { get; set; } = "";

        /// <summary>The plot whose approval is withdrawn.</summary>
        public string PlotId { get; set; } = "";

        /// <summary>The day the case opened (the decision).</summary>
        public int OpenedDay { get; set; }

        /// <summary>The day the withdrawal takes effect and an appeal is decided.</summary>
        public int EffectiveDay { get; set; }

        /// <summary>Where it stands.</summary>
        public SiteWithdrawalStatus Status { get; set; } = SiteWithdrawalStatus.Decided;

        /// <summary>The requirement left undone that opened it (its <c>HandlingRequirement</c> name, snake_case).</summary>
        public string Requirement { get; set; } = "";

        /// <summary>The day it was appealed (-1 when not).</summary>
        public int AppealedDay { get; set; } = -1;

        /// <summary>The day it was decided for good: lifted or effective (-1 while pending).</summary>
        public int ClosedDay { get; set; } = -1;

        /// <summary>The day the fish left on the effective day were harvested by order (-1 none yet, or none were left).</summary>
        public int ForcedHarvestDay { get; set; } = -1;

        /// <summary>Licences the withdrawal released from the unit (they stay with the company, unplaced).</summary>
        public int LicencesReleased { get; set; }

        /// <summary>Their MTB (t).</summary>
        public double ReleasedTonnes { get; set; }

        /// <summary>True while it may still be appealed or lifted.</summary>
        [JsonIgnore] public bool IsPending => Status is SiteWithdrawalStatus.Decided or SiteWithdrawalStatus.Appealed;
    }
}
