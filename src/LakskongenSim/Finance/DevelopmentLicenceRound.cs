using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using LakskongenSim.Calibration;

namespace LakskongenSim.Finance
{
    /// <summary>The offshore concepts a development-licence application can be for (F2-09; ids as the farm-type catalogue's).</summary>
    public static class DevelopmentConcepts
    {
        /// <summary>The OF1-class semi-submersible (stage 2).</summary>
        public const string Of1 = "of1";

        /// <summary>The Havfarm-class ship (stage 3, a rival's concept until F2-11).</summary>
        public const string Havfarm = "havfarm";

        /// <summary>The concepts a rival applies with, in draw order.</summary>
        public static readonly IReadOnlyList<string> RivalConcepts = new[] { Of1, Havfarm };
    }

    /// <summary>Where a development-licence round stands.</summary>
    public enum DevelopmentRoundStatus
    {
        /// <summary>Applications are taken until <see cref="DevelopmentLicenceRound.ClosesDay"/>.</summary>
        Open = 0,
        /// <summary>Closed: no new application; pending ones are still decided.</summary>
        Closed = 1,
    }

    /// <summary>One rival's application in a round (drawn when the round opens; deterministic per seed).</summary>
    public sealed class RivalApplication
    {
        /// <summary>The rival company's id (the competitor roster's; "rival_N" when the game has none).</summary>
        public string RivalId { get; set; } = "";

        /// <summary>The rival's parody name.</summary>
        public string RivalName { get; set; } = "";

        /// <summary>The concept it applies with (<see cref="DevelopmentConcepts"/>).</summary>
        public string Concept { get; set; } = "";

        /// <summary>The plot its dossier names.</summary>
        public string PlotId { get; set; } = "";

        /// <summary>The day it files.</summary>
        public int SubmittedDay { get; set; }

        /// <summary>Its dossier's score (0-1).</summary>
        public double Score { get; set; }

        /// <summary>The day it is decided (filing + the award delay).</summary>
        public int DecisionDay { get; set; }

        /// <summary>True once decided.</summary>
        public bool Decided { get; set; }

        /// <summary>True when it won an award (and took its plot).</summary>
        public bool Awarded { get; set; }
    }

    /// <summary>
    /// A development-licence round (F2-09, spec §6.1 act 3): it opens once the
    /// company has a completed sea cycle, twelve escape-free months and the
    /// research for offshore farming, and stays open for two in-game years.
    /// The rival field (how many rivals apply, with what concept, when and how
    /// strong their dossiers are) is drawn when the round opens from a private
    /// stream seeded by the game's seed and the round's id, so it is the same
    /// for a seed whatever else happens in the game, and moves no game stream.
    /// Saved in <c>GameState.DevelopmentRounds</c>.
    /// </summary>
    public sealed class DevelopmentLicenceRound
    {
        /// <summary>Deterministic id "dlr_N".</summary>
        public string Id { get; set; } = "";

        /// <summary>The day it opened.</summary>
        public int OpenedDay { get; set; }

        /// <summary>The last day it takes applications.</summary>
        public int ClosesDay { get; set; }

        /// <summary>Where it stands.</summary>
        public DevelopmentRoundStatus Status { get; set; } = DevelopmentRoundStatus.Open;

        /// <summary>True when rivals may be awarded in it (the game's <c>rivalOffshore</c> when it opened).</summary>
        public bool RivalAwards { get; set; }

        /// <summary>The rival field, in filing order.</summary>
        public List<RivalApplication> Rivals { get; set; } = new();

        /// <summary>The company's applications (permit case ids), in filing order.</summary>
        public List<string> CompanyCaseIds { get; set; } = new();

        /// <summary>True while it takes applications on <paramref name="day"/>.</summary>
        public bool IsOpenOn(int day) => Status == DevelopmentRoundStatus.Open && day >= OpenedDay && day <= ClosesDay;

        /// <summary>
        /// Draw a round's rival field from <paramref name="uniform"/> (a private
        /// stream's draws): the number of rivals (none with the calibrated share,
        /// else one to the maximum), and for each a concept, a filing day in the
        /// window and a score in the band. Each rival takes exactly four draws
        /// after the count's one. <paramref name="roster"/> gives the rivals'
        /// ids and names in order (cycled when shorter than the field).
        /// </summary>
        public static List<RivalApplication> DrawField(Func<double> uniform, int openedDay, OffshoreLicencesCalibration cal,
            IReadOnlyList<(string Id, string Name)> roster, bool atLeastOne)
        {
            ArgumentNullException.ThrowIfNull(uniform);
            ArgumentNullException.ThrowIfNull(cal);
            double u = uniform();
            int max = Math.Max(0, cal.RivalApplicantsMax);
            int count = max == 0 || u < cal.RivalNoApplicantShare
                ? 0
                : 1 + Math.Min(max - 1, (int)((u - cal.RivalNoApplicantShare) / Math.Max(1e-12, 1.0 - cal.RivalNoApplicantShare) * max));
            if (atLeastOne && max > 0) count = Math.Max(1, count);
            var field = new List<RivalApplication>(count);
            for (int i = 0; i < count; i++)
            {
                double uConcept = uniform(), uDay = uniform(), uScore = uniform(), uRoster = uniform();
                var concepts = DevelopmentConcepts.RivalConcepts;
                string concept = concepts[Math.Clamp((int)(uConcept * concepts.Count), 0, concepts.Count - 1)];
                int day = openedDay + Math.Clamp((int)(uDay * cal.RivalSubmissionWindowDays), 0, Math.Max(0, cal.RivalSubmissionWindowDays - 1));
                var (id, name) = roster.Count == 0
                    ? ($"rival_{i + 1}", $"Rival {i + 1}")
                    : roster[(Math.Clamp((int)(uRoster * roster.Count), 0, roster.Count - 1) + i) % roster.Count];
                field.Add(new RivalApplication
                {
                    RivalId = id,
                    RivalName = name,
                    Concept = concept,
                    SubmittedDay = day,
                    Score = cal.RivalScoreMin + uScore * (cal.RivalScoreMax - cal.RivalScoreMin),
                    DecisionDay = day + cal.AwardDelayDays,
                });
            }
            field.Sort((a, b) => a.SubmittedDay != b.SubmittedDay ? a.SubmittedDay.CompareTo(b.SubmittedDay) : string.CompareOrdinal(a.RivalId, b.RivalId));
            return field;
        }

        /// <summary>The field's scores, ascending.</summary>
        public IReadOnlyList<double> FieldScores() => Rivals.Select(r => r.Score).OrderBy(s => s).ToList();

        /// <summary>The median rival score (0 for an empty field).</summary>
        public double FieldMedian()
        {
            var s = FieldScores();
            if (s.Count == 0) return 0.0;
            return s.Count % 2 == 1 ? s[s.Count / 2] : 0.5 * (s[s.Count / 2 - 1] + s[s.Count / 2]);
        }

        /// <summary>
        /// The score an application must reach to be awarded: the field's
        /// median, but never below the calibrated floor (the regulator's
        /// yardstick from the round's comparable dossiers).
        /// </summary>
        public double AwardBar(OffshoreLicencesCalibration cal) => Math.Max(cal.AwardFloorScore, FieldMedian());
    }

    /// <summary>Where an award stands.</summary>
    public enum DevelopmentAwardStatus
    {
        /// <summary>Awarded: construction must start by <see cref="DevelopmentAward.ConstructionDeadlineDay"/>.</summary>
        AwaitingConstruction = 0,
        /// <summary>A unit was built on it (its licences are bound to the unit).</summary>
        Built = 1,
        /// <summary>No construction started in time: the award lapsed and its licences with it.</summary>
        Lapsed = 2,
    }

    /// <summary>
    /// A development-licence award (F2-09): the company's (its licences in the
    /// register, bound to the unit it builds) or a rival's (its plot taken by
    /// "rival:&lt;id&gt;"). Saved in <c>GameState.DevelopmentAwards</c>.
    /// </summary>
    public sealed class DevelopmentAward
    {
        /// <summary>Deterministic id "da_N".</summary>
        public string Id { get; set; } = "";

        /// <summary>The round it was awarded in.</summary>
        public string RoundId { get; set; } = "";

        /// <summary>"" for the company, else the rival's id.</summary>
        public string RivalId { get; set; } = "";

        /// <summary>The concept (<see cref="DevelopmentConcepts"/>).</summary>
        public string Concept { get; set; } = "";

        /// <summary>The plot it is for.</summary>
        public string PlotId { get; set; } = "";

        /// <summary>The day it was awarded.</summary>
        public int AwardedDay { get; set; }

        /// <summary>The last day construction may start.</summary>
        public int ConstructionDeadlineDay { get; set; }

        /// <summary>Licences it carries.</summary>
        public int Licences { get; set; }

        /// <summary>Their MTB together (t).</summary>
        public double LicenceTonnes { get; set; }

        /// <summary>The company's application (permit case) it answers ("" for a rival's).</summary>
        public string CaseId { get; set; } = "";

        /// <summary>The application's score.</summary>
        public double Score { get; set; }

        /// <summary>Where it stands.</summary>
        public DevelopmentAwardStatus Status { get; set; } = DevelopmentAwardStatus.AwaitingConstruction;

        /// <summary>The unit built on it ("" before).</summary>
        public string UnitId { get; set; } = "";

        /// <summary>True for the company's own award.</summary>
        [JsonIgnore] public bool IsCompany => RivalId.Length == 0;
    }

    /// <summary>
    /// A development-licence application's score before it is filed (spec §6.1:
    /// "Before submitting, the player sees their score breakdown against the
    /// rival field band"): novelty, competence (escape-free months, lice
    /// compliance) and financing, against the round's field and its award bar.
    /// </summary>
    /// <param name="Novelty">+0.3 when no application of its class was filed before in the round.</param>
    /// <param name="EscapeFree">The escape-free months' part.</param>
    /// <param name="LiceCompliance">The lice compliance's part.</param>
    /// <param name="Financing">The financing's part.</param>
    /// <param name="Total">Their sum (0-1).</param>
    /// <param name="FieldMin">The weakest rival dossier (0 for an empty field).</param>
    /// <param name="FieldMedian">The field's median.</param>
    /// <param name="FieldMax">The strongest rival dossier.</param>
    /// <param name="AwardBar">The score that wins an award (the median, not below the floor).</param>
    /// <param name="Band">"strong" (at or above the strongest rival and the bar), "middle" (at or above the bar), "weak" (below it).</param>
    /// <param name="WouldBeAwarded">True when the total reaches the bar.</param>
    /// <param name="EscapeFreeMonths">The company's escape-free months.</param>
    /// <param name="LiceComplianceShare">The share of its stocked fjord sites under the limit at their last count.</param>
    /// <param name="FinancingCoverage">Cash over the financing need (≥ 1 earns the full part).</param>
    public sealed record DevelopmentScoreBreakdown(double Novelty, double EscapeFree, double LiceCompliance, double Financing,
        double Total, double FieldMin, double FieldMedian, double FieldMax, double AwardBar, string Band, bool WouldBeAwarded,
        double EscapeFreeMonths, double LiceComplianceShare, double FinancingCoverage);
}
