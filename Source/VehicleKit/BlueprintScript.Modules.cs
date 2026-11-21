using System.Numerics;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// Module sockets for generators (docs/sandbox/design/module-sockets.md 8 and 14.2, phase 5): <see cref="Socket"/> lays a Module
/// Socket by the kit's build rules with its settings, and <see cref="Module"/> builds a module design (a blueprint with a
/// <c>"module"</c> block) onto it, step by step, so a generated ship carries its modules as fitted plain parts (the rebuild's plug
/// flood makes them modules, as for any fitted module).
/// </summary>
public partial class BlueprintScript
{
    /// <summary>
    /// A Module Socket W x L on <paramref name="on"/>'s <paramref name="socket"/>, by its own back socket <paramref name="by"/>
    /// (b00.00: the first cell's corner; back.0.0: the first cell's middle), with its kind, style, well depth, room above and keying.
    /// </summary>
    public VPart Socket(VPart on, string socket, int w, int l, ModKind kind = ModKind.Generic, ModStyle style = ModStyle.Proud, float depth = 0, float above = 0,
        bool keyed = false, string by = "b00.00", int? spin = null)
    {
        if (!ModuleSocketTable.ValidSize(w, l) || w > l)
            throw new VehicleKitException($"a module socket is W x L in whole metres, W <= L: not {w} x {l}");
        var p = Onto(on, socket, $"Module Socket {ModuleSocketTable.SizeText(w, l)}", by, spin);
        var set = new List<(string, object)>();
        if (kind != ModKind.Generic)
            set.Add((ConstructConfig.KeyName(CfgKey.ModKind), (int)kind));
        if (style != ModStyle.Proud)
            set.Add((ConstructConfig.KeyName(CfgKey.ModStyle), (int)style));
        if (depth > 0)
            set.Add((ConstructConfig.KeyName(CfgKey.ModDepth), depth));
        if (above > 0)
            set.Add((ConstructConfig.KeyName(CfgKey.ModAbove), above));
        if (keyed)
            set.Add((ConstructConfig.KeyName(CfgKey.ModKeyed), 1));
        if (set.Count > 0)
            Config(p, [.. set]);
        return p;
    }

    /// <summary>
    /// The module design <paramref name="name"/> (a blueprint with a <c>"module"</c> block) built onto plate <paramref name="plate"/>:
    /// its plug on the plate's module socket, turned by <paramref name="spin"/> (a keyed socket takes one turn), then its steps as
    /// they are, its wires and its links. A design with freeform welds, placed steps or uses is refused (not yet). The plug's part.
    /// </summary>
    public VPart Module(VPart plate, string name, int spin = 0)
    {
        var bp = ConstructBuild.FindBlueprint(name) ?? throw new VehicleKitException($"no blueprint named \"{name}\"");
        if (bp.Module is not { } mb)
            throw new VehicleKitException($"the {name} blueprint isn't a module design (it has no \"module\" block)");
        if (plate.Def.ModPart != ModPart.Socket || plate.Def.ModW != mb.W || plate.Def.ModL != mb.L || plate.Def.ModRound != mb.Round)
            throw new VehicleKitException($"the {name} is {ModuleSocketTable.SizeText(mb.W, mb.L, mb.Round)}: it goes on a Module Socket of that size, not a {plate.Def.Name}");
        if (bp.Uses.Count > 0 || bp.StepWeld.Any(x => x != null) || bp.StepAt.Any(x => x != null))
            throw new VehicleKitException($"the {name} has welds, placed steps or uses: a generator can't build it onto a socket yet");
        var made = new VPart[bp.Steps.Count];
        for (var k = 0; k < bp.Steps.Count; k++)
        {
            var s = bp.Steps[k];
            made[k] = k == 0
                ? Onto(plate, "module", s.Part, plate.Def.Sockets[plate.Def.ModSocket].Name == "module" ? "module" : null, spin)
                : Onto(made[s.On], s.Socket, s.Part, s.Own.Length > 0 ? s.Own : null, s.Spin);
        }
        foreach (var (from, to) in bp.Wires)
            Wire(made[from], made[to]);
        foreach (var ls in bp.Links)
            if (ls.Branch < 0 && ls.From >= 0 && ls.To >= 0)
                Link(ConstructRoutes.TypeKey(ls.Type), made[ls.From], made[ls.To], ls.FromPort.Length > 0 ? ls.FromPort : null, ls.ToPort.Length > 0 ? ls.ToPort : null);
        Modules.Add($"module {name}");
        return made[0];
    }
}

/// <summary>
/// <c>--gen-module TAG W L [--name NAME] [--out DIR]</c> (module-sockets.md 15, phase 5): a module design of a tag and size from stock
/// parts on a plug, written as a module blueprint (its <c>"module"</c> block worked out from its parts, as a save does). Tags made:
/// engine (an RCS Thruster on each cell), turret (an AA Gun on each cell), cargo (a Fuel Tank on each cell), power-store (a Battery on
/// each cell), reactor (3x3: a Magpie Power Plant in the middle). Faction-styled designs are later.
/// </summary>
public static class ModuleGen
{
    public static int Cli(string[] args)
    {
        string? Flag(string f) => args.SkipWhile(a => !a.Equals(f, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var rest = args.SkipWhile(a => !a.Equals("--gen-module", StringComparison.OrdinalIgnoreCase)).Skip(1).ToArray();
        if (rest.Length < 3 || !int.TryParse(rest[1], out var w) || !int.TryParse(rest[2], out var l) || !ModuleSocketTable.ValidSize(w, l) || w > l)
        {
            Console.WriteLine("--gen-module TAG W L [--name NAME] [--out DIR]  (tags: engine, turret, cargo, power-store, reactor; W <= L, whole metres up to 3)");
            return 1;
        }
        var tag = rest[0].ToLowerInvariant();
        try
        {
            var text = Make(tag, w, l, Flag("--name"), out var name);
            var dir = Flag("--out") ?? ".";
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, name.Replace(" ", "") + ".json");
            File.WriteAllText(path, text);
            Console.WriteLine($"{name}: written to {path}");
            return 0;
        }
        catch (VehicleKitException ex)
        {
            Console.WriteLine($"REFUSED --gen-module {tag} {w} {l}: {ex.Message}");
            return 1;
        }
    }

    /// <summary>The design's blueprint text, and its name.</summary>
    public static string Make(string tag, int w, int l, string? name, out string named)
    {
        var size = ModuleSocketTable.SizeText(w, l);
        var (part, by, what) = tag switch
        {
            "engine" => ("RCS Thruster", "base", "RCS Thrusters"),
            "turret" => ("AA Gun", "bottom", "AA Guns"),
            "cargo" => ("Fuel Tank", "-y", "Fuel Tanks"),
            "power-store" => ("Battery", "-y", "Batteries"),
            "reactor" => ("Magpie Power Plant", "bottom", "a Magpie Power Plant"),
            _ => throw new VehicleKitException($"no module of tag \"{tag}\" is made yet (engine, turret, cargo, power-store, reactor)"),
        };
        named = name ?? $"Module Gen {char.ToUpperInvariant(tag[0])}{tag[1..]} {size}";
        var s = new BlueprintScript(named) { Description = $"A generated {size} {tag} module: {what} on its plug. Written by --gen-module." };
        var plug = s.Root($"Module Plug {size}", Vector3.Zero);
        if (tag == "reactor")
        {
            if (w < 3)
                throw new VehicleKitException("a reactor needs a 3x3 plug (its power plant is 1.7 m across)");
            s.Onto(plug, "s1.1", part, by, 0);
        }
        else
            for (var i = 0; i < w; i++)
                for (var j = 0; j < l; j++)
                    s.Onto(plug, $"s{i}.{j}", part, by, 0);
        var text = s.Text();
        if (ModuleBlueprints.BlockText(s.Live) is not { } block)
            throw new VehicleKitException("it isn't one module");
        var at = text.IndexOf("  \"steps\"", StringComparison.Ordinal);
        return text[..at] + "  \"module\": " + block + ",\n" + text[at..];
    }
}
