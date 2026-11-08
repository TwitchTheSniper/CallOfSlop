using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;

namespace CallOfSlop;

/// <summary>
/// Body styles phase 3 (docs/vehiclekit/BODY-STYLES.md, section 9): a driver's cab, a hood and a window pane lofted at run time in a body's language, named by
/// their keys as a lofted bay is (<c>Hull Loft (cab...)</c>, <c>Hull Loft (hood...)</c>, <c>Hull Loft (pane...)</c>), so blueprints, saves and the wire carry only
/// the name and every machine makes the same part from it. A cab is a section (a half profile, floor edge up and over to the centreline) drawn along its length
/// with a raked windscreen, pillars, a belt line, doorways and a back wall; a hood is a section lofted from its cowl to its nose over the engine; a pane is a
/// see-through quad that goes in a cab's window, its own part so a shot breaks the glass and not the cab.
/// </summary>
public static partial class HullLoft
{
    /// <summary>
    /// A cab's kinds (the key's letter): <c>e</c> conventional, behind a hood; <c>w</c> crew (two rows of seats, two doorways a side, longer); <c>g</c> an open cab
    /// in a roll cage, bars and no glass; <c>a</c> armoured, plated with a slit to see through; <c>o</c> a cab-over capsule, a flat front over the engine and no
    /// hood; <c>m</c> merged, a capsule whose back is the room's front section, so cab and body read as one mass (a bus front).
    /// </summary>
    public const string CabKinds = "ewgaom";

    public const string CabWord = "cab", HoodWord = "hood", PaneWord = "pane";

    /// <summary>A cab's sill (the doorway's floor) and how wide its pillars and its back wall's jambs are.</summary>
    private const float CabSill = 0.15f, Pillar = 0.08f, Jamb = 0.3f, CabT = 0.04f;

    private const float SealDrop = 0.004f;

    private const string Frame = "#3a3e42", Bars = "#5a5f64", GlassTint = "#9fc6d6";

    // ---- Keys ---------------------------------------------------------------------------------------------------

    /// <summary>A length in centimetres as two of the key's characters (-20.48 m to 20.47 m).</summary>
    private static void Num(StringBuilder sb, float m)
    {
        var v = Math.Clamp(Cm(m) + 2048, 0, 4095);
        sb.Append(Digits[v >> 6]).Append(Digits[v & 63]);
    }

    private static float[]? Nums(string s, int count)
    {
        if (s.Length != 2 * count)
            return null;
        var list = new float[count];
        for (var i = 0; i < count; i++)
        {
            var (a, b) = (Digits.IndexOf(s[2 * i]), Digits.IndexOf(s[2 * i + 1]));
            if (a < 0 || b < 0)
                return null;
            list[i] = ((a << 6 | b) - 2048) / 100f;
        }
        return list;
    }

    /// <summary>A lofted cab's shape: what its key says (lengths in metres, the cab's frame: its feet at z 0.25, its floor y 0, its front toward -z).</summary>
    public sealed record CabShape(char Kind, float Zf, float Zm, float Zb, float Yb, float Yt, float Rake, float D0, float D1, float D2, float D3, float FrontX, Section T, Section B)
    {
        /// <summary>A windscreen pane (and a back window on a closed back).</summary>
        public bool Glass => Kind is 'e' or 'w' or 'o' or 'm';
        public bool Cage => Kind == 'g';
        public bool Slit => Kind == 'a';
        public bool BackWall => Kind is 'e' or 'w' or 'a';
        public bool Crew => D3 > D2 + 0.01f;

        /// <summary>The slit an armoured cab is driven through: 12 cm tall, 4 cm under the window band's top (the seated eye is 1.3 m up).</summary>
        public (float Lo, float Hi) SlitBand => (Yt - 0.17f, Yt - 0.05f);

        public string Key()
        {
            var sb = new StringBuilder(Prefix).Append(CabWord).Append(Kind).Append(' ');
            foreach (var v in (float[])[Zf, Zm, Zb, Yb, Yt, Rake, D0, D1, D2, D3, FrontX])
                Num(sb, v);
            sb.Append(' ').Append(T.Key()).Append(' ').Append(B.SameAs(T) ? "=" : B.Key()).Append(')');
            return sb.ToString();
        }
    }

    /// <summary>A lofted hood: its nose (<c>f</c> flat, <c>w</c> wedge, <c>r</c> rounded, <c>g</c> a grille box), its front, the station where the full section starts, its back, the engine's top above the section's foot, the nose section's factors and the front wheel's place (the fenders' sockets).</summary>
    public sealed record HoodShape(char Nose, float Zf, float Zm, float Zb, float Y0, float Kx, float Ky, float FenderZ, Section S)
    {
        public Section Front => S.Scaled(Kx, Ky);

        public string Key()
        {
            var sb = new StringBuilder(Prefix).Append(HoodWord).Append(Nose).Append(' ');
            foreach (var v in (float[])[Zf, Zm, Zb, Y0, Kx, Ky, FenderZ])
                Num(sb, v);
            return sb.Append(' ').Append(S.Key()).Append(')').ToString();
        }
    }

    /// <summary>A window pane: four corners in the frame of the part it goes in (its socket "frame" at the middle of the first edge, facing down onto that part's socket).</summary>
    public sealed record PaneShape(Vector3[] Q)
    {
        public Vector3 Socket => (Q[0] + Q[1]) / 2 - new Vector3(0, SealDrop, 0); // (under the seal: its face is 4 mm below the glass's edge, so the two never share a plane)

        public string Key()
        {
            var sb = new StringBuilder(Prefix).Append(PaneWord).Append(' ');
            foreach (var q in Q)
            {
                Num(sb, q.X);
                Num(sb, q.Y);
                Num(sb, q.Z);
            }
            return sb.Append(')').ToString();
        }
    }

    public static CabShape? CabOf(string name)
    {
        if (!name.StartsWith(Prefix + CabWord, StringComparison.Ordinal) || !name.EndsWith(')'))
            return null;
        var words = name[(Prefix.Length + CabWord.Length)..^1].Split(' ');
        if (words.Length != 4 || words[0].Length != 1 || !CabKinds.Contains(words[0][0]) || Nums(words[1], 11) is not { } n || Section.Parse(words[2]) is not { } t)
            return null;
        var b = words[3] == "=" ? t : Section.Parse(words[3]);
        if (b == null || b.P.Length != t.P.Length)
            return null;
        var c = new CabShape(words[0][0], n[0], n[1], n[2], n[3], n[4], n[5], n[6], n[7], n[8], n[9], n[10], t, b);
        var ok = c.Zf < c.Zb - 0.5f && c.Zm >= c.Zf && c.Zm < c.Zb && c.Yb > 0.4f && c.Yt > c.Yb + 0.25f && t.Top > c.Yt + 0.04f && b.Top > c.Yt + 0.04f && c.Rake >= 0
            && c.D0 > c.Zf + c.Rake + 0.03f && c.D1 > c.D0 + 0.3f && c.D1 < c.Zb && (c.D3 <= c.D2 + 0.01f || c.D2 > c.D1 + 0.05f && c.D3 < c.Zb) && c.FrontX is >= 0.5f and <= 1.0f
            && (c.Zm <= c.Zf + 0.01f || c.Zm > c.Zf + c.Rake + 0.05f && c.Zm < c.D0);
        return ok ? c : null;
    }

    public static HoodShape? HoodOf(string name)
    {
        if (!name.StartsWith(Prefix + HoodWord, StringComparison.Ordinal) || !name.EndsWith(')'))
            return null;
        var words = name[(Prefix.Length + HoodWord.Length)..^1].Split(' ');
        if (words.Length != 3 || words[0].Length != 1 || !"fwrg".Contains(words[0][0]) || Nums(words[1], 7) is not { } n || Section.Parse(words[2]) is not { } s)
            return null;
        var h = new HoodShape(words[0][0], n[0], n[1], n[2], n[3], n[4], n[5], n[6], s);
        var ok = h.Zf < h.Zm - 0.05f && h.Zm < h.Zb - 0.3f && h.Y0 > 0.1f && h.Y0 < s.Top - 0.05f && h.Kx is >= 0.4f and <= 1.0f && h.Ky is >= 0.3f and <= 1.0f && h.FenderZ > h.Zm && h.FenderZ < h.Zb;
        return ok ? h : null;
    }

    public static PaneShape? PaneOf(string name)
    {
        if (!name.StartsWith(Prefix + PaneWord + " ", StringComparison.Ordinal) || !name.EndsWith(')'))
            return null;
        if (Nums(name[(Prefix.Length + PaneWord.Length + 1)..^1], 12) is not { } n)
            return null;
        var q = Enumerable.Range(0, 4).Select(i => new Vector3(n[3 * i], n[3 * i + 1], n[3 * i + 2])).ToArray();
        var area = Vector3.Cross(q[1] - q[0], q[3] - q[0]).Length();
        return area > 0.01f && area < 8 ? new PaneShape(q) : null;
    }

    /// <summary>A cab's, a hood's or a pane's part from its key (null: not one of theirs; Ok false: one no machine would write).</summary>
    private static (bool Ok, ConstructPartDef? Def)? MakeFront(string name)
    {
        string? json;
        if (name.StartsWith(Prefix + CabWord, StringComparison.Ordinal))
            json = CabOf(name) is { } c ? CabJson(name, c) : null;
        else if (name.StartsWith(Prefix + HoodWord, StringComparison.Ordinal))
            json = HoodOf(name) is { } h ? HoodJson(name, h) : null;
        else if (name.StartsWith(Prefix + PaneWord, StringComparison.Ordinal))
            json = PaneOf(name) is { } q ? PaneJson(name, q) : null;
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

    // ---- The cab ------------------------------------------------------------------------------------------------

    /// <summary>The panes a cab's key puts in it, each with the cab's socket it goes on (the windscreen, then the back window).</summary>
    public static List<(string Socket, PaneShape Pane)> PanesOf(CabShape c)
    {
        var g = new CabGeometry(c);
        return g.Panes();
    }

    /// <summary>
    /// A cab's sections with the levels its rows are cut at (the doorway's sill, the belt, the slit, the window band's top) put in, in the template and the back
    /// section at the same places along them, so row k is the same row at every station.
    /// </summary>
    private sealed class CabGeometry
    {
        public readonly CabShape C;
        public readonly Vector2[] Tp;
        public readonly List<(float Z, Vector2[] P)> Stations = new();
        private readonly Dictionary<float, Vector2[]> _mitres = new();

        public CabGeometry(CabShape c)
        {
            C = c;
            var t = c.T.P.ToList();
            var b = c.B.P.ToList();
            var levels = new List<float> { CabSill, c.Yb, c.Yt };
            if (c.Slit)
                levels.AddRange([c.SlitBand.Lo, c.SlitBand.Hi]);
            foreach (var y in levels.OrderBy(x => x))
                for (var k = 0; k + 1 < t.Count; k++)
                {
                    if (MathF.Abs(t[k].Y - y) < 0.01f || MathF.Abs(t[k + 1].Y - y) < 0.01f)
                    {
                        var at = MathF.Abs(t[k].Y - y) < 0.01f ? k : k + 1;
                        t[at] = t[at] with { Y = y }; // (a level on a point: the point is the level)
                        break;
                    }
                    if (t[k].Y < y && t[k + 1].Y > y)
                    {
                        var u = (y - t[k].Y) / (t[k + 1].Y - t[k].Y);
                        t.Insert(k + 1, new Vector2(t[k].X + (t[k + 1].X - t[k].X) * u, y));
                        b.Insert(k + 1, Vector2.Lerp(b[k], b[k + 1], u));
                        break;
                    }
                }
            Tp = t.ToArray();
            var front = c.FrontX < 0.999f ? Tp.Select(p => p with { X = p.X * c.FrontX }).ToArray() : Tp;
            Stations.Add((c.Zf, front));
            if (c.Zm > c.Zf + 0.01f)
                Stations.Add((c.Zm, Tp));
            Stations.Add((c.Zb, b.ToArray()));
        }

        public int N => Tp.Length;
        public float Top => Tp[^1].Y;

        /// <summary>Point k of the section at z (between the stations).</summary>
        public Vector2 At(int k, float z)
        {
            for (var i = 0; i + 1 < Stations.Count; i++)
                if (z <= Stations[i + 1].Z + 1e-4f || i + 2 == Stations.Count)
                {
                    var (za, pa) = Stations[i];
                    var (zb, pb) = Stations[i + 1];
                    var u = Math.Clamp((z - za) / MathF.Max(1e-4f, zb - za), 0, 1);
                    return Vector2.Lerp(pa[k], pb[k], u);
                }
            return Stations[^1].P[k];
        }

        public Section SectionAt(float z) => new(Enumerable.Range(0, N).Select(k => At(k, z)).ToArray());

        public Vector2[] MitresAt(float z)
        {
            if (!_mitres.TryGetValue(z, out var m))
                _mitres[z] = m = Mitres(SectionAt(z));
            return m;
        }

        /// <summary>Where point k's row starts at the front: the belt and below at the front, above it raked back toward the roof.</summary>
        public float Front(int k) => FrontAtY(Tp[k].Y);

        public float FrontAtY(float y) => y <= C.Yb + 1e-4f ? C.Zf : C.Zf + C.Rake * (y - C.Yb) / MathF.Max(0.05f, Top - C.Yb);

        /// <summary>A row's band: 0 the sill, 1 below the belt, 2 the window band, 3 the roof.</summary>
        public int Band(int k)
        {
            var y = (Tp[k].Y + Tp[k + 1].Y) / 2;
            return y < CabSill ? 0 : y < C.Yb ? 1 : y < C.Yt ? 2 : 3;
        }

        public bool InSlit(int k)
        {
            var y = (Tp[k].Y + Tp[k + 1].Y) / 2;
            return C.Slit && y > C.SlitBand.Lo && y < C.SlitBand.Hi;
        }

        /// <summary>The z cuts every side row is split at (doorways, the mid station), front to back, the back last.</summary>
        public List<float> Cuts()
        {
            var cuts = new List<float> { C.D0, C.D1 };
            if (C.Crew)
                cuts.AddRange([C.D2, C.D3]);
            if (C.Zm > C.Zf + 0.01f)
                cuts.Add(C.Zm);
            cuts.Add(C.Zb);
            return cuts.Distinct().OrderBy(z => z).ToList();
        }

        public bool Doorway(float z0, float z1) => MathF.Abs(z0 - C.D0) < 1e-4f && MathF.Abs(z1 - C.D1) < 1e-4f || C.Crew && MathF.Abs(z0 - C.D2) < 1e-4f && MathF.Abs(z1 - C.D3) < 1e-4f;

        /// <summary>The front's half width at height y (along the raked front's points).</summary>
        public float FrontX(float y)
        {
            for (var k = 0; k + 1 < N; k++)
                if (y >= Tp[k].Y && y <= Tp[k + 1].Y && Tp[k + 1].Y > Tp[k].Y)
                {
                    var (a, b) = (At(k, Front(k)), At(k + 1, Front(k + 1)));
                    return a.X + (b.X - a.X) * (y - Tp[k].Y) / (Tp[k + 1].Y - Tp[k].Y);
                }
            return At(0, C.Zf).X;
        }

        public float BackX(float y) => new Section(Stations[^1].P).XAt(y);

        /// <summary>The windscreen's pane (in the raked front between the pillars, a little into them) and the back window's (in the back wall between its jambs).</summary>
        public List<(string Socket, PaneShape Pane)> Panes()
        {
            var list = new List<(string, PaneShape)>();
            if (!C.Glass)
                return list;
            var (y0, y1) = (C.Yb + 0.03f, C.Yt - 0.03f);
            var (x0, x1) = (FrontX(y0) - Pillar + 0.015f, FrontX(y1) - Pillar + 0.015f);
            list.Add(("glass0", new PaneShape([R3(-x0, y0, FrontAtY(y0)), R3(x0, y0, FrontAtY(y0)), R3(x1, y1, FrontAtY(y1)), R3(-x1, y1, FrontAtY(y1))])));
            if (C.BackWall)
            {
                var (b0, b1) = (BackX(y0) - Jamb + 0.015f, BackX(y1) - Jamb + 0.015f);
                if (b0 > 0.15f && b1 > 0.15f)
                    list.Add(("glass1", new PaneShape([R3(b0, y0, C.Zb), R3(-b0, y0, C.Zb), R3(-b1, y1, C.Zb), R3(b1, y1, C.Zb)])));
            }
            return list;
        }

        private static Vector3 R3(float x, float y, float z) => new(Snap(x), Snap(y), Snap(z));
    }

    private static Vector3 Mitre3(Vector3 n1, Vector3 n2)
    {
        var m = n1 + n2;
        if (m.LengthSquared() < 1e-8f)
            return n1;
        m = Vector3.Normalize(m);
        return m / MathF.Max(0.3f, Vector3.Dot(m, n1));
    }

    /// <summary>A bar from a to b, <paramref name="w"/> square (a cage's tube): a facet <paramref name="w"/> wide, as thick.</summary>
    private static void Bar(Builder p, Vector3 a, Vector3 b, float w, string colour = Bars)
    {
        var d = Vector3.Normalize(b - a);
        var up = MathF.Abs(d.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        var side = Vector3.Normalize(Vector3.Cross(d, up));
        var n = Vector3.Normalize(Vector3.Cross(side, d));
        p.Facet([a - side * w / 2, a + side * w / 2, b + side * w / 2, b - side * w / 2], n, colour, w);
    }

    private static float Area(IReadOnlyList<Vector3> q) => q.Count < 3 ? 0 : Vector3.Cross(q[1] - q[0], q[^1] - q[0]).Length() * (q.Count == 4 ? 1 : 0.5f);

    private static string CabJson(string name, CabShape c)
    {
        var g = new CabGeometry(c);
        var p = new Builder();
        var n = g.N;
        var area = 0f;
        var bars = 0f;
        var flatRoof = g.Tp[^1].Y == g.Tp[^2].Y && g.Stations.All(s => MathF.Abs(s.P[^1].Y - s.P[^2].Y) < 1e-4f);
        var cuts = g.Cuts();

        // The sides: each row cut at the doorways and the mid station; a doorway's segments left out from the sill up; above the belt nothing on a cage.
        foreach (var s in (float[])[-1, 1])
            for (var k = 0; k + 1 < n; k++)
            {
                var band = g.Band(k);
                if (c.Cage && band >= 2)
                    continue;
                var last = k + 2 == n;
                if (last && flatRoof && s > 0)
                    continue; // (the flat roof is one panel across, drawn once)
                var z = float.NaN; // (NaN: the raked front)
                foreach (var cut in cuts)
                {
                    var (za0, za1) = float.IsNaN(z) ? (g.Front(k), g.Front(k + 1)) : (z, z);
                    var z1 = cut;
                    var door = !float.IsNaN(z) && g.Doorway(z, z1) && band is 1 or 2
                        || c.Kind is 'o' or 'm' && band == 2 && z1 <= c.D0 + 1e-4f; // (a capsule's side windows ahead of its doorway: open, so the driver sees out sideways past the long screen)
                    z = cut;
                    if (door)
                        continue;
                    var (a0, a1, b0, b1) = (g.At(k, za0), g.At(k + 1, za1), g.At(k, z1), g.At(k + 1, z1));
                    var (ma0, ma1, mb0, mb1) = (g.MitresAt(za0)[k], g.MitresAt(za1)[k + 1], g.MitresAt(z1)[k], g.MitresAt(z1)[k + 1]);
                    if (last && flatRoof)
                    {
                        Vector3[] roof = [new(-a0.X, a0.Y, za0), new(a0.X, a0.Y, za0), new(b0.X, b0.Y, z1), new(-b0.X, b0.Y, z1)];
                        p.Panel(roof, Vector3.UnitY, Roof, false, [Off(ma0, -1), Off(ma0, 1), Off(mb0, 1), Off(mb0, -1)]);
                        area += Area(roof);
                        var y = MathF.Min(a0.Y, b0.Y);
                        if (MathF.Min(z1, c.Zb - 0.04f) > MathF.Max(za0, za1) + 0.03f)
                            p.Hide(new Vector3(-MathF.Max(a0.X, b0.X), y - 0.05f, MathF.Max(za0, za1) + 0.01f), new Vector3(MathF.Max(a0.X, b0.X), y - 0.01f, MathF.Min(z1, c.Zb - 0.04f)));
                        continue;
                    }
                    if (last && a1.X == 0 && b1.X == 0)
                        (ma1, mb1) = (Ridge(a0, a1), Ridge(b0, b1));
                    var nn = new Vector3((a1.Y - a0.Y) * s, -(a1.X - a0.X), 0);
                    if (nn.LengthSquared() < 1e-6f)
                        continue;
                    var (colour, paint) = Row(a0, a1, k);
                    Vector3[] q = [new(s * a0.X, a0.Y, za0), new(s * a1.X, a1.Y, za1), new(s * b1.X, b1.Y, z1), new(s * b0.X, b0.Y, z1)];
                    p.Panel(q, nn, colour, paint, [Off(ma0, s), Off(ma1, s), Off(mb1, s), Off(mb0, s)]);
                    area += Area(q);
                    // Hidden slabs just inside the skin, from 0.3 m up (the seats' sill and the cab's floor plates are below), never in a doorway.
                    var (ylo, yhi) = (MathF.Max(0.3f, MathF.Min(a0.Y, b0.Y)), MathF.Min(a1.Y, b1.Y));
                    var zlo = MathF.Max(za0, za1) + 0.01f;
                    var zhi = MathF.Min(z1, c.Zb - 0.04f); // (clear of what stands behind the cab: a merged cab's room's brow has its own slabs at its front)
                    if (yhi - ylo < 0.02f || zhi - zlo < 0.02f || c.Zm > c.Zf + 0.01f && z1 <= c.Zm + 1e-3f)
                        continue; // (a capsule's sides ahead of its mid station have none: the front wheels turn under them, as the Cab Over Shell's)
                    if (MathF.Abs(a1.Y - a0.Y) >= MathF.Abs(a1.X - a0.X))
                    {
                        var x = MathF.Min(MathF.Min(a0.X, a1.X), MathF.Min(b0.X, b1.X));
                        if (x < 0.83f && (ylo = MathF.Max(ylo, 1.25f)) > yhi - 0.02f)
                            continue; // (a wall coming in over the seats: its slab only above their backs, 1.2 m up)
                        p.Hide(new Vector3(s * (x - 0.05f), ylo, zlo), new Vector3(s * (x - 0.01f), yhi, zhi));
                    }
                    else if (band == 3)
                    {
                        var y = MathF.Min(MathF.Min(a0.Y, a1.Y), MathF.Min(b0.Y, b1.Y));
                        var (xlo, xhi) = (MathF.Min(MathF.Min(a0.X, a1.X), MathF.Min(b0.X, b1.X)), MathF.Max(MathF.Max(a0.X, a1.X), MathF.Max(b0.X, b1.X)));
                        p.Hide(new Vector3(s * xlo, y - 0.05f, zlo), new Vector3(s * xhi, y - 0.01f, zhi));
                    }
                }
            }
        if (!flatRoof && g.Tp[^1].X == 0 && !c.Cage)
        {
            // (a pitched roof's ridge strip, as a bay's: the halves meet on the centreline's plane)
            float Apex(int st) => g.Stations[st].P[^1].Y + T / 2 / MathF.Max(0.3f, RowNormal(g.Stations[st].P[^2], g.Stations[st].P[^1]).Y) + 0.011f;
            var zr = g.Front(n - 1);
            p.Facet([new(-0.02f, g.At(n - 1, zr).Y + (Apex(0) - g.Stations[0].P[^1].Y), zr), new(0.02f, g.At(n - 1, zr).Y + (Apex(0) - g.Stations[0].P[^1].Y), zr), new(0.02f, Apex(g.Stations.Count - 1), c.Zb), new(-0.02f, Apex(g.Stations.Count - 1), c.Zb)], Vector3.UnitY, Roof, 0.02f, paint: false);
        }

        // The front: the dash and firewall below the belt at the front's plane, the windscreen's pillars and the header above it in the raked plane.
        var lower = new Vector3(0, 0, -1);
        var raked = Vector3.Normalize(new Vector3(0, c.Rake, -(g.Top - c.Yb)));
        var belt = Mitre3(lower, raked);
        Vector3 FrontOff(float y) => MathF.Abs(y - c.Yb) < 1e-3f ? belt : y < c.Yb ? lower : raked;
        Strips(g.Tp, (i, j) =>
        {
            var (bandMid, yi, yj) = ((g.Tp[i].Y + g.Tp[j].Y) / 2, g.Tp[i].Y, g.Tp[j].Y);
            var band = bandMid < CabSill ? 0 : bandMid < c.Yb ? 1 : bandMid < c.Yt ? 2 : 3;
            var (pi, pj) = (g.At(i, g.Front(i)), g.At(j, g.Front(j)));
            var (zi, zj) = (g.Front(i), g.Front(j));
            var slit = c.Slit && bandMid > c.SlitBand.Lo && bandMid < c.SlitBand.Hi;
            var colour = band >= 2 ? Frame : band == 0 ? Rust : Steel;
            if (c.Cage && band >= 2)
                return;
            if (band <= 1 || band == 3 || c.Slit && !slit)
            {
                Vector3[] q = [new(-pi.X, yi < 0.001f ? 0.003f : yi, zi), new(pi.X, yi < 0.001f ? 0.003f : yi, zi), new(pj.X, yj, zj), new(-pj.X, yj, zj)];
                p.Facet(q, band <= 1 ? lower : raked, colour, CabT, paint: band != 3, off: [FrontOff(yi), FrontOff(yi), FrontOff(yj), FrontOff(yj)]);
                area += Area(q);
                var w = MathF.Min(pi.X, pj.X) - 0.05f;
                if (band == 1 && w > 0.05f && yj > 0.82f)
                    p.Hide(new Vector3(-w, MathF.Max(0.8f, yi), c.Zf - 0.025f), new Vector3(w, yj - 0.01f, c.Zf + 0.025f)); // (over the gearbox only: it stands behind the engine, through the cab's firewall)
                return;
            }
            foreach (var s in (float[])[-1, 1]) // (the pillars either side of the windscreen, or of the slit)
            {
                Vector3[] q = [new(s * (pi.X - Pillar), yi, zi), new(s * pi.X, yi, zi), new(s * pj.X, yj, zj), new(s * (pj.X - Pillar), yj, zj)];
                p.Facet(q, raked, Frame, CabT, paint: false, off: [FrontOff(yi), FrontOff(yi), FrontOff(yj), FrontOff(yj)]);
                area += Area(q);
            }
        });

        // The back: closed on a conventional, a crew and an armoured cab (a window between jambs, a slit's cab plated), open on a cage and a capsule.
        if (c.BackWall)
        {
            var back = g.Stations[^1].P;
            Strips(back, (i, j) =>
            {
                var bandMid = (g.Tp[i].Y + g.Tp[j].Y) / 2;
                var band = bandMid < CabSill ? 0 : bandMid < c.Yb ? 1 : bandMid < c.Yt ? 2 : 3;
                var (pi, pj) = (back[i], back[j]);
                var y0 = pi.Y < 0.001f ? 0.003f : pi.Y;
                if (band != 2 || c.Slit)
                {
                    Vector3[] q = [new(pi.X, y0, c.Zb), new(-pi.X, y0, c.Zb), new(-pj.X, pj.Y, c.Zb), new(pj.X, pj.Y, c.Zb)];
                    p.Facet(q, Vector3.UnitZ, band == 0 ? Rust : Steel, CabT);
                    area += Area(q);
                    var w = MathF.Min(pi.X, pj.X) - 0.05f;
                    if (band == 3 && w > 0.05f)
                        p.Hide(new Vector3(-w, pi.Y, c.Zb - 0.025f), new Vector3(w, pj.Y, c.Zb + 0.025f)); // (above the seats only: a seat's back stands just in front of the wall)
                    return;
                }
                foreach (var s in (float[])[-1, 1])
                {
                    Vector3[] q = [new(s * pi.X, y0, c.Zb), new(s * (pi.X - Jamb), y0, c.Zb), new(s * (pj.X - Jamb), pj.Y, c.Zb), new(s * pj.X, pj.Y, c.Zb)];
                    p.Facet(q, Vector3.UnitZ, Steel, CabT);
                    area += Area(q);
                }
            }, pointsOf: g.Tp);
        }

        // A cage: hoops at the front (the pillars and the header), at the doorways' backs and at the back, rails along the shoulders, a brace across the back
        // hoop and a bar across each doorway at the belt (the reviews' "roll cages, protective bars"). Bar sizes differ by family, so no two share a face's plane.
        if (c.Cage)
        {
            var kb = Array.FindIndex(g.Tp, q => MathF.Abs(q.Y - c.Yb) < 1e-3f);
            var kt = Array.FindIndex(g.Tp, q => MathF.Abs(q.Y - c.Yt) < 1e-3f);
            var corner = n - 2; // (the roof's corner: the last point before the centreline)
            void Hoop(Func<int, Vector3> at, float w0)
            {
                for (var k = kb; k < corner; k++)
                    foreach (var s in (float[])[-1, 1])
                    {
                        var (a, b) = (at(k), at(k + 1));
                        Bar(p, a with { X = s * a.X }, b with { X = s * b.X }, w0 + 0.004f * ((k - kb) % 2));
                        bars += Vector3.Distance(a, b);
                    }
                var top = at(corner);
                if (g.Tp[^1].Y > g.Tp[corner].Y + 0.03f)
                    foreach (var s in (float[])[-1, 1])
                        Bar(p, top with { X = s * top.X }, at(n - 1), w0 + 0.008f);
                else
                    Bar(p, top with { X = -top.X }, top, w0 + 0.008f);
                bars += 2 * top.X;
            }
            Vector3 Pt(int k, float z) => new(g.At(k, z).X, g.At(k, z).Y, z);
            Hoop(k => new Vector3(g.At(k, g.Front(k)).X, g.Tp[k].Y, g.Front(k)), 0.05f);
            if (c.Zb - c.D1 > 0.13f)
                Hoop(k => Pt(k, c.D1 + 0.03f), 0.046f); // (a short cab's back hoop is its door's: two hoops 5 cm apart would share their end faces' planes)
            if (c.Crew)
                Hoop(k => Pt(k, c.D3 + 0.03f), 0.046f);
            Hoop(k => Pt(k, c.Zb - 0.03f), 0.05f);
            foreach (var s in (float[])[-1, 1])
            {
                foreach (var k in new[] { kt, corner }.Distinct())
                {
                    var (a, b) = (new Vector3(s * g.At(k, g.Front(k)).X, g.Tp[k].Y, g.Front(k)), Pt(k, c.Zb - 0.03f) with { X = s * g.At(k, c.Zb - 0.03f).X });
                    Bar(p, a + new Vector3(-s * 0.06f, 0, 0), b + new Vector3(-s * 0.06f, 0, 0), 0.042f); // (inside the hoops: a rail's faces never meet a hoop's in its plane)
                    bars += Vector3.Distance(a, b);
                }
                foreach (var (d0, d1) in c.Crew ? new[] { (c.D0, c.D1), (c.D2, c.D3) } : [(c.D0, c.D1)])
                {
                    var x = g.At(kb, (d0 + d1) / 2).X;
                    Bar(p, new Vector3(s * (x + 0.035f), c.Yb - 0.06f, d0 - 0.02f), new Vector3(s * (x + 0.035f), c.Yb - 0.06f, d1 + 0.02f), 0.034f); // (a door bar outside the skin's line)
                    bars += d1 - d0;
                }
            }
            var (bl, br) = (Pt(kb, c.Zb - 0.09f), Pt(corner, c.Zb - 0.09f));
            Bar(p, bl with { X = -bl.X + 0.06f }, br with { X = br.X - 0.06f }, 0.038f);
            bars += Vector3.Distance(bl, br);
        }

        // Sockets: its feet on the rails (as the fixed cab shells'), its roof's top, and a socket for each pane.
        p.Sock("footL", new Vector3(-0.25f, 0, 0.25f), "-y");
        p.Sock("footR", new Vector3(0.25f, 0, 0.25f), "-y");
        p.Hide(new Vector3(-0.28f, 0, 0.22f), new Vector3(0.28f, 0.06f, 0.28f)); // (what the feet stand on: a pad between them, as the shells have)
        var topZ = c.Cage ? c.Zb - 0.03f : (g.Front(n - 1) + c.Zb) / 2;
        var topY = c.Cage ? g.At(n - 1, topZ).Y + 0.04f : MathF.Min(g.At(n - 1, topZ).Y, g.At(n - 2, topZ).Y);
        p.Pad("top", new Vector3(0, topY + 0.03f, topZ), "+y");
        foreach (var (socket, pane) in g.Panes())
            p.Pad(socket, pane.Socket, "+y", 0.02f); // (a hidden pad under it: a socket stands on a face)
        var mass = MathF.Round(Math.Clamp(area * 19.5f + bars * 3.5f, 20, 400), 1);
        var what = c.Kind switch
        {
            'w' => "a crew cab: two rows of seats, two doorways a side",
            'g' => "an open cab in a roll cage, with bars across its doorways",
            'a' => "an armoured cab, plated, with a slit to drive by",
            'o' => "a cab-over capsule: a flat front over the engine",
            'm' => "a cab merged into the body behind it",
            _ => "a cab with a raked windscreen and a doorway each side",
        };
        var part = new JsonObject
        {
            ["format"] = "call-of-slop-construct-part/1",
            ["name"] = name,
            ["description"] = $"The driver's cab lofted in a body's language: {what}. Made by the vehicle kit for one vehicle; stands on the rails like a cab shell.",
            ["group"] = "Vehicle Body",
            ["section"] = "Cabs and cages",
            ["role"] = "panel",
            ["tool"] = "welder",
            ["mass"] = mass,
            ["health"] = MathF.Round(Math.Clamp(area * 80 + bars * 20, 150, 800)),
            ["armour"] = c.Slit ? 9 : 6,
            ["material"] = "steel",
            ["color"] = Steel,
            ["attachSeconds"] = 3.0,
            ["stage"] = true,
            ["boxes"] = p.Boxes,
            ["sockets"] = p.Sockets,
        };
        return part.ToJsonString();
    }

    /// <summary>
    /// The strips across a section's face, centreline to each row (as <see cref="Cap"/>): a row too flat to stand alone joins the next. <paramref name="pointsOf"/>:
    /// the points that decide which rows join (the template's), when the face is another station's.
    /// </summary>
    private static void Strips(Vector2[] pts, Action<int, int> strip, Vector2[]? pointsOf = null)
    {
        var by = pointsOf ?? pts;
        var from = 0;
        for (var k = 1; k < by.Length; k++)
        {
            if (by[k].Y - by[from].Y < 0.02f && k + 1 < by.Length)
                continue;
            if (by[k].Y - by[from].Y >= 0.012f)
                strip(from, k);
            from = k;
        }
    }

    // ---- The hood -----------------------------------------------------------------------------------------------

    private static string HoodJson(string name, HoodShape h)
    {
        var p = new Builder();
        var front = h.Front;
        Sides(p, front, h.S, h.Zf, h.Zm, false);
        Sides(p, h.S, h.S, h.Zm, h.Zb, false);
        Cap(p, front, h.Zf);
        if (h.Nose == 'g')
        {
            // A grille box: slats across the front, proud of the cap (their own planes: the cap's face is 2.5 cm out, a slat's 3.25 and 4.75 cm).
            for (var y = 0.08f; y < front.Top - 0.1f; y += 0.07f)
            {
                var x = MathF.Min(front.XAt(y), front.XAt(y + 0.03f)) - 0.08f;
                if (x > 0.1f)
                    p.Facet([new(-x, y, h.Zf - 0.04f), new(x, y, h.Zf - 0.04f), new(x, y + 0.035f, h.Zf - 0.04f), new(-x, y + 0.035f, h.Zf - 0.04f)], -Vector3.UnitZ, Frame, 0.015f, paint: false);
            }
        }
        p.Pad("engine", new Vector3(0, h.Y0, 0), "-y"); // (its pad on the engine's top, as the fixed hoods')
        p.Pad("front", new Vector3(0, MathF.Max(0.1f, h.Y0 - 0.3f), h.Zf - 0.035f), "-z"); // (just out past the cap's hidden slab, so a ram or a grille isn't in its way)
        var fy = MathF.Max(0.08f, h.Y0 - 0.27f);
        var fx = h.S.XAt(fy) + T / 2;
        p.Pad("-x0", new Vector3(-fx, fy, h.FenderZ), "-x");
        p.Pad("+x0", new Vector3(fx, fy, h.FenderZ), "+x");
        p.Pad("top", new Vector3(0, h.S.Top + 0.03f, (h.Zm + h.Zb) / 2), "+y");
        float Around(Section s) => 2 * Enumerable.Range(0, s.P.Length - 1).Sum(k => Vector2.Distance(s.P[k], s.P[k + 1]));
        var skin = Around(h.S) * (h.Zb - h.Zm) + (Around(h.S) + Around(front)) / 2 * (h.Zm - h.Zf) + front.Top * front.P[0].X * 2;
        var shape = h.Nose switch { 'w' => "a wedge nose", 'r' => "a rounded nose", 'g' => "a grille box", _ => "a flat nose" };
        var part = new JsonObject
        {
            ["format"] = "call-of-slop-construct-part/1",
            ["name"] = name,
            ["description"] = $"A hood lofted in a body's language, {shape}. Bolts to the top of an engine, which it hides. Made by the vehicle kit for one vehicle.",
            ["group"] = "Vehicle Body",
            ["section"] = "Hoods and noses",
            ["role"] = "panel",
            ["tool"] = "welder",
            ["mass"] = MathF.Round(Math.Clamp(skin * 14f, 15, 200), 1),
            ["health"] = MathF.Round(Math.Clamp(skin * 60, 120, 500)),
            ["armour"] = 5,
            ["material"] = "steel",
            ["color"] = Steel,
            ["attachSeconds"] = 2.0,
            ["stage"] = true,
            ["boxes"] = p.Boxes,
            ["sockets"] = p.Sockets,
        };
        return part.ToJsonString();
    }

    // ---- The pane -----------------------------------------------------------------------------------------------

    private static string PaneJson(string name, PaneShape s)
    {
        var p = new Builder();
        var n = Vector3.Normalize(Vector3.Cross(s.Q[1] - s.Q[0], s.Q[3] - s.Q[0]));
        var pts = new JsonArray();
        var (mn, mx) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
        foreach (var q in s.Q)
            foreach (var side in (float[])[-1, 1])
            {
                var v = q + n * (side * 0.006f); // (a flat pane's box is 1.2 cm thick: a part's box is 1 cm or more)
                v = new Vector3(R(v.X), R(v.Y), R(v.Z));
                pts.Add(V(v.X, v.Y, v.Z));
                (mn, mx) = (Vector3.Min(mn, v), Vector3.Max(mx, v));
            }
        p.Boxes.Add(new JsonObject { ["min"] = V(mn.X, mn.Y, mn.Z), ["max"] = V(mx.X, mx.Y, mx.Z), ["color"] = GlassTint, ["shape"] = "hull", ["points"] = pts, ["glass"] = true, ["detail"] = true });
        // The seal along its bottom edge: the solid box it stands by and collides with (a shaped box is drawn only), on the socket.
        var (e0, e1) = (s.Q[0], s.Q[1]);
        var sx = MathF.Abs(e1.X - e0.X) / 2 - 0.01f; // (a centimetre short of the glass's sides)
        p.Boxes.Add(new JsonObject { ["min"] = V(s.Socket.X - sx, s.Socket.Y, s.Socket.Z - 0.009f), ["max"] = V(s.Socket.X + sx, s.Socket.Y + 0.017f, s.Socket.Z + 0.009f), ["color"] = Frame, ["paintable"] = false });
        p.Sock("frame", s.Socket, "-y");
        var area = Area(s.Q);
        var part = new JsonObject
        {
            ["format"] = "call-of-slop-construct-part/1",
            ["name"] = name,
            ["description"] = "A window pane cut for one cab by the vehicle kit. Glass: any bullet breaks it.",
            ["group"] = "Panels",
            ["section"] = "Glass",
            ["role"] = "panel",
            ["tool"] = "wrench",
            ["mass"] = MathF.Round(Math.Clamp(area * 10f, 1, 40), 1),
            ["health"] = 28,
            ["armour"] = 0,
            ["material"] = "glass",
            ["color"] = GlassTint,
            ["attachSeconds"] = 1.2,
            ["stage"] = true,
            ["boxes"] = p.Boxes,
            ["sockets"] = p.Sockets,
        };
        return part.ToJsonString();
    }
}
