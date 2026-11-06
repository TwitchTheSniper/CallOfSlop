using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>What a design is (the key's first letter).</summary>
public enum OrdKind : byte
{
    Bomb,
    Missile,
    Spent, // a stage dropped in flight: its sections, nothing in them (OrdnanceFlight.cs)
}

/// <summary>A setting on a section ("configured pieces"): its value rides in the design's key.</summary>
public sealed record OrdSetting(string Key, string Label, string Unit, float Min, float Max, float Step, float Default);

/// <summary>A section part's <c>"ordnance"</c> block: what it adds to a design. Every number is at the reference diameter (<see cref="OrdnanceDesign.RefCm"/>).</summary>
public sealed class OrdSectionDef
{
    public string Slot = ""; // seeker, payload, fuse, stabilizer, body...
    public bool ForBomb = true, ForMissile = true;
    public float Length; // m
    public float Payload, Blast = 1f, Damage = 1f, Crater = 1f; // kg of filling; what it does to the blast's radius, damage and crater
    public float FuseDelay; // s after impact
    public OrdSetting[] Settings = [];
    public readonly Dictionary<string, float> Nums = new(StringComparer.Ordinal); // the flight stages' numbers
    public string Text = ""; // a section's kind in words (a seeker's, a fuel's)

    public float Num(string key, float or = 0) => Nums.GetValueOrDefault(key, or);

    private static readonly string[] Keys = ["slot", "for", "length", "payload", "blast", "damage", "crater", "fuseDelay", "settings", "kind", "numbers"];

    public static OrdSectionDef Parse(JsonElement b, string file)
    {
        ConstructParts.Known(b, Keys, $"{file}: \"ordnance\"");
        var o = new OrdSectionDef
        {
            Slot = ConstructParts.Str(ConstructParts.Need(b, "slot", file), file, "ordnance.slot"),
            Length = ConstructParts.Num(ConstructParts.Need(b, "length", file), file, "ordnance.length"),
        };
        ConstructParts.Range(o.Length, 0.02f, 50f, file, "ordnance.length");
        if (ConstructParts.Opt(b, "for") is { } w)
        {
            var who = ConstructParts.Str(w, file, "ordnance.for");
            if (who is not ("bomb" or "missile"))
                throw new ConstructPartException($"{file}: \"ordnance.for\" is bomb or missile (leave it out for both)");
            o.ForBomb = who == "bomb";
            o.ForMissile = who == "missile";
        }
        float Opt(string key, float or) => ConstructParts.Opt(b, key) is { } v ? ConstructParts.Num(v, file, $"ordnance.{key}") : or;
        o.Payload = Opt("payload", 0);
        o.Blast = Opt("blast", 1);
        o.Damage = Opt("damage", 1);
        o.Crater = Opt("crater", 1);
        o.FuseDelay = Opt("fuseDelay", 0);
        o.Text = ConstructParts.Opt(b, "kind") is { } t ? ConstructParts.Str(t, file, "ordnance.kind") : "";
        if (ConstructParts.Opt(b, "numbers") is { ValueKind: JsonValueKind.Object } nums)
            foreach (var p in nums.EnumerateObject())
                o.Nums[p.Name] = ConstructParts.Num(p.Value, file, $"ordnance.numbers.{p.Name}");
        if (ConstructParts.Opt(b, "settings") is { ValueKind: JsonValueKind.Array } arr)
        {
            var list = new List<OrdSetting>();
            foreach (var s in arr.EnumerateArray())
            {
                string Text(string k) => ConstructParts.Str(ConstructParts.Need(s, k, file), file, $"ordnance.settings.{k}");
                float Number(string k) => ConstructParts.Num(ConstructParts.Need(s, k, file), file, $"ordnance.settings.{k}");
                var one = new OrdSetting(Text("key"), Text("label"), ConstructParts.Opt(s, "unit") is { } u ? ConstructParts.Str(u, file, "ordnance.settings.unit") : "", Number("min"), Number("max"), Number("step"), Number("default"));
                if (one.Min >= one.Max || one.Default < one.Min || one.Default > one.Max || one.Step <= 0)
                    throw new ConstructPartException($"{file}: ordnance setting \"{one.Key}\": min, max, step and default don't add up");
                list.Add(one);
            }
            o.Settings = list.ToArray();
        }
        return o;
    }
}

/// <summary>One line of a design: a section part, how many in a row, and its settings' values.</summary>
public sealed record OrdPiece(ConstructPartDef Section, int Count, float[] Values)
{
    public float Value(string key)
    {
        var s = Section.Ord!.Settings;
        for (var k = 0; k < s.Length; k++)
            if (s[k].Key == key)
                return k < Values.Length ? Values[k] : s[k].Default;
        return 0;
    }
}

/// <summary>The numbers of a design, as the bench shows them and the part is made from.</summary>
public sealed class OrdStats
{
    public float Mass, Length, Diameter, Payload, Blast, Damage, Crater, Fuse;
    public int Sections, Items; // lines, and section parts it takes in survival
    public string Problem = ""; // why it won't be built ("" when it will)
    public readonly OrdFlightStats Flight = new(); // stages, steering, seeker, fuses and charges (OrdnanceFlight.cs)
}

/// <summary>
/// A modular bomb or missile (docs/sandbox/CONSTRUCTS.md, "Modular ordnance"): sections from nose to tail at one diameter. Its key is text
/// ("Ordnance:B25:Impact Fuse+Warhead HE*3+Tail Fins"): it is the part's name in saves and blueprints, as an extrusion's is, and it comes over the
/// wire in numbers (<see cref="Ordnance.Write"/>). There is no cap on length, stacks or diameter short of what the numbers can hold: a bigger weapon is only heavier.
/// </summary>
public sealed class OrdnanceDesign
{
    public const int RefCm = 25; // the diameter a section's numbers are at
    public const int MinCm = 5, MaxCm = 2000;
    public const int MaxCount = 1000; // of a section in a row (two bytes on the wire; a safety for the numbers, far past any airframe)

    public OrdKind Kind;
    public int DiameterCm = RefCm;
    public readonly List<OrdPiece> Pieces = [];

    /// <summary>The scale of everything against a section's reference diameter.</summary>
    public float Scale => DiameterCm / (float)RefCm;

    /// <summary>The key as text.</summary>
    public string Key()
    {
        var sb = new StringBuilder(Ordnance.Prefix).Append(Kind switch { OrdKind.Bomb => 'B', OrdKind.Missile => 'M', _ => 'S' }).Append(DiameterCm.ToString(CultureInfo.InvariantCulture)).Append(':');
        for (var i = 0; i < Pieces.Count; i++)
        {
            var p = Pieces[i];
            if (i > 0)
                sb.Append('+');
            sb.Append(p.Section.Name);
            var s = p.Section.Ord!.Settings;
            for (var k = 0; k < s.Length && k < p.Values.Length; k++)
                if (MathF.Abs(p.Values[k] - s[k].Default) > 1e-4f)
                    sb.Append('@').Append(s[k].Key).Append('=').Append(p.Values[k].ToString("0.###", CultureInfo.InvariantCulture));
            if (p.Count > 1)
                sb.Append('*').Append(p.Count.ToString(CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>A key back into a design, or null with why.</summary>
    public static OrdnanceDesign? Parse(string key, out string why)
    {
        why = "";
        if (!key.StartsWith(Ordnance.Prefix, StringComparison.OrdinalIgnoreCase) || key.Length < Ordnance.Prefix.Length + 3)
        {
            why = "not a design";
            return null;
        }
        var head = key.IndexOf(':', Ordnance.Prefix.Length);
        if (head < 0 || !int.TryParse(key.AsSpan(Ordnance.Prefix.Length + 1, head - Ordnance.Prefix.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var cm) || cm is < MinCm or > MaxCm)
        {
            why = "a bad diameter";
            return null;
        }
        var letter = char.ToUpperInvariant(key[Ordnance.Prefix.Length]);
        if (letter is not ('B' or 'M' or 'S'))
        {
            why = "neither a bomb nor a missile";
            return null;
        }
        var d = new OrdnanceDesign { Kind = letter switch { 'B' => OrdKind.Bomb, 'M' => OrdKind.Missile, _ => OrdKind.Spent }, DiameterCm = cm };
        foreach (var token in key[(head + 1)..].Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var text = token;
            var count = 1;
            var star = text.LastIndexOf('*');
            if (star >= 0)
            {
                if (!int.TryParse(text.AsSpan(star + 1), NumberStyles.None, CultureInfo.InvariantCulture, out count) || count is < 1 or > MaxCount)
                {
                    why = $"a bad count in \"{token}\"";
                    return null;
                }
                text = text[..star];
            }
            var bits = text.Split('@');
            if (ConstructParts.Find(bits[0]) is not { Ord: { } ord } def)
            {
                why = $"no section named \"{bits[0]}\"";
                return null;
            }
            var values = new float[ord.Settings.Length];
            for (var k = 0; k < values.Length; k++)
                values[k] = ord.Settings[k].Default;
            for (var b = 1; b < bits.Length; b++)
            {
                var kv = bits[b].Split('=');
                var at = kv.Length == 2 ? Array.FindIndex(ord.Settings, s => s.Key == kv[0]) : -1;
                if (at < 0 || !float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !float.IsFinite(v))
                {
                    why = $"a bad setting in \"{token}\"";
                    return null;
                }
                values[at] = Math.Clamp(v, ord.Settings[at].Min, ord.Settings[at].Max);
            }
            d.Pieces.Add(new OrdPiece(def, count, values));
        }
        return d;
    }

    /// <summary>The numbers. Mass goes with the cube of the scale, the blast with the cube root of the filling and the damage with its square root: the curve the bench shows.</summary>
    public OrdStats Stats()
    {
        var s = Scale;
        var cube = s * s * s;
        var st = new OrdStats { Diameter = DiameterCm / 100f, Sections = Pieces.Count };
        float mass = 0, payload = 0, blastK = 0, damageK = 0, craterK = 0, fuse = 0;
        var fused = false;
        foreach (var p in Pieces)
        {
            var o = p.Section.Ord!;
            mass += (p.Section.Mass + OrdFlightStats.SettingMass(p)) * p.Count * cube;
            st.Length += o.Length * p.Count * s;
            st.Items += p.Count * Math.Max(1, (int)MathF.Ceiling(cube));
            if (o.Payload > 0)
            {
                var kg = o.Payload * p.Count * cube;
                payload += kg;
                blastK += o.Blast * kg;
                damageK += o.Damage * kg;
                craterK += o.Crater * kg;
            }
            if (o.Slot == "fuse")
            {
                fused = true;
                fuse = MathF.Max(fuse, p.Section.Ord!.Settings.Length > 0 && p.Value("delay") > 0 ? p.Value("delay") : o.FuseDelay);
            }
        }
        st.Mass = mass;
        st.Payload = payload;
        st.Fuse = fuse;
        if (payload > 0)
            (st.Blast, st.Damage, st.Crater) = Charge(blastK, damageK, craterK, payload);
        OrdFlightStats.Fill(this, st);
        if (Pieces.Count == 0)
            st.Problem = "add a section";
        else if (Kind == OrdKind.Spent)
            st.Problem = mass > ConstructParts.MaxPartMass ? "too heavy" : "";
        else if (st.Flight.Problem.Length > 0)
            st.Problem = st.Flight.Problem;
        else if (payload <= 0)
            st.Problem = "it needs a payload";
        else if (!fused)
            st.Problem = "it needs a fuse";
        else if (mass > ConstructParts.MaxPartMass)
            st.Problem = "too heavy for the numbers to hold (50,000 t)";
        return st;
    }

    /// <summary>A charge's blast radius, damage and crater from its filling's weighted sums (kg): the curve the bench draws.</summary>
    public static (float Blast, float Damage, float Crater) Charge(float blastK, float damageK, float craterK, float payload) =>
        payload <= 0 ? (0, 0, 0) : (BlastPerRoot * MathF.Cbrt(blastK), DamagePerRoot * MathF.Sqrt(damageK), BlastPerRoot * MathF.Cbrt(blastK) * CraterShare * (craterK / payload));

    private const float BlastPerRoot = 2.84f, DamagePerRoot = 19f, CraterShare = 0.36f; // (120 kg of filling: the Aerial Bomb's 14 m and about its 260)
}

/// <summary>The run-time parts designs become: one def a key, interned like an extrusion's, in the same id space (<see cref="Extrusion.Adopt"/>).</summary>
public static class Ordnance
{
    public const string Prefix = "Ordnance:";
    private const float LugHeight = 0.05f;
    private const byte WireKind = 0x40; // (Missile: this plus one; only to tell a garbled byte)

    private static readonly Dictionary<string, ConstructPartDef> Interned = new(StringComparer.Ordinal);

    /// <summary>The part for a key, made the first time. Null: a bad key, a design that doesn't add up, or the run-time table is full.</summary>
    public static ConstructPartDef? Parse(string key)
    {
        lock (Interned)
            if (Interned.TryGetValue(key, out var have))
                return have;
        return OrdnanceDesign.Parse(key, out _) is { } d ? Def(d) : null;
    }

    public static ConstructPartDef? Def(OrdnanceDesign d)
    {
        var key = d.Key(); // (the canonical key, however it was spelt)
        lock (Interned)
        {
            if (Interned.TryGetValue(key, out var have))
                return have;
            var st = d.Stats();
            if (st.Problem.Length > 0)
                return null;
            var def = Build(d, st, key);
            if (!Extrusion.Adopt(def))
                return null;
            Interned[key] = def;
            return def;
        }
    }

    private static ConstructPartDef Build(OrdnanceDesign d, OrdStats st, string key)
    {
        var s = d.Scale;
        float r = st.Diameter * 0.5f, half = st.Length * 0.5f;
        var boxes = new List<CPartBox>(ConstructParts.MaxBoxes)
        {
            new(Vector3.Zero, new Vector3(r, r, half), new Vector3(0.3f, 0.34f, 0.2f), Hidden: true),
            new(new Vector3(0, r + LugHeight * 0.5f, 0), new Vector3(0.05f, LugHeight * 0.5f + 0.01f, 0.08f), new Vector3(0.16f), Hidden: true),
        };
        var solid = boxes.Count;
        var budget = ConstructParts.MaxBoxes - solid;
        var drawn = 0;
        foreach (var p in d.Pieces)
            foreach (var b in p.Section.Boxes)
                drawn += !b.Hidden ? 1 : 0;
        var firstOnly = drawn > budget; // (each box is a draw call: past the budget a section is its first box)
        var z = -half;
        foreach (var p in d.Pieces)
        {
            var len = p.Section.Ord!.Length * p.Count * s;
            var stretch = p.Count; // (a stack draws as one long section)
            foreach (var b in p.Section.Boxes)
            {
                if (b.Hidden)
                    continue;
                if (boxes.Count >= ConstructParts.MaxBoxes)
                    break;
                boxes.Add(b with
                {
                    Center = new Vector3(b.Center.X * s, b.Center.Y * s, z + len * 0.5f + b.Center.Z * s * stretch),
                    Half = new Vector3(b.Half.X * s, b.Half.Y * s, b.Half.Z * s * stretch),
                    Detail = true,
                });
                if (firstOnly)
                    break;
            }
            z += len;
        }
        var def = new ConstructPartDef
        {
            Name = key,
            Description = Label(d, st),
            Group = "Weapons",
            Section = "Ordnance",
            Role = PartRole.Frame,
            Tool = ConstructTool.Welder,
            AttachSeconds = 2,
            Mass = MathF.Max(0.5f, st.Mass),
            Health = Math.Clamp(st.Mass * 0.8f, 20, ConstructParts.MaxPartHealth),
            Armour = 1,
            Material = "steel",
            PhysicsOnly = true,
            PhysicsWhy = "built at a bench",
            Stage = true, // (never offered: it comes off a bench)
            OnBreak = PartBreak.Detach,
            CentreOptOut = true,
            Friction = 0.5f,
            IsOrdnance = true,
            ExHash = Fnv(key),
            Boxes = boxes.ToArray(),
            SolidBoxes = solid,
            BoundsMin = new Vector3(-r, -r, -half),
            BoundsMax = new Vector3(r, r + LugHeight, half),
            Sockets = [new CSocket("lug", new Vector3(0, r + LugHeight, 0), ConstructParts.DirOf("+y", "", ""), SocketKind.Struct)],
            Explosive = d.Kind == OrdKind.Spent ? null : new PropBreakable { ExplodeRadius = st.Blast, ExplodeDamage = st.Damage, ExplodeFuse = MathF.Max(0.02f, st.Fuse), ExplodeCrater = st.Crater, ExplodeKnockback = 12 },
            OrdDesign = d,
            OrdStats = st,
        };
        SurfaceRules.Apply(def);
        return def;
    }

    /// <summary>A design in words (its look line).</summary>
    public static string Label(OrdnanceDesign d, OrdStats st) => d.Kind == OrdKind.Spent
        ? $"A spent stage, {st.Diameter * 100:0} cm by {st.Length:0.##} m, {st.Mass:0} kg"
        : $"{(d.Kind == OrdKind.Bomb ? "Bomb" : "Missile")}, {st.Diameter * 100:0} cm by {st.Length:0.##} m, {st.Mass:0} kg, {st.Blast:0.#} m blast{st.Flight.Label}";

    private static uint Fnv(string s)
    {
        var h = 2166136261u;
        foreach (var ch in s)
            h = (h ^ ch) * 16777619u;
        return h;
    }

    // ---- The wire -----------------------------------------------------------------------------------------------------

    /// <summary>After <see cref="Extrusion.WireMark"/> and <see cref="Extrusion.OrdnanceWire"/>: the design in numbers (a few bytes a section; section ids are the part files', the same on every PC).</summary>
    public static void Write(PacketWriter w, ConstructPartDef def)
    {
        var d = OrdnanceDesign.Parse(def.Name, out _)!;
        w.U8((byte)(WireKind + (byte)d.Kind)).U16((ushort)d.DiameterCm).U16((ushort)d.Pieces.Count);
        foreach (var p in d.Pieces)
        {
            w.U16(p.Section.Id).U16((ushort)p.Count).U8((byte)p.Values.Length);
            foreach (var v in p.Values)
                w.F32(v);
        }
    }

    /// <summary>The design from the wire, as this machine's part id. A stand-in, never a dropped packet, if the table is full.</summary>
    public static ushort Read(ref PacketReader r)
    {
        var kind = r.U8() - WireKind;
        if (kind is < 0 or > 2)
            throw new MalformedPacketException($"an ordnance design of kind {kind}");
        var d = new OrdnanceDesign { Kind = (OrdKind)kind, DiameterCm = r.U16() };
        int n = r.U16();
        for (var i = 0; i < n; i++)
        {
            var id = r.U16();
            int count = r.U16();
            int nv = r.U8();
            if (ConstructParts.Get(id) is not { Ord: not null } section || nv > 16)
                throw new MalformedPacketException($"an ordnance section of part {id}");
            var values = new float[nv];
            for (var k = 0; k < nv; k++)
                values[k] = r.F32();
            d.Pieces.Add(new OrdPiece(section, count, values));
        }
        if (Def(d) is { } def)
            return def.Id;
        if (Extrusion.DefCount >= Extrusion.MaxDefs)
            return ConstructParts.Need("Frame Beam").Id;
        throw new MalformedPacketException("an ordnance design that doesn't add up");
    }
}
