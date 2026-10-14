using System;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using Xunit;
using DelousingMethod = LakskongenSim.Data.DelousingMethod;

namespace LakskongenSim.Tests.Farm;

/// <summary>
/// U1-08 (no dead verbs): the pen panel's treatment picker sends the enum
/// name the bridge lists in GetTreatmentProfiles ("HydrogenPeroxide" …).
/// The parser only knew legacy aliases, so the default treatment — hydrogen
/// peroxide, first in the list — failed every time with "Invalid delousing
/// method" and the Avlus button was a dead verb.
/// </summary>
public class DelousingMethodNameTests
{
    public static TheoryData<DelousingMethod> Methods()
    {
        var data = new TheoryData<DelousingMethod>();
        foreach (var method in Enum.GetValues<DelousingMethod>())
            data.Add(method);
        return data;
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public void TryParseMethodName_AcceptsTheEnumNameTheBridgeSends(DelousingMethod method)
    {
        Assert.True(DelousingSystem.TryParseMethodName(method.ToString(), out var parsed),
            $"'{method}' is the id GetTreatmentProfiles hands the UI");
        Assert.Equal(method, parsed);
    }

    [Theory]
    [InlineData("chemical", DelousingMethod.HydrogenPeroxide)]
    [InlineData("mechanical", DelousingMethod.Hydrolicer)]
    [InlineData("thermal", DelousingMethod.Thermolicer)]
    [InlineData("biological", DelousingMethod.CleanerFish)]
    public void TryParseMethodName_KeepsTheLegacyAliases(string alias, DelousingMethod expected)
    {
        Assert.True(DelousingSystem.TryParseMethodName(alias, out var parsed));
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bleach")]
    [InlineData("42")]
    public void TryParseMethodName_RejectsUnknownNames(string name)
    {
        Assert.False(DelousingSystem.TryParseMethodName(name, out _));
    }

    [Fact]
    public void DelouseSite_TreatsAStockedSiteWithTheDefaultPickerMethod()
    {
        var engine = new SimulationEngine();
        engine.FinanceManager = new FinanceManager(10_000_000m);
        engine.Initialize(enableEvents: false, seed: 91);
        Assert.True(engine.StartScenario("forste_sesong").Ok);
        var site = engine.State.SeaSites[0];

        var result = engine.DelouseSite(site.Id, DelousingMethod.HydrogenPeroxide.ToString());

        Assert.True(result.Ok, result.Message);
    }
}
