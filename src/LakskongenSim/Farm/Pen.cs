using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using LakskongenSim.Calibration;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// One production unit of a sea site (roadmap S2-01b): a net pen in the
    /// site's frame mooring. A pen holds at most one fish population (one
    /// stocking, one year class); the regulator counts fish and density per
    /// production unit (akvakulturdriftsforskriften § 47a: at most 200,000
    /// fish; § 25: at most 25 kg/m³ in the volume the fish can move in).
    /// <para>
    /// Populations map to pens through <see cref="PopulationId"/>; an empty
    /// id is an empty (fallow) pen. Farm types reuse the class: F2-10 adds a
    /// lice skirt per pen, F2-12's land tank wraps a pen for its volume and
    /// occupancy (docs/FARM_TYPES_SPEC.md §3.2).
    /// </para>
    /// </summary>
    public sealed class Pen
    {
        /// <summary>Stable id: the site id and the pen's number, "po3_nord/p03".</summary>
        public string Id { get; set; } = "";

        /// <summary>Position in the site's mooring frame, 1-based (the number the player reads: "merd 3").</summary>
        public int Number { get; set; }

        /// <summary>Collar circumference (m) the net hangs from.</summary>
        public double CircumferenceM { get; set; }

        /// <summary>Depth (m) of the net's vertical wall.</summary>
        public double NetWallDepthM { get; set; }

        /// <summary>Height (m) of the net's cone bottom below the wall.</summary>
        public double ConeDepthM { get; set; }

        /// <summary>
        /// Water volume (m³) the fish can move in (§ 25): the cylinder of the
        /// wall plus the cone (<see cref="PenGeometry.VolumeM3"/>), set when the
        /// pen is built. Stored, so a land tank (F2-12) or a grandfathered legacy
        /// unit can carry a volume of its own.
        /// </summary>
        public double VolumeM3 { get; set; }

        /// <summary>Id of the population in the pen; empty when the pen is empty.</summary>
        public string PopulationId { get; set; } = "";

        /// <summary>True when the pen holds no population.</summary>
        [JsonIgnore]
        public bool IsEmpty => string.IsNullOrEmpty(PopulationId);

        /// <summary>Total net depth (m): wall plus cone.</summary>
        [JsonIgnore]
        public double NetDepthM => NetWallDepthM + ConeDepthM;

        /// <summary>Density (kg/m³) of <paramref name="biomassKg"/> in this pen.</summary>
        public double DensityKgPerM3(double biomassKg) => VolumeM3 > 0.0 ? biomassKg / VolumeM3 : 0.0;

        /// <summary>The id of pen number <paramref name="number"/> at <paramref name="siteId"/>.</summary>
        public static string ComposeId(string siteId, int number) => $"{siteId}/p{number:D2}";

        /// <summary>A pen of <paramref name="geometry"/> at position <paramref name="number"/> of <paramref name="siteId"/>.</summary>
        public static Pen Build(string siteId, int number, PenGeometry geometry) => new()
        {
            Id = ComposeId(siteId, number),
            Number = number,
            CircumferenceM = geometry.CircumferenceM,
            NetWallDepthM = geometry.NetWallDepthM,
            ConeDepthM = geometry.ConeDepthM,
            VolumeM3 = geometry.VolumeM3,
        };
    }

    /// <summary>
    /// Shape of a sea pen's net (S2-01b): a vertical wall hanging from a
    /// circular collar and a cone bottom. The volume the fish can move in is the
    /// wall's cylinder plus the cone, A × wall + A × cone / 3, with the collar's
    /// area A = C² / (4π).
    /// </summary>
    /// <param name="CircumferenceM">Collar circumference (m).</param>
    /// <param name="NetWallDepthM">Depth of the vertical net wall (m).</param>
    /// <param name="ConeDepthM">Height of the cone bottom (m).</param>
    public readonly record struct PenGeometry(double CircumferenceM, double NetWallDepthM, double ConeDepthM)
    {
        /// <summary>Area (m²) inside a collar of <see cref="CircumferenceM"/>.</summary>
        public double CollarAreaM2 => CircumferenceM * CircumferenceM / (4.0 * Math.PI);

        /// <summary>Volume (m³): the wall's cylinder plus the cone.</summary>
        public double VolumeM3 => VolumeOf(CircumferenceM, NetWallDepthM, ConeDepthM);

        /// <summary>The volume (m³) of a net of these dimensions.</summary>
        public static double VolumeOf(double circumferenceM, double wallDepthM, double coneDepthM)
        {
            double c = Math.Max(0.0, circumferenceM);
            double area = c * c / (4.0 * Math.PI);
            return area * (Math.Max(0.0, wallDepthM) + Math.Max(0.0, coneDepthM) / 3.0);
        }

        /// <summary>
        /// The circumference (m) a net of <paramref name="wallDepthM"/> and
        /// <paramref name="coneDepthM"/> needs to hold <paramref name="volumeM3"/>.
        /// </summary>
        public static double CircumferenceFor(double volumeM3, double wallDepthM, double coneDepthM)
        {
            double depth = Math.Max(0.0, wallDepthM) + Math.Max(0.0, coneDepthM) / 3.0;
            if (depth <= 0.0 || volumeM3 <= 0.0) return 0.0;
            return Math.Sqrt(4.0 * Math.PI * volumeM3 / depth);
        }

        /// <summary>The standard sea pen of the calibration (<c>pens.standard*</c>).</summary>
        public static PenGeometry Standard(PenCalibration calibration)
        {
            if (calibration == null) throw new ArgumentNullException(nameof(calibration));
            return new PenGeometry(calibration.StandardCircumferenceM, calibration.StandardNetWallDepthM,
                calibration.StandardConeDepthM);
        }
    }

    /// <summary>
    /// A site's pens as its locality is laid out (S2-01b): how many pens the
    /// mooring frame holds and the net of each. Scenario sites take theirs from
    /// <c>pen_layout</c> in the scenario data; a built site gets the standard
    /// layout of the calibration.
    /// </summary>
    /// <param name="Count">Pens in the mooring frame.</param>
    /// <param name="Geometry">The net every pen of the site has.</param>
    public readonly record struct PenLayout(int Count, PenGeometry Geometry)
    {
        /// <summary>The standard layout: <c>pens.builtSitePenCount</c> pens of the standard net.</summary>
        public static PenLayout Standard(PenCalibration calibration)
        {
            if (calibration == null) throw new ArgumentNullException(nameof(calibration));
            return new PenLayout(calibration.BuiltSitePenCount, PenGeometry.Standard(calibration));
        }

        /// <summary>The pens of this layout at <paramref name="siteId"/>, numbered from 1, all empty.</summary>
        public List<Pen> Build(string siteId)
        {
            var pens = new List<Pen>(Math.Max(0, Count));
            for (int number = 1; number <= Count; number++)
                pens.Add(Pen.Build(siteId, number, Geometry));
            return pens;
        }
    }
}
