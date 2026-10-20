using Godot;
using LakskongenSim.Environment;

// G1-02 / G1-04 — what the 3D sky reads from the sim: the latitude of the
// active scenario's region, the calendar day and the atmosphere that day's
// weather implies (Koschmieder visibility, cloud cover, precipitation, sea
// fog, frost smoke). The hour of day stays a presentation clock (D20). Pure
// projection: every rule lives in LakskongenSim.Environment (AtmosphereModel,
// RegionGeography, SolarGeometry).
public partial class GameBridge : Node
{
    /// <summary>Latitude (degrees north) of the active scenario's region; Vestland without one.</summary>
    public double GetSceneLatitude()
    {
        var region = SimulationEngine?.Scenario?.ActiveScenario?.StartingRegion
                     ?? LakskongenSim.Data.ProductionRegion.Vestland;
        return RegionGeography.LatitudeDeg(region);
    }

    /// <summary>
    /// The sky context for the 3D environment. Keys: latitude, region,
    /// sim_day, day_of_year, weather_state, wind_ms, wave_m, sea_temp_c,
    /// visibility_km, cloud_cover, precip_mm_h, snow, sea_fog, air_temp_c,
    /// frost_smoke (G3-05).
    /// </summary>
    public Godot.Collections.Dictionary GetSkyContext()
    {
        var weather = SimulationEngine?.Weather;
        var state = weather?.CurrentState ?? WeatherState.Calm;
        double wind = weather?.CurrentWindSpeedMs ?? 3.0;
        double seaTemp = weather?.CurrentTemperatureC ?? 8.0;
        var region = SimulationEngine?.Scenario?.ActiveScenario?.StartingRegion
                     ?? LakskongenSim.Data.ProductionRegion.Vestland;
        return SkyContext(state, wind, weather?.CurrentWaveHeight ?? 0.5, seaTemp, CurrentDay,
            GetSceneLatitude(), region.ToString());
    }

    /// <summary>
    /// The sky context the sim derives for given conditions (the keys of
    /// <see cref="GetSkyContext"/>): the weather regime by name (Calm,
    /// Moderate, Storm, Severe), the 10 m wind, the sea temperature, the sim
    /// day and the latitude. For staging a real sim atmosphere in QA (the
    /// screenshot harness's mood goldens, the UI flow tests) without
    /// advancing a game to that day. Pure: touches no sim state.
    /// </summary>
    public Godot.Collections.Dictionary SkyContextFor(string weatherState, double windMs, double seaTempC, int simDay, double latitude)
    {
        if (!System.Enum.TryParse(weatherState, out WeatherState state))
            state = WeatherState.Calm;
        return SkyContext(state, windMs, 0.0, seaTempC, System.Math.Max(simDay, 0), latitude, "");
    }

    private static Godot.Collections.Dictionary SkyContext(
        WeatherState state, double wind, double waveM, double seaTemp, int day, double latitude, string region)
    {
        var atmosphere = AtmosphereModel.Derive(state, wind, seaTemp, day, latitude);
        return new Godot.Collections.Dictionary
        {
            ["latitude"] = latitude,
            ["region"] = region,
            ["sim_day"] = day,
            ["day_of_year"] = SolarGeometry.DayOfYear(day),
            ["weather_state"] = state.ToString(),
            ["wind_ms"] = wind,
            ["wave_m"] = waveM,
            ["sea_temp_c"] = seaTemp,
            ["visibility_km"] = atmosphere.VisibilityKm,
            ["cloud_cover"] = atmosphere.CloudCover01,
            ["precip_mm_h"] = atmosphere.PrecipitationMmPerHour,
            ["snow"] = atmosphere.Snow,
            ["sea_fog"] = atmosphere.SeaFog01,
            ["air_temp_c"] = atmosphere.AirTemperatureC,
            ["frost_smoke"] = atmosphere.FrostSmoke01,
        };
    }
}
