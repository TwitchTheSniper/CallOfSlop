using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;

namespace CallOfSlop;

/// <summary>
/// The Dress step on a plain hull skin (docs/sandbox/design/hull-dress.md, "The dress station"; Sean's option B, 2026-11-21): the Panel Former
/// makes a plain skin, a Building Station lays a dress style on it as subparts, and the Bake Oven bakes skin and dress into one part.
/// <list type="bullet">
/// <item>A <b>plain skin</b> is a hull section whose style is <c>-</c> (<c>Hull Section (600 f1-4 s - 0 - | ...)</c>): its facets in bare steel,
/// its voids and sockets, and on each facet a row of <b>dress anchors</b> (<c>d{facet}.{n}</c>, a socket on a small hidden pad just off its outer
/// face), worked out from the shape alone, so any style can be laid on any skin.</item>
/// <item>A <b>dress subpart</b> is <c>Hull Dress (KIND ARGS @F.N | the dressed section's head and profiles)</c>: one armour panel (with its bolt rows
/// when the extras have A), a chine rail, the light strip, the running lamps, the navigation light, a warning lamp, the hazard chevrons, a vent or
/// the conduit, drawn exactly as the dressed <c>Hull Section</c> draws it, in the skin's own frame, with one socket on anchor F.N. Placed on that
/// anchor it lies where the dress belongs, so a player can take any one off, put it back, or swap a panel for another pattern (<c>pN</c>).</item>
/// <item>The <b>dress blueprint</b> (<see cref="DressBlueprint"/>) is the skin plus every subpart on its anchor; a Building Station's Dress job makes
/// one for each plain skin that comes into its bay (ConstructStation.Dress.cs).</item>
/// </list>
/// </summary>
public static partial class HullLoft
{
    public const string DressPrefix = "Hull Dress (";
    public const string PlainStyle = "-";
    public const string PlainSkinColour = "#8f9196"; // (bare rolled steel: a dress step paints it the style's skin)
    public const int AnchorMost = 32; // dress anchors on one facet at most (fewer when the skin's sockets would pass ConstructParts.MaxSockets)
    public const double AnchorStep = 0.25; // ...and at least this far apart along it
    private const float AnchorPad = 0.015f; // the anchors' hidden pads, half a side (3 cm cubes)
    private const double AnchorOff = 0.006; // an anchor stands this far off the skin's outer face

    public static readonly string[] DressKinds = ["panel", "rail", "strip", "run", "nav", "warn", "chev", "vent", "pipe"];

    public static bool IsDressName(string name) => name.StartsWith(DressPrefix, StringComparison.Ordinal);

    /// <summary>A dress subpart's key read: what it is (<see cref="DressKinds"/>), its facet or index, a panel's pattern (-1: by the salt), the anchor
    /// it goes on, and the dressed section it belongs to.</summary>
    public sealed record DressKey(string Kind, int A, int B, int Pattern, int AnchorFacet, int Anchor, SectionShape Sh)
    {
        public string Key()
        {
            var sb = new StringBuilder(DressPrefix).Append(Kind);
            if (Kind == "panel")
                sb.Append(CultureInfo.InvariantCulture, $" {A}.{B}");
            else if (Kind is "rail" or "warn" or "vent")
                sb.Append(CultureInfo.InvariantCulture, $" {A}");
            if (Pattern >= 0)
                sb.Append(CultureInfo.InvariantCulture, $" p{Pattern}");
            sb.Append(CultureInfo.InvariantCulture, $" @{AnchorFacet}.{Anchor} | ");
            var sec = Sh.Key();
            return sb.Append(sec, SectionPrefix.Length, sec.Length - SectionPrefix.Length).ToString();
        }
    }

    /// <summary>A plain skin's key for a dressed section's shape (style -, salt 0, no extras).</summary>
    public static string PlainOf(SectionShape sh) => (sh with { Style = PlainStyle, Salt = 0, Extras = "" }).Key();

    /// <summary>A dress subpart's key read, or null with the reason.</summary>
    public static DressKey? DressOf(string name, out string why)
    {
        why = "";
        if (!IsDressName(name) || !name.EndsWith(')'))
            return Fail2(out why, "not a dress subpart's key");
        var bar = name.IndexOf(" | ", StringComparison.Ordinal);
        if (bar < 0)
            return Fail2(out why, "a key is: kind, its numbers, @anchor | the section's head | profile A | profile B");
        var head = name[DressPrefix.Length..bar].Split(' ');
        if (SectionOf(SectionPrefix + name[(bar + 3)..], out var swhy) is not { } sh)
            return Fail2(out why, swhy);
        if (sh.Plain)
            return Fail2(out why, "a subpart names the style it dresses in, not -");
        var style = DressStyles.Find(sh.Style)!;
        var kind = head[0];
        if (Array.IndexOf(DressKinds, kind) < 0)
            return Fail2(out why, $"a subpart is one of {string.Join(", ", DressKinds)}");
        var at = 1;
        int a = 0, b = 0, pattern = -1;
        static bool Int(string s, out int v) => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out v);
        if (kind == "panel")
        {
            var w = at < head.Length ? head[at++].Split('.') : [];
            if (w.Length != 2 || !Int(w[0], out a) || !Int(w[1], out b))
                return Fail2(out why, "a panel is panel <facet>.<panel>");
            if (a < sh.K0 || a > sh.K1 || b >= PanelCount(sh, style))
                return Fail2(out why, $"no panel {a}.{b} on this section (facets {sh.K0} to {sh.K1}, {PanelCount(sh, style)} panels a facet)");
            if (at < head.Length && head[at].StartsWith('p'))
            {
                if (!Int(head[at][1..], out pattern) || pattern >= style.Patterns.Count)
                    return Fail2(out why, $"a panel's pattern is p0 to p{style.Patterns.Count - 1}");
                at++;
            }
        }
        else if (kind is "rail" or "warn" or "vent")
        {
            if (at >= head.Length || !Int(head[at++], out a))
                return Fail2(out why, $"a {kind} has its number");
            var ok = kind switch
            {
                "rail" => a >= sh.K0 && a < sh.K1,
                "warn" => a <= 1,
                _ => a < VentCount(sh, style),
            };
            if (!ok)
                return Fail2(out why, $"no {kind} {a} on this section");
        }
        if (at != head.Length - 1 || head[at].Length < 4 || head[at][0] != '@')
            return Fail2(out why, "the anchor is @<facet>.<n>, last before the |");
        var an = head[at][1..].Split('.');
        if (an.Length != 2 || !Int(an[0], out var af) || !Int(an[1], out var ai) || af < sh.K0 || af > sh.K1 || ai >= AnchorCount(sh))
            return Fail2(out why, $"no anchor {head[at]} on this skin ({AnchorCount(sh)} a facet)");
        return new DressKey(kind, a, b, pattern, af, ai, sh);
    }

    private static DressKey? Fail2(out string why, string text)
    {
        why = text;
        return null;
    }

    public static int PanelCount(SectionShape sh, DressStyle style) => Math.Max(1, (int)Math.Round(sh.L / 100.0 / style.PanelLength));

    private static int VentCount(SectionShape sh, DressStyle style) => Math.Min(4, Math.Max(1, (int)Math.Floor(sh.L / 100.0 / style.Trim.VentEvery)));

    /// <summary>How many dress anchors each facet of a skin of this shape has.</summary>
    public static int AnchorCount(SectionShape sh)
    {
        var facets = sh.K1 - sh.K0 + 1;
        var cap = Math.Min(AnchorMost, (ConstructParts.MaxSockets - 3 * facets - 1) / facets); // (each facet's a, b and i sockets, one spare)
        return Math.Clamp((int)Math.Floor(sh.L / 100.0 / AnchorStep), 2, cap);
    }

    /// <summary>The dress anchors on facet <paramref name="k"/>: each one's point (just off the outer face, whole centimetres), the axis its socket
    /// faces out on, and its place on the facet (u along, v across). In two staggered rows a third and two thirds across.</summary>
    public static (Vector3 At, string Out, double U, double V)[] Anchors(SectionShape sh, int k)
    {
        var len = sh.L / 100.0;
        double[] Pt((int H, int Y) q, double z) => [sh.Side * q.H / 100.0, q.Y / 100.0, z];
        double axisY = (sh.A.Min(q => q.Y) + sh.A.Max(q => q.Y)) / 200.0;
        var band = new HullDress.Band(Pt(sh.A[k - 1], 0), Pt(sh.A[k], 0), Pt(sh.B[k - 1], len), Pt(sh.B[k], len), len);
        var n = HullDress.Normal(band, 0, axisY);
        var axis = Math.Abs(n[0]) >= Math.Abs(n[1]) && Math.Abs(n[0]) >= Math.Abs(n[2]) ? 0 : Math.Abs(n[1]) >= Math.Abs(n[2]) ? 1 : 2;
        var dir = (n[axis] >= 0 ? "+" : "-") + "xyz"[axis];
        var count = AnchorCount(sh);
        var r = new (Vector3, string, double, double)[count];
        for (var a = 0; a < count; a++)
        {
            double u = (a + 0.5) / count, v = a % 2 == 0 ? 1 / 3.0 : 2 / 3.0;
            var q = HullDress.On(band, u, v);
            var d = SecT / 2 + AnchorOff;
            static float Cm(double x) => MathF.Round((float)x * 100) / 100;
            r[a] = (new Vector3(Cm(q[0] + n[0] * d), Cm(q[1] + n[1] * d), Cm(q[2] + n[2] * d)), dir, u, v);
        }
        return r;
    }

    private static string Opposite(string dir) => (dir[0] == '+' ? "-" : "+") + dir[1];

    // ---- The plain skin ----------------------------------------------------------------------------------------------

    /// <summary>A plain skin's part file: the facets (bare steel, paintable), the voids behind them with their sockets, and the dress anchors.</summary>
    private static string PlainJson(string name, SectionShape sh, out SectionCounts counts)
    {
        var p = new Builder();
        double len = sh.L / 100.0;
        double[] Pt((int H, int Y) q, double z) => [sh.Side * q.H / 100.0, q.Y / 100.0, z];
        double axisY = (sh.A.Min(q => q.Y) + sh.A.Max(q => q.Y)) / 200.0;
        Vector3 F(double[] q) => new((float)q[0], (float)q[1], (float)q[2]);
        int facets = 0, hidden = 0;
        double area = 0;
        for (var k = sh.K0; k <= sh.K1; k++)
        {
            var band = new HullDress.Band(Pt(sh.A[k - 1], 0), Pt(sh.A[k], 0), Pt(sh.B[k - 1], len), Pt(sh.B[k], len), len);
            var nrm = HullDress.Normal(band, 0, axisY);
            var dz = k % 2 == 1 ? 0.006 : 0; // (the dressed section's stagger: the same skin under the dress)
            double[] Corner(double u, double v)
            {
                var q = HullDress.On(band, u, v);
                return [q[0], q[1], q[2] + dz * (u < 0.5 ? 1 : -1) + (u < 0.5 ? 0.0015 : -0.0015)];
            }
            p.Facet([F(Corner(0, 0)), F(Corner(0, 1)), F(Corner(1, 1)), F(Corner(1, 0))], F(nrm), PlainSkinColour, (float)SecT, paint: true);
            facets++;
            var across = Math.Sqrt(HullDress.Dot(HullDress.Sub(band.B0, band.A0), HullDress.Sub(band.B0, band.A0)));
            area += across * len;
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
            var anchors = Anchors(sh, k);
            for (var a = 0; a < anchors.Length; a++)
                p.Pad($"d{k}.{a}", anchors[a].At, anchors[a].Out, AnchorPad);
        }
        var mass = (float)Math.Round(area * 60, 1);
        counts = new SectionCounts(facets, 0, 0, 0, 0, 0, 0, 0, 0, hidden, p.Sockets.Count, mass);
        return new JsonObject
        {
            ["format"] = "call-of-slop-construct-part/1",
            ["name"] = name,
            ["description"] = $"A plain hull skin, {sh.L / 100.0:0.##} m long: bare plate on its frame, with dress anchors on every facet. A Building Station's Dress job lays a style on it; a Bake Oven bakes the two into one piece.",
            ["group"] = "Ship",
            ["section"] = "Hull sections",
            ["role"] = "frame",
            ["tool"] = "welder",
            ["mass"] = mass,
            ["health"] = MathF.Round(Math.Clamp(mass * 7, 200, 9000)),
            ["armour"] = 30,
            ["material"] = "steel",
            ["color"] = PlainSkinColour,
            ["attachSeconds"] = 6.0,
            ["stage"] = true,
            ["boxes"] = p.Boxes,
            ["sockets"] = p.Sockets,
        }.ToJsonString();
    }

    // ---- Dress subparts ----------------------------------------------------------------------------------------------

    private static (bool Ok, ConstructPartDef? Def)? MakeDress(string name)
    {
        if (!IsDressName(name))
            return null;
        if (DressOf(name, out var why) is not { } dk)
        {
            Console.WriteLine($"[hull] {name}: REFUSED, {why}");
            return (false, null);
        }
        try
        {
            var json = DressJson(name, dk, out var boxes);
            if (boxes == 0)
            {
                Console.WriteLine($"[hull] {name}: REFUSED, nothing of it fits this section (too small for it)");
                return (false, null);
            }
            var def = Adopt(ConstructParts.Parse(json, name), name);
            return (def != null, def);
        }
        catch (ConstructPartException e)
        {
            Console.WriteLine($"[hull] {name}: REFUSED, {e.Message}");
            return (false, null);
        }
    }

    /// <summary>What one subpart draws, into <paramref name="p"/>: the dressed section's own arithmetic for that piece (HullLoft.Section.cs). Its mass, kg.</summary>
    private static double DrawDress(Builder p, DressKey dk)
    {
        var sh = dk.Sh;
        var style = DressStyles.Find(sh.Style)!;
        var tr = style.Trim;
        double len = sh.L / 100.0;
        double[] Pt((int H, int Y) q, double z) => [sh.Side * q.H / 100.0, q.Y / 100.0, z];
        var n = sh.A.Length;
        double axisY = (sh.A.Min(q => q.Y) + sh.A.Max(q => q.Y)) / 200.0;
        var beam = Enumerable.Range(0, n).MaxBy(i => sh.A[i].H + sh.B[i].H);
        HullDress.Band BandOf(int k) => new(Pt(sh.A[k - 1], 0), Pt(sh.A[k], 0), Pt(sh.B[k - 1], len), Pt(sh.B[k], len), len);
        double Across(HullDress.Band b) => Math.Sqrt(HullDress.Dot(HullDress.Sub(b.B0, b.A0), HullDress.Sub(b.B0, b.A0)));
        switch (dk.Kind)
        {
            case "panel":
            {
                var band = BandOf(dk.A);
                var nrm = HullDress.Normal(band, 0, axisY);
                var across = Across(band);
                double armour = 0;
                var items = PanelItems(band, style, dk.A, dk.B, sh.Salt, dk.Pattern);
                foreach (var it in items)
                {
                    if (it.Strip)
                    {
                        Plate(p, HullDress.StripCorners(band, nrm, it.U0, it.U1, it.V0, it.V1, it.D0 + StripLift(band)), it.Color, it.Finish, false);
                        continue;
                    }
                    var (inner, outer) = HullDress.SlabOutlines(band, nrm, it.U0, it.U1, it.V0, it.V1, it.D0, it.D1, it.Bevel);
                    Hull(p, [.. inner.Concat(outer)], it.Color, it.Finish, false);
                    armour += (it.U1 - it.U0) * len * (it.V1 - it.V0) * across;
                }
                if (sh.Has('A'))
                {
                    var bolts = 0;
                    foreach (var it in items)
                        if (!it.Strip)
                            bolts += Bolts(p, band, nrm, it, tr, style, len, across, Math.Max(0, 96 - bolts));
                }
                return armour * 90;
            }
            case "rail":
            {
                var i = dk.A;
                var isBeam = i == beam;
                double w = isBeam ? tr.BeamW : tr.RailW, h = isBeam ? tr.BeamH : tr.RailH;
                double[][] Ring((int H, int Y)[] prof, double z) => HullDress.ChineRing(Pt(prof[i - 1], z), Pt(prof[i], z), Pt(prof[i + 1], z), w, h);
                Hull(p, [.. Ring(sh.A, TrimOf(len)), .. Ring(sh.B, len - TrimOf(len))], style.Color(isBeam ? tr.BeamRail : tr.Rail), "plated steel", false);
                return len * w * h * 2000;
            }
            case "strip" or "run" or "nav":
            {
                if (StripSpot(sh, tr, beam, BandOf, Across) is not var (k, v0, v1))
                    return 0;
                var band = BandOf(k);
                var nrm = HullDress.Normal(band, 0, axisY);
                if (dk.Kind == "strip")
                {
                    Plate(p, HullDress.StripCorners(band, nrm, 2 * TrimOf(len) / len, 1 - 2 * TrimOf(len) / len, v0, v1, 0.025), style.Color(tr.Strip), null, true);
                    return len * 2;
                }
                if (dk.Kind == "nav")
                    return 3 * Lamp(p, band, nrm, Math.Min(0.4, 0.25 / len), (v0 + v1) / 2, 0.025, tr.LampSize, style.Color(sh.Side < 0 ? tr.Port : tr.Starboard));
                var runs = Math.Max(1, (int)Math.Floor(len / tr.RunEvery));
                var lamps = 0;
                for (var r = 0; r < runs && lamps < 12; r++)
                    lamps += Lamp(p, band, nrm, (r + 0.5) / runs, (v0 + v1) / 2, 0.025, tr.LampSize * 0.7, style.Color(tr.Running));
                return lamps * 2;
            }
            case "warn":
            {
                var band = BandOf(sh.K1);
                var u = dk.A == 0 ? Math.Min(0.3, 0.3 / len) : Math.Max(0.7, 1 - 0.3 / len);
                return 2 * Lamp(p, band, HullDress.Normal(band, 0, axisY), u, 0.96, 0.03, tr.LampSize * 0.8, style.Color(tr.Warning));
            }
            case "chev":
            {
                var band = BandOf(sh.K0);
                return Chevrons(p, band, HullDress.Normal(band, 0, axisY), tr, style, len) > 0 ? 6 : 0;
            }
            case "vent":
            {
                var k = sh.K1 > sh.K0 ? sh.K1 - 1 : sh.K0;
                var band = BandOf(k);
                var items = HullDress.Plates(band, style, k, 0, sh.Salt);
                var count = Math.Max(1, (int)Math.Floor(len / tr.VentEvery));
                return 10 * Vent(p, band, HullDress.Normal(band, 0, axisY), (dk.A + 0.5) / count, 0.5, items, tr, style, len);
            }
            case "pipe":
            {
                var top = sh.K1;
                if (top <= sh.K0)
                    return 0;
                var band = BandOf(top);
                var items = HullDress.Plates(band, style, top, 0, sh.Salt);
                var v = Math.Min(0.45, (tr.RailW + tr.PipeR + 0.06) / Math.Max(Across(band), 0.1));
                return 8 * Conduit(p, band, HullDress.Normal(band, 0, axisY), v, items, tr, style, len, 64);
            }
        }
        return 0;
    }

    /// <summary>The light strip's facet and its edges across (as the dressed section has it), or null when it doesn't lie on this section.</summary>
    private static (int K, double V0, double V1)? StripSpot(SectionShape sh, DressTrim tr, int beam, Func<int, HullDress.Band> bandOf, Func<HullDress.Band, double> acrossOf)
    {
        if (beam + 1 < sh.K0 || beam + 1 > sh.K1)
            return null;
        var k = beam + 1;
        var across = acrossOf(bandOf(k));
        double v0 = (tr.StripOff + (beam >= sh.K0 && beam < sh.K1 ? tr.BeamW * 0.5 : 0)) / Math.Max(across, 0.1), v1 = v0 + tr.StripW / Math.Max(across, 0.1);
        return v1 < 0.5 ? (k, v0, v1) : null;
    }

    /// <summary>One armour panel's layers (<see cref="HullDress.Plates"/> for panel <paramref name="i"/> alone), in <paramref name="pattern"/> or the salt's.</summary>
    private static List<HullDress.Item> PanelItems(HullDress.Band band, DressStyle style, int facet, int i, int salt, int pattern)
    {
        var items = new List<HullDress.Item>();
        var len = band.Length;
        var n = Math.Max(1, (int)Math.Round(len / style.PanelLength));
        double u0 = (double)i / n + style.PanelGap / len * 4, u1 = (double)(i + 1) / n - style.PanelGap / len * 4;
        var pick = pattern >= 0 ? pattern : (facet * 7 + i * 3 + salt * 5) % style.Patterns.Count;
        foreach (var l in style.Patterns[pick])
            items.Add(new HullDress.Item(l.Strip, l.U0.At(u0, u1), l.U1.At(u0, u1), l.V0.At(0.07, 0.93), l.V1.At(0.07, 0.93), l.D0, l.D1, l.Bevel, style.Color(l.Color), l.Finish));
        return items;
    }

    /// <summary>A subpart's part file: what it draws, and its one socket on its anchor (facing the skin's anchor) with its pad.</summary>
    private static string DressJson(string name, DressKey dk, out int boxes)
    {
        var p = new Builder();
        var mass = DrawDress(p, dk);
        boxes = p.Boxes.Count;
        var anchor = Anchors(dk.Sh, dk.AnchorFacet)[dk.Anchor];
        p.Pad("skin", anchor.At, Opposite(anchor.Out), AnchorPad);
        var style = DressStyles.Find(dk.Sh.Style)!;
        var what = dk.Kind switch
        {
            "panel" => $"An armour panel of {style.Label} hull dress",
            "rail" => $"A chine rail of {style.Label} hull dress",
            "strip" => $"The light strip of {style.Label} hull dress",
            "run" => $"The running lamps of {style.Label} hull dress",
            "nav" => $"A navigation light of {style.Label} hull dress",
            "warn" => $"A warning lamp of {style.Label} hull dress",
            "chev" => $"Hazard chevrons of {style.Label} hull dress",
            "vent" => $"A vent of {style.Label} hull dress",
            _ => $"Outside conduit of {style.Label} hull dress",
        };
        var m = (float)Math.Round(Math.Max(1, mass), 1);
        return new JsonObject
        {
            ["format"] = "call-of-slop-construct-part/1",
            ["name"] = name,
            ["description"] = $"{what}, made for one spot on a plain hull skin: it goes on that skin's dress anchor. A Bake Oven bakes it into the skin.",
            ["group"] = "Ship",
            ["section"] = "Hull dress",
            ["role"] = "frame",
            ["tool"] = "welder",
            ["mass"] = m,
            ["health"] = MathF.Round(Math.Clamp(m * 7, 20, 3000)),
            ["armour"] = dk.Kind == "panel" ? 40 : 10,
            ["material"] = "steel",
            ["color"] = style.Color("armour"),
            ["attachSeconds"] = 2.0,
            ["stage"] = true,
            ["boxes"] = p.Boxes,
            ["sockets"] = p.Sockets,
        }.ToJsonString();
    }

    // ---- The dress blueprint -----------------------------------------------------------------------------------------

    public const string DressJobName = "Dress skins"; // a Building Station's Dress job, waiting for its next plain skin (ConstructStation.Dress.cs)
    public const string DressBlueprintPrefix = "Dress ";

    /// <summary>The extras a station's Dress Extras setting gives, by its choice.</summary>
    public static readonly string[] DressExtraChoices = ["ABDF", "ABD", "ABF", "AB", ""];

    public static bool IsDressBlueprintName(string name) => name.StartsWith(DressBlueprintPrefix, StringComparison.Ordinal) && name.Contains(": " + SectionPrefix, StringComparison.Ordinal);

    /// <summary>A dress blueprint's name: <c>Dress it 4 ABDF: Hull Section (... - 0 - ...)</c>, the style, salt and extras, then the plain skin's key.</summary>
    public static string DressBlueprintName(string skin, string style, int salt, string extras) =>
        $"{DressBlueprintPrefix}{style} {salt} {(extras.Length == 0 ? "-" : extras)}: {skin}";

    /// <summary>
    /// The subparts a style lays on a section, in order (panels facet by facet, rails, the strip, the lamps, chevrons, vents, conduit), each on the
    /// free anchor of its facet nearest its own middle. A piece that draws nothing on this section is left out; one with no anchor left is left out
    /// with a log line.
    /// </summary>
    public static List<DressKey> DressPlan(SectionShape dressed)
    {
        var sh = dressed;
        var style = DressStyles.Find(sh.Style)!;
        var tr = style.Trim;
        var len = sh.L / 100.0;
        double[] Pt((int H, int Y) q, double z) => [sh.Side * q.H / 100.0, q.Y / 100.0, z];
        HullDress.Band BandOf(int k) => new(Pt(sh.A[k - 1], 0), Pt(sh.A[k], 0), Pt(sh.B[k - 1], len), Pt(sh.B[k], len), len);
        double Across(HullDress.Band b) => Math.Sqrt(HullDress.Dot(HullDress.Sub(b.B0, b.A0), HullDress.Sub(b.B0, b.A0)));
        var beam = Enumerable.Range(0, sh.A.Length).MaxBy(i => sh.A[i].H + sh.B[i].H);
        var want = new List<(string Kind, int A, int B, int Facet, double U, double V)>();
        var panels = PanelCount(sh, style);
        for (var k = sh.K0; k <= sh.K1; k++)
            for (var i = 0; i < panels; i++)
                want.Add(("panel", k, i, k, (i + 0.5) / panels, 0.5));
        for (var i = sh.K0; i < sh.K1; i++)
            want.Add(("rail", i, 0, i, 0.5, 1));
        if (StripSpot(sh, tr, beam, BandOf, Across) is var (sk, v0, _))
        {
            want.Add(("strip", 0, 0, sk, 0.5, v0));
            if (sh.Has('D'))
            {
                want.Add(("run", 0, 0, sk, 0.5, v0));
                want.Add(("nav", 0, 0, sk, 0, v0));
            }
        }
        if (sh.Has('D'))
        {
            want.Add(("warn", 0, 0, sh.K1, 0, 1));
            want.Add(("warn", 1, 0, sh.K1, 1, 1));
        }
        if (sh.Has('A'))
            want.Add(("chev", 0, 0, sh.K0, 0, 0));
        if (sh.Has('B'))
        {
            var vk = sh.K1 > sh.K0 ? sh.K1 - 1 : sh.K0;
            var count = Math.Max(1, (int)Math.Floor(len / tr.VentEvery));
            for (var i = 0; i < Math.Min(4, count); i++)
                want.Add(("vent", i, 0, vk, (i + 0.5) / count, 0.5));
        }
        if (sh.Has('F') && sh.K1 > sh.K0)
            want.Add(("pipe", 0, 0, sh.K1, 0.5, 0.2));
        var free = new Dictionary<int, List<(int Index, double U, double V)>>();
        for (var k = sh.K0; k <= sh.K1; k++)
            free[k] = [.. Anchors(sh, k).Select((x, a) => (a, x.U, x.V))];
        var plan = new List<DressKey>();
        foreach (var w in want)
        {
            var probe = new DressKey(w.Kind, w.A, w.B, -1, w.Facet, 0, sh);
            if (DrawDress(new Builder(), probe) <= 0 && w.Kind != "panel")
                continue; // (nothing of it fits this section: as the dressed section leaves it out)
            var list = free[w.Facet];
            if (list.Count == 0)
            {
                Console.WriteLine($"[dress] {sh.Key()}: {w.Kind} {w.A} left out: no dress anchor left on facet {w.Facet}");
                continue;
            }
            var best = 0;
            for (var a = 1; a < list.Count; a++)
                if (Dist(list[a], w) < Dist(list[best], w))
                    best = a;
            plan.Add(probe with { Pattern = w.Kind == "panel" ? (w.A * 7 + w.B * 3 + sh.Salt * 5) % style.Patterns.Count : -1, Anchor = list[best].Index });
            list.RemoveAt(best);
        }
        return plan;

        static double Dist((int Index, double U, double V) a, (string, int, int, int, double U, double V) w) => (a.U - w.U) * (a.U - w.U) + (a.V - w.V) * (a.V - w.V) * 0.25;
    }

    /// <summary>
    /// The dress blueprint for a plain skin: the skin (painted the style's skin colour) and each subpart on its anchor. Null with the reason when
    /// it can't be made (not a plain skin, no such style, a key too long for a part's name or the station's).
    /// </summary>
    public static ConstructBuild.Blueprint? DressBlueprint(string skin, string style, int salt, string extras, out string why)
    {
        why = "";
        var plain = SectionOf(skin, out var swhy);
        if (plain is not { Plain: true })
        {
            why = plain == null ? $"not a hull skin: {swhy}" : "that skin is dressed already";
            return null;
        }
        if (DressStyles.Find(style) is not { } st)
        {
            why = $"no dress style \"{style}\"";
            return null;
        }
        var dressed = plain with { Style = st.Name, Salt = Math.Clamp(salt, 0, SectionMaxSalt), Extras = new string([.. SectionExtras.Where(extras.Contains)]) };
        var name = DressBlueprintName(skin, st.Name, dressed.Salt, dressed.Extras);
        if (Encoding.UTF8.GetByteCount(name) > 255)
        {
            why = "that skin's key is too long to name its dress job";
            return null;
        }
        var plan = DressPlan(dressed);
        var skinDef = ConstructParts.Find(skin);
        if (skinDef == null)
        {
            why = "the skin's part couldn't be made (callofslop.log has the REFUSED line)";
            return null;
        }
        var steps = new JsonArray { new JsonObject { ["part"] = skin, ["paint"] = st.Color("skin") } };
        Span<byte> turns = stackalloc byte[4];
        foreach (var dk in plan)
        {
            var key = dk.Key();
            if (key.Length > ConstructParts.MaxRuntimeName)
            {
                Console.WriteLine($"[dress] {key[..60]}...: left out, its key is {key.Length} characters (the most {ConstructParts.MaxRuntimeName})");
                continue;
            }
            if (ConstructParts.Find(key) is not { } d)
                continue; // (refused: its REFUSED line is in the log)
            var sock = $"d{dk.AnchorFacet}.{dk.Anchor}";
            var ts = Array.FindIndex(skinDef.Sockets, s => s.Name == sock);
            if (ts < 0)
                continue;
            var tn = ConstructBuild.SpinTurns(d.Sockets[0].Dir, skinDef.Sockets[ts].Dir, turns);
            var spin = turns[..tn].IndexOf((byte)0); // (no turn: the subpart is drawn in the skin's own frame)
            if (spin < 0)
                continue;
            steps.Add(new JsonObject { ["part"] = key, ["on"] = 0, ["socket"] = sock, ["by"] = "skin", ["spin"] = spin });
        }
        var json = new JsonObject
        {
            ["format"] = ConstructBuild.BlueprintFormat,
            ["name"] = name,
            ["description"] = $"A plain hull skin dressed in {st.Label} (salt {dressed.Salt}), {steps.Count - 1} subparts.",
            ["steps"] = steps,
        }.ToJsonString();
        try
        {
            return ConstructBuild.ParseBlueprint(json, "(dress)");
        }
        catch (ConstructPartException e)
        {
            why = e.Message;
            return null;
        }
    }

    /// <summary>A dress blueprint by its name (<see cref="DressBlueprintName"/>), as a saved job names it. Null: not one, or it can't be made.</summary>
    public static ConstructBuild.Blueprint? DressBlueprintOf(string name)
    {
        if (!IsDressBlueprintName(name))
            return null;
        var colon = name.IndexOf(": ", StringComparison.Ordinal);
        var w = name[DressBlueprintPrefix.Length..colon].Split(' ');
        if (w.Length != 3 || !int.TryParse(w[1], NumberStyles.None, CultureInfo.InvariantCulture, out var salt))
            return null;
        return DressBlueprint(name[(colon + 2)..], w[0], salt, w[2] == "-" ? "" : w[2], out _);
    }

    /// <summary><c>--dress-skin [KEY|example] [STYLE] [SALT] [EXTRAS]</c>: a plain skin's dress plan, its subparts' keys and counts, built once.</summary>
    public static int DressCli(string[] args)
    {
        var skin = args.Length == 0 || args[0] == "example" ? PlainOf(SectionOf(SectionExample, out _)!) : args[0];
        var style = args.Length > 1 ? args[1] : "it";
        var salt = args.Length > 2 && int.TryParse(args[2], out var s) ? s : 4;
        var extras = args.Length > 3 ? (args[3] == "-" ? "" : args[3]) : SectionExtras;
        var sh = SectionOf(skin, out var why);
        if (sh is not { Plain: true })
        {
            Console.WriteLine($"refused: {(sh == null ? why : "not a plain skin (style -)")}");
            return 1;
        }
        if (ConstructParts.Find(skin) is not { } sd)
        {
            Console.WriteLine("refused: the skin's part was refused (callofslop.log has the REFUSED line)");
            return 1;
        }
        Console.WriteLine(skin);
        Console.WriteLine($"  skin: {sd.Boxes.Length} boxes, {sd.Sockets.Length} sockets ({AnchorCount(sh)} dress anchors a facet), mass {sd.Mass:0.#} kg");
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (DressBlueprint(skin, style, salt, extras, out why) is not { } bp)
        {
            Console.WriteLine($"refused: {why}");
            return 1;
        }
        var ms = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        Console.WriteLine($"  {bp.Name[..Math.Min(bp.Name.Length, 40)]}...: {bp.Steps.Count - 1} subparts, planned and made in {ms:0} ms");
        var bad = 0;
        foreach (var g in bp.Steps.Skip(1).GroupBy(x => x.Part[DressPrefix.Length..].Split(' ')[0]))
            Console.WriteLine($"    {g.Key}: {g.Count()}");
        foreach (var st in bp.Steps.Skip(1).Take(3))
            Console.WriteLine($"    e.g. {st.Part} on {st.Socket}");
        var world = new ConstructWorld();
        if (ConstructBuild.Build(world, bp, Vector3.Zero, Quaternion.Identity, out var bwhy) is not { } built)
        {
            Console.WriteLine($"refused: the dress blueprint doesn't build: {bwhy}");
            return 1;
        }
        Console.WriteLine($"  built: {built.PartCount} parts, {built.LinkCount} links (every subpart welded to the skin: {(built.LinkCount >= built.PartCount - 1 ? "yes" : "NO")})");
        if (built.LinkCount < built.PartCount - 1)
            bad++;
        var all = new List<ZFight.ZBox>();
        var off = 0;
        for (var i = 0; i < built.PartCount; i++)
        {
            if (built.Parts[i].Pos != built.Parts[0].Pos || built.Parts[i].Rot != built.Parts[0].Rot)
                off++;
            all.AddRange(ZFight.Boxes(built.DefOf(i)));
        }
        Console.WriteLine($"  subparts off the skin's own frame: {off}");
        var zf = ZFight.Report("dressed skin", all, max: 12);
        Console.WriteLine(zf.Count == 0 ? "  coplanar faces: none" : $"  coplanar faces:\n    {string.Join("\n    ", zf)}");
        return bad + off + zf.Count;
    }
}
