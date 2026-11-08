using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;

namespace CallOfSlop;

/// <summary>
/// Body styles phase 4 (docs/vehiclekit/BODY-STYLES.md, section 10): a faction's exterior on its lofted rooms and cabs, made at run time and named by its key as a
/// lofted bay is. A <b>trim</b> (<c>Hull Loft (trim...)</c>) is cut for one bay's side: it stands on the bay's waist socket and follows its two sections a few
/// millimetres off the skin (welded plates, cage bars, slat armour, stowage bins, a smooth fairing and vents, a signboard, clinker laps and a strake, a two-tone
/// livery band with a chrome strip), so a room reads as its faction's even in one colour. A <b>deco</b> (<c>Hull Loft (deco...)</c>) is a small fitting sized by its
/// key: a roof sign, a destination board, a radome, a bonnet mascot, a chrome grille, a roof basket, a jerry can. Every face keeps 2 mm or more from any other's
/// plane (the z-fighting rule), and the solid box a part stands by is a small one at its socket (what it draws is detail).
/// </summary>
public static partial class HullLoft
{
    public const string TrimWord = "trim", DecoWord = "deco";

    /// <summary>
    /// A trim's kinds: <c>p</c> welded plates, <c>c</c> cage bars over the upper wall (Bandit); <c>s</c> slat armour, <c>b</c> stowage bins (Militia); <c>f</c> a
    /// smooth skirt fairing, a stripe and vents (IT); <c>g</c> a signboard and a kick strip (Scam); <c>r</c> clinker laps and a strake (Highland); <c>l</c> a livery
    /// band and chrome strips (British).
    /// </summary>
    public const string TrimKinds = "pcsbfgrl";

    /// <summary>A deco's kinds: <c>S</c> a roof sign, <c>D</c> a destination board, <c>R</c> a radome, <c>O</c> a bonnet mascot, <c>G</c> a chrome grille, <c>B</c> a roof basket, <c>J</c> a jerry can.</summary>
    public const string DecoKinds = "SDROGBJ";

    private const string Chrome = "#cfd4d8", Wood = "#5b3a22", Dark = "#2b2e31", Lit = "#f0b43c", Dome = "#e8e6df";
    private const float TrimGap = 0.011f; // (a trim's inner face this far off the skin's outer face, the skin T thick about its section: 6 mm and more however its place rounds to the centimetre)

    /// <summary>A trim's shape: its kind, a variant (0 to 9) for where its pieces go, the bay's length, the lowest it may come (the wheels' room), and the bay's sections in the trim's own order (the -x side's is the bay's back first).</summary>
    public sealed record TrimShape(char Kind, int Variant, float Length, float Low, Section A, Section B)
    {
        public string Key()
        {
            var sb = new StringBuilder(Prefix).Append(TrimWord).Append(Kind).Append((char)('0' + Variant)).Append(' ');
            Num(sb, Length);
            Num(sb, Low);
            return sb.Append(' ').Append(A.Key()).Append(' ').Append(B.SameAs(A) ? "=" : B.Key()).Append(')').ToString();
        }

        /// <summary>The bay's waist socket the trim stands on, in the bay's frame (+x side): as <see cref="Json"/> puts "+x0".</summary>
        public Vector3 Pad
        {
            get
            {
                var y = (A.WaistY + B.WaistY) / 2;
                return new Vector3((MathF.Max(A.XAt(y), 0) + MathF.Max(B.XAt(y), 0)) / 2, y, Length / 2);
            }
        }
    }

    /// <summary>A deco: its kind and its numbers (sizes and offsets in metres from its socket; what each means is the kind's).</summary>
    public sealed record DecoShape(char Kind, float[] N)
    {
        public string Key()
        {
            var sb = new StringBuilder(Prefix).Append(DecoWord).Append(Kind).Append(' ');
            foreach (var v in N)
                Num(sb, v);
            return sb.Append(')').ToString();
        }

        public static int Count(char kind) => kind switch { 'S' => 4, 'D' => 4, 'R' => 2, 'O' => 2, 'G' => 3, 'B' => 2, _ => 1 };
    }

    public static TrimShape? TrimOf(string name)
    {
        if (!name.StartsWith(Prefix + TrimWord, StringComparison.Ordinal) || !name.EndsWith(')'))
            return null;
        var words = name[(Prefix.Length + TrimWord.Length)..^1].Split(' ');
        if (words.Length != 4 || words[0].Length != 2 || !TrimKinds.Contains(words[0][0]) || words[0][1] is < '0' or > '9' || Nums(words[1], 2) is not { } n || Section.Parse(words[2]) is not { } a)
            return null;
        var b = words[3] == "=" ? a : Section.Parse(words[3]);
        if (b == null || b.P.Length != a.P.Length || n[0] is < 0.5f or > 2f || n[1] is < 0 or > 1.2f)
            return null;
        return new TrimShape(words[0][0], words[0][1] - '0', n[0], n[1], a, b);
    }

    /// <summary>A trim's or a fitting's key (dressing: not part of what the kit lofted for a body's shape, so the driver's view check leaves it out).</summary>
    public static bool IsDressing(string name) => name.StartsWith(Prefix + TrimWord, StringComparison.Ordinal) || name.StartsWith(Prefix + DecoWord, StringComparison.Ordinal);

    public static DecoShape? DecoOf(string name)
    {
        if (!name.StartsWith(Prefix + DecoWord, StringComparison.Ordinal) || !name.EndsWith(')'))
            return null;
        var words = name[(Prefix.Length + DecoWord.Length)..^1].Split(' ');
        if (words.Length != 2 || words[0].Length != 1 || !DecoKinds.Contains(words[0][0]) || Nums(words[1], DecoShape.Count(words[0][0])) is not { } n)
            return null;
        var ok = words[0][0] switch
        {
            'S' => n[0] is >= 0.3f and <= 1.2f && n[1] is >= 0.05f and <= 0.6f && n[2] is >= 0.15f and <= 0.8f && MathF.Abs(n[3]) <= 1.5f,
            'D' => n[0] is >= 0.3f and <= 1.2f && n[1] is >= 0.12f and <= 0.5f && MathF.Abs(n[2]) <= 2.5f && MathF.Abs(n[3]) <= 0.5f,
            'R' => n[0] is >= 0.15f and <= 0.6f && n[1] is >= 0.1f and <= 0.6f,
            'O' => MathF.Abs(n[0]) <= 1.5f && MathF.Abs(n[1]) <= 0.5f,
            'G' => n[0] is >= 0.15f and <= 0.8f && n[1] >= -0.5f && n[2] > n[1] + 0.12f && n[2] <= 1f,
            'B' => n[0] is >= 0.2f and <= 1f && n[1] is >= 0.2f and <= 1.2f,
            _ => n[0] is 0,
        };
        return ok ? new DecoShape(words[0][0], n) : null;
    }

    /// <summary>A jerry can, the Militia's trophy: a part a style's slot can name (its key has no numbers to roll).</summary>
    public static readonly string JerryCan = new DecoShape('J', [0]).Key();

    private static (bool Ok, ConstructPartDef? Def)? MakeTrim(string name)
    {
        string? json;
        if (name.StartsWith(Prefix + TrimWord, StringComparison.Ordinal))
            json = TrimOf(name) is { } t ? TrimJson(name, t) : null;
        else if (name.StartsWith(Prefix + DecoWord, StringComparison.Ordinal))
            json = DecoOf(name) is { } d ? DecoJson(name, d) : null;
        else
            return null;
        if (json == null)
            return (false, null);
        try
        {
            var def = Adopt(ConstructParts.Parse(json, name), name);
            return (def != null, def);
        }
        catch (ConstructPartException e)
        {
            Console.WriteLine($"[vehicles] {name}: refused, {e.Message}");
            return (false, null);
        }
    }

    // ---- Following a section --------------------------------------------------------------------------------------

    /// <summary>A place along a section's rows: row k, a fraction f of the way from point k to k + 1 (the same place on the bay's other section is the loft's own).</summary>
    private readonly record struct RowAt(int K, float F)
    {
        public float T => K + F;
    }

    /// <summary>Where height y falls on a section's rows (the first rising row that reaches it); past the top: the last row's end.</summary>
    private static RowAt Locate(Section s, float y)
    {
        for (var k = 0; k + 1 < s.P.Length; k++)
            if (y >= s.P[k].Y && y <= s.P[k + 1].Y && s.P[k + 1].Y > s.P[k].Y)
                return new RowAt(k, (y - s.P[k].Y) / (s.P[k + 1].Y - s.P[k].Y));
        return y < s.P[0].Y ? new RowAt(0, 0) : new RowAt(s.P.Length - 2, 1);
    }

    private static Vector2 PointAt(Section s, RowAt r) => Vector2.Lerp(s.P[r.K], s.P[Math.Min(r.K + 1, s.P.Length - 1)], r.F);

    private static RowAt FromT(float t)
    {
        var k = (int)MathF.Floor(t);
        return new RowAt(k, t - k);
    }

    /// <summary>The section between two of its places as a path (its points and, at each, the offset direction: the row's normal at the ends, the mitre between).</summary>
    private static (List<Vector2> P, List<Vector2> M) PathOf(Section s, RowAt from, RowAt to)
    {
        var (p, m) = (new List<Vector2>(), new List<Vector2>());
        p.Add(PointAt(s, from));
        m.Add(RowNormal(s.P[from.K], s.P[from.K + 1]));
        for (var k = from.K + 1; k <= to.K && k < s.P.Length - 1; k++)
        {
            if (k == to.K && to.F < 1e-4f)
                break;
            p.Add(s.P[k]);
            m.Add(Mitre(RowNormal(s.P[k - 1], s.P[k]), RowNormal(s.P[k], s.P[k + 1])));
        }
        p.Add(PointAt(s, to));
        m.Add(RowNormal(s.P[to.K], s.P[to.K + 1]));
        return (p, m);
    }

    /// <summary>The loft's section at a fraction u of the bay's length (point by point: the same row at every station).</summary>
    private static Section Between(Section a, Section b, float u) => u <= 0 ? a : u >= 1 ? b : new Section(a.P.Zip(b.P, (x, y) => Vector2.Lerp(x, y, u)).ToArray());

    /// <summary>
    /// A band on the bay's side from row place <paramref name="t0"/> to <paramref name="t1"/> (along the rows: <see cref="RowAt.T"/>), from z0 to z1 along the bay, its middle
    /// <paramref name="d"/> out from the section and <paramref name="t"/> thick, each row a facet mitred to the next (so their ends meet on an edge).
    /// Coordinates are the bay's; <paramref name="shift"/> moves the result into the part's frame. Returns its area.
    /// </summary>
    private static float Band(Builder p, TrimShape s, float t0, float t1, float z0, float z1, float d, float t, string colour, bool paint, Vector3 shift)
    {
        if (t1 - t0 < 1e-3f || z1 - z0 < 0.01f)
            return 0;
        var (r0, r1) = (FromT(t0), FromT(t1));
        if (r1.F < 1e-4f && r1.K > r0.K)
            r1 = new RowAt(r1.K - 1, 1);
        var (sa, sb) = (Between(s.A, s.B, z0 / s.Length), Between(s.A, s.B, z1 / s.Length));
        var (pa, ma) = PathOf(sa, r0, r1);
        var (pb, mb) = PathOf(sb, r0, r1);
        var area = 0f;
        for (var i = 0; i + 1 < pa.Count && i + 1 < pb.Count; i++)
        {
            var (a0, a1, b0, b1) = (pa[i] + ma[i] * d, pa[i + 1] + ma[i + 1] * d, pb[i] + mb[i] * d, pb[i + 1] + mb[i + 1] * d);
            if (Vector2.Distance(a0, a1) < 0.004f || Vector2.Distance(b0, b1) < 0.004f)
                continue;
            var n = RowNormal(pa[i], pa[i + 1]);
            Vector3[] q = [new Vector3(a0, z0) + shift, new Vector3(a1, z0) + shift, new Vector3(b1, z1) + shift, new Vector3(b0, z1) + shift];
            Vector3[] off = [new Vector3(ma[i], 0), new Vector3(ma[i + 1], 0), new Vector3(mb[i + 1], 0), new Vector3(mb[i], 0)];
            var (lo, hi) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
            for (var c = 0; c < 4; c++)
                foreach (var sg in (float[])[-1, 1])
                    (lo, hi) = (Vector3.Min(lo, q[c] + off[c] * (sg * t / 2)), Vector3.Max(hi, q[c] + off[c] * (sg * t / 2)));
            if (hi.X - lo.X < 0.0105f || hi.Y - lo.Y < 0.0105f || hi.Z - lo.Z < 0.0105f)
                continue; // (a sliver of a row: a box is a centimetre or more each way)
            p.Facet(q, new Vector3(n, 0), colour, t, paint, off);
            area += Vector2.Distance(a0, a1) * (z1 - z0);
        }
        return area;
    }

    /// <summary>A small deterministic roll from a trim's key (its variant and length), for where plates and bins go.</summary>
    private sealed class Roll(int seed)
    {
        private uint _s = (uint)seed * 2654435761u + 0x9e3779b9u;

        public float Next()
        {
            _s = _s * 1664525u + 1013904223u;
            return (_s >> 8) / 16777216f;
        }

        public float Between(float a, float b) => a + (b - a) * Next();
    }

    // ---- The trim -----------------------------------------------------------------------------------------------

    private static string TrimJson(string name, TrimShape s)
    {
        var p = new Builder();
        // Where the trim's frame sits on its bay: the waist socket. A part's place is kept to the centimetre (ConstructWorld.Quantise), so the trim may sit up to half
        // of one off it either way: its faces keep 6 mm and more off the skin (TrimGap), and its solid box 6 mm off the bay's slabs (ClearOf).
        var pad = s.Pad;
        var shift = -pad;
        var (a, b, len) = (s.A, s.B, s.Length);
        // The wall: from the trim's lowest (the wheels' room) up to where either section's side turns into its roof (a row leaning more than 57 degrees off upright).
        float Tp(float y) => MathF.Max(Locate(a, y).T, Locate(b, y).T);
        var top = (float)(a.P.Length - 1);
        for (var k = 0; k + 1 < a.P.Length; k++)
            if ((a.P[k].Y + a.P[k + 1].Y) / 2 > 0.8f && (RowNormal(a.P[k], a.P[k + 1]).X < 0.55f || RowNormal(b.P[k], b.P[k + 1]).X < 0.55f))
            {
                top = k;
                break;
            }
        float Y(float t) => PointAt(a, FromT(MathF.Min(t, a.P.Length - 1.0001f))).Y; // (a place's height on the front section)
        var wallTop = MathF.Min(Y(top), MathF.Min(a.Top, b.Top)) - 0.04f;
        var low = MathF.Max(0.05f, s.Low);
        var waist = pad.Y;
        float Tc(float y) => MathF.Min(Tp(y), top - 0.01f); // (a height's place, never past the wall)
        var skin = T / 2 + TrimGap; // (a trim's inner face's least distance out: the skin's outer face and the gap)
        var area = 0f;
        var roll = new Roll(s.Variant * 131 + Cm(len) + s.Kind);
        void B(float y0, float y1, float z0, float z1, float inner, float thick, string colour, bool paint)
        {
            if (y1 - y0 >= 0.01f && y0 >= low - 1e-4f && y1 <= wallTop + 1e-4f)
                area += Band(p, s, Tc(y0), Tc(y1), z0, z1, inner + thick / 2, thick, colour, paint, shift);
        }
        const float ends = 0.015f; // (a trim stops this short of its bay's ends: the next bay's trim is its own part)
        switch (s.Kind)
        {
            case 'p': // Bandit: welded plates, two or three a side, never on each other's planes (each a little further out than the last)
            {
                var slots = new List<(float Y0, float Y1)> { (low + 0.04f, MathF.Min(waist - 0.08f, low + 0.5f)), (MathF.Max(low + 0.55f, waist + 0.05f), MathF.Min(1.45f, wallTop)), (1.5f, wallTop) };
                var n = 2 + (int)(roll.Next() * 2);
                for (var i = 0; i < slots.Count && n > 0; i++)
                {
                    var (y0, y1) = slots[(i + s.Variant) % slots.Count];
                    if (y1 - y0 < 0.22f)
                        continue;
                    var h = MathF.Min(y1 - y0, roll.Between(0.25f, 0.5f));
                    var yb = y0 + roll.Next() * (y1 - y0 - h);
                    var l = MathF.Min(len - 0.1f, roll.Between(0.35f, 0.7f));
                    var zb = 0.05f + roll.Next() * (len - 0.1f - l);
                    B(yb, yb + h, zb, zb + l, skin + 0.004f * i, 0.012f, Steel, true);
                    n--;
                }
                break;
            }
            case 'c': // Bandit: cage bars over the upper wall, on rails stood off on brackets
            {
                var (yb, yt) = (MathF.Max(low, MathF.Max(1.05f, waist + 0.15f)), wallTop - 0.02f);
                if (yt - yb < 0.4f || len < 0.7f)
                    break;
                foreach (var (y0, y1) in new[] { (yb, yb + 0.03f), (yt - 0.03f, yt) })
                {
                    B(y0, y1, ends + 0.01f, len - ends - 0.01f, 0.06f, 0.03f, Bars, true);
                    foreach (var z in new[] { 0.05f, len - 0.05f })
                        B(MathF.Max(low, y0 - 0.005f), MathF.Min(wallTop, y1 + 0.005f), z - 0.02f, z + 0.02f, skin, 0.05f, Bars, true);
                }
                var count = Math.Max(2, (int)((len - 0.2f) / 0.18f));
                for (var j = 0; j < count; j++)
                {
                    var z = 0.1f + (len - 0.2f) * (j + 0.5f) / count;
                    B(yb + 0.005f, yt - 0.005f, z - 0.011f, z + 0.011f, 0.049f, 0.022f, Bars, true);
                }
                break;
            }
            case 's': // Militia: slat armour, slats on three posts well off the skin
            {
                var (ys, ye) = (MathF.Max(low, 0.15f) + 0.02f, MathF.Min(wallTop, 1.45f));
                if (ye - ys < 0.35f)
                    break;
                var y = ys;
                var slats = 0;
                for (; y + 0.045f <= ye; y += 0.11f, slats++)
                    B(y, y + 0.045f, ends, len - ends, 0.14f, 0.02f, Steel, true);
                foreach (var z in new[] { 0.12f, len / 2, len - 0.12f })
                    B(ys - 0.015f, MathF.Min(wallTop, y - 0.11f + 0.06f), z - 0.025f, z + 0.025f, skin, 0.145f - skin, Dark, false);
                break;
            }
            case 'b': // Militia: stowage bins on the lower wall, lidded and strapped
            {
                var (y0, y1) = (MathF.Max(low, 0.3f), MathF.Max(low, 0.3f) + 0.32f);
                if (y1 + 0.02f > MathF.Min(wallTop, waist + 0.25f))
                    break;
                var bl = MathF.Min(len - 0.16f, roll.Between(0.42f, 0.6f));
                var spots = new List<float> { 0.08f };
                if (len - 0.16f - 2 * bl >= 0.1f && roll.Next() < 0.7f)
                    spots.Add(len - 0.08f - bl);
                else
                    spots[0] = (len - bl) / 2;
                foreach (var z0 in spots)
                {
                    B(y0, y1 - 0.03f, z0, z0 + bl, skin, 0.23f, Steel, true);
                    B(y1 - 0.045f, y1 + 0.012f, z0 - 0.008f, z0 + bl + 0.008f, skin - 0.004f, 0.25f, Steel, true); // (the lid, a little wider than the box and over it)
                    foreach (var zs in new[] { z0 + bl * 0.25f, z0 + bl * 0.75f })
                        B(y0 + 0.01f, y1 - 0.06f, zs - 0.02f, zs + 0.02f, skin + 0.22f, 0.016f, Dark, false);
                }
                break;
            }
            case 'f': // IT: a smooth fairing over the lower wall, a stripe along the waist, vents at the back
            {
                B(low + 0.02f, MathF.Min(waist - 0.06f, low + 0.36f), ends, len - ends, skin, 0.014f, Steel, true);
                if (waist + 0.16f < wallTop)
                    B(waist + 0.1f, waist + 0.14f, ends + 0.004f, len - ends - 0.004f, skin, 0.011f, "#0f7bd8", false);
                if (len >= 0.9f && wallTop > 1.7f && s.Variant % 2 == 0)
                    for (var i = 0; i < 4; i++)
                        B(1.5f + 0.045f * i, 1.525f + 0.045f * i, len * 0.55f, len * 0.55f + 0.24f, skin, 0.012f, Dark, false);
                break;
            }
            case 'g': // Scam: a signboard on brackets (painted the faction's loudest), a kick strip low down
            {
                B(low + 0.02f, low + 0.14f, ends, len - ends, skin, 0.012f, Dark, false);
                var (y0, y1) = (MathF.Max(1.15f, waist + 0.25f), MathF.Min(1.75f, wallTop - 0.03f));
                if (y1 - y0 < 0.28f || len < 0.75f)
                    break;
                B(y0, y1, 0.1f, len - 0.1f, 0.039f, 0.022f, Steel, true);
                foreach (var z in new[] { 0.2f, len - 0.2f })
                    B(y0 + 0.06f, y1 - 0.06f, z - 0.03f, z + 0.03f, skin + 0.002f, 0.024f, Dark, false);
                break;
            }
            case 'r': // Highland: clinker laps over the lower hull, each over the one below, and a wooden strake along the waist
            {
                var y = low + 0.03f;
                for (var i = 0; y + 0.12f <= MathF.Min(waist - 0.04f, wallTop) && i < 5; i++, y += 0.18f)
                {
                    var y1 = MathF.Min(y + 0.22f, MathF.Min(waist - 0.04f, wallTop));
                    B(y, y1, ends + 0.003f * i, len - ends - 0.003f * i, skin + 0.008f * i, 0.012f, i % 2 == 0 ? "#6e4b2a" : "#5c3d22", false); // (varnished planks, each over the one below)
                }
                B(waist - 0.035f, waist + 0.035f, ends, len - ends, skin, 0.045f, Wood, false);
                if (wallTop - 0.1f > waist + 0.5f)
                    B(wallTop - 0.08f, wallTop - 0.05f, ends, len - ends, skin, 0.03f, Wood, false);
                break;
            }
            case 'l': // British: the lower panel in a second colour, a chrome rubbing strip over it and one under the windows
            {
                var bandTop = MathF.Min(waist - 0.02f, wallTop);
                B(low + 0.02f, bandTop, ends, len - ends, skin, 0.011f, Steel, true);
                B(bandTop - 0.012f, bandTop + 0.02f, ends + 0.004f, len - ends - 0.004f, skin + 0.004f, 0.02f, Chrome, false);
                if (wallTop > 1.6f)
                    B(1.5f, 1.525f, ends, len - ends, skin, 0.016f, Chrome, false);
                break;
            }
        }
        if (p.Boxes.Count == 0)
            return null!; // (nothing fits this bay's side: no part)
        // What it stands by. Its socket is the bay's waist socket, on the section's line in the middle of the skin: a lug from there out through the skin is the
        // face it stands on, and its solid box is out past the skin of the rows about the waist (the bay's own slabs stand just inside the skin, along each row).
        Cube(p, new Vector3(0, -0.015f, -0.02f), new Vector3(0.035f, 0.015f, 0.02f), Dark, false);
        if (StandSpot(a, b, pad) is not { } spot)
            return null!; // (every stretch of wall stands out past its waist socket: nowhere to stand it by)
        p.Boxes.Add(new JsonObject { ["min"] = V(0, spot.Y - 0.01f, -0.01f), ["max"] = V(0.02f, spot.Y + 0.01f, 0.01f), ["hidden"] = true });
        if (spot.Out > 0)
            p.Boxes.Add(new JsonObject { ["min"] = V(spot.Out, -0.01f, -0.01f), ["max"] = V(spot.Out + 0.02f, 0.01f, 0.01f), ["hidden"] = true }); // (at the socket's height, out past the slabs there)
        p.Sock("base", Vector3.Zero, "-x");
        var what = s.Kind switch
        {
            'p' => "welded plates", 'c' => "cage bars", 's' => "slat armour", 'b' => "stowage bins", 'f' => "a fairing and vents", 'g' => "a signboard", 'r' => "clinker laps and a strake", _ => "a livery band and chrome strips",
        };
        return PartJson(name, $"A hull bay's side dressed with {what}, cut for one bay by the vehicle kit. Hangs on the bay's waist socket.", "Hull trim", MathF.Round(Math.Clamp(area * 9f, 2, 80), 1), p);
    }

    /// <summary>
    /// The hidden slabs <see cref="Sides"/> lays along a bay's +x side (a wall row in steps of 30 cm, each at its corners' mean, 1 to 5 cm inside; a flat row's
    /// under it), as rectangles in x and y (each runs the bay's length).
    /// </summary>
    private static List<(float X0, float X1, float Y0, float Y1)> Slabs(Section a, Section b)
    {
        var list = new List<(float, float, float, float)>();
        for (var k = 0; k + 1 < a.P.Length; k++)
        {
            var (a0, a1, b0, b1) = (a.P[k], a.P[k + 1], b.P[k], b.P[k + 1]);
            var steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(a0, a1) / 0.3f));
            for (var i = 0; i < steps; i++)
            {
                var (c0, c1) = (Vector2.Lerp(a0, a1, i / (float)steps), Vector2.Lerp(a0, a1, (i + 1) / (float)steps));
                var (d0, d1) = (Vector2.Lerp(b0, b1, i / (float)steps), Vector2.Lerp(b0, b1, (i + 1) / (float)steps));
                if (MathF.Abs(c1.Y - c0.Y) >= MathF.Abs(c1.X - c0.X))
                {
                    var x = (c0.X + c1.X + d0.X + d1.X) / 4;
                    list.Add((x - 0.05f, x - 0.01f, MathF.Min(c0.Y, d0.Y), MathF.Min(c1.Y, d1.Y)));
                }
                else
                {
                    var y = MathF.Min(MathF.Min(c0.Y, c1.Y), MathF.Min(d0.Y, d1.Y));
                    list.Add((MathF.Min(MathF.Min(c0.X, c1.X), MathF.Min(d0.X, d1.X)), MathF.Max(MathF.Max(c0.X, c1.X), MathF.Max(d0.X, d1.X)), y - 0.05f, y - 0.01f));
                }
            }
        }
        return list;
    }

    /// <summary>
    /// Is a 2 cm solid box with its inner face at <paramref name="x"/> and its middle at height <paramref name="y"/> (the bay's frame) clear of the bay's slabs, as the
    /// build rules judge it (each new box shrunk a centimetre a side: its middle must not be inside a slab, with a millimetre to spare)?
    /// </summary>
    private static bool ClearOf(List<(float X0, float X1, float Y0, float Y1)> slabs, float x, float y)
    {
        var (px, py) = (x + 0.01f, y);
        foreach (var (x0, x1, y0, y1) in slabs)
            if (px > x0 - 0.006f && px < x1 + 0.006f && py > y0 - 0.006f && py < y1 + 0.006f) // (a part's place rounds to the centimetre: half of one either way)
                return false;
        return true;
    }

    /// <summary>
    /// Where a trim's solid box can stand in its socket's plane (the socket only has to be on its part's face, its solid bounds' face, so the box may stand off it
    /// in height): the height nearest the waist, from 0.12 m up, where it meets no slab of the bay (null: none), and how far out a second box at the socket's own
    /// height must stand to meet none (the socket must lie within the part's bounds).
    /// </summary>
    private static (float Y, float Out)? StandSpot(Section a, Section b, Vector3 pad)
    {
        var slabs = Slabs(a, b).Concat(Slabs(b, a)).ToList(); // (a row's steps are counted on the bay's front section, which is this trim's back on the bay's -x side: both)
        var top = MathF.Min(a.Top, b.Top) - 0.05f;
        float? best = null;
        for (var dy = 0f; dy < 2.5f && best == null; dy += 0.01f)
            foreach (var y in new[] { pad.Y + dy, pad.Y - dy })
                if (y >= 0.12f && y <= top && ClearOf(slabs, pad.X, y))
                {
                    best = y - pad.Y;
                    break;
                }
        if (best is not { } spot)
            return null;
        var outward = 0f;
        if (MathF.Abs(spot) > 0.0005f)
            for (outward = 0.03f; outward < 0.5f && !ClearOf(slabs, pad.X + outward, pad.Y); outward += 0.01f)
            {
            }
        return (MathF.Round(spot * 1000) / 1000, MathF.Round(outward * 1000) / 1000);
    }

    private static string PartJson(string name, string description, string section, float mass, Builder p) => new JsonObject
    {
        ["format"] = "call-of-slop-construct-part/1",
        ["name"] = name,
        ["description"] = description,
        ["group"] = "Vehicle Body",
        ["section"] = section,
        ["role"] = "panel",
        ["tool"] = "welder",
        ["mass"] = mass,
        ["health"] = MathF.Round(Math.Clamp(mass * 6, 40, 300)),
        ["armour"] = 4,
        ["material"] = "steel",
        ["color"] = Steel,
        ["attachSeconds"] = 1.5,
        ["stage"] = true, // (never offered: only a vehicle made by the kit has one)
        ["boxes"] = p.Boxes,
        ["sockets"] = p.Sockets,
    }.ToJsonString();

    // ---- Decos --------------------------------------------------------------------------------------------------

    /// <summary>A drawn convex shape through <paramref name="pts"/> (detail: its look only).</summary>
    private static void Solid(Builder p, IReadOnlyList<Vector3> pts, string colour, bool paint, bool glow = false)
    {
        var arr = new JsonArray();
        var (mn, mx) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
        foreach (var q in pts)
        {
            var v = new Vector3(R(q.X), R(q.Y), R(q.Z));
            arr.Add(V(v.X, v.Y, v.Z));
            (mn, mx) = (Vector3.Min(mn, v), Vector3.Max(mx, v));
        }
        var b = new JsonObject { ["min"] = V(mn.X, mn.Y, mn.Z), ["max"] = V(mx.X, mx.Y, mx.Z), ["color"] = colour, ["detail"] = true, ["shape"] = "hull", ["points"] = arr };
        if (!paint)
            b["paintable"] = false;
        if (glow)
            b["glow"] = true;
        p.Boxes.Add(b);
    }

    private static void Cube(Builder p, Vector3 mn, Vector3 mx, string colour, bool paint, bool glow = false) =>
        Solid(p, [mn, new(mx.X, mn.Y, mn.Z), new(mn.X, mx.Y, mn.Z), new(mx.X, mx.Y, mn.Z), new(mn.X, mn.Y, mx.Z), new(mx.X, mn.Y, mx.Z), new(mn.X, mx.Y, mx.Z), mx], colour, paint, glow);

    /// <summary>A ring of <paramref name="n"/> points about the y axis.</summary>
    private static IEnumerable<Vector3> Ring(float r, float y, int n = 8) =>
        Enumerable.Range(0, n).Select(i => new Vector3(r * MathF.Cos(MathF.PI * 2 * (i + 0.5f) / n), y, r * MathF.Sin(MathF.PI * 2 * (i + 0.5f) / n)));

    private static string DecoJson(string name, DecoShape s)
    {
        var p = new Builder();
        var n = s.N;
        string what;
        float mass;
        var by = "-y";
        switch (s.Kind)
        {
            case 'S': // a roof sign: a board facing ahead on two posts (W half width, P its foot over the roof, H its height, Dz where it stands along the roof)
            {
                var (w, ph, h, dz) = (n[0], n[1], n[2], n[3]);
                Cube(p, new Vector3(-w, ph, dz - 0.015f), new Vector3(w, ph + h, dz + 0.015f), Steel, true);
                Cube(p, new Vector3(-w - 0.012f, ph + h - 0.004f, dz - 0.02f), new Vector3(w + 0.012f, ph + h + 0.022f, dz + 0.012f), Dark, false); // (a capping strip along its top)
                foreach (var x in new[] { -(w - 0.12f), w - 0.12f })
                    Cube(p, new Vector3(x - 0.02f, -0.08f, dz + 0.018f), new Vector3(x + 0.02f, ph + h * 0.7f, dz + 0.058f), Dark, false);
                what = "a sign board on two posts";
                mass = 6 + 10 * w * h;
                break;
            }
            case 'D': // a destination board: a black box over the windscreen, a lit panel in its face (W half width, H height, Dz its face, Dy its foot)
            {
                var (w, h, dz, dy) = (n[0], n[1], n[2], n[3]);
                Cube(p, new Vector3(-w, dy, dz), new Vector3(w, dy + h, dz + 0.12f), Dark, false);
                Cube(p, new Vector3(-w + 0.045f, dy + 0.035f, dz - 0.015f), new Vector3(w - 0.045f, dy + h - 0.035f, dz - 0.004f), Lit, false, glow: true);
                what = "a lit destination board";
                mass = 4 + 8 * w * h;
                break;
            }
            case 'R': // a radome: a faceted dome on a low ring (R its radius, H its height)
            {
                var (r, h) = (n[0], n[1]);
                Solid(p, Ring(r + 0.035f, -0.03f).Concat(Ring(r + 0.035f, 0.045f)).ToList(), Dark, false);
                Solid(p, Ring(r, 0.03f).Concat(Ring(r * 0.86f, 0.03f + h * 0.5f)).Concat(Ring(r * 0.5f, 0.03f + h * 0.86f)).Append(new Vector3(0, 0.03f + h, 0)).ToList(), Dome, false);
                what = "a radome";
                mass = 5 + 20 * r * r;
                break;
            }
            case 'O': // a bonnet mascot: a chrome fin leaping forward off a plinth (Dz along the hood from its top socket, Dy down to the hood's skin)
            {
                var (dz, dy) = (n[0], n[1]);
                Cube(p, new Vector3(-0.025f, dy - 0.012f, dz - 0.045f), new Vector3(0.025f, dy + 0.026f, dz + 0.045f), Chrome, false);
                Solid(p, [new(-0.009f, dy + 0.02f, dz + 0.035f), new(0.009f, dy + 0.02f, dz + 0.035f), new(-0.009f, dy + 0.02f, dz - 0.025f), new(0.009f, dy + 0.02f, dz - 0.025f),
                    new(-0.004f, dy + 0.14f, dz - 0.075f), new(0.004f, dy + 0.14f, dz - 0.075f), new(-0.006f, dy + 0.1f, dz + 0.01f), new(0.006f, dy + 0.1f, dz + 0.01f)], Chrome, false);
                what = "a chrome bonnet mascot";
                mass = 1.5f;
                break;
            }
            case 'G': // a chrome grille: a frame and slats proud of a hood's nose (W its half width, Y0 and Y1 its foot and top from the socket)
            {
                var (w, y0, y1) = (n[0], n[1], n[2]);
                Cube(p, new Vector3(-w, y1 - 0.035f, -0.045f), new Vector3(w, y1, -0.02f), Chrome, false);
                Cube(p, new Vector3(-w, y0, -0.045f), new Vector3(w, y0 + 0.035f, -0.02f), Chrome, false);
                foreach (var sx in new[] { -1f, 1f })
                    Cube(p, new Vector3(sx > 0 ? w - 0.037f : -w + 0.002f, y0 + 0.033f, -0.043f), new Vector3(sx > 0 ? w - 0.002f : -w + 0.037f, y1 - 0.033f, -0.022f), Chrome, false);
                var count = Math.Max(3, (int)((2 * w - 0.08f) / 0.06f));
                for (var i = 0; i < count; i++)
                {
                    var x = -w + 0.04f + (2 * w - 0.08f) * (i + 0.5f) / count;
                    Cube(p, new Vector3(x - 0.007f, y0 + 0.02f, -0.038f), new Vector3(x + 0.007f, y1 - 0.02f, -0.027f), Chrome, false);
                }
                by = "+z";
                what = "a chrome grille";
                mass = 3 + 6 * w * (y1 - y0);
                break;
            }
            case 'B': // a roof basket: low rails on four posts (W half width, L half length)
            {
                var (w, l) = (n[0], n[1]);
                foreach (var sz in new[] { -1f, 1f })
                    Cube(p, new Vector3(-w - 0.015f, 0.105f, sz * l - 0.015f), new Vector3(w + 0.015f, 0.135f, sz * l + 0.015f), Dark, false);
                foreach (var sx in new[] { -1f, 1f })
                    Cube(p, new Vector3(sx * w - 0.013f, 0.107f, -l - 0.013f), new Vector3(sx * w + 0.013f, 0.133f, l + 0.013f), Dark, false);
                foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    Cube(p, new Vector3(sx * w - 0.01f, -0.06f, sz * l - 0.01f), new Vector3(sx * w + 0.01f, 0.12f, sz * l + 0.01f), Dark, false);
                foreach (var z in new[] { -l * 0.33f, l * 0.33f })
                    Cube(p, new Vector3(-w, 0.012f, z - 0.03f), new Vector3(w, 0.03f, z + 0.03f), Dark, false); // (slats across its floor)
                what = "a roof basket";
                mass = 5 + 6 * w * l;
                break;
            }
            default: // 'J': a jerry can standing on its socket, three handles on its top and a spout
            {
                Cube(p, new Vector3(-0.08f, -0.006f, -0.125f), new Vector3(0.08f, 0.33f, 0.125f), Steel, true);
                foreach (var z in new[] { -0.07f, 0f, 0.07f })
                    Cube(p, new Vector3(-0.012f, 0.32f, z - 0.016f), new Vector3(0.012f, 0.37f, z + 0.016f), Dark, false);
                Solid(p, Ring(0.022f, 0.31f, 6).Concat(Ring(0.018f, 0.375f, 6)).Select(v => v + new Vector3(0, 0, 0.1f)).ToList(), Dark, false);
                what = "a jerry can";
                mass = 4;
                break;
            }
        }
        // What it stands by: a small solid box at its socket (on the far side of the socket from the part it goes on).
        if (by == "+z")
            p.Boxes.Add(new JsonObject { ["min"] = V(-0.03f, -0.03f, -0.03f), ["max"] = V(0.03f, 0.03f, 0), ["hidden"] = true });
        else
            p.Boxes.Add(new JsonObject { ["min"] = V(-0.02f, 0.0f, -0.02f), ["max"] = V(0.02f, 0.02f, 0.02f), ["hidden"] = true });
        p.Sock("base", Vector3.Zero, by);
        return PartJson(name, $"{char.ToUpperInvariant(what[0])}{what[1..]}, sized by the vehicle kit for one vehicle.", "Fittings", MathF.Round(mass, 1), p);
    }
}
