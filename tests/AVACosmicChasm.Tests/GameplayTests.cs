using AVACosmicChasm.Engine;
using static AVACosmicChasm.Tests.Harness;

namespace AVACosmicChasm.Tests;

public sealed class GameplayTests
{
    // ───────────────────────────── flying ─────────────────────────────

    [Fact]
    public void Turning_right_turns_clockwise_and_thrust_pushes_the_way_the_nose_points()
    {
        var w = Quiet();
        var s = Park(w);
        Run(w, 20, new Controls(Turn: 1));
        Assert.Equal(20 * Field.Step * Ship.TurnRate, s.Angle, 6);
        Park(w);
        Run(w, 30, new Controls(Thrust: true));
        Assert.True(s.Vy < -100 && Math.Abs(s.Vx) < 1e-9, "pointing up, it goes up the screen");
        Park(w, angle: Math.PI / 2);
        Run(w, 30, new Controls(Thrust: true));
        Assert.True(s.Vx > 100 && Math.Abs(s.Vy) < 1e-6, "pointing right, it goes right");
    }

    [Fact]
    public void Let_go_it_drifts_on_slowing_a_little_and_never_goes_faster_than_its_top_speed()
    {
        var w = Quiet();
        var s = Park(w, angle: Math.PI / 2, x: Field.CX - 300);
        Run(w, 70, new Controls(Thrust: true));
        Assert.False(s.Dead);
        Assert.InRange(s.Speed, Ship.MaxSpeed - 10, Ship.MaxSpeed + 1e-6);
        Park(w, angle: Math.PI / 2, x: Field.CX - 300);
        s.Vx = 200;
        Run(w, 30);
        Assert.InRange(s.Vx, 120, 199);
        Assert.True(s.X > Field.CX - 300 + 50);
    }

    // ───────────────────────────── the walls ─────────────────────────────

    [Fact]
    public void The_walls_close_in_and_crush_the_ship_if_it_is_still_there_when_they_meet()
    {
        var w = Quiet();
        Park(w);
        double l = w.L, t = w.T;
        Run(w, 120);
        Assert.True(w.L > l && w.T > t, "closing in");
        Assert.Equal(w.L - Field.Left, Field.Right - w.R, 6);
        Assert.InRange(Sim.TimeLeft(w), w.Pace.SqueezeSeconds - 2.1, w.Pace.SqueezeSeconds - 1.9);
        w.Squeeze = Field.MaxSqueeze - 0.05;
        Run(w, 5, new Controls(Shield: true));
        Assert.True(w.Ship.Dead, "not even the shield saves it");
    }

    [Fact]
    public void Later_stations_close_faster_and_send_more()
    {
        var a = new Pace(1);
        var b = new Pace(6);
        Assert.True(b.SqueezeSeconds < a.SqueezeSeconds);
        Assert.True(b.SpawnSeconds < a.SpawnSeconds);
        Assert.True(b.MaxEnemies > a.MaxEnemies);
        Assert.True(b.EnemySpeed > a.EnemySpeed);
        Assert.True(b.CoreHp > a.CoreHp);
    }

    [Fact]
    public void Hitting_a_wall_without_the_shield_destroys_the_ship()
    {
        var w = Quiet();
        var s = Park(w, x: Field.Left + 40, y: Field.CY + 160, angle: -Math.PI / 2);
        s.Vx = -200;
        Run(w, 15);
        Assert.True(s.Dead);
        Assert.Contains(Sfx.ShipBoom, w.Heard);
    }

    [Fact]
    public void With_the_shield_up_it_bounces_off_and_the_shield_costs_energy()
    {
        var w = Quiet();
        var s = Park(w, x: Field.Left + 40, y: Field.CY + 160, angle: -Math.PI / 2);
        s.Vx = -200;
        Run(w, 15, new Controls(Shield: true));
        Assert.False(s.Dead);
        Assert.True(s.Vx > 0, "bounced back");
        Assert.True(s.Energy < 1);
        // Let go, it charges again.
        double e = s.Energy;
        Park(w);
        Run(w, 60);
        Assert.True(s.Energy > e);
    }

    [Fact]
    public void The_shield_runs_out()
    {
        var w = Quiet();
        var s = Park(w);
        int steps = 0;
        do Sim.Step(w, new Controls(Shield: true)); while (s.Shielded && ++steps < 600);
        Assert.InRange(steps / 60.0, 1 / Ship.ShieldDrain - 0.3, 1 / Ship.ShieldDrain + 0.1);
        Assert.True(s.Energy < 0.05);
    }

    // ───────────────────────────── doorways ─────────────────────────────

    /// <summary>The ship against a doorway, nose to it.</summary>
    private static Ship AtDoor(World w, Side side, double gap = 2)
    {
        var (nx, ny) = Sim.Inward(side);
        var (x, y) = w.DoorAt(side);
        return Park(w, x + nx * (Ship.Radius + gap), y + ny * (Ship.Radius + gap), Pilot.Heading(-nx, -ny));
    }

    [Fact]
    public void Pushing_the_shield_against_a_plug_drills_through_it_and_the_door_stays_open()
    {
        var w = Quiet();
        var side = PluggedSide(w);
        var door = w.Here.Door(side)!;
        var s = AtDoor(w, side);
        var drill = new Controls(Thrust: true, Shield: true);
        Run(w, (int)(w.Pace.DrillSeconds * 60 / 2), drill);
        Assert.False(s.Dead);
        Assert.True(s.Drilling);
        Assert.InRange(door.Drilled, 0.35, 0.6);
        Assert.False(door.Open);
        Assert.Contains(Sfx.Drill, w.Heard);
        int score = w.Score;
        Run(w, (int)(w.Pace.DrillSeconds * 60 / 2) + 20, drill);
        Assert.True(door.Open);
        Assert.Equal(score + Sim.DoorPoints, w.Score);
        Assert.Contains(Sfx.DoorOpen, w.Heard);
        // Through it, into the next room, where the same doorway is open behind.
        int next = door.Other(w.Room);
        Run(w, 60, new Controls(Thrust: true));
        Assert.Equal(next, w.Room);
        Assert.Equal(side.Opposite(), w.EntrySide);
        Assert.True(w.Here.Door(side.Opposite())!.Open);
        Assert.False(w.Ship.Dead);
    }

    [Fact]
    public void Touching_a_plug_without_the_shield_destroys_the_ship()
    {
        var w = Quiet();
        var side = PluggedSide(w);
        var s = AtDoor(w, side, 6);
        Run(w, 30, new Controls(Thrust: true));
        Assert.True(s.Dead);
        Assert.Equal(0, w.Here.Door(side)!.Drilled);
    }

    [Fact]
    public void A_new_room_starts_with_its_walls_back_and_nothing_in_it_and_its_first_visit_scores()
    {
        var w = Quiet();
        var side = PluggedSide(w);
        w.Here.Door(side)!.Open = true;
        Run(w, 300);
        Sim.Add(w, Foe.Spore, Side.North, 0.6);
        Assert.True(w.Squeeze > 0);
        AtDoor(w, side, 4);
        int score = w.Score;
        Run(w, 40, new Controls(Thrust: true));
        Assert.True(w.Squeeze < 5);
        Assert.Empty(w.Enemies);
        Assert.True(w.Here.Visited);
        Assert.Equal(score + Sim.RoomPoints, w.Score);
        Assert.Contains(Sfx.Warp, w.Heard);
    }

    [Fact]
    public void The_airlock_stays_shut_until_the_core_is_gone_and_then_it_is_the_way_out()
    {
        var w = Quiet();
        Assert.True(w.InDock);
        var side = w.Station.DockSide;
        var s = AtDoor(w, side);
        Run(w, 60, new Controls(Thrust: true, Shield: true));
        Assert.False(w.Escaped);
        Assert.False(s.Dead);
        Assert.True(w.InDock);
        w.Escaping = true;
        w.EscapeTime = 30;
        AtDoor(w, side, 4);
        Run(w, 60, new Controls(Thrust: true));
        Assert.True(w.Escaped);
    }

    // ───────────────────────────── shooting and enemies ─────────────────────────────

    [Fact]
    public void A_shot_flies_from_the_nose_and_destroys_what_it_hits()
    {
        var w = Quiet();
        Park(w);
        var e = Sim.Add(w, Foe.Spore, Side.North, 0);
        e.Out = 1;
        (e.X, e.Y) = (Field.CX, Field.CY - 200);
        Sim.Step(w, new Controls(Fire: true));
        var shot = Assert.Single(w.Shots);
        Assert.True(shot.Vy < -600);
        Run(w, 30);
        Assert.Empty(w.Enemies);
        Assert.Equal(Enemy.Points(Foe.Spore), w.Score);
        Assert.Equal(1, w.Kills);
    }

    [Fact]
    public void A_crawler_takes_two_shots()
    {
        var w = Quiet();
        Park(w, y: Field.CY + 100);
        var e = Sim.Add(w, Foe.Crawler, Side.North, 0);
        e.Vx = 0;
        e.Reload = 100;
        Run(w, 60);
        e.Along = 0;
        Sim.Step(w, new Controls(Fire: true));
        Run(w, 40);
        Assert.Single(w.Enemies);
        Sim.Step(w, new Controls(Fire: true));
        Run(w, 40);
        Assert.Empty(w.Enemies);
    }

    [Fact]
    public void Holding_fire_keeps_shooting_but_only_a_few_shots_fly_at_once_and_none_through_the_shield()
    {
        var w = Quiet();
        Park(w, y: Field.Bottom - 60);
        int fired = 0;
        for (int i = 0; i < 120; i++)
        {
            Sim.Step(w, new Controls(Fire: true));
            fired += w.Heard.Count(x => x == Sfx.Fire);
            w.Heard.Clear();
            Assert.True(w.Shots.Count <= Sim.MaxShots);
        }
        Assert.True(fired >= 8, $"{fired}");
        Park(w);
        w.Shots.Clear();
        Run(w, 30, new Controls(Fire: true, Shield: true));
        Assert.Empty(w.Shots);
    }

    [Fact]
    public void Shots_stop_at_the_walls()
    {
        var w = Quiet();
        Park(w, y: Field.CY + 160, angle: -Math.PI / 2);
        Sim.Step(w, new Controls(Fire: true));
        Run(w, 50);
        Assert.Empty(w.Shots);
    }

    [Fact]
    public void Enemies_come_out_of_the_walls_but_not_out_of_a_doorway_or_beside_the_ship()
    {
        var w = Quiet();
        w.Spawning = true;
        Park(w);
        for (int i = 0; i < 60 * 40; i++)
        {
            Sim.Step(w, default);
            w.Ship.Safe = 1;
            w.Squeeze = 0;
            foreach (var e in w.Enemies.Where(e => e.Age < Field.Step * 1.5))
            {
                var (x, y) = w.WallPoint(e.Wall, e.Along);
                double off = e.Wall is Side.North or Side.South ? x - Field.CX : y - Field.CY;
                Assert.True(Math.Abs(off) > Field.DoorHalf);
                Assert.True(Math.Abs(x - w.Ship.X) + Math.Abs(y - w.Ship.Y) >= 220);
            }
        }
        Assert.Equal(w.Pace.MaxEnemies, w.Enemies.Count);
        Assert.Contains(Sfx.Emerge, w.Heard);
    }

    [Fact]
    public void A_crawler_keeps_to_its_wall_and_fires_at_the_ship()
    {
        var w = Quiet();
        Park(w, y: Field.CY + 120);
        var e = Sim.Add(w, Foe.Crawler, Side.North, 0.3);
        e.Reload = 1;
        Run(w, 150);
        Assert.Equal(w.T + e.Radius, e.Y, 3);
        Assert.Contains(w.Shots, x => x.Hostile && x.Vy > 0);
        Assert.Contains(Sfx.EnemyFire, w.Heard);
    }

    [Fact]
    public void Spores_and_spinners_come_after_the_ship()
    {
        foreach (var kind in new[] { Foe.Spore, Foe.Spinner })
        {
            var w = Quiet();
            Park(w, x: Field.CX + 200);
            w.Ship.Safe = 100;
            var e = Sim.Add(w, kind, Side.West, 0.5);
            Run(w, 60);
            double Gap() => Math.Sqrt((e.X - w.Ship.X) * (e.X - w.Ship.X) + (e.Y - w.Ship.Y) * (e.Y - w.Ship.Y));
            double d0 = Gap(), nearest = d0;
            for (int i = 0; i < 240; i++) { Sim.Step(w, default); nearest = Math.Min(nearest, Gap()); }
            Assert.True(nearest < d0 - 150, $"{kind}: {d0} to {nearest}");
        }
    }

    [Fact]
    public void Running_into_an_enemy_destroys_the_ship_but_the_shield_smashes_it()
    {
        var w = Quiet();
        Park(w);
        var e = Sim.Add(w, Foe.Spore, Side.North, 0);
        (e.X, e.Y, e.Out) = (Field.CX + 10, Field.CY, 1);
        Sim.Step(w, default);
        Assert.True(w.Ship.Dead);
        w = Quiet();
        Park(w);
        e = Sim.Add(w, Foe.Spore, Side.North, 0);
        (e.X, e.Y, e.Out) = (Field.CX + 10, Field.CY, 1);
        Sim.Step(w, new Controls(Shield: true));
        Assert.False(w.Ship.Dead);
        Assert.Empty(w.Enemies);
        Assert.Equal(Enemy.Points(Foe.Spore), w.Score);
    }

    [Fact]
    public void Enemy_fire_destroys_the_ship_but_the_shield_stops_it()
    {
        foreach (bool shield in new[] { false, true })
        {
            var w = Quiet();
            Park(w);
            w.Shots.Add(new Shot { X = Field.CX - 60, Y = Field.CY, Vx = Shot.EnemySpeed, Hostile = true });
            Run(w, 20, new Controls(Shield: shield));
            Assert.Equal(!shield, w.Ship.Dead);
            Assert.Empty(w.Shots);
        }
    }

    [Fact]
    public void Just_through_a_doorway_or_back_to_life_it_is_safe_for_a_moment()
    {
        var w = Quiet();
        Sim.Revive(w);
        Assert.True(w.Ship.Safe > 1);
        var e = Sim.Add(w, Foe.Spore, Side.North, 0);
        (e.X, e.Y, e.Out) = (w.Ship.X, w.Ship.Y, 1);
        Sim.Step(w, default);
        Assert.False(w.Ship.Dead);
    }

    // ───────────────────────────── the core ─────────────────────────────

    private static (World W, Core C) AtCore()
    {
        var w = Quiet();
        Into(w, w.Station.CoreRoom, Side.West);
        var c = w.Core!;
        c.Reload = 1000;
        Park(w, Field.CX - 250, Field.CY, Math.PI / 2);
        return (w, c);
    }

    [Fact]
    public void The_core_s_rings_stop_shots_until_plates_are_shot_away_and_the_plates_grow_back()
    {
        var (w, c) = AtCore();
        Assert.True(w.InCore);
        Sim.Step(w, new Controls(Fire: true));
        Run(w, 40);
        Assert.Equal(c.MaxHp, c.Hp);
        int gone = c.Outer.Count(p => p > 0) + c.Inner.Count(p => p > 0);
        Assert.Equal(1, gone);
        Assert.Equal(Sim.PlatePoints, w.Score);
        Run(w, (int)(Core.Regrow * 60) + 5);
        Assert.Equal(0, c.Outer.Count(p => p > 0) + c.Inner.Count(p => p > 0));
    }

    [Fact]
    public void A_shot_through_a_gap_in_both_rings_hits_the_core()
    {
        var (w, c) = AtCore();
        Array.Fill(c.Outer, 100);
        Array.Fill(c.Inner, 100);
        Sim.Step(w, new Controls(Fire: true));
        Run(w, 40);
        Assert.Equal(c.MaxHp - 1, c.Hp);
        Assert.Contains(Sfx.CoreHit, w.Heard);
    }

    [Fact]
    public void Destroying_the_core_starts_the_countdown_and_the_way_out_is_the_dock()
    {
        var (w, c) = AtCore();
        Array.Fill(c.Outer, 100);
        Array.Fill(c.Inner, 100);
        c.Hp = 1;
        Sim.Step(w, new Controls(Fire: true));
        Run(w, 40);
        Assert.True(c.Dead && w.CoreDestroyed && w.Escaping);
        Assert.InRange(w.EscapeTime, Sim.EscapeSeconds(w.Station) - 1, Sim.EscapeSeconds(w.Station));
        Assert.True(w.Score >= Sim.CorePoints(1));
        Assert.Contains(Sfx.CoreBoom, w.Heard);
        double t = w.EscapeTime;
        Run(w, 60);
        Assert.Equal(t - 1, w.EscapeTime, 1);
    }

    [Fact]
    public void The_core_fires_at_the_ship_and_its_field_is_as_hard_as_a_wall()
    {
        var (w, c) = AtCore();
        c.Reload = 0.5;
        Run(w, 40);
        Assert.Equal(3, w.Shots.Count(x => x.Hostile));
        w.Shots.Clear();
        c.Reload = 1000;
        var s = Park(w, Field.CX - Core.OuterRing - 40, Field.CY, Math.PI / 2);
        s.Vx = 200;
        Run(w, 20);
        Assert.True(s.Dead);
    }

    [Fact]
    public void The_escape_time_allows_for_the_rooms_between_the_core_and_the_dock()
    {
        var s = new Station(5, 2);
        Assert.Equal(12 + 6 * s.Distances(s.CoreRoom)[s.DockRoom], Sim.EscapeSeconds(s));
    }

    [Fact]
    public void Extra_ships_at_ten_thousand_and_every_twenty_five_thousand_after()
    {
        var w = Quiet();
        Sim.AddScore(w, Sim.FirstExtra - 1);
        Assert.Equal(3, w.Lives);
        Sim.AddScore(w, 1);
        Assert.Equal(4, w.Lives);
        Assert.Contains(Sfx.ExtraLife, w.Heard);
        Sim.AddScore(w, Sim.ExtraEvery);
        Assert.Equal(5, w.Lives);
    }
}
