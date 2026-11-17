using System.Globalization;
using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.EngineKit;

/// <summary>
/// Engine generator sets from the Engine Kit (docs/vehiclekit/ENGINEKIT.md "Generator sets"; docs/sandbox/design/engine-generators-notes.md): a kit engine
/// with a Generator Head on its flywheel, on a skid of Foundations (so it anchors where it is set down), with fuel tanks piped to it and batteries to start it and
/// buffer the circuit. The head is the size for the engine's power (<see cref="HeadFor"/>), the engine is Small pieces with nothing pointing down (its crank
/// and flywheel clear the floor at the heads' shaft height). <c>--gen-engine --genset [--preset ...] [--style militia]</c> makes one;
/// <c>--gen-engine --genset-ship [--check]</c> writes the shipped sets (<see cref="Shipped"/>).
/// </summary>
public static class EngineKitGenset
{
    /// <summary>The heads by the most they give, smallest first (the part kit's GeneratorHeadParts: Small 150 kW to Huge 4000 kW).</summary>
    public static readonly (string Part, float Kw)[] Heads = [("Small Generator Head", 150), ("Medium Generator Head", 400), ("Large Generator Head", 1000), ("Huge Generator Head", 4000)];

    /// <summary>The shaft's height over the feet of every head (GeneratorHeadParts.Axis).</summary>
    public const float Axis = 0.5f;

    /// <summary>Minutes of fuel at full load the tanks hold (up to <see cref="MaxTanks"/> Large Fuel Tanks of 300 l).</summary>
    public const float EnduranceMinutes = 30;

    public const int MaxTanks = 6, MaxBatteries = 6;

    /// <summary>The shipped sets: (file under Constructs/Blueprints, preset, tier, style, seed).</summary>
    public static readonly (string File, string Preset, int Tier, string Style, int Seed)[] Shipped =
    [
        ("MilitiaGensetSmall.json", "small", 1, "militia", 1),
        ("MilitiaGensetMedium.json", "medium", 2, "militia", 2),
    ];

    /// <summary>The smallest head that takes the engine's whole power (past the largest, the largest).</summary>
    public static string HeadFor(float engineKw)
    {
        foreach (var (part, kw) in Heads)
            if (kw >= engineKw)
                return part;
        return Heads[^1].Part;
    }

    /// <summary>What a set request asks of the kit: Small pieces, nothing pointing down (the skid is under the crank).</summary>
    public static EngineRequest Request(EngineRequest r) => r with { Size = 0, Underside = false };

    /// <summary>A set's result: the engine's numbers (from the live body) and the blueprint text, or why it wouldn't build.</summary>
    public sealed class Result
    {
        public bool Built;
        public string Text = "", Name = "", Head = "", Note = "";
        public float EngineKw, SetKw, BurnLps, FuelLitres, Mass;
        public int Parts, Tanks, Batteries;
        public EngineResult? Engine;

        public override string ToString() => !Built ? $"no generator set: {Note}" : string.Create(CultureInfo.InvariantCulture,
            $"{Name}: {Engine?.Layout} engine {EngineKw:0} kW on a {Head}, {SetKw:0} kW into the circuit at full load, {BurnLps:0.###} l/s ({BurnLps * 3600 / MathF.Max(1, SetKw):0.00} l a kWh) at full load; {Tanks} tank(s) {FuelLitres:0} l ({FuelLitres / MathF.Max(1e-4f, BurnLps) / 60:0} min flat out), {Batteries} battery(ies), {Parts} parts, {Mass:0} kg{(Note.Length > 0 ? "; " + Note : "")}");
    }

    /// <summary>Builds a set for <paramref name="r"/> (its engine through <see cref="EngineKit"/>) in <paramref name="style"/>'s colours ("" plain).</summary>
    public static Result Make(EngineRequest r, string style = "", string name = "")
    {
        r = Request(r);
        var engine = EngineKit.Request(r);
        if (!engine.Built || engine.Plan is not { } plan)
            return new Result { Note = $"the engine wouldn't build: {engine.Note}" };
        var head = HeadFor(engine.Kw);
        var dice = Dice.Of(r.Seed);
        var palette = style.Length > 0 ? FactoryKit.FactoryStyles.Find(style) : null;
        var paintPower = palette?.Colour(dice, "power");
        var paintFrame = palette?.Colour(dice, "frame");
        var paintMachine = palette?.Colour(dice, "machine");
        var setName = name.Length > 0 ? name : $"{(palette != null ? palette.Name + " " : "")}Genset {engine.Kw:0} kW";
        var burn = engine.BurnLps;
        var tanks = Math.Clamp((int)MathF.Ceiling(burn * EnduranceMinutes * 60 / 300f), 1, MaxTanks);
        var batteries = Math.Clamp((int)MathF.Ceiling(engine.Kw * 5 / 300f), 1, MaxBatteries); // (five seconds of full output, at least the starter's)
        var s = new BlueprintScript(setName) { Group = "Engine Kit", Description = $"An engine generator set made by the Engine Kit for {r}: a {engine.Name} turning a {head}. It runs as the circuit asks." };
        try
        {
            Build(s, r, plan with { }, engine, head, tanks, batteries, r.Paint.Length > 0 ? r.Paint : paintFrame, paintPower, paintMachine);
        }
        catch (VehicleKitException ex)
        {
            return new Result { Note = ex.Message, Engine = engine };
        }
        var live = s.Live;
        var res = new Result { Built = true, Name = setName, Head = head, Engine = engine, Text = s.Text(), Parts = live.PartCount, Tanks = tanks, Batteries = batteries, Note = engine.Note };
        res.EngineKw = live.Engine >= 0 ? ConstructGenset.EngineKw(live.EngineDef) : engine.Kw;
        var hd = ConstructParts.Find(head)!;
        res.SetKw = MathF.Min(hd.PlantRate, res.EngineKw * hd.PlantShaft * ConstructGenset.Headroom);
        res.BurnLps = live.Engine >= 0 ? live.EngineDef.BurnRate : burn;
        res.FuelLitres = tanks * 300f;
        for (var i = 0; i < live.PartCount; i++)
            res.Mass += live.Parts[i].D.Mass;
        if (!live.HasPowertrain || live.ShaftFeedCount == 0)
            return new Result { Note = "the game doesn't see the head on the engine's shaft", Engine = engine };
        return res;
    }

    private static readonly Orient Ahead = new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, Vector3.UnitZ); // own +z to the vehicle's +z (the head's drive, its -z, faces the engine)

    private static void Build(BlueprintScript s, EngineRequest r, EnginePlan p, EngineResult engine, string head, int tanks, int batteries, string? paintEngine, string? paintPower, string? paintMachine)
    {
        var hd = ConstructParts.Find(head) ?? throw new VehicleKitException($"no part named {head}");
        var headLen = hd.BoundsMax.Z - hd.BoundsMin.Z;
        var width = MathF.Max(engine.Dims.Y, hd.BoundsMax.X - hd.BoundsMin.X);
        // The skid: 2 m Foundations (top at 0.3 m), the head's feet at z 0.25 (on the tops' 0.5 m grid), the engine back along -z, tanks and batteries ahead.
        const float floor = 0.3f;
        var headZ = 0.25f;
        var front = headZ + headLen / 2 + 0.25f; // (the head's end cap, and a walkway)
        var tankZ = Grid(front + 0.5f); // (a Large Fuel Tank's centre: it is 1 m long)
        var battZ = tankZ + 1f; // (the tank is 1 m long, a battery 0.2 m deep: on the grid, clear of it)
        var back = headZ - headLen / 2 - engine.Dims.X - 0.5f;
        var rowW = MathF.Max(tanks * 1f, batteries * 0.5f); // (tanks a metre apart, batteries half: whole steps of the socket grid)
        var x0 = MathF.Min(0.25f - width / 2 - 0.5f, -0.25f - rowW / 2);
        var x1 = MathF.Max(0.25f + width / 2 + 0.5f, -0.25f + rowW / 2);
        var z1 = battZ + 0.6f;
        int i0 = (int)MathF.Floor((x0 + 1) / 2), i1 = (int)MathF.Ceiling((x1 - 1) / 2);
        int j0 = (int)MathF.Floor((back + 1) / 2), j1 = (int)MathF.Ceiling((z1 - 1) / 2);
        var root = s.Root("Foundation", new Vector3(0, 0.15f, 0), Ahead);
        var down = new HashSet<(int, int)> { (0, 0) };
        for (var grew = true; grew;)
        {
            grew = false;
            for (var i = i0; i <= i1; i++)
                for (var j = j0; j <= j1; j++)
                    if (!down.Contains((i, j)) && (down.Contains((i - 1, j)) || down.Contains((i + 1, j)) || down.Contains((i, j - 1)) || down.Contains((i, j + 1))))
                    {
                        s.Put("Foundation", new Vector3(2 * i, 0.15f, 2 * j), Ahead);
                        down.Add((i, j));
                        grew = true;
                    }
        }
        // The head on the skid; the flywheel on its drive, turned so its +y is up (the banks' "up"); the engine back from it.
        var gen = s.Stand(head, new Vector3(0.25f, floor, headZ), Ahead, null, "bottom");
        if (paintMachine != null)
            s.Paint(gen, paintMachine);
        var flyName = $"{EngineKit.SizeWords[p.Size]} Flywheel";
        var spin = -1;
        for (var k = 0; k < 4 && spin < 0; k++)
            if (s.TurnOf(gen, "drive", flyName, "+z", k) is { } turn && Vector3.Dot(Rot24.Apply(turn, Vector3.UnitY), Vector3.UnitY) > 0.99f)
                spin = k;
        if (spin < 0)
            throw new VehicleKitException($"a {flyName} won't go on the {head}'s drive upright");
        var fly = s.Onto(gen, "drive", flyName, "+z", spin);
        EngineKit.Assemble(s, r, p, fly);
        if (paintEngine != null)
            for (var k = 0; k < s.Parts.Count; k++)
                if (s.Parts[k].Def.Piece is EnginePiece.Crank or EnginePiece.Output)
                    s.Paint(s.Parts[k], paintEngine);
        // Fuel: a row of Large Fuel Tanks ahead, a metre apart (no two faces flush) (one fuel pool: the first is piped to the flywheel's Fuel in), and the batteries beyond them.
        VPart? first = null;
        for (var k = 0; k < tanks; k++)
        {
            var t = s.Stand("Large Fuel Tank", new Vector3(Grid(-0.25f - tanks / 2) + k, floor, tankZ), Ahead, null, "-y");
            if (paintPower != null)
                s.Paint(t, paintPower);
            first ??= t;
        }
        s.Link("pipe", first!, fly, laid: true);
        for (var k = 0; k < batteries; k++)
            s.Stand("Battery", new Vector3(Grid(-0.25f - 0.5f * (batteries / 2)) + 0.5f * k, floor, battZ), Ahead, null, "-y");
    }

    /// <summary>The nearest place on the Foundations' 0.5 m socket grid (x.25 and x.75).</summary>
    private static float Grid(float v) => MathF.Round((v - 0.25f) * 2) / 2 + 0.25f;

    /// <summary><c>--gen-engine --genset-ship [--check]</c>: writes (or, with --check, compares) the shipped sets. Exit code: files that differ or failed.</summary>
    public static int Ship(bool check)
    {
        var bad = 0;
        foreach (var (file, preset, tier, style, seed) in Shipped)
        {
            var kw = EngineKitCli.Presets.First(p => p.Name == preset).Kw;
            var res = Make(new EngineRequest { PowerKw = kw, Tier = tier, Seed = seed }, style, $"Militia Genset {char.ToUpperInvariant(preset[0])}{preset[1..]}");
            Console.WriteLine(res.ToString());
            if (!res.Built)
            {
                bad++;
                continue;
            }
            var path = Path.Combine("Constructs", "Blueprints", file);
            if (check)
            {
                var same = File.Exists(path) && File.ReadAllText(path).ReplaceLineEndings("\n") == res.Text.ReplaceLineEndings("\n");
                Console.WriteLine($"  {path}: {(same ? "the same" : "DIFFERS")}");
                bad += same ? 0 : 1;
            }
            else
            {
                File.WriteAllText(path, res.Text);
                Console.WriteLine($"  wrote {path}");
            }
        }
        return bad;
    }
}
