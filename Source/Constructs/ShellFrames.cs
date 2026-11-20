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
    Leg, // a leg (its "speed" is the shell's pace; the Shell Gantry's lower arms fit them)
    Tool, // an arm that is a tool (its "tool"): held forward along the look like the drill arm, drawn from its part's model (ShellModuleNodes.cs)
}

/// <summary>One module from Constructs/Shells.json: a name, the slot kind it fits ("head", "back", "arm", "leg") and its kind's numbers.</summary>
public sealed class ShellModule
{
    public string Name = "", Slot = "";
    public ShellModuleKind Kind;
    public float Rate; // a drill: ore tapped a second while it is drilling a deposit
    public int Tier = 1; // a drill: the Drill Module tier it works as (a deposit's minTier)
    public string[] Takes = []; // a hopper: the materials it holds (Recycling.json names)
    public string Part = ""; // "part": the construct part this module is off the shell (on a Shell Gantry's parts table; mining-shell.md "Shell Gantry"); "": it is never taken off
    public float Speed = 1, Damage = 1; // "speed", "damage": times the frame's pace and the hits it takes while fitted (1: no change)
    public byte Index; // its place in the file's "modules" (a wire and save byte: a new module goes at the end)
    public ConstructTool? Tool; // "tool": while the shell is worn this is its only tool (the Drill Arm's ground drill; the user, 2026-11-17)
    public bool View; // "view": in first person its wearer sees the module's own model (the one on the shell: ShellKitNodes) in place of its tool's (the user, 2026-11-19)
    public float Hip, Knee, Hock, Ankle; // a leg's "joints": heights on its part's model (the part's metres; Hock 0: none), where the rig's bones take its boxes (ShellModuleNodes.cs)
    public float HockUp, HockBack; // a leg's "hock": where its hock stands above and behind the ankle (a person's metres): the rig walks it digitigrade (LegAnimator)
}

/// <summary>
/// How a shell frame folds into a shell pod (Shells.json's "fold"; docs/sandbox/design/shell-pod.md): it kneels (thighs forward by
/// <see cref="Hip"/> degrees, shins back under them by <see cref="Knee"/>), bends its body forward (<see cref="Bend"/>, negative),
/// draws its arms in (each shoulder and elbow, degrees forward), sinks its head into its shoulders (<see cref="Head"/>) and comes down
/// by <see cref="Drop"/> and back by <see cref="Shift"/>; lengths are in a person's metres, times the frame's scale. Folded it is about
/// <see cref="Height"/> tall (the same units): a ceiling pod hangs it by its top.
/// </summary>
public sealed class ShellFold
{
    public float Hip = 90, Knee = -160, Bend = -35, LeftShoulder = 50, RightShoulder = 50, LeftElbow = 60, RightElbow = 60;
    public float Head = 0.2f, Drop = 0.72f, Shift = 0.15f, Height = 0.95f;
}

/// <summary>One shell frame: a body (its part), its size and pace, and the module in each slot (<see cref="ShellFrames.Slots"/> order).</summary>
public sealed class ShellFrame
{
    public string Name = "", Part = "";
    public float Scale = 1, Speed = 1, Damage = 1;
    public float Lift = 1; // "lift": the share of the survival physics gun's limit its wearer lifts (the Half Shell's 0.5; docs/sandbox/design/half-shell.md)
    public bool Sit = true; // "sit": false, seats refuse its wearer (the Half Shell: it would sit below a seat's controls)
    public string[] Jobs = [];
    public ShellModule?[] Modules = new ShellModule?[ShellFrames.SlotCount];
    public bool[] Fixed = new bool[ShellFrames.SlotCount]; // a slot the Shell Gantry may not change (the Mining Shell's hopper and drill arm)
    public ShellFold Fold = new(); // "fold": how it folds into a shell pod (docs/sandbox/design/shell-pod.md; ShellPodRender.cs)

    /// <summary>The tool a module forces while the shell is worn (a drill arm's first: the Mining Shell keeps its drill whatever its other arm is; else the first slot's that names one), or null: any tool.</summary>
    public ConstructTool? ForcedTool => ToolModule?.Tool;

    /// <summary>Whether a shell fitted so may hold <paramref name="tool"/>: any tool when no module names one, else only the tools its modules are.</summary>
    public bool Allows(ConstructTool tool)
    {
        var any = false;
        foreach (var m in Modules)
            if (m?.Tool is { } t)
            {
                if (t == tool)
                    return true;
                any = true;
            }
        return !any;
    }

    /// <summary>The module that is the tool <paramref name="tool"/> (a tool arm, the drill arm), or null.</summary>
    public ShellModule? ModuleFor(ConstructTool tool)
    {
        foreach (var m in Modules)
            if (m?.Tool == tool)
                return m;
        return null;
    }

    /// <summary>The module that forces <see cref="ForcedTool"/> (for its name in a refusal), or null.</summary>
    public ShellModule? ToolModule
    {
        get
        {
            foreach (var m in Modules)
                if (m?.Tool != null && m.Kind == ShellModuleKind.Drill)
                    return m;
            foreach (var m in Modules)
                if (m?.Tool != null)
                    return m;
            return null;
        }
    }

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
    public const int SlotCount = 6;

    /// <summary>The slots in their fixed order (a wire and save order: a new slot goes at the end; the legs came with the Shell Gantry).</summary>
    public static readonly string[] Slots = ["head", "back", "leftArm", "rightArm", "leftLeg", "rightLeg"];

    /// <summary>The kind of module slot <paramref name="i"/> takes: "head", "back", "arm" or "leg".</summary>
    public static string SlotKind(int i) => i switch { 0 => "head", 1 => "back", 2 or 3 => "arm", _ => "leg" };

    /// <summary>A slot's name for a player ("left arm").</summary>
    public static string SlotLabel(int i) => i switch { 0 => "head", 1 => "back", 2 => "left arm", 3 => "right arm", 4 => "left leg", _ => "right leg" };

    private static ShellModule[] _modules = [];

    /// <summary>Every module in file order (its <see cref="ShellModule.Index"/>).</summary>
    public static ShellModule[] Modules
    {
        get
        {
            _ = All;
            return _modules;
        }
    }

    /// <summary>The module a part stands for off the shell (its <c>"part"</c>), or null.</summary>
    public static ShellModule? ModuleOfPart(string partName)
    {
        foreach (var m in Modules)
            if (m.Part.Length > 0 && m.Part == partName)
                return m;
        return null;
    }

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
        _modules = [f.Modules[2]!];
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
        var order = new List<ShellModule>();
        foreach (var m in root.GetProperty("modules").EnumerateArray())
        {
            var mod = new ShellModule
            {
                Name = m.GetProperty("name").GetString() ?? "",
                Slot = m.GetProperty("slot").GetString() ?? "",
                Kind = Enum.TryParse<ShellModuleKind>(m.GetProperty("kind").GetString(), true, out var k) ? k : throw new FormatException($"module \"{m.GetProperty("name").GetString()}\": unknown kind \"{m.GetProperty("kind").GetString()}\""),
                Index = order.Count < 254 ? (byte)order.Count : throw new FormatException("\"modules\": at most 254"),
            };
            if (mod.Slot is not ("head" or "back" or "arm" or "leg"))
                throw new FormatException($"module \"{mod.Name}\": \"slot\" is \"head\", \"back\", \"arm\" or \"leg\"");
            if (m.TryGetProperty("speed", out _))
                mod.Speed = Num(m, "speed", 0.5f, 1.5f);
            if (m.TryGetProperty("damage", out _))
                mod.Damage = Num(m, "damage", 0.5f, 1.5f);
            if (mod.Kind == ShellModuleKind.Drill)
            {
                mod.Rate = Num(m, "rate", 0.1f, 1000);
                mod.Tier = m.GetProperty("tier").GetInt32();
                if (mod.Tier is < 1 or > ConstructParts.MaxTier)
                    throw new FormatException($"module \"{mod.Name}\": \"tier\" 1 to {ConstructParts.MaxTier}");
            }
            if (mod.Kind == ShellModuleKind.Hopper)
            {
                mod.Takes = [.. m.GetProperty("takes").EnumerateArray().Select(e => e.GetString() ?? "")];
                foreach (var t in mod.Takes)
                    if (RecyclingTable.Current.IdOf(t) == 0)
                        throw new FormatException($"module \"{mod.Name}\": \"takes\": no material named \"{t}\" in Recycling.json");
            }
            if (m.TryGetProperty("part", out var mp))
                mod.Part = mp.GetString() ?? "";
            if (m.TryGetProperty("tool", out var tl))
                mod.Tool = Enum.TryParse<ConstructTool>(tl.GetString(), true, out var tool) && ConstructToolWire.From((byte)tool) == tool && tool != ConstructTool.None
                    ? tool : throw new FormatException($"module \"{mod.Name}\": no tool \"{tl.GetString()}\"");
            if (m.TryGetProperty("view", out var vw))
            {
                mod.View = vw.GetBoolean();
                if (mod.View && (mod.Tool == null || mod.Kind is not (ShellModuleKind.Drill or ShellModuleKind.Tool)))
                    throw new FormatException($"module \"{mod.Name}\": \"view\" needs a \"tool\" and a kind with a model of its own (drill, tool)");
            }
            if (mod.Kind == ShellModuleKind.Tool && (mod.Tool == null || mod.Slot != "arm" || mod.Part.Length == 0))
                throw new FormatException($"module \"{mod.Name}\": a tool module is an arm with a \"tool\" and a \"part\" (its model)");
            if (mod.Kind == ShellModuleKind.Leg && mod.Part.Length > 0)
            {
                if (!m.TryGetProperty("joints", out var jt))
                    throw new FormatException($"module \"{mod.Name}\": a leg with a part says its \"joints\" (hip, knee, ankle heights on the part)");
                mod.Hip = Num(jt, "hip", 0.2f, 3f);
                mod.Knee = Num(jt, "knee", 0.1f, 3f);
                mod.Ankle = Num(jt, "ankle", 0f, 1f);
                if (jt.TryGetProperty("hock", out _))
                    mod.Hock = Num(jt, "hock", 0.05f, 2f);
                if (!(mod.Hip > mod.Knee && mod.Knee > MathF.Max(mod.Hock, mod.Ankle) && (mod.Hock == 0 || mod.Hock > mod.Ankle)))
                    throw new FormatException($"module \"{mod.Name}\": \"joints\" go down: hip, knee, hock, ankle");
                if (m.TryGetProperty("hock", out var hk))
                {
                    if (mod.Hock == 0 || hk.GetArrayLength() != 2)
                        throw new FormatException($"module \"{mod.Name}\": \"hock\" is [up, back] and needs a hock in its \"joints\"");
                    (mod.HockUp, mod.HockBack) = (hk[0].GetSingle(), hk[1].GetSingle());
                    if (mod.HockUp is < 0.05f or > 0.6f || mod.HockBack is < -0.2f or > 0.3f)
                        throw new FormatException($"module \"{mod.Name}\": \"hock\" up 0.05 to 0.6, back -0.2 to 0.3");
                }
                else if (mod.Hock > 0)
                    throw new FormatException($"module \"{mod.Name}\": a hock in its \"joints\" needs \"hock\": [up, back]");
            }
            if (!modules.TryAdd(mod.Name, mod))
                throw new FormatException($"module \"{mod.Name}\" twice");
            if (mod.Part.Length > 0 && order.Exists(o => o.Part == mod.Part))
                throw new FormatException($"module \"{mod.Name}\": part \"{mod.Part}\" is another module's");
            order.Add(mod);
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
            if (f.TryGetProperty("lift", out _))
                fr.Lift = Num(f, "lift", 0.1f, 2f);
            if (f.TryGetProperty("sit", out var sit))
                fr.Sit = sit.GetBoolean();
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
                var kind = SlotKind(i);
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
            if (f.TryGetProperty("fold", out var fo))
            {
                var fd = fr.Fold;
                foreach (var pr in fo.EnumerateObject())
                {
                    var v = pr.Value.ValueKind == JsonValueKind.Number ? pr.Value.GetSingle() : throw new FormatException($"frame \"{fr.Name}\": fold.{pr.Name} is a number");
                    var angle = MathF.Abs(v) <= 180;
                    var length = v is >= -0.5f and <= 1.5f;
                    switch (pr.Name)
                    {
                        case "hip" when angle: fd.Hip = v; break;
                        case "knee" when angle: fd.Knee = v; break;
                        case "bend" when angle: fd.Bend = v; break;
                        case "leftShoulder" when angle: fd.LeftShoulder = v; break;
                        case "rightShoulder" when angle: fd.RightShoulder = v; break;
                        case "leftElbow" when angle: fd.LeftElbow = v; break;
                        case "rightElbow" when angle: fd.RightElbow = v; break;
                        case "head" when length: fd.Head = v; break;
                        case "drop" when length: fd.Drop = v; break;
                        case "shift" when length: fd.Shift = v; break;
                        case "height" when v is > 0.2f and <= 2: fd.Height = v; break;
                        default: throw new FormatException($"frame \"{fr.Name}\": fold.{pr.Name}: unknown, or out of range (angles -180 to 180 degrees, lengths -0.5 to 1.5, height to 2)");
                    }
                }
            }
            if (fr.Find(ShellModuleKind.Drill) != null && fr.Find(ShellModuleKind.Hopper) == null)
                throw new FormatException($"frame \"{fr.Name}\": a drill arm needs a hopper on the back to put its ore in");
            for (var i = 0; i < SlotCount; i++)
                if (fr.Modules[i] == null)
                    throw new FormatException($"frame \"{fr.Name}\": no module in its {Slots[i]} slot");
            if (list.Any(o => o.Name == fr.Name || o.Part == fr.Part))
                throw new FormatException($"frame \"{fr.Name}\": a name or part used twice");
            list.Add(fr);
        }
        if (list.Count == 0 || list.Count > 255)
            throw new FormatException("\"frames\": 1 to 255");
        if (list[0].Scale != 1 || list[0].Speed != 1 || list[0].Damage != 1 || list[0].Lift != 1 || !list[0].Sit || list[0].Part != "Robot Shell")
            throw new FormatException("frame 0 is the Robot Shell, at scale, speed and damage 1 (every older shell is it)");
        _modules = [.. order];
        return [.. list];
    }

    // ---- A shell's own modules (the Shell Gantry; mining-shell.md "Shell Gantry layout") ---------------------------------
    // A kit is a byte a slot (Slots order): 0, the frame's own module; else 1 + the module's Index. A standing shell keeps it in three
    // hidden settings (CfgKey.ShellKit0..2, two slots a setting), so it rides saves, blueprints and CBody; worn, it is the player's
    // ShellMods and every peer hears it with Msg.Shell. A shell saved before the gantry has none: every slot its frame's.

    private static readonly CfgKey[] KitKeys = [CfgKey.ShellKit0, CfgKey.ShellKit1, CfgKey.ShellKit2];

    /// <summary>Every shell part has these (ConstructConfig.Parse, through ShellWear.AddSettings): its kit, hidden.</summary>
    public static void AddSettings(ConstructPartDef d, List<CConfigDef> list)
    {
        foreach (var k in KitKeys)
            if (!list.Exists(e => e.Id == k))
                list.Add(new CConfigDef { Key = ConstructConfig.KeyName(k), Id = k, Type = CfgType.Mask, Hidden = true, Label = "Shell modules", Group = "Shell", Tooltip = "Not on the panel: the Shell Gantry sets them." });
    }

    /// <summary>A standing shell's kit, read from its part's settings.</summary>
    public static void ReadKit(in CPart p, ConstructPartDef d, Span<byte> kit)
    {
        for (var s = 0; s < KitKeys.Length; s++)
        {
            var v = (int)ConstructConfig.Get(p, d, KitKeys[s], 0);
            kit[s * 2] = (byte)(v & 0xFF);
            kit[s * 2 + 1] = (byte)(v >> 8 & 0xFF);
        }
    }

    /// <summary>...and written back.</summary>
    public static void WriteKit(ref CPart p, ConstructPartDef d, ReadOnlySpan<byte> kit)
    {
        for (var s = 0; s < KitKeys.Length; s++)
            ConstructConfig.Put(ref p, d, KitKeys[s], kit[s * 2] | kit[s * 2 + 1] << 8);
    }

    /// <summary>The module in slot <paramref name="slot"/> of a shell of frame <paramref name="frame"/> with kit <paramref name="kit"/>.</summary>
    public static ShellModule? ModuleAt(byte frame, ReadOnlySpan<byte> kit, int slot)
    {
        var f = Of(frame);
        var k = (uint)slot < (uint)kit.Length ? kit[slot] : (byte)0;
        if (k == 0 || f.Fixed[slot])
            return f.Modules[slot];
        var all = Modules;
        return k - 1 < all.Length && all[k - 1].Slot == SlotKind(slot) ? all[k - 1] : f.Modules[slot];
    }

    /// <summary>
    /// Fills <paramref name="into"/> with frame <paramref name="frame"/> as fitted with <paramref name="kit"/>: the frame's numbers, each
    /// slot's module, its pace and the hits it takes times its modules'. No allocation (a player's own <see cref="Player.ShellKit"/>).
    /// </summary>
    public static void Fit(ShellFrame into, byte frame, ReadOnlySpan<byte> kit)
    {
        var f = Of(frame);
        (into.Name, into.Part, into.Scale, into.Jobs, into.Lift, into.Sit) = (f.Name, f.Part, f.Scale, f.Jobs, f.Lift, f.Sit);
        float speed = f.Speed, damage = f.Damage;
        for (var i = 0; i < SlotCount; i++)
        {
            var m = ModuleAt(frame, kit, i);
            into.Modules[i] = m;
            into.Fixed[i] = f.Fixed[i];
            if (m != null)
                (speed, damage) = (speed * m.Speed, damage * m.Damage);
        }
        (into.Speed, into.Damage) = (speed, damage);
    }

    /// <summary>A player's shell as fitted now (their frame and kit; refitted by <see cref="Refit"/> whenever either changes).</summary>
    public static ShellFrame Worn(Player p) => p.ShellKit;

    /// <summary>After a player's <see cref="Player.ShellFrame"/> or <see cref="Player.ShellMods"/> changed.</summary>
    public static void Refit(Player p) => Fit(p.ShellKit, p.ShellFrame, p.ShellMods);

    /// <summary>Why <paramref name="m"/> can't go in slot <paramref name="slot"/> of a shell of frame <paramref name="frame"/> (null: it can).</summary>
    public static string? FitRefusal(byte frame, int slot, ShellModule m)
    {
        var f = Of(frame);
        if ((uint)slot >= SlotCount)
            return "no such slot";
        if (f.Fixed[slot])
            return $"the {f.Part}'s {SlotLabel(slot)} is fixed: the gantry can't change it";
        if (m.Slot != SlotKind(slot))
            return $"a {m.Name} goes in a {m.Slot} slot, not the {SlotLabel(slot)}";
        if (m.Part.Length == 0)
            return $"a {m.Name} is never taken off a shell";
        if (m.Kind is ShellModuleKind.Hopper or ShellModuleKind.Drill)
            return $"a {m.Name} is built into its shell";
        return null;
    }

    private static float Num(JsonElement e, string key, float min, float max)
    {
        var v = e.GetProperty(key).GetSingle();
        if (!float.IsFinite(v) || v < min || v > max)
            throw new FormatException($"\"{key}\" is {v}, not between {min} and {max}");
        return v;
    }
}
