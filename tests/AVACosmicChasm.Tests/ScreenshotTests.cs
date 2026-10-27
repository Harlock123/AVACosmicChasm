using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVACosmicChasm.Engine;
using AVACosmicChasm.Views;
using Controls = AVACosmicChasm.Engine.Controls;

namespace AVACosmicChasm.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set CHASM_SHOTS to a folder to save them
/// as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avacosmicchasm-ui-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("CHASM_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVACosmicChasm-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 240, Func<GameEngine, Controls, Controls>? hand = null)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            // Kept alive for the pictures: they're of the station, not of the pilot's luck.
            g.World.Lives = Math.Max(g.World.Lives, 3);
            var move = g.Pilot.Decide(g);
            g.Tick(hand == null ? move : hand(g, move));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    private static void Play(string name, int station, Func<GameEngine, bool> moment, int seconds = 240, int seed = 11, Func<GameEngine, Controls, Controls>? hand = null)
    {
        var (w, c, g) = Open(seed);
        g.Go(station);
        g.Pilot.Reset();
        Assert.True(PlayUntil(g, c, moment, seconds, hand), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    private static bool Busy(GameEngine g) => g.Phase == Phase.Fight && !g.World.Ship.Dead && g.World.Blasts.Count(b => b.Size > 50) == 0;

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        for (int i = 0; i < 60 * 3; i++) c.Advance(1 / 60.0);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 182400, 9);
        g.HighScores.Insert("AVA", 96550, 6);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 120 && g.State != GameState.HighScores; i++) g.Idle(1);
        c.Advance(1);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void The_station_map()
    {
        var (w, c, g) = Open();
        g.Go(4);
        // Back to the map, the way a station starts.
        typeof(GameEngine).GetMethod("SetPhase", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(g, [Phase.Map]);
        c.Advance(0.3);
        Shot(w, c, "map");
    }

    [AvaloniaFact]
    public void Drilling_through_a_plug() => Play("drilling", 2, g => Busy(g) && g.World.Ship.Drilling && g.World.Squeeze > 25
        && g.World.Enemies.Count(e => e.Out >= 1) >= 2 && !g.World.InDock
        && g.World.Here.Doors.Any(d => d is { Open: false, Drilled: > 0.35 and < 0.7 }));

    [AvaloniaFact]
    public void A_room_closing_in() => Play("room", 3, g => Busy(g) && g.World.Squeeze > 130 && g.World.Enemies.Count(e => e.Out >= 1) >= 3
        && g.World.Shots.Count(s => !s.Hostile) >= 1,
        // Once it's through the dock, it stays to fight while the walls close in.
        hand: (g, c) => g.World.InDock ? c : c with { Thrust = false });

    [AvaloniaFact]
    public void The_core() => Play("core", 2, g => Busy(g) && g.World.InCore && g.World.Core is { Dead: false } core && core.Hp < core.MaxHp - 2
        && core.Outer.Count(p => p > 0) >= 3 && g.World.Shots.Count >= 4 && g.World.Enemies.Count >= 2);

    [AvaloniaFact]
    public void The_escape() => Play("escape", 1, g => Busy(g) && g.World.Escaping && !g.World.InCore && g.World.Enemies.Count(e => e.Out >= 1) >= 2 && g.World.Squeeze > 30);

    [AvaloniaFact]
    public void The_station_blows() => Play("station-blows", 1, g => g.Phase == Phase.Escaped && g.PhaseTime > 1.4);
}
