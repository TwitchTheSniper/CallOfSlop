using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// Body styles phase 6 (docs/vehiclekit/BODY-STYLES.md, section 12): a <b>balcony</b> (<c>Hull Loft (balc&lt;kind&gt; &lt;length, depth, rail&gt; &lt;gap end&gt;)</c>), a
/// platform at a room's floor level outside its door, with a railing round its outer edge and one end and a gap at the other end where its ladder comes up. Made at
/// run time and named by its key, as a pod is; it stands on a free edge socket of the room's floor, outward (its <c>base</c>), so its key carries how far its deck's
/// inner edge stands out past that socket (clear of the jambs and the hinge) and how far its deck's top stands over it (the floor's top). Its frame: x out from the
/// wall (0 at its deck's inner edge), y up (0 the room's floor top), z along the wall (0 its middle); the socket is at (-inset, -rise, 0). Kinds, each a faction's: <c>w</c> welded grating and diagonals (Bandit), <c>s</c> slats outside its posts
/// (Militia), <c>f</c> a smooth fairing panel under a chrome rail (IT), <c>g</c> chrome posts with gold knobs and a sign band (Scam), <c>t</c> timber posts, rails
/// and balusters on planks (Highland), <c>r</c> a wrought rail of thin dark balusters (British).
/// </summary>
public static partial class HullLoft
{
    public const string BalconyWord = "balc";

    /// <summary>A balcony's kinds (above).</summary>
    public const string BalconyKinds = "wsfgtr";

    private const string Gold = "#d8a832", Ivory = "#e4e2da";

    /// <summary>A balcony: its kind, its length along the wall, its depth out from it, its rail's height over its floor, and which end (+1 the +z end, -1 the -z) is open for the ladder.</summary>
    public sealed record BalconyShape(char Kind, float Length, float Depth, float Rail, int Gap, float Inset, float Rise)
    {
        public string Key()
        {
            var sb = new StringBuilder(Prefix).Append(BalconyWord).Append(Kind).Append(' ');
            Num(sb, Length);
            Num(sb, Depth);
            Num(sb, Rail);
            Num(sb, Inset);
            Num(sb, Rise);
            return sb.Append(' ').Append(Gap > 0 ? '+' : '-').Append(')').ToString();
        }

        /// <summary>Its deck's top, a few millimetres under the room's floor (off its plane).</summary>
        public const float Top = -0.004f;
    }

    /// <summary>Every box's corners and every socket moved by <paramref name="o"/>.</summary>
    private static void Shift(Builder p, Vector3 o)
    {
        static void Move(System.Text.Json.Nodes.JsonArray v, Vector3 o)
        {
            var (x, y, z) = ((float)v[0]!, (float)v[1]!, (float)v[2]!);
            v[0] = R(x + o.X);
            v[1] = R(y + o.Y);
            v[2] = R(z + o.Z);
        }
        foreach (var b in p.Boxes)
        {
            Move(b!["min"]!.AsArray(), o);
            Move(b["max"]!.AsArray(), o);
            if (b["points"] is System.Text.Json.Nodes.JsonArray pts)
                foreach (var q in pts)
                    Move(q!.AsArray(), o);
        }
        foreach (var sk in p.Sockets)
            Move(sk!["at"]!.AsArray(), o);
    }

    public static BalconyShape? BalconyOf(string name)
    {
        if (!name.StartsWith(Prefix + BalconyWord, StringComparison.Ordinal) || !name.EndsWith(')'))
            return null;
        var words = name[(Prefix.Length + BalconyWord.Length)..^1].Split(' ');
        if (words.Length != 3 || words[0].Length != 1 || !BalconyKinds.Contains(words[0][0]) || Nums(words[1], 5) is not { } n || words[2] is not ("+" or "-"))
            return null;
        if (n[0] is < 0.8f or > 2.6f || n[1] is < 0.5f or > 1.2f || n[2] is < 0.85f or > 1.25f || n[3] is < 0 or > 0.35f || n[4] is < 0 or > 0.3f)
            return null;
        return new BalconyShape(words[0][0], n[0], n[1], n[2], words[2] == "+" ? 1 : -1, n[3], n[4]);
    }

    /// <summary>
    /// A balcony's part: the deck (a drawn plate and a hidden slab a player stands on, its top just under the room's floor), an edge beam and two knee brackets under
    /// it, posts round the outer edge and the closed end, a top and a middle rail on them (their hidden boxes hold a player in), and the faction's infill. Every piece
    /// of one family differs in size from the others it meets, so no two share a face's plane (the posts 5 cm, rails 4.5 and 4, infill 3 and less).
    /// </summary>
    private static string BalconyJson(string name, BalconyShape s)
    {
        var p = new Builder();
        var (len, d, h, g) = (s.Length, s.Depth, s.Rail, s.Gap);
        var (z0, z1) = (-len / 2, len / 2);
        var top = BalconyShape.Top;
        var timber = s.Kind is 't';
        var (postCol, railCol) = s.Kind switch
        {
            'w' => (BarSteel, BarSteel),
            's' => (BarSteel, SlatGreen),
            'f' => (Steel, Chrome),
            'g' => (Chrome, Chrome),
            't' => (Timber, Timber),
            _ => (Dark, Dark),
        };
        var ph = timber ? 0.035f : 0.025f; // (a post's half width)
        var xo = d - ph - 0.005f; // (the outer posts' and rails' line)
        var xi = 0.06f; // (the closed end's inner post: clear of the wall)
        var zc = -g * (len / 2 - ph - 0.005f); // (the closed end's posts' line)
        var zg = g * (len / 2 - ph - 0.005f); // (the open end's corner post)

        // The deck: a plate (planks on timber and wrought kinds), its hidden slab, an edge beam and two brackets under it.
        if (s.Kind is 't' or 'r')
        {
            var n = Math.Max(2, (int)MathF.Round(d / 0.15f));
            var w = d / n;
            for (var i = 0; i < n; i++)
                p.Facet([new(i * w + 0.004f, top - 0.02f, z0), new((i + 1) * w - 0.002f, top - 0.02f, z0), new((i + 1) * w - 0.002f, top - 0.02f, z1), new(i * w + 0.004f, top - 0.02f, z1)],
                    Vector3.UnitY, timber ? Wood : Roof, 0.04f, s.Kind == 'r');
        }
        else
            p.Facet([new(0.004f, top - 0.02f, z0), new(d, top - 0.02f, z0), new(d, top - 0.02f, z1), new(0.004f, top - 0.02f, z1)], Vector3.UnitY, s.Kind == 'w' ? Dark : Roof, 0.04f);
        p.Hide(new Vector3(0.01f, top - 0.06f, z0 + 0.005f), new Vector3(d - 0.005f, top, z1 - 0.005f));
        if (s.Kind == 'w') // (grating: bars across the deck, a centimetre proud)
            for (var z = z0 + 0.12f; z < z1 - 0.08f; z += 0.15f)
                Bar(p, new Vector3(0.03f, top + 0.006f, z), new Vector3(d - 0.07f, top + 0.006f, z), Vector3.UnitY, 0.025f, 0.012f, BarSteel, hide: false, paint: false);
        Box(p, new Vector3(d - 0.065f, top - 0.13f, z0 + 0.012f), new Vector3(d - 0.008f, top - 0.05f, z1 - 0.012f), railCol, false);
        foreach (var zb in new[] { z0 + 0.16f, z1 - 0.16f })
            Bar(p, new Vector3(0.012f, -0.5f, zb), new Vector3(d * 0.72f, top - 0.055f, zb), Vector3.UnitZ, 0.05f, 0.045f, postCol, hide: false, paint: false);

        // Posts: along the outer edge at most 0.7 m apart (both corners), and the closed end's inner post.
        var spans = Math.Max(1, (int)MathF.Ceiling((len - 2 * ph) / 0.7f));
        var zs = Enumerable.Range(0, spans + 1).Select(i => -len / 2 + ph + 0.005f + i * (len - 2 * ph - 0.01f) / spans).ToList();
        var postTop = h + 0.04f;
        foreach (var z in zs)
            Box(p, new Vector3(xo - ph, top - 0.02f, z - ph), new Vector3(xo + ph, postTop, z + ph), postCol, false);
        Box(p, new Vector3(xi - ph, top - 0.02f, zc - ph), new Vector3(xi + ph, postTop, zc + ph), postCol, false);

        // Rails: the outer edge's top and middle (4.5 and 4 cm), the closed end's (4 and 3.5 cm), stopping inside the posts.
        Bar(p, new Vector3(xo, h, zs[0]), new Vector3(xo, h, zs[^1]), Vector3.UnitX, 0.045f, 0.035f, railCol, paint: false);
        Bar(p, new Vector3(xo, h * 0.5f, zs[0]), new Vector3(xo, h * 0.5f, zs[^1]), Vector3.UnitX, 0.04f, 0.035f, railCol, paint: false);
        Bar(p, new Vector3(xi, h, zc), new Vector3(xo, h, zc), Vector3.UnitZ, 0.04f, 0.035f, railCol, paint: false);
        Bar(p, new Vector3(xi, h * 0.5f, zc), new Vector3(xo, h * 0.5f, zc), Vector3.UnitZ, 0.035f, 0.035f, railCol, paint: false);

        // The faction's infill, outer edge and closed end.
        void Infill(Vector3 a, Vector3 b, Vector3 n) // (a run of railing from a to b at the posts' line, n out of it)
        {
            var along = b - a;
            var l = along.Length();
            var u = along / l;
            switch (s.Kind)
            {
                case 'w': // (welded diagonals, zigzag, stopping short of the rails)
                {
                    var k = Math.Max(1, (int)MathF.Round(l / 0.35f));
                    for (var i = 0; i < k; i++)
                    {
                        var (q0, q1) = (a + u * (l * i / k + 0.03f), a + u * (l * (i + 1) / k - 0.03f));
                        var (y0, y1) = i % 2 == 0 ? (0.07f, h * 0.5f - 0.05f) : (h * 0.5f - 0.05f, 0.07f);
                        Bar(p, q0 with { Y = y0 }, q1 with { Y = y1 }, n, 0.03f, 0.02f, BarSteel, hide: false, paint: false);
                        Bar(p, q0 with { Y = h * 0.5f + 0.05f }, q1 with { Y = h - 0.05f }, n, 0.03f, 0.02f, BarSteel, hide: false, paint: false);
                    }
                    break;
                }
                case 's': // (three slats outside the posts)
                    foreach (var y in new[] { 0.08f, 0.3f, 0.52f })
                    {
                        var o = n * (ph + 0.017f);
                        p.Facet([a + o + u * 0.01f + Vector3.UnitY * y, a + o + u * 0.01f + Vector3.UnitY * (y + 0.15f), b + o - u * 0.01f + Vector3.UnitY * (y + 0.15f), b + o - u * 0.01f + Vector3.UnitY * y], n, SlatGreen, 0.02f);
                    }
                    p.Hide(a + n * (ph + 0.008f) + u * 0.02f + Vector3.UnitY * 0.1f, b + n * (ph + 0.026f) - u * 0.02f + Vector3.UnitY * 0.65f);
                    break;
                case 'f': // (a smooth panel between the posts, inside the rail's line)
                    p.Facet([a + u * (ph + 0.003f) + Vector3.UnitY * 0.04f, a + u * (ph + 0.003f) + Vector3.UnitY * (h - 0.05f), b - u * (ph + 0.003f) + Vector3.UnitY * (h - 0.05f), b - u * (ph + 0.003f) + Vector3.UnitY * 0.04f], n, Ivory, 0.02f);
                    p.Hide(a + u * (ph + 0.01f) - n * 0.006f + Vector3.UnitY * 0.06f, b - u * (ph + 0.01f) + n * 0.006f + Vector3.UnitY * (h - 0.07f));
                    break;
                case 'g': // (a sign band outside the posts, under the rail)
                {
                    var o = n * (ph + 0.017f);
                    p.Facet([a + o + u * 0.01f + Vector3.UnitY * (h - 0.3f), a + o + u * 0.01f + Vector3.UnitY * (h - 0.06f), b + o - u * 0.01f + Vector3.UnitY * (h - 0.06f), b + o - u * 0.01f + Vector3.UnitY * (h - 0.3f)], n, Lit, 0.02f);
                    break;
                }
                default: // (balusters: timber every 14 cm, wrought iron every 10)
                {
                    var step = timber ? 0.14f : 0.1f;
                    var w = timber ? 0.03f : 0.016f;
                    var k = Math.Max(1, (int)(l / step));
                    for (var i = 1; i < k; i++)
                    {
                        var q = a + u * (l * i / k);
                        if (zs.Any(z => MathF.Abs(q.Z - z) < ph + w) && MathF.Abs(n.X) > 0.5f)
                            continue; // (not inside a post)
                        foreach (var (y0, y1) in new[] { (top + 0.002f, h * 0.5f - 0.03f), (h * 0.5f + 0.03f, h - 0.03f) })
                            Bar(p, q with { Y = y0 }, q with { Y = y1 }, n, w, w * 0.9f, railCol, hide: false, paint: false);
                    }
                    break;
                }
            }
        }
        for (var i = 0; i + 1 < zs.Count; i++)
            Infill(new Vector3(xo, 0, zs[i]), new Vector3(xo, 0, zs[i + 1]), Vector3.UnitX);
        Infill(new Vector3(xi, 0, zc), new Vector3(xo, 0, zc), new Vector3(0, 0, -g));
        if (s.Kind == 'g') // (gold knobs on the posts)
            foreach (var z in zs)
                Box(p, new Vector3(xo - ph - 0.008f, postTop, z - ph - 0.008f), new Vector3(xo + ph + 0.008f, postTop + 0.06f, z + ph + 0.008f), Gold, false);
        // The ladder's socket at the open end, on its deck's edge, midway between the wall and the corner post.
        p.Pad("ladder", new Vector3(MathF.Round((xo - ph) / 2 * 100) / 100, top, zg + g * (ph + 0.005f)), g > 0 ? "+z" : "-z", 0.02f);
        // Into the socket's frame: the base on the floor's edge socket, and a stand-off from it to the deck's inner edge.
        Shift(p, new Vector3(s.Inset, s.Rise, 0));
        p.Pad("base", Vector3.Zero, "-x", 0.02f);
        var what = s.Kind switch { 'w' => "welded grating", 's' => "slatted rails", 'f' => "a smooth fairing rail", 'g' => "chrome rails and a sign band", 't' => "timber rails", _ => "a wrought rail" };
        return PartJson(name, $"A balcony of {what} outside a door, made by the vehicle kit for one body.", "Hull trim", MathF.Round(Math.Clamp(25 + 30 * len * d, 30, 120), 1), p);
    }
}
