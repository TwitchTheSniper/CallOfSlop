using System.Text.Json;

namespace CallOfSlop;

/// <summary>What a shell module does: the kinds the code knows (Constructs/Shells.json "modules"; a new kind needs its rule).</summary>
public enum ShellModuleKind : byte
{
    Head, // the visor and optics (no rule of its own)
    Pack, // the Robot Shell's power pack on the back (no rule of its own)
    Hopper, // loose ore on the back: the shell part's own bin (its storage.bulk) is the hopper (GameShellHopper.cs)
    Hand, // an arm that holds the tools, as a person's
    Drill, // an arm that is a drill, always out: the ground drill's scoop taps ore into the hopper (GameShellHopper.cs)
}

/// <summary>One module from Constructs/Shells.json: a name, the slot kind it fits ("head", "back", "arm") and its kind's numbers.</summary>
public sealed class ShellModule
{
    public string Name = "", Slot = "";
    public ShellModuleKind Kind;
    public int Units; // a drill: ore tapped a scoop
    public int Tier = 1; // a drill: the Drill Module tier it works as (a deposit's minTier)
    public string[] Takes = []; // a hopper: the materials it holds (Recycling.json names)
}

/// <summary>One shell frame: a body (its part), its size and pace, and the module in each slot (<see cref="ShellFrames.Slots"/> order).</summary>
public sealed class ShellFrame
{
    public string Name = "", Part = "";
    public float Scale = 1, Speed = 1, Damage = 1;
    public string[] Jobs = [];
    public ShellModule?[] Modules = new ShellModule?[ShellFrames.SlotCount];
    public bool[] Fixed = new bool[ShellFrames.SlotCount]; // a slot customisation may not change (none can be changed yet)

    /// <summary>The first module of a kind in its slots, or null.</summary>
    public ShellModule? Find(ShellModuleKind kind)
    {
        foreach (var m in Modules)
            if (m?.Kind == kind)
                return m;
        return null;
    }
}

/// <summary>
/// Robot shells as frames and modules (docs/sandbox/design/mining-shell.md "Modular shells"; the user, 2026-11-17: "prepare to
/// make things modular"). <c>Constructs/Shells.json</c> (format <c>call-of-slop-shells/1</c>) lists the slots, the modules that
/// go in them and the frames. A frame's index is its byte on the wire and in <see cref="MoveState.Frame"/>; frame 0 is the
/// Robot Shell, whose numbers are the ones every shell had before. A bad file is refused with a reason, and then every shell is
/// frame 0. The code asks a frame for a module's kind (<see cref="ShellFrame.Find"/>), never for a frame's name.
/// </summary>
public static class ShellFrames
{
    public const string Format = "call-of-slop-shells/1";
    public const int SlotCount = 4;

    /// <summary>The slots in their fixed order (a wire and save order: a new slot goes at the end).</summary>
    public static readonly string[] Slots = ["head", "back", "leftArm", "rightArm"];

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "Shells.json");

    private static ShellFrame[]? _frames;
    private static readonly float[] Scales = MakeScales();
    public static string Problem = "";

    private static float[] MakeScales()
    {
        var a = new float[256];
        Array.Fill(a, 1f);
        return a;
    }

    /// <summary>The frames in wire order (loaded once).</summary>
    public static ShellFrame[] All => _frames ??= Load(FilePath);

    /// <summary>A frame by its byte (anything unknown: frame 0).</summary>
    public static ShellFrame Of(byte frame)
    {
        var a = All;
        return frame < a.Length ? a[frame] : a[0];
    }

    /// <summary>A frame's size (1: a person). No allocation: read every tick by <see cref="World.Body(in MoveState)"/>.</summary>
    public static float ScaleOf(byte frame)
    {
        if (frame == 0)
            return 1f;
        _ = All;
        return Scales[frame];
    }

    /// <summary>A player's size now: a shell's frame while they wear one, else 1.</summary>
    public static float ScaleOf(Player p) => p.InShell ? ScaleOf(p.ShellFrame) : 1f;

    /// <summary>The frame a shell part stands as (by the frame's <c>"part"</c>; a shell part no frame names: 0).</summary>
    public static byte FrameOf(ConstructPartDef d)
    {
        var a = All;
        for (var i = 0; i < a.Length; i++)
            if (a[i].Part == d.Name)
                return (byte)i;
        return 0;
    }

    /// <summary>Frame 0 with the Robot Shell's numbers: what every shell is when the file is refused.</summary>
    private static ShellFrame Standard()
    {
        var f = new ShellFrame { Name = "Standard", Part = "Robot Shell" };
        f.Modules[2] = f.Modules[3] = new ShellModule { Name = "Hand", Slot = "arm", Kind = ShellModuleKind.Hand };
        return f;
    }

    public static ShellFrame[] Load(string path)
    {
        try
        {
            var frames = Parse(File.ReadAllText(path));
            Array.Fill(Scales, 1f);
            for (var i = 0; i < frames.Length; i++)
                Scales[i] = frames[i].Scale;
            Problem = "";
            return frames;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            Problem = $"{Path.GetFileName(path)}: {ex.Message}";
            Console.WriteLine($"[shells] REFUSED {Problem}: every robot shell is the standard frame");
            Array.Fill(Scales, 1f);
            return [Standard()];
        }
    }

    /// <summary>Reads the file's text (throws FormatException with the reason).</summary>
    public static ShellFrame[] Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var fm) || fm.GetString() != Format)
            throw new FormatException($"\"format\" must be \"{Format}\"");
        var slots = root.GetProperty("slots");
        if (slots.GetArrayLength() != SlotCount)
            throw new FormatException($"\"slots\" must list {SlotCount}: {string.Join(", ", Slots)}");
        for (var i = 0; i < SlotCount; i++)
            if (slots[i].GetString() != Slots[i])
                throw new FormatException($"\"slots\"[{i}] must be \"{Slots[i]}\" (the order is the wire's)");
        var modules = new Dictionary<string, ShellModule>();
        foreach (var m in root.GetProperty("modules").EnumerateArray())
        {
            var mod = new ShellModule
            {
                Name = m.GetProperty("name").GetString() ?? "",
                Slot = m.GetProperty("slot").GetString() ?? "",
                Kind = Enum.TryParse<ShellModuleKind>(m.GetProperty("kind").GetString(), true, out var k) ? k : throw new FormatException($"module \"{m.GetProperty("name").GetString()}\": unknown kind \"{m.GetProperty("kind").GetString()}\""),
            };
            if (mod.Slot is not ("head" or "back" or "arm"))
                throw new FormatException($"module \"{mod.Name}\": \"slot\" is \"head\", \"back\" or \"arm\"");
            if (mod.Kind == ShellModuleKind.Drill)
            {
                mod.Units = m.GetProperty("units").GetInt32();
                mod.Tier = m.GetProperty("tier").GetInt32();
                if (mod.Units is < 1 or > 1000 || mod.Tier is < 1 or > ConstructParts.MaxTier)
                    throw new FormatException($"module \"{mod.Name}\": \"units\" 1 to 1000, \"tier\" 1 to {ConstructParts.MaxTier}");
            }
            if (mod.Kind == ShellModuleKind.Hopper)
            {
                mod.Takes = [.. m.GetProperty("takes").EnumerateArray().Select(e => e.GetString() ?? "")];
                foreach (var t in mod.Takes)
                    if (RecyclingTable.Current.IdOf(t) == 0)
                        throw new FormatException($"module \"{mod.Name}\": \"takes\": no material named \"{t}\" in Recycling.json");
            }
            if (!modules.TryAdd(mod.Name, mod))
                throw new FormatException($"module \"{mod.Name}\" twice");
        }
        var list = new List<ShellFrame>();
        foreach (var f in root.GetProperty("frames").EnumerateArray())
        {
            var fr = new ShellFrame
            {
                Name = f.GetProperty("name").GetString() ?? "",
                Part = f.GetProperty("part").GetString() ?? "",
                Scale = Num(f, "scale", 0.5f, 2.5f),
                Speed = Num(f, "speed", 0.2f, 2f),
                Damage = Num(f, "damage", 0.1f, 5f),
            };
            if (f.TryGetProperty("jobs", out var jobs))
                fr.Jobs = [.. jobs.EnumerateArray().Select(e => e.GetString() ?? "")];
            var sl = f.GetProperty("slots");
            for (var i = 0; i < SlotCount; i++)
            {
                if (!sl.TryGetProperty(Slots[i], out var mn))
                    continue;
                var name = mn.GetString() ?? "";
                if (!modules.TryGetValue(name, out var mod))
                    throw new FormatException($"frame \"{fr.Name}\": no module named \"{name}\"");
                var kind = i >= 2 ? "arm" : Slots[i];
                if (mod.Slot != kind)
                    throw new FormatException($"frame \"{fr.Name}\": \"{name}\" goes in a {mod.Slot} slot, not {Slots[i]}");
                fr.Modules[i] = mod;
            }
            if (f.TryGetProperty("fixed", out var fx))
                foreach (var e in fx.EnumerateArray())
                {
                    var i = Array.IndexOf(Slots, e.GetString());
                    if (i < 0)
                        throw new FormatException($"frame \"{fr.Name}\": \"fixed\": no slot \"{e.GetString()}\"");
                    fr.Fixed[i] = true;
                }
            if (fr.Find(ShellModuleKind.Drill) != null && fr.Find(ShellModuleKind.Hopper) == null)
                throw new FormatException($"frame \"{fr.Name}\": a drill arm needs a hopper on the back to put its ore in");
            if (list.Any(o => o.Name == fr.Name || o.Part == fr.Part))
                throw new FormatException($"frame \"{fr.Name}\": a name or part used twice");
            list.Add(fr);
        }
        if (list.Count == 0 || list.Count > 255)
            throw new FormatException("\"frames\": 1 to 255");
        if (list[0].Scale != 1 || list[0].Speed != 1 || list[0].Damage != 1 || list[0].Part != "Robot Shell")
            throw new FormatException("frame 0 is the Robot Shell, at scale, speed and damage 1 (every older shell is it)");
        return [.. list];
    }

    private static float Num(JsonElement e, string key, float min, float max)
    {
        var v = e.GetProperty(key).GetSingle();
        if (!float.IsFinite(v) || v < min || v > max)
            throw new FormatException($"\"{key}\" is {v}, not between {min} and {max}");
        return v;
    }
}
