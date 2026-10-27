namespace AVACosmicChasm.Engine;

/// <summary>
/// The rules, one 60th of a second at a time: the ship turning, thrusting and firing, the walls closing in, the
/// drill boring through plugged doorways, enemies coming out of the walls, the core and its shield rings, and the
/// escape.
/// </summary>
public static class Sim
{
    public const int FirstExtra = 10000, ExtraEvery = 25000;
    public const int RoomPoints = 100, DoorPoints = 50, PlatePoints = 10, CoreHitPoints = 50;
    public const int MaxShots = 5;
    public const double ReloadSeconds = 0.14;
    /// <summary>How far past a doorway's wall line the ship flies before it's in the next room.</summary>
    public const double PassDepth = 26;

    public static int CorePoints(int station) => 2000 + 500 * (station - 1);

    /// <summary>The seconds to get from the core to the docking bay: a little for every room on the way.</summary>
    public static double EscapeSeconds(Station s) => 12 + 6 * s.Distances(s.CoreRoom)[s.DockRoom];

    public static void Start(World w, int seed)
    {
        w.Dice = new Dice(seed);
        w.Score = 0;
        w.Lives = 3;
        w.NextExtra = FirstExtra;
        BeginStation(w, 1, seed);
    }

    /// <summary>A new station: the ship comes in through the docking bay's airlock.</summary>
    public static void BeginStation(World w, int number, int seed)
    {
        w.Station = new Station(number, seed);
        w.Core = new Core { MaxHp = w.Pace.CoreHp, Hp = w.Pace.CoreHp };
        w.CoreDestroyed = w.Escaping = w.Escaped = false;
        w.EscapeTime = 0;
        w.Ship = new Ship();
        EnterRoom(w, w.Station.DockRoom, w.Station.DockSide);
        w.Ship.Vx = w.Ship.Vy = 0;
        w.Ship.Safe = 1.5;
    }

    /// <summary>Into a room through one of its walls: its walls start wide open again, and it's empty for a moment.</summary>
    public static void EnterRoom(World w, int room, Side from)
    {
        bool first = !w.Station[room].Visited;
        w.Room = room;
        w.Station[room].Visited = true;
        w.EntrySide = from;
        Reset(w);
        var s = w.Ship;
        var (dx, dy) = Inward(from);
        var (x, y) = w.DoorAt(from);
        s.X = x + dx * 46;
        s.Y = y + dy * 46;
        s.Angle = Math.Atan2(dx, -dy);
        // It keeps its way on, into the room.
        double v = Math.Max(70, s.Vx * dx + s.Vy * dy);
        s.Vx = dx * v;
        s.Vy = dy * v;
        s.Safe = Math.Max(s.Safe, 0.8);
        if (first && w.Clock > 0 && room != w.Station.DockRoom) AddScore(w, RoomPoints);
        w.Heard.Add(Sfx.Warp);
    }

    /// <summary>The room as it is when the ship comes in: walls back, no enemies, the first coming soon.</summary>
    private static void Reset(World w)
    {
        w.Squeeze = 0;
        w.Enemies.Clear();
        w.Shots.Clear();
        w.Blasts.Clear();
        w.SpawnClock = 1.2;
        w.Alarm = 0;
    }

    /// <summary>Back to life after a crash: at the doorway it came in by, the room set back.</summary>
    public static void Revive(World w)
    {
        w.Ship = new Ship();
        Reset(w);
        var (dx, dy) = Inward(w.EntrySide);
        var (x, y) = w.DoorAt(w.EntrySide);
        var s = w.Ship;
        s.X = x + dx * 60;
        s.Y = y + dy * 60;
        s.Angle = Math.Atan2(dx, -dy);
        s.Safe = 2.5;
    }

    /// <summary>The direction into the room from a wall.</summary>
    public static (double X, double Y) Inward(Side s) => s switch
    {
        Side.North => (0, 1),
        Side.South => (0, -1),
        Side.East => (-1, 0),
        _ => (1, 0),
    };

    public static void AddScore(World w, int points)
    {
        w.Score += points;
        while (w.Score >= w.NextExtra)
        {
            w.Lives++;
            w.NextExtra += ExtraEvery;
            w.Heard.Add(Sfx.ExtraLife);
        }
    }

    private static void Float(World w, double x, double y, string text) => w.Floaters.Add(new Floater { X = x, Y = y, Text = text });

    private static void Boom(World w, double x, double y, double size, uint colour, double life = 0.7)
    {
        w.Blasts.Add(new Blast { X = x, Y = y, Size = size, Colour = colour, Life = life, Seed = w.Dice.Next(1000) });
    }

    /// <summary>Seconds until the walls close right in.</summary>
    public static double TimeLeft(World w) => (Field.MaxSqueeze - w.Squeeze) / SqueezeRate(w);

    public static double SqueezeRate(World w) => Field.MaxSqueeze / (w.Pace.SqueezeSeconds * (w.InCore ? 1.4 : 1));

    public static void Step(World w, Controls c)
    {
        double dt = Field.Step;
        w.Clock += dt;
        var s = w.Ship;
        int room = w.Room;
        if (!s.Dead)
        {
            w.Squeeze = Math.Min(Field.MaxSqueeze, w.Squeeze + SqueezeRate(w) * dt);
            double left = TimeLeft(w);
            w.Alarm -= dt;
            if (w.Escaping)
            {
                w.EscapeTime = Math.Max(0, w.EscapeTime - dt);
                if (w.Alarm <= 0) { w.Heard.Add(Sfx.Siren); w.Alarm = 1; }
            }
            else if (left < 6 && w.Alarm <= 0) { w.Heard.Add(Sfx.Alarm); w.Alarm = left < 2.5 ? 0.35 : 0.7; }
            if (w.Squeeze >= Field.MaxSqueeze)
            {
                // The walls have met: nothing inside comes out.
                Crash(w);
            }
        }
        if (!s.Dead) Fly(w, c);
        if (w.Room != room || w.Escaped) return; // gone through a doorway
        Spawn(w);
        foreach (var e in w.Enemies) Move(w, e);
        if (w.InCore && w.Core is { Dead: false } core) Heart(w, core);
        Shots(w);
        if (!s.Dead) Touch(w);
        foreach (var b in w.Blasts) b.Age += dt;
        w.Blasts.RemoveAll(b => b.Age > b.Life);
        foreach (var f in w.Floaters) { f.Age += dt; f.Y -= 30 * dt; }
        w.Floaters.RemoveAll(f => f.Age > 1.2);
        w.Enemies.RemoveAll(e => e.Gone);
        w.Shots.RemoveAll(x => x.Gone);
    }

    // ───────────────────────────── the ship ─────────────────────────────

    private static void Fly(World w, Controls c)
    {
        double dt = Field.Step;
        var s = w.Ship;
        s.Safe = Math.Max(0, s.Safe - dt);
        s.Reload -= dt;
        s.Angle += c.Turn * Ship.TurnRate * dt;
        s.Thrusting = c.Thrust;
        if (c.Thrust)
        {
            s.Vx += s.Nx * Ship.Thrust * dt;
            s.Vy += s.Ny * Ship.Thrust * dt;
        }
        // A little drag, so it can be brought under control.
        double drag = Math.Pow(0.55, dt);
        s.Vx *= drag;
        s.Vy *= drag;
        double v = s.Speed;
        if (v > Ship.MaxSpeed) { s.Vx *= Ship.MaxSpeed / v; s.Vy *= Ship.MaxSpeed / v; }
        bool was = s.Shielded;
        s.Shielded = c.Shield && s.Energy > 0.02;
        if (s.Shielded)
        {
            s.Energy = Math.Max(0, s.Energy - Ship.ShieldDrain * dt);
            if (!was) w.Heard.Add(Sfx.Shield);
        }
        else s.Energy = Math.Min(1, s.Energy + Ship.ShieldRecharge * dt);
        // No firing through the shield.
        if (c.Fire && !s.Shielded && s.Reload <= 0 && w.Shots.Count(x => !x.Hostile) < MaxShots)
        {
            w.Shots.Add(new Shot
            {
                X = s.X + s.Nx * 16, Y = s.Y + s.Ny * 16,
                Vx = s.Nx * Shot.Speed + s.Vx * 0.4, Vy = s.Ny * Shot.Speed + s.Vy * 0.4,
            });
            s.Reload = ReloadSeconds;
            w.Heard.Add(Sfx.Fire);
        }
        s.X += s.Vx * dt;
        s.Y += s.Vy * dt;
        s.Drilling = false;
        Walls(w);
    }

    /// <summary>The ship against the walls: through an open doorway, drilling a plug, bouncing off with the shield up,
    /// or smashed without it.</summary>
    private static void Walls(World w)
    {
        var s = w.Ship;
        const double r = Ship.Radius;
        foreach (var side in Sides.All)
        {
            var (nx, ny) = Inward(side);
            var (wx, wy) = w.DoorAt(side);
            // How far it's into the wall, and how far off the doorway's middle.
            double depth = r - ((s.X - wx) * nx + (s.Y - wy) * ny);
            double off = side is Side.North or Side.South ? s.X - Field.CX : s.Y - Field.CY;
            if (depth <= 0) continue;
            bool inGap = Math.Abs(off) < Field.DoorHalf;
            if (inGap && w.ExitOpen(side))
            {
                // In the doorway: its sides guide the ship through.
                if (depth > r)
                {
                    double lim = Field.DoorHalf - r * 0.7;
                    if (Math.Abs(off) > lim)
                    {
                        double fix = Math.Sign(off) * lim - off;
                        if (side is Side.North or Side.South) { s.X += fix; s.Vx = 0; } else { s.Y += fix; s.Vy = 0; }
                    }
                }
                if (depth - r > PassDepth) { GoThrough(w, side); return; }
                continue;
            }
            if (inGap && w.Here.Door(side) is { Open: false } door && s.Shielded)
            {
                // The drill bites into the plug.
                s.Drilling = true;
                Push(s, nx, ny, depth, 0);
                door.Drilled += Field.Step / w.Pace.DrillSeconds;
                if (w.Clock % 0.12 < Field.Step) w.Heard.Add(Sfx.Drill);
                if (door.Drilled >= 1)
                {
                    door.Drilled = 1;
                    door.Open = true;
                    AddScore(w, DoorPoints);
                    Boom(w, wx, wy, 60, 0xFFFFFFFF, 0.6);
                    w.Heard.Add(Sfx.DoorOpen);
                }
                continue;
            }
            Hit(w, nx, ny, depth);
            if (s.Dead) return;
        }
        // The cut-off corners.
        foreach (var (left, top) in new[] { (true, true), (false, true), (true, false), (false, false) })
        {
            double u = left ? s.X - w.L : w.R - s.X, v = top ? s.Y - w.T : w.B - s.Y;
            if (u > Field.Bevel + r || v > Field.Bevel + r) continue;
            double d = (u + v - Field.Bevel) / Math.Sqrt(2);
            if (d >= r) continue;
            Hit(w, (left ? 1 : -1) / Math.Sqrt(2), (top ? 1 : -1) / Math.Sqrt(2), r - d);
            if (s.Dead) return;
        }
    }

    /// <summary>Pushes the ship out of a wall and takes away its speed into it (a bounce keeps some, turned round).</summary>
    private static void Push(Ship s, double nx, double ny, double depth, double bounce)
    {
        s.X += nx * depth;
        s.Y += ny * depth;
        double into = s.Vx * nx + s.Vy * ny;
        if (into < 0)
        {
            s.Vx -= (1 + bounce) * into * nx;
            s.Vy -= (1 + bounce) * into * ny;
        }
    }

    /// <summary>The ship meets a wall: the shield bounces it off; without it, it's smashed.</summary>
    private static void Hit(World w, double nx, double ny, double depth)
    {
        var s = w.Ship;
        if (s.Shielded || s.Safe > 0)
        {
            double into = s.Vx * nx + s.Vy * ny;
            Push(s, nx, ny, depth, 0.7);
            if (into < -30)
            {
                w.Heard.Add(Sfx.Bounce);
                if (s.Shielded) s.Energy = Math.Max(0, s.Energy - 0.04);
            }
            return;
        }
        Crash(w);
    }

    /// <summary>The ship is destroyed.</summary>
    public static void Crash(World w)
    {
        var s = w.Ship;
        if (s.Dead) return;
        s.Dead = true;
        s.Shielded = s.Thrusting = s.Drilling = false;
        Boom(w, s.X, s.Y, 90, 0xFFFFFFFF, 1.6);
        Boom(w, s.X, s.Y, 50, 0xFF60E0FF, 1.2);
        w.Heard.Add(Sfx.ShipBoom);
    }

    private static void GoThrough(World w, Side side)
    {
        if (w.InDock && side == w.Station.DockSide)
        {
            // Out through the airlock, and away.
            w.Escaped = true;
            w.Ship.Thrusting = false;
            return;
        }
        var door = w.Here.Door(side)!;
        EnterRoom(w, door.Other(w.Room), side.Opposite());
    }

    // ───────────────────────────── enemies ─────────────────────────────

    /// <summary>Something comes out of the walls now and then, never right beside the ship.</summary>
    private static void Spawn(World w)
    {
        if (!w.Spawning || w.Ship.Dead) return;
        w.SpawnClock -= Field.Step;
        if (w.SpawnClock > 0) return;
        var pace = w.Pace;
        w.SpawnClock = pace.SpawnSeconds * w.Dice.Between(0.7, 1.3);
        if (w.Enemies.Count >= pace.MaxEnemies) return;
        int n = w.Station.Number;
        double roll = w.Dice.Next();
        var kind = n >= 2 && roll < 0.15 ? Foe.Mine
            : roll < 0.45 ? Foe.Spore
            : roll < 0.72 ? Foe.Spinner
            : Foe.Crawler;
        if (w.InCore && kind == Foe.Crawler && w.Dice.Next() < 0.5) kind = Foe.Spinner;
        for (int tries = 0; tries < 8; tries++)
        {
            var side = Sides.All[w.Dice.Next(4)];
            double along = w.Dice.Between(-1, 1);
            var (x, y) = w.WallPoint(side, along);
            // Not out of a doorway, and not on top of the ship.
            double off = side is Side.North or Side.South ? x - Field.CX : y - Field.CY;
            if (Math.Abs(off) < Field.DoorHalf + 18) continue;
            if (Math.Abs(x - w.Ship.X) + Math.Abs(y - w.Ship.Y) < 220) continue;
            Add(w, kind, side, along);
            w.Heard.Add(Sfx.Emerge);
            return;
        }
    }

    public static Enemy Add(World w, Foe kind, Side side, double along)
    {
        var (x, y) = w.WallPoint(side, along);
        var e = new Enemy
        {
            Kind = kind, Wall = side, Along = along, X = x, Y = y,
            Reload = w.Dice.Between(1.2, 2.4),
            Spin = w.Dice.Between(0, 6.28),
            Hp = kind == Foe.Crawler ? 2 : 1,
        };
        // Crawlers set off along the wall one way or the other.
        if (kind == Foe.Crawler) e.Vx = w.Dice.Next() < 0.5 ? -1 : 1;
        w.Enemies.Add(e);
        return e;
    }

    private static void Move(World w, Enemy e)
    {
        double dt = Field.Step, speed = w.Pace.EnemySpeed;
        var s = w.Ship;
        e.Age += dt;
        e.Flash = Math.Max(0, e.Flash - dt);
        e.Spin += dt * (e.Kind == Foe.Spinner ? 9 : 2);
        var (nx, ny) = Inward(e.Wall);
        if (e.Out < 1 || e.Kind == Foe.Crawler)
        {
            // Coming out of the wall (and a crawler stays on it), moving as the wall moves.
            e.Out = Math.Min(1, e.Out + dt / 0.9);
            if (e.Kind == Foe.Crawler && e.Out >= 1)
            {
                e.Along += e.Vx * 0.12 * speed * dt;
                if (Math.Abs(e.Along) > 1) { e.Along = Math.Sign(e.Along); e.Vx = -e.Vx; }
            }
            var (x, y) = w.WallPoint(e.Wall, e.Along);
            e.X = x + nx * e.Radius * e.Out;
            e.Y = y + ny * e.Radius * e.Out;
            if (e.Out >= 1 && e.Kind != Foe.Crawler)
            {
                e.Vx = nx * 60;
                e.Vy = ny * 60;
            }
            if (e.Kind == Foe.Crawler && e.Out >= 1 && !s.Dead)
            {
                e.Reload -= dt;
                if (e.Reload <= 0)
                {
                    e.Reload = w.Dice.Between(2.2, 3.4) / speed;
                    Fire(w, e.X, e.Y, Aim(e.X, e.Y, s), Shot.EnemySpeed * (0.9 + 0.1 * speed));
                }
            }
            return;
        }
        double dx = s.X - e.X, dy = s.Y - e.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        if (s.Dead) { dx = Field.CX - e.X; dy = Field.CY - e.Y; d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy)); }
        switch (e.Kind)
        {
            case Foe.Spore:
            {
                double wob = Math.Sin(e.Age * 2.3 + e.Spin) * 40;
                e.Vx += (dx / d * 70 - dy / d * wob) * dt;
                e.Vy += (dy / d * 70 + dx / d * wob) * dt;
                Limit(e, 75 * speed);
                break;
            }
            case Foe.Spinner:
            {
                // Swoops: its heading swings towards the ship at a limited rate, so it comes round in arcs.
                double v = 175 * speed;
                double heading = Math.Atan2(e.Vy, e.Vx), want = Math.Atan2(dy, dx);
                double turn = Math.IEEERemainder(want - heading, Math.PI * 2);
                heading += Math.Clamp(turn, -2.1 * dt, 2.1 * dt);
                e.Vx = Math.Cos(heading) * v;
                e.Vy = Math.Sin(heading) * v;
                break;
            }
            case Foe.Mine:
            {
                e.Reload -= dt;
                if (e.Reload < -1 && d < 170 && !s.Dead)
                {
                    // Darts at the ship.
                    e.Vx = dx / d * 250 * speed;
                    e.Vy = dy / d * 250 * speed;
                    e.Reload = 1.1;
                    w.Heard.Add(Sfx.Ping);
                }
                double slow = Math.Pow(e.Reload > 0 ? 0.6 : 0.2, dt);
                e.Vx *= slow;
                e.Vy *= slow;
                break;
            }
        }
        e.X += e.Vx * dt;
        e.Y += e.Vy * dt;
        // Kept in by the walls as they close.
        double r = e.Radius + 2;
        if (e.X < w.L + r) { e.X = w.L + r; e.Vx = Math.Abs(e.Vx); }
        if (e.X > w.R - r) { e.X = w.R - r; e.Vx = -Math.Abs(e.Vx); }
        if (e.Y < w.T + r) { e.Y = w.T + r; e.Vy = Math.Abs(e.Vy); }
        if (e.Y > w.B - r) { e.Y = w.B - r; e.Vy = -Math.Abs(e.Vy); }
    }

    private static void Limit(Enemy e, double max)
    {
        double v = Math.Sqrt(e.Vx * e.Vx + e.Vy * e.Vy);
        if (v > max) { e.Vx *= max / v; e.Vy *= max / v; }
    }

    /// <summary>The way to fire from a point to hit the ship, leading it a little.</summary>
    private static double Aim(double x, double y, Ship s)
    {
        double t = Math.Sqrt((s.X - x) * (s.X - x) + (s.Y - y) * (s.Y - y)) / Shot.EnemySpeed;
        return Math.Atan2(s.Y + s.Vy * t * 0.5 - y, s.X + s.Vx * t * 0.5 - x);
    }

    private static void Fire(World w, double x, double y, double angle, double speed)
    {
        if (w.Ship.Safe > 0.3) return;
        w.Shots.Add(new Shot { X = x, Y = y, Vx = Math.Cos(angle) * speed, Vy = Math.Sin(angle) * speed, Hostile = true });
        w.Heard.Add(Sfx.EnemyFire);
    }

    // ───────────────────────────── the core ─────────────────────────────

    private static void Heart(World w, Core c)
    {
        double dt = Field.Step;
        c.Spin += dt * (0.7 + 0.06 * w.Station.Number);
        c.Flash = Math.Max(0, c.Flash - dt);
        for (int i = 0; i < c.Inner.Length; i++) c.Inner[i] = Math.Max(0, c.Inner[i] - dt);
        for (int i = 0; i < c.Outer.Length; i++) c.Outer[i] = Math.Max(0, c.Outer[i] - dt);
        c.Reload -= dt;
        if (c.Reload <= 0 && !w.Ship.Dead)
        {
            c.Reload = Math.Max(1.3, 3.0 - 0.15 * w.Station.Number);
            double a = Aim(Core.X, Core.Y, w.Ship);
            for (int k = -1; k <= 1; k++) Fire(w, Core.X + Math.Cos(a) * Core.Radius, Core.Y + Math.Sin(a) * Core.Radius, a + k * 0.22, Shot.EnemySpeed * 0.9);
        }
    }

    /// <summary>Which plate of a ring a direction from the core points at.</summary>
    public static int Plate(double angle, double ringAngle, int plates)
    {
        double a = (angle - ringAngle) % (Math.PI * 2);
        if (a < 0) a += Math.PI * 2;
        return Math.Min(plates - 1, (int)(a / (Math.PI * 2 / plates)));
    }

    private static void DestroyCore(World w, Core c)
    {
        c.Dead = true;
        w.CoreDestroyed = true;
        w.Escaping = true;
        w.EscapeTime = EscapeSeconds(w.Station);
        AddScore(w, CorePoints(w.Station.Number));
        Float(w, Core.X, Core.Y - 40, CorePoints(w.Station.Number).ToString());
        for (int i = 0; i < 6; i++)
            Boom(w, Core.X + w.Dice.Between(-60, 60), Core.Y + w.Dice.Between(-60, 60), w.Dice.Between(60, 140), i % 2 == 0 ? 0xFFFF6040 : 0xFFFFE060, 1.4 + i * 0.15);
        foreach (var x in w.Shots) if (x.Hostile) x.Gone = true;
        w.Heard.Add(Sfx.CoreBoom);
        w.Alarm = 0.8;
    }

    // ───────────────────────────── shots ─────────────────────────────

    /// <summary>The distance from a point to the line from (x0, y0) to (x1, y1).</summary>
    public static double SegDist(double x0, double y0, double x1, double y1, double px, double py)
    {
        double dx = x1 - x0, dy = y1 - y0, len = dx * dx + dy * dy;
        double t = len <= 0 ? 0 : Math.Clamp(((px - x0) * dx + (py - y0) * dy) / len, 0, 1);
        double ex = x0 + dx * t - px, ey = y0 + dy * t - py;
        return Math.Sqrt(ex * ex + ey * ey);
    }

    private static void Shots(World w)
    {
        double dt = Field.Step;
        var s = w.Ship;
        foreach (var x in w.Shots)
        {
            double x0 = x.X, y0 = x.Y;
            x.X += x.Vx * dt;
            x.Y += x.Vy * dt;
            x.Age += dt;
            if (!x.Hostile && x.Age > Shot.Life) { x.Gone = true; continue; }
            if (x.X < w.L || x.X > w.R || x.Y < w.T || x.Y > w.B)
            {
                x.Gone = true;
                if (!x.Hostile) Boom(w, Math.Clamp(x.X, w.L, w.R), Math.Clamp(x.Y, w.T, w.B), 10, 0xFFB0C0FF, 0.25);
                continue;
            }
            if (x.Hostile)
            {
                if (s.Dead || SegDist(x0, y0, x.X, x.Y, s.X, s.Y) > Ship.Radius + 3) continue;
                x.Gone = true;
                if (s.Shielded) { s.Energy = Math.Max(0, s.Energy - 0.05); w.Heard.Add(Sfx.Bounce); }
                else if (s.Safe <= 0) Crash(w);
                continue;
            }
            foreach (var e in w.Enemies)
            {
                if (e.Gone || SegDist(x0, y0, x.X, x.Y, e.X, e.Y) > e.Radius + 3) continue;
                x.Gone = true;
                if (--e.Hp > 0) { e.Flash = 0.15; w.Heard.Add(Sfx.Ping); break; }
                Kill(w, e);
                break;
            }
            if (x.Gone || !w.InCore || w.Core is not { Dead: false } c) continue;
            double d0 = Math.Sqrt((x0 - Core.X) * (x0 - Core.X) + (y0 - Core.Y) * (y0 - Core.Y));
            double d1 = Math.Sqrt((x.X - Core.X) * (x.X - Core.X) + (x.Y - Core.Y) * (x.Y - Core.Y));
            double ang = Math.Atan2(x.Y - Core.Y, x.X - Core.X);
            // The rings first, from the outside in: a plate that's there stops the shot.
            if (Crosses(d0, d1, Core.OuterRing))
            {
                int i = Plate(ang, c.OuterAngle, Core.OuterPlates);
                if (c.Outer[i] <= 0) { PlateHit(w, c.Outer, i, x); continue; }
            }
            if (Crosses(d0, d1, Core.InnerRing))
            {
                int i = Plate(ang, c.InnerAngle, Core.InnerPlates);
                if (c.Inner[i] <= 0) { PlateHit(w, c.Inner, i, x); continue; }
            }
            if (SegDist(x0, y0, x.X, x.Y, Core.X, Core.Y) < Core.Radius)
            {
                x.Gone = true;
                c.Hp--;
                c.Flash = 0.2;
                AddScore(w, CoreHitPoints);
                Boom(w, x.X, x.Y, 24, 0xFFFF8040, 0.4);
                w.Heard.Add(Sfx.CoreHit);
                if (c.Hp <= 0) DestroyCore(w, c);
            }
        }
    }

    private static bool Crosses(double d0, double d1, double ring) => (d0 - ring) * (d1 - ring) <= 0 || Math.Abs(d1 - ring) < 5;

    private static void PlateHit(World w, double[] ring, int i, Shot x)
    {
        ring[i] = Core.Regrow;
        x.Gone = true;
        AddScore(w, PlatePoints);
        Boom(w, x.X, x.Y, 18, 0xFFFFE060, 0.35);
        w.Heard.Add(Sfx.PlateHit);
    }

    public static void Kill(World w, Enemy e)
    {
        e.Gone = true;
        w.Kills++;
        int pts = Enemy.Points(e.Kind);
        AddScore(w, pts);
        Float(w, e.X, e.Y - 16, pts.ToString());
        Boom(w, e.X, e.Y, 40, Colour(e.Kind));
        w.Heard.Add(Sfx.Blast);
    }

    public static uint Colour(Foe k) => k switch
    {
        Foe.Spore => 0xFF50FF90,
        Foe.Spinner => 0xFFFF50E0,
        Foe.Crawler => 0xFFFFD040,
        _ => 0xFFFF6050,
    };

    // ───────────────────────────── touching ─────────────────────────────

    private static void Touch(World w)
    {
        var s = w.Ship;
        foreach (var e in w.Enemies)
        {
            if (e.Gone) continue;
            double dx = e.X - s.X, dy = e.Y - s.Y;
            if (dx * dx + dy * dy > (e.Radius + Ship.Radius) * (e.Radius + Ship.Radius)) continue;
            if (s.Shielded)
            {
                // The shield smashes it.
                s.Energy = Math.Max(0, s.Energy - 0.08);
                Kill(w, e);
            }
            else if (s.Safe <= 0)
            {
                Kill(w, e);
                Crash(w);
                return;
            }
        }
        if (w.InCore && w.Core is { Dead: false })
        {
            // The core's field is as hard as a wall.
            double dx = s.X - Core.X, dy = s.Y - Core.Y, d = Math.Sqrt(dx * dx + dy * dy);
            double depth = Core.OuterRing + 6 + Ship.Radius - d;
            if (depth > 0 && d > 0) Hit(w, dx / d, dy / d, depth);
        }
    }
}
