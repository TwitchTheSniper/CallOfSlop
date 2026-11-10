using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.Interiors;

/// <summary>
/// The builder (docs/sandbox/INTERIORS.md "The builder"): a blueprint replayed step by step through the game's own build rules (the vehicle
/// kit's <see cref="BlueprintScript"/>, used as it is), its deck measured into a hull volume, then a plan built on it from real parts: adapter
/// pads on the deck's capital sockets, Building slabs, walls, door corners and door walls with hung doors, stairs, bulkheads, and each
/// room's things. What the rules refuse is listed, never forced; a stair the rules won't stand is welded on (a freeform step) and counted.
/// </summary>
public sealed class InteriorBuilder : BlueprintScript
{
    public readonly List<string> Refused = new(), Notes = new(), Missing = new();
    public int Placed, Welded, Doors, LootSpotsSet;
    private readonly InteriorData _data;
    private readonly List<VPart> _base = new();
    private readonly Dictionary<(int L, int X, int Z), VPart> _floors = new();
    private readonly Dictionary<(int L, int X, int Z), VPart> _adapters = new();
    private readonly Dictionary<(int L, int X, int Z), List<VPart>> _walls = new();
    private Vector3 _corner; // the grid's (0, 0) corner on the deck's top, rest frame
    private float _floorY; // deck 0's slab origin height
    private int _w, _d;

    public SizeClass? Class { get; private set; }
    public HullVolume? Hull { get; private set; }
    public double Tonnes { get; private set; }

    private InteriorBuilder(InteriorData data, string name) : base(name) => _data = data;

    private string Shell(string key) => _data.Shell[key];

    /// <summary>Replays <paramref name="bp"/> (socket steps and wires; anything else is refused) and measures its root part's top as the hull.</summary>
    public static InteriorBuilder From(InteriorData data, ConstructBuild.Blueprint bp)
    {
        var b = new InteriorBuilder(data, bp.Name + " Fitted") { Group = bp.Group, Description = $"{bp.Name} with an interior from the interior planner (docs/sandbox/INTERIORS.md)." };
        if (bp.Steps.Count == 0)
            throw new VehicleKitException("the blueprint has no steps");
        if (bp.StepWeld.Any(w => w != null) || bp.Links.Count > 0 || bp.Hoses.Count > 0 || bp.Connectors.Count > 0 || bp.Tracks.Count > 0)
            throw new VehicleKitException("phase 1 carries socket steps and wires only; this blueprint has welds, links, hoses, connectors or tracks");
        var root = ConstructParts.Find(bp.Steps[0].Part) ?? throw new VehicleKitException($"no part \"{bp.Steps[0].Part}\"");
        b._base.Add(b.Root(root.Name, (root.BoundsMin + root.BoundsMax) * 0.5f));
        for (var i = 1; i < bp.Steps.Count; i++)
        {
            var s = bp.Steps[i];
            b._base.Add(b.Onto(b._base[s.On], s.Socket, s.Part, s.Own, s.Spin));
        }
        foreach (var (f, t) in bp.Wires)
            b.Wire(b._base[f], b._base[t]);
        b.Measure();
        return b;
    }

    /// <summary>The class from the body's mass, the grid laid on the root's top sockets with a margin, keep-outs from every other part, the bow from the helm.</summary>
    private void Measure()
    {
        double kg = 0;
        for (var i = 0; i < C.PartCount; i++)
            kg += C.DefOf(i).Mass;
        Tonnes = kg / 1000;
        Class = _data.ClassFor(Tonnes) ?? throw new VehicleKitException($"{Tonnes:0} t is below the smallest interior class ({_data.Classes[0].MinTonnes} t)");
        var rules = Class.Deck;
        C.PartBounds(0, out var lo, out var hi);
        var tops = new List<Vector3>();
        for (var s = 0; s < C.SocketCount(0); s++)
        {
            C.SocketAt(0, s, out var at, out var dir);
            if (ConstructParts.DirVector(dir).Y > 0.9f && MathF.Abs(at.Y - hi.Y) < 0.01f)
                tops.Add(at);
        }
        if (tops.Count == 0)
            throw new VehicleKitException($"the {C.DefOf(0).Name} has no sockets on its top to stand an interior on");
        var lattice = tops[0];
        var cell = rules.GridCell;
        var margin = rules.Margin * cell;
        // block corners sit 0.125 m before a slab's origin cell, on the root's socket lattice, so a slab's first socket lands on an adapter pad
        float First(float min, float lat) => lat - 0.125f + MathF.Ceiling((min + margin - 0.25f - (lat - 0.125f)) / cell) * cell;
        _corner = new Vector3(First(lo.X, lattice.X), hi.Y, First(lo.Z, lattice.Z));
        _w = (int)MathF.Floor((hi.X - margin + 0.25f - _corner.X) / cell);
        _d = (int)MathF.Floor((hi.Z - margin + 0.25f - _corner.Z) / cell);
        var adapter = ConstructParts.Find(Shell("adapter"))!;
        var front = adapter.Sockets.First(s => s.Name.StartsWith("front", StringComparison.Ordinal)).At.Y;
        var back = adapter.Sockets.First(s => s.Name == "back").At.Y;
        var floor = ConstructParts.Find(Shell("floor"))!;
        _floorY = hi.Y + (front - back) - floor.Sockets.First(s => s.Name == "b0_0").At.Y;
        var keep = new List<(Vector3, Vector3)>();
        for (var i = 1; i < C.PartCount; i++)
        {
            C.RestBounds(i, out var a, out var z);
            keep.Add((a - new Vector3(_corner.X, hi.Y, _corner.Z), z - new Vector3(_corner.X, hi.Y, _corner.Z)));
        }
        var fwd = C.SeatForward();
        var bowAtMin = MathF.Abs(fwd.X) > MathF.Abs(fwd.Z) ? fwd.X < 0 : true;
        if (MathF.Abs(fwd.X) <= MathF.Abs(fwd.Z))
            Notes.Add($"the helm faces across the deck ({fwd.X:0},{fwd.Z:0}): the bow is taken at the x-min end");
        Hull = new HullVolume(new Vector3(_w * cell, rules.Decks * rules.Storey, _d * cell), keep, bowAtMin);
    }

    /// <summary>A slab origin (cell 0's centre) of block <paramref name="x"/>, <paramref name="z"/> at level <paramref name="level"/> (0: deck 0's floor).</summary>
    private Vector3 Slab(int level, int x, int z) => new(_corner.X + 0.125f + x * 4, _floorY + level * 3, _corner.Z + 0.125f + z * 4);

    /// <summary>A wall origin on deck <paramref name="k"/>: block x, z and the offset in metres within it (walls stand a cell above the slab's origin).</summary>
    private Vector3 At(int k, int x, int z, float lx, float lz) => Slab(k, x, z) + new Vector3(lx, 0.25f, lz);

    private static byte TurnFor(Vector3 a)
    {
        for (byte r = 0; r < Rot24.Count; r++)
            if (Vector3.Dot(Rot24.Apply(r, Vector3.UnitX), a) > 0.99f && Rot24.Apply(r, Vector3.UnitY).Y > 0.99f)
                return r;
        return 0;
    }

    /// <summary>
    /// Fits <paramref name="part"/> with its origin at <paramref name="origin"/>, its own +x along <paramref name="a"/>, up kept up, on the first of
    /// <paramref name="on"/> whose free sockets take it there. Null when none does; <paramref name="why"/> is the first refusal.
    /// </summary>
    private VPart? Try(string part, Vector3 origin, Vector3 a, IEnumerable<VPart> on, out string why)
    {
        var def = ConstructParts.Find(part);
        why = "nothing to stand on";
        if (def == null)
        {
            why = $"no part \"{part}\"";
            return null;
        }
        var centre = origin + Rot24.Apply(TurnFor(a), (def.BoundsMin + def.BoundsMax) * 0.5f);
        var o = new Orient(Vector3.UnitX, a, Vector3.UnitY, Vector3.UnitY);
        var first = true;
        foreach (var c in on.Distinct())
        {
            try
            {
                var v = Put(part, centre, o, 0.07f, c);
                Placed++;
                return v;
            }
            catch (VehicleKitException e)
            {
                if (first)
                    why = e.Message;
                first = false;
            }
        }
        return null;
    }

    private VPart? Place(string part, Vector3 origin, Vector3 a, IEnumerable<VPart> on, string what)
    {
        var v = Try(part, origin, a, on, out var why);
        if (v == null)
            Refused.Add($"{what}: {why}");
        return v;
    }

    private IEnumerable<VPart> FloorsUnder(int level, IEnumerable<(int X, int Z)> blocks)
    {
        foreach (var (x, z) in blocks)
            if (_floors.TryGetValue((level, x, z), out var f))
                yield return f;
    }

    private void Remember(int k, IEnumerable<(int X, int Z)> blocks, VPart v)
    {
        foreach (var b in blocks)
        {
            if (!_walls.TryGetValue((k, b.X, b.Z), out var list))
                _walls[(k, b.X, b.Z)] = list = new List<VPart>();
            list.Add(v);
        }
    }

    /// <summary>Builds <paramref name="plan"/> on the deck: slabs level by level, each deck's walls, doors, bulkheads, stairs and rooms' things.</summary>
    public void Build(InteriorPlan plan, int seed)
    {
        var rng = new InteriorRng(seed ^ 0x5A17);
        // adapter pads on the root's top sockets under the grid
        for (var s = 0; s < C.SocketCount(0); s++)
        {
            C.SocketAt(0, s, out var at, out var dir);
            if (ConstructParts.DirVector(dir).Y < 0.9f || C.SocketUsed(0, s))
                continue;
            var fx = (at.X - _corner.X - 0.125f) / 4;
            var fz = (at.Z - _corner.Z - 0.125f) / 4;
            var (x, z) = ((int)MathF.Round(fx), (int)MathF.Round(fz));
            if (MathF.Abs(fx - x) > 0.01f || MathF.Abs(fz - z) > 0.01f || x < 0 || z < 0 || x >= plan.W || z >= plan.D || plan.Cells[0][x, z] == InteriorPlan.KeepOut)
                continue;
            try
            {
                _adapters[(0, x, z)] = Onto(_base[0], C.DefOf(0).Sockets[s].Name, Shell("adapter"), "back", 0);
                Placed++;
            }
            catch (VehicleKitException e)
            {
                Refused.Add($"adapter pad at {x},{z}: {e.Message}");
            }
        }
        if (_adapters.Count == 0)
        {
            Refused.Add("no adapter pad: the grid doesn't meet the deck's sockets");
            return;
        }
        Slabs(plan, 0);
        for (var k = 0; k < plan.Decks; k++)
        {
            foreach (var r in plan.Rooms.Where(r => r.Deck == k))
                Walls(plan, r);
            Bulkheads(plan, k);
            Stairs(plan, k);
            Slabs(plan, k + 1);
            foreach (var r in plan.Rooms.Where(r => r.Deck == k))
                Furnish(plan, r, rng);
        }
        foreach (var e in plan.Rooms.Where(r => r.Shaft == "elevator" && r.Deck == 0))
            Notes.Add($"freight elevator shaft {e.Rect} reserved through {plan.Decks} decks (slabs closed over it): no small-class lift part yet");
        if (plan.Rooms.Any(r => r.Shaft == "elevator"))
            Missing.Add("a powered freight lift in the small class (Lift Platform Medium 5x5 on a Piston Medium is medium class, so it won't take a Building slab's sockets)");
        foreach (var t in plan.Rooms.Select(r => _data.Rooms[r.Type].Modules).OfType<string>().Distinct())
            foreach (var m in _data.Sets[t].Missing)
                Missing.Add($"{_data.Rooms.Values.First(r => r.Modules == t).Name}: {m}");
        Notes.Add("power: machines and lamps are placed but not linked to the batteries (phase 2)");
    }

    /// <summary>Every block's slab at <paramref name="level"/> (the top level is the roof), on the walls below or a neighbour, in passes until none is added.</summary>
    private void Slabs(InteriorPlan plan, int level)
    {
        var todo = new List<(int X, int Z)>();
        for (var x = 0; x < plan.W; x++)
            for (var z = 0; z < plan.D; z++)
            {
                var below = level == 0 ? plan.Cells[0][x, z] : plan.Cells[level - 1][x, z];
                if (below != InteriorPlan.KeepOut)
                    todo.Add((x, z));
            }
        var why = new Dictionary<(int, int), string>();
        for (var pass = 0; pass < 64 && todo.Count > 0; pass++)
        {
            var added = 0;
            for (var i = 0; i < todo.Count; i++)
            {
                var (x, z) = todo[i];
                var on = new List<VPart>();
                if (_adapters.TryGetValue((level, x, z), out var pad))
                    on.Add(pad);
                if (level > 0 && _walls.TryGetValue((level - 1, x, z), out var walls))
                    on.AddRange(walls);
                foreach (var (dx, dz) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                    if (_floors.TryGetValue((level, x + dx, z + dz), out var n))
                        on.Add(n);
                if (on.Count == 0)
                    continue;
                var stairBelow = level > 0 && level < plan.Decks && plan.Rooms.Any(r => r.Shaft == "stairs" && r.Deck == level - 1 && r.Rect.X0 == x && r.Rect.Z0 == z);
                var part = stairBelow ? Shell("stairFloor") : Shell("floor");
                var v = Try(part, Slab(level, x, z), Vector3.UnitX, on, out var w);
                if (v == null)
                {
                    why[(x, z)] = w;
                    continue;
                }
                _floors[(level, x, z)] = v;
                todo.RemoveAt(i--);
                added++;
            }
            if (added == 0)
                break;
        }
        foreach (var b in todo)
            Refused.Add($"slab level {level} at {b.X},{b.Z}: {why.GetValueOrDefault(b, "no neighbour or wall to stand on")}");
    }

    private static Side Opposite(Side s) => s switch { Side.North => Side.South, Side.South => Side.North, Side.East => Side.West, _ => Side.East };

    /// <summary>A room's own walls: four corner pieces with 2 m legs and 4 m pieces between, doorways where the plan has doors (both rooms of a shared door open theirs, lined up).</summary>
    private void Walls(InteriorPlan plan, PlanRoom r)
    {
        var k = r.Deck;
        var g = r.Rect;
        var doors = new Dictionary<(Side, int), (PlanDoor D, bool Own)>();
        foreach (var d in plan.Doors.Where(d => d.Room == r.Id))
            doors[(d.Side, d.Slot)] = (d, true);
        foreach (var d in plan.Doors.Where(d => d.Other == r.Id))
        {
            var a = plan.Rooms[d.Room].Rect;
            var startA = d.Side is Side.North or Side.South ? a.X0 : a.Z0;
            var startB = d.Side is Side.North or Side.South ? g.X0 : g.Z0;
            doors[(Opposite(d.Side), startA + d.Slot - startB)] = (d, false);
        }
        var what = $"{r.Name} deck {k}";
        // corners: NW leg A +x (the north side's door corner), NE +z (east), SE -x (south), SW -z (west)
        var corners = new (Side S, int X, int Z, float Lx, float Lz, Vector3 A)[]
        {
            (Side.North, g.X0, g.Z0, 0, 0, Vector3.UnitX), (Side.East, g.X1, g.Z0, 3.75f, 0, Vector3.UnitZ),
            (Side.South, g.X1, g.Z1, 3.75f, 3.75f, -Vector3.UnitX), (Side.West, g.X0, g.Z1, 0, 3.75f, -Vector3.UnitZ),
        };
        foreach (var c in corners)
        {
            var door = doors.TryGetValue((c.S, -1), out var dd) ? dd : default;
            var blocks = new[] { (c.X, c.Z) };
            var v = Place(door.D != null ? Shell("doorCorner") : Shell("corner"), At(k, c.X, c.Z, c.Lx, c.Lz), c.A, FloorsUnder(k, blocks), $"{what} corner");
            if (v == null)
                continue;
            Remember(k, blocks, v);
            if (door.D != null)
                Hang(v, door, what);
        }
        // middle pieces: from 2 m into a block to 2 m into the next, +x along north and south, +z along west and east
        void Side_(Side s, int n, Func<int, (int X, int Z, float Lx, float Lz)> at, Vector3 a, Func<int, (int, int)[]> blocksOf)
        {
            for (var i = 0; i < n - 1; i++)
            {
                var (x, z, lx, lz) = at(i);
                var blocks = blocksOf(i);
                var o = At(k, x, z, lx, lz);
                if (doors.TryGetValue((s, i), out var door))
                {
                    if (door.D.Wide)
                    {
                        if (Place(Shell("garageWall"), o, a, FloorsUnder(k, blocks), $"{what} {door.D.Why}") is { } gw)
                            Remember(k, blocks, gw);
                        continue;
                    }
                    if (Place(Shell("doorWall"), o, a, FloorsUnder(k, blocks), $"{what} door") is { } dw)
                    {
                        Remember(k, blocks, dw);
                        Hang(dw, door, what);
                    }
                    if (Place(Shell("wall2"), o + a * 2, a, FloorsUnder(k, blocks), $"{what} wall") is { } w2)
                        Remember(k, blocks, w2);
                    continue;
                }
                if (Place(Shell("wall4"), o, a, FloorsUnder(k, blocks), $"{what} wall") is { } w4)
                    Remember(k, blocks, w4);
            }
        }
        Side_(Side.North, g.W, i => (g.X0 + i, g.Z0, 2, 0), Vector3.UnitX, i => [(g.X0 + i, g.Z0), (g.X0 + i + 1, g.Z0)]);
        Side_(Side.South, g.W, i => (g.X0 + i, g.Z1, 2, 3.75f), Vector3.UnitX, i => [(g.X0 + i, g.Z1), (g.X0 + i + 1, g.Z1)]);
        Side_(Side.West, g.D, i => (g.X0, g.Z0 + i, 0, 2), Vector3.UnitZ, i => [(g.X0, g.Z0 + i), (g.X0, g.Z0 + i + 1)]);
        Side_(Side.East, g.D, i => (g.X1, g.Z0 + i, 3.75f, 2), Vector3.UnitZ, i => [(g.X1, g.Z0 + i), (g.X1, g.Z0 + i + 1)]);
    }

    /// <summary>A door leaf on its hinge in a doorway the plan gives this room (a shared doorway's leaf hangs in one room's half only).</summary>
    private void Hang(VPart frame, (PlanDoor D, bool Own) door, string what)
    {
        if (!door.Own || door.D.Wide)
            return;
        try
        {
            var hinge = Onto(frame, "hinge", Shell("hinge"), "base");
            Onto(hinge, "leaf", Shell("door"), "hang");
            Placed += 2;
            Doors++;
        }
        catch (VehicleKitException e)
        {
            Refused.Add($"{what} door leaf: {e.Message}");
        }
    }

    /// <summary>Bulkheads across the corridor (a door in the spine, a wide opening over the reserved lane), and the upper decks' closed spine ends.</summary>
    private void Bulkheads(InteriorPlan plan, int k)
    {
        foreach (var b in plan.Bulkheads.Where(b => b.Deck == k))
            for (var z = b.Z0; z <= b.Z1; z++)
            {
                var blocks = new[] { (b.X, z) };
                var o = At(k, b.X, z, 0, 0);
                var what = $"bulkhead deck {k} at {b.X},{z}";
                if (plan.Cells[k][b.X, z] == InteriorPlan.Lane)
                {
                    if (Place(Shell("garageWall"), o, Vector3.UnitZ, FloorsUnder(k, blocks), what) is { } gw)
                        Remember(k, blocks, gw);
                }
                else if (z == plan.SpineZ0)
                {
                    if (Place(Shell("doorWall"), o, Vector3.UnitZ, FloorsUnder(k, blocks), what) is { } dw)
                    {
                        Remember(k, blocks, dw);
                        Hang(dw, (new PlanDoor { Why = "bulkhead" }, true), what);
                    }
                    if (Place(Shell("wall2"), o + Vector3.UnitZ * 2, Vector3.UnitZ, FloorsUnder(k, blocks), what) is { } w2)
                        Remember(k, blocks, w2);
                }
                else if (Place(Shell("wall4"), o, Vector3.UnitZ, FloorsUnder(k, blocks), what) is { } w4)
                    Remember(k, blocks, w4);
            }
        if (k == 0)
            return;
        for (var z = plan.SpineZ0; z <= plan.SpineZ1; z++)
            foreach (var (x, lx) in new[] { (0, 0f), (plan.W - 1, 3.75f) })
            {
                var blocks = new[] { (x, z) };
                if (Place(Shell("wall4"), At(k, x, z, lx, 0), Vector3.UnitZ, FloorsUnder(k, blocks), $"spine end deck {k}") is { } w)
                    Remember(k, blocks, w);
            }
    }

    /// <summary>A straight stair in each stairwell below the top deck: in its first block, a cell in from the west wall, rising +z under the stair slab's opening.</summary>
    private void Stairs(InteriorPlan plan, int k)
    {
        if (k >= plan.Decks - 1)
            return;
        foreach (var r in plan.Rooms.Where(r => r.Shaft == "stairs" && r.Deck == k))
        {
            var (x, z) = (r.Rect.X0, r.Rect.Z0);
            var on = FloorsUnder(k, [(x, z)]).Concat(_walls.GetValueOrDefault((k, x, z)) ?? new List<VPart>()).ToList();
            var origin = Slab(k, x, z) + new Vector3(0.25f, 0.25f, 1.0f);
            if (Try(Shell("stair"), origin, Vector3.UnitX, on, out var why) != null)
                continue;
            if (on.Count == 0)
            {
                Refused.Add($"stair deck {k} at {x},{z}: {why}");
                continue;
            }
            try
            {
                var def = ConstructParts.Find(Shell("stair"))!;
                Weld(Shell("stair"), on[0], origin + (def.BoundsMin + def.BoundsMax) * 0.5f, new Orient(Vector3.UnitX, Vector3.UnitX, Vector3.UnitY, Vector3.UnitY), 0, 0, 0);
                Welded++;
                Placed++;
                Notes.Add($"stair deck {k} at {x},{z} welded on (the rules found no socket: {why})");
            }
            catch (VehicleKitException e)
            {
                Refused.Add($"stair deck {k} at {x},{z}: {why}; welding it: {e.Message}");
            }
        }
    }

    /// <summary>A room's things from its module set, one to a free slab socket (block middles first, nearest the room's middle), with the room's loot hook.</summary>
    private void Furnish(InteriorPlan plan, PlanRoom r, InteriorRng rng)
    {
        if (_data.Rooms[r.Type].Modules is not { } setId || r.Shaft != null)
            return;
        var set = _data.Sets[setId];
        var spots = new List<(float Order, VPart Floor, string Socket, Vector3 At)>();
        var (cx, cz) = r.Rect.Centre;
        for (var x = r.Rect.X0; x <= r.Rect.X1; x++)
            for (var z = r.Rect.Z0; z <= r.Rect.Z1; z++)
            {
                if (!_floors.TryGetValue((r.Deck, x, z), out var f))
                    continue;
                var def = C.DefOf(f.Index);
                for (var s = 0; s < C.SocketCount(f.Index) && s < def.Sockets.Length; s++)
                {
                    var name = def.Sockets[s].Name;
                    if (C.SocketUsed(f.Index, s) || name[0] != 't')
                        continue;
                    C.SocketAt(f.Index, s, out var at, out _);
                    var local = def.Sockets[s].At;
                    var middle = local.X is > 1.5f and < 2.1f && local.Z is > 1.5f and < 2.1f;
                    spots.Add(((middle ? 0 : 100) + MathF.Abs(x - cx) + MathF.Abs(z - cz) + (float)rng.Next() * 0.3f, f, name, at));
                }
            }
        spots.Sort((a, b) => a.Order.CompareTo(b.Order));
        var table = _data.Rooms[r.Type].Loot is { } hook ? _data.LootHooks.GetValueOrDefault(hook) : null;
        var cells = r.Rect.Area;
        VPart? helm = null, director = null, gunner = null;
        foreach (var item in set.Items)
        {
            var def = ConstructParts.Find(item.Part);
            if (def == null)
            {
                Refused.Add($"{r.Name}: no part \"{item.Part}\"");
                continue;
            }
            var foot = def.Sockets.Where(s => ConstructParts.DirVector(s.Dir).Y < -0.9f).OrderBy(s => s.At.Y).Select(s => s.Name).FirstOrDefault();
            if (foot == null)
            {
                Refused.Add($"{r.Name}: a {def.Name} has no socket to stand on");
                continue;
            }
            var n = Math.Min(item.PerCells > 0 ? Math.Max(item.Count, cells / item.PerCells) : item.Count, 3 * cells);
            for (var i = 0; i < n; i++)
            {
                VPart? got = null;
                var why = "no free slab socket";
                for (var j = 0; j < spots.Count && got == null; j++)
                {
                    var spot = spots[j];
                    if (C.SocketUsed(spot.Floor.Index, Array.FindIndex(C.DefOf(spot.Floor.Index).Sockets, s => s.Name == spot.Socket)))
                    {
                        spots.RemoveAt(j--);
                        continue;
                    }
                    foreach (var a in new[] { Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitX, -Vector3.UnitZ })
                    {
                        try
                        {
                            got = Stand(def.Name, spot.At, new Orient(Vector3.UnitX, a, Vector3.UnitY, Vector3.UnitY), spot.Floor, foot);
                            Placed++;
                            break;
                        }
                        catch (VehicleKitException e)
                        {
                            why = e.Message;
                        }
                    }
                }
                if (got == null)
                {
                    Refused.Add($"{r.Name} deck {r.Deck}: {def.Name}: {why}");
                    break;
                }
                if (item.Loot && table != null)
                {
                    try
                    {
                        Loot(got, table);
                        LootSpotsSet++;
                    }
                    catch (VehicleKitException e)
                    {
                        Notes.Add($"{r.Name}: loot hook \"{_data.Rooms[r.Type].Loot}\": {e.Message}");
                    }
                }
                if (item.Role == "helm")
                    helm = got;
                else if (item.Role == "gunner" && def.Role == PartRole.Seat)
                    gunner = got;
                else if (item.Role == "gunner")
                    director = got;
            }
        }
        if (helm != null && _base.FirstOrDefault(p => p.Name == "Driving Module") is { } drive)
            TryWire(helm, drive, $"{r.Name}: helm to the Driving Module");
        if (gunner != null && director != null)
            TryWire(gunner, director, $"{r.Name}: gunner's station to the Fire Director");
    }

    private void TryWire(VPart a, VPart b, string what)
    {
        try
        {
            Wire(a, b);
            Notes.Add($"{what}: wired");
        }
        catch (VehicleKitException e)
        {
            Notes.Add($"{what}: not wired ({e.Message})");
        }
    }
}
