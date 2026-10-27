using AVACosmicChasm.Engine;
using static AVACosmicChasm.Tests.Harness;

namespace AVACosmicChasm.Tests;

/// <summary>The game around the rooms: the map, lives, the escape, the next station, the title and the tables.</summary>
public sealed class GameTests
{
    private static int Seconds(double s) => (int)(s * 60) + 2;

    [Fact]
    public void A_game_starts_with_the_station_map_and_then_the_ship_flies()
    {
        var g = Game();
        g.StartGame();
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(Phase.Map, g.Phase);
        Assert.Equal("STATION 1", g.Message);
        var (x, y) = (g.World.Ship.X, g.World.Ship.Y);
        g.Run(60, new Controls(Thrust: true));
        Assert.Equal((x, y), (g.World.Ship.X, g.World.Ship.Y));
        g.Run(Seconds(GameEngine.MapSeconds));
        Assert.Equal(Phase.Fight, g.Phase);
        Assert.True(g.World.InDock);
        Assert.Equal(3, g.World.Lives);
    }

    [Fact]
    public void Fire_skips_the_map()
    {
        var g = Game();
        g.StartGame();
        g.Run(60);
        g.Tick(new Controls(Fire: true));
        Assert.Equal(Phase.Fight, g.Phase);
    }

    [Fact]
    public void A_crash_costs_a_ship_and_it_comes_back_where_it_came_in()
    {
        var g = Game();
        g.Go();
        var w = g.World;
        Sim.Crash(w);
        g.Tick(default);
        Assert.Equal(Phase.Down, g.Phase);
        g.Run(Seconds(GameEngine.DownSeconds));
        Assert.Equal(Phase.Fight, g.Phase);
        Assert.Equal(2, w.Lives);
        Assert.False(w.Ship.Dead);
        Assert.True(w.Ship.Safe > 0);
        Assert.True(w.Squeeze < 1, "the room is set back");
    }

    [Fact]
    public void Out_of_the_airlock_in_time_the_station_blows_and_the_next_one_is_bigger()
    {
        var g = Game();
        g.Go();
        var w = g.World;
        w.Escaping = true;
        w.EscapeTime = 20.5;
        var side = w.Station.DockSide;
        var (nx, ny) = Sim.Inward(side);
        var (x, y) = w.DoorAt(side);
        Park(w, x + nx * 20, y + ny * 20, Pilot.Heading(-nx, -ny));
        for (int i = 0; i < 120 && g.Phase == Phase.Fight; i++) g.Tick(new Controls(Thrust: true));
        Assert.Equal(Phase.Escaped, g.Phase);
        Assert.InRange(g.LastBonus, GameEngine.EscapeBonus(1, 19), GameEngine.EscapeBonus(1, 20.5));
        Assert.Equal(g.LastBonus, g.Score);
        g.Run(Seconds(GameEngine.EscapedSeconds));
        Assert.Equal(Phase.Map, g.Phase);
        Assert.Equal(2, g.StationNumber);
        Assert.False(g.World.Escaping);
        Assert.True(g.World.Core is { Dead: false });
        Assert.Equal(3, g.World.Lives);
    }

    [Fact]
    public void Too_late_the_station_goes_up_with_the_ship_and_costs_a_life()
    {
        var g = Game();
        g.Go();
        var w = g.World;
        w.Escaping = true;
        w.EscapeTime = 0.1;
        g.Run(10);
        Assert.Equal(Phase.Blown, g.Phase);
        Assert.True(w.Ship.Dead);
        g.Run(Seconds(GameEngine.BlownSeconds));
        Assert.Equal(2, g.World.Lives);
        Assert.Equal(2, g.StationNumber);
        Assert.Equal(Phase.Map, g.Phase);
    }

    [Fact]
    public void With_the_last_ship_gone_it_is_game_over_and_a_good_score_gets_initials()
    {
        var g = Game();
        g.Go();
        g.World.Lives = 1;
        g.World.Score = 99999;
        Sim.Crash(g.World);
        g.Run(Seconds(GameEngine.DownSeconds) + 2);
        Assert.Equal(GameState.GameOver, g.State);
        g.Run(Seconds(4));
        Assert.Equal(GameState.EnterInitials, g.State);
        g.Initials!.Type('L');
        g.Initials.Type('W');
        g.Initials.Type('X');
        g.Tick(default);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(0, g.NewEntryIndex);
        Assert.Equal(("LWX", 99999, 1), (g.HighScores.Entries[0].Initials, g.HighScores.Entries[0].Score, g.HighScores.Entries[0].Stage));
        var again = HighScoreTable.Load(g.HighScores.Folder);
        Assert.Equal("LWX", again.Entries[0].Initials);
    }

    [Fact]
    public void A_poor_score_goes_straight_to_the_table_and_then_the_title()
    {
        var g = Game();
        g.Go();
        g.World.Lives = 1;
        Sim.Crash(g.World);
        g.Run(Seconds(GameEngine.DownSeconds) + Seconds(4) + 2);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(-1, g.NewEntryIndex);
        g.Run(Seconds(GameEngine.HighScoreSeconds));
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void Left_alone_the_title_gives_way_to_the_demonstration_and_then_the_table()
    {
        var g = Game();
        g.Run(Seconds(GameEngine.TitleIdleSeconds));
        Assert.Equal(GameState.Attract, g.State);
        for (int i = 0; i < Seconds(GameEngine.AttractSeconds) && g.State == GameState.Attract; i++) g.Tick(default);
        Assert.Equal(GameState.HighScores, g.State);
    }

    [Fact]
    public void Enter_or_fire_starts_a_game_from_the_title_or_the_demonstration()
    {
        var g = Game();
        g.Run(40);
        g.Tick(new Controls(Fire: true));
        Assert.Equal(GameState.Playing, g.State);
        g = Game();
        g.Run(Seconds(GameEngine.TitleIdleSeconds));
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
    }

    [Fact]
    public void Paused_nothing_moves()
    {
        var g = Game();
        g.Go();
        g.Run(30);
        g.Pause();
        Assert.Equal(GameState.Paused, g.State);
        double squeeze = g.World.Squeeze, clock = g.World.Clock;
        g.Run(120, new Controls(Thrust: true));
        Assert.Equal((squeeze, clock), (g.World.Squeeze, g.World.Clock));
        g.Resume();
        g.Run(10);
        Assert.True(g.World.Squeeze > squeeze);
    }

    [Fact]
    public void Coming_into_the_core_room_calls_it_out_and_destroying_the_core_says_get_out()
    {
        var g = Game();
        g.Go();
        var w = g.World;
        var st = w.Station;
        int next = st.Route(w.Room, st.CoreRoom)[^2];
        var side = Sides.All.First(s => st[next].Door(s)?.Other(next) == st.CoreRoom);
        Into(w, next, side.Opposite());
        st[next].Door(side)!.Open = true;
        var (nx, ny) = Sim.Inward(side);
        var (x, y) = w.DoorAt(side);
        Park(w, x + nx * 20, y + ny * 20, Pilot.Heading(-nx, -ny));
        for (int i = 0; i < 120 && !w.InCore; i++) g.Tick(new Controls(Thrust: true));
        Assert.True(w.InCore);
        Assert.Equal("THE CORE!", g.Message);
        w.Core!.Hp = 1;
        Array.Fill(w.Core.Inner, 100);
        Array.Fill(w.Core.Outer, 100);
        w.Core.Reload = 100;
        Park(w, Field.CX - 250, Field.CY, Math.PI / 2);
        g.Tick(new Controls(Fire: true));
        g.Run(40);
        Assert.True(w.Escaping);
        Assert.Equal("CORE DESTROYED!", g.Message);
    }

    [Fact]
    public void Every_sound_has_its_file()
    {
        using var sound = new SoundManager(silent: true);
        foreach (var s in Enum.GetValues<Sfx>().Where(SoundManager.HasFile))
            Assert.True(File.Exists(sound.File(s)), $"{s}");
    }

    [Fact]
    public void The_thrust_roars_while_it_is_held_and_stops_when_it_is_let_go()
    {
        var g = Game();
        g.Silent = false;
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        g.Go();
        g.Run(5, new Controls(Thrust: true));
        Assert.Equal(1, heard.Count(s => s == Sfx.ThrustOn));
        g.Run(5);
        Assert.Equal(1, heard.Count(s => s == Sfx.ThrustOff));
    }
}
