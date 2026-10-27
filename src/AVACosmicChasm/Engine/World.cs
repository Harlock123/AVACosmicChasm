namespace AVACosmicChasm.Engine;

/// <summary>
/// The screen's measures, in pixels of the game's own 1024×768 screen (down is +y). A room fills the screen under
/// the score bar; its walls start at the full extent and close in.
/// </summary>
public static class Field
{
    public const double Width = 1024, Height = 768;
    /// <summary>The score bar's height.</summary>
    public const double Top = 64;
    public const double Step = 1 / 60.0;
    /// <summary>A room's walls at their widest.</summary>
    public const double Left = 28, Right = 996, RoomTop = 92, Bottom = 744;
    public const double CX = (Left + Right) / 2, CY = (RoomTop + Bottom) / 2;
    public const double HalfW = (Right - Left) / 2, HalfH = (Bottom - RoomTop) / 2;
    /// <summary>How far the walls can close in before the room crushes whatever is inside.</summary>
    public const double MaxSqueeze = HalfH - 78;
    /// <summary>Half a doorway's width.</summary>
    public const double DoorHalf = 54;
    /// <summary>The corners of a room are cut off this much.</summary>
    public const double Bevel = 46;
}

/// <summary>How a station plays: the deeper in, the faster the walls and the more that comes out of them.</summary>
public readonly record struct Pace(int Number)
{
    /// <summary>Seconds for a room's walls to close right in.</summary>
    public double SqueezeSeconds => Math.Max(24, 52 - 4.5 * (Number - 1));
    /// <summary>Seconds between enemies coming out of the walls.</summary>
    public double SpawnSeconds => Math.Max(0.9, 2.6 - 0.25 * (Number - 1));
    public int MaxEnemies => Math.Min(9, 3 + Number);
    public double EnemySpeed => 1 + 0.1 * (Number - 1);
    public int CoreHp => 8 + 2 * Number;
    /// <summary>Seconds of drilling to open a doorway.</summary>
    public double DrillSeconds => Math.Min(1.8, 1.1 + 0.08 * (Number - 1));
}

/// <summary>The player's ship: it turns and thrusts, and drifts on as ships in space do.</summary>
public sealed class Ship
{
    public const double Radius = 13;
    public const double TurnRate = 4.4, Thrust = 430, MaxSpeed = 360;
    /// <summary>Shield energy used a second while it's up, and won back a second while it isn't.</summary>
    public const double ShieldDrain = 0.22, ShieldRecharge = 0.1;
    public double X = Field.CX, Y = Field.CY, Vx, Vy;
    /// <summary>Which way the nose points, in radians: 0 is up the screen, and it turns clockwise.</summary>
    public double Angle;
    public double Energy = 1;
    public bool Shielded, Thrusting, Dead;
    /// <summary>Seconds of being untouchable after coming into a room or back to life.</summary>
    public double Safe;
    public double Reload;
    /// <summary>Touching a plug with the shield up this step: the drill is turning.</summary>
    public bool Drilling;
    public double Nx => Math.Sin(Angle);
    public double Ny => -Math.Cos(Angle);
    public double Speed => Math.Sqrt(Vx * Vx + Vy * Vy);
}

public sealed class Shot
{
    public const double Speed = 760, Life = 0.85, EnemySpeed = 300;
    public double X, Y, Vx, Vy, Age;
    public bool Hostile, Gone;
}

public enum Foe
{
    /// <summary>A slow spore that drifts after the ship.</summary>
    Spore,
    /// <summary>A fast spinning thing that swoops in arcs.</summary>
    Spinner,
    /// <summary>A gun that crawls along the wall it came out of, firing.</summary>
    Crawler,
    /// <summary>A mine that sits where it stopped, and darts at the ship when it comes close.</summary>
    Mine,
}

public sealed class Enemy
{
    public Foe Kind;
    public double X, Y, Vx, Vy, Age, Reload, Spin, Flash;
    /// <summary>The wall it came out of, and how far along it (a fraction, -1 to 1 from the middle).</summary>
    public Side Wall;
    public double Along;
    /// <summary>How far out of the wall it has come, 0 to 1. Until it's out it only grows.</summary>
    public double Out;
    public int Hp = 1;
    public bool Gone;
    public double Radius => Kind switch { Foe.Spore => 14, Foe.Spinner => 13, Foe.Crawler => 15, _ => 12 };
    public static int Points(Foe k) => k switch { Foe.Spore => 100, Foe.Spinner => 150, Foe.Crawler => 250, _ => 200 };
}

/// <summary>The station's core: a glowing heart behind two turning rings of shield plates.</summary>
public sealed class Core
{
    public const double Radius = 30, InnerRing = 64, OuterRing = 100;
    public const int InnerPlates = 10, OuterPlates = 14;
    /// <summary>A shield plate grows back this long after it's shot away.</summary>
    public const double Regrow = 9;
    public int Hp, MaxHp;
    public double Spin, Reload, Flash;
    public readonly double[] Inner = new double[InnerPlates], Outer = new double[OuterPlates];
    public bool Dead;
    public static double X => Field.CX;
    public static double Y => Field.CY;
    public double InnerAngle => Spin;
    public double OuterAngle => -Spin * 0.7;
}

/// <summary>An explosion, or a spark.</summary>
public sealed class Blast
{
    public double X, Y, Age, Life = 0.6, Size = 30;
    public uint Colour = 0xFFFFE080;
    public int Seed;
}

/// <summary>A score floating up from where it was won.</summary>
public sealed class Floater
{
    public double X, Y, Age;
    public string Text = "";
}

/// <summary>Everything in play: the station, the room the ship is in, and what's in it.</summary>
public sealed class World
{
    public Station Station = new(1, 1);
    public Pace Pace => new(Station.Number);
    public int Room;
    /// <summary>How far the walls have closed in.</summary>
    public double Squeeze;
    /// <summary>The wall the ship came in through (where it comes back after a crash).</summary>
    public Side EntrySide;
    public Ship Ship = new();
    public readonly List<Shot> Shots = [];
    public readonly List<Enemy> Enemies = [];
    public readonly List<Blast> Blasts = [];
    public readonly List<Floater> Floaters = [];
    public Core? Core;
    public bool CoreDestroyed;
    /// <summary>The core is gone: get out before the station goes up.</summary>
    public bool Escaping;
    public double EscapeTime;
    /// <summary>Out through the airlock.</summary>
    public bool Escaped;
    public int Score, Lives = 3, Kills;
    public int NextExtra = Sim.FirstExtra;
    public double Clock, SpawnClock, Alarm;
    public Dice Dice = new(1);
    /// <summary>Sounds asked for since the host last looked.</summary>
    public readonly List<Sfx> Heard = [];
    /// <summary>Turned off by the tests that want a room to themselves.</summary>
    public bool Spawning = true;

    public Room Here => Station[Room];
    public bool InCore => Room == Station.CoreRoom;
    public bool InDock => Room == Station.DockRoom;

    // The walls where they are now.
    public double L => Field.Left + Squeeze;
    public double R => Field.Right - Squeeze;
    public double T => Field.RoomTop + Squeeze;
    public double B => Field.Bottom - Squeeze;

    /// <summary>Whether a side of this room has a way out: a doorway, or the airlock in the docking bay.</summary>
    public bool HasExit(Side s) => Here.Door(s) != null || InDock && s == Station.DockSide;

    /// <summary>Whether a side's way out is open to fly through.</summary>
    public bool ExitOpen(Side s) => Here.Door(s) is { Open: true } || InDock && s == Station.DockSide && Escaping;

    /// <summary>The middle of a wall, where its doorway is.</summary>
    public (double X, double Y) DoorAt(Side s) => s switch
    {
        Side.North => (Field.CX, T),
        Side.South => (Field.CX, B),
        Side.East => (R, Field.CY),
        _ => (L, Field.CY),
    };

    /// <summary>A point on a wall, <paramref name="along"/> from -1 at one end to 1 at the other (clear of the corners).</summary>
    public (double X, double Y) WallPoint(Side s, double along)
    {
        double hw = (R - L) / 2 - Field.Bevel - 10, hh = (B - T) / 2 - Field.Bevel - 10;
        return s switch
        {
            Side.North => (Field.CX + along * hw, T),
            Side.South => (Field.CX + along * hw, B),
            Side.East => (R, Field.CY + along * hh),
            _ => (L, Field.CY + along * hh),
        };
    }
}
