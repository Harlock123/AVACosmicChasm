namespace AVACosmicChasm.Engine;

/// <summary>The controls for one step: turn left or right, thrust, fire, and the shield (which is also the drill).</summary>
public readonly record struct Controls(int Turn = 0, bool Thrust = false, bool Fire = false, bool Shield = false);

/// <summary>A room's four walls. North is up the screen.</summary>
public enum Side { North, East, South, West }

public static class Sides
{
    public static readonly Side[] All = [Side.North, Side.East, Side.South, Side.West];

    public static Side Opposite(this Side s) => (Side)(((int)s + 2) % 4);

    /// <summary>The step across the station's grid that a side leads to.</summary>
    public static (int Dc, int Dr) Step(this Side s) => s switch
    {
        Side.North => (0, -1),
        Side.East => (1, 0),
        Side.South => (0, 1),
        _ => (-1, 0),
    };
}

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
    public double Between(double a, double b) => a + Next() * (b - a);
}

/// <summary>A doorway between two rooms, plugged until it's drilled through. Once open it stays open.</summary>
public sealed class Door
{
    public int A, B;
    public bool Open;
    /// <summary>How far the drill has got, 0 to 1.</summary>
    public double Drilled;

    public int Other(int room) => room == A ? B : A;
}

/// <summary>One room of the station: its place on the grid and its doorways.</summary>
public sealed class Room
{
    public int Id, Col, Row;
    public readonly Door?[] Doors = new Door?[4];
    public bool Visited;
    public Door? Door(Side s) => Doors[(int)s];
}

/// <summary>
/// A space station: a grid of rooms joined by doorways in a branching network, with the core in the middle room and
/// the docking bay (where the ship comes in, and must get out again) on the edge, as far from it as can be.
/// </summary>
public sealed class Station
{
    public int Number { get; }
    public int Cols { get; }
    public int Rows { get; }
    public List<Room> Rooms { get; } = [];
    public List<Door> Doors { get; } = [];
    public int CoreRoom { get; private set; }
    public int DockRoom { get; private set; }
    /// <summary>The docking bay's outside wall: the airlock is there.</summary>
    public Side DockSide { get; private set; }

    /// <summary>The size of the <paramref name="number"/>th station (1 is the first): they grow from 3×3 to 5×5.</summary>
    public static (int Cols, int Rows) SizeOf(int number) => (Math.Min(5, 3 + (number - 1) / 2), Math.Min(5, 3 + number / 3));

    public Station(int number, int seed)
    {
        Number = number;
        (Cols, Rows) = SizeOf(number);
        var dice = new Dice(seed * 7919 + number * 104729);
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                Rooms.Add(new Room { Id = r * Cols + c, Col = c, Row = r });
        CoreRoom = Rows / 2 * Cols + Cols / 2;
        // A winding tree of passages first (a random depth-first walk), so every room can be reached...
        var seen = new bool[Rooms.Count];
        var stack = new Stack<int>();
        stack.Push(CoreRoom);
        seen[CoreRoom] = true;
        while (stack.Count > 0)
        {
            int at = stack.Peek();
            var ways = Sides.All.Where(s => Neighbour(at, s) is int n && !seen[n]).ToList();
            if (ways.Count == 0) { stack.Pop(); continue; }
            var side = ways[dice.Next(ways.Count)];
            int next = Neighbour(at, side)!.Value;
            Join(at, side);
            seen[next] = true;
            stack.Push(next);
        }
        // ...then a few short cuts, so there's more than one way through.
        foreach (var room in Rooms)
            foreach (var side in new[] { Side.East, Side.South })
                if (room.Door(side) == null && Neighbour(room.Id, side) != null && dice.Next() < 0.3)
                    Join(room.Id, side);
        // The docking bay: an edge room as far from the core as the passages go.
        var far = Distances(CoreRoom);
        var edge = Rooms.Where(r => OutsideSides(r.Id).Any()).ToList();
        int best = edge.Max(r => far[r.Id]);
        var choices = edge.Where(r => far[r.Id] >= best - 1).ToList();
        var dock = choices[dice.Next(choices.Count)];
        DockRoom = dock.Id;
        var outside = OutsideSides(dock.Id).ToList();
        DockSide = outside[dice.Next(outside.Count)];
    }

    public Room this[int id] => Rooms[id];

    /// <summary>The room next door on the grid (whether or not a doorway joins them), or null at the edge.</summary>
    public int? Neighbour(int room, Side side)
    {
        var (dc, dr) = side.Step();
        int c = Rooms[room].Col + dc, r = Rooms[room].Row + dr;
        return c < 0 || r < 0 || c >= Cols || r >= Rows ? null : r * Cols + c;
    }

    /// <summary>The sides of a room that face out of the station.</summary>
    public IEnumerable<Side> OutsideSides(int room) => Sides.All.Where(s => Neighbour(room, s) == null);

    private void Join(int room, Side side)
    {
        int other = Neighbour(room, side)!.Value;
        var door = new Door { A = room, B = other };
        Doors.Add(door);
        Rooms[room].Doors[(int)side] = door;
        Rooms[other].Doors[(int)side.Opposite()] = door;
    }

    /// <summary>How many doorways from <paramref name="from"/> to every room.</summary>
    public int[] Distances(int from, bool openOnly = false)
    {
        var d = Enumerable.Repeat(int.MaxValue, Rooms.Count).ToArray();
        var q = new Queue<int>();
        d[from] = 0;
        q.Enqueue(from);
        while (q.Count > 0)
        {
            int at = q.Dequeue();
            foreach (var door in Rooms[at].Doors)
            {
                if (door == null || openOnly && !door.Open) continue;
                int n = door.Other(at);
                if (d[n] != int.MaxValue) continue;
                d[n] = d[at] + 1;
                q.Enqueue(n);
            }
        }
        return d;
    }

    /// <summary>
    /// The shortest way from one room to another, as the rooms passed through (both ends included). Open doorways are
    /// a little preferred, as there's no drilling to do.
    /// </summary>
    public List<int> Route(int from, int to)
    {
        // Dijkstra over a small graph: a plugged doorway costs a little more than an open one.
        var cost = Enumerable.Repeat(double.MaxValue, Rooms.Count).ToArray();
        var prev = Enumerable.Repeat(-1, Rooms.Count).ToArray();
        var done = new bool[Rooms.Count];
        cost[from] = 0;
        for (int k = 0; k < Rooms.Count; k++)
        {
            int at = -1;
            for (int i = 0; i < Rooms.Count; i++)
                if (!done[i] && cost[i] < double.MaxValue && (at < 0 || cost[i] < cost[at])) at = i;
            if (at < 0) break;
            done[at] = true;
            foreach (var door in Rooms[at].Doors)
            {
                if (door == null) continue;
                int n = door.Other(at);
                double c = cost[at] + (door.Open ? 1 : 1.3);
                if (c < cost[n]) { cost[n] = c; prev[n] = at; }
            }
        }
        var path = new List<int>();
        if (cost[to] == double.MaxValue) return path;
        for (int at = to; at != -1; at = prev[at]) path.Add(at);
        path.Reverse();
        return path;
    }

    /// <summary>Which side of <paramref name="room"/> leads to the next room on the way to <paramref name="to"/>.</summary>
    public Side? WayTo(int room, int to)
    {
        var path = Route(room, to);
        if (path.Count < 2) return null;
        foreach (var s in Sides.All)
            if (Rooms[room].Door(s) is { } d && d.Other(room) == path[1]) return s;
        return null;
    }
}
