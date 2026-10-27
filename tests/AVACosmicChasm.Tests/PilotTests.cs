using AVACosmicChasm.Engine;
using static AVACosmicChasm.Tests.Harness;

namespace AVACosmicChasm.Tests;

/// <summary>The computer pilot: it finds its way through the stations, drills, scores, destroys the core and gets out.</summary>
public sealed class PilotTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void The_pilot_destroys_the_first_station_and_gets_out(int seed)
    {
        var g = Game(seed);
        g.Go();
        g.Pilot.Reset();
        bool coreGone = false;
        for (int t = 0; t < 60 * 240 && g.StationNumber == 1 && g.State == GameState.Playing; t++)
        {
            g.World.Lives = Math.Max(g.World.Lives, 2);
            g.Tick(g.Pilot.Decide(g));
            coreGone |= g.World.CoreDestroyed;
        }
        Assert.True(coreGone, $"{g.State} {g.Phase} station {g.StationNumber} room {g.World.Room} {g.Pilot.Doing} t {g.World.Clock:0}");
        Assert.Equal(2, g.StationNumber);
        Assert.True(g.Score > Sim.CorePoints(1) + GameEngine.EscapeBonus(1, 0));
    }

    [Fact]
    public void The_pilot_drills_the_doorway_it_needs()
    {
        var g = Game();
        g.Go();
        g.World.Spawning = false;
        var st = g.World.Station;
        var way = st.WayTo(g.World.Room, st.CoreRoom)!.Value;
        var door = g.World.Here.Door(way)!;
        for (int t = 0; t < 60 * 20 && !door.Open; t++) g.Tick(g.Pilot.Decide(g));
        Assert.True(door.Open);
        Assert.False(g.World.Ship.Dead);
    }

    [Fact]
    public void The_demonstration_plays_itself()
    {
        var g = Game();
        g.Run((int)(GameEngine.TitleIdleSeconds * 60) + 2);
        Assert.Equal(GameState.Attract, g.State);
        for (int t = 0; t < 60 * 20 && g.Phase != Phase.Fight; t++) g.Tick(default);
        int rooms = 0, room = g.World.Room;
        for (int t = 0; t < 60 * 40 && g.State == GameState.Attract; t++)
        {
            g.Tick(default);
            if (g.World.Room != room) { rooms++; room = g.World.Room; }
        }
        Assert.True(rooms >= 1, "it moves on through the station");
    }
}
