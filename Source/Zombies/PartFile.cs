using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// Reads and checks part files ("call-of-slop-part/1"). Every mistake throws a <see cref="WeaponFileException"/> naming
/// the file and the problem, in the same style as weapon and attachment files (unknown keys are errors, comments and
/// trailing commas are allowed). See docs/zombies/WEAPON-BUILDER.md for the format.
/// </summary>
public static class PartFile
{
    public const string Format = "call-of-slop-part/1";

    /// <summary>
    /// Socket types (what a part's "mount" and a socket's "type" may be): from Parts/_Registry.json (see
    /// <see cref="PartRegistry"/>; the built-in list when there is no registry file).
    /// </summary>
    public static string[] SocketTypes => Registry.SocketTypes;

    /// <summary>Calibre classes ("bore"), from the registry too.</summary>
    public static string[] BoreNames => Registry.Bores;

    /// <summary>The registry the part being loaded is checked against (its library's, else the game's).</summary>
    private static PartRegistry Registry => _lib?.Registry ?? Parts.Registry;

    /// <summary>The library (fragments, bases, registry) of the part being loaded on this thread; see <see cref="Load(string, string, PartLibrary)"/>.</summary>
    [ThreadStatic] private static PartLibrary? _lib;

    /// <summary>Box shapes a part's model may use.</summary>
    public static readonly string[] Shapes = ["box", "rod", "plate"];

    private static readonly string[] Keys =
    [
        "format", "name", "category", "rarity", "flavor", "author", "mount", "bores", "sizedFor", "requiresAction", "weight", "scrap", "flags", "hand",
        "gripAngle", "muzzles", "optic", "sight", "feed", "receiver", "split", "frame", "sockets", "model", "stats", "carry", "shared", "hilt", "melee",
    ];

    /// <summary>The mount a category gets when the file doesn't say (the registry's "mount" for it).</summary>
    public static string DefaultMount(PartCategory c) => Registry.MountOf(c);

    /// <summary>The built-in default mounts (used when there is no registry file, and checked against it by the self-test).</summary>
    public static string BuiltInMount(PartCategory c) => c switch
    {
        PartCategory.Receiver => "receiver",
        PartCategory.Barrel or PartCategory.Splitter => "barrel",
        PartCategory.Feed => "feed",
        PartCategory.Ammo => "ammo",
        PartCategory.Stock => "stock",
        PartCategory.Grip => "",
        PartCategory.Optic => "optic",
        PartCategory.Muzzle => "muzzle",
        PartCategory.Upgrade => "upgrade",
        PartCategory.Multiplier => "amp",
        PartCategory.Hilt => "",
        PartCategory.Head => "head",
        PartCategory.MeleeMod => "mod",
        _ => "utility",
    };

    /// <summary>Builds a part from a part file, with the game's part library (fragments, bases, registry). Throws <see cref="WeaponFileException"/> with a readable message if anything is wrong.</summary>
    public static PartDef Load(string json, string source) => Load(json, source, Parts.Library);

    /// <summary>
    /// Builds a part from a part file. "extends" is resolved against <paramref name="lib"/>'s parts and bases, and model
    /// entries with "use" against its fragments (Source/Zombies/PartLibrary.cs). Throws <see cref="WeaponFileException"/>
    /// with a readable message if anything is wrong.
    /// </summary>
    public static PartDef Load(string json, string source, PartLibrary lib)
    {
        var outer = _lib;
        _lib = lib;
        try
        {
            using var raw = WeaponFile.Parse(json, source);
            if (raw.RootElement.ValueKind == JsonValueKind.Object && PartLibrary.Get(raw.RootElement, "extends") != null)
                json = lib.ResolveExtends(raw.RootElement, source); // the merged file: the base's keys with this file's on top
            else
                PartLibrary.CheckNoMergeKeys(raw.RootElement, source);
            return LoadResolved(json, source);
        }
        finally
        {
            _lib = outer;
        }
    }

    private static PartDef LoadResolved(string json, string source)
    {
        using var doc = WeaponFile.Parse(json, source);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new WeaponFileException($"{source}: a part file is one JSON object {{ \"name\": ..., \"category\": ..., ... }}");
        var given = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in root.EnumerateObject())
        {
            if (!Keys.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
                throw new WeaponFileException($"{source}: unknown key \"{p.Name}\". Part files know: {string.Join(", ", Keys)}");
            if (!given.Add(p.Name))
                throw new WeaponFileException($"{source}: \"{p.Name}\" is given twice");
        }
        JsonElement? Get(string key) => root.EnumerateObject().Where(p => p.Name.Equals(key, StringComparison.OrdinalIgnoreCase)).Select(p => (JsonElement?)p.Value).FirstOrDefault();

        if (Get("format") is { } fmt)
            WeaponFile.CheckFormat(fmt, "call-of-slop-part/", Format, $"{source}: \"format\"");
        var name = Get("name") is { } n ? WeaponFile.Text(n, $"{source}: \"name\"").Trim() : throw new WeaponFileException($"{source}: missing \"name\"");
        CheckName(name, source);
        var where = $"{source} ('{name}')";
        var cat = Get("category") is { } c ? WeaponFile.EnumValue<PartCategory>(c, $"{where}: \"category\"") : throw new WeaponFileException($"{where}: missing \"category\" (one of {string.Join(", ", Enum.GetNames<PartCategory>()).ToLowerInvariant()})");
        var rarity = Get("rarity") is { } r ? WeaponFile.EnumValue<Rarity>(r, $"{where}: \"rarity\"") : throw new WeaponFileException($"{where}: missing \"rarity\" (one of {string.Join(", ", Enum.GetNames<Rarity>()).ToLowerInvariant()})");
        var part = new PartDef { Name = name, Category = cat, Rarity = rarity, SourceFile = source, Mount = DefaultMount(cat) };

        if (Get("flavor") is { } fl)
            part.Flavor = WeaponFile.Text(fl, $"{where}: \"flavor\"");
        if (Get("author") is { } au)
            part.Author = WeaponFile.Text(au, $"{where}: \"author\"");
        if (Get("mount") is { } mo)
            part.Mount = WeaponFile.Text(mo, $"{where}: \"mount\"").ToLowerInvariant();
        if (Get("bores") is { } bo)
            part.Bores = WeaponFile.TextList(bo, $"{where}: \"bores\"").Select(x => x.ToLowerInvariant()).ToArray();
        if (Get("sizedFor") is { } sf)
            part.SizedFor = WeaponFile.Text(sf, $"{where}: \"sizedFor\"").ToLowerInvariant();
        else if (cat == PartCategory.Barrel && part.Bores.Length > 0)
            part.SizedFor = part.Bores.Contains("rifle") ? "rifle" : part.Bores[0]; // an older barrel file: its bores only say what it was drawn for
        if (Get("requiresAction") is { } ra)
            part.RequiresAction = ra.ValueKind == JsonValueKind.Array
                ? ra.EnumerateArray().Select(x => WeaponFile.EnumValue<ActionKind>(x, $"{where}: \"requiresAction\"")).ToArray()
                : throw new WeaponFileException($"{where}: \"requiresAction\" should be a list like [\"auto\", \"chain\"]");
        if (Get("weight") is { } we)
            part.Weight = Range(WeaponFile.Number(we, $"{where}: \"weight\""), 0, 30, $"{where}: \"weight\" (kg)");
        if (Get("scrap") is { } sc)
            part.ScrapValue = (int)Range(WeaponFile.Int(sc, $"{where}: \"scrap\""), 0, 10000, $"{where}: \"scrap\"");
        if (Get("flags") is { } fg)
            part.Flags = WeaponFile.TextList(fg, $"{where}: \"flags\"").Select(x => x.ToLowerInvariant()).ToArray();
        if (Get("hand") is { } ha)
            part.Hand = Vec(ha, $"{where}: \"hand\"");
        if (Get("gripAngle") is { } ga)
            part.GripAngleDeg = Range(WeaponFile.Number(ga, $"{where}: \"gripAngle\""), -10, 50, $"{where}: \"gripAngle\"");
        if (Get("muzzles") is { } mz)
            part.Muzzles = mz.ValueKind == JsonValueKind.Array
                ? mz.EnumerateArray().Select((x, i) => Vec(x, $"{where}: \"muzzles\" #{i + 1}")).ToArray()
                : throw new WeaponFileException($"{where}: \"muzzles\" should be a list of points like [[0, 0, -0.3]]");
        if (Get("optic") is { } op)
            part.Optic = WeaponFile.EnumValue<OpticKind>(op, $"{where}: \"optic\"");
        if (Get("feed") is { } fe)
            part.FeedStyle = WeaponFile.Text(fe, $"{where}: \"feed\"").ToLowerInvariant();
        if (Get("receiver") is { } rc)
            part.Receiver = ReadReceiver(rc, $"{where}: \"receiver\"");
        if (Get("frame") is { } fr)
            part.Frame = ReadFrame(fr, $"{where}: \"frame\"");
        if (Get("split") is { } sp)
            part.Split = ReadSplit(sp, $"{where}: \"split\"");
        if (Get("carry") is { } cb)
            part.CarryBonus = Range(WeaponFile.Number(cb, $"{where}: \"carry\""), 0, 40, $"{where}: \"carry\" (kg of weight relief)");
        if (Get("shared") is { } sh)
            part.Shared = ReadShared(sh, $"{where}: \"shared\"");
        if (Get("hilt") is { } hi)
            part.Hilt = ReadHilt(hi, $"{where}: \"hilt\"");
        if (Get("melee") is { } me)
            part.Melee = ReadMelee(me, $"{where}: \"melee\"");
        if (Get("sockets") is { } so)
            part.Sockets = so.ValueKind == JsonValueKind.Array
                ? so.EnumerateArray().Select((x, i) => ReadSocket(x, $"{where}: socket #{i + 1}")).ToArray()
                : throw new WeaponFileException($"{where}: \"sockets\" should be a list of {{ \"name\", \"type\", \"pos\" }}");
        if (Get("model") is { } md)
            part.Boxes = ReadModel(md, where, 0);
        if (Get("stats") is { } st)
            part.Effects = st.ValueKind == JsonValueKind.Array
                ? st.EnumerateArray().Select((x, i) => ReadEffect(x, $"{where}: stat #{i + 1}")).ToArray()
                : throw new WeaponFileException($"{where}: \"stats\" should be a list like [{{ \"stat\": \"rangeM\", \"mul\": 1.2 }}]");
        // The sight block is read last: its defaults depend on the optic look and the zoom the stats set.
        if (Get("sight") is { } si)
        {
            if (part.Optic == OpticKind.Iron)
                throw new WeaponFileException($"{where}: \"sight\" needs an \"optic\" look");
            part.Sight = Sights.Read(si, part.Optic, ZoomOf(part), $"{where}: \"sight\"");
        }
        Validate(part, where);
        return part;
    }

    /// <summary>The zoom an optic part sets (its "adsZoom" effect), or 1.5.</summary>
    public static float ZoomOf(PartDef p) =>
        p.Effects.FirstOrDefault(e => e.Field.Name == nameof(WeaponDef.AdsZoom) && e.Op == "set")?.Value is float z ? z : 1.5f;

    /// <summary>Rules that need the whole part (category-specific requirements).</summary>
    public static void Validate(PartDef p, string where)
    {
        if (p.Category == PartCategory.Receiver)
        {
            if (p.Receiver == null)
                throw new WeaponFileException($"{where}: a receiver needs a \"receiver\" block (action, class, bore, rpm...)");
            if (p.Mount != "receiver")
                throw new WeaponFileException($"{where}: a receiver mounts on a grip's \"receiver\" socket");
            if (!p.Sockets.Any(s => s.Type == "barrel"))
                throw new WeaponFileException($"{where}: a receiver needs at least one \"barrel\" socket");
            if (p.Receiver.MinBarrels > p.Receiver.MaxBarrels)
                throw new WeaponFileException($"{where}: \"minBarrels\" is more than \"maxBarrels\"");
        }
        else if (p.Receiver != null)
            throw new WeaponFileException($"{where}: only receivers have a \"receiver\" block");
        if (p.Category == PartCategory.Hilt)
        {
            if (p.Mount != "" || p.Hilt == null)
                throw new WeaponFileException($"{where}: a hilt is the root of a melee weapon: no \"mount\", and a \"hilt\" block (damage, swing, reach...)");
            if (p.Hand == null)
                throw new WeaponFileException($"{where}: a hilt needs a \"hand\" point (where the hand holds it)");
            if (!p.Sockets.Any(s => s.Type == "head"))
                throw new WeaponFileException($"{where}: a hilt needs a \"head\" socket (where the blade or head goes)");
        }
        else if (p.Hilt != null)
            throw new WeaponFileException($"{where}: only hilts have a \"hilt\" block");
        if (p.Category is PartCategory.Head or PartCategory.MeleeMod && p.Melee == null)
            throw new WeaponFileException($"{where}: a melee {(p.Category == PartCategory.Head ? "head" : "mod")} needs a \"melee\" block (damage, swing, reach, cleave, stun)");
        if (p.Melee != null && p.Category is not (PartCategory.Head or PartCategory.MeleeMod))
            throw new WeaponFileException($"{where}: only melee heads and mods have a \"melee\" block");
        if (p.IsMeleePart && (p.Effects.Length > 0 || p.Bores.Length > 0))
            throw new WeaponFileException($"{where}: melee parts change the melee numbers with \"melee\"/\"hilt\", not gun \"stats\" or \"bores\"");
        if (p.Shared != null && (p.Category != PartCategory.Feed || p.Mount != "hopper"))
            throw new WeaponFileException($"{where}: a \"shared\" block is for shared feeds (category feed, \"mount\": \"hopper\")");
        if (p.Category == PartCategory.Feed && p.Mount == "hopper" && p.Shared == null)
            throw new WeaponFileException($"{where}: a feed on a hopper socket is a shared feed: give it a \"shared\" block (feeds)");
        if (p.Category == PartCategory.Grip)
        {
            if (p.Mount is not ("" or "foregrip"))
                throw new WeaponFileException($"{where}: a grip is either a root grip (no \"mount\", with a \"frame\" block) or mounts on \"foregrip\"");
            if (p.Mount == "" && p.Frame == null)
                throw new WeaponFileException($"{where}: a root grip (the part the weapon is built on) needs a \"frame\" block (fireStyles, hands, carry...)");
            if (p.Mount == "foregrip" && p.Frame != null)
                throw new WeaponFileException($"{where}: a foregrip has no \"frame\" block (only root grips do)");
            if (p.Frame != null && p.ReceiverCapacity == 0)
                throw new WeaponFileException($"{where}: a root grip needs at least one \"receiver\" socket (its receiver capacity)");
            if (p.Frame != null && p.Hand == null)
                throw new WeaponFileException($"{where}: a root grip needs a \"hand\" point (where the trigger hand holds it)");
        }
        else
        {
            if (p.Frame != null)
                throw new WeaponFileException($"{where}: only root grips have a \"frame\" block");
            if (p.Category != PartCategory.Hilt && !SocketTypes.Contains(p.Mount))
                throw new WeaponFileException($"{where}: \"mount\" \"{p.Mount}\" isn't a socket type ({string.Join(", ", SocketTypes)}; socket types are listed in Parts/_Registry.json)");
        }
        if (p.ReceiverCapacity > 0 && p.Frame == null)
            throw new WeaponFileException($"{where}: only root grips have \"receiver\" sockets");
        if (p.ReceiverCapacity > 16)
            throw new WeaponFileException($"{where}: at most 16 receiver sockets");
        if (p.Category == PartCategory.Splitter)
        {
            if (p.Split == null)
                throw new WeaponFileException($"{where}: a splitter needs a \"split\" block (outputMult, splay)");
            if (p.Sockets.Count(s => s.Type == "barrel") < 2)
                throw new WeaponFileException($"{where}: a splitter needs at least two \"barrel\" sockets (its outputs)");
        }
        else if (p.Split != null)
            throw new WeaponFileException($"{where}: only splitters have a \"split\" block");
        if (p.Category == PartCategory.Barrel && p.Muzzles.Length == 0)
            throw new WeaponFileException($"{where}: a barrel needs \"muzzles\": where its shots come out, e.g. [[0, 0, -0.3]]");
        if (p.Category == PartCategory.Barrel && p.Muzzles.Length > 8)
            throw new WeaponFileException($"{where}: at most 8 muzzles per barrel");
        if (p.Category == PartCategory.Optic && p.Optic == OpticKind.Iron)
            throw new WeaponFileException($"{where}: an optic needs an \"optic\" look ({string.Join(", ", Enum.GetNames<OpticKind>().Skip(1))})");
        if (p.Category != PartCategory.Optic && p.Optic != OpticKind.Iron)
            throw new WeaponFileException($"{where}: only optics have an \"optic\" look");
        foreach (var b in p.Bores)
            if (!BoreNames.Contains(b))
                throw new WeaponFileException($"{where}: bore \"{b}\" isn't one of {string.Join(", ", BoreNames)}");
        if (p.Category != PartCategory.Barrel && p.SizedFor != "rifle")
            throw new WeaponFileException($"{where}: \"sizedFor\" is for barrels (the bore class its model is drawn for)");
        if (!BoreNames.Contains(p.SizedFor))
            throw new WeaponFileException($"{where}: \"sizedFor\" \"{p.SizedFor}\" isn't a bore, one of {string.Join(", ", BoreNames)}");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in p.Sockets)
            if (!names.Add(s.Name))
                throw new WeaponFileException($"{where}: two sockets are called \"{s.Name}\"");
        foreach (var b in p.Boxes)
            if (b.Tag.StartsWith("with:", StringComparison.Ordinal) && !names.Contains(b.Tag[5..]))
                throw new WeaponFileException($"{where}: a box is tagged \"{b.Tag}\" but the part has no socket called \"{b.Tag[5..]}\"");
        if (p.Boxes.Length > MaxBoxes)
            throw new WeaponFileException($"{where}: {p.Boxes.Length} model boxes, at most {MaxBoxes} per part (fragments count with every box they bring)");
        if (p.Sockets.Length > 24)
            throw new WeaponFileException($"{where}: at most 24 sockets per part");
        foreach (var f in p.Flags)
            if (f.Length is 0 or > 24 || !f.All(ch => ch is >= 'a' and <= 'z' or '-'))
                throw new WeaponFileException($"{where}: flag \"{f}\" should be a short lower-case word like \"incendiary\"");
    }

    private static void CheckName(string name, string source)
    {
        if (name.Length is 0 or > 40)
            throw new WeaponFileException($"{source}: \"name\" must be 1-40 characters");
        foreach (var ch in name)
            if (!(char.IsAsciiLetterOrDigit(ch) || " .'-!#&+/()".Contains(ch)))
                throw new WeaponFileException($"{source}: \"name\" '{name}' has a '{ch}': use letters, digits, spaces and . ' - ! # & + / ( ) only");
    }

    private static float Range(float v, float min, float max, string where) =>
        v >= min && v <= max ? v : throw new WeaponFileException($"{where} is {WeaponFile.Num(v)}, but must be between {WeaponFile.Num(min)} and {WeaponFile.Num(max)}");

    private static Vector3 Vec(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 3)
            throw new WeaponFileException($"{where}: expected [x, y, z] in metres");
        var v = new Vector3(WeaponFile.Number(e[0], where), WeaponFile.Number(e[1], where), WeaponFile.Number(e[2], where));
        if (MathF.Abs(v.X) > 2 || MathF.Abs(v.Y) > 2 || MathF.Abs(v.Z) > 2)
            throw new WeaponFileException($"{where}: {v} is more than 2 m from the part's origin");
        return v;
    }

    private static Vector3 Angles(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 3)
            throw new WeaponFileException($"{where}: expected [x, y, z] in degrees");
        var v = new Vector3(WeaponFile.Number(e[0], where), WeaponFile.Number(e[1], where), WeaponFile.Number(e[2], where));
        if (MathF.Abs(v.X) > 360 || MathF.Abs(v.Y) > 360 || MathF.Abs(v.Z) > 360)
            throw new WeaponFileException($"{where}: angles must be between -360 and 360 degrees");
        return v;
    }

    private static IEnumerable<(string Key, JsonElement Value)> Props(JsonElement e, string where, params string[] known)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new WeaponFileException($"{where}: expected an object {{ ... }}");
        foreach (var p in e.EnumerateObject())
        {
            var k = known.FirstOrDefault(x => x.Equals(p.Name, StringComparison.OrdinalIgnoreCase))
                    ?? throw new WeaponFileException($"{where}: unknown key \"{p.Name}\" (known: {string.Join(", ", known)})");
            yield return (k, p.Value);
        }
    }

    private static ReceiverInfo ReadReceiver(JsonElement e, string where)
    {
        var r = new ReceiverInfo();
        var hasAction = false;
        foreach (var (k, v) in Props(e, where, "action", "class", "bore", "soundOf", "rpm", "burstCount", "burstDelay", "spoolTime", "spoolStart", "minBarrels", "maxBarrels",
                     "rpmPerBarrel", "barrelScale"))
        {
            var at = $"{where}.{k}";
            switch (k)
            {
                case "action": r.Action = WeaponFile.EnumValue<ActionKind>(v, at); hasAction = true; break;
                case "class": r.Class = WeaponFile.EnumValue<WeaponClass>(v, at); break;
                case "bore":
                    r.Bore = WeaponFile.Text(v, at).ToLowerInvariant();
                    if (!BoreNames.Contains(r.Bore))
                        throw new WeaponFileException($"{at}: \"{r.Bore}\" isn't one of {string.Join(", ", BoreNames)}");
                    break;
                case "soundOf": r.SoundOf = WeaponFile.Text(v, at); break;
                case "rpm": r.Rpm = (int)Range(WeaponFile.Int(v, at), 10, 3000, at); break;
                case "burstCount": r.BurstCount = (int)Range(WeaponFile.Int(v, at), 2, 10, at); break;
                case "burstDelay": r.BurstDelay = Range(WeaponFile.Number(v, at), 0, 2, at); break;
                case "spoolTime": r.SpoolTime = Range(WeaponFile.Number(v, at), 0, 5, at); break;
                case "spoolStart": r.SpoolStart = Range(WeaponFile.Number(v, at), 0.05f, 1, at); break;
                case "minBarrels": r.MinBarrels = (int)Range(WeaponFile.Int(v, at), 1, 8, at); break;
                case "maxBarrels": r.MaxBarrels = (int)Range(WeaponFile.Int(v, at), 1, 16, at); break;
                case "rpmPerBarrel": r.RpmPerBarrel = (int)Range(WeaponFile.Int(v, at), 0, 1000, at); break;
                case "barrelScale": r.BarrelScale = BoreClass.ReadScale(v, at); break;
            }
        }
        if (!hasAction)
            throw new WeaponFileException($"{where}: missing \"action\" ({string.Join(", ", Enum.GetNames<ActionKind>()).ToLowerInvariant()})");
        return r;
    }

    private static FrameInfo ReadFrame(JsonElement e, string where)
    {
        var f = new FrameInfo();
        var styled = false;
        foreach (var (k, v) in Props(e, where, "fireStyle", "fireStyles", "hands", "carry", "mobility", "ads", "ironHeight", "support", "reloadPerReceiver"))
        {
            var at = $"{where}.{k}";
            switch (k)
            {
                case "fireStyle":
                    if (styled)
                        throw new WeaponFileException($"{where}: give \"fireStyle\" once (the grip fixes the style)");
                    f.Style = WeaponFile.EnumValue<FireStyle>(v, at);
                    styled = true;
                    break;
                case "fireStyles":
                    // Older files listed styles the player could pick between; the grip now fixes one: the first.
                    if (styled)
                        throw new WeaponFileException($"{where}: give \"fireStyle\" once (the grip fixes the style)");
                    if (v.ValueKind != JsonValueKind.Array || v.GetArrayLength() == 0)
                        throw new WeaponFileException($"{at}: give \"fireStyle\": \"volley\" or \"rotate\" (the grip fixes the style)");
                    f.Style = WeaponFile.EnumValue<FireStyle>(v[0], at);
                    styled = true;
                    break;
                case "hands":
                    f.Hands = WeaponFile.Text(v, at).ToLowerInvariant();
                    if (f.Hands is not ("one" or "two" or "chainsaw" or "cart" or "wheel" or "case"))
                        throw new WeaponFileException($"{at}: \"{f.Hands}\" isn't one of one, two, chainsaw, cart, wheel, case");
                    break;
                case "carry": f.Carry = Range(WeaponFile.Number(v, at), 1, 60, at); break;
                case "mobility": f.Mobility = Range(WeaponFile.Number(v, at), 0.4f, 1.2f, at); break;
                case "ads": f.Ads = (bool)WeaponFile.FromJson(v, typeof(bool), at); break;
                case "ironHeight": f.IronHeight = Range(WeaponFile.Number(v, at), 0, 0.5f, at); break;
                case "support": f.Support = Vec(v, at); break;
                case "reloadPerReceiver": f.ReloadPerReceiver = Range(WeaponFile.Number(v, at), 0, 2, at); break;
            }
        }
        return f;
    }

    private static SplitInfo ReadSplit(JsonElement e, string where)
    {
        var s = new SplitInfo();
        foreach (var (k, v) in Props(e, where, "outputMult", "splay", "spreadMult"))
        {
            if (k == "outputMult")
                throw new WeaponFileException($"{where}: \"outputMult\" is gone: every splitter conserves damage (each output gets 1/N of the shot). Rarer splitters pay off in \"spreadMult\" (accuracy) and more outputs");
            if (k == "splay")
                s.SplayDeg = Range(WeaponFile.Number(v, $"{where}.splay"), 0, 30, $"{where}.splay");
            else
                s.SpreadMult = Range(WeaponFile.Number(v, $"{where}.spreadMult"), 0.2f, 2, $"{where}.spreadMult");
        }
        return s;
    }

    private static SharedFeedInfo ReadShared(JsonElement e, string where)
    {
        var s = new SharedFeedInfo();
        foreach (var (k, v) in Props(e, where, "feeds"))
            if (k == "feeds")
                s.Feeds = (int)Range(WeaponFile.Int(v, $"{where}.feeds"), 2, 16, $"{where}.feeds");
        return s;
    }

    private static HiltInfo ReadHilt(JsonElement e, string where)
    {
        var h = new HiltInfo();
        foreach (var (k, v) in Props(e, where, "damage", "swing", "reach", "cleave", "stun", "hands"))
        {
            var at = $"{where}.{k}";
            switch (k)
            {
                case "damage": h.Damage = Range(WeaponFile.Number(v, at), 1, 300, at); break;
                case "swing": h.Swing = Range(WeaponFile.Number(v, at), 0.2f, 3, at); break;
                case "reach": h.Reach = Range(WeaponFile.Number(v, at), 1, 4, at); break;
                case "cleave": h.Cleave = (int)Range(WeaponFile.Int(v, at), 1, 8, at); break;
                case "stun": h.Stun = Range(WeaponFile.Number(v, at), 0, 3, at); break;
                case "hands":
                    h.Hands = WeaponFile.Text(v, at).ToLowerInvariant();
                    if (h.Hands is not ("one" or "two"))
                        throw new WeaponFileException($"{at}: \"{h.Hands}\" isn't one or two");
                    break;
            }
        }
        return h;
    }

    private static MeleeEffects ReadMelee(JsonElement e, string where)
    {
        var m = new MeleeEffects();
        foreach (var (k, v) in Props(e, where, "damage", "damageMult", "swingMult", "reach", "cleave", "stun"))
        {
            var at = $"{where}.{k}";
            switch (k)
            {
                case "damage": m.Damage = Range(WeaponFile.Number(v, at), -100, 300, at); break;
                case "damageMult": m.DamageMult = Range(WeaponFile.Number(v, at), 0.1f, 5, at); break;
                case "swingMult": m.SwingMult = Range(WeaponFile.Number(v, at), 0.2f, 3, at); break;
                case "reach": m.Reach = Range(WeaponFile.Number(v, at), -1, 2, at); break;
                case "cleave": m.Cleave = (int)Range(WeaponFile.Int(v, at), -4, 8, at); break;
                case "stun": m.Stun = Range(WeaponFile.Number(v, at), 0, 3, at); break;
            }
        }
        return m;
    }

    private static PartSocket ReadSocket(JsonElement e, string where)
    {
        string? name = null, type = null;
        var s = new PartSocket { Name = "", Type = "" };
        foreach (var (k, v) in Props(e, where, "name", "type", "accepts", "pos", "rot", "required"))
        {
            var at = $"{where}.{k}";
            switch (k)
            {
                case "name": name = WeaponFile.Text(v, at); break;
                case "type": type = WeaponFile.Text(v, at).ToLowerInvariant(); break;
                case "accepts":
                    s.Accepts = v.ValueKind == JsonValueKind.Array
                        ? v.EnumerateArray().Select(x => WeaponFile.EnumValue<PartCategory>(x, at)).ToArray()
                        : throw new WeaponFileException($"{at}: expected a list like [\"barrel\", \"splitter\"]");
                    break;
                case "pos": s.Pos = Vec(v, at); break;
                case "rot": s.RotDeg = Angles(v, at); break;
                case "required": s.Required = (bool)WeaponFile.FromJson(v, typeof(bool), at); break;
            }
        }
        if (string.IsNullOrWhiteSpace(name) || name.Length > 24 || !name.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_'))
            throw new WeaponFileException($"{where}: a socket needs a \"name\" of letters, digits and _ (at most 24)");
        if (type == null || !SocketTypes.Contains(type))
            throw new WeaponFileException($"{where} ('{name}'): {(type == null ? "missing \"type\"" : $"\"type\" \"{type}\" isn't a socket type")}: use one of {string.Join(", ", SocketTypes)} (socket types are listed in Parts/_Registry.json)");
        s.Name = name;
        s.Type = type;
        return s;
    }

    /// <summary>Most model boxes one part may have (after fragments are expanded).</summary>
    public const int MaxBoxes = 64;

    /// <summary>
    /// A "model" list: boxes, and fragment uses (<c>{ "use": "Picatinny Rail", "pos", "rot", "scale", "color" }</c>)
    /// expanded into the fragment's boxes, moved, turned and scaled (see <see cref="PartLibrary"/>).
    /// </summary>
    internal static PartBox[] ReadModel(JsonElement md, string where, int depth)
    {
        if (md.ValueKind != JsonValueKind.Array)
            throw new WeaponFileException($"{where}: \"model\" should be a list of boxes");
        var list = new List<PartBox>();
        var i = 0;
        foreach (var x in md.EnumerateArray())
        {
            i++;
            var at = $"{where}: model box #{i}";
            if (x.ValueKind == JsonValueKind.Object && PartLibrary.Get(x, "use") != null)
                list.AddRange((_lib ?? Parts.Library).Use(x, at, depth));
            else
                list.Add(ReadBox(x, at));
        }
        return list.ToArray();
    }

    /// <summary>A position (metres, within 2 m of the part's origin).</summary>
    internal static Vector3 ReadVec(JsonElement e, string where) => Vec(e, where);

    /// <summary>Euler angles in degrees (X, Y, Z; applied Z, X, Y).</summary>
    internal static Vector3 ReadAngles(JsonElement e, string where) => Angles(e, where);

    /// <summary>A number between <paramref name="min"/> and <paramref name="max"/>.</summary>
    internal static float ReadRange(JsonElement e, float min, float max, string where) => Range(WeaponFile.Number(e, where), min, max, where);

    /// <summary>A box tag ("", "irons", "glow" or "with:SOCKET"; the socket is checked against the part in <see cref="Validate"/>).</summary>
    internal static string ReadTag(JsonElement v, string at)
    {
        var t = WeaponFile.Text(v, at).ToLowerInvariant();
        return t is "" or "irons" or "glow" || (t.StartsWith("with:", StringComparison.Ordinal) && t.Length > 5)
            ? t
            : throw new WeaponFileException($"{at}: \"{t}\" isn't a tag (irons, glow, with:SOCKET)");
    }

    private static PartBox ReadBox(JsonElement e, string where)
    {
        var b = new PartBox();
        var hasSize = false;
        foreach (var (k, v) in Props(e, where, "shape", "size", "pos", "rot", "color", "alpha", "tag"))
        {
            var at = $"{where}.{k}";
            switch (k)
            {
                case "shape":
                    b.Shape = WeaponFile.Text(v, at).ToLowerInvariant();
                    if (!Shapes.Contains(b.Shape))
                        throw new WeaponFileException($"{at}: \"{b.Shape}\" isn't one of {string.Join(", ", Shapes)}");
                    break;
                case "size":
                    b.Size = Vec(v, at);
                    hasSize = true;
                    if (b.Size.X is < 0.001f or > 1.5f || b.Size.Y is < 0.001f or > 1.5f || b.Size.Z is < 0.001f or > 1.5f)
                        throw new WeaponFileException($"{at}: each size must be between 0.001 and 1.5 m");
                    break;
                case "pos": b.Pos = Vec(v, at); break;
                case "rot": b.RotDeg = Angles(v, at); break;
                case "color": b.Color = ReadColor(v, at); break;
                case "alpha": b.Alpha = Range(WeaponFile.Number(v, at), 0.05f, 1, at); break;
                case "tag": b.Tag = ReadTag(v, at); break;
            }
        }
        if (!hasSize)
            throw new WeaponFileException($"{where}: a box needs a \"size\" [w, h, l]");
        return b;
    }

    /// <summary>A colour: "accent" (the build's accent), a named finish (black, gunmetal, wood...), [r, g, b] or "#rrggbb".</summary>
    public static GunColor ReadColor(JsonElement v, string where)
    {
        if (v.ValueKind == JsonValueKind.String)
        {
            var t = v.GetString()!.ToLowerInvariant();
            if (t == "accent" || GunColor.Named.ContainsKey(t))
                return new GunColor(t, default);
            if (t.StartsWith('#'))
                return new GunColor(null, (Vector3)WeaponFile.FromJson(v, typeof(Vector3), where));
            throw new WeaponFileException($"{where}: \"{t}\" isn't a colour: use accent, {string.Join(", ", GunColor.Named.Keys)}, [r, g, b] or \"#rrggbb\"");
        }
        var c = (Vector3)WeaponFile.FromJson(v, typeof(Vector3), where);
        if (c.X is < 0 or > 1 || c.Y is < 0 or > 1 || c.Z is < 0 or > 1)
            throw new WeaponFileException($"{where}: colour values must be between 0 and 1");
        return new GunColor(null, c);
    }

    private static readonly string[] Ops = ["set", "add", "mul", "grow"];

    /// <summary>The stats a part may change: every attachment stat (see Attachments/README.md) except the optic look and suppressor (a part says those itself).</summary>
    public static IEnumerable<System.Reflection.FieldInfo> StatFields => AttachmentFile.EffectFields;

    private static AttachmentEffect ReadEffect(JsonElement e, string where)
    {
        System.Reflection.FieldInfo? field = null;
        string? op = null;
        JsonElement value = default;
        float? min = null, max = null;
        foreach (var (k, v) in Props(e, where, "stat", "set", "add", "mul", "grow", "min", "max"))
        {
            if (k == "stat")
            {
                var s = WeaponFile.Text(v, where);
                field = AttachmentFile.EffectFields.FirstOrDefault(f => f.Name.Equals(s, StringComparison.OrdinalIgnoreCase))
                        ?? throw new WeaponFileException($"{where}: unknown stat \"{s}\". Parts can change: {string.Join(", ", AttachmentFile.EffectFields.Select(WeaponFile.Key))}");
            }
            else if (Ops.Contains(k))
            {
                if (op != null)
                    throw new WeaponFileException($"{where}: give only one of set, add, mul or grow per stat change");
                op = k;
                value = v;
            }
            else if (k == "min")
                min = WeaponFile.Number(v, where);
            else
                max = WeaponFile.Number(v, where);
        }
        if (field == null || op == null)
            throw new WeaponFileException($"{where}: a stat change needs \"stat\" and one of \"set\", \"add\", \"mul\" or \"grow\"");
        var numeric = field.FieldType == typeof(float) || field.FieldType == typeof(int);
        object val;
        if (op == "set")
            val = WeaponFile.FromJson(value, field.FieldType, $"{where} ({WeaponFile.Key(field)})");
        else if (!numeric)
            throw new WeaponFileException($"{where}: \"{WeaponFile.Key(field)}\" isn't a number, so it can only be \"set\"");
        else if (op == "grow" && field.FieldType != typeof(int))
            throw new WeaponFileException($"{where}: \"grow\" is for whole numbers like \"mag\"; use \"mul\" for \"{WeaponFile.Key(field)}\"");
        else
            val = WeaponFile.Number(value, where);
        if (field.Name is nameof(WeaponDef.Optic) or nameof(WeaponDef.Suppressor))
            throw new WeaponFileException($"{where}: parts set the optic with \"optic\" and suppress with the \"suppressed\" flag, not as stats");
        return new AttachmentEffect { Field = field, Op = op, Value = val, Min = min, Max = max }.Prepare();
    }
}

/// <summary>
/// Every weapon part, loaded from the Parts folder next to the game (Zombies weapon builder). Top-level folders and files
/// starting with "_" aren't parts: _Registry.json (socket types, bores, categories), _Fragments (reusable model pieces),
/// _Bases (templates parts can "extend") and _Template (the commented starting point); see <see cref="PartLibrary"/>.
/// <see cref="Reload"/> re-reads the folder while the game runs (Source/Zombies/PartReload.cs).
/// </summary>
public static partial class Parts
{
    private static readonly object LoadLock = new();
    private static PartDef[]? _all;
    private static PartLibrary? _library;

    /// <summary>Problems found while loading (bad files are skipped). Shown on the workbench and in the log.</summary>
    public static readonly List<string> LoadErrors = new();

    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Parts");

    /// <summary>Every part, sorted by category, tier, then name.</summary>
    public static PartDef[] All
    {
        get
        {
            var all = _all;
            if (all != null)
                return all;
            lock (LoadLock)
            {
                if (_all == null)
                {
                    var (parts, lib, _) = LoadDetailed(Folder, LoadErrors);
                    _library = lib;
                    _all = parts;
                }
                return _all;
            }
        }
    }

    /// <summary>The game's part library: the registry, fragments, bases and every part file's text (for "extends").</summary>
    public static PartLibrary Library
    {
        get
        {
            _ = All;
            return _library!;
        }
    }

    /// <summary>The game's registry (socket types, bores, categories): Parts/_Registry.json, or the built-in one.</summary>
    public static PartRegistry Registry => Library.Registry;

    /// <summary>
    /// Files under Parts that aren't part files: Builds/ (standard complete builds), Tables/ (drop tables), and anything
    /// whose top-level folder or file name starts with "_" (the registry, fragments, bases and templates).
    /// </summary>
    public static bool IsDataFile(string relative)
    {
        var first = relative.Replace('\\', '/').Split('/')[0];
        return first.Equals("Builds", StringComparison.OrdinalIgnoreCase) || first.Equals("Tables", StringComparison.OrdinalIgnoreCase) || first.StartsWith('_');
    }

    public static PartDef? Find(string name) => All.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<PartDef> InCategory(PartCategory c) => All.Where(p => p.Category == c);

    /// <summary>Loads every *.json under <paramref name="folder"/> (bad files go to <paramref name="errors"/>; duplicate names are errors).</summary>
    public static PartDef[] LoadFrom(string folder, List<string> errors) => LoadDetailed(folder, errors).Parts;

    /// <summary>
    /// <see cref="LoadFrom"/>, also returning the library it built and the part files that didn't load (relative paths;
    /// hot reload keeps their previous definitions).
    /// </summary>
    public static (PartDef[] Parts, PartLibrary Library, HashSet<string> Failed) LoadDetailed(string folder, List<string> errors)
    {
        var list = new List<PartDef>();
        var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lib = PartLibrary.FromFolder(folder, errors);
        if (!Directory.Exists(folder))
        {
            errors.Add($"no Parts folder at {folder}");
            return ([], lib, failed);
        }
        foreach (var (rel, text) in lib.PartTexts)
        {
            try
            {
                var p = PartFile.Load(text, rel, lib);
                if (list.FirstOrDefault(x => x.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase)) is { } dup)
                    throw new WeaponFileException($"{rel}: another part is already called '{p.Name}' ({dup.SourceFile})");
                list.Add(p);
            }
            catch (WeaponFileException ex)
            {
                failed.Add(rel);
                errors.Add(ex.Message);
                Console.WriteLine($"[parts] skipped: {ex.Message}");
            }
        }
        Console.WriteLine($"[parts] {list.Count} part(s) from {folder}"
                          + (lib.Fragments.Count + lib.Bases.Count > 0 ? $" ({lib.Fragments.Count} fragment(s), {lib.Bases.Count} base(s))" : ""));
        return (Sort(list), lib, failed);
    }

    /// <summary>The order <see cref="All"/> is in: category, tier, name. Network ids are positions in it.</summary>
    internal static PartDef[] Sort(IEnumerable<PartDef> parts) =>
        parts.OrderBy(p => p.Category).ThenBy(p => p.Rarity).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
}
