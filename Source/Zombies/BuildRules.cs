using System.Text;

namespace CallOfSlop;

/// <summary>
/// A weapon as a tree of parts: a grip at the root (the part you hold), one or more receivers in its receiver sockets,
/// each with its own barrels, splitters, feed and ammo; stock, optic, foregrip and utility parts go on the grip
/// (Zombies weapon builder; see docs/zombies/WEAPON-BUILDER.md). Saved as one line of text:
/// <c>Name|style|Grip{socket:Part{...},socket:Part}</c>.
/// </summary>
public sealed class WeaponBuild
{
    public string Name = "Custom Build";
    public BuildNode? Root;

    /// <summary>
    /// The build's own choice of how several receivers fire (the workbench's "Chain fire" checkbox: Volley = all at
    /// once, Rotate = chain fire, taking turns), or null for the grip's default ("fireStyle" in its file). Saved in the
    /// build text's style field.
    /// </summary>
    public FireStyle? StyleChoice;

    /// <summary>How the receivers fire: the build's choice, else the grip's default.</summary>
    public FireStyle Style => StyleChoice ?? Frame?.Style ?? FireStyle.Volley;

    /// <summary>Chain fire: the receivers fire in sequence (one per shot) rather than together.</summary>
    public bool ChainFire => Style == FireStyle.Rotate;

    public WeaponBuild()
    {
    }

    public WeaponBuild(string name, PartDef grip)
    {
        Name = name;
        Root = new BuildNode(grip);
    }

    public IEnumerable<BuildNode> Nodes => Root?.Walk() ?? [];

    public FrameInfo? Frame => Root?.Part.Frame;

    /// <summary>The receivers in the grip's receiver sockets, in socket order (the first is the lead receiver).</summary>
    public List<BuildNode> Receivers => Root == null ? [] : Root.Children().Where(c => c.Socket.Type == "receiver" && c.Node.Part.Category == PartCategory.Receiver).Select(c => c.Node).ToList();

    /// <summary>The fire style used: the grip's.</summary>
    public FireStyle EffectiveStyle => Style;

    /// <summary>A melee build (rooted on a hilt) rather than a gun.</summary>
    public bool IsMelee => Root?.Part.Hilt != null;

    /// <summary>The shared feed (hopper / manifold) on the grip's hopper socket, if one is fitted.</summary>
    public BuildNode? Hopper => Root?.Children().Where(c => c.Socket.Type == "hopper" && c.Node.Part.Shared != null).Select(c => c.Node).FirstOrDefault();

    /// <summary>Why <paramref name="hopper"/> can't feed <paramref name="receiver"/> (bore, action, its ammo's bore), or null if it can.</summary>
    public static string? WhyNotShared(BuildNode hopper, BuildNode receiver)
    {
        var h = hopper.Part;
        var r = receiver.Part.Receiver!;
        if (!h.FitsBore(r.Bore))
            return $"the {h.Name} feeds {string.Join("/", h.Bores)} rounds, the {receiver.Part.Name} fires {r.Bore}";
        // A receiver with no action yet counts as fed (the missing action is what the build reports).
        if (h.RequiresAction.Length > 0 && ActionOf(receiver) is { } act && !h.RequiresAction.Contains(act.Kind))
            return $"the {h.Name} needs {string.Join("/", h.RequiresAction.Select(a => ActionInfo.Name(a)))} action, the {receiver.Part.Name}'s action is {act.FireType}";
        if (hopper.Child("ammo") is { } ammo && !ammo.Part.FitsBore(r.Bore))
            return $"the {h.Name} is loaded with {ammo.Part.Name}, the {receiver.Part.Name} fires {r.Bore}";
        return null;
    }

    /// <summary>
    /// The receivers the shared feed feeds: those with no feed of their own that it can feed (bore and action), in receiver
    /// socket order, up to its capacity. Empty when there's no shared feed.
    /// </summary>
    public List<BuildNode> HopperFed()
    {
        var list = new List<BuildNode>();
        if (Hopper is not { } h)
            return list;
        foreach (var r in Receivers)
            if (list.Count < h.Part.Shared!.Feeds && r.Child("feed") == null && WhyNotShared(h, r) == null)
                list.Add(r);
        return list;
    }

    /// <summary>The kg the grip handles: its carry weight plus every weight-relief part's bonus (the cap is twice this).</summary>
    public float CarryWeight => (Frame?.Carry ?? 0) + Nodes.Sum(n => n.Part.CarryBonus);
    /// <summary>The node at a path from <see cref="BuildNode.Path"/> ("" = the root), or null.</summary>
    public BuildNode? At(string path)
    {
        var n = Root;
        if (n == null || path.Length == 0)
            return n;
        foreach (var s in path.Split('/'))
        {
            n = n.Child(s);
            if (n == null)
                return null;
        }
        return n;
    }

    /// <summary>How many barrels the build has in all (bundle barrels count each of their muzzles).</summary>
    public int BarrelCount => Nodes.Where(n => n.Part.Category == PartCategory.Barrel).Sum(n => n.Part.Muzzles.Length);

    /// <summary>Barrels under one receiver.</summary>
    public static int BarrelsOf(BuildNode receiver) => receiver.Walk().Where(n => n.Part.Category == PartCategory.Barrel).Sum(n => n.Part.Muzzles.Length);

    public int PartCount => Nodes.Count();

    public float Weight => Nodes.Sum(n => n.Part.Weight);

    public int ScrapValue => Nodes.Sum(n => n.Part.Scrap);

    public WeaponBuild Clone() => new() { Name = Name, Root = Root?.Clone(), StyleChoice = StyleChoice };

    /// <summary>Stock actions <see cref="Parse"/> added to an old build (one written before actions were parts): it should be saved again.</summary>
    public int ActionsAdded;

    /// <summary>The action part in <paramref name="receiver"/>'s action socket (its fire type and rate), or null if it has none.</summary>
    public static ActionInfo? ActionOf(BuildNode receiver) => ActionNodeOf(receiver)?.Part.Action;

    /// <summary>The action node in <paramref name="receiver"/>'s action socket, or null.</summary>
    public static BuildNode? ActionNodeOf(BuildNode receiver)
    {
        foreach (var (s, c) in receiver.Children())
            if (s.Type == "action" && c.Part.Action != null)
                return c;
        return null;
    }

    // ---- Text form (saves) ---------------------------------------------------------------

    /// <summary>The build as one line: <c>Name|style|Grip{socket:Part{...},socket:Part}</c>.</summary>
    public string ToText()
    {
        var sb = new StringBuilder(Name).Append('|').Append(Style.ToString().ToLowerInvariant()).Append('|');
        if (Root != null)
            Write(sb, Root);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, BuildNode n)
    {
        sb.Append(n.Part.Name);
        var kids = n.Children().ToList();
        if (kids.Count == 0)
            return;
        sb.Append('{');
        for (var i = 0; i < kids.Count; i++)
        {
            if (i > 0)
                sb.Append(',');
            sb.Append(kids[i].Socket.Name).Append(':');
            Write(sb, kids[i].Node);
        }
        sb.Append('}');
    }

    /// <summary>
    /// Reads <see cref="ToText"/>'s format. Parts that no longer exist (a removed file) or no longer fit their socket (a
    /// receiver beyond a grip's capacity, say) are dropped and listed in <paramref name="dropped"/>, so an old save still
    /// loads. Throws <see cref="FormatException"/> on garbage.
    /// </summary>
    /// <remarks>
    /// <paramref name="migrate"/>: a text written before actions were parts (no action part anywhere in it) gets each
    /// receiver's stock action (<see cref="ActionMigration"/>; <see cref="ActionsAdded"/> counts them). Off for texts
    /// that are always current: the workbench's undo steps and builds a client sends the server.
    /// </remarks>
    public static WeaponBuild Parse(string text, Func<string, PartDef?> find, List<string>? dropped = null, bool migrate = true)
    {
        var fields = text.Split('|', 3);
        if (fields.Length != 3)
            throw new FormatException("a build is \"Name|style|Grip{...}\"");
        var b = new WeaponBuild { Name = CleanName(fields[0]), StyleChoice = StyleOf(fields[1]) };
        // fields[1] is the fire style (the "Chain fire" checkbox): "volley", or "rotate"/"chain"; anything else (an
        // empty field, "grip") means the grip's default. Saves always wrote the style the build fired with.
        var tree = fields[2];
        var pos = 0;
        if (tree.Length == 0)
            return b;
        var root = ReadNode(tree, ref pos, find, dropped);
        if (pos != tree.Length)
            throw new FormatException($"unexpected '{tree[pos]}' at {pos}");
        if (root is { Part.IsRoot: true })
            b.Root = root;
        else if (root != null)
            dropped?.Add($"{root.Part.Name} (a build starts from a grip or a hilt)");
        if (migrate)
            b.ActionsAdded = ActionMigration.FillIfOld(b, find);
        return b;
    }

    /// <summary>The fire style a build text's style field names, or null for the grip's default.</summary>
    public static FireStyle? StyleOf(string field) => field.Trim().ToLowerInvariant() switch
    {
        "volley" => FireStyle.Volley,
        "rotate" or "chain" => FireStyle.Rotate,
        _ => null,
    };

    /// <summary>
    /// Parts that were merged into or renamed to others, old name to new. The chain-fire twin grips became the "Chain
    /// fire" checkbox: a build on one loads on the merged grip, and its saved style field ("rotate") keeps it chain
    /// firing. The calibre-named ammo became universal round types.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Competition Pistol Grip"] = "Pistol Grip",
        ["Tactical Rifle Grip"] = "Rifle Grip",
        // Universal ammo (2026-10-23): the calibre-named rounds became round types that fit every receiver (the receiver
        // sets the calibre). An old build names the calibre its receiver fires anyway, so it loads with the same numbers.
        ["5.56 FMJ"] = "FMJ",
        ["9mm Ball"] = "FMJ",
        [".50 BMG"] = "Armour Piercing",
        ["00 Buckshot"] = "Buckshot",
        ["40mm Frag"] = "Frag",
        ["Plasma Charge"] = "Plasma",
        ["Rubber Duck Rounds"] = "Rubber Duck",
    };

    /// <summary>How deep <see cref="Parse"/> nests before calling the text garbage (far past <see cref="BuildRules.MaxParts"/>), so a hostile build can't overflow the stack.</summary>
    public const int MaxParseDepth = 256;

    private static BuildNode? ReadNode(string s, ref int pos, Func<string, PartDef?> find, List<string>? dropped, int depth = 0)
    {
        if (depth > MaxParseDepth)
            throw new FormatException($"parts nested more than {MaxParseDepth} deep");
        var start = pos;
        while (pos < s.Length && s[pos] is not ('{' or '}' or ','))
            pos++;
        var name = s[start..pos].Trim();
        if (name.Length == 0)
            throw new FormatException($"missing part name at {start}");
        var part = find(name) ?? (Renamed.TryGetValue(name, out var now) ? find(now) : null);
        var node = part != null ? new BuildNode(part) : null;
        if (part == null)
            dropped?.Add($"{name} (no such part)");
        if (pos < s.Length && s[pos] == '{')
        {
            pos++;
            while (true)
            {
                var colon = s.IndexOf(':', pos);
                if (colon < 0)
                    throw new FormatException($"expected socket:part at {pos}");
                var socket = s[pos..colon].Trim();
                pos = colon + 1;
                var child = ReadNode(s, ref pos, find, dropped, depth + 1);
                if (node != null && child != null)
                {
                    if (node.Part.Sockets.FirstOrDefault(x => x.Name.Equals(socket, StringComparison.OrdinalIgnoreCase)) is { } sock && sock.Type == child.Part.Mount)
                        node.Attach(sock.Name, child);
                    else
                        dropped?.Add($"{child.Part.Name} (doesn't fit {node.Part.Name}'s {socket} socket)");
                }
                if (pos >= s.Length)
                    throw new FormatException("missing '}'");
                if (s[pos] == ',')
                {
                    pos++;
                    continue;
                }
                if (s[pos] == '}')
                {
                    pos++;
                    break;
                }
                throw new FormatException($"unexpected '{s[pos]}' at {pos}");
            }
        }
        return node;
    }

    /// <summary>A build name: printable ASCII without the save format's separators, at most 32 characters.</summary>
    public static string CleanName(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Trim())
            if (ch is >= ' ' and <= '~' && ch is not ('|' or '{' or '}' or ':' or ',' or '=' or '%'))
                sb.Append(ch);
        var s = sb.ToString().Trim();
        if (s.Length > 32)
            s = s[..32].TrimEnd();
        return s.Length == 0 ? "Custom Build" : s;
    }
}

/// <summary>A problem with a build. Errors break a rule; "incomplete" means a required socket is still empty.</summary>
public readonly record struct BuildProblem(bool Incomplete, string Message);

/// <summary>
/// The assembly rules (see docs/zombies/WEAPON-BUILDER.md, "Graph rules"): a build starts from a root grip whose
/// receiver sockets are its receiver capacity; sockets take parts of their type and listed categories; feeds must
/// match the calibre class of the receiver they hang off (ammo fits any); some feeds need particular actions; splitters
/// stack at most two deep; and there are limits on parts, barrels, projectiles per trigger pull and weight. Every
/// refusal comes with a plain-English reason.
/// </summary>
public static class BuildRules
{
    /// <summary>Most parts in one weapon.</summary>
    public const int MaxParts = 96;

    /// <summary>Most barrels in one weapon, over all its receivers (after splitters and bundles).</summary>
    public const int MaxBarrels = 32;

    /// <summary>Splitters may feed splitters, but only this deep.</summary>
    public const int MaxSplitterDepth = 2;

    /// <summary>Most projectiles one trigger pull may fire (barrels x pellets; a shot packet carries a byte count).</summary>
    public const int MaxProjectiles = 240;

    /// <summary>
    /// Categories that must match their receiver's bore: only the feed (a magazine is sized for its rounds). Barrels,
    /// splitters and muzzle devices fit every receiver (the receiver adapts the barrel: <see cref="BarrelScale"/>,
    /// <see cref="BarrelClass"/>), and so does ammo: the receiver sets the calibre, the ammo part only picks the round
    /// type and its effect on top (docs/zombies/WEAPON-BUILDER.md, "Universal ammo").
    /// </summary>
    public static bool BoreBound(PartCategory c) => c is PartCategory.Feed;

    /// <summary>The bore class that adapts <paramref name="node"/> (a barrel): its nearest receiver's (walking up the tree), or null when it hangs under none.</summary>
    public static BoreClass? BarrelClass(BuildNode node) =>
        node.ReceiverNode() is { Part.Receiver: { } r } ? Parts.Registry.ClassOf(r.Bore) : null;

    /// <summary>
    /// How much <paramref name="barrel"/>'s model is scaled (width, height, length) under the receiver it hangs from (the
    /// nearest one up the tree): that receiver's barrel size (its own "barrelScale", else its bore class's) over the size
    /// of the class the barrel is drawn for (<see cref="PartDef.SizedFor"/>). One for a non-barrel or a barrel under no receiver.
    /// </summary>
    public static System.Numerics.Vector3 BarrelScale(BuildNode barrel)
    {
        if (barrel.Part.Category != PartCategory.Barrel || barrel.ReceiverNode() is not { Part.Receiver: { } r })
            return System.Numerics.Vector3.One;
        var reg = Parts.Registry;
        var want = r.BarrelScale ?? reg.ClassOf(r.Bore).Scale;
        var drawn = reg.ClassOf(barrel.Part.SizedFor).Scale;
        return want / drawn;
    }

    /// <summary>Heaviest melee weapon (a melee build has no grip carry weight).</summary>
    public const float MaxMeleeWeight = 14f;

    private static string Article(string word) => word.Length > 0 && "aeiou".Contains(char.ToLowerInvariant(word[0])) ? "an" : "a";

    private static string ActionName(ActionKind a) => ActionInfo.Name(a);

    private static string ListOr(IEnumerable<string> items)
    {
        var l = items.ToList();
        return l.Count switch
        {
            0 => "",
            1 => l[0],
            _ => string.Join(", ", l.Take(l.Count - 1)) + " or " + l[^1],
        };
    }

    private static string Plural(PartCategory c) => c switch
    {
        PartCategory.Ammo => "ammo",
        PartCategory.Utility => "utility parts",
        PartCategory.Multiplier => "damage multipliers",
        PartCategory.Upgrade => "receiver upgrades",
        PartCategory.MeleeMod => "melee mods",
        _ => c.ToString().ToLowerInvariant() + "s",
    };

    /// <summary>The socket-level rules for placing <paramref name="part"/> in <paramref name="socket"/> of <paramref name="parent"/>, or null.</summary>
    private static string? SocketProblem(BuildNode parent, PartSocket socket, PartDef part)
    {
        if (part.IsRoot)
            return part.Hilt != null
                ? $"{part.Name} is a hilt you build a melee weapon on: start a melee build with it instead."
                : $"{part.Name} is a grip you build on: start a new build with it instead.";
        if (socket.Type != part.Mount)
            return $"{part.Name} mounts on {Article(part.Mount)} {part.Mount} socket, not {Article(socket.Type)} {socket.Type} socket.";
        if (socket.Accepts.Length > 0 && !socket.Accepts.Contains(part.Category))
            return $"{parent.Part.Name}'s {socket.Name} socket takes {ListOr(socket.Accepts.Select(Plural))} only, not {Plural(part.Category)}.";
        if (parent.ReceiverNode() is { Part: var rp } rn && rp.Receiver is { } r)
        {
            // The receiver this socket hangs under (walking up the tree), not the build's first one.
            if (BoreBound(part.Category) && !part.FitsBore(r.Bore))
                return $"{part.Name} is made for {ListOr(part.Bores)} rounds; the {rp.Name} fires {r.Bore} rounds.";
            // Fire types: a feed that needs one is checked against the receiver's action part (none yet: no conflict),
            // and an action part against a feed already on the receiver that needs one.
            if (part.RequiresAction.Length > 0 && OtherAction(rn, socket) is { Part: var ap } && !part.RequiresAction.Contains(ap.Action!.Kind))
                return $"{part.Name} needs {ListOr(part.RequiresAction.Select(ActionName))} action; the {rp.Name}'s {ap.Name} is {ap.Action!.FireType}.";
            if (part.Action is { } pa && FeedNeeding(rn, socket) is { } feed && !feed.Part.RequiresAction.Contains(pa.Kind))
                return $"{part.Name} can't work the {feed.Part.Name}: it needs {ListOr(feed.Part.RequiresAction.Select(ActionName))} action, the {part.Name} is {pa.FireType}.";
        }
        else if (BoreBound(part.Category))
        {
            // Shared feeds sit on the grip; their ammo hangs off them (bores checked against the feed's).
            var hopper = part.Mount == "hopper" ? null : HopperAbove(parent);
            if (part.Mount != "hopper" && hopper == null)
                return $"{part.Name} has to hang off a receiver.";
            if (hopper != null && part.Bores.Length > 0 && hopper.Part.Bores.Length > 0 && !part.Bores.Intersect(hopper.Part.Bores).Any())
                return $"{part.Name} is made for {ListOr(part.Bores)} rounds; the {hopper.Part.Name} feeds {ListOr(hopper.Part.Bores)} rounds.";
        }
        if (part.Category == PartCategory.Splitter && parent.SplitterDepth() + (parent.Part.Category == PartCategory.Splitter ? 1 : 0) >= MaxSplitterDepth)
            return $"Splitters only stack {MaxSplitterDepth} deep: the shot gets too thin.";
        return null;
    }

    /// <summary>
    /// The socket-level rules alone (mount type, what the socket accepts, bores, actions, splitter depth), without the
    /// whole-build limits: cheap, so the workbench can ask it for every part and socket (null = the part fits there).
    /// </summary>
    public static string? SocketWhy(BuildNode parent, PartSocket socket, PartDef part) => SocketProblem(parent, socket, part);

    /// <summary>
    /// <see cref="SocketWhy"/> as a short lowercase phrase for the workbench list ("needs a 5.56 bore barrel socket"),
    /// or null when the part fits. Mirrors <see cref="SocketProblem"/> rule for rule (the self-test checks they agree).
    /// </summary>
    public static string? SocketWhyShort(BuildNode parent, PartSocket socket, PartDef part)
    {
        if (part.IsRoot)
            return part.Hilt != null ? "starts a new melee weapon" : "starts a new weapon";
        var mount = part.Mount;
        if (socket.Type != mount)
            return $"needs {Article(mount)} {mount} socket";
        if (socket.Accepts.Length > 0 && !socket.Accepts.Contains(part.Category))
            return $"needs {Article(mount)} {mount} socket that takes {Plural(part.Category)}";
        if (parent.ReceiverNode() is { Part: var rp } rn && rp.Receiver is { } r)
        {
            if (BoreBound(part.Category) && !part.FitsBore(r.Bore))
                return $"needs {Article(part.Bores[0])} {ListOr(part.Bores)} bore {mount} socket";
            if (part.RequiresAction.Length > 0 && OtherAction(rn, socket) is { Part: var ap } && !part.RequiresAction.Contains(ap.Action!.Kind))
                return $"needs {Article(ActionName(part.RequiresAction[0]))} {ListOr(part.RequiresAction.Select(ActionName))} action";
            if (part.Action is { } pa && FeedNeeding(rn, socket) is { } feed && !feed.Part.RequiresAction.Contains(pa.Kind))
                return $"the {feed.Part.Name} needs {ListOr(feed.Part.RequiresAction.Select(ActionName))} action";
        }
        else if (BoreBound(part.Category))
        {
            var hopper = mount == "hopper" ? null : HopperAbove(parent);
            if (mount != "hopper" && hopper == null)
                return "needs a receiver to hang off";
            if (hopper != null && part.Bores.Length > 0 && hopper.Part.Bores.Length > 0 && !part.Bores.Intersect(hopper.Part.Bores).Any())
                return $"needs {Article(part.Bores[0])} {ListOr(part.Bores)} bore shared feed";
        }
        if (part.Category == PartCategory.Splitter && parent.SplitterDepth() + (parent.Part.Category == PartCategory.Splitter ? 1 : 0) >= MaxSplitterDepth)
            return $"splitters already stack {MaxSplitterDepth} deep";
        return null;
    }

    /// <summary>
    /// Whether the empty socket <paramref name="s"/> of <paramref name="n"/> must be filled before the build is complete
    /// (required sockets and splitter outputs; a receiver the shared feed feeds needs no feed of its own, nor ammo when
    /// the shared feed is loaded). <paramref name="fed"/> is <see cref="WeaponBuild.HopperFed"/>.
    /// </summary>
    public static bool NeedsFilling(WeaponBuild b, BuildNode n, PartSocket s, List<BuildNode> fed)
    {
        if (n.Child(s.Name) != null || !(s.Required || (n.Part.Category == PartCategory.Splitter && s.Type == "barrel")))
            return false;
        if (b.IsMelee)
            return s.Required;
        return !(fed.Contains(n) && (s.Type == "feed" || (s.Type == "ammo" && b.Hopper?.Child("ammo") != null)));
    }

    /// <summary>
    /// The feed on <paramref name="receiver"/> that needs particular fire types (a belt, a hopper), or null. What's in
    /// <paramref name="replacing"/> doesn't count (it is being swapped out).
    /// </summary>
    private static BuildNode? FeedNeeding(BuildNode receiver, PartSocket replacing)
    {
        foreach (var (s, c) in receiver.Children())
            if (s != replacing && c.Part.RequiresAction.Length > 0)
                return c;
        return null;
    }

    /// <summary>The action node on <paramref name="receiver"/>, unless it sits in <paramref name="replacing"/> (being swapped out).</summary>
    private static BuildNode? OtherAction(BuildNode receiver, PartSocket replacing) =>
        WeaponBuild.ActionNodeOf(receiver) is { } a && a.ParentSocket != replacing ? a : null;

    private static BuildNode? HopperAbove(BuildNode n)
    {
        for (var x = n; x != null; x = x.Parent)
            if (x.Part.Shared != null)
                return x;
        return null;
    }

    /// <summary>
    /// Why <paramref name="part"/> can't go into <paramref name="socket"/> of <paramref name="parent"/> in <paramref name="build"/>
    /// (replacing what's there), or null if it can. Checks the socket itself, then the whole build as it would be.
    /// </summary>
    public static string? WhyNot(WeaponBuild build, BuildNode parent, PartSocket socket, PartDef part)
    {
        if (SocketProblem(parent, socket, part) is { } why)
            return why;
        // The build as it would be with the part in place.
        var trial = build.Clone();
        var at = trial.At(parent.Path());
        if (at == null)
            return "That socket isn't on this build any more.";
        at.Attach(socket.Name, part);
        return LimitProblem(trial);
    }

    /// <summary>
    /// <inheritdoc cref="WhyNot(WeaponBuild, BuildNode, PartSocket, PartDef)"/> By socket name; asking for a receiver
    /// socket the grip doesn't have explains its receiver capacity.
    /// </summary>
    public static string? WhyNot(WeaponBuild build, string parentPath, string socketName, PartDef part)
    {
        var parent = build.At(parentPath);
        if (parent == null)
            return "There's no such part on this build.";
        var socket = parent.Part.Sockets.FirstOrDefault(s => s.Name.Equals(socketName, StringComparison.OrdinalIgnoreCase));
        if (socket == null)
            return parent.Part.IsRoot && (part.Category == PartCategory.Receiver || socketName.StartsWith("receiver", StringComparison.OrdinalIgnoreCase))
                ? $"The {parent.Part.Name} holds {parent.Part.ReceiverCapacity} receiver{(parent.Part.ReceiverCapacity == 1 ? "" : "s")}; it has no {socketName} slot."
                : $"{parent.Part.Name} has no {socketName} socket.";
        return WhyNot(build, parent, socket, part);
    }

    /// <summary>Projectiles one trigger pull fires (volley: every receiver; rotate: the busiest receiver; rotary receivers fire one barrel per shot).</summary>
    public static int ProjectilesPerPull(WeaponBuild b)
    {
        var per = new List<int>();
        foreach (var r in b.Receivers)
        {
            var barrels = WeaponBuild.BarrelsOf(r);
            if (WeaponBuild.ActionOf(r)?.Kind == ActionKind.Rotary)
                barrels = Math.Min(barrels, 1);
            per.Add(barrels * BuildStats.PelletsOf(r));
        }
        if (per.Count == 0)
            return 0;
        return b.EffectiveStyle == FireStyle.Rotate ? per.Max() : per.Sum();
    }

    /// <summary>The first limit the build breaks (parts, barrels, projectiles, weight), or null.</summary>
    public static string? LimitProblem(WeaponBuild b)
    {
        var parts = b.PartCount;
        if (parts > MaxParts)
            return $"Too many parts: {parts}, the most a weapon holds is {MaxParts}.";
        var barrels = b.BarrelCount;
        if (barrels > MaxBarrels)
            return $"Too many barrels: {barrels}; the most a weapon holds is {MaxBarrels}.";
        foreach (var r in b.Receivers)
        {
            var rb = WeaponBuild.BarrelsOf(r);
            var max = Math.Min(MaxBarrels, r.Part.Receiver!.MaxBarrels);
            if (rb > max)
                return $"Too many barrels on the {r.Part.Name}: {rb}; it handles at most {max}.";
        }
        var shots = ProjectilesPerPull(b);
        if (shots > MaxProjectiles)
            return $"Too many projectiles per trigger pull: {shots}; the most is {MaxProjectiles}.";
        if (b.Frame != null && b.Weight > b.CarryWeight * 2)
            return $"Too heavy: {b.Weight:0.0} kg; the {b.Root!.Part.Name} can't carry more than {b.CarryWeight * 2:0.#} kg.";
        if (b.IsMelee && b.Weight > MaxMeleeWeight)
            return $"Too heavy: {b.Weight:0.0} kg; a melee weapon can't be more than {MaxMeleeWeight:0} kg.";
        return null;
    }

    /// <summary>Every problem with the build (errors first, then empty required sockets). An empty list = complete and legal.</summary>
    public static List<BuildProblem> Check(WeaponBuild b)
    {
        var list = new List<BuildProblem>();
        if (b.Root == null)
        {
            list.Add(new(true, "Pick a grip to start a build."));
            return list;
        }
        if (!b.Root.Part.IsRoot)
        {
            list.Add(new(false, $"{b.Root.Part.Name} isn't a grip you can build on: a build starts from a grip."));
            return list;
        }
        foreach (var n in b.Nodes)
            foreach (var (s, c) in n.Children())
                if (SocketProblem(n, s, c.Part) is { } why)
                    list.Add(new(false, why));
        if (LimitProblem(b) is { } limit)
            list.Add(new(false, limit));
        if (b.IsMelee)
        {
            foreach (var n in b.Nodes)
                foreach (var s in n.Part.Sockets)
                    if (n.Child(s.Name) == null && s.Required)
                        list.Add(new(true, $"{n.Part.Name} needs {Article(s.Type)} {s.Type}."));
            return list;
        }
        if (b.Receivers.Count == 0)
            list.Add(new(true, $"The {b.Root.Part.Name} needs at least one receiver (it holds up to {b.Root.Part.ReceiverCapacity})."));
        var fed = b.HopperFed();
        var hopper = b.Hopper;
        foreach (var n in b.Nodes)
            foreach (var s in n.Part.Sockets)
            {
                // A receiver the shared feed feeds needs no feed of its own (nor ammo, when the shared feed is loaded).
                if (!NeedsFilling(b, n, s, fed))
                    continue;
                var extra = s.Type == "feed" && hopper != null && n.Part.Receiver != null && !fed.Contains(n)
                    ? $" (the {hopper.Part.Name} can't feed it: {WeaponBuild.WhyNotShared(hopper, n) ?? $"it feeds {hopper.Part.Shared!.Feeds} receivers at most"})"
                    : s.Type == "ammo" && fed.Contains(n) ? $" (or load the {hopper!.Part.Name})" : "";
                list.Add(new(true, n.Part.Category == PartCategory.Splitter
                    ? $"{n.Part.Name}: output {s.Name} needs a barrel."
                    : $"{n.Part.Name} needs {(s.Type == "ammo" ? "ammo" : Article(s.Type) + " " + s.Type)}{extra}."));
            }
        foreach (var r in b.Receivers)
        {
            var info = r.Part.Receiver!;
            var barrels = WeaponBuild.BarrelsOf(r);
            if (barrels > 0 && barrels < info.MinBarrels)
                list.Add(new(true, $"{r.Part.Name} needs at least {info.MinBarrels} barrels (has {barrels})."));
        }
        return list;
    }

    public static bool IsComplete(WeaponBuild b) => Check(b).Count == 0;
}
