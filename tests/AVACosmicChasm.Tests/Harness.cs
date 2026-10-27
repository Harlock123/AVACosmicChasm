using AVACosmicChasm.Engine;

namespace AVACosmicChasm.Tests;

/// <summary>Worlds and games set up for the tests, with anything saved kept in a temporary folder.</summary>
internal static class Harness
{
    public static string TempFolder() => Path.Combine(Path.GetTempPath(), "avacosmicchasm-tests-" + Guid.NewGuid().ToString("N"));

    public static GameEngine Game(int seed = 1) => new(HighScoreTable.Load(TempFolder()), seed) { Silent = true };

    /// <summary>A station with the ship in a room of its own: nothing comes out of the walls unless sent for.</summary>
    public static World Quiet(int station = 1, int seed = 1)
    {
        var w = new World();
        Sim.Start(w, seed);
        if (station > 1) Sim.BeginStation(w, station, seed);
        w.Spawning = false;
        w.Heard.Clear();
        return w;
    }

    /// <summary>The ship parked in the middle of the room, still, pointing up, past its moment of safety.</summary>
    public static Ship Park(World w, double x = Field.CX, double y = Field.CY, double angle = 0)
    {
        var s = w.Ship;
        (s.X, s.Y, s.Vx, s.Vy, s.Angle, s.Safe) = (x, y, 0, 0, angle, 0);
        return s;
    }

    /// <summary>Puts the ship in a room (as if it came in by a side) with nothing in it.</summary>
    public static void Into(World w, int room, Side from)
    {
        Sim.EnterRoom(w, room, from);
        w.Heard.Clear();
    }

    public static void Run(World w, int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c);
    }

    /// <summary>The first side of the current room with a plugged doorway.</summary>
    public static Side PluggedSide(World w) => Sides.All.First(s => w.Here.Door(s) is { Open: false });
}
