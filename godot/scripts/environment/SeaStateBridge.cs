using Godot;
using LakskongenSim.Environment;

/// <summary>
/// Godot face of the sim's <see cref="SeaStateModel"/> (G1-05): the wind sea
/// over a fetch, as the fixed wave bands the water shader draws. One
/// implementation, unit-tested in LakskongenSim.Tests; EnvironmentDirector.gd
/// calls it through <c>call()</c> because the C# type is invisible to the
/// GDScript compiler. Pure compute, no scene access.
/// </summary>
public partial class SeaStateBridge : RefCounted
{
    /// <summary>Fetch (m) of an exposed coast.</summary>
    public double ExposedFetchM() => SeaStateModel.ExposedFetchM;

    /// <summary>Wavelengths (m) of the fixed wave bands, longest first.</summary>
    public float[] BandWavelengths()
    {
        var bands = SeaStateModel.BandWavelengthsM;
        var result = new float[bands.Length];
        for (int i = 0; i < bands.Length; i++)
            result[i] = (float)bands[i];
        return result;
    }

    /// <summary>Wavelengths (m) of the open sea's two swell bands (F2-21), longest first.</summary>
    public float[] SwellBandWavelengths()
    {
        var bands = SeaStateModel.SwellBandWavelengthsM;
        var result = new float[bands.Length];
        for (int i = 0; i < bands.Length; i++)
            result[i] = (float)bands[i];
        return result;
    }

    /// <summary>
    /// Amplitudes (m) of the swell bands for a swell of significant height
    /// (m) and peak period (s): Σa² = hs²/8 (F2-21).
    /// </summary>
    public float[] SwellAmplitudes(double hsM, double tpS)
    {
        double[] a = SeaStateModel.SwellAmplitudes(hsM, tpS);
        var result = new float[a.Length];
        for (int i = 0; i < a.Length; i++)
            result[i] = (float)a[i];
        return result;
    }

    /// <summary>The 10 m wind (m/s) whose sea over a fetch (m) has a significant height (m) (F2-21).</summary>
    public double WindForHeight(double hsM, double fetchM) => SeaStateModel.WindForHeight(hsM, fetchM);

    /// <summary>
    /// The sea for a 10 m wind (m/s) over a fetch (m). Keys: hs (m), tp (s),
    /// peak_wavelength (m), amplitudes (one per band, Σa² = hs²/8),
    /// whitecaps (Monahan fraction).
    /// </summary>
    public Godot.Collections.Dictionary Sea(double windMs, double fetchM)
    {
        var sea = SeaStateModel.Derive(windMs, fetchM);
        double[] a = SeaStateModel.BandAmplitudes(sea);
        var amplitudes = new float[a.Length];
        for (int i = 0; i < a.Length; i++)
            amplitudes[i] = (float)a[i];
        return new Godot.Collections.Dictionary
        {
            ["hs"] = sea.SignificantHeightM,
            ["tp"] = sea.PeakPeriodS,
            ["peak_wavelength"] = sea.PeakPeriodS > 0.0 ? sea.PeakWavelengthM : 0.0,
            ["amplitudes"] = amplitudes,
            ["whitecaps"] = SeaStateModel.WhitecapFraction(windMs),
        };
    }
}
