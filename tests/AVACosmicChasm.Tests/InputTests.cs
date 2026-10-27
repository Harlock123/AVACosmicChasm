using Avalonia.Input;
using AVACosmicChasm.Engine;
using static AVACosmicChasm.Tests.Harness;

namespace AVACosmicChasm.Tests;

/// <summary>The keys, through the input handler and into the game.</summary>
public sealed class InputTests
{
    /// <summary>Holds keys down through the handler for a while, from the middle of a quiet room, pointing up.</summary>
    private static (World W, Ship S, int Shots) Hold(InputHandler input, int steps, params Key[] keys)
    {
        var w = Quiet();
        var s = Park(w);
        foreach (var k in keys) input.OnKeyDown(k);
        int shots = 0;
        for (int i = 0; i < steps; i++)
        {
            Sim.Step(w, input.Player);
            shots += w.Heard.Count(x => x == Sfx.Fire);
            w.Heard.Clear();
        }
        foreach (var k in keys) input.OnKeyUp(k);
        return (w, s, shots);
    }

    [Fact]
    public void Right_turns_the_ship_clockwise_and_left_anticlockwise()
    {
        using var input = new InputHandler();
        foreach (var key in new[] { Key.Right, Key.D }) Assert.True(Hold(input, 20, key).S.Angle > 1, $"{key}");
        foreach (var key in new[] { Key.Left, Key.A }) Assert.True(Hold(input, 20, key).S.Angle < -1, $"{key}");
        Assert.Equal(0, Hold(input, 20).S.Angle);
    }

    [Fact]
    public void Up_thrusts_it_the_way_it_points_which_starts_as_up_the_screen()
    {
        using var input = new InputHandler();
        foreach (var key in new[] { Key.Up, Key.W })
        {
            var (_, s, _) = Hold(input, 30, key);
            Assert.True(s.Y < Field.CY - 20, $"{key}");
            Assert.Equal(Field.CX, s.X, 6);
        }
        var turned = Hold(input, 40, Key.Right, Key.Up).S;
        Assert.True(turned.X > Field.CX + 10, "turned to the right first, it goes right");
    }

    [Fact]
    public void Space_Z_and_Ctrl_fire_and_holding_one_keeps_firing()
    {
        using var input = new InputHandler();
        foreach (var key in new[] { Key.Space, Key.Z, Key.LeftCtrl, Key.RightCtrl })
            Assert.True(Hold(input, 40, key).Shots >= 3, $"{key}");
        Assert.Equal(0, Hold(input, 40, Key.Q).Shots);
    }

    [Fact]
    public void Down_S_X_and_Shift_raise_the_shield()
    {
        using var input = new InputHandler();
        foreach (var key in new[] { Key.Down, Key.S, Key.X, Key.LeftShift, Key.RightShift })
        {
            var (_, s, _) = Hold(input, 5, key);
            Assert.True(s.Shielded, $"{key}");
        }
        Assert.False(Hold(input, 5).S.Shielded);
    }

    [Fact]
    public void Thrust_and_shield_together_drill_a_plug()
    {
        using var input = new InputHandler();
        var w = Quiet();
        var side = PluggedSide(w);
        var (nx, ny) = Sim.Inward(side);
        var (x, y) = w.DoorAt(side);
        Park(w, x + nx * (Ship.Radius + 2), y + ny * (Ship.Radius + 2), Pilot.Heading(-nx, -ny));
        input.OnKeyDown(Key.Up);
        input.OnKeyDown(Key.LeftShift, PhysicalKey.ShiftLeft);
        for (int i = 0; i < 120; i++) Sim.Step(w, input.Player);
        Assert.True(w.Here.Door(side)!.Open || w.Room != w.Station.DockRoom);
        // Let go of the Shift (it comes up as Caps Lock here): the shield drops.
        input.OnKeyUp(Key.CapsLock, PhysicalKey.ShiftLeft);
        Assert.False(input.Player.Shield);
    }

    [Fact]
    public void Enter_starts()
    {
        using var input = new InputHandler();
        Assert.False(input.Start);
        input.OnKeyDown(Key.Enter);
        Assert.True(input.Start);
    }
}
