namespace AVACosmicChasm.Engine;

/// <summary>
/// The computer player, for the demonstration and the tests. It works out the way through the station (to the core,
/// or back to the docking bay once the core is gone) and the doorway to make for in this room, then the velocity it
/// wants: towards that doorway, lined up with it, and away from walls, enemies and the core's field. It turns the
/// ship to thrust that way when it's far off, and otherwise turns to shoot the nearest enemy (or the core). It raises
/// the shield to drill a plug, and when a shot or an enemy is about to hit or a wall is coming up too fast.
/// </summary>
public sealed class Pilot
{
    public string Doing { get; private set; } = "";

    public void Reset() => Doing = "";

    /// <summary>The ship's heading for a direction on the screen (0 is up, clockwise).</summary>
    public static double Heading(double x, double y) => Math.Atan2(x, -y);

    private static double Diff(double want, double have) => Math.IEEERemainder(want - have, Math.PI * 2);

    public Controls Decide(GameEngine g)
    {
        var w = g.World;
        var s = w.Ship;
        if (g.State is not (GameState.Playing or GameState.Attract) || g.Phase != Phase.Fight || s.Dead) return default;
        var st = w.Station;
        bool coreMode = !w.Escaping && w.InCore && w.Core is { Dead: false };
        Side? exit = w.Escaping && w.InDock ? st.DockSide : coreMode ? null : st.WayTo(w.Room, w.Escaping ? st.DockRoom : st.CoreRoom);

        double vx, vy;
        bool drill = false, final = false;
        if (exit is { } way)
        {
            var (nx, ny) = Sim.Inward(way);
            var (dx, dy) = w.DoorAt(way);
            double depth = (s.X - dx) * nx + (s.Y - dy) * ny;
            double off = way is Side.North or Side.South ? s.X - Field.CX : s.Y - Field.CY;
            bool open = w.ExitOpen(way);
            final = Math.Abs(off) < 26 && depth < 170;
            double gx, gy, speed;
            if (!final)
            {
                // Line up in front of the doorway first.
                gx = dx + nx * 110;
                gy = dy + ny * 110;
                speed = 240;
            }
            else
            {
                gx = dx - nx * 60;
                gy = dy - ny * 60;
                speed = open ? 230 : 110;
                drill = !open && depth < Ship.Radius + 26;
            }
            (vx, vy) = Toward(s, gx, gy, speed);
            // Keep to the doorway's middle on the final run.
            if (final)
            {
                if (way is Side.North or Side.South) vx = -off * 3; else vy = -off * 3;
            }
            Doing = drill ? "drilling" : final ? "through" : "lining up";
        }
        else
        {
            // At the core: hang off to one side of it, where the room is widest, and shoot.
            double gx = Core.X + (s.X < Core.X ? -1 : 1) * 250;
            (vx, vy) = Toward(s, gx, Core.Y, 200);
            Doing = "core";
        }
        // Away from the walls (but not the one it's going out through).
        foreach (var side in Sides.All)
        {
            if (final && side == exit) continue;
            var (nx, ny) = Sim.Inward(side);
            var (dx, dy) = w.DoorAt(side);
            double gap = (s.X - dx) * nx + (s.Y - dy) * ny - Ship.Radius;
            if (gap < 80) { vx += nx * (80 - gap) * 4; vy += ny * (80 - gap) * 4; }
        }
        foreach (var (left, top) in new[] { (true, true), (false, true), (true, false), (false, false) })
        {
            double u = left ? s.X - w.L : w.R - s.X, v = top ? s.Y - w.T : w.B - s.Y;
            double gap = (u + v - Field.Bevel) / Math.Sqrt(2) - Ship.Radius;
            if (gap < 60) { vx += (left ? 1 : -1) * (60 - gap) * 3; vy += (top ? 1 : -1) * (60 - gap) * 3; }
        }
        // Away from enemies that are close.
        Enemy? target = null;
        double best = double.MaxValue;
        foreach (var e in w.Enemies)
        {
            double ex = s.X - e.X, ey = s.Y - e.Y, d = Math.Sqrt(ex * ex + ey * ey);
            if (d < 150 && e.Out >= 1 && !drill) { vx += ex / d * (150 - d) * 2.5; vy += ey / d * (150 - d) * 2.5; }
            double rank = d - (e.Kind == Foe.Spinner ? 80 : 0);
            if (e.Out > 0.3 && rank < best) { best = rank; target = e; }
        }
        if (w.InCore && w.Core is { Dead: false })
        {
            double cx = s.X - Core.X, cy = s.Y - Core.Y, d = Math.Sqrt(cx * cx + cy * cy);
            double gap = d - Core.OuterRing - Ship.Radius;
            if (gap < 90) { vx += cx / d * (90 - gap) * 4; vy += cy / d * (90 - gap) * 4; }
        }

        double needX = vx - s.Vx, needY = vy - s.Vy, need = Math.Sqrt(needX * needX + needY * needY);
        int turn = 0;
        bool thrust = false, fire = false;
        double aimAt = double.NaN;
        if (target != null && best < 420)
        {
            double t = best / Shot.Speed;
            aimAt = Heading(target.X + target.Vx * t - s.X, target.Y + target.Vy * t - s.Y);
        }
        else if (coreMode) aimAt = Heading(Core.X - s.X, Core.Y - s.Y);
        bool busy = need > (drill ? 25 : 70) || double.IsNaN(aimAt);
        if (busy)
        {
            double want = drill ? Heading(-Sim.Inward(exit!.Value).X, -Sim.Inward(exit.Value).Y) : Heading(needX, needY);
            double diff = Diff(want, s.Angle);
            turn = Math.Abs(diff) < 0.05 ? 0 : Math.Sign(diff);
            thrust = Math.Abs(diff) < 0.5 && (need > 25 || drill);
        }
        else
        {
            double diff = Diff(aimAt, s.Angle);
            turn = Math.Abs(diff) < 0.04 ? 0 : Math.Sign(diff);
            fire = Math.Abs(diff) < 0.12;
        }
        // Anything in its sights gets a shot anyway.
        foreach (var e in w.Enemies)
        {
            double ex = e.X - s.X, ey = e.Y - s.Y, d = Math.Sqrt(ex * ex + ey * ey);
            if (d < 520 && Math.Abs(Diff(Heading(ex, ey), s.Angle)) < Math.Max(0.06, Math.Atan2(e.Radius, d))) fire = true;
        }
        if (coreMode && Math.Abs(Diff(Heading(Core.X - s.X, Core.Y - s.Y), s.Angle)) < 0.1) fire = true;
        bool shield = drill || Danger(w);
        if (shield && !drill) Doing = "shielding";
        return new Controls(turn, thrust, fire && !shield, shield);
    }

    /// <summary>Whether something is about to hit the ship: a shot, an enemy, or a wall coming up fast.</summary>
    private static bool Danger(World w)
    {
        var s = w.Ship;
        if (s.Safe > 0.2) return false;
        foreach (var x in w.Shots)
        {
            if (!x.Hostile) continue;
            // Where it passes closest, in the next third of a second.
            double rx = x.X - s.X, ry = x.Y - s.Y, vx = x.Vx - s.Vx, vy = x.Vy - s.Vy;
            double v2 = vx * vx + vy * vy;
            double t = v2 <= 0 ? 0 : Math.Clamp(-(rx * vx + ry * vy) / v2, 0, 0.35);
            double cx = rx + vx * t, cy = ry + vy * t;
            if (cx * cx + cy * cy < 24 * 24) return true;
        }
        foreach (var e in w.Enemies)
        {
            double rx = e.X - s.X, ry = e.Y - s.Y, vx = e.Vx - s.Vx, vy = e.Vy - s.Vy;
            double v2 = vx * vx + vy * vy;
            double t = v2 <= 0 ? 0 : Math.Clamp(-(rx * vx + ry * vy) / v2, 0, 0.3);
            double cx = rx + vx * t, cy = ry + vy * t, reach = e.Radius + Ship.Radius + 10;
            if (cx * cx + cy * cy < reach * reach) return true;
        }
        // A wall coming up too fast to stop for.
        foreach (var side in Sides.All)
        {
            var (nx, ny) = Sim.Inward(side);
            var (dx, dy) = w.DoorAt(side);
            double gap = (s.X - dx) * nx + (s.Y - dy) * ny - Ship.Radius;
            double closing = -(s.Vx * nx + s.Vy * ny);
            double off = side is Side.North or Side.South ? s.X - Field.CX : s.Y - Field.CY;
            if (w.ExitOpen(side) && Math.Abs(off) < Field.DoorHalf - 14) continue;
            if (closing > 0 && gap < 4 + closing * 0.12) return true;
        }
        foreach (var (left, top) in new[] { (true, true), (false, true), (true, false), (false, false) })
        {
            double u = left ? s.X - w.L : w.R - s.X, v = top ? s.Y - w.T : w.B - s.Y;
            double gap = (u + v - Field.Bevel) / Math.Sqrt(2) - Ship.Radius;
            double closing = -((left ? 1 : -1) * s.Vx + (top ? 1 : -1) * s.Vy) / Math.Sqrt(2);
            if (closing > 0 && gap < 4 + closing * 0.12) return true;
        }
        if (w.InCore && w.Core is { Dead: false })
        {
            double cx = s.X - Core.X, cy = s.Y - Core.Y, d = Math.Sqrt(cx * cx + cy * cy);
            double closing = -(s.Vx * cx + s.Vy * cy) / d;
            if (closing > 0 && d - Core.OuterRing - 6 - Ship.Radius < 4 + closing * 0.12) return true;
        }
        return false;
    }

    private static (double X, double Y) Toward(Ship s, double gx, double gy, double speed)
    {
        double dx = gx - s.X, dy = gy - s.Y, d = Math.Sqrt(dx * dx + dy * dy);
        if (d < 1) return (0, 0);
        double v = Math.Min(speed, d * 1.6);
        return (dx / d * v, dy / d * v);
    }
}
