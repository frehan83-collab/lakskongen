using Godot;
using LakskongenSim.Environment;

// G1-02 / G1-04 — what the 3D sky reads from the sim: the latitude of the
// active scenario's region, the calendar day and the atmosphere that day's
// weather implies (Koschmieder visibility, cloud cover, precipitation, sea
// fog). The hour of day stays a presentation clock (D20). Pure projection:
// every rule lives in LakskongenSim.Environment (AtmosphereModel,
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
    /// visibility_km, cloud_cover, precip_mm_h, snow, sea_fog, air_temp_c.
    /// </summary>
    public Godot.Collections.Dictionary GetSkyContext()
    {
        var weather = SimulationEngine?.Weather;
        var state = weather?.CurrentState ?? WeatherState.Calm;
        double wind = weather?.CurrentWindSpeedMs ?? 3.0;
        double seaTemp = weather?.CurrentTemperatureC ?? 8.0;
        int day = CurrentDay;
        double latitude = GetSceneLatitude();
        var atmosphere = AtmosphereModel.Derive(state, wind, seaTemp, day, latitude);
        var region = SimulationEngine?.Scenario?.ActiveScenario?.StartingRegion
                     ?? LakskongenSim.Data.ProductionRegion.Vestland;
        return new Godot.Collections.Dictionary
        {
            ["latitude"] = latitude,
            ["region"] = region.ToString(),
            ["sim_day"] = day,
            ["day_of_year"] = SolarGeometry.DayOfYear(day),
            ["weather_state"] = state.ToString(),
            ["wind_ms"] = wind,
            ["wave_m"] = weather?.CurrentWaveHeight ?? 0.5,
            ["sea_temp_c"] = seaTemp,
            ["visibility_km"] = atmosphere.VisibilityKm,
            ["cloud_cover"] = atmosphere.CloudCover01,
            ["precip_mm_h"] = atmosphere.PrecipitationMmPerHour,
            ["snow"] = atmosphere.Snow,
            ["sea_fog"] = atmosphere.SeaFog01,
            ["air_temp_c"] = atmosphere.AirTemperatureC,
        };
    }
}
