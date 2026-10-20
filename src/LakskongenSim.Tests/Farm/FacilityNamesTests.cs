using System;
using LakskongenSim.Farm;
using Xunit;

namespace LakskongenSim.Tests.Farm;

/// <summary>
/// F2-03 acceptance 8: the "Ship"/"Mega" parse defects. The bridge documented
/// "SemiSubmersible", "Ship", "Submerged" and "Small", "Medium", "Large",
/// "Mega", but parsed with <c>Enum.TryParse</c>: "Ship", "Submerged" and "Mega"
/// were refused, and a number ("7") passed as an undefined enum value.
/// </summary>
public class FacilityNamesTests
{
    [Theory]
    [InlineData("SemiSubmersible", OffshoreType.SemiSubmersible)]
    [InlineData("semi_submersible", OffshoreType.SemiSubmersible)] // the offshore panel's
    [InlineData("Semi Submersible", OffshoreType.SemiSubmersible)]
    [InlineData("OF1", OffshoreType.SemiSubmersible)]
    [InlineData("havmerd", OffshoreType.SemiSubmersible)]
    [InlineData("Ship", OffshoreType.ShipShaped)]          // documented, never parsed
    [InlineData("ship_shaped", OffshoreType.ShipShaped)]
    [InlineData("ShipShaped", OffshoreType.ShipShaped)]
    [InlineData("Havfarm", OffshoreType.ShipShaped)]
    [InlineData("Submerged", OffshoreType.Submersible)]    // documented, never parsed
    [InlineData("SUBMERSIBLE", OffshoreType.Submersible)]
    public void OffshoreTypes_ParseByNameOrAlias(string text, OffshoreType expected)
    {
        Assert.True(FacilityNames.TryParseOffshoreType(text, out var type), text);
        Assert.Equal(expected, type);
        // The old parser refused the documented "Ship" and "Submerged".
        bool old = Enum.TryParse<OffshoreType>(text.Replace("_", "").Replace(" ", ""), true, out _);
        if (text is "Ship" or "Submerged")
            Assert.False(old);
    }

    [Theory]
    [InlineData("Small", RASScale.Small)]
    [InlineData("medium", RASScale.Medium)]
    [InlineData("LARGE", RASScale.Large)]
    [InlineData("Mega", RASScale.Large)]   // documented, never parsed: the 20,000 t "mega facility"
    [InlineData(" mega ", RASScale.Large)]
    public void RasScales_ParseByNameOrMega(string text, RASScale expected)
    {
        Assert.True(FacilityNames.TryParseRasScale(text, out var scale), text);
        Assert.Equal(expected, scale);
        Assert.Equal(20_000.0, RASFacility.DefaultCapacityTonnesPerYear(RASScale.Large));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("7")]   // Enum.TryParse took this as an undefined OffshoreType / RASScale
    [InlineData("-1")]
    [InlineData("1")]
    [InlineData("Boat")]
    [InlineData("Huge")]
    public void Garbage_AndNumbers_AreRefused(string text)
    {
        Assert.False(FacilityNames.TryParseOffshoreType(text, out _));
        Assert.False(FacilityNames.TryParseRasScale(text, out _));
        if (text is "7" or "1")
        {
            Assert.True(Enum.TryParse<OffshoreType>(text, true, out var undefined)); // the old defect
            Assert.True(text == "1" || !Enum.IsDefined(undefined));
        }
    }
}
