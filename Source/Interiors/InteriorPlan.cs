using System.Text;

namespace CallOfSlop.Interiors;

/// <summary>A rectangle of grid cells, both ends inclusive. X runs along the spine (bow to stern or the other way), Z across it.</summary>
public readonly record struct GridRect(int X0, int Z0, int X1, int Z1)
{
    public int W => X1 - X0 + 1;
    public int D => Z1 - Z0 + 1;
    public int Area => W * D;
    public bool Contains(int x, int z) => x >= X0 && x <= X1 && z >= Z0 && z <= Z1;
    public (float X, float Z) Centre => ((X0 + X1) * 0.5f, (Z0 + Z1) * 0.5f);
    public override string ToString() => $"{W}x{D} at {X0},{Z0}";
}

/// <summary>Which edge of a room: North is its Z0 edge, South its Z1 edge, West its X0 edge, East its X1 edge.</summary>
public enum Side { North, East, South, West }

/// <summary>
/// A room on one deck. Phase 1 rooms are grid rectangles; a free room (phase 2) keeps its rectangle as the grid space it claims and adds an
/// outline (metres in the deck's frame) that the walls follow, so the planner's bookkeeping is the same for both.
/// </summary>
public sealed class PlanRoom
{
    public int Id;
    public string Type = "";
    public string Name = "";
    public int Deck;
    public GridRect Rect;
    public RoomLayout Layout = RoomLayout.Grid;
    public List<(float X, float Z)>? Outline; // (free rooms only: null on the grid)
    public int Section;
    public bool Critical, System, Inner, Outer;
    public string? Shaft; // "stairs" or "elevator": the same rectangle on every deck it joins
    public string Origin = "programme"; // programme, system, leftover, merged
    public char Mark;
    public override string ToString() => $"{Name} {Rect} deck {Deck}";
}

/// <summary>
/// A doorway in a room's wall. <see cref="Slot"/> -1 is the door corner at that side's first corner; otherwise the side's 4 m middle piece
/// number (it spans from 2 m into cell <c>start + Slot</c> to 2 m into the next), which lines up with the neighbour's piece across the edge.
/// </summary>
public sealed class PlanDoor
{
    public int Deck, Room, Other; // Other: a room id, Corridor or Outside
    public Side Side;
    public int Slot;
    public bool Wide; // a 3 m opening (Garage Wall): a hangar's ramp, a lane's bulkhead
    public string Why = "";
    public const int Corridor = -1, Outside = -2;
}

/// <summary>A bulkhead across the corridor at the west edge of column <see cref="X"/>: walls with a door in the spine, a wide opening over the reserved lane.</summary>
public sealed record PlanBulkhead(int Deck, int X, int Z0, int Z1);

/// <summary>A way in from outside: a spine end, an outside door, a hangar's ramp.</summary>
public sealed record PlanEntry(int Deck, int X, int Z, string Kind);

/// <summary>
/// An interior plan: decks of rooms on a grid, the corridor spine, the reserved logistics lane, stairs and elevator shafts, bulkhead sections
/// and entry points. It knows nothing of vehicles: the planner made it from a hull volume and keep-outs.
/// </summary>
public sealed class InteriorPlan
{
    public const int Free = -9, Corridor = -1, Lane = -2, KeepOut = -3;

    public SizeClass Class = null!;
    public int Seed, W, D, Decks;
    public bool BowAtMinX;
    public int SpineZ0, SpineZ1;
    public int[][,] Cells = []; // [deck][x, z]: a room id, or Corridor, Lane, KeepOut, Free
    public readonly List<PlanRoom> Rooms = new();
    public readonly List<PlanDoor> Doors = new();
    public readonly List<PlanBulkhead> Bulkheads = new();
    public readonly List<PlanEntry> Entries = new();
    public GridRect? ReservedRoute; // (deck 0)
    public readonly List<string> Notes = new(), Refused = new();

    public int SectionOf(int x) => x / Class.Deck.Section;

    public IEnumerable<PlanDoor> DoorsOf(int room) => Doors.Where(d => d.Room == room || d.Other == room);

    /// <summary>ASCII deck maps, a legend and the room list.</summary>
    public string Ascii()
    {
        var sb = new StringBuilder();
        sb.Append($"Interior plan: class {Class.Name}, seed {Seed}, {W}x{D} cells of {Class.Deck.GridCell:0} m, {Decks} deck(s), bow at {(BowAtMinX ? "x 0" : $"x {W - 1}")}\n");
        sb.Append("Key: letters rooms (list below), . corridor, = reserved logistics lane, # keep-out, S stairs, E elevator shaft, s storage, f fuel, - spare (unassigned), x void, | bulkhead line, + door\n");
        for (var k = Decks - 1; k >= 0; k--)
        {
            sb.Append($"\nDeck {k}:\n");
            var rows = new char[D * 2 + 1, W * 2 + 1];
            for (var r = 0; r < rows.GetLength(0); r++)
                for (var c = 0; c < rows.GetLength(1); c++)
                    rows[r, c] = ' ';
            for (var z = 0; z < D; z++)
                for (var x = 0; x < W; x++)
                {
                    var v = Cells[k][x, z];
                    var ch = v switch { Corridor => '.', Lane => '=', KeepOut => '#', Free => ' ', _ => Rooms[v].Mark };
                    rows[z * 2 + 1, x * 2 + 1] = ch;
                    // (between two cells of one space, the gap takes its mark; a wall between different spaces stays blank)
                    if (x + 1 < W && Cells[k][x + 1, z] == v && v != Free)
                        rows[z * 2 + 1, x * 2 + 2] = ch;
                    if (z + 1 < D && Cells[k][x, z + 1] == v && v != Free)
                        rows[z * 2 + 2, x * 2 + 1] = ch;
                }
            foreach (var b in Bulkheads.Where(b => b.Deck == k))
                for (var z = b.Z0; z <= b.Z1; z++)
                    rows[z * 2 + 1, b.X * 2] = '|';
            foreach (var d in Doors.Where(d => d.Deck == k))
            {
                var (r, c) = DoorMark(d);
                if (r >= 0 && r < rows.GetLength(0) && c >= 0 && c < rows.GetLength(1))
                    rows[r, c] = '+';
            }
            for (var r = 0; r < rows.GetLength(0); r++)
            {
                sb.Append("  ");
                for (var c = 0; c < rows.GetLength(1); c++)
                    sb.Append(rows[r, c]);
                sb.Append('\n');
            }
        }
        sb.Append("\nRooms:\n");
        foreach (var r in Rooms)
        {
            var doors = DoorsOf(r.Id).Select(d => d.Other == PlanDoor.Corridor ? "corridor" : d.Other == PlanDoor.Outside ? (d.Wide ? "outside (ramp)" : "outside") : Rooms[d.Other == r.Id ? d.Room : d.Other].Name);
            sb.Append($"  {r.Mark} {r.Name,-24} deck {r.Deck} {r.Rect,-14} section {r.Section}{(r.Critical ? " CRITICAL" : "")}{(r.Origin != "programme" ? $" ({r.Origin})" : "")} doors: {string.Join(", ", doors)}\n");
        }
        if (ReservedRoute is { } lane)
            sb.Append($"Reserved logistics route: deck 0, {lane} (kept clear for the train track)\n");
        sb.Append($"Entry points: {string.Join("; ", Entries.Select(e => $"{e.Kind} deck {e.Deck} at {e.X},{e.Z}"))}\n");
        sb.Append($"Bulkheads: {Bulkheads.Count} (sections of {Class.Deck.Section} cells; choke points at their spine doors)\n");
        return sb.ToString();
    }

    /// <summary>Where a door's mark goes on the doubled map.</summary>
    private (int R, int C) DoorMark(PlanDoor d)
    {
        var r = Rooms[d.Room].Rect;
        var along = d.Slot >= 0 ? d.Slot : d.Side == Side.South ? r.W - 1 : d.Side == Side.West ? r.D - 1 : 0;
        return d.Side switch
        {
            Side.North => (r.Z0 * 2, (r.X0 + along) * 2 + 1),
            Side.South => (r.Z1 * 2 + 2, (r.X0 + along) * 2 + 1),
            Side.West => ((r.Z0 + along) * 2 + 1, r.X0 * 2),
            _ => ((r.Z0 + along) * 2 + 1, r.X1 * 2 + 2),
        };
    }
}
