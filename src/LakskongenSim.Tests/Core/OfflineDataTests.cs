using System;
using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Net.Http;
using LakskongenSim.Core;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.Core
{
    /// <summary>
    /// Roadmap D18 / S2-02 acceptance 2: offline snapshots only. <c>Initialize</c>
    /// performs no network I/O, even with a BarentsWatch token in the environment
    /// (the old engine fetched the API whenever BARENTSWATCH_TOKEN was set, so two
    /// machines started different games), and writes no snapshot cache.
    /// </summary>
    [Collection(nameof(OfflineDataTests))]
    public class OfflineDataTests
    {
        /// <summary>Records every event of .NET's networking event sources.</summary>
        private sealed class NetworkListener : EventListener
        {
            public readonly ConcurrentQueue<string> Events = new();

            protected override void OnEventSourceCreated(EventSource source)
            {
                if (source.Name.StartsWith("System.Net", StringComparison.Ordinal))
                    EnableEvents(source, EventLevel.Verbose, EventKeywords.All);
            }

            protected override void OnEventWritten(EventWrittenEventArgs e)
            {
                if (e.EventSource.Name.StartsWith("System.Net", StringComparison.Ordinal))
                    Events.Enqueue($"{e.EventSource.Name}/{e.EventName}");
            }
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
        }

        [Fact]
        public void TheListener_SeesAnHttpRequest()
        {
            // Control: the listener below would see the engine reach for the
            // network. A request to a closed port on this machine fails at once.
            using var listener = new NetworkListener();
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
            {
                try { http.GetAsync("http://127.0.0.1:9/").GetAwaiter().GetResult(); }
                catch (HttpRequestException) { }
                catch (System.Threading.Tasks.TaskCanceledException) { }
            }
            Assert.Contains(listener.Events, e => e.StartsWith("System.Net.Http/", StringComparison.Ordinal));
        }

        [Fact]
        public void Initialize_MakesNoNetworkCall_EvenWithABarentsWatchTokenSet()
        {
            string? saved = System.Environment.GetEnvironmentVariable("BARENTSWATCH_TOKEN");
            string cache = Path.Combine(AppContext.BaseDirectory, "Data", "barentswatch-snapshots");
            int cachedBefore = Directory.Exists(cache) ? Directory.GetFiles(cache).Length : -1;
            System.Environment.SetEnvironmentVariable("BARENTSWATCH_TOKEN", "test-token-s2-02");
            try
            {
                using var listener = new NetworkListener();
                foreach (bool events in new[] { true, false })
                {
                    var engine = new SimulationEngine { FinanceManager = new FinanceManager(10_000_000m) };
                    engine.Initialize(enableEvents: events, seed: 4);
                    engine.Advance(3);
                }
                Assert.Empty(listener.Events);
            }
            finally
            {
                System.Environment.SetEnvironmentVariable("BARENTSWATCH_TOKEN", saved);
            }
            int cachedAfter = Directory.Exists(cache) ? Directory.GetFiles(cache).Length : -1;
            Assert.Equal(cachedBefore, cachedAfter);
        }

        [Fact]
        public void TheSimulation_HasNoNetworkClient_OutsideTheUnusedEmodnetLoader()
        {
            string src = Path.Combine(RepoRoot(), "src", "LakskongenSim");
            string[] networkApis = { "HttpClient", "WebClient", "TcpClient", "System.Net.Sockets", "WebRequest" };
            var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .Where(f => networkApis.Any(api => File.ReadAllText(f).Contains(api, StringComparison.Ordinal)))
                .Select(f => Path.GetRelativePath(RepoRoot(), f).Replace('\\', '/'))
                .ToList();
            Assert.Equal(new[] { "src/LakskongenSim/Data/RealData/Loaders/EmodnetDataLoader.cs" }, offenders);

            // ...and nothing in the simulation constructs that loader.
            var users = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith("EmodnetDataLoader.cs", StringComparison.Ordinal))
                .Where(f => File.ReadAllText(f).Contains("EmodnetDataLoader", StringComparison.Ordinal))
                .ToList();
            Assert.Empty(users);
        }

        [Fact]
        public void TheBarentsWatchClient_ReadsAnOfflineFile_AndKeepsItsRetrievalTime()
        {
            string dir = Path.Combine(Path.GetTempPath(), "lakskongen-bw-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                string raw = Path.Combine(dir, "raw.json");
                File.WriteAllText(raw, "{\"retrievedAtUtc\":\"2026-03-13T12:00:00Z\",\"year\":2026,\"week\":11,\"productionAreas\":[]}");
                var client = new BarentsWatchFishHealthClient(new BarentsWatchClientOptions { LocalSnapshotPath = raw });
                var first = client.FetchLatest();
                var second = client.FetchLatest();
                Assert.Equal(new DateTime(2026, 3, 13, 12, 0, 0, DateTimeKind.Utc), first.RetrievedAtUtc.ToUniversalTime());
                Assert.Equal(first.RetrievedAtUtc, second.RetrievedAtUtc);
                Assert.Equal("offline-file", first.SourceMode);

                var none = new BarentsWatchFishHealthClient(new BarentsWatchClientOptions());
                Assert.Throws<InvalidOperationException>(() => none.FetchLatest());
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
