using System.Text.Json;

namespace CallOfSlop;

/// <summary>One recipe of the Assembling table: <see cref="Count"/> of a part from materials and other parts, in <see cref="Seconds"/>.</summary>
public sealed class AssembleRecipe
{
    public string Name = ""; // the output part's name: what the configure tool lists and a blueprint names
    public ConstructPartDef Output = null!;
    public int Count = 1;
    public (byte Material, int Units)[] Materials = [];
    public (ConstructPartDef Part, int Count)[] Parts = [];
    public float Seconds = 1;
    public int MinTier = 1;
    public int TechTier = 1; // how advanced the part is (the old Assembler tier, "techTier"): faction vehicles and loot tables read it; the Assembler gates by Needs
    public byte Needs; // the upgrade modules an Assembler needs on its Core to build it: bits by MachineBlocks.TierPieceNames (2 Power Coupling to 5 Plasma Conditioner)
    public string Tool = ""; // the tool block it needs docked in the Core's Tool Bay ("thruster", "tailor"; Assembler PR 3), "" none
    public byte ToolId; // the same as an index of AssemblingTable.Machines (1 thruster, 2 tailor), 0 none
    public byte Machine; // which family of Assembler builds it: an index of AssemblingTable.Machines
    public int Choice; // its value of the recipe setting: 1 + its index in its family's list (AssemblingTable.ByMachine)
    public string Batch = ""; // "4 steel + 2 copper + 1 Gear Set" (made once)
    // (Phase 5b dropped the "pressed" marker rows: no recipe is pressed. These two stay only so ConstructCrafting.MachineMakes and the
    // MachineScreens "Pressed now" card, other sessions' files, still compile; remove them with those branches. press-phase5-notes.md.)
    public bool Pressed => false;
    public string PressedDie() => AssemblingTable.PressedDieOf(Output) ?? "no die";

    /// <summary>The ticks a batch takes at speed 1.</summary>
    public int Ticks => (int)MathF.Ceiling(Seconds * World.TickRate - 0.001f);

    public int UnitsOf(byte material)
    {
        foreach (var (m, u) in Materials)
            if (m == material)
                return u;
        return 0;
    }

    public int CountOf(ushort part)
    {
        foreach (var (p, n) in Parts)
            if (p.Id == part)
                return n;
        return 0;
    }
}

/// <summary>
/// The Assembling table (Constructs/Assembling.json, format call-of-slop-assembling/1; docs/sandbox/CONSTRUCTS.md
/// "Assembling"): what an Assembler can build, one recipe per output part. A file that is refused gives no recipes and
/// <see cref="Problem"/>. A part's recipe choice is filled from the table when the parts load (<see cref="Attach"/>);
/// 0 means none, so a recipe's value is 1 + its index in its machine's own list (<see cref="ByMachine"/>): a recipe with a
/// <c>"machine"</c> is built by that family of Assembler (a part's <c>storage.assemble.machine</c>) and no other.
/// </summary>
public sealed class AssemblingTable
{
    public const string Format = "call-of-slop-assembling/1";
    public const int MaxRecipes = 4000, MaxInputs = 6, MaxUnits = 2000, MaxPartCount = 20, MaxOutput = 12;
    public const float MinSeconds = 0.5f, MaxSeconds = 600f;
    public const string NoRecipe = "(no recipe)";

    /// <summary>The Assembler families: "" the plain Assembler, then the machines that took flight parts over (docs/sandbox/CONSTRUCTS.md "Flight in survival").</summary>
    public static readonly string[] Machines = ["", "thruster", "tailor"];

    /// <summary>
    /// Written into every world file's key names (WorldNames): a file that has it saved its Assemblers' recipe choices against the
    /// table as it is now. A file without it was saved before the refiner block machines (PRs 611, 618, 624, 2026-11-24) took the
    /// Workshop to Plasma Smelter, Foundry and Sawmill rows out of the plain Assembler's list, so its choices after them are moved
    /// back (<see cref="LegacyChoice"/>). A row only ever goes at the end of a family's list (.claude/rules/production.md); if one
    /// is ever taken out again, its place is kept here the same way, under a new mark.
    /// </summary>
    public const string RowsMark = "asm-rows-2026-11-28-retired-phase6";

    /// <summary>Every rows mark there has been, oldest first (<see cref="RowsMark"/> is the last): a file's level is the newest one it names.</summary>
    public static readonly string[] Marks = ["asm-rows-2026-11-24", "asm-rows-2026-11-24-arms", "asm-rows-2026-11-26-servo", "asm-rows-2026-11-28-control-unit", "asm-rows-2026-11-28-press-phase2", "asm-rows-2026-11-28-tools-phase4", "asm-rows-2026-11-28-works-phase5", RowsMark];

    /// <summary>
    /// The rows taken out, by the mark that took them (its place in <see cref="Marks"/>): each group stood right after its After row, and
    /// an old choice of a gone row becomes its To row's (null: After's). Mark 0: the refiner block machines' Workshop to Plasma tiers.
    /// Mark 1 (2026-11-24): the fixed loader arms, now kit presets (an old choice builds an Arm Core; MIGRATING.md).
    /// Mark 2 (2026-11-26): the Servo Motor, now pressed (an Actuator Die round a Copper Coil; an old choice built the Control Unit before it,
    /// then the Bearing once mark 3 took that row, and no recipe since mark 4 took the Bearing).
    /// Mark 3 (2026-11-28, Sean): the Control Unit, folded into 2 Circuit Boards (pressed, so no Assembler row: an old choice built the
    /// Bearing before it, and no recipe since mark 4). Both groups keyed on the Bearing, the row left before them, mark 3's first: in the old list the Servo Motor came
    /// after the Control Unit, and groups after one row insert in this order.
    /// Mark 4 (2026-11-28, Sean; retiring the Assembler, phase 2): 62 rows pressed now (PressParts.Phase2.cs: the Ring, Bell and Shell dies,
    /// the Lens, Field Coil and Levite Core patterns, the frigate's small fittings), the Bearing among them. The Bearing was the plain
    /// Assembler's first row, so its group and those of marks 3 and 2 (once keyed on it) stand at the start of the list: an After of "" is
    /// before the plain family's first row, and their old choices become no recipe (the part is pressed). The start groups go in the old
    /// list's order: Bearing (4), Control Unit (3), Servo Motor (2).
    /// Mark 5 (2026-11-28; retiring the Assembler, phase 4): the Decoupler, the Thruster Jig's one item, pressed on the Button die. It stood
    /// after the Capital Rocket Engine (a kit row now, still in the list); an old choice of it builds the Medium Decoupler, its nearest row.
    /// Mark 6 (2026-11-28; retiring the Assembler, phase 5: the Zero Day Works have no Assemblers): the Server Rack, Robot Shell and Manned
    /// Manoeuvring Unit, pressed round their Circuit Boards (PressParts.Phase2.cs); an old choice of one builds the row before it, as mark 4's.
    /// The Robot Shell's group goes after mark 4's on the Laser Seeker: it stood after the Remote Fuse in that list.
    /// Mark 7 (2026-11-28; retiring the Assembler, phase 6): the Assembler's retired pieces, stand-ins now with no recipe (AssemblerParts.Retire):
    /// the Gantry Carriage, Overdrive Unit and Feed Buffer after the Assembly Bed Block, the Thruster Jig and Sewing Table Blocks after the
    /// Small Solid Booster; an old choice of one builds the row before it, as mark 4's.
    /// </summary>
    private static readonly (int Mark, string After, string[] Gone, string? To)[] Removed =
    [
        (0, "Foundry", ["Workshop Foundry", "Industrial Foundry", "Military Foundry", "Plasma Foundry"], null),
        (0, "Sawmill", ["Workshop Sawmill", "Industrial Sawmill", "Military Sawmill", "Plasma Sawmill"], null),
        (0, "Smelter", ["Workshop Smelter", "Industrial Smelter", "Military Smelter", "Plasma Smelter"], null),
        (1, "Medium Crane Bridge", ["Industrial Loader Arm"], "Arm Core"),
        (1, "Lifter Cab", ["Loader Arm", "Military Loader Arm", "Plasma Loader Arm", "Workshop Loader Arm"], "Arm Core"),
        (1, "Half Shell", ["Module Fitter S"], "Arm Core"),
        (1, "Press Bed Block", ["Heavy Loader Arm", "Shipyard Loader Arm", "Long Loader Arm"], "Arm Core"),
        (4, "", ["Bearing"], null),
        (3, "", ["Control Unit"], null),
        (2, "", ["Servo Motor"], null),
        (4, "Tyre Carcass", ["Lens", "Turbine Stage", "Thrust Chamber", "Field Coil", "Levite Core"], null),
        (4, "Bunk", ["Driving Module", "Turret Module"], null),
        (4, "Proximity Sensor", ["Ammo Box"], null),
        (4, "Shell Hoist", ["High Explosive Shell", "Siege Shell", "Smoke Shell", "Incendiary Shell"], null),
        (4, "Smart Terminal", ["Computer"], null),
        (4, "Ordnance Bench", ["Impact Fuse", "Delay Fuse", "Warhead HE", "Warhead Blast"], null),
        (4, "Warhead Bunker", ["Bomb Tail Fins", "Solid Motor", "Boost Motor", "Liquid Motor", "Hybrid Motor", "Cold Gas Thruster", "Ramjet", "Turbojet", "Missile Fuel Tank", "Stage Separator", "Body Tube", "Tail Fins", "Canards", "Gimbal Mount", "Jet Vanes", "Reaction Jets"], null),
        (4, "Heat Seeker", ["Radar Seeker", "Command Link"], null),
        (4, "Laser Seeker", ["GPS Unit", "Radio Homer", "Beacon Homer", "Proximity Fuse", "Altitude Fuse", "Timer Fuse", "Airburst Fuse", "Remote Fuse"], null),
        (4, "Shell Control Chair", ["Cluster Dispenser", "Fire Bomblets", "Mine Dispenser", "MIRV Bus"], null),
        (4, "Antimatter Warhead", ["Tandem Spacer", "Glide Wings", "Pop-out Wings", "Cruise Wings"], null),
        (4, "Glide Guidance Kit", ["Rocket Assist"], null),
        (4, "Ballute", ["Ring Tail"], null),
        (4, "Cargo Winch 16 m", ["Ammo Container"], null),
        (4, "Bay Marking", ["Bulkhead Light"], null),
        (4, "Medical Station", ["Medicine Cabinet"], null),
        (4, "Rust Bucket Upper Stern", ["Ship Basin"], null),
        (4, "Ship Mess Table", ["Ship Toilet"], null),
        (4, "Parts Shelf", ["Requisition Crate"], null),
        (4, "Deck Speed Dial", ["Fire Director", "Long Range Sensor"], null),
        (5, "Capital Rocket Engine", ["Decoupler"], "Medium Decoupler"),
        (6, "Fax Machine", ["Server Rack"], null),
        (6, "Laser Seeker", ["Robot Shell"], null),
        (6, "MMU Dock", ["Manned Manoeuvring Unit"], null),
        (7, "Assembly Bed Block", ["Gantry Carriage", "Overdrive Unit", "Feed Buffer"], null),
        (7, "Small Solid Booster", ["Thruster Jig Block", "Sewing Table Block"], null),
    ];

    /// <summary>
    /// Rows put into the middle of the plain list, not at its end, by the newest mark a file without them can name (its place in
    /// <see cref="Marks"/>, -1 none): a file at that level or older had no such row, so its later choices are moved on past it.
    /// PR 645 (2026-11-25) put the three Dock Expansions after Grapnel Ladder Section 3 and PR 672 (2026-11-26) the Ship Docking
    /// Station after Worn Flight Couch, with no new mark, so only the files they can be told from are moved: a file at mark 0 or none
    /// had neither, one at mark 1 had no Ship Docking Station. A mark 1 file may or may not have the Dock Expansions and a mark 2 file
    /// the Ship Docking Station (the same mark spans both sides): those are left as they read. Never add to this: a new row goes at the end.
    /// </summary>
    private static readonly (int Level, string Name)[] Inserted =
    [
        (0, "Dock Expansion"), (0, "Medium Dock Expansion"), (0, "Large Dock Expansion"),
        (1, "Ship Docking Station"),
    ];

    private Dictionary<(int, int), int[]>? _legacy; // per (family, file level): an old choice (1 + its place in the old list) to this table's choice

    /// <summary>
    /// An Assembler's recipe choice from a world saved at an older rows mark (<paramref name="level"/>: the newest of <see cref="Marks"/>
    /// it names, -1 none), in this table's numbering: a row that was taken out becomes its To row's, and every row after one moves back.
    /// </summary>
    public float LegacyChoice(int machine, float v, int level = -1)
    {
        if (machine < 0 || machine >= ByMachine.Length || v < 1 || v != MathF.Floor(v) || level >= Marks.Length - 1)
            return v;
        _legacy ??= new();
        if (!_legacy.TryGetValue((machine, level), out var map))
            _legacy[(machine, level)] = map = LegacyMap(ByMachine[machine], level, machine);
        var k = (int)v - 1;
        return k < map.Length ? map[k] : v - (map.Length - ByMachine[machine].Length);
    }

    private static int[] LegacyMap(AssembleRecipe[] now, int level, int machine)
    {
        var map = new List<int>(now.Length + 96);
        foreach (var (mark, after, gone, to) in Removed) // (After "": the plain family's first rows, before every row there is now)
            if (mark > level && machine == 0 && after.Length == 0)
            {
                var target = to == null ? 0 : Array.Find(now, x => x.Name.Equals(to, StringComparison.OrdinalIgnoreCase))?.Choice ?? 0;
                foreach (var _ in gone)
                    map.Add(target);
            }
        foreach (var r in now)
        {
            if (machine != 0 || Array.FindIndex(Inserted, x => x.Level >= level && r.Name.Equals(x.Name, StringComparison.OrdinalIgnoreCase)) < 0)
                map.Add(r.Choice); // (a row the file's list didn't have yet takes no old choice)
            foreach (var (mark, after, gone, to) in Removed)
                if (mark > level && r.Name.Equals(after, StringComparison.OrdinalIgnoreCase))
                {
                    var target = to == null ? r.Choice : Array.Find(now, x => x.Name.Equals(to, StringComparison.OrdinalIgnoreCase))?.Choice ?? 0;
                    foreach (var _ in gone)
                        map.Add(target); // (a Workshop Smelter's Assembler now builds the Smelter Core: Renamed maps the part the same way)
                }
        }
        return [.. map];
    }

    public AssembleRecipe[] Recipes = [];
    public string[] Choices = [NoRecipe];
    public AssembleRecipe[][] ByMachine = [[], [], []]; // per family: its recipes, in the file's order
    public string[][] ChoicesBy = [[NoRecipe], [NoRecipe], [NoRecipe]]; // ...and its recipe choice
    public string Problem = "";
    public Dictionary<ushort, AssembleRecipe> ByOutput = new(); // the recipe that makes a part (one recipe a part): work orders find theirs here

    /// <summary>The recipe that makes a part, or null: a run-time Ordnance Rack's or ordnance design's is made from its key (AssembledParts.cs).</summary>
    public AssembleRecipe? Of(ushort part) => ByOutput.TryGetValue(part, out var r) ? r : Extrusion.IsRuntimeId(part) && Recipes.Length > 0 ? AssembledParts.RecipeOf(part) : null;

    /// <summary>
    /// A part that left the Assembler for a press (Press phase 5: a die presses it from a standard blank, a preset of Pressing.json, and no
    /// Assembler builds it): "the Gear Set is pressed now: a Gear Die and a steel round 15x15x15", or null. For the picker, a blueprint
    /// that names it as a recipe and an order's refusal; made on demand (menus and loads, never per tick).
    /// </summary>
    public static string? PressedLine(ConstructPartDef d) => PressedSpec(d) is { } s
        ? $"the {d.Name} is pressed now: a {s.Die.Family} Die and a {Extrusion.Label(s.Blank)}{(s.Pattern.Bay2 is { } kit ? $", with {kit.What} in bay 2" : "")}"
        : null;

    /// <summary>The die that presses such a part ("Gear Die"), or null.</summary>
    public static string? PressedDieOf(ConstructPartDef d) => PressedSpec(d) is { } s ? $"{s.Die.Family} Die" : null;

    private static Pressed.OrderSpec? PressedSpec(ConstructPartDef d) => // (row or none: a row a preset keeps is its bill, phase 6)
        PressTable.Current.PresetKeyOf(d) is { } key ? Pressed.SpecOf(key) : null;

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "Assembling.json");

    private static AssemblingTable? _current;

    /// <summary>The table in use. Loading the parts loads it (<see cref="Attach"/>).</summary>
    public static AssemblingTable Current
    {
        get
        {
            if (_current == null)
                _ = ConstructParts.All;
            return _current ?? new AssemblingTable { Problem = "the parts haven't loaded" };
        }
    }

    /// <summary>Sets the table in use (the self-test; null: read the file again, against the parts as loaded).</summary>
    public static void Use(AssemblingTable? t)
    {
        _current = t ?? Load(FilePath, ConstructParts.All);
        Fill(ConstructParts.All, _current);
    }

    /// <summary>Called as the parts load: reads the table against them and fills every Assembler's recipe choice.</summary>
    public static ConstructPartDef[] Attach(ConstructPartDef[] parts)
    {
        _current = Load(FilePath, parts);
        Fill(parts, _current);
        return parts;
    }

    private static void Fill(IReadOnlyList<ConstructPartDef> parts, AssemblingTable t)
    {
        foreach (var d in parts)
            if (d.CfgSlot[(int)CfgKey.Recipe] >= 0 && !d.IsPanelFormer) // (a Panel Former's recipe setting is its preset list, from its file)
                d.Config[d.CfgSlot[(int)CfgKey.Recipe]].Options = t.ChoicesBy[d.AsmMachine];
    }

    public static AssemblingTable Load(string path, IReadOnlyList<ConstructPartDef> parts)
    {
        try
        {
            return Parse(File.ReadAllText(path), parts);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new AssemblingTable { Problem = $"{Path.GetFileName(path)}: {ex.Message}" };
        }
    }

    /// <summary>Reads the table against <paramref name="parts"/>; a bad file gives no recipes and the reason.</summary>
    public static AssemblingTable Parse(string json, IReadOnlyList<ConstructPartDef>? parts = null, RecyclingTable? names = null)
    {
        parts ??= ConstructParts.All;
        names ??= RecyclingTable.Current;
        try
        {
            var byName = new Dictionary<string, ConstructPartDef>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in parts)
                byName[d.Name] = d;
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.ValueKind != JsonValueKind.String || f.GetString() != Format)
                throw new FormatException($"\"format\" must be \"{Format}\"");
            foreach (var p in root.EnumerateObject())
                if (p.Name is not ("format" or "about" or "recipes"))
                    throw new FormatException($"unknown key \"{p.Name}\"");
            if (!root.TryGetProperty("recipes", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is 0 or > MaxRecipes)
                throw new FormatException($"\"recipes\" lists 1 to {MaxRecipes} recipes");
            var recipes = new List<AssembleRecipe>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in list.EnumerateArray())
            {
                var r = Recipe(e, $"recipe {recipes.Count + 1}", byName, names);
                if (!seen.Add(r.Name))
                    throw new FormatException($"recipe {recipes.Count + 1}: {r.Name} already has a recipe: one recipe a part");
                recipes.Add(r);
            }
            var t = new AssemblingTable { Recipes = [.. recipes] };
            for (var m = 0; m < Machines.Length; m++)
            {
                t.ByMachine[m] = recipes.Where(r => r.Machine == m).ToArray();
                for (var k = 0; k < t.ByMachine[m].Length; k++)
                    t.ByMachine[m][k].Choice = k + 1;
                t.ChoicesBy[m] = [NoRecipe, .. t.ByMachine[m].Select(r => r.Name)];
            }
            t.Choices = t.ChoicesBy[0];
            foreach (var r in t.Recipes)
                t.ByOutput[(ushort)r.Output.Id] = r;
            return t;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return new AssemblingTable { Problem = ex.Message };
        }
    }

    private static AssembleRecipe Recipe(JsonElement e, string at, Dictionary<string, ConstructPartDef> byName, RecyclingTable names)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new FormatException($"{at} is an object (output, materials, parts, seconds)");
        foreach (var p in e.EnumerateObject())
            if (p.Name is not ("output" or "count" or "materials" or "parts" or "seconds" or "minTier" or "machine" or "needs" or "tool" or "techTier"))
                throw new FormatException($"{at}: unknown key \"{p.Name}\"");
        var output = e.TryGetProperty("output", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString()! : throw new FormatException($"{at} needs \"output\" (a part's name)");
        if (!byName.TryGetValue(output, out var def))
            throw new FormatException($"{at}: no part named \"{output}\"");
        at = $"{at} ({def.Name})";
        var r = new AssembleRecipe { Name = def.Name, Output = def };
        if (e.TryGetProperty("machine", out var mc))
            r.Machine = MachineOf(mc.GetString() ?? "", at);
        if (e.TryGetProperty("count", out var c))
            r.Count = Whole(c, "count", 1, MaxOutput, at);
        if (r.Count > 1 && def.PhysicsOnly)
            throw new FormatException($"{at}: a physics-only part leaves as one body: \"count\" is 1");
        var mats = new List<(byte, int)>();
        var bits = new List<string>();
        if (e.TryGetProperty("materials", out var ml))
        {
            if (ml.ValueKind != JsonValueKind.Object)
                throw new FormatException($"{at}: \"materials\" is an object of material: units");
            foreach (var p in ml.EnumerateObject())
            {
                var id = names.IdOf(p.Name);
                if (id == 0)
                    throw new FormatException($"{at}: \"{p.Name}\" isn't a bulk material (Recycling.json)");
                mats.Add((id, Whole(p.Value, p.Name, 1, MaxUnits, at)));
                bits.Add($"{mats[^1].Item2} {p.Name}");
            }
        }
        var parts = new List<(ConstructPartDef, int)>();
        if (e.TryGetProperty("parts", out var pl))
        {
            if (pl.ValueKind != JsonValueKind.Object)
                throw new FormatException($"{at}: \"parts\" is an object of part name: count");
            foreach (var p in pl.EnumerateObject())
            {
                if (!byName.TryGetValue(p.Name, out var d))
                    throw new FormatException($"{at}: no part named \"{p.Name}\"");
                if (d.PhysicsOnly)
                    throw new FormatException($"{at}: the {d.Name} is physics-only ({d.PhysicsWhy}): no chute or hands can bring it, so it can't be an input");
                if (d.Id == def.Id)
                    throw new FormatException($"{at}: it can't be built from itself");
                parts.Add((d, Whole(p.Value, p.Name, 1, MaxPartCount, at)));
                bits.Add($"{parts[^1].Item2} {d.Name}");
            }
        }
        if (mats.Count + parts.Count == 0)
            throw new FormatException($"{at}: a recipe takes something (\"materials\", \"parts\")");
        if (mats.Count + parts.Count > MaxInputs)
            throw new FormatException($"{at}: at most {MaxInputs} inputs");
        r.Materials = mats.ToArray();
        r.Parts = parts.ToArray();
        r.Batch = string.Join(" + ", bits);
        r.Seconds = e.TryGetProperty("seconds", out var s) && s.ValueKind == JsonValueKind.Number && s.GetSingle() is >= MinSeconds and <= MaxSeconds ? s.GetSingle()
            : throw new FormatException($"{at}: \"seconds\" is a number from {MinSeconds} to {MaxSeconds}");
        if (e.TryGetProperty("minTier", out var t))
            r.MinTier = Whole(t, "minTier", 1, ConstructParts.MaxTier, at);
        if (e.TryGetProperty("techTier", out var tt))
            r.TechTier = Whole(tt, "techTier", 1, ConstructParts.MaxTier, at);
        if (e.TryGetProperty("needs", out var nd))
        {
            // The Assembler's upgrade modules it takes (docs/sandbox/constructs/production.md "Assembling"): "needs": ["Power Coupling", "Servo Rack"].
            if (nd.ValueKind != JsonValueKind.Array)
                throw new FormatException($"{at}: \"needs\" is a list of upgrade modules ({string.Join(", ", MachineBlocks.TierPieceNames.Skip(2))})");
            foreach (var n in nd.EnumerateArray())
            {
                var k = n.ValueKind == JsonValueKind.String ? Array.IndexOf(MachineBlocks.TierPieceNames, n.GetString()) : -1;
                if (k < 2)
                    throw new FormatException($"{at}: \"needs\" names upgrade modules ({string.Join(", ", MachineBlocks.TierPieceNames.Skip(2))}), not {n}");
                r.Needs |= (byte)(1 << k);
            }
        }
        if (e.TryGetProperty("tool", out var tl))
        {
            r.Tool = tl.ValueKind == JsonValueKind.String && tl.GetString() is "thruster" or "tailor" ? tl.GetString()! : throw new FormatException($"{at}: \"tool\" is thruster or tailor");
            r.ToolId = (byte)Array.IndexOf(Machines, r.Tool);
        }
        return r;
    }

    /// <summary>The family a <c>"machine"</c> names.</summary>
    internal static byte MachineOf(string name, string at)
    {
        var k = Array.IndexOf(Machines, name);
        return k >= 0 ? (byte)k : throw new FormatException($"{at}: \"machine\" is one of {string.Join(", ", Machines.Skip(1))}, not \"{name}\"");
    }

    private static int Whole(JsonElement e, string key, int min, int max, string at) =>
        e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n) && n >= min && n <= max ? n : throw new FormatException($"{at}: \"{key}\" is a whole number from {min} to {max}");
}
