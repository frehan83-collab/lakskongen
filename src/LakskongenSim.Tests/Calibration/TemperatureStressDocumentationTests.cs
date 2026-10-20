#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.Calibration
{
    /// <summary>
    /// S2-05 acceptance 4: the temperature-stress constant matches its
    /// documentation. The documentation of <c>ApplySeaTemperatureStress</c>
    /// said "0.5 % per degree of deviation per day" while the engine applied
    /// 0.01 × 0.005 = 0.005 %; the constant now lives in the calibration
    /// registry and this test reads the compiled XML documentation of both the
    /// engine method and the registry entry and holds them to the value the
    /// engine applies.
    /// </summary>
    public class TemperatureStressDocumentationTests
    {
        private static readonly MortalityCalibration Cal = CalibrationRegistry.Load().Mortality;

        private static string Doc(string memberName)
        {
            string xmlPath = Path.ChangeExtension(typeof(SimulationEngine).Assembly.Location, ".xml");
            Assert.True(File.Exists(xmlPath), $"XML documentation not found next to the assembly: {xmlPath}");
            var member = XDocument.Load(xmlPath).Descendants("member")
                .SingleOrDefault(m => (string?)m.Attribute("name") == memberName);
            Assert.True(member != null, $"no documentation for {memberName}");
            return Regex.Replace(member!.Value, @"\s+", " ");
        }

        private static string Percent(double fraction)
            => (fraction * 100.0).ToString("0.#####", CultureInfo.InvariantCulture) + " %";

        [Fact]
        public void EngineAndRegistryDocumentation_StateTheRateAndBandTheEngineApplies()
        {
            string rate = Percent(Cal.TemperatureStressDailyFractionPerDegreeC);
            string band = string.Format(CultureInfo.InvariantCulture, "{0:0.##}-{1:0.##} °C",
                Cal.TemperatureStressLowerC, Cal.TemperatureStressUpperC);

            string method = Doc("M:LakskongenSim.Core.SimulationEngine.ApplySeaTemperatureStress(System.Double)");
            Assert.Contains($"{rate} of the fish per °C of deviation per day", method);
            Assert.Contains(band, method);

            string entry = Doc("P:LakskongenSim.Calibration.MortalityCalibration.TemperatureStressDailyFractionPerDegreeC");
            Assert.Contains(Cal.TemperatureStressDailyFractionPerDegreeC.ToString("0.#########", CultureInfo.InvariantCulture)
                + " = " + rate + " of the fish per °C per day", entry);
            Assert.Contains("https://", entry); // sourced
            Assert.Contains(string.Format(CultureInfo.InvariantCulture, "{0:0.##} °C", Cal.TemperatureStressLowerC),
                Doc("P:LakskongenSim.Calibration.MortalityCalibration.TemperatureStressLowerC"));
            Assert.Contains(string.Format(CultureInfo.InvariantCulture, "{0:0.##} °C", Cal.TemperatureStressUpperC),
                Doc("P:LakskongenSim.Calibration.MortalityCalibration.TemperatureStressUpperC"));
        }

        [Fact]
        public void TheShippedCalibration_IsTheDocumentedCodeDefault()
        {
            var codeDefault = new MortalityCalibration();
            Assert.Equal(codeDefault, CalibrationRegistry.Load().Mortality);
            Assert.Equal(0.00005, codeDefault.TemperatureStressDailyFractionPerDegreeC);
            Assert.Equal(4.0, codeDefault.TemperatureStressLowerC);
            Assert.Equal(16.0, codeDefault.TemperatureStressUpperC);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(2.0)]
        [InlineData(3.5)]
        [InlineData(4.0)]
        [InlineData(10.0)]
        [InlineData(16.0)]
        [InlineData(18.0)]
        [InlineData(21.5)]
        public void SeaPen_LosesTheDocumentedFractionPerDegreeOutsideTheBand(double seaTempC)
        {
            var engine = new SimulationEngine();
            engine.FinanceManager = new FinanceManager(100_000_000m);
            engine.Initialize(enableEvents: false, seed: 3);
            engine.State.Zones.Add(new Zone("zone-t"));
            var site = new SeaSite("pen-t", "zone-t") { MABTonnes = 780 };
            engine.State.SeaSites.Add(site);
            Assert.True(engine.StockSmolts(site.Id, 200_000).Ok);
            int before = site.Pens.Sum(p => p.TotalCount);

            engine.ApplySeaTemperatureStress(seaTempC);

            double deviation = seaTempC < Cal.TemperatureStressLowerC ? Cal.TemperatureStressLowerC - seaTempC
                : seaTempC > Cal.TemperatureStressUpperC ? seaTempC - Cal.TemperatureStressUpperC : 0.0;
            int expected = (int)(before * (deviation * Cal.TemperatureStressDailyFractionPerDegreeC));
            Assert.Equal(expected, before - site.Pens.Sum(p => p.TotalCount));
            Assert.Equal(expected, engine.MortalityLedger.ForSite(site.Id).Deaths(MortalityCause.Temperature).Fish);
            if (deviation > 0.0)
                Assert.InRange(expected, (int)(before * deviation * 0.00004), (int)(before * deviation * 0.00006));
        }
    }
}
