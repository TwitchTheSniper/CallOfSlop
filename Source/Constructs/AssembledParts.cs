namespace CallOfSlop;

/// <summary>
/// Run-time parts an Assembler makes by their key (2026-11-22: the Zero Day works' yard station could order none of the frigate's): an
/// Ordnance Rack of a shape no part file has (<c>"Ord Rack (1x6 drum 500x5500)"</c>, <see cref="ModularRack"/>) and an ordnance design
/// (<c>"Ordnance:M50:..."</c>, <see cref="Ordnance"/>). Neither is in Assembling.json, since a shape or a design is one of thousands: its
/// recipe is made from the key the first time it is asked for (<see cref="AssemblingTable.Of"/>), a rack's by the rule its presets' rows follow
/// (the part kit's <c>Assembling.RackRecipe</c>), a design's from the section parts the Ordnance Bench takes for it in survival
/// (<see cref="OrdPiece.Items"/>).
///
/// Its work order holds both the run-time id (<see cref="WorkOrder.Part"/>, in this process) and the key (<see cref="WorkOrder.Name"/>),
/// which is all that goes on the wire or in a save: the reader makes the def again from the key (<see cref="Order"/>).
/// </summary>
public static class AssembledParts
{
    private const int ChoiceBase = 10_000; // (a made recipe's "choice": past any table's list, so an Assembler's batch restarts when its head order's kind changes)
    private static readonly Dictionary<ushort, (ConstructPartDef Def, AssembleRecipe? Recipe)> Made = new();
    private static int _next;

    /// <summary>Is this a key an Assembler makes by: an Ordnance Rack's shape or an ordnance design?</summary>
    public static bool IsName(string? name) => name != null && (name.StartsWith(ModularRack.Prefix, StringComparison.Ordinal) || name.StartsWith(Ordnance.Prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>The def for such a key (made the first time), or null.</summary>
    public static ConstructPartDef? Def(string key) =>
        key.StartsWith(ModularRack.Prefix, StringComparison.Ordinal) ? ModularRack.Parse(key) : key.StartsWith(Ordnance.Prefix, StringComparison.OrdinalIgnoreCase) ? Ordnance.Parse(key) : null;

    /// <summary>The work order for <paramref name="count"/> of a key, or null when it doesn't read.</summary>
    public static WorkOrder? Order(string key, int count) => !IsName(key) || Def(key) is not { } d ? null
        : !Extrusion.IsRuntimeId((ushort)d.Id) ? new WorkOrder { Part = (ushort)d.Id, Count = (ushort)Math.Clamp(count, 1, WorkOrders.MaxCount) } // (a shape a part file has: that preset, the table's row)
        : new WorkOrder { Name = d.Name, Part = (ushort)d.Id, Key = new ExtrusionKey(0, 0, 0, 0, RecyclingTable.Current.IdOf("steel"), false, ExShape.Section), Count = (ushort)Math.Clamp(count, 1, WorkOrders.MaxCount) };

    /// <summary>The recipe that makes a run-time part, or null (not a rack or a design, or more than one batch takes).</summary>
    public static AssembleRecipe? RecipeOf(ushort id)
    {
        if (!Extrusion.IsRuntimeId(id) || ConstructParts.Get(id) is not { } d || !IsName(d.Name))
            return null;
        lock (Made)
        {
            if (Made.TryGetValue(id, out var have) && ReferenceEquals(have.Def, d))
                return have.Recipe;
            var r = Make(d);
            if (r != null)
                r.Choice = ChoiceBase + ++_next;
            Made[id] = (d, r);
            return r;
        }
    }

    private static AssembleRecipe? Make(ConstructPartDef d)
    {
        var names = RecyclingTable.Current;
        var mats = new List<(byte, int)>();
        var parts = new List<(ConstructPartDef, int)>();
        float seconds;
        if (d.Name.StartsWith(ModularRack.Prefix, StringComparison.Ordinal))
        {
            if (ModularRack.ParseKey(d.Name) is not { } s)
                return null;
            // (as the part kit's Assembling.RackRecipe: steel by its stations and their grade, copper and a board or a unit as it grows, gears for a drum)
            var n = s.Stations;
            var g = MathF.Cbrt(s.Diameter * s.Diameter * s.Length / 0.0625f);
            mats.Add((names.IdOf("steel"), (int)Math.Min(AssemblingTable.MaxUnits, MathF.Round(3 + 3 * n * MathF.Pow(g, 2.3f)))));
            if (g <= 1.6f)
                seconds = 3 + n * g;
            else if (g <= 4)
            {
                mats.Add((names.IdOf("copper"), 2));
                Add(parts, "Circuit Board", 1);
                seconds = 6 + 2 * n * g;
            }
            else
            {
                mats.Add((names.IdOf("copper"), 6));
                Add(parts, "Control Unit", 1);
                seconds = 20 + 5 * n * g;
            }
            if (s.Layout == RackLayout.Drum)
                Add(parts, "Gear Set", g <= 1.6f ? 1 : 2);
        }
        else
        {
            if (OrdnanceDesign.Parse(d.Name, out _) is not { } design || design.Stats().Problem.Length > 0)
                return null;
            // (what the Ordnance Bench takes for it in survival, GameSandboxOrdnance: one of a section for each own length begun, a diameter's cube of each)
            var each = Math.Max(1, (int)MathF.Ceiling(design.Scale * design.Scale * design.Scale));
            seconds = 4;
            foreach (var piece in design.Pieces)
            {
                var k = parts.FindIndex(p => ReferenceEquals(p.Item1, piece.Section));
                var items = piece.Items(each);
                if (k < 0)
                    parts.Add((piece.Section, items));
                else
                    parts[k] = (piece.Section, parts[k].Item2 + items);
                seconds += 2 * items;
            }
        }
        if (mats.Exists(m => m.Item1 == 0) || parts.Exists(p => p.Item1 == null) || mats.Count + parts.Count is 0 or > AssemblingTable.MaxInputs || parts.Exists(p => p.Item2 > AssemblingTable.MaxPartCount))
            return null; // (more inputs, or more of one, than a batch holds: nothing makes it)
        var r = new AssembleRecipe
        {
            Name = d.Name, Output = d, Materials = [.. mats], Parts = [.. parts], Seconds = Math.Clamp(seconds, AssemblingTable.MinSeconds, AssemblingTable.MaxSeconds), TechTier = 2,
            Batch = string.Join(" + ", mats.Select(m => $"{m.Item2} {names.NameOf(m.Item1)}").Concat(parts.Select(p => $"{p.Item2} {p.Item1.Name}"))),
        };
        // (its upgrade modules by its inputs, by the part kit's rule for the table's rows: Assembling.ModuleOf)
        foreach (var (m, _) in mats)
            r.Needs |= NeedOf(names.NameOf(m));
        foreach (var (p, _) in parts)
            r.Needs |= NeedOf(p.Name);
        return r;
    }

    private static byte NeedOf(string input) => input switch
    {
        "copper" or "Copper Coil" or "Wiring Loom" or "Servo Motor" => Bit("Power Coupling"),
        "bronze" or "Bearing" or "Piston Set" or "Turbine Stage" => Bit("Coolant Loop"),
        "gold" or "circuits" or "Circuit Board" or "Control Unit" => Bit("Servo Rack"),
        "levite" or "Field Coil" or "Levite Core" => Bit("Plasma Conditioner"),
        _ => 0,
    };

    private static byte Bit(string module) => (byte)(1 << Array.IndexOf(MachineBlocks.TierPieceNames, module));

    private static void Add(List<(ConstructPartDef, int)> parts, string name, int n) => parts.Add((ConstructParts.Find(name)!, n));
}
