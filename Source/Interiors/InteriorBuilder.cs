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
    public int Placed, Welded, Doors, LootSpotsSet, Lifts, Wired;
    private readonly InteriorData _data;
    private readonly List<VPart> _base = new();
    private readonly Dictionary<(int L, int X, int Z), VPart> _floors = new();
    private readonly Dictionary<(int L, int X, int Z), VPart> _adapters = new();
    private readonly Dictionary<(int L, int X, int Z), List<VPart>> _walls = new();
    private readonly Dictionary<(int Lv, int Room, Side S, int I), VPart> _pieces = new(); // each room's 4 m middle pieces and door walls, by plan (its level base), side and number
    private readonly HashSet<(int L, int X, int Z)> _holes = new(); // blocks a lift car runs up through (no slab), by level
    private readonly HashSet<(int L, int X, int Z)> _stairTops = new(); // blocks a stair comes up through (a Stair Floor), by level
    private int _lv; // the level of the plan being built's deck 0 (levels: 0 is the top interior's deck 0; the chassis void's are below, -5 to -1)
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
        // phase 6: the vehicle's own helm (a seat wired into a Driving Module) and what hangs on it are carried into the bridge
        // (CarryHelm), so the bridge takes it over rather than holding a second helm that can't drive (a module takes one input)
        var carry = HelmChain(bp);
        Vector3? fwd = null;
        if (carry != null)
        {
            var scratch = new InteriorBuilder(data, "scratch");
            var all = new List<VPart> { scratch.Root(root.Name, (root.BoundsMin + root.BoundsMax) * 0.5f) };
            for (var i = 1; i < bp.Steps.Count; i++)
                all.Add(scratch.Onto(all[bp.Steps[i].On], bp.Steps[i].Socket, bp.Steps[i].Part, bp.Steps[i].Own, bp.Steps[i].Spin));
            fwd = scratch.C.SeatForward();
            b._helmAxis = Rot24.Apply(scratch.C.Parts[all[carry.Value.Helm].Index].Rot, Vector3.UnitX);
            b._carry = (bp, carry.Value.Helm, carry.Value.Steps);
        }
        b._base.Add(b.Root(root.Name, (root.BoundsMin + root.BoundsMax) * 0.5f));
        b._byStep[0] = b._base[0];
        for (var i = 1; i < bp.Steps.Count; i++)
        {
            if (carry != null && (carry.Value.Steps.Contains(i) || carry.Value.Pad == i))
                continue;
            var s = bp.Steps[i];
            b._byStep[i] = b.Onto(b._byStep[s.On], s.Socket, s.Part, s.Own, s.Spin);
            b._base.Add(b._byStep[i]);
        }
        foreach (var (f, t) in bp.Wires)
            if (b._byStep.ContainsKey(f) && b._byStep.ContainsKey(t))
                b.Wire(b._byStep[f], b._byStep[t]);
        b.Measure(fwd);
        foreach (var pn in data.Panels)
            if (ConstructParts.Find(pn) is { } pd)
                b._panels.Add((pn, (int)MathF.Round((pd.BoundsMax.X - pd.BoundsMin.X) / 4f)));
        b._panels.Sort((x, y) => y.Cells.CompareTo(x.Cells));
        return b;
    }

    private readonly Dictionary<int, VPart> _byStep = new(); // the blueprint's steps as replayed (and the carried helm chain's, once in the bridge)
    private readonly HashSet<int> _carried = new(); // part indices of the carried helm chain (counted as the vehicle's)
    private (ConstructBuild.Blueprint Bp, int Helm, HashSet<int> Steps)? _carry;
    private Vector3 _helmAxis = Vector3.UnitX; // the carried helm's own +x, so it faces the way it did

    /// <summary>
    /// The blueprint's helm chain: a seat step wired into a Driving Module, with every step that hangs on it (the module, its battery). Null
    /// when there is none, or when a step outside the chain hangs on it. <c>Pad</c>: the adapter the helm stood on, when nothing else does.
    /// </summary>
    private static (int Helm, HashSet<int> Steps, int Pad)? HelmChain(ConstructBuild.Blueprint bp)
    {
        for (var i = 1; i < bp.Steps.Count; i++)
        {
            if (ConstructParts.Find(bp.Steps[i].Part) is not { Role: PartRole.Seat })
                continue;
            if (!bp.Wires.Any(w => w.Item1 == i && bp.Steps[w.Item2].Part == "Driving Module"))
                continue;
            var steps = new HashSet<int> { i };
            for (var grew = true; grew;)
            {
                grew = false;
                for (var j = i + 1; j < bp.Steps.Count; j++)
                    if (!steps.Contains(j) && steps.Contains(bp.Steps[j].On))
                        grew = steps.Add(j);
            }
            var on = bp.Steps[i].On;
            var pad = on > 0 && ConstructParts.Find(bp.Steps[on].Part)?.Name == "Adapter Capital to Small" && Enumerable.Range(1, bp.Steps.Count - 1).All(j => j == i || bp.Steps[j].On != on) ? on : -1;
            return (i, steps, pad);
        }
        return null;
    }

    /// <summary>
    /// Fits the carried helm chain's other steps on <paramref name="helm"/> (the bridge's helm, placed as the vehicle's) and its wires. False,
    /// with the reason in Refused, when a step won't go.
    /// </summary>
    private bool CarryHelm(VPart helm, string room)
    {
        var (bp, h, steps) = _carry!.Value;
        _carry = null;
        _byStep[h] = helm;
        _carried.Add(helm.Index);
        foreach (var j in steps.Where(j => j != h).OrderBy(j => j))
        {
            var s = bp.Steps[j];
            try
            {
                _byStep[j] = Onto(_byStep[s.On], s.Socket, s.Part, s.Own, s.Spin);
                _carried.Add(_byStep[j].Index);
            }
            catch (VehicleKitException e)
            {
                Refused.Add($"{room}: the vehicle's {s.Part} won't go on its {bp.Steps[s.On].Part} ({e.Message})");
                return false;
            }
        }
        foreach (var (f, t) in bp.Wires)
            if ((steps.Contains(f) || steps.Contains(t)) && _byStep.ContainsKey(f) && _byStep.ContainsKey(t))
                TryWire(_byStep[f], _byStep[t], $"{room}: the vehicle's {bp.Steps[f].Part} to its {bp.Steps[t].Part}");
        Notes.Add($"{room}: the vehicle's helm is the bridge's, with its {string.Join(", ", steps.Where(j => j != h).Select(j => bp.Steps[j].Part))}");
        return true;
    }

    /// <summary>The class from the body's mass, the grid laid on the root's top sockets with a margin, keep-outs from every other part, the bow from the helm.</summary>
    private void Measure(Vector3? helmForward = null)
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
        var fwd = helmForward ?? C.SeatForward();
        var bowAtMin = MathF.Abs(fwd.X) > MathF.Abs(fwd.Z) ? fwd.X < 0 : true;
        if (MathF.Abs(fwd.X) <= MathF.Abs(fwd.Z))
            Notes.Add($"the helm faces across the deck ({fwd.X:0},{fwd.Z:0}): the bow is taken at the x-min end");
        Hull = new HullVolume(new Vector3(_w * cell, rules.Decks * rules.Storey, _d * cell), keep, bowAtMin);
    }

    /// <summary>
    /// The root's own hollow (phase 3, INTERIORS.md "The chassis is the interior"): the space between its lowest full-span plate and its highest
    /// one, as a hull volume whose keep-outs are the root's other solid boxes (its beams), on the same bow. Null (with the reason) when the root
    /// has no such hollow at least a storey high. Planned only: nothing is built in it until the chassis has sockets inside (see the doc).
    /// </summary>
    public HullVolume? ChassisVoid(out string why, out int innerSockets)
    {
        var def = C.DefOf(0);
        var size = def.BoundsMax - def.BoundsMin;
        var solid = def.Boxes.Take(def.SolidBoxes).ToList();
        bool Spans(CPartBox b) => b.Half.X * 2 >= size.X * 0.9f && b.Half.Z * 2 >= size.Z * 0.9f && b.Half.Y * 2 < size.Y * 0.5f;
        var plates = solid.Where(Spans).OrderBy(b => b.Center.Y).ToList();
        innerSockets = 0;
        if (plates.Count < 2)
        {
            why = $"the {def.Name} has no top and bottom plates with a hollow between them";
            return null;
        }
        var floorY = plates[0].Center.Y + plates[0].Half.Y;
        var roofY = plates[^1].Center.Y - plates[^1].Half.Y;
        var storey = Class!.Deck.Storey;
        if (roofY - floorY < storey)
        {
            why = $"the {def.Name}'s hollow is {roofY - floorY:0.0} m high, under a storey";
            return null;
        }
        innerSockets = def.Sockets.Count(s => s.At.Y > floorY - 0.01f && s.At.Y < roofY + 0.01f && ConstructParts.DirVector(s.Dir).Y > 0.9f);
        // the walls round its edge (beams along a whole side) bound the hollow; the beams inside are keep-outs
        var inside = solid.Where(b => !plates.Contains(b) && b.Center.Y + b.Half.Y > floorY && b.Center.Y - b.Half.Y < roofY).ToList();
        float x0 = def.BoundsMin.X, x1 = def.BoundsMax.X, z0 = def.BoundsMin.Z, z1 = def.BoundsMax.Z;
        foreach (var b in inside.ToList())
        {
            var (lo, hi) = (b.Center - b.Half, b.Center + b.Half);
            var alongZ = hi.Z - lo.Z >= size.Z * 0.9f;
            var alongX = hi.X - lo.X >= size.X * 0.9f;
            if (alongZ && lo.X <= def.BoundsMin.X + 0.01f) x0 = MathF.Max(x0, hi.X);
            else if (alongZ && hi.X >= def.BoundsMax.X - 0.01f) x1 = MathF.Min(x1, lo.X);
            else if (alongX && lo.Z <= def.BoundsMin.Z + 0.05f) z0 = MathF.Max(z0, hi.Z);
            else if (alongX && hi.Z >= def.BoundsMax.Z - 0.05f) z1 = MathF.Min(z1, lo.Z);
            else continue;
            inside.Remove(b);
        }
        var corner = new Vector3(x0, floorY, z0);
        var keep = inside.Select(b => (b.Center - b.Half - corner, b.Center + b.Half - corner)).ToList();
        why = "";
        return new HullVolume(new Vector3(x1 - x0, roofY - floorY, z1 - z0), keep, Hull!.BowAtMinX);
    }

    /// <summary>A slab origin (cell 0's centre) of block <paramref name="x"/>, <paramref name="z"/> at level <paramref name="level"/> (0: deck 0's floor).</summary>
    private Vector3 Slab(int level, int x, int z) => new(_corner.X + 0.125f + x * 4, _floorY + (level + _lv) * 3, _corner.Z + 0.125f + z * 4);

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
            if (_floors.TryGetValue((level + _lv, x, z), out var f))
                yield return f;
    }

    private void Remember(int k, IEnumerable<(int X, int Z)> blocks, VPart v)
    {
        foreach (var b in blocks)
        {
            if (!_walls.TryGetValue((k + _lv, b.X, b.Z), out var list))
                _walls[(k + _lv, b.X, b.Z)] = list = new List<VPart>();
            list.Add(v);
        }
    }

    /// <summary>
    /// Builds <paramref name="plan"/> on the deck: slabs level by level, each deck's walls, doors, bulkheads, stairs and rooms' things. With
    /// <paramref name="voidPlan"/> (phase 4, a hollow chassis: <see cref="VoidHull"/>), the chassis void's plan is built first, its storeys
    /// below the deck on the same grid, and a mezzanine over its shafts carries the stairs and the lift up through the deck's hatches.
    /// </summary>
    public void Build(InteriorPlan plan, int seed, InteriorPlan? voidPlan = null)
    {
        var rng = new InteriorRng(seed ^ 0x5A17);
        Pads(plan, voidPlan);
        if (!_adapters.Keys.Any(a => a.L == 0))
        {
            Refused.Add("no adapter pad: the grid doesn't meet the deck's sockets");
            return;
        }
        if (voidPlan != null)
            Notes.Add($"adapter pads: {_adapters.Keys.Count(a => a.L == 0)} on the deck, {_adapters.Keys.Count(a => a.L < 0)} on the void's floor sockets ({string.Join(", ", _adapters.Keys.Where(a => a.L < 0).GroupBy(a => a.L).OrderBy(g => g.Key).Select(g => $"level {g.Key}: {g.Count()}"))})");
        var mezz = voidPlan != null ? Mezzanine(voidPlan, plan) : new List<PlanRoom>();
        // the shafts over both plans: where each flight of stairs stands and comes up, where each lift car runs
        var flights = new List<(PlanRoom Room, int Lv)>();
        var gaps = new List<(PlanRoom Lo, int LoLv, PlanRoom Hi, int HiLv)>();
        if (voidPlan != null)
        {
            var rooms = voidPlan.Rooms.Concat(mezz).ToList();
            foreach (var r in rooms.Where(r => r.Shaft == "stairs" && (r.Deck < voidPlan.Decks - 1 || rooms.Any(m => m.Shaft == "stairs" && m.Deck == r.Deck + 1 && m.Rect == r.Rect))))
                flights.Add((r, VoidLevel));
            foreach (var r in rooms.Where(r => r.Shaft == "elevator" && r.Rect.D >= 3))
                if (rooms.FirstOrDefault(m => m.Shaft == "elevator" && m.Deck == r.Deck + 1 && m.Rect == r.Rect) is { } up)
                    gaps.Add((r, VoidLevel, up, VoidLevel));
            foreach (var m in mezz)
                if (m.Shaft == "stairs")
                    flights.Add((m, VoidLevel));
                else if (m.Shaft == "elevator" && plan.Rooms.FirstOrDefault(r => r.Shaft == "elevator" && r.Deck == 0 && r.Rect == m.Rect) is { } top)
                    gaps.Add((m, VoidLevel, top, 0));
        }
        foreach (var r in plan.Rooms.Where(r => r.Shaft == "stairs" && r.Deck < plan.Decks - 1))
            flights.Add((r, 0));
        foreach (var r in plan.Rooms.Where(r => r.Shaft == "elevator" && r.Rect.D >= 3))
            if (plan.Rooms.FirstOrDefault(m => m.Shaft == "elevator" && m.Deck == r.Deck + 1 && m.Rect == r.Rect) is { } up)
                gaps.Add((r, 0, up, 0));
        foreach (var (r, lv) in flights)
            _stairTops.Add((r.Deck + lv + 1, r.Rect.X0, StairBlock(r, lv)));
        foreach (var (lo, lv, _, _) in gaps)
            _holes.Add((lo.Deck + lv + 1, lo.Rect.X0, CarBlock(lo, lv)));
        _jointReserve = gaps.Count;
        if (voidPlan != null)
        {
            _lv = VoidLevel;
            Decks(voidPlan, rng, flights, mezz);
        }
        _lv = 0;
        Decks(plan, rng, flights, null);
        _jointReserve = 0;
        foreach (var g in gaps)
            Lift(g.Lo, g.LoLv, g.Hi, g.HiLv);
        // (in the budget's order: the deck's other doorways' leaves, then the void's, then the generated rooms' things; what doesn't fit is left out and reported)
        foreach (var (frame, door, what, lv) in _laterLeaves.OrderByDescending(l => l.Lv))
        {
            _lv = lv;
            Hang(frame, door, what, true);
        }
        foreach (var (p, r, lv) in _laterRooms)
        {
            _lv = lv;
            var (items, parts) = Estimate(r);
            if (C.LinkCount + items * 3 > _budget.Links || C.PartCount + parts > _budget.Parts)
            {
                Bare.Add($"{r.Name} (level {r.Deck + lv})");
                continue;
            }
            Furnish(p, r, rng);
        }
        _lv = 0;
        if (OpenDoorways > 0)
            Notes.Add($"budget: {OpenDoorways} doorways left open (a frame, no leaf): each leaf's hinge is a joint and the class's budget is {_budget.Joints} of {Construct.MaxJoints}; critical rooms and bulkheads first, then the deck's, then the void's");
        if (Bare.Count > 0)
            Notes.Add($"budget: {Bare.Count} generated rooms left bare (over the class's {_budget.Links} links or {_budget.Parts} parts): {string.Join(", ", Bare.Take(12))}{(Bare.Count > 12 ? ", ..." : "")}");
        if (Lifts > 0)
            Notes.Add($"freight lift: {Lifts} car(s) of {gaps.Count}, stacked a storey each (levels {string.Join(", ", gaps.Select(g => $"{g.Lo.Deck + g.LoLv}-{g.Hi.Deck + g.HiLv}"))}), calls from every landing and each car");
        foreach (var p in voidPlan != null ? new[] { voidPlan, plan } : new[] { plan })
            foreach (var t in p.Rooms.Select(r => _data.Rooms[r.Type].Modules).OfType<string>().Distinct())
                foreach (var m in _data.Sets[t].Missing)
                    Missing.Add($"{_data.Rooms.Values.First(r => r.Modules == t).Name}: {m}");
        if (_carry != null)
            Refused.Add($"the vehicle's {_carry.Value.Bp.Steps[_carry.Value.Helm].Part} had no bridge to go in: the fitted construct has no helm");
        Power();
    }

    /// <summary>One plan's decks, bottom up (the level base <see cref="_lv"/> set): slabs, walls, bulkheads, stairs, the next slabs, the rooms' things; then the mezzanine over its shafts, if any.</summary>
    private void Decks(InteriorPlan plan, InteriorRng rng, List<(PlanRoom Room, int Lv)> flights, List<PlanRoom>? mezz)
    {
        Slabs(plan, 0);
        for (var k = 0; k < plan.Decks; k++)
        {
            foreach (var r in plan.Rooms.Where(r => r.Deck == k && !(_lv < 0 && r.Type == "dead"))) // (the void's dead space: neither slab nor walls)
                Walls(plan, r);
            Bulkheads(plan, k);
            Stairs(plan, k, flights);
            if (k + 1 < plan.Decks || mezz == null)
                Slabs(plan, k + 1);
            foreach (var r in plan.Rooms.Where(r => r.Deck == k))
                if (r.Origin == "generated")
                    _laterRooms.Add((plan, r, _lv));
                else
                    Furnish(plan, r, rng);
        }
        if (mezz == null || mezz.Count == 0)
            return;
        var only = mezz.SelectMany(m => m.Cells()).ToHashSet();
        Slabs(plan, plan.Decks, only);
        foreach (var m in mezz)
            Walls(plan, m);
        Stairs(plan, plan.Decks, flights);
    }

    /// <summary>The level of the chassis void's lowest storey (the top interior's deck 0 is level 0), set by <see cref="VoidHull"/>.</summary>
    public int VoidLevel { get; private set; }

    /// <summary>
    /// The hollow chassis's void (phase 4, INTERIORS.md "Phase 4"): the hollow between the root's bottom and top plates as a hull volume on
    /// the deck's own grid (the same corner and size, so the shafts line up with the deck's and its hatches), its beams as keep-outs. Null
    /// (with the reason) unless the root has floor sockets inside the hollow, a storey apart on the deck's levels.
    /// </summary>
    public HullVolume? VoidHull(out string why)
    {
        why = "";
        var def = C.DefOf(0);
        var size = def.BoundsMax - def.BoundsMin;
        var solid = def.Boxes.Take(def.SolidBoxes).ToList();
        bool Plate(CPartBox b) => b.Half.Y * 2 < size.Y * 0.25f && (b.Half.X * 2 >= size.X * 0.9f || b.Half.Z * 2 >= size.Z * 0.9f);
        var low = solid.Where(b => Plate(b) && b.Center.Y - b.Half.Y <= def.BoundsMin.Y + 0.01f).ToList();
        var high = solid.Where(b => Plate(b) && b.Center.Y + b.Half.Y >= def.BoundsMax.Y - 0.01f).ToList();
        if (low.Count == 0 || high.Count == 0)
        {
            why = $"the {def.Name} has no top and bottom plates with a hollow between them";
            return null;
        }
        var floorY = low.Max(b => b.Center.Y + b.Half.Y);
        var roofY = high.Min(b => b.Center.Y - b.Half.Y);
        var adapter = ConstructParts.Find(Shell("adapter"))!;
        var pad = adapter.Sockets.First(s => s.Name.StartsWith("front", StringComparison.Ordinal)).At.Y - adapter.Sockets.First(s => s.Name == "back").At.Y;
        var slabOff = -ConstructParts.Find(Shell("floor"))!.Sockets.First(s => s.Name == "b0_0").At.Y;
        var inner = def.Sockets.Where(s => s.At.Y > floorY - 0.01f && s.At.Y < roofY - 0.01f && ConstructParts.DirVector(s.Dir).Y > 0.9f).ToList();
        if (inner.Count == 0)
        {
            why = $"the {def.Name} has no floor sockets inside its hollow";
            return null;
        }
        var lv = (floorY + pad + slabOff - _floorY) / 3f;
        if (MathF.Abs(lv - MathF.Round(lv)) > 0.01f)
        {
            why = $"the {def.Name}'s hollow floor is {lv:0.00} storeys below the deck's: its storeys don't line up with the deck's";
            return null;
        }
        VoidLevel = (int)MathF.Round(lv);
        var corner = new Vector3(_corner.X, floorY, _corner.Z);
        // (a floor socket's bracket, just under the slab it carries, is no keep-out)
        bool Bracket(CPartBox b) => inner.Any(s => MathF.Abs(s.At.Y - (b.Center.Y + b.Half.Y)) < 0.01f && MathF.Abs(s.At.X - b.Center.X) <= b.Half.X && MathF.Abs(s.At.Z - b.Center.Z) <= b.Half.Z);
        var keep = solid.Where(b => !low.Contains(b) && !high.Contains(b) && !Bracket(b) && b.Center.Y + b.Half.Y > floorY + 0.01f && b.Center.Y - b.Half.Y < roofY - 0.01f)
            .Select(b => (b.Center - b.Half - corner, b.Center + b.Half - corner)).ToList();
        // (the storeys from the floor up to the last whole one under the roof; the deck's grid in plan)
        var storeys = (int)MathF.Floor((roofY - floorY) / 3f + 1e-4f);
        return new HullVolume(new Vector3(_w * 4, storeys * 3, _d * 4), keep, Hull!.BowAtMinX);
    }

    /// <summary>
    /// The mezzanine (phase 4): a storey over the void's top deck, only in its shafts that have the deck's shaft of the same kind right over
    /// them (through the hatches): a copy of each such shaft room one deck up, so walls and stairs build there as on any deck.
    /// </summary>
    private static List<PlanRoom> Mezzanine(InteriorPlan voidPlan, InteriorPlan plan)
    {
        var list = new List<PlanRoom>();
        foreach (var r in voidPlan.Rooms.Where(r => r.Shaft != null && r.Deck == voidPlan.Decks - 1))
            if (plan.Rooms.Any(t => t.Shaft == r.Shaft && t.Deck == 0 && t.Rect == r.Rect))
                list.Add(new PlanRoom { Id = 100_000 + r.Id, Type = r.Type, Name = r.Name + " (mezzanine)", Deck = voidPlan.Decks, Rect = r.Rect, Section = r.Section, System = true, Shaft = r.Shaft, Origin = "system" });
        return list;
    }

    /// <summary>The block a flight of stairs stands in: its stairwell's first block on even levels, the next on odd ones, so flights alternate and none stands over the one below.</summary>
    private static int StairBlock(PlanRoom r, int lv) => r.Rect.Z0 + (r.Rect.D >= 2 && ((r.Deck + lv) & 1) != 0 ? 1 : 0);

    /// <summary>The block a lift car runs through from a level: the shaft's middle block on even levels, its first on odd ones, so stacked cars alternate.</summary>
    private static int CarBlock(PlanRoom r, int lv) => r.Rect.Z0 + (((r.Deck + lv) & 1) == 0 ? r.Rect.D / 2 : 0);

    /// <summary>Adapter pads on the root's capital sockets under the grid: the top's on its top sockets, the void's on its floor sockets (by storey).</summary>
    private void Pads(InteriorPlan plan, InteriorPlan? voidPlan)
    {
        C.PartBounds(0, out _, out var hi);
        var adapter = ConstructParts.Find(Shell("adapter"))!;
        var pad = adapter.Sockets.First(s => s.Name.StartsWith("front", StringComparison.Ordinal)).At.Y - adapter.Sockets.First(s => s.Name == "back").At.Y;
        var slabOff = -ConstructParts.Find(Shell("floor"))!.Sockets.First(s => s.Name == "b0_0").At.Y;
        for (var s = 0; s < C.SocketCount(0); s++)
        {
            C.SocketAt(0, s, out var at, out var dir);
            if (ConstructParts.DirVector(dir).Y < 0.9f || C.SocketUsed(0, s))
                continue;
            var lvf = (at.Y + pad + slabOff - _floorY) / 3f;
            var level = (int)MathF.Round(lvf);
            var p = MathF.Abs(at.Y - hi.Y) < 0.01f ? plan : voidPlan;
            if (p == null || MathF.Abs(lvf - level) > 0.01f)
                continue;
            var deck = level - (p == plan ? 0 : VoidLevel);
            // a pad meets a slab's corner socket or its middle one (2 m in)
            var (x, z) = ((int)MathF.Floor((at.X - _corner.X) / 4), (int)MathF.Floor((at.Z - _corner.Z) / 4));
            var (rx, rz) = (at.X - _corner.X - 0.125f - x * 4, at.Z - _corner.Z - 0.125f - z * 4);
            bool On(float r) => MathF.Abs(r) < 0.01f || MathF.Abs(r - 2) < 0.01f;
            if (!On(rx) || !On(rz) || x < 0 || z < 0 || x >= p.W || z >= p.D || deck < 0 || deck >= p.Decks || p.Cells[deck][x, z] == InteriorPlan.KeepOut)
                continue;
            try
            {
                _adapters[(level, x, z)] = Onto(_base[0], C.DefOf(0).Sockets[s].Name, Shell("adapter"), "back", 0);
                Placed++;
            }
            catch (VehicleKitException e)
            {
                Refused.Add($"adapter pad at level {level} {x},{z}: {e.Message}");
            }
        }
    }

    public const string LiftRail = "Linear Actuator 3x3 4 m", LiftCar = "Freight Lift Car";
    public const int LiftStopLow = 12, LiftStopHigh = 87; // % of the 4 m rail: 0.48 m and 3.48 m, a storey (3 m) apart

    /// <summary>
    /// One freight lift car (INTERIORS.md "Phase 2"; phase 4 stacks one a storey): a Linear Actuator 3x3 4 m stood on end on the shaft's west
    /// wall, its carriage carrying a Freight Lift Car whose floor is 2 cm over the lower level's floor at the low stop and the upper level's at
    /// the high stop; the slab over its block is left out. Calls: a Toggle drives the rail's Set (0 down, 1 up); the car's button flips it, the
    /// lower landing's button flips it only while it says up (AND with the Toggle), the upper's only while it says down (AND with a NOT).
    /// </summary>
    private void Lift(PlanRoom lo, int loLv, PlanRoom hi, int hiLv)
    {
        var level = lo.Deck + loLv;
        var hole = (X: lo.Rect.X0, Z: CarBlock(lo, loLv));
        var piece = hole.Z - lo.Rect.Z0;
        var what = $"freight lift from level {level}";
        if (!_pieces.TryGetValue((loLv, lo.Id, Side.West, piece), out var wall))
        {
            Refused.Add($"{what}: the shaft's west wall piece {piece} isn't there to hold the rail");
            return;
        }
        var floorTop = _floorY + level * 3 + 0.125f;
        var (bx, bz) = (_corner.X + hole.X * 4, _corner.Z + hole.Z * 4);
        var def = ConstructParts.Find(LiftRail)!;
        var o = new Orient(Vector3.UnitZ, Vector3.UnitY, Vector3.UnitY, Vector3.UnitX); // the rail's length up, its carriage facing into the shaft (+x)
        var origin = new Vector3(bx + 0.25f + 0.1875f, floorTop + 2f, bz + 2.125f); // its back on the wall's face, its bottom on the floor
        VPart rail;
        try
        {
            rail = Put(LiftRail, origin + Rot24.Apply(FirstTurn(o), (def.BoundsMin + def.BoundsMax) * 0.5f), o, 0.05f, wall, "base.4");
            Placed++;
        }
        catch (VehicleKitException e)
        {
            Refused.Add($"{what}: rail: {e.Message}");
            return;
        }
        Config(rail, ("stopLow", LiftStopLow), ("stopHigh", LiftStopHigh), ("speed", 1.0));
        VPart? car = null;
        var why = "no turn keeps it upright";
        for (var sp = 0; sp < 4 && car == null; sp++)
            if (TurnOf(rail, "top", LiftCar, "mount", sp) is { } t && Rot24.Apply(t, Vector3.UnitY).Y > 0.9f)
                try
                {
                    car = Onto(rail, "top", LiftCar, "mount", sp);
                    Placed++;
                }
                catch (VehicleKitException e)
                {
                    why = e.Message;
                }
        if (car == null)
        {
            Refused.Add($"{what}: car: {why}");
            return;
        }
        // the calls, on the shaft's west wall facing in: logic low down by the rail, buttons at hand height by the landing (the block south of the car)
        var inward = Vector3.UnitX;
        var low = new List<VPart> { wall };
        if (_pieces.TryGetValue((loLv, lo.Id, Side.West, piece - 1), out var w0))
            low.Add(w0);
        var up = new List<VPart>();
        foreach (var i in new[] { piece, piece - 1 })
            if (_pieces.TryGetValue((hiLv, hi.Id, Side.West, i), out var w1))
                up.Add(w1);
        var landing = new Vector3(bx + 0.4f, floorTop + 1.4f, bz + 4.1f);
        // (the landing buttons first, at hand height: 1 to 1.8 m over their floor, where the wall's middle row of face sockets is, 1.375 m up;
        // then the gates low down by the rail, on what is left)
        var callLow = Mount("Button", low, inward, landing, $"{what}: lower landing", floorTop + 1f, floorTop + 1.8f);
        var callHigh = Mount("Button", up, inward, landing + new Vector3(0, 3, 0), $"{what}: upper landing", floorTop + 4f, floorTop + 4.8f);
        var toggle = Mount("Toggle", low, inward, new Vector3(bx, floorTop, bz + 1), what);
        var and1 = Mount("AND Gate", low, inward, new Vector3(bx, floorTop, bz + 1), what);
        var and2 = Mount("AND Gate", low, inward, new Vector3(bx, floorTop, bz + 1), what);
        var not = Mount("NOT Gate", low, inward, new Vector3(bx, floorTop, bz + 1), what);
        VPart? send = null;
        try
        {
            send = Onto(car, "button", "Button");
            Placed++;
        }
        catch (VehicleKitException e)
        {
            Refused.Add($"{what}: the car's button: {e.Message}");
        }
        if (toggle == null || and1 == null || and2 == null || not == null || callLow == null || callHigh == null || send == null)
            return;
        foreach (var (a, b) in new[] { (callLow, and1), (toggle, and1), (callHigh, and2), (toggle, not), (not, and2), (and1, toggle), (and2, toggle), (send, toggle), (toggle, rail) })
            if (!TryWire(a, b, null))
            {
                Refused.Add($"{what}: a wire {a.Name} -> {b.Name} won't go");
                return;
            }
        Lifts++;
        LiftCars.Add((level, rail, car, send, callLow, callHigh));
    }

    /// <summary>Each lift car built: its lower level, rail, car, the car's button and the two landing buttons (for a headless try).</summary>
    public readonly List<(int Level, VPart Rail, VPart Car, VPart Send, VPart CallLow, VPart CallHigh)> LiftCars = new();

    /// <summary>A part on the first free socket of <paramref name="on"/> that faces <paramref name="face"/>, nearest <paramref name="near"/> first.</summary>
    private VPart? Mount(string part, IEnumerable<VPart> on, Vector3 face, Vector3 near, string what, float minY = float.NegativeInfinity, float maxY = float.PositiveInfinity)
    {
        var spots = new List<(float D, VPart P, string S)>();
        foreach (var p in on)
        {
            var def = C.DefOf(p.Index);
            for (var s = 0; s < C.SocketCount(p.Index) && s < def.Sockets.Length; s++)
            {
                if (C.SocketUsed(p.Index, s))
                    continue;
                C.SocketAt(p.Index, s, out var at, out var dir);
                if (Vector3.Dot(ConstructParts.DirVector(dir), face) > 0.9f && at.Y >= minY && at.Y <= maxY)
                    spots.Add((Vector3.Distance(at, near), p, def.Sockets[s].Name));
            }
        }
        var why = "no free socket facing the right way";
        foreach (var (_, p, s) in spots.OrderBy(x => x.D))
            try
            {
                var v = Onto(p, s, part);
                Placed++;
                return v;
            }
            catch (VehicleKitException e)
            {
                why = e.Message;
            }
        Refused.Add($"{what}: a {part}: {why}");
        return null;
    }

    /// <summary>
    /// Power (INTERIORS.md "Phase 2"): the circuit is the frame (ConstructPower), so what is fitted to the interior is on the circuit of the
    /// batteries and plants it touches. Lists the electrical parts no working battery or plant reaches, and plants no fuel reaches.
    /// </summary>
    private void Power()
    {
        ConstructPower.Rebuild(C);
        if (!C.HasBattery)
        {
            Notes.Add("power: no battery or plant, so the construct isn't electrical: nothing needs power");
            return;
        }
        int on = 0, off = 0;
        var dead = new Dictionary<string, int>();
        var deadAt = new SortedDictionary<int, int>();
        for (var i = 0; i < C.PartCount; i++)
        {
            var d = C.DefOf(i);
            if (!d.Electric)
                continue;
            if (C.Parts[i].Powered)
                on++;
            else
            {
                off++;
                dead[d.Name] = dead.GetValueOrDefault(d.Name) + 1;
                C.PartBounds(i, out var plo, out _);
                var lvl = (int)MathF.Floor((plo.Y - _floorY + 0.5f) / 3);
                deadAt[lvl] = deadAt.GetValueOrDefault(lvl) + 1;
            }
        }
        var plants = Enumerable.Range(0, C.PartCount).Where(i => C.DefOf(i).Role == PartRole.Generator).ToList();
        var dry = plants.Count(i => C.DefOf(i).PlantFuel == EngineFuel.Fuel && !C.ReachesFuel(i));
        Notes.Add($"power: {on} electrical parts on the circuit, {off} not{(off > 0 ? $" ({string.Join(", ", dead.Select(kv => $"{kv.Value} {kv.Key}"))})" : "")}; " +
                  $"{C.ChargeCapacity:0} kJ held; {plants.Count} plant(s), {dry} with no fuel reaching it");
        if (off > 0)
            Notes.Add($"power: the parts off by level: {string.Join(", ", deadAt.Select(kv => $"{kv.Key}: {kv.Value}"))}");
        // (a construct holds at most Construct.MaxLinks socket links: a part fitted past it has none, so it is off the circuit and loose)
        var unlinked = Enumerable.Range(1, C.PartCount - 1).Count(i => C.FirstLink(i) < 0);
        Notes.Add($"links: {C.LinkCount} of {Construct.MaxLinks}{(unlinked > 0 ? $", {unlinked} parts with none (over the limit)" : "")}");
        if (unlinked > 0)
            Refused.Add($"{unlinked} parts have no link: the construct is over its {Construct.MaxLinks} links");
        PowerOff = off;
    }

    public int PowerOff, OpenDoorways;
    private int _jointReserve; // joints kept for the lifts, built last
    private readonly List<(VPart Frame, (PlanDoor D, bool Own) Door, string What, int Lv)> _laterLeaves = new(); // doorways other than critical rooms' and bulkheads', hung last while the budget lasts
    private readonly List<(InteriorPlan Plan, PlanRoom Room, int Lv)> _laterRooms = new(); // generated rooms, furnished last while the budget lasts
    public readonly List<string> Bare = new(); // generated rooms the budget left without things
    private Budget _budget => Class?.Budget ?? Budget.None;

    /// <summary>A room's things as <see cref="Furnish"/> counts them (items, and parts with their switch and day tanks), for the budget.</summary>
    private (int Items, int Parts) Estimate(PlanRoom r)
    {
        if (_data.Rooms[r.Type].Modules is not { } setId || r.Shaft != null)
            return (0, 0);
        var cells = r.Area;
        var items = _data.Sets[setId].Items.Sum(i => Math.Min(i.PerCells > 0 ? Math.Max(i.Count, cells / i.PerCells) : i.Count, 3 * cells));
        return (items, items * 2 + 1);
    }

    /// <summary>Every block's slab at <paramref name="level"/> (the top level is the roof), on the walls below or a neighbour, in passes until none is added. <paramref name="only"/>: just these blocks.</summary>
    private void Slabs(InteriorPlan plan, int level, HashSet<(int X, int Z)>? only = null)
    {
        var todo = new List<(int X, int Z)>();
        for (var x = 0; x < plan.W; x++)
            for (var z = 0; z < plan.D; z++)
            {
                var below = level == 0 ? plan.Cells[0][x, z] : plan.Cells[level - 1][x, z];
                if (below != InteriorPlan.KeepOut && !_holes.Contains((level + _lv, x, z)) && (only == null || only.Contains((x, z))) && !(_lv < 0 && DeadAt(plan, level, x, z)))
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
                if (_adapters.TryGetValue((level + _lv, x, z), out var pad))
                    on.Add(pad);
                if (_walls.TryGetValue((level - 1 + _lv, x, z), out var walls))
                    on.AddRange(walls);
                foreach (var (dx, dz) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                    if (_floors.TryGetValue((level + _lv, x + dx, z + dz), out var n))
                        on.Add(n);
                if (on.Count == 0)
                    continue;
                var part = _stairTops.Contains((level + _lv, x, z)) ? Shell("stairFloor") : Shell("floor");
                var v = Try(part, Slab(level, x, z), Vector3.UnitX, on, out var w);
                if (v == null)
                {
                    why[(x, z)] = w;
                    continue;
                }
                _floors[(level + _lv, x, z)] = v;
                todo.RemoveAt(i--);
                added++;
            }
            if (added == 0)
                break;
        }
        foreach (var b in todo)
            Refused.Add($"slab level {level + _lv} at {b.X},{b.Z}: {why.GetValueOrDefault(b, "no neighbour or wall to stand on")}");
    }

    /// <summary>A void (dead) room's cell on the deck under <paramref name="level"/> and over it, or none: the chassis void builds no slab there.</summary>
    private static bool DeadAt(InteriorPlan plan, int level, int x, int z)
    {
        bool Dead(int k) => k < 0 || k >= plan.Decks || plan.Cells[k][x, z] is var id && id >= 0 && plan.Rooms[id].Type == "dead";
        return Dead(level - 1) && Dead(level);
    }

    private static Side Opposite(Side s) => s switch { Side.North => Side.South, Side.South => Side.North, Side.East => Side.West, _ => Side.East };

    /// <summary>
    /// Whether room <paramref name="r"/> leaves its wall on side <paramref name="s"/> of cell <paramref name="x"/>, <paramref name="z"/> to the
    /// neighbour across it (phase 5: one wall between two rooms, not two back to back). The neighbour's wall stands at the shared edge
    /// either way, so the room on the south or east side of an edge builds it; a shaft always builds its own (its lift and calls hang on it),
    /// so its neighbours leave theirs out. The chassis void's dead space builds none, so its neighbours keep theirs.
    /// </summary>
    private bool Shares(InteriorPlan plan, PlanRoom r, Side s, int x, int z)
    {
        if (r.Shaft != null || r.Deck >= plan.Decks)
            return false;
        var (ax, az) = s switch { Side.North => (x, z - 1), Side.South => (x, z + 1), Side.East => (x + 1, z), _ => (x - 1, z) };
        if (ax < 0 || az < 0 || ax >= plan.W || az >= plan.D || plan.Cells[r.Deck][ax, az] is var id && id < 0 || id == r.Id)
            return false;
        var o = plan.Rooms[id];
        if (_lv < 0 && o.Type == "dead")
            return false;
        return o.Shaft != null || s is Side.North or Side.West;
    }

    /// <summary>
    /// A room's walls along the outline of its cells (a rectangle, or a free room's L or T), less the edges it shares with a neighbour that
    /// builds them (<see cref="Shares"/>): each straight run of edges gets wall from 2 m into a cell to 2 m into the next (the longest panel
    /// that fits between doorways, phase 5, or 4 m pieces in a shaft), a corner piece (2 m legs) at each convex corner and a 2 m piece at each
    /// end that meets an inner corner or a shared edge. Doorways go where the plan has doors; a doorway in a shared wall is built (and its
    /// leaf hung) by the room that builds the wall. A door's side and slot count along the room's rectangle, so only runs on the rectangle's
    /// edges take doors.
    /// </summary>
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
            // (the owner left its half of a shared wall to this room: the doorway and its leaf are this room's)
            var owner = plan.Rooms[d.Room];
            var leftToMe = owner.Shaft == null && (r.Shaft != null || d.Side is Side.North or Side.West);
            doors[(Opposite(d.Side), startA + d.Slot - startB)] = (d, leftToMe);
        }
        var what = $"{r.Name} deck {k}";
        var mine = r.Cells().ToHashSet();
        bool In(int x, int z) => mine.Contains((x, z));
        // each side's outward step, its runs' direction (min to max), where its pieces stand in a cell, its corner piece's turn and spot
        var sides = new (Side S, int Dx, int Dz, bool AlongX, float Lx, float Lz, Vector3 CornerA, float Cx, float Cz, bool CornerAtMin)[]
        {
            (Side.North, 0, -1, true, 0, 0, Vector3.UnitX, 0, 0, true),
            (Side.East, 1, 0, false, 3.75f, 0, Vector3.UnitZ, 3.75f, 0, true),
            (Side.South, 0, 1, true, 0, 3.75f, -Vector3.UnitX, 3.75f, 3.75f, false),
            (Side.West, -1, 0, false, 0, 0, -Vector3.UnitZ, 0, 3.75f, false),
        };
        var panels = r.Shaft == null ? _panels : new List<(string Part, int Cells)>();
        var done = new HashSet<(Side, int, int)>();
        var late = new List<(string Part, Vector3 At, Vector3 A, (int X, int Z) Block, (PlanDoor D, bool Own) Door)>();
        foreach (var sd in sides)
            foreach (var (cx, cz) in mine.OrderBy(c => c.X).ThenBy(c => c.Z))
            {
                if (In(cx + sd.Dx, cz + sd.Dz) || done.Contains((sd.S, cx, cz)) || Shares(plan, r, sd.S, cx, cz))
                    continue;
                // the run of edges this cell's starts, along x (north, south) or z (east, west)
                var (ux, uz) = sd.AlongX ? (1, 0) : (0, 1);
                var cellsOf = new List<(int X, int Z)>();
                for (var (x, z) = (cx, cz); In(x, z) && !In(x + sd.Dx, z + sd.Dz) && !Shares(plan, r, sd.S, x, z); x += ux, z += uz)
                {
                    cellsOf.Add((x, z));
                    done.Add((sd.S, x, z));
                }
                var n = cellsOf.Count;
                var onRect = sd.S switch { Side.North => cz == g.Z0, Side.South => cz == g.Z1, Side.West => cx == g.X0, _ => cx == g.X1 } &&
                             (sd.AlongX ? cellsOf[0].X >= g.X0 && cellsOf[^1].X <= g.X1 : cellsOf[0].Z >= g.Z0 && cellsOf[^1].Z <= g.Z1);
                var start = sd.AlongX ? g.X0 : g.Z0;
                var first = sd.AlongX ? cellsOf[0].X : cellsOf[0].Z;
                // an end is an inner corner (or meets a shared edge) when the cell beyond it along the run is the room's
                bool Inner(bool atMin)
                {
                    var (ex, ez) = atMin ? cellsOf[0] : cellsOf[^1];
                    var s = atMin ? -1 : 1;
                    return In(ex + ux * s, ez + uz * s);
                }
                Vector3 a = sd.AlongX ? Vector3.UnitX : Vector3.UnitZ;
                foreach (var atMin in new[] { true, false })
                {
                    var (ex, ez) = atMin ? cellsOf[0] : cellsOf[^1];
                    var half = At(k, ex, ez, sd.AlongX ? (atMin ? 0 : 2) : sd.Lx, sd.AlongX ? sd.Lz : (atMin ? 0 : 2)); // (a 2 m piece over the end cell's half at this end)
                    if (Inner(atMin))
                    {
                        if (Place(Shell("wall2"), half, a, FloorsUnder(k, [(ex, ez)]), $"{what} wall") is { } w2)
                            Remember(k, [(ex, ez)], w2);
                        continue;
                    }
                    // a convex corner: the side across it at this end (west or east of a run along x, north or south of one along z)
                    var across = sd.AlongX ? (atMin ? Side.West : Side.East) : (atMin ? Side.North : Side.South);
                    var acrossShared = Shares(plan, r, across, ex, ez);
                    if (atMin == sd.CornerAtMin)
                    {
                        // the convex corner this side starts at (walking the outline clockwise): the door corner of a one-cell side
                        var door = onRect && doors.TryGetValue((sd.S, -1), out var dd) && first == (sd.CornerAtMin ? start : (sd.AlongX ? g.X1 : g.Z1)) - (sd.CornerAtMin ? 0 : n - 1) ? dd : default;
                        if (acrossShared && door.D == null)
                        {
                            // (the side across is the neighbour's: this side's own end half only)
                            if (Place(Shell("wall2"), half, a, FloorsUnder(k, [(ex, ez)]), $"{what} wall") is { } w2c)
                                Remember(k, [(ex, ez)], w2c);
                            continue;
                        }
                        var cornerPart = door.D != null ? Shell("doorCorner") : Shell("corner");
                        if (!FloorsUnder(k, [(ex, ez)]).Any())
                        {
                            // (over a lift car's block there is no slab: the corner goes on once the walls beside it stand)
                            late.Add((cornerPart, At(k, ex, ez, sd.Cx, sd.Cz), sd.CornerA, (ex, ez), door));
                            continue;
                        }
                        var v = Place(cornerPart, At(k, ex, ez, sd.Cx, sd.Cz), sd.CornerA, FloorsUnder(k, [(ex, ez)]), $"{what} corner");
                        if (v != null)
                        {
                            Remember(k, [(ex, ez)], v);
                            if (door.D != null)
                                Hang(v, door, what, r.Critical);
                        }
                    }
                    else if (acrossShared)
                    {
                        // (the corner's owner, the side across, is the neighbour's: this side's end half)
                        if (Place(Shell("wall2"), half, a, FloorsUnder(k, [(ex, ez)]), $"{what} wall") is { } w2e)
                            Remember(k, [(ex, ez)], w2e);
                    }
                }
                for (var i = 0; i < n - 1; i++)
                {
                    var (x, z) = cellsOf[i];
                    var blocks = new[] { (x, z), cellsOf[i + 1] };
                    var o = At(k, x, z, sd.AlongX ? 2 : sd.Lx, sd.AlongX ? sd.Lz : 2);
                    var slot = first + i - start;
                    if (onRect && doors.TryGetValue((sd.S, slot), out var door))
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
                            _pieces[(_lv, r.Id, sd.S, slot)] = dw;
                            Hang(dw, door, what, r.Critical || door.D.Room >= 0 && plan.Rooms[door.D.Room].Critical);
                        }
                        if (Place(Shell("wall2"), o + a * 2, a, FloorsUnder(k, blocks), $"{what} wall") is { } w2)
                            Remember(k, blocks, w2);
                        continue;
                    }
                    // the longest panel that fits before the run's end or its next doorway (4 m pieces where there is none)
                    var free = 1;
                    while (i + free < n - 1 && !(onRect && doors.ContainsKey((sd.S, slot + free))))
                        free++;
                    VPart? wall = null;
                    var len = 1;
                    foreach (var (pn, cells) in panels)
                        if (cells <= free)
                        {
                            var span = cellsOf.Skip(i).Take(cells + 1).ToArray();
                            if (Try(pn, o, a, FloorsUnder(k, span), out _) is { } pv)
                            {
                                Remember(k, span, pv);
                                (wall, len) = (pv, cells);
                                break;
                            }
                        }
                    if (wall == null && Place(Shell("wall4"), o, a, FloorsUnder(k, blocks), $"{what} wall") is { } w4)
                    {
                        Remember(k, blocks, w4);
                        wall = w4;
                    }
                    if (wall != null && onRect)
                        for (var j = 0; j < len; j++)
                            _pieces[(_lv, r.Id, sd.S, slot + j)] = wall;
                    i += len - 1;
                }
            }
        foreach (var (part, at, a, (bx, bz), door) in late)
            if (Place(part, at, a, _walls.GetValueOrDefault((k + _lv, bx, bz)) ?? new List<VPart>(), $"{what} corner") is { } v)
            {
                Remember(k, [(bx, bz)], v);
                if (door.D != null)
                    Hang(v, door, what, r.Critical);
            }
    }

    /// <summary>The shell's panel walls (phase 5) with their length in grid cells, longest first: data's <c>shell.panels</c>, those this build has.</summary>
    private List<(string Part, int Cells)> _panels = new();

    /// <summary>
    /// A door leaf on its hinge in a doorway the plan gives this room (a shared doorway's leaf hangs in one room's half only). A hinge is a
    /// joint and a construct has at most <see cref="Construct.MaxJoints"/>: in the chassis void only <paramref name="important"/> doorways (critical
    /// rooms, bulkheads) get a leaf, and none is hung into the joints kept for the lifts; those doorways stay open and are counted.
    /// </summary>
    private void Hang(VPart frame, (PlanDoor D, bool Own) door, string what, bool important = false)
    {
        if (!door.Own || door.D.Wide)
            return;
        if (!important)
        {
            _laterLeaves.Add((frame, door, what, _lv));
            return;
        }
        if (C.JointCount >= Math.Min(Construct.MaxJoints, _budget.Joints) - _jointReserve || C.LinkCount + 2 > _budget.Links)
        {
            OpenDoorways++;
            return;
        }
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
                        Hang(dw, (new PlanDoor { Why = "bulkhead" }, true), what, true);
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

    /// <summary>
    /// A straight stair for each flight on this deck (<see cref="Build"/> lists them: each stairwell below a plan's top deck, and in a hollow
    /// chassis every void storey and the mezzanine): a cell in from the stairwell's west wall, rising +z under a Stair Floor's opening, in
    /// the block <see cref="StairBlock"/> gives (flights alternate between the first two blocks, so none stands over the one below).
    /// </summary>
    private void Stairs(InteriorPlan plan, int k, List<(PlanRoom Room, int Lv)> flights)
    {
        foreach (var (r, _) in flights.Where(f => f.Lv == _lv && f.Room.Deck == k))
        {
            var (x, z) = (r.Rect.X0, StairBlock(r, _lv));
            var on = FloorsUnder(k, [(x, z)]).Concat(_walls.GetValueOrDefault((k + _lv, x, z)) ?? new List<VPart>()).ToList();
            var origin = Slab(k, x, z) + new Vector3(0.25f, 0.25f, 1.0f);
            if (Try(Shell("stair"), origin, Vector3.UnitX, on, out var why) != null)
                continue;
            if (on.Count == 0)
            {
                Refused.Add($"stair level {k + _lv} at {x},{z}: {why}");
                continue;
            }
            try
            {
                var def = ConstructParts.Find(Shell("stair"))!;
                Weld(Shell("stair"), on[0], origin + (def.BoundsMin + def.BoundsMax) * 0.5f, new Orient(Vector3.UnitX, Vector3.UnitX, Vector3.UnitY, Vector3.UnitY), 0, 0, 0);
                Welded++;
                Placed++;
                Notes.Add($"stair level {k + _lv} at {x},{z} welded on (the rules found no socket: {why})");
            }
            catch (VehicleKitException e)
            {
                Refused.Add($"stair level {k + _lv} at {x},{z}: {why}; welding it: {e.Message}");
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
        foreach (var (x, z) in r.Cells())
            {
                if (!_floors.TryGetValue((r.Deck + _lv, x, z), out var f))
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
        var cells = r.Area;
        VPart? helm = null, director = null, gunner = null;
        var lamps = new List<VPart>();
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
            // (the gunner's station and its Fire Director within a wire's reach: the second goes on the free spot nearest the first)
            if (item.Role == "gunner" && (gunner ?? director) is { } partner)
            {
                C.PartBounds(partner.Index, out var plo, out var phi);
                var pc = (plo + phi) * 0.5f;
                spots.Sort((a, b) => Vector3.DistanceSquared(a.At, pc).CompareTo(Vector3.DistanceSquared(b.At, pc)));
            }
            // (the carried helm goes on the free spot nearest the base parts its chain is wired to, a leg's hip: a wire reaches 8 m)
            if (item.Role == "helm" && _carry is { } cy && def.Name == cy.Bp.Steps[cy.Helm].Part)
            {
                var ends = cy.Bp.Wires.SelectMany(w => new[] { (w.From, w.To), (w.To, w.From) }).Where(w => cy.Steps.Contains(w.Item1) && !cy.Steps.Contains(w.Item2) && _byStep.ContainsKey(w.Item2))
                    .Select(w => { C.PartBounds(_byStep[w.Item2].Index, out var lo, out var hi); return (lo, hi); }).ToList();
                if (ends.Count > 0)
                    spots.Sort((a, b) => ends.Sum(e => Vector3.Distance(a.At, Vector3.Clamp(a.At, e.lo, e.hi))).CompareTo(ends.Sum(e => Vector3.Distance(b.At, Vector3.Clamp(b.At, e.lo, e.hi)))));
            }
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
                    var takeOver = item.Role == "helm" && _carry != null && def.Name == _carry.Value.Bp.Steps[_carry.Value.Helm].Part;
                    foreach (var a in takeOver ? new[] { _helmAxis } : new[] { Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitX, -Vector3.UnitZ })
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
                if (def.Logic == CLogic.Lamp)
                    lamps.Add(got);
                if (item.Role == "plant" && def.PlantFuel == EngineFuel.Fuel)
                    Fuel(got, r);
                if (item.Role == "helm" && _carry != null && def.Name == _carry.Value.Bp.Steps[_carry.Value.Helm].Part)
                    CarryHelm(got, r.Name);
                else if (item.Role == "helm")
                    helm = got;
                else if (item.Role == "gunner" && def.Role == PartRole.Seat)
                    gunner = got;
                else if (item.Role == "gunner")
                    director = got;
            }
        }
        // the room's lights: a Switch on the first lamp, wired to every lamp (a lamp lights while its input is on)
        if (lamps.Count > 0)
        {
            VPart? sw = null;
            foreach (var s in new[] { "+x", "-x", "+z", "-z", "+y" })
                try
                {
                    sw = Onto(lamps[0], s, "Switch");
                    Config(sw, ("startOn", true)); // (the lights are on when it is built)
                    Placed++;
                    break;
                }
                catch (VehicleKitException)
                {
                }
            if (sw == null)
                Refused.Add($"{r.Name} deck {r.Deck}: no place for the lights' Switch on its lamp");
            else
                foreach (var l in lamps)
                    if (!TryWire(sw, l, null))
                        Notes.Add($"{r.Name} deck {r.Deck}: a lamp out of the switch's reach stays dark");
        }
        if (helm != null && _base.FirstOrDefault(p => p.Name == "Driving Module") is { } drive)
            TryWire(helm, drive, $"{r.Name}: helm to the Driving Module");
        if (gunner != null && director != null)
            TryWire(gunner, director, $"{r.Name}: gunner's station to the Fire Director");
    }

    /// <summary>A fuel plant's day tank: a Fuel Tank fitted to it, so fuel reaches it as it reaches an engine (Construct.FuelReaches).</summary>
    private void Fuel(VPart plant, PlanRoom r)
    {
        foreach (var s in new[] { "+y", "+y+", "+y-", "+x", "-x", "+z", "-z" })
            try
            {
                Onto(plant, s, "Fuel Tank");
                Placed++;
                return;
            }
            catch (VehicleKitException)
            {
            }
        Refused.Add($"{r.Name} deck {r.Deck}: no place for the {plant.Name}'s Fuel Tank");
    }

    /// <summary>What a part is for the budget (INTERIORS.md "Phase 5"): the base vehicle's own parts, then the interior's by kind.</summary>
    private string KindOf(int i)
    {
        if (i < _base.Count || _carried.Contains(i))
            return "vehicle";
        var n = C.DefOf(i).Name;
        if (n == Shell("floor") || n == Shell("stairFloor"))
            return "slabs";
        if (n == Shell("wall4") || n == Shell("wall2") || n == Shell("corner") || _panels.Any(p => p.Part == n))
            return "walls";
        if (n == Shell("doorWall") || n == Shell("doorCorner") || n == Shell("garageWall"))
            return "doorways";
        if (n == Shell("hinge") || n == Shell("door"))
            return "doors";
        if (n == Shell("adapter"))
            return "pads";
        if (n == Shell("stair"))
            return "stairs";
        if (n is LiftRail or LiftCar or "Toggle" or "AND Gate" or "NOT Gate" or "Button")
            return "lift";
        if (n is "Lamp" or "Switch")
            return "lights";
        return "furniture";
    }

    /// <summary>
    /// The construct's use of its limits by kind (INTERIORS.md "Phase 5"): parts, socket links (a link goes to the later of its two parts,
    /// the one whose placing made it), joints, wires and mass. Totals against the engine's limits and the class's budget.
    /// </summary>
    public List<string> BudgetLines()
    {
        var kinds = new SortedDictionary<string, (int Parts, int Links, int Joints, int Wires, double Kg)>();
        void Add(string k, int p, int l, int j, int w, double kg)
        {
            var v = kinds.GetValueOrDefault(k);
            kinds[k] = (v.Parts + p, v.Links + l, v.Joints + j, v.Wires + w, v.Kg + kg);
        }
        for (var i = 0; i < C.PartCount; i++)
            Add(KindOf(i), 1, 0, C.DefOf(i).HasJoint ? 1 : 0, 0, C.DefOf(i).Mass);
        for (var l = 0; l < C.LinkCount; l++)
            Add(KindOf(Math.Max(C.Links[l].A, C.Links[l].B)), 0, 1, 0, 0, 0);
        for (var w = 0; w < C.WireCount; w++)
            Add(KindOf(Math.Max(C.Wires[w].From, C.Wires[w].To)), 0, 0, 0, 1, 0);
        var lines = kinds.Select(kv => $"budget {kv.Key}: {kv.Value.Parts} parts, {kv.Value.Links} links, {kv.Value.Joints} joints, {kv.Value.Wires} wires, {kv.Value.Kg / 1000:0} t").ToList();
        lines.Add($"budget total: {C.PartCount} of {Construct.MaxParts} parts, {C.LinkCount} of {Construct.MaxLinks} links ({100.0 * C.LinkCount / Construct.MaxLinks:0}%), {C.JointCount} of {Construct.MaxJoints} joints, {C.WireCount} of {Construct.MaxWires} wires, {kinds.Values.Sum(v => v.Kg) / 1000:0} t");
        return lines;
    }

    private bool TryWire(VPart a, VPart b, string? what)
    {
        try
        {
            Wire(a, b);
            Wired++;
            if (what != null)
                Notes.Add($"{what}: wired");
            return true;
        }
        catch (VehicleKitException e)
        {
            if (what != null)
                Notes.Add($"{what}: not wired ({e.Message})");
            return false;
        }
    }
}
