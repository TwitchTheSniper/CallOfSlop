using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// Every Zombies weapon builder part as a Sandbox wrench item (board item dev-sandbox-weapon-parts-wp01, decision 23;
/// docs/sandbox/CONSTRUCTS.md, "Weapon builder parts in Sandbox"). The construct defs are generated at load from the part
/// library, never written by hand: each builder part becomes a part file's text (its model's boxes, scaled, a hidden solid
/// box round them, a socket on each face; a receiver a gun on that receiver with a bottom mount) that goes through the
/// ordinary <see cref="ConstructParts.Parse"/>, so it is checked like any file. They are appended after the hand-made
/// files in the builder's own sorted order, so the hand-made parts keep their ids and every peer with the same files gets
/// the same ids. A builder part a hand-made file already wraps (a <c>gunPart</c> or a receiver gun) is not made again.
/// The rules (scale, mass, creative-only categories) are data: Constructs/WeaponParts.json.
/// </summary>
public static class ConstructWeaponParts
{
    public const string Format = "call-of-slop-weapon-parts/1";

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "WeaponParts.json");

    public sealed class Rules
    {
        public float Scale = 2f, MassScale = 2f, HealthBase = 40f, HealthPerKg = 15f, Armour = 2f, AttachSeconds = 1.2f, ReceiverAttachSeconds = 2f, ReceiverKick = 6f;
        public HashSet<PartCategory> CreativeOnly = [PartCategory.Upgrade, PartCategory.Multiplier];
        public HashSet<PartCategory> Skip = [];
    }

    /// <summary>Reads the rules file (a missing or bad file: the defaults, the problem in <paramref name="refused"/>).</summary>
    public static Rules Load(string text, string file, List<string> refused)
    {
        var r = new Rules();
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (!root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new ConstructPartException($"{file}: \"format\" must be \"{Format}\"");
            foreach (var p in root.EnumerateObject())
                switch (p.Name)
                {
                    case "format" or "about": break;
                    case "scale": r.Scale = Clamp(p.Value, 0.5f, 6f, file, p.Name); break;
                    case "massScale": r.MassScale = Clamp(p.Value, 0.1f, 20f, file, p.Name); break;
                    case "healthBase": r.HealthBase = Clamp(p.Value, 1f, 10000f, file, p.Name); break;
                    case "healthPerKg": r.HealthPerKg = Clamp(p.Value, 0f, 1000f, file, p.Name); break;
                    case "armour": r.Armour = Clamp(p.Value, 0f, 100f, file, p.Name); break;
                    case "attachSeconds": r.AttachSeconds = Clamp(p.Value, 0.2f, 20f, file, p.Name); break;
                    case "receiverAttachSeconds": r.ReceiverAttachSeconds = Clamp(p.Value, 0.2f, 20f, file, p.Name); break;
                    case "receiverKick": r.ReceiverKick = Clamp(p.Value, 0f, 1000f, file, p.Name); break;
                    case "creativeOnly": r.CreativeOnly = Categories(p.Value, file, p.Name); break;
                    case "skip": r.Skip = Categories(p.Value, file, p.Name); break;
                    default: throw new ConstructPartException($"{file}: unknown key \"{p.Name}\"");
                }
        }
        catch (Exception ex) when (ex is ConstructPartException or JsonException or InvalidOperationException)
        {
            refused.Add(ex is ConstructPartException ? ex.Message : $"{file}: {ex.Message}");
            return new Rules();
        }
        return r;
    }

    private static float Clamp(JsonElement e, float lo, float hi, string file, string key)
    {
        if (e.ValueKind != JsonValueKind.Number || e.GetSingle() is var v && (v < lo || v > hi))
            throw new ConstructPartException($"{file}: \"{key}\" is a number from {lo} to {hi}");
        return v;
    }

    private static HashSet<PartCategory> Categories(JsonElement e, string file, string key)
    {
        var set = new HashSet<PartCategory>();
        if (e.ValueKind != JsonValueKind.Array)
            throw new ConstructPartException($"{file}: \"{key}\" is a list of weapon builder categories");
        foreach (var c in e.EnumerateArray())
            set.Add(Enum.TryParse<PartCategory>(c.GetString(), true, out var pc) ? pc : throw new ConstructPartException($"{file}: \"{key}\": no weapon builder category \"{c.GetString()}\""));
        return set;
    }

    /// <summary>The rules in force (loaded with the parts).</summary>
    public static Rules Current { get; private set; } = new();

    /// <summary>
    /// Appends a generated def for every builder part not already wrapped by a hand-made file, in the builder's sorted
    /// order. A part whose def can't be made is refused with the reason (the rest still load).
    /// </summary>
    public static void Append(List<ConstructPartDef> list, List<string> refused, IReadOnlyList<PartDef> builder, Rules rules)
    {
        Current = rules;
        var wrapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in list)
        {
            names.Add(d.Name);
            if (d.GunPartName.Length > 0)
                wrapped.Add(d.GunPartName);
            if (d.GunReceiver.Length > 0)
                wrapped.Add(d.GunReceiver);
        }
        foreach (var p in builder)
        {
            if (wrapped.Contains(p.Name) || rules.Skip.Contains(p.Category))
                continue;
            var file = $"(weapon part) {p.Name}";
            try
            {
                var name = UniqueName(p.Name, names);
                var def = ConstructParts.Parse(Text(p, name, rules), file);
                def.Folder = "Weapon parts/" + p.Category;
                def.Generated = true;
                def.CreativeOnly = rules.CreativeOnly.Contains(p.Category);
                if (list.Count >= ConstructParts.MaxTypes)
                    throw new ConstructPartException($"{file}: more than {ConstructParts.MaxTypes} part types");
                def.Id = (ushort)list.Count;
                names.Add(def.Name);
                list.Add(def);
            }
            catch (Exception ex) when (ex is ConstructPartException or JsonException)
            {
                refused.Add(ex.Message);
            }
        }
    }

    /// <summary>The builder's name, or (if a construct part already has it, or it is too long) a deterministic variant.</summary>
    private static string UniqueName(string n, HashSet<string> taken)
    {
        var name = Ascii(n);
        if (name.Length > 32)
            name = name[..32].TrimEnd();
        if (!taken.Contains(name))
            return name;
        var stem = name.Length > 27 ? name[..27].TrimEnd() : name;
        for (var k = 1; k < 100; k++)
        {
            var cand = k == 1 ? $"{stem} (W)" : $"{stem} (W{k})";
            if (!taken.Contains(cand))
                return cand;
        }
        throw new ConstructPartException($"(weapon part) {n}: no free name");
    }

    private static string Ascii(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
            sb.Append(ch is >= ' ' and <= '~' ? ch : '-');
        return sb.ToString().Trim();
    }

    /// <summary>The builder part a construct part stands for ("" for any other part): its gun part, or the receiver of a receiver gun.</summary>
    public static string NameOf(ConstructPartDef d) => d.GunPartName.Length > 0 ? d.GunPartName : d.IsReceiverGun ? d.GunReceiver : "";

    /// <summary>The construct part (the item) that stands for builder part <paramref name="name"/>: the lowest id that does, or null.</summary>
    public static ConstructPartDef? DefFor(string name)
    {
        foreach (var d in ConstructParts.All)
            if (NameOf(d) is { Length: > 0 } n && n.Equals(name, StringComparison.OrdinalIgnoreCase))
                return d;
        return null;
    }

    /// <summary>The inventory section a builder category goes in (the builder's own display names).</summary>
    public static string SectionOf(PartCategory c) => c switch
    {
        PartCategory.Hilt or PartCategory.Head or PartCategory.MeleeMod => "Melee: " + PartDef.CategoryName(c),
        PartCategory.Casing or PartCategory.Fuse or PartCategory.Payload or PartCategory.GrenadeMod => "Grenade: " + PartDef.CategoryName(c),
        _ => PartDef.CategoryName(c),
    };

    /// <summary>The part file text for one builder part (see the class summary).</summary>
    public static string Text(PartDef p, string name, Rules rules)
    {
        var s = rules.Scale;
        // The drawn boxes: the builder model's (tagged boxes, drawn only with something else fitted, left out), each as the
        // box round it turned, the biggest 15 kept, in their own order.
        var boxes = new List<(Vector3 Min, Vector3 Max, Vector3 Rgb, float Vol, int Order)>();
        for (var i = 0; i < p.Boxes.Length; i++)
        {
            var b = p.Boxes[i];
            if (b.Tag.Length > 0 || b.Alpha < 0.5f)
                continue;
            var h = b.Size * 0.5f;
            var q = b.Rotation;
            var ext = Vector3.Zero;
            for (var k = 0; k < 8; k++)
            {
                var corner = new Vector3((k & 1) != 0 ? h.X : -h.X, (k & 2) != 0 ? h.Y : -h.Y, (k & 4) != 0 ? h.Z : -h.Z);
                ext = Vector3.Max(ext, Vector3.Abs(Vector3.Transform(corner, q)));
            }
            ext = Vector3.Max(ext * s, new Vector3(0.0055f));
            var c = b.Pos * s;
            boxes.Add((c - ext, c + ext, b.Color.Resolve(new Vector3(0.75f, 0.45f, 0.15f)), ext.X * ext.Y * ext.Z, i));
        }
        if (boxes.Count == 0)
            boxes.Add((new Vector3(-0.03f), new Vector3(0.03f), new Vector3(0.3f), 1, 0));
        if (boxes.Count > 15)
            boxes = boxes.OrderByDescending(b => b.Vol).ThenBy(b => b.Order).Take(15).OrderBy(b => b.Order).ToList();
        var mn = new Vector3(float.MaxValue);
        var mx = new Vector3(float.MinValue);
        foreach (var b in boxes)
        {
            mn = Vector3.Min(mn, b.Min);
            mx = Vector3.Max(mx, b.Max);
        }
        // Centred on its bounds (the sockets on its faces, a merged receiver's grid on its size), at least 4 cm a side.
        var centre = (mn + mx) * 0.5f;
        var half = Vector3.Max((mx - mn) * 0.5f, new Vector3(0.02f));
        var mass = MathF.Max(0.5f, MathF.Round(p.Weight * rules.MassScale * 10f) / 10f);
        var receiver = p.Category == PartCategory.Receiver && p.Receiver != null;
        var ic = CultureInfo.InvariantCulture;
        string V(Vector3 v) => string.Create(ic, $"[{v.X:0.####}, {v.Y:0.####}, {v.Z:0.####}]");
        string Hex(Vector3 c) => $"#{(int)Math.Clamp(c.X * 255, 0, 255):x2}{(int)Math.Clamp(c.Y * 255, 0, 255):x2}{(int)Math.Clamp(c.Z * 255, 0, 255):x2}";
        var sb = new StringBuilder(2048);
        sb.Append("{\"format\": \"").Append(ConstructParts.Format).Append("\", \"name\": ").Append(JsonSerializer.Serialize(name));
        var about = p.Flavor.Length > 0 ? Ascii(p.Flavor) : "";
        if (about.Length > 300)
            about = about[..300];
        sb.Append(", \"description\": ").Append(JsonSerializer.Serialize($"Weapon builder {PartDef.CategoryName(p.Category).ToLowerInvariant()}. {about}".Trim()));
        sb.Append(", \"role\": \"").Append(receiver ? "gun" : "gunpart").Append("\", \"tool\": \"wrench\"");
        sb.Append(string.Create(ic, $", \"attachSeconds\": {(receiver ? rules.ReceiverAttachSeconds : rules.AttachSeconds)}, \"mass\": {mass}, \"health\": {MathF.Round(rules.HealthBase + rules.HealthPerKg * mass)}, \"armour\": {rules.Armour}"));
        sb.Append(", \"material\": \"steel\", \"group\": \"Weapon parts\", \"section\": ").Append(JsonSerializer.Serialize(SectionOf(p.Category)));
        sb.Append(", \"boxes\": [{\"min\": ").Append(V(-half)).Append(", \"max\": ").Append(V(half)).Append(", \"hidden\": true}");
        foreach (var b in boxes)
            sb.Append(", {\"min\": ").Append(V(b.Min - centre)).Append(", \"max\": ").Append(V(b.Max - centre)).Append(", \"color\": \"").Append(Hex(b.Rgb)).Append("\", \"detail\": true}");
        sb.Append("], \"sockets\": [");
        sb.Append("{\"name\": \"right\", \"at\": ").Append(V(new Vector3(half.X, 0, 0))).Append(", \"dir\": \"+x\"}, ");
        sb.Append("{\"name\": \"left\", \"at\": ").Append(V(new Vector3(-half.X, 0, 0))).Append(", \"dir\": \"-x\"}, ");
        sb.Append("{\"name\": \"top\", \"at\": ").Append(V(new Vector3(0, half.Y, 0))).Append(", \"dir\": \"+y\"}, ");
        sb.Append("{\"name\": \"bottom\", \"at\": ").Append(V(new Vector3(0, -half.Y, 0))).Append(", \"dir\": \"-y\"}, ");
        sb.Append("{\"name\": \"back\", \"at\": ").Append(V(new Vector3(0, 0, half.Z))).Append(", \"dir\": \"+z\"}, ");
        sb.Append("{\"name\": \"front\", \"at\": ").Append(V(new Vector3(0, 0, -half.Z))).Append(", \"dir\": \"-z\"}]");
        if (receiver)
        {
            var act = p.Receiver!.StockAction is { } sa && Parts.Find(sa) is { Action: not null } ? sa : DefaultAction(p);
            sb.Append(", \"gun\": {\"receiver\": ").Append(JsonSerializer.Serialize(p.Name));
            if (act != null)
                sb.Append(", \"action\": ").Append(JsonSerializer.Serialize(act));
            sb.Append(string.Create(ic, $", \"kick\": {rules.ReceiverKick}}}"));
        }
        else
            sb.Append(", \"gunPart\": {\"part\": ").Append(JsonSerializer.Serialize(p.Name)).Append('}');
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>A receiver with no stock action: the first action (in the builder's order) its action socket takes.</summary>
    private static string? DefaultAction(PartDef rec)
    {
        var root = new BuildNode(rec);
        var s = Array.Find(rec.Sockets, x => x.Type == "action");
        if (s == null)
            return null;
        foreach (var a in Parts.All)
            if (a.Action != null && BuildRules.SocketWhy(root, s, a) == null)
                return a.Name;
        return null;
    }
}
