using AVACosmicChasm.Engine;

namespace AVACosmicChasm.Tests;

/// <summary>The stations: a grid of rooms in a branching network, the core in the middle, the dock on the edge.</summary>
public sealed class StationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public void Every_room_can_be_reached_and_the_core_is_in_the_middle(int number)
    {
        for (int seed = 1; seed <= 20; seed++)
        {
            var s = new Station(number, seed);
            Assert.All(s.Distances(s.CoreRoom), d => Assert.NotEqual(int.MaxValue, d));
            var core = s[s.CoreRoom];
            Assert.Equal((s.Cols / 2, s.Rows / 2), (core.Col, core.Row));
            // Every doorway is shared by the two rooms it joins, on facing sides.
            foreach (var room in s.Rooms)
                foreach (var side in Sides.All)
                    if (room.Door(side) is { } d)
                        Assert.Same(d, s[d.Other(room.Id)].Door(side.Opposite()));
            Assert.All(s.Doors, d => Assert.False(d.Open));
        }
    }

    [Fact]
    public void Stations_grow_from_three_by_three_to_five_by_five()
    {
        Assert.Equal((3, 3), Station.SizeOf(1));
        var sizes = Enumerable.Range(1, 12).Select(n => Station.SizeOf(n)).ToList();
        for (int i = 1; i < sizes.Count; i++)
            Assert.True(sizes[i].Cols * sizes[i].Rows >= sizes[i - 1].Cols * sizes[i - 1].Rows);
        Assert.Equal((5, 5), sizes[^1]);
    }

    [Fact]
    public void The_dock_is_on_the_edge_its_airlock_faces_out_and_it_is_well_away_from_the_core()
    {
        for (int seed = 1; seed <= 20; seed++)
            foreach (int n in new[] { 1, 4, 7 })
            {
                var s = new Station(n, seed);
                Assert.Null(s.Neighbour(s.DockRoom, s.DockSide));
                int far = s.Distances(s.CoreRoom).Max();
                Assert.True(s.Distances(s.CoreRoom)[s.DockRoom] >= Math.Max(2, far - 1), $"station {n} seed {seed}");
            }
    }

    [Fact]
    public void The_rooms_branch_with_more_than_one_way_through_somewhere()
    {
        int loops = 0, branches = 0;
        for (int seed = 1; seed <= 20; seed++)
        {
            var s = new Station(3, seed);
            if (s.Doors.Count > s.Rooms.Count - 1) loops++;
            if (s.Rooms.Any(r => r.Doors.Count(d => d != null) >= 3)) branches++;
        }
        Assert.True(loops >= 10, $"{loops} of 20 had a short cut");
        Assert.Equal(20, branches);
    }

    [Fact]
    public void The_route_goes_door_by_door_and_prefers_open_doors()
    {
        var s = new Station(5, 3);
        var path = s.Route(s.DockRoom, s.CoreRoom);
        Assert.Equal(s.DockRoom, path[0]);
        Assert.Equal(s.CoreRoom, path[^1]);
        Assert.Equal(s.Distances(s.DockRoom)[s.CoreRoom] + 1, path.Count);
        for (int i = 0; i + 1 < path.Count; i++)
            Assert.Contains(s[path[i]].Doors, d => d != null && d.Other(path[i]) == path[i + 1]);
        var way = s.WayTo(s.DockRoom, s.CoreRoom);
        Assert.NotNull(way);
        Assert.Equal(path[1], s[s.DockRoom].Door(way!.Value)!.Other(s.DockRoom));
    }

    [Fact]
    public void The_same_seed_builds_the_same_station()
    {
        var a = new Station(4, 9);
        var b = new Station(4, 9);
        Assert.Equal(a.Doors.Select(d => (d.A, d.B)), b.Doors.Select(d => (d.A, d.B)));
        Assert.Equal((a.DockRoom, a.DockSide), (b.DockRoom, b.DockSide));
    }
}
