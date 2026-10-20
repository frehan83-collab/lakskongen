namespace LakskongenSim.Processing
{
    /// <summary>
    /// Feed recipe preview math (Phase 4.4, slim): 3 sliders → cost/FCR/grade.
    /// Pure functions so UI preview matches the engine exactly.
    /// </summary>
    public static class FeedRecipe
    {
        /// <summary>NOK/kg: base 12 + protein + novel ingredients + astaxanthin.</summary>
        public static double CostPerKg(double proteinPct, double replacePct, double astaxPpm) =>
            12.0 + (proteinPct - 38.0) * 0.18 + replacePct * 0.05 + astaxPpm * 0.025;

        /// <summary>FCR multiplier 0.90-1.0: better recipe converts better.</summary>
        public static double FcrMult(double proteinPct, double replacePct, double astaxPpm) =>
            1.0 - (proteinPct - 38.0) * 0.003 - replacePct * 0.0006
                - System.Math.Min(astaxPpm, 60.0) * 0.0002;

        /// <summary>VAP fillet grade from recipe: A/B/C.</summary>
        public static string Grade(double proteinPct, double astaxPpm) =>
            astaxPpm >= 50.0 && proteinPct >= 42.0 ? "A"
            : astaxPpm >= 40.0 ? "B" : "C";

        /// <summary>VAP harvest premium: full +12% on A/B, halved on C (pale).</summary>
        public static decimal VapMult(string grade) => grade == "C" ? 1.06m : 1.12m;

        public static (double protein, double replace, double astax) Clamp(
            double protein, double replace, double astax) => (
            System.Math.Clamp(protein, 38.0, 48.0),
            System.Math.Clamp(replace, 0.0, 60.0),
            System.Math.Clamp(astax, 0.0, 80.0));
    }
}
