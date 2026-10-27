using AVACosmicChasm.Engine;

namespace AVACosmicChasm.Tests;

/// <summary>
/// How the computer pilot gets on (set CHASM_PROBE to run it, for tuning: "1" for the pilot from the first station,
/// or a station number to start there). CHASM_SEEDS picks the games.
/// </summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_pilot_does()
    {
        string? mode = Environment.GetEnvironmentVariable("CHASM_PROBE");
        if (mode is null) return;
        int from = int.TryParse(mode, out int n) && n > 1 ? n : 1;
        var seeds = (Environment.GetEnvironmentVariable("CHASM_SEEDS") ?? "1,2,3").Split(',').Select(int.Parse);
        foreach (int seed in seeds)
        {
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "chasm-probe-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
            g.Go(from);
            g.Pilot.Reset();
            var deaths = new List<string>();
            var stations = new List<string>();
            int t = 0, station = from;
            double at = 0;
            bool wasDead = false;
            for (; t < 60 * 900 && g.State == GameState.Playing; t++)
            {
                g.Tick(g.Pilot.Decide(g));
                var w = g.World;
                if (w.Ship.Dead && !wasDead) deaths.Add($"{w.Station.Number}:{(w.InCore ? "core" : "room" + w.Room)}:{g.Pilot.Doing}:{(w.Squeeze >= Field.MaxSqueeze ? "crushed" : w.Escaping && w.EscapeTime <= 0 ? "blown" : "hit")}");
                wasDead = w.Ship.Dead;
                if (w.Station.Number != station) { stations.Add($"{t / 60.0 - at:0}s"); at = t / 60.0; station = w.Station.Number; }
            }
            output.WriteLine($"seed {seed}: {g.State}/{g.Phase} at station {g.StationNumber} after {t / 60}s, score {g.Score}, lives {g.World.Lives}, kills {g.World.Kills}, deaths [{string.Join(" ", deaths)}], stations {string.Join(" ", stations)}");
        }
    }
}
