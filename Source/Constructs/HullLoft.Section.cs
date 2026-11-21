using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;

namespace CallOfSlop;

/// <summary>
/// Hull sections (docs/sandbox/design/hull-dress.md; Sean, 2026-11-21): a ship-scale hull piece with the Zero Day's detail, made at run time from
/// its key as a formed panel is. It is one side of a lofted, faceted hull between two half profiles (a keel-to-spine run of points, mirrored by
/// side), the facets <c>K0</c> to <c>K1</c> of it, dressed in a style from Constructs/DressStyles.json: armour slabs laid by
/// <see cref="HullDress.Plates"/>, chine rails at its corners, the light strip over the beam, and the extra bits the key asks for (A bolts and
/// chevrons, B vents, D lights, F conduit). Sean chose dress drawn into the skin's own key (one part, rerolled by its salt) over separate
/// subparts on facet welds, 2026-11-21.
/// <c>Hull Section (600 f1-4 s it 4 ABDF | 280,-160 530,30 600,330 500,630 260,740 | =)</c>: length cm, facets, side (p port, s starboard),
/// style, salt, extras (or -), then profile A (the front end, z 0) and profile B (z = length; = for the same), each point a half width and a
/// height in cm, keel first. The part's origin is the hull's centreline at height 0 and the front end, so sections built from the same
/// stations line up when placed at their station's z.
/// </summary>
public static partial class HullLoft
{
    public const string SectionPrefix = "Hull Section (";
    public const int SectionMinCm = 50, SectionMaxCm = 3200, SectionMaxHalfCm = 1600, SectionMaxYCm = 1600, SectionMinPoints = 3, SectionMaxPoints = 8, SectionMaxSalt = 99;
    public const string SectionExtras = "ABDF";

    /// <summary>A hull section's shape, as its key has it (cm).</summary>
    public sealed record SectionShape(int L, int K0, int K1, int Side, string Style, int Salt, string Extras, (int H, int Y)[] A, (int H, int Y)[] B)
    {
        public string Key()
        {
            var sb = new StringBuilder(SectionPrefix);
            sb.Append(CultureInfo.InvariantCulture, $"{L} f{K0}-{K1} {(Side < 0 ? 'p' : 's')} {Style} {Salt} {(Extras.Length == 0 ? "-" : Extras)} | ");
            Prof(sb, A);
            sb.Append(" | ");
            if (B.SequenceEqual(A))
                sb.Append('=');
            else
                Prof(sb, B);
            return sb.Append(')').ToString();
        }

        private static void Prof(StringBuilder sb, (int H, int Y)[] p)
        {
            for (var i = 0; i < p.Length; i++)
                sb.Append(i == 0 ? "" : " ").Append(CultureInfo.InvariantCulture, $"{p[i].H},{p[i].Y}");
        }

        public bool Has(char extra) => Extras.Contains(extra);
    }

    public static bool IsSectionName(string name) => name.StartsWith(SectionPrefix, StringComparison.Ordinal);

    /// <summary>A hull section's key read, or null with the reason (a refusal names what is wrong).</summary>
    public static SectionShape? SectionOf(string name, out string why)
    {
        why = "";
        if (!IsSectionName(name) || !name.EndsWith(')'))
            return Fail(out why, "not a hull section's key");
        var parts = name[SectionPrefix.Length..^1].Split(" | ");
        if (parts.Length != 3)
            return Fail(out why, "a key is: length, facets, side, style, salt, extras | profile A | profile B");
        var w = parts[0].Split(' ');
        if (w.Length != 6)
            return Fail(out why, "the head is: length, f<first>-<last>, p or s, style, salt, extras (or -)");
        if (!int.TryParse(w[0], NumberStyles.None, CultureInfo.InvariantCulture, out var l) || l < SectionMinCm || l > SectionMaxCm)
            return Fail(out why, $"the length is {SectionMinCm} to {SectionMaxCm} cm");
        var f = w[1];
        var dash = f.IndexOf('-');
        if (f.Length < 4 || f[0] != 'f' || dash < 2 || !int.TryParse(f.AsSpan(1, dash - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var k0)
            || !int.TryParse(f.AsSpan(dash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var k1))
            return Fail(out why, "facets are f<first>-<last>, such as f1-4");
        var side = w[2] switch { "p" => -1, "s" => 1, _ => 0 };
        if (side == 0)
            return Fail(out why, "the side is p (port) or s (starboard)");
        if (DressStyles.Find(w[3]) == null)
            return Fail(out why, $"no dress style \"{w[3]}\" (Constructs/DressStyles.json)");
        if (!int.TryParse(w[4], NumberStyles.None, CultureInfo.InvariantCulture, out var salt) || salt > SectionMaxSalt)
            return Fail(out why, $"the salt is 0 to {SectionMaxSalt}");
        var extras = w[5] == "-" ? "" : w[5];
        if (extras.Any(c => !SectionExtras.Contains(c)) || extras.Distinct().Count() != extras.Length || !extras.SequenceEqual(SectionExtras.Where(extras.Contains)))
            return Fail(out why, $"extras are some of {SectionExtras} in that order, or -");
        var a = ProfileOf(parts[1]);
        var b = parts[2] == "=" ? a : ProfileOf(parts[2]);
        if (a == null || b == null)
            return Fail(out why, $"a profile is {SectionMinPoints} to {SectionMaxPoints} points H,Y in cm (H 0 to {SectionMaxHalfCm}, Y within {SectionMaxYCm})");
        if (a.Length != b.Length)
            return Fail(out why, "both profiles have as many points");
        if (k0 < 1 || k1 < k0 || k1 > a.Length - 1)
            return Fail(out why, $"facets run from 1 to {a.Length - 1} (facet k is from point k-1 to k)");
        return new SectionShape(l, k0, k1, side, w[3], salt, extras, a, b);
    }

    private static SectionShape? Fail(out string why, string text)
    {
        why = text;
        return null;
    }

    private static (int H, int Y)[]? ProfileOf(string s)
    {
        var pts = s.Split(' ');
        if (pts.Length < SectionMinPoints || pts.Length > SectionMaxPoints)
            return null;
        var r = new (int H, int Y)[pts.Length];
        for (var i = 0; i < pts.Length; i++)
        {
            var c = pts[i].Split(',');
            if (c.Length != 2 || !int.TryParse(c[0], NumberStyles.None, CultureInfo.InvariantCulture, out var h) || h > SectionMaxHalfCm
                || !int.TryParse(c[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var y) || Math.Abs(y) > SectionMaxYCm)
                return null;
            r[i] = (h, y);
        }
        return r;
    }

    private static (bool Ok, ConstructPartDef? Def)? MakeSection(string name)
    {
        if (!IsSectionName(name))
            return null;
        if (SectionOf(name, out var why) is not { } sh)
        {
            Console.WriteLine($"[hull] {name}: REFUSED, {why}");
            return (false, null);
        }
        try
        {
            var def = Adopt(ConstructParts.Parse(SectionJson(name, sh, out _), name), name);
            return (def != null, def);
        }
        catch (ConstructPartException e)
        {
            Console.WriteLine($"[hull] {name}: REFUSED, {e.Message}");
            return (false, null);
        }
    }

    /// <summary>What a section's part holds, for <c>--hull-section</c>: its shapes by kind.</summary>
    public readonly record struct SectionCounts(int Facets, int Slabs, int Strips, int Rails, int Bolts, int Chevrons, int Vents, int Lamps, int Conduit, int Hidden, int Sockets, float Mass);

    /// <summary>A sample key: a starboard flank of a Zero Day-sized hull, keel to spine, dressed in IT with every extra.</summary>
    public const string SectionExample = "Hull Section (600 f1-4 s it 4 ABDF | 280,-160 530,30 600,330 500,630 260,740 | =)";

    /// <summary><c>--hull-section [KEY|example]</c>: a section's key made into its part, with its counts and checks.</summary>
    public static int SectionCli(string[] args)
    {
        var name = args.Length == 0 || args[0] == "example" ? SectionExample : string.Join(' ', args);
        if (SectionOf(name, out var why) is not { } sh)
        {
            Console.WriteLine($"refused: {why}");
            return 1;
        }
        if (name.Length > ConstructParts.MaxRuntimeName)
        {
            Console.WriteLine($"refused: the key is {name.Length} characters, the most {ConstructParts.MaxRuntimeName}");
            return 1;
        }
        if (sh.Key() != name)
            Console.WriteLine($"note: the key as written is {sh.Key()}");
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        SectionJson(name, sh, out var c);
        if (ConstructParts.Find(name) is not { } d) // (by the door saves and blueprints use)
        {
            Console.WriteLine("refused: the part loader refused it (callofslop.log has the REFUSED line)");
            return 1;
        }
        var ms = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        var size = d.BoundsMax - d.BoundsMin;
        Console.WriteLine(name);
        Console.WriteLine($"  facets {c.Facets}, slabs {c.Slabs}, strips {c.Strips}, rails {c.Rails}, bolts {c.Bolts}, chevrons {c.Chevrons}, vents {c.Vents}, lamps {c.Lamps}, conduit {c.Conduit}");
        Console.WriteLine($"  boxes {d.Boxes.Length} of {ConstructParts.MaxBoxes} (collision {c.Hidden}), sockets {d.Sockets.Length} ({string.Join(' ', d.Sockets.Select(q => q.Name))})");
        Console.WriteLine($"  bounds {size.X:0.00} x {size.Y:0.00} x {size.Z:0.00} m, mass {d.Mass:0.#} kg, health {d.Health:0}, armour {d.Armour:0}; made in {ms:0.0} ms; key {name.Length} of {ConstructParts.MaxRuntimeName}");
        var zf = ZFight.Report(name, ZFight.Boxes(d), max: 12);
        Console.WriteLine(zf.Count == 0 ? "  coplanar faces: none" : $"  coplanar faces:\n    {string.Join("\n    ", zf)}");
        return zf.Count == 0 ? 0 : 1;
    }

    // ---- The part ------------------------------------------------------------------------------------------------

    private const double SecT = 0.04; // the skin's thickness (the Zero Day's)
    private const double SecTrim = 0.003; // dress stops this far short of the section's ends (rails 1x, the light strip 2x, conduit 3x), so no two pieces' ends share a plane

    /// <summary>A section's part file. Its numbers by kind in <paramref name="counts"/>.</summary>
    public static string SectionJson(string name, SectionShape sh, out SectionCounts counts)
    {
        var style = DressStyles.Find(sh.Style)!;
        var tr = style.Trim;
        var p = new Builder();
        double len = sh.L / 100.0;
        double[] Pt((int H, int Y) q, double z) => [sh.Side * q.H / 100.0, q.Y / 100.0, z];
        var n = sh.A.Length;
        double axisY = (sh.A.Min(q => q.Y) + sh.A.Max(q => q.Y)) / 200.0;
        var beam = Enumerable.Range(0, n).MaxBy(i => sh.A[i].H + sh.B[i].H); // (the widest point: the beam line)
        var top = sh.K1; // (the highest corner inside the section's facets: the top chine, for the conduit)
        int facets = 0, slabs = 0, strips = 0, rails = 0, bolts = 0, chevrons = 0, vents = 0, lamps = 0, conduit = 0, hidden = 0;
        double area = 0, armourArea = 0;
        var boltJobs = new List<(HullDress.Band Band, double[] N, HullDress.Item It, double Across)>();
        // The part's boxes are capped (ConstructParts.MaxBoxes): the skin, its armour, rails and voids always fit (at most 7 facets of 10 panels),
        // and the extras take what is left in order (lights, conduit, vents, chevrons, then bolts), one spare kept for a pad.
        int Room() => ConstructParts.MaxBoxes - 1 - p.Boxes.Count;

        HullDress.Band BandOf(int k) => new(Pt(sh.A[k - 1], 0), Pt(sh.A[k], 0), Pt(sh.B[k - 1], len), Pt(sh.B[k], len), len);
        Vector3 F(double[] q) => new((float)q[0], (float)q[1], (float)q[2]);

        // Each facet: the skin plate, the armour on it, its bolts, and the void behind it as a hidden box.
        for (var k = sh.K0; k <= sh.K1; k++)
        {
            var band = BandOf(k);
            var nrm = HullDress.Normal(band, 0, axisY);
            var dz = k % 2 == 1 ? 0.006 : 0; // (odd facets 6 mm along, so neighbours' plates never end on one plane: the Zero Day's stagger)
            double[] Corner(double u, double v)
            {
                var q = HullDress.On(band, u, v);
                return [q[0], q[1], q[2] + dz * (u < 0.5 ? 1 : -1) + (u < 0.5 ? 0.0015 : -0.0015)];
            }
            p.Facet([F(Corner(0, 0)), F(Corner(0, 1)), F(Corner(1, 1)), F(Corner(1, 0))], F(nrm), style.Color("skin"), (float)SecT, paint: false);
            facets++;
            var across = Math.Sqrt(HullDress.Dot(HullDress.Sub(band.B0, band.A0), HullDress.Sub(band.B0, band.A0)));
            area += across * len;
            var items = HullDress.Plates(band, style, k, 0, sh.Salt);
            foreach (var it in items)
            {
                if (it.Strip)
                {
                    Plate(p, HullDress.StripCorners(band, nrm, it.U0, it.U1, it.V0, it.V1, it.D0 + StripLift(band)), it.Color, it.Finish, false);
                    strips++;
                    continue;
                }
                var (inner, outer) = HullDress.SlabOutlines(band, nrm, it.U0, it.U1, it.V0, it.V1, it.D0, it.D1, it.Bevel);
                Hull(p, [.. inner.Concat(outer)], it.Color, it.Finish, false);
                slabs++;
                armourArea += (it.U1 - it.U0) * len * (it.V1 - it.V0) * across;
                if (sh.Has('A'))
                    boltJobs.Add((band, nrm, it, across));
            }
            // The void: a hidden box under the facet, inside the hull, from its innermost corner in (the Zero Day's Void, one per facet).
            var hIn = Math.Min(Math.Min(sh.A[k - 1].H, sh.A[k].H), Math.Min(sh.B[k - 1].H, sh.B[k].H)) / 100.0;
            var y0 = Math.Min(Math.Min(sh.A[k - 1].Y, sh.A[k].Y), Math.Min(sh.B[k - 1].Y, sh.B[k].Y)) / 100.0;
            var y1 = Math.Max(Math.Max(sh.A[k - 1].Y, sh.A[k].Y), Math.Max(sh.B[k - 1].Y, sh.B[k].Y)) / 100.0;
            var xo = Math.Floor((hIn - 0.06) * 100) / 100;
            var xi = Math.Max(0, Math.Floor((hIn - 0.36) * 100) / 100);
            if (xo - xi >= 0.05 && y1 - y0 >= 0.05)
            {
                double ya = Math.Ceiling(y0 * 100 + 1) / 100, yb = Math.Floor(y1 * 100 - 1) / 100, za = 0.01, zb = Math.Floor(len * 100 - 1) / 100;
                Vector3 mn = new((float)(sh.Side < 0 ? -xo : xi), (float)ya, (float)za), mx = new((float)(sh.Side < 0 ? -xi : xo), (float)yb, (float)zb);
                p.Hide(mn, mx);
                hidden++;
                var mid = (ya + yb) / 2;
                p.Sock($"a{k}", new Vector3((mn.X + mx.X) / 2, (float)Math.Round(mid, 2), mn.Z), "-z");
                p.Sock($"b{k}", new Vector3((mn.X + mx.X) / 2, (float)Math.Round(mid, 2), mx.Z), "+z");
                p.Sock($"i{k}", new Vector3(sh.Side < 0 ? mx.X : mn.X, (float)Math.Round(mid, 2), (float)Math.Round(len / 2, 2)), sh.Side < 0 ? "+x" : "-x");
            }
        }

        // Chine rails at the corners between its facets (the beam's own size at its widest point), stopping short of its ends.
        for (var i = sh.K0; i < sh.K1; i++)
        {
            var isBeam = i == beam;
            double w = isBeam ? tr.BeamW : tr.RailW, h = isBeam ? tr.BeamH : tr.RailH;
            double[][] Ring((int H, int Y)[] prof, double z) => HullDress.ChineRing(Pt(prof[i - 1], z), Pt(prof[i], z), Pt(prof[i + 1], z), w, h);
            Hull(p, [.. Ring(sh.A, SecTrim), .. Ring(sh.B, len - SecTrim)], style.Color(isBeam ? tr.BeamRail : tr.Rail), "plated steel", false);
            rails++;
        }

        // The light strip, just over the beam, on the facet above it; D: the running lamps along it and the navigation light at its front end.
        if (beam + 1 >= sh.K0 && beam + 1 <= sh.K1)
        {
            var k = beam + 1;
            var band = BandOf(k);
            var nrm = HullDress.Normal(band, 0, axisY);
            var across = Math.Sqrt(HullDress.Dot(HullDress.Sub(band.B0, band.A0), HullDress.Sub(band.B0, band.A0)));
            double v0 = (tr.StripOff + (beam >= sh.K0 && beam < sh.K1 ? tr.BeamW * 0.5 : 0)) / Math.Max(across, 0.1), v1 = v0 + tr.StripW / Math.Max(across, 0.1);
            if (v1 < 0.5)
            {
                Plate(p, HullDress.StripCorners(band, nrm, 2 * SecTrim / len, 1 - 2 * SecTrim / len, v0, v1, 0.025), style.Color(tr.Strip), null, true);
                strips++;
                if (sh.Has('D'))
                {
                    var runs = Math.Max(1, (int)Math.Floor(len / tr.RunEvery));
                    for (var r = 0; r < runs && lamps < 12 && Room() >= 2; r++)
                        lamps += Lamp(p, band, nrm, (r + 0.5) / runs, (v0 + v1) / 2, 0.025, tr.LampSize * 0.7, style.Color(tr.Running));
                    if (Room() >= 2)
                        lamps += Lamp(p, band, nrm, Math.Min(0.4, 0.25 / len), (v0 + v1) / 2, 0.025, tr.LampSize, style.Color(sh.Side < 0 ? tr.Port : tr.Starboard));
                }
            }
        }

        // D: an amber warning lamp at each end of the top facet; A: hazard chevrons at the front of the lowest facet.
        if (sh.Has('D'))
        {
            var band = BandOf(sh.K1);
            var nrm = HullDress.Normal(band, 0, axisY);
            foreach (var u in new[] { Math.Min(0.3, 0.3 / len), Math.Max(0.7, 1 - 0.3 / len) })
                if (Room() >= 2)
                    lamps += Lamp(p, band, nrm, u, 0.96, 0.03, tr.LampSize * 0.8, style.Color(tr.Warning));
        }
        if (sh.Has('A') && Room() >= 6)
            chevrons += Chevrons(p, BandOf(sh.K0), HullDress.Normal(BandOf(sh.K0), 0, axisY), tr, style, len);

        // B: vents, one every so many metres, on the facet under the top one (or the only one), standing on whatever armour is under them.
        if (sh.Has('B'))
        {
            var k = sh.K1 > sh.K0 ? sh.K1 - 1 : sh.K0;
            var band = BandOf(k);
            var nrm = HullDress.Normal(band, 0, axisY);
            var items = HullDress.Plates(band, style, k, 0, sh.Salt);
            var count = Math.Max(1, (int)Math.Floor(len / tr.VentEvery));
            for (var i = 0; i < count && vents < 4 && Room() >= 1 + tr.VentSlats; i++)
                vents += Vent(p, band, nrm, (i + 0.5) / count, 0.5, items, tr, style, len);
        }

        // F: a conduit along the top chine, on the facet under it, with brackets.
        if (sh.Has('F') && top > sh.K0 && Room() >= 3)
        {
            var band = BandOf(top);
            var nrm = HullDress.Normal(band, 0, axisY);
            var across = Math.Sqrt(HullDress.Dot(HullDress.Sub(band.B0, band.A0), HullDress.Sub(band.B0, band.A0)));
            var items = HullDress.Plates(band, style, top, 0, sh.Salt);
            var v = Math.Min(0.45, (tr.RailW + tr.PipeR + 0.06) / Math.Max(across, 0.1));
            conduit += Conduit(p, band, nrm, v, items, tr, style, len, Math.Min(10, Room()));
        }

        // A: bolt rows on the slabs, as many as there is room for (at most 96).
        foreach (var j in boltJobs)
        {
            var most = Math.Min(96 - bolts, Room());
            if (most < 2)
                break;
            bolts += Bolts(p, j.Band, j.N, j.It, tr, style, len, j.Across, most);
        }

        var mass = (float)Math.Round(area * 60 + armourArea * 90, 1);
        if (p.Sockets.Count == 0)
            p.Pad("c", Vector3.Zero, "-y");
        counts = new SectionCounts(facets, slabs, strips, rails, bolts, chevrons, vents, lamps, conduit, hidden, p.Sockets.Count, mass);
        return new JsonObject
        {
            ["format"] = "call-of-slop-construct-part/1",
            ["name"] = name,
            ["description"] = $"A ship's hull section in the {style.Label} style: skin, armour, rails and the void behind them, {sh.L / 100.0:0.##} m long. Shot off, it opens the side.",
            ["group"] = "Ship",
            ["section"] = "Hull sections",
            ["role"] = "frame",
            ["tool"] = "welder",
            ["mass"] = mass,
            ["health"] = MathF.Round(Math.Clamp(mass * 7, 200, 9000)),
            ["armour"] = 40,
            ["material"] = "steel",
            ["color"] = style.Color("skin"),
            ["attachSeconds"] = 6.0,
            ["stage"] = true,
            ["boxes"] = p.Boxes,
            ["sockets"] = p.Sockets,
        }.ToJsonString();
    }

    // ---- Drawing helpers -----------------------------------------------------------------------------------------

    /// <summary>A drawn convex shape through <paramref name="pts"/> (detail, a "hull": it collides where drawn, as the Zero Day's armour does).</summary>
    private static void Hull(Builder p, double[][] pts, string colour, string? finish, bool glow)
    {
        var arr = new JsonArray();
        var (mn, mx) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
        foreach (var q in pts)
        {
            var v = new Vector3(R((float)q[0]), R((float)q[1]), R((float)q[2]));
            arr.Add(V(v.X, v.Y, v.Z));
            (mn, mx) = (Vector3.Min(mn, v), Vector3.Max(mx, v));
        }
        if (mx.X - mn.X < 0.01f || mx.Y - mn.Y < 0.01f || mx.Z - mn.Z < 0.01f)
        {
            // (a flat or thin shape the loader would refuse: grown to a centimetre on its thin axes about its middle, never dropped)
            var c = (mn + mx) / 2;
            var h = Vector3.Max((mx - mn) / 2, new Vector3(0.0051f));
            (mn, mx) = (c - h, c + h);
        }
        var b = new JsonObject { ["min"] = V(mn.X, mn.Y, mn.Z), ["max"] = V(mx.X, mx.Y, mx.Z), ["color"] = colour, ["detail"] = true, ["shape"] = "hull", ["points"] = arr, ["paintable"] = false };
        if (glow)
            b["glow"] = true;
        else if (!string.IsNullOrEmpty(finish))
            b["finish"] = finish;
        p.Boxes.Add(b);
    }

    /// <summary>How far a strip stands off a twisted band (profiles A and B differ) beyond its style's number: a twisted skin facet's outer face
    /// bows out from the band's middle plane by up to half its corners' twist, and a thin strip on it would share its plane.</summary>
    private static double StripLift(HullDress.Band b)
    {
        var n = HullDress.Unit(HullDress.Cross(HullDress.Sub(b.B0, b.A0), HullDress.Sub(b.A1, b.A0)));
        var off = Math.Abs(HullDress.Dot(HullDress.Sub(b.B1, b.A0), n)); // (the fourth corner off the plane of the other three)
        return off < 0.0005 ? 0 : off / 2 + 0.004;
    }

    /// <summary>A flat plate through four corners, the skin's thickness about them.</summary>
    private static void Plate(Builder p, double[][] q, string colour, string? finish, bool glow)
    {
        var n = HullDress.Unit(HullDress.Cross(HullDress.Sub(q[1], q[0]), HullDress.Sub(q[2], q[0])));
        var t = SecT / 2;
        Hull(p, [.. q.Select(c => new[] { c[0] - n[0] * t, c[1] - n[1] * t, c[2] - n[2] * t }), .. q.Select(c => new[] { c[0] + n[0] * t, c[1] + n[1] * t, c[2] + n[2] * t })], colour, finish, glow);
    }

    /// <summary>A small square block standing on the band at (u, v), <paramref name="d"/> out, <paramref name="size"/> across and half as proud.</summary>
    private static void Stud(Builder p, HullDress.Band band, double[] n, double u, double v, double d, double size, string colour, string? finish, bool glow)
    {
        var c = HullDress.On(band, u, v);
        var along = HullDress.Unit(HullDress.Sub(HullDress.On(band, 1, v), HullDress.On(band, 0, v)));
        var side = HullDress.Unit(HullDress.Cross(n, along));
        var h = size / 2;
        var pts = new List<double[]>();
        foreach (var dd in new[] { d, d + size * 0.5 })
            foreach (var (a, b) in new[] { (-h, -h), (h, -h), (h, h), (-h, h) })
                pts.Add([c[0] + along[0] * a + side[0] * b + n[0] * dd, c[1] + along[1] * a + side[1] * b + n[1] * dd, c[2] + along[2] * a + side[2] * b + n[2] * dd]);
        Hull(p, [.. pts], colour, finish, glow);
    }

    /// <summary>A: bolt heads along a slab's long edges, on its face, every so far (at most 12 a slab).</summary>
    private static int Bolts(Builder p, HullDress.Band band, double[] n, HullDress.Item it, DressTrim tr, DressStyle style, double len, double across, int most)
    {
        var made = 0;
        var span = (it.U1 - it.U0) * len;
        var count = Math.Min(6, (int)Math.Floor(span / tr.BoltEvery));
        if (count < 2)
            return 0;
        double inU = tr.BoltInset / len + it.Bevel / len, inV = (tr.BoltInset + it.Bevel) / Math.Max(across, 0.1);
        if (it.V1 - it.V0 < 3 * inV)
            return 0;
        for (var i = 0; i < count; i++)
        {
            var u = it.U0 + inU + (it.U1 - it.U0 - 2 * inU) * i / (count - 1);
            foreach (var v in new[] { it.V0 + inV, it.V1 - inV })
            {
                if (made >= most)
                    return made;
                Stud(p, band, n, u, v, it.D1 + 0.002, tr.BoltSize, style.Color(tr.Bolt), "dirty steel", false);
                made++;
            }
        }
        return made;
    }

    /// <summary>A: hazard chevrons at the front of a facet's lower edge: alternating light and dark slanted bars.</summary>
    private static int Chevrons(Builder p, HullDress.Band band, double[] n, DressTrim tr, DressStyle style, double len)
    {
        var across = Math.Sqrt(HullDress.Dot(HullDress.Sub(band.B0, band.A0), HullDress.Sub(band.B0, band.A0)));
        if (len < tr.ChevronLength + 0.6 || across < tr.ChevronWidth + 0.2)
            return 0;
        const int bars = 6;
        double u0 = 0.3 / len, du = tr.ChevronLength / len / bars, v0 = 0.02, v1 = v0 + tr.ChevronWidth / across, slant = du * 0.6;
        for (var i = 0; i < bars; i++)
        {
            double a = u0 + i * du;
            double[][] q = [Off(HullDress.On(band, a, v0), n, 0.026), Off(HullDress.On(band, a + du, v0), n, 0.026), Off(HullDress.On(band, a + du + slant, v1), n, 0.026), Off(HullDress.On(band, a + slant, v1), n, 0.026)];
            Plate(p, q, style.Color(i % 2 == 0 ? tr.Chevron : tr.ChevronDark), "hazard paint", false);
        }
        return bars;
    }

    private static double[] Off(double[] q, double[] n, double d) => [q[0] + n[0] * d, q[1] + n[1] * d, q[2] + n[2] * d];

    /// <summary>The highest armour face under (u, v) on a band: the stand-off a fitting on top of it needs (the skin's face where there is none).</summary>
    private static double FaceAt(List<HullDress.Item> items, double u0, double u1, double v0, double v1)
    {
        var d = SecT / 2;
        foreach (var it in items)
            if (it.U0 < u1 && it.U1 > u0 && it.V0 < v1 && it.V1 > v0)
                d = Math.Max(d, it.Strip ? it.D0 + SecT / 2 : it.D1);
        return d;
    }

    /// <summary>B: a louvred vent: a dark frame plate and its slats over it, standing on what is under it.</summary>
    private static int Vent(Builder p, HullDress.Band band, double[] n, double u, double v, List<HullDress.Item> items, DressTrim tr, DressStyle style, double len)
    {
        var across = Math.Sqrt(HullDress.Dot(HullDress.Sub(band.B0, band.A0), HullDress.Sub(band.B0, band.A0)));
        if (len < tr.VentW + 0.4 || across < tr.VentH + 0.2)
            return 0;
        double hu = tr.VentW / 2 / len, hv = tr.VentH / 2 / across;
        var d = FaceAt(items, u - hu, u + hu, v - hv, v + hv) + 0.004;
        Plate(p, HullDress.StripCorners(band, n, u - hu, u + hu, v - hv, v + hv, d + SecT / 2), style.Color(tr.Vent), "dirty steel", false);
        for (var i = 0; i < tr.VentSlats; i++)
        {
            var sv = v - hv + (i + 0.5) * 2 * hv / tr.VentSlats;
            Plate(p, HullDress.StripCorners(band, n, u - hu * 0.88, u + hu * 0.88, sv - hv * 0.28 / tr.VentSlats * 2, sv + hv * 0.28 / tr.VentSlats * 2, d + SecT + 0.004), style.Color(tr.VentSlat), "painted plating", false);
        }
        return 1;
    }

    /// <summary>D: a lamp: a dark housing and a glowing lens on it.</summary>
    private static int Lamp(Builder p, HullDress.Band band, double[] n, double u, double v, double d, double size, string colour)
    {
        Stud(p, band, n, u, v, d, size * 1.4, "#1f2226", "dirty steel", false);
        Stud(p, band, n, u, v, d + size * 0.7 + 0.003, size, colour, null, true);
        return 1;
    }

    /// <summary>F: a pipe along the band at v, on brackets every so far, standing on what is under it.</summary>
    private static int Conduit(Builder p, HullDress.Band band, double[] n, double v, List<HullDress.Item> items, DressTrim tr, DressStyle style, double len, int most)
    {
        var r = tr.PipeR;
        var lift = FaceAt(items, 0, 1, v - 0.02, v + 0.02) + 0.03 + r;
        var across = HullDress.Unit(HullDress.Sub(HullDress.On(band, 0.5, 1), HullDress.On(band, 0.5, 0)));
        double[][] Ring(double u)
        {
            var c = Off(HullDress.On(band, u, v), n, lift);
            return [.. Enumerable.Range(0, 6).Select(i =>
            {
                var a = Math.PI / 6 + i * Math.PI / 3;
                double cn = Math.Cos(a) * r, cs = Math.Sin(a) * r;
                return new[] { c[0] + n[0] * cn + across[0] * cs, c[1] + n[1] * cn + across[1] * cs, c[2] + n[2] * cn + across[2] * cs };
            })];
        }
        Hull(p, [.. Ring(3 * SecTrim / len), .. Ring(1 - 3 * SecTrim / len)], style.Color(tr.Pipe), "painted plating", false);
        var made = 1;
        var count = Math.Max(2, (int)Math.Floor(len / tr.BracketEvery));
        for (var i = 0; i < count && made < most; i++)
        {
            var u = (i + 0.5) / count;
            var under = FaceAt(items, u - 0.01, u + 0.01, v - 0.02, v + 0.02);
            var h = Math.Max(0.02, lift - r - under + 0.01);
            var c = HullDress.On(band, u, v);
            var along = HullDress.Unit(HullDress.Sub(HullDress.On(band, 1, v), HullDress.On(band, 0, v)));
            double w = 0.03, s2 = r * 1.3;
            var pts = new List<double[]>();
            foreach (var dd in new[] { under, under + h + r * 0.4 })
                foreach (var (a, b) in new[] { (-w, -s2), (w, -s2), (w, s2), (-w, s2) })
                    pts.Add([c[0] + along[0] * a + across[0] * b + n[0] * dd, c[1] + along[1] * a + across[1] * b + n[1] * dd, c[2] + along[2] * a + across[2] * b + n[2] * dd]);
            Hull(p, [.. pts], style.Color(tr.Bracket), "dirty steel", false);
            made++;
        }
        return made;
    }
}
