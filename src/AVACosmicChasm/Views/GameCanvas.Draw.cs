using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVACosmicChasm.Engine;

namespace AVACosmicChasm.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVACosmicChasm/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFEAF4FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF7F8CA8, Cyan = 0xFF60E8FF,
        Orange = 0xFFFF9A30, Green = 0xFF50FF90, Back = 0xFF02030A;

    /// <summary>Each station's walls glow their own colour.</summary>
    private static readonly uint[] WallColours = [0xFF3A7CFF, 0xFF30E070, 0xFFB060FF, 0xFFFF9030, 0xFF30E0E0, 0xFFFF50A0];

    private static uint WallColour(int station) => WallColours[(station - 1) % WallColours.Length];

    /// <summary>Text with a soft glow behind it, like the beam on a vector screen.</summary>
    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool glow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        var pos = new Point(x, at.Y - ft.Height / 2);
        if (glow)
        {
            var halo = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour & 0x30FFFFFF));
            foreach (var (ox, oy) in new[] { (-1.5, 0.0), (1.5, 0.0), (0.0, -1.5), (0.0, 1.5) }) dc.DrawText(halo, new Point(pos.X + ox, pos.Y + oy));
        }
        dc.DrawText(ft, pos);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    /// <summary>A line as the beam draws it: a wide faint halo, a narrower one, and the bright core.</summary>
    private static void Beam(DrawingContext dc, Point a, Point b, uint colour, double bright = 1, double core = 1.4)
    {
        if (bright <= 0.01) return;
        uint Alpha(double f) => (uint)Math.Clamp(255 * f * bright, 0, 255) << 24 | (colour & 0x00FFFFFF);
        dc.DrawLine(Pen(Alpha(0.12), core * 5), a, b);
        dc.DrawLine(Pen(Alpha(0.3), core * 2.5), a, b);
        dc.DrawLine(Pen(Alpha(1), core), a, b);
    }

    private static void Outline(DrawingContext dc, IReadOnlyList<Point> pts, uint colour, double bright = 1, double core = 1.4)
    {
        for (int i = 0; i < pts.Count; i++) Beam(dc, pts[i], pts[(i + 1) % pts.Count], colour, bright, core);
    }

    private static void Polyline(DrawingContext dc, IReadOnlyList<Point> pts, uint colour, double bright = 1, double core = 1.4)
    {
        for (int i = 0; i + 1 < pts.Count; i++) Beam(dc, pts[i], pts[i + 1], colour, bright, core);
    }

    /// <summary>A loop of points round a centre: an ellipse turned to an angle.</summary>
    private static Point[] Oval(double x, double y, double rx, double ry, double turn, int n = 12)
    {
        double c = Math.Cos(turn), s = Math.Sin(turn);
        return Enumerable.Range(0, n).Select(i =>
        {
            double a = i * Math.PI * 2 / n, px = Math.Cos(a) * rx, py = Math.Sin(a) * ry;
            return new Point(x + px * c - py * s, y + px * s + py * c);
        }).ToArray();
    }

    private static uint Mix(uint a, uint b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        uint C(int shift) => (uint)Math.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return 0xFF000000 | C(16) << 16 | C(8) << 8 | C(0);
    }

    private bool InGame => Engine.State is GameState.Playing or GameState.Attract or GameState.Paused;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(Back), new Rect(0, 0, Field.Width, Field.Height));
        if (InGame)
        {
            switch (g.Phase)
            {
                case Phase.Map: DrawMapScreen(dc); break;
                case Phase.Escaped: DrawStationBlowing(dc); break;
                default: DrawPlay(dc); break;
            }
        }
        else DrawShowRoom(dc);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 330), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 14), 24, Yellow, center: true);
        if (_toast != null && _toastTime > 0)
            Text(dc, _toast, new Point(Field.Width / 2, Field.Height - 40), 28, Cyan, center: true);
    }

    // ───────────────────────────── the room ─────────────────────────────

    /// <summary>A room's eight corners, its corners cut off, clockwise from the top left.</summary>
    private static Point[] Octagon(double l, double t, double r, double b, double bevel) =>
    [
        new(l + bevel, t), new(r - bevel, t), new(r, t + bevel), new(r, b - bevel),
        new(r - bevel, b), new(l + bevel, b), new(l, b - bevel), new(l, t + bevel),
    ];

    /// <summary>The walls of an octagon, with a doorway's gap in each side that has one.</summary>
    private static void Walls(DrawingContext dc, Point[] o, bool[] gaps, uint colour, double bright, double core = 1.6)
    {
        double h = Field.DoorHalf;
        // The straight sides are 0-1 (north), 2-3 (east), 4-5 (south) and 6-7 (west).
        for (int i = 0; i < 8; i++)
        {
            var a = o[i];
            var b = o[(i + 1) % 8];
            int side = i / 2;
            if (i % 2 == 1 || !gaps[side]) { Beam(dc, a, b, colour, bright, core); continue; }
            var mid = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            var dir = new Point(b.X - a.X, b.Y - a.Y);
            double len = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
            var u = new Point(dir.X / len, dir.Y / len);
            Beam(dc, a, new Point(mid.X - u.X * h, mid.Y - u.Y * h), colour, bright, core);
            Beam(dc, new Point(mid.X + u.X * h, mid.Y + u.Y * h), b, colour, bright, core);
        }
    }

    private void DrawPlay(DrawingContext dc)
    {
        var w = Engine.World;
        DrawRoom(dc, w);
        if (w.InCore && w.Core is { Dead: false } core) DrawCore(dc, core, 1);
        foreach (var e in w.Enemies) DrawEnemy(dc, w, e);
        foreach (var s in w.Shots) DrawShot(dc, s);
        if (!w.Ship.Dead) DrawShip(dc, w.Ship);
        foreach (var b in w.Blasts) DrawBlast(dc, b);
        foreach (var f in w.Floaters) Text(dc, f.Text, new Point(f.X, f.Y), 22, Mix(Yellow, Back, f.Age / 1.2), center: true, glow: false);
        if (Engine.Phase == Phase.Blown)
        {
            double t = Engine.PhaseTime / GameEngine.BlownSeconds;
            dc.FillRectangle(Brush((uint)(Math.Clamp(1 - t * 1.5, 0, 1) * 230) << 24 | 0xFFFFFF), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        }
        if (!w.Escaping && Sim.TimeLeft(w) < 6 && !w.Ship.Dead && (int)(_clock * 4) % 2 == 0)
            Text(dc, "THE WALLS ARE CLOSING IN!", new Point(Field.Width / 2, Field.CY + 120), 34, Red, center: true);
    }

    private void DrawRoom(DrawingContext dc, World w)
    {
        uint wall = WallColour(w.Station.Number);
        double left = Sim.TimeLeft(w);
        if (left < 6) wall = Mix(wall, Red, (int)(_clock * (left < 2.5 ? 8 : 4)) % 2 == 0 ? 1 : 0.4);
        var outer = Octagon(Field.Left, Field.RoomTop, Field.Right, Field.Bottom, Field.Bevel);
        var inner = Octagon(w.L, w.T, w.R, w.B, Field.Bevel);
        var gaps = Sides.All.Select(w.HasExit).ToArray();
        // The station's hull at the room's widest, dim, and the walls where they are now, bright; lines from one to
        // the other give the walls their depth, like looking down into the chasm.
        Walls(dc, outer, gaps, wall, 0.35, 1.2);
        if (w.Squeeze > 1)
        {
            for (int i = 0; i < 8; i++) Beam(dc, outer[i], inner[i], wall, 0.45, 1.1);
            for (int side = 0; side < 4; side++)
                for (int k = 1; k < 8; k++)
                {
                    double f = k / 8.0;
                    if (gaps[side] && Math.Abs(f - 0.5) < 0.13) continue;
                    var oa = outer[side * 2]; var ob = outer[side * 2 + 1];
                    var ia = inner[side * 2]; var ib = inner[side * 2 + 1];
                    Beam(dc, new Point(oa.X + (ob.X - oa.X) * f, oa.Y + (ob.Y - oa.Y) * f), new Point(ia.X + (ib.X - ia.X) * f, ia.Y + (ib.Y - ia.Y) * f), wall, 0.18, 1);
                }
        }
        Walls(dc, inner, gaps, wall, 1, 1.8);
        foreach (var side in Sides.All)
            if (w.HasExit(side)) DrawDoorway(dc, w, side, wall);
    }

    /// <summary>A doorway: a passage out through the walls to the hull, and its plug until it's drilled.</summary>
    private void DrawDoorway(DrawingContext dc, World w, Side side, uint wall)
    {
        var (nx, ny) = Sim.Inward(side);
        // Along the wall, and the wall's line now and at the hull (and on to the screen's edge).
        double ux = -ny, uy = nx;
        var (ix, iy) = w.DoorAt(side);
        double ox = side switch { Side.East => Field.Right, Side.West => Field.Left, _ => Field.CX };
        double oy = side switch { Side.North => Field.RoomTop, Side.South => Field.Bottom, _ => Field.CY };
        double ex = ox - nx * 26, ey = oy - ny * 26;
        double h = Field.DoorHalf;
        foreach (int k in new[] { -1, 1 })
        {
            Beam(dc, new Point(ix + ux * h * k, iy + uy * h * k), new Point(ox + ux * h * k, oy + uy * h * k), wall, 0.8, 1.3);
            Beam(dc, new Point(ox + ux * h * k, oy + uy * h * k), new Point(ex + ux * h * k, ey + uy * h * k), wall, 0.35, 1.1);
        }
        bool airlock = w.InDock && side == w.Station.DockSide;
        if (airlock)
        {
            // The airlock: shut fast until the core's gone, then open, flashing the way out.
            if (!w.Escaping)
            {
                for (int k = 0; k < 2; k++)
                {
                    double d = k * 7;
                    Beam(dc, new Point(ix + ux * h - nx * d, iy + uy * h - ny * d), new Point(ix - ux * h - nx * d, iy - uy * h - ny * d), Cyan, 0.9, 1.6);
                }
                Text(dc, "AIRLOCK", new Point(ix + nx * 26, iy + ny * 26), 20, Cyan, center: true);
            }
            else if ((int)(_clock * 4) % 2 == 0) Chevrons(dc, ix, iy, nx, ny, Green);
            return;
        }
        var door = w.Here.Door(side)!;
        if (!door.Open)
        {
            // The plug: a zigzag of armour across the gap, eaten away from the middle as it's drilled.
            double hole = h * 0.85 * door.Drilled;
            var pts = new List<Point>();
            for (int i = 0; i <= 12; i++)
            {
                double along = -h + 2 * h * i / 12.0, zig = i % 2 == 0 ? 0 : -7;
                if (Math.Abs(along) < hole) { Polyline(dc, pts, Orange, 1, 1.6); pts.Clear(); continue; }
                pts.Add(new Point(ix + ux * along + nx * zig, iy + uy * along + ny * zig));
            }
            Polyline(dc, pts, Orange, 1, 1.6);
            if (door.Drilled > 0)
            {
                // The hole's ragged edges, glowing hot.
                foreach (int k in new[] { -1, 1 })
                    Beam(dc, new Point(ix + ux * hole * k, iy + uy * hole * k), new Point(ix + ux * (hole + 6) * k - nx * 9, iy + uy * (hole + 6) * k - ny * 9), Yellow, 0.9, 1.4);
                Beam(dc, new Point(ix + ux * hole, iy + uy * hole), new Point(ix - ux * hole, iy - uy * hole), Orange, 0.2, 1);
            }
        }
        // The way to go: chevrons pointing out.
        var st = w.Station;
        int to = w.Escaping ? st.DockRoom : st.CoreRoom;
        if (st.WayTo(w.Room, to) == side && !(w.InCore && !w.Escaping) && (int)(_clock * 3) % 2 == 0)
            Chevrons(dc, ix, iy, nx, ny, w.Escaping ? Green : Yellow);
    }

    private static void Chevrons(DrawingContext dc, double ix, double iy, double nx, double ny, uint colour)
    {
        double ux = -ny, uy = nx;
        for (int k = 0; k < 2; k++)
        {
            double d = 28 + k * 16;
            var tip = new Point(ix + nx * (d - 10), iy + ny * (d - 10));
            Beam(dc, new Point(ix + nx * d + ux * 12, iy + ny * d + uy * 12), tip, colour, 0.9);
            Beam(dc, new Point(ix + nx * d - ux * 12, iy + ny * d - uy * 12), tip, colour, 0.9);
        }
    }

    // ───────────────────────────── the ship ─────────────────────────────

    /// <summary>A point in the ship's own frame (forward, and across to its right) on the screen.</summary>
    private static Point Hull(double x, double y, double angle, double fwd, double right, double scale = 1)
    {
        double s = Math.Sin(angle), c = Math.Cos(angle);
        return new Point(x + (fwd * s + right * c) * scale, y + (-fwd * c + right * s) * scale);
    }

    private void DrawShip(DrawingContext dc, Ship s, double scale = 1, uint colour = White)
    {
        if (s.Safe > 0 && (int)(_clock * 12) % 2 == 0 && scale == 1) colour = 0xFF8090A0;
        Point P(double f, double r) => Hull(s.X, s.Y, s.Angle, f, r, scale);
        Outline(dc, [P(18, 0), P(-6, 8), P(-11, 14), P(-8, 0), P(-11, -14), P(-6, -8)], colour, 1, 1.5);
        Beam(dc, P(7, 0), P(-3, 0), Cyan, 0.8, 1.2);
        if (s.Thrusting && scale == 1)
        {
            double len = 12 + Hash((int)(_clock * 30), 1, 2) % 10;
            Polyline(dc, [P(-9, 5), P(-9 - len, 0), P(-9, -5)], Orange, 0.9, 1.3);
        }
        if (s.Shielded && scale == 1)
        {
            double flicker = 0.6 + 0.4 * Math.Sin(_clock * 40);
            Outline(dc, Oval(s.X, s.Y, 24, 24, _clock * 3, 16), Cyan, flicker, 1.4);
            if (s.Drilling)
            {
                // Sparks off the drill.
                for (int i = 0; i < 6; i++)
                {
                    uint hsh = Hash((int)(_clock * 40), i, 5);
                    double a = s.Angle + (hsh % 100 / 100.0 - 0.5) * 2.4, r = 20 + hsh % 18;
                    var at = P(22, 0);
                    Beam(dc, at, new Point(at.X - Math.Sin(a) * r * 0.6, at.Y + Math.Cos(a) * r * 0.6), Yellow, 0.9, 1.1);
                }
            }
        }
    }

    // ───────────────────────────── enemies and the core ─────────────────────────────

    private void DrawEnemy(DrawingContext dc, World w, Enemy e)
    {
        uint colour = e.Flash > 0 ? 0xFFFFFFFF : Sim.Colour(e.Kind);
        double grow = Math.Clamp(e.Out, 0.15, 1);
        if (e.Out < 1)
        {
            // A crack in the wall as it pushes out.
            var (wx, wy) = w.WallPoint(e.Wall, e.Along);
            var (nx, ny) = Sim.Inward(e.Wall);
            double ux = -ny, uy = nx, len = 22 * (1 - e.Out * 0.5);
            Beam(dc, new Point(wx - ux * len, wy - uy * len), new Point(wx + ux * len, wy + uy * len), White, 0.9 - e.Out * 0.6, 1.6);
        }
        switch (e.Kind)
        {
            case Foe.Spore:
            {
                var pts = Enumerable.Range(0, 16).Select(i =>
                {
                    double a = i * Math.PI / 8 + e.Spin, r = (i % 2 == 0 ? 15 : 8) * grow;
                    return new Point(e.X + Math.Cos(a) * r, e.Y + Math.Sin(a) * r);
                }).ToArray();
                Outline(dc, pts, colour, 1, 1.3);
                Outline(dc, Oval(e.X, e.Y, 4 * grow, 4 * grow, 0, 6), colour, 0.8, 1.1);
                break;
            }
            case Foe.Spinner:
            {
                for (int k = 0; k < 3; k++)
                {
                    double a = e.Spin + k * Math.PI * 2 / 3;
                    var mid = new Point(e.X + Math.Cos(a) * 13 * grow, e.Y + Math.Sin(a) * 13 * grow);
                    Beam(dc, new Point(e.X, e.Y), mid, colour, 1, 1.4);
                    Beam(dc, mid, new Point(e.X + Math.Cos(a + 0.9) * 10 * grow, e.Y + Math.Sin(a + 0.9) * 10 * grow), colour, 1, 1.4);
                }
                Outline(dc, Oval(e.X, e.Y, 4 * grow, 4 * grow, 0, 6), White, 0.8, 1.1);
                break;
            }
            case Foe.Crawler:
            {
                var (nx, ny) = Sim.Inward(e.Wall);
                double ux = -ny, uy = nx;
                var (wx, wy) = (e.X - nx * e.Radius * e.Out, e.Y - ny * e.Radius * e.Out);
                // A dome on the wall and a gun barrel turning to follow the ship.
                var dome = Enumerable.Range(0, 9).Select(i =>
                {
                    double a = Math.PI * i / 8;
                    return new Point(wx + ux * Math.Cos(a) * 17 * grow + nx * Math.Sin(a) * 15 * grow, wy + uy * Math.Cos(a) * 17 * grow + ny * Math.Sin(a) * 15 * grow);
                }).ToList();
                Polyline(dc, dome, colour, 1, 1.4);
                Beam(dc, dome[0], dome[^1], colour, 1, 1.4);
                var s = w.Ship;
                double bx = s.X - e.X, by = s.Y - e.Y, bd = Math.Max(1, Math.Sqrt(bx * bx + by * by));
                if (bx * nx + by * ny < 0) { bx = nx; by = ny; bd = 1; }
                Beam(dc, new Point(e.X, e.Y), new Point(e.X + bx / bd * 18 * grow, e.Y + by / bd * 18 * grow), White, 0.9, 1.6);
                if (e.Hp > 1) dc.DrawEllipse(Brush(colour), null, new Point(wx + nx * 6, wy + ny * 6), 2, 2);
                break;
            }
            case Foe.Mine:
            {
                double pulse = 0.6 + 0.4 * Math.Sin(_clock * (e.Reload > 0 ? 20 : 5) + e.Spin);
                var pts = Enumerable.Range(0, 4).Select(i => new Point(e.X + Math.Cos(e.Spin + i * Math.PI / 2) * 13 * grow, e.Y + Math.Sin(e.Spin + i * Math.PI / 2) * 13 * grow)).ToArray();
                Outline(dc, pts, colour, 1, 1.4);
                Beam(dc, pts[0], pts[2], colour, pulse, 1);
                Beam(dc, pts[1], pts[3], colour, pulse, 1);
                break;
            }
        }
    }

    private static void Arc(DrawingContext dc, double cx, double cy, double r, double a0, double a1, uint colour, double bright, double core)
    {
        const int n = 4;
        var pts = Enumerable.Range(0, n + 1).Select(i => { double a = a0 + (a1 - a0) * i / n; return new Point(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r); }).ToArray();
        Polyline(dc, pts, colour, bright, core);
    }

    private void DrawCore(DrawingContext dc, Core c, double bright, double cx = Field.CX, double cy = Field.CY, double scale = 1)
    {
        void Ring(double[] plates, double r, double angle, uint colour)
        {
            double step = Math.PI * 2 / plates.Length;
            for (int i = 0; i < plates.Length; i++)
            {
                // A plate shot away grows back, faint at first.
                double b = plates[i] <= 0 ? 1 : plates[i] < 2 ? (2 - plates[i]) * 0.2 : 0;
                double a0 = angle + i * step + 0.06, a1 = angle + (i + 1) * step - 0.06;
                Arc(dc, cx, cy, r * scale, a0, a1, colour, b * bright, 2);
                Arc(dc, cx, cy, (r - 6) * scale, a0, a1, colour, b * bright * 0.5, 1.2);
            }
        }
        Ring(c.Outer, Core.OuterRing, c.OuterAngle, Yellow);
        Ring(c.Inner, Core.InnerRing, c.InnerAngle, Orange);
        double pulse = 0.75 + 0.25 * Math.Sin(_clock * 6);
        uint heart = c.Flash > 0 ? 0xFFFFFFFF : Mix(Red, Yellow, 1 - (double)c.Hp / Math.Max(1, c.MaxHp));
        Outline(dc, Oval(cx, cy, Core.Radius * scale, Core.Radius * scale, 0, 20), heart, pulse * bright, 2);
        var star = Enumerable.Range(0, 10).Select(i =>
        {
            double a = -_clock * 2 + i * Math.PI / 5, r = (i % 2 == 0 ? 22 : 9) * scale;
            return new Point(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r);
        }).ToArray();
        Outline(dc, star, heart, bright, 1.3);
    }

    private static void DrawShot(DrawingContext dc, Shot s)
    {
        if (s.Hostile)
        {
            var c = new Point(s.X, s.Y);
            Outline(dc, [new(c.X - 5, c.Y), new(c.X, c.Y - 5), new(c.X + 5, c.Y), new(c.X, c.Y + 5)], Orange, 1, 1.3);
            return;
        }
        var tail = new Point(s.X - s.Vx * 0.016, s.Y - s.Vy * 0.016);
        Beam(dc, tail, new Point(s.X, s.Y), 0xFFFFFFFF, 1, 1.8);
    }

    private static void DrawBlast(DrawingContext dc, Blast e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, e.Y);
        int n = e.Size > 50 ? 16 : 10;
        for (int i = 0; i < n; i++)
        {
            uint h = Hash(i, e.Seed, 3);
            double a = i * Math.PI * 2 / n + h % 10 * 0.06, speed = 0.6 + h % 40 / 100.0;
            double r0 = e.Size * 0.15 + e.Size * t * speed, r1 = r0 + e.Size * 0.25 * (1 - t);
            Beam(dc, new Point(c.X + Math.Cos(a) * r0, c.Y + Math.Sin(a) * r0), new Point(c.X + Math.Cos(a) * r1, c.Y + Math.Sin(a) * r1), e.Colour, 1 - t);
        }
        if (e.Size > 50) Outline(dc, Oval(c.X, c.Y, e.Size * Math.Sqrt(t), e.Size * Math.Sqrt(t), 0, 24), e.Colour, (1 - t) * 0.6);
    }

    // ───────────────────────────── the station map ─────────────────────────────

    /// <summary>
    /// The station's plan: its rooms, the doorways between them (bright when drilled open), the core pulsing in the
    /// middle, the docking bay's airlock, and the way to the core. <paramref name="burst"/> blows it apart.
    /// </summary>
    private void DrawMap(DrawingContext dc, World w, Rect area, bool route, double burst = 0, bool here = true)
    {
        var st = w.Station;
        double cell = Math.Min(area.Width / st.Cols, area.Height / st.Rows);
        double ox = area.X + (area.Width - cell * st.Cols) / 2, oy = area.Y + (area.Height - cell * st.Rows) / 2;
        double box = cell * 0.56, half = box / 2;
        var core = st[st.CoreRoom];
        Point Centre(Room r)
        {
            var p = new Point(ox + (r.Col + 0.5) * cell, oy + (r.Row + 0.5) * cell);
            if (burst <= 0) return p;
            double dx = r.Col - core.Col, dy = r.Row - core.Row;
            return new Point(p.X + dx * cell * burst * 1.6, p.Y + dy * cell * burst * 1.6);
        }
        double fade = 1 - burst;
        uint wall = WallColour(st.Number);
        foreach (var d in st.Doors)
        {
            var a = Centre(st[d.A]);
            var b = Centre(st[d.B]);
            Beam(dc, a, b, d.Open ? wall : Dim, (d.Open ? 0.9 : 0.35) * fade, d.Open ? 1.6 : 1);
        }
        if (route && burst <= 0)
        {
            int to = w.Escaping ? st.DockRoom : st.CoreRoom;
            var path = st.Route(w.Room, to);
            double flow = _clock * 2 % 1;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                var a = Centre(st[path[i]]);
                var b = Centre(st[path[i + 1]]);
                Beam(dc, a, b, w.Escaping ? Green : Yellow, 0.95, 2.2);
                // A pip travelling along each step of the way.
                var m = new Point(a.X + (b.X - a.X) * flow, a.Y + (b.Y - a.Y) * flow);
                dc.DrawEllipse(Brush(White), null, m, 2.5, 2.5);
            }
        }
        foreach (var r in st.Rooms)
        {
            var c = Centre(r);
            double tilt = burst * (Hash(r.Col, r.Row, 9) % 7 - 3) * 0.6;
            var pts = Oval(c.X, c.Y, half * 1.41, half * 1.41, Math.PI / 4 + tilt, 4);
            dc.FillRectangle(Brush(Back), new Rect(c.X - half, c.Y - half, box, box));
            Outline(dc, pts, r.Id == st.CoreRoom ? Red : wall, (r.Visited ? 1 : 0.55) * fade, r.Visited ? 1.6 : 1.1);
        }
        if (burst <= 0)
        {
            var cc = Centre(core);
            double p = 0.6 + 0.4 * Math.Sin(_clock * 6);
            if (!w.CoreDestroyed)
            {
                Outline(dc, Oval(cc.X, cc.Y, half * 0.55, half * 0.55, _clock, 10), Red, p, 1.8);
                if (cell > 60) Text(dc, "CORE", new Point(cc.X, cc.Y + half + 12), Math.Min(22, cell * 0.2), Red, center: true);
            }
            else Text(dc, "X", cc, half, Dim, center: true, glow: false);
            var dock = st[st.DockRoom];
            var dc0 = Centre(dock);
            var (nx, ny) = Sim.Inward(st.DockSide);
            var gate = new Point(dc0.X - nx * half, dc0.Y - ny * half);
            var outside = new Point(dc0.X - nx * (half + cell * 0.3), dc0.Y - ny * (half + cell * 0.3));
            Beam(dc, gate, outside, Cyan, 0.9, 1.6);
            if (cell > 60) Text(dc, "DOCK", new Point(outside.X - nx * 18, outside.Y - ny * 14), Math.Min(20, cell * 0.18), Cyan, center: true);
            if (here && (int)(_clock * 4) % 3 != 0)
            {
                var me = Centre(st[w.Room]);
                var sh = new Ship { X = me.X, Y = me.Y, Angle = w.Ship.Angle };
                DrawShip(dc, sh, Math.Min(1, cell / 110), White);
            }
        }
    }

    private void DrawMapScreen(DrawingContext dc)
    {
        var w = Engine.World;
        Text(dc, $"STATION {w.Station.Number}", new Point(Field.Width / 2, 104), 56, Yellow, center: true);
        Text(dc, $"{w.Station.Rooms.Count} ROOMS - THE CORE IS IN THE MIDDLE", new Point(Field.Width / 2, 146), 26, Dim, center: true);
        DrawMap(dc, w, new Rect(170, 200, 684, 470), route: true);
        Text(dc, "DRILL THROUGH THE PLUGGED DOORS WITH YOUR SHIELD", new Point(Field.Width / 2, 694), 26, White, center: true);
        Text(dc, "DESTROY THE CORE, THEN ESCAPE THROUGH THE DOCK", new Point(Field.Width / 2, 724), 26, White, center: true);
    }

    private void DrawStationBlowing(DrawingContext dc)
    {
        var w = Engine.World;
        double t = Engine.PhaseTime / GameEngine.EscapedSeconds;
        var area = new Rect(170, 200, 684, 520);
        double burst = Math.Min(1, Math.Max(0, t - 0.08) * 1.6);
        DrawMap(dc, w, area, route: false, burst: burst, here: false);
        var c = area.Center;
        for (int k = 0; k < 3; k++)
        {
            double tk = t * 1.5 - k * 0.15;
            if (tk is <= 0 or >= 1) continue;
            Outline(dc, Oval(c.X, c.Y, 420 * tk, 300 * tk, 0, 40), k == 1 ? Yellow : 0xFFFF6040, 1 - tk, 2);
        }
        for (int i = 0; i < 24; i++)
        {
            uint h = Hash(i, w.Station.Number, 4);
            double a = h % 628 / 100.0, r0 = 40 + t * (200 + h % 260), r1 = r0 + 30 * (1 - t);
            Beam(dc, new Point(c.X + Math.Cos(a) * r0, c.Y + Math.Sin(a) * r0), new Point(c.X + Math.Cos(a) * r1, c.Y + Math.Sin(a) * r1), i % 3 == 0 ? White : Orange, 1 - t);
        }
        // The ship gets clear, heading off the top of the screen.
        var sh = new Ship { X = Field.Width / 2 + 340, Y = 640 - t * 520, Angle = 0, Thrusting = true };
        DrawShip(dc, sh, 1.2);
    }

    // ───────────────────────────── the score bar and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        dc.FillRectangle(Brush(Back), new Rect(0, 0, Field.Width, Field.Top));
        Beam(dc, new Point(0, Field.Top - 1), new Point(Field.Width, Field.Top - 1), WallColour(w.Station.Number), 0.4);
        Text(dc, "SCORE", new Point(16, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 44), 34, White);
        Text(dc, "HI", new Point(170, 16), 22, Dim);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(170, 44), 28, Dim);
        if (!InGame) return;
        if (w.Escaping && g.Phase is Phase.Fight or Phase.Down)
        {
            bool flash = w.EscapeTime < 8 && (int)(_clock * 4) % 2 == 0;
            Text(dc, "SELF-DESTRUCT", new Point(Field.Width / 2, 16), 22, Red, center: true);
            Text(dc, w.EscapeTime.ToString("0.0", CultureInfo.InvariantCulture), new Point(Field.Width / 2, 44), 34, flash ? White : Red, center: true);
        }
        else
        {
            Text(dc, $"STATION {w.Station.Number}", new Point(Field.Width / 2, 16), 24, Yellow, center: true);
            string where = w.InCore ? "THE CORE" : w.InDock ? "DOCKING BAY" : $"ROOM {w.Room + 1}";
            Text(dc, where, new Point(Field.Width / 2, 44), 22, Dim, center: true);
        }
        // The shield's energy.
        Text(dc, "SHIELD", new Point(330, 16), 22, Dim);
        double e = w.Ship.Energy;
        dc.FillRectangle(Brush(0x40FFFFFF), new Rect(330, 38, 100, 12));
        dc.FillRectangle(Brush(e < 0.25 ? Red : Cyan), new Rect(330, 38, 100 * e, 12));
        Text(dc, "SHIPS", new Point(650, 16), 22, Dim);
        for (int i = 0; i < Math.Min(6, w.Lives - 1); i++)
            DrawShip(dc, new Ship { X = 660 + i * 22, Y = 44 }, 0.6);
        // A little map of the station in the corner.
        if (g.Phase != Phase.Map) DrawMap(dc, w, new Rect(900, 4, 110, 56), route: true);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg || g.Phase == Phase.Map) return;
        double cx = Field.Width / 2;
        double y = g.Phase == Phase.Escaped ? 112 : 190;
        Text(dc, msg, new Point(cx, y), 60, g.Phase == Phase.Blown || g.World.Escaping && g.Phase != Phase.Escaped ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, y + 50), 30, White, center: true);
    }

    /// <summary>Behind the title and the tables: the core turning in its room.</summary>
    private void DrawShowRoom(DrawingContext dc)
    {
        var o = Octagon(Field.Left, Field.RoomTop, Field.Right, Field.Bottom, Field.Bevel);
        double sq = 60 + 30 * Math.Sin(_clock * 0.4);
        var i = Octagon(Field.Left + sq, Field.RoomTop + sq, Field.Right - sq, Field.Bottom - sq, Field.Bevel);
        bool[] gaps = [true, true, true, true];
        uint wall = WallColour(1 + (int)(_clock / 8) % WallColours.Length);
        Walls(dc, o, gaps, wall, 0.25, 1.1);
        for (int k = 0; k < 8; k++) Beam(dc, o[k], i[k], wall, 0.3, 1);
        Walls(dc, i, gaps, wall, 0.5, 1.4);
        var core = new Core { Spin = _clock * 0.8, Hp = 1, MaxHp = 1 };
        DrawCore(dc, core, 0.35);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0x9002030A), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "AVA COSMIC CHASM", new Point(cx, 134), 104, Cyan, center: true);
        Text(dc, "IN THE STYLE OF THE 1983 COLOUR VECTOR ARCADE GAME", new Point(cx, 194), 26, Dim, center: true);
        string[] lines =
        [
            "FIGHT YOUR WAY THROUGH THE STATION'S ROOMS TO ITS CORE",
            "DRILL THROUGH THE PLUGGED DOORWAYS WITH YOUR SHIELD",
            "THE WALLS CLOSE IN - DON'T BE THERE WHEN THEY MEET",
            "DESTROY THE CORE, THEN ESCAPE BEFORE THE STATION BLOWS",
        ];
        for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(cx, 252 + i * 32), 27, White, center: true);
        (Foe Kind, string Name)[] table = [(Foe.Spore, "SPORE"), (Foe.Spinner, "SPINNER"), (Foe.Crawler, "CRAWLER"), (Foe.Mine, "MINE")];
        var w = new World();
        for (int i = 0; i < table.Length; i++)
        {
            double x = 248 + i * 176;
            var e = new Enemy { Kind = table[i].Kind, X = x, Y = 420, Out = 1, Spin = _clock * 2, Wall = Side.South, Hp = 1 };
            if (e.Kind == Foe.Crawler) e.Y = 428;
            w.Ship.X = x;
            w.Ship.Y = 300;
            DrawEnemy(dc, w, e);
            Text(dc, table[i].Name, new Point(x, 458), 24, Sim.Colour(table[i].Kind), center: true);
            Text(dc, Enemy.Points(table[i].Kind).ToString(), new Point(x, 482), 24, Yellow, center: true);
        }
        Text(dc, $"THE CORE: {Sim.CorePoints(1)}+      ESCAPE: 1000 A STATION + 100 A SECOND TO SPARE", new Point(cx, 524), 24, Orange, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR FIRE", new Point(cx, 590), 48, Yellow, center: true);
        Text(dc, "LEFT/RIGHT: TURN    UP: THRUST    SPACE/Z/CTRL: FIRE    DOWN/X/SHIFT: SHIELD AND DRILL", new Point(cx, 650), 23, Dim, center: true);
        Text(dc, "GAMEPAD: STICK TURNS, UP OR B THRUSTS, A FIRES, X SHIELDS    P: PAUSE    M: SOUND", new Point(cx, 680), 23, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xC002030A), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "CHASM RAIDERS", new Point(cx, 128), 72, Yellow, center: true);
        Text(dc, "RANK NAME    SCORE  STATION", new Point(cx, 194), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 238 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xC002030A), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
