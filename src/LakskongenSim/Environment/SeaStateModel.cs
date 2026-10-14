using System;

namespace LakskongenSim.Environment
{
    /// <summary>A wind sea: significant wave height (m) and spectral peak period (s).</summary>
    public readonly record struct WindSea(double SignificantHeightM, double PeakPeriodS)
    {
        /// <summary>Deep-water wavelength of the spectral peak, g·Tp²/2π (m).</summary>
        public double PeakWavelengthM => SeaStateModel.Gravity * PeakPeriodS * PeakPeriodS / (2.0 * Math.PI);
    }

    /// <summary>
    /// The sea a wind raises over a fetch (G1-05), for the water the player
    /// sees. Pure: no state, no RNG.
    ///
    /// <list type="bullet">
    /// <item>Growth: the JONSWAP fetch-limited relations as given by Carter
    /// (1982), gHs/U² = 1.6·10⁻³·χ^½ and gTp/U = 0.286·χ^⅓ with
    /// χ = gF/U² (U the 10 m wind), capped at the fully developed
    /// Pierson–Moskowitz sea, gHs/U² = 0.243 and gTp/U = 8.134. The two
    /// regimes meet near χ = 2.3·10⁴.</item>
    /// <item>Spectrum: JONSWAP, γ = 3.3, σ = 0.07 / 0.09, sampled into a fixed
    /// set of wave bands. Each band carries the energy of its frequency
    /// interval, normalised so that Σa² = Hs²/8 (m0 = Hs²/16 = Σa²/2).
    /// The bands are fixed, not tied to the peak: a wind change moves energy
    /// between them instead of stretching wavelengths, so the waves never
    /// slide across the fjord while the weather blends.</item>
    /// <item>Whitecaps: Monahan &amp; O'Muircheartaigh (1980),
    /// W = 3.84·10⁻⁶·U^3.41 (fraction of the surface).</item>
    /// </list>
    ///
    /// A sheltered fjord basin a few kilometres long stays near flat in a
    /// light breeze (3 m/s over 3 km gives Hs 0.08 m, Tp 1.3 s) and raises a
    /// short, steep chop in a gale; an exposed coast (100 km of fetch) rolls
    /// 2-4 m seas at 20 m/s.
    /// </summary>
    public static class SeaStateModel
    {
        public const double Gravity = 9.81;

        private const double FetchHeightCoefficient = 1.6e-3;
        private const double FetchPeriodCoefficient = 0.286;
        private const double FullyDevelopedHeight = 0.243;
        private const double FullyDevelopedPeriod = 8.134;
        /// <summary>Below this wind (m/s) the surface is glassy: no wind sea.</summary>
        private const double CalmWindMs = 0.3;

        private const double MonahanCoefficient = 3.84e-6;
        private const double MonahanExponent = 3.41;

        private const double JonswapGamma = 3.3;
        private const double JonswapSigmaLow = 0.07;
        private const double JonswapSigmaHigh = 0.09;
        /// <summary>Integration points per band.</summary>
        private const int BandSamples = 48;

        /// <summary>Fetch of an exposed coastal site: open sea sheltered only by distant islands.</summary>
        public const double ExposedFetchM = 100_000.0;

        /// <summary>
        /// Wavelengths (m) of the fixed wave bands the renderer draws: log-spaced
        /// from the long sea of an exposed coast to the short chop of a basin.
        /// Shorter waves are carried by the water's normal maps.
        /// </summary>
        public static readonly double[] BandWavelengthsM = { 128.0, 68.4, 36.6, 19.6, 10.5, 5.6, 3.0, 1.6 };

        /// <summary>The fetch-limited (or fully developed) wind sea for a wind and a fetch.</summary>
        public static WindSea Derive(double windMs, double fetchM)
        {
            if (windMs <= CalmWindMs || fetchM <= 0.0)
                return new WindSea(0.0, 0.0);
            double u2 = windMs * windMs;
            double chi = Gravity * fetchM / u2;
            double height = Math.Min(FetchHeightCoefficient * Math.Sqrt(chi), FullyDevelopedHeight) * u2 / Gravity;
            double period = Math.Min(FetchPeriodCoefficient * Math.Cbrt(chi), FullyDevelopedPeriod) * windMs / Gravity;
            return new WindSea(height, period);
        }

        /// <summary>Fraction of the sea surface covered by whitecaps at a 10 m wind.</summary>
        public static double WhitecapFraction(double windMs)
        {
            if (windMs <= 0.0)
                return 0.0;
            return Math.Min(1.0, MonahanCoefficient * Math.Pow(windMs, MonahanExponent));
        }

        /// <summary>JONSWAP spectral shape at angular frequency ω for a peak ωp (arbitrary scale).</summary>
        public static double JonswapShape(double omega, double omegaPeak)
        {
            if (omega <= 0.0 || omegaPeak <= 0.0)
                return 0.0;
            double ratio = omegaPeak / omega;
            double pm = Math.Pow(omega, -5.0) * Math.Exp(-1.25 * ratio * ratio * ratio * ratio);
            double sigma = omega <= omegaPeak ? JonswapSigmaLow : JonswapSigmaHigh;
            double d = (omega - omegaPeak) / (sigma * omegaPeak);
            return pm * Math.Pow(JonswapGamma, Math.Exp(-0.5 * d * d));
        }

        /// <summary>Deep-water angular frequency (rad/s) of a wavelength: ω = √(g·2π/λ).</summary>
        public static double AngularFrequency(double wavelengthM) => Math.Sqrt(Gravity * 2.0 * Math.PI / wavelengthM);

        /// <summary>
        /// Amplitudes (m) of the fixed wave bands for a sea: each band's share
        /// of the JONSWAP energy, scaled so Σa² = Hs²/8. A calm sea returns zeros.
        /// </summary>
        public static double[] BandAmplitudes(WindSea sea) => BandAmplitudes(sea, BandWavelengthsM);

        public static double[] BandAmplitudes(WindSea sea, double[] wavelengthsM)
        {
            int n = wavelengthsM.Length;
            var amplitudes = new double[n];
            if (sea.SignificantHeightM <= 0.0 || sea.PeakPeriodS <= 0.0 || n == 0)
                return amplitudes;
            double omegaPeak = 2.0 * Math.PI / sea.PeakPeriodS;
            var omegas = new double[n];
            for (int i = 0; i < n; i++)
                omegas[i] = AngularFrequency(wavelengthsM[i]);
            var energy = new double[n];
            double total = 0.0;
            for (int i = 0; i < n; i++)
            {
                // Band edges halfway (geometrically) to the neighbours; the end
                // bands take the spectrum's tails out to a factor 2.
                double lo = i == 0 ? omegas[0] * 0.5 : Math.Sqrt(omegas[i - 1] * omegas[i]);
                double hi = i == n - 1 ? omegas[n - 1] * 2.0 : Math.Sqrt(omegas[i] * omegas[i + 1]);
                double sum = 0.0;
                double step = (hi - lo) / BandSamples;
                for (int s = 0; s < BandSamples; s++)
                    sum += JonswapShape(lo + (s + 0.5) * step, omegaPeak) * step;
                energy[i] = sum;
                total += sum;
            }
            if (total <= 0.0)
                return amplitudes;
            // m0 = Hs²/16 shared by band energy; a² = 2·E.
            double m0 = sea.SignificantHeightM * sea.SignificantHeightM / 16.0;
            for (int i = 0; i < n; i++)
                amplitudes[i] = Math.Sqrt(2.0 * m0 * energy[i] / total);
            return amplitudes;
        }

        /// <summary>Significant wave height of a set of band amplitudes: Hs = √(8·Σa²).</summary>
        public static double SignificantHeight(double[] amplitudes)
        {
            double sum = 0.0;
            foreach (double a in amplitudes)
                sum += a * a;
            return Math.Sqrt(8.0 * sum);
        }
    }
}
