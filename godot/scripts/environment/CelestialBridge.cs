using Godot;
using LakskongenSim.Environment;
using Vector3 = Godot.Vector3;

/// <summary>
/// Godot face of the sim's sky models (G1-04): <see cref="SolarGeometry"/>
/// for the sun and moon and <see cref="AuroraModel"/> for the night's aurora.
/// One implementation, unit-tested in LakskongenSim.Tests, shared by the
/// presentation clock (<c>TimeOfDay.gd</c>) and the tests.
///
/// World axes: north = +X, up = +Y, east = +Z (a right-handed east-north-up
/// frame with Y up). The fjord opens west, toward −Z, so the default camera,
/// which looks down the fjord to the mouth, faces west: the noon sun stands to
/// its left and sets ahead of it.
///
/// Pure compute, no scene access; GDScript calls it through
/// <c>call()</c> because the C# type is invisible to its compiler.
/// </summary>
public partial class CelestialBridge : RefCounted
{
    /// <summary>World direction (north +X, up +Y, east +Z) of a sky position.</summary>
    public static Vector3 ToWorld(HorizontalPosition p)
    {
        var (north, up, east) = p.Direction();
        return new Vector3((float)north, (float)up, (float)east);
    }

    /// <summary>
    /// The sky at a latitude, a sim day (0 = 1 January) and a local solar
    /// hour (0-24, which advances the date inside the day). Keys: sun_direction, sun_elevation, sun_azimuth,
    /// moon_direction, moon_elevation, moon_illumination, moon_waxing,
    /// aurora_kp, aurora_brightness, aurora_offset_km, day_of_year; and the
    /// ground's season (<see cref="GroundSeasonModel"/>, G2-02): snow_line_m,
    /// foliage, autumn.
    /// </summary>
    public Godot.Collections.Dictionary Sky(double latitudeDeg, int simDay, double solarHour)
    {
        int day = Mathf.Max(simDay, 0);
        int dayOfYear = SolarGeometry.DayOfYear(day);
        var sun = SolarGeometry.Sun(latitudeDeg, dayOfYear, solarHour);
        var moon = SolarGeometry.Moon(latitudeDeg, day, solarHour);
        // The night belongs to the evening's date: the activity knots are
        // smooth, so the hour just slides along them.
        var aurora = AuroraModel.At(latitudeDeg, day + solarHour / 24.0);
        var ground = GroundSeasonModel.At(latitudeDeg, dayOfYear + solarHour / 24.0);
        return new Godot.Collections.Dictionary
        {
            ["sun_direction"] = ToWorld(sun),
            ["sun_elevation"] = sun.ElevationDeg,
            ["sun_azimuth"] = sun.AzimuthDeg,
            ["moon_direction"] = ToWorld(moon.Position),
            ["moon_elevation"] = moon.Position.ElevationDeg,
            ["moon_illumination"] = moon.IlluminatedFraction,
            ["moon_waxing"] = moon.Waxing,
            ["aurora_kp"] = aurora.Kp,
            ["aurora_brightness"] = aurora.Brightness01,
            ["aurora_offset_km"] = aurora.OvalOffsetKm,
            ["day_of_year"] = dayOfYear,
            ["snow_line_m"] = ground.SnowLineM,
            ["foliage"] = ground.Foliage01,
            ["autumn"] = ground.Autumn01,
        };
    }

    /// <summary>The sun's elevation (degrees) at noon for a latitude and day of year.</summary>
    public double NoonElevation(double latitudeDeg, int dayOfYear)
        => SolarGeometry.NoonElevationDeg(latitudeDeg, dayOfYear);

    /// <summary>The sun's elevation (degrees) at solar midnight for a latitude and day of year.</summary>
    public double MidnightElevation(double latitudeDeg, int dayOfYear)
        => SolarGeometry.MidnightElevationDeg(latitudeDeg, dayOfYear);
}
