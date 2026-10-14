using System;
using System.Globalization;

namespace LakskongenSim.Core
{
    /// <summary>
    /// Player-facing text written by the sim (event log entries, day notes) is
    /// Norwegian Bokmål with the Norwegian number format ("12,5 t", "71 472 kr"),
    /// whatever culture the host process runs in (roadmap U1-03). Formatting
    /// with a fixed culture also keeps saved event texts identical on every
    /// machine.
    /// </summary>
    public static class SimText
    {
        /// <summary>Norwegian Bokmål (nb-NO).</summary>
        public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("nb-NO");

        /// <summary>Formats an interpolated string in Norwegian: SimText.Nb($"{x:F1} t").</summary>
        public static string Nb(FormattableString text) => text.ToString(Culture);
    }
}
