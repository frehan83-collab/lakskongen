using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LakskongenSim.Core;

namespace StateHashProbe;

/// <summary>Seeded career → hash of the save it would write.</summary>
public static class StateHash
{
    private static readonly Regex GuidPattern = new(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.Compiled);

    /// <summary>
    /// Run a scripted career (scenario, stocking, loan, insurance, contract,
    /// delousing) for <paramref name="days"/> and return the SHA-256 of the
    /// GUID-normalized save JSON (entity GUIDs are process-random by design).
    /// </summary>
    public static string Compute(int seed, int days, bool events)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: events, seed: seed);
        engine.State.RandomSeed = seed;
        Require(engine.StartScenario("po3_red_sone"));
        Require(engine.TakeLoan(4_000_000, 36, 0.07));
        Require(engine.SetInsurancePolicy("mortality", true, 1.0, 250_000));
        while (engine.State.Day < days)
        {
            switch (engine.State.Day)
            {
                case 5: Require(engine.AcceptContractOffer(0)); break;
                case 170: Require(engine.DelouseSite("po3_nord", "thermolicer")); break;
            }
            engine.Advance(1);
        }
        engine.PrepareForSave();
        string json = GuidPattern.Replace(JsonSerializer.Serialize(engine.State, SaveJson.Options), "GUID");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static void Require(CommandResult result)
    {
        if (!result.Ok)
            throw new InvalidOperationException($"{result.ErrorKey}: {result.Message}");
    }

    /// <summary>usage: StateHashProbe &lt;seed&gt; &lt;days&gt; &lt;events:true|false&gt;</summary>
    public static int Main(string[] args)
    {
        if (args.Length != 3
            || !int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed)
            || !int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int days)
            || !bool.TryParse(args[2], out bool events))
        {
            Console.Error.WriteLine("usage: StateHashProbe <seed> <days> <events:true|false>");
            return 2;
        }
        SimLog.MinLevel = SimLogLevel.Error;
        Console.WriteLine(Compute(seed, days, events));
        return 0;
    }
}
