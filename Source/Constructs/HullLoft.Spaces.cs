using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;

namespace CallOfSlop;

/// <summary>
/// Body styles phase 5 (docs/vehiclekit/BODY-STYLES.md, section 11): new spaces, made at run time and named by their keys as a lofted bay is.
/// <list type="bullet">
/// <item>A <b>caged bay</b> (a bay's key with the fourth word <c>c</c> and a pattern): the bay's two sections drawn as a frame of bars you can see and shoot through, rails
/// along every point of the section and between them, bows at its ends and middle, and the faction's infill: <c>w</c> welded diagonals (Bandit), <c>s</c> slats low down
/// and a mesh of rails above (Militia), <c>t</c> timber stakes (Highland). Its hidden boxes are the bars' own, so a player is held in by the rails and a round passes
/// between them.</item>
/// <item>An <b>upper floor</b> (<c>u</c>): a level roof whose slab is walked on, with sockets on the quarter grid for an upper storey's feet and its furniture, cut
/// round a hatch where a stair comes up (<c>uX0,X1,Z0,Z1</c>); an <b>open top</b> (<c>o</c>): a bay with no roof row, the bulwark round an open upper deck.</item>
/// <item>A <b>stair</b> (<c>Hull Loft (stair ...)</c>): treads up from a room's floor to the hatch, walked up (a ship ladder's walk).</item>
/// <item>A <b>side pod</b> (<c>Hull Loft (pod...)</c>): a compartment hung on a bay's waist socket, its profile the faction's (a welded box, a smooth blister, a kiosk
/// with a window, a clinker locker), cut for the bay's own sections like a trim.</item>
/// </list>
/// </summary>
public static partial class HullLoft
{
    /// <summary>A caged bay's patterns: <c>w</c> welded diagonals (Bandit), <c>s</c> slats and a mesh (Militia), <c>t</c> timber stakes (Highland).</summary>
    public const string CageKinds = "wst";

    public const string StairWord = "stair", PodWord = "pod";

    /// <summary>A pod's kinds: <c>w</c> a welded box with a sloped lid (Bandit), <c>b</c> a smooth blister (IT), <c>k</c> a kiosk with a window and a sign band (Scam), <c>l</c> a clinker locker (Highland), <c>a</c> an armoured stowage pod (Militia), <c>g</c> a luggage locker (British).</summary>
    public const string PodKinds = "wbklag";

    private const string BarSteel = "#55524c", Timber = "#6b4a2b", SlatGreen = "#56604a", Tread = "#5d6268", Window = "#29343c";
    private const float RailHalf = 0.02f, PostHalf = 0.025f;

    // ---- Caged bays ---------------------------------------------------------------------------------------------

    /// <summary>A bar from <paramref name="a"/> to <paramref name="b"/>, <paramref name="w"/> wide across and <paramref name="t"/> thick along <paramref name="n"/>, drawn; its hidden boxes stepped along it (a round stops on it, a player is held by it).</summary>
    /// <param name="zLo">The hidden boxes stay at or behind this z, and in front of <paramref name="zHi"/> (a bow on a bay's end reaches into its neighbour's drawn, not its solid).</param>
    private static void Bar(Builder p, Vector3 a, Vector3 b, Vector3 n, float w, float t, string colour, bool hide = true, bool paint = true, float zLo = float.MinValue, float zHi = float.MaxValue)
    {
        n = Vector3.Normalize(n);
        var along = b - a;
        if (along.LengthSquared() < 1e-6f)
            return;
        var side = Vector3.Cross(Vector3.Normalize(along), n);
        if (side.LengthSquared() < 1e-6f)
            return;
        side = Vector3.Normalize(side) * (w / 2);
        p.Facet([a - side, a + side, b + side, b - side], n, colour, t, paint);
        if (!hide)
            return;
        var ext = Vector3.Abs(along);
        var off = ext.X + ext.Y + ext.Z - MathF.Max(ext.X, MathF.Max(ext.Y, ext.Z)); // (how far it strays off its main axis: a rail along the bay is one box, a slanted bow is stepped)
        var steps = Math.Clamp((int)MathF.Ceiling(off / 0.15f), 1, 6);
        var th = n * (t / 2);
        for (var i = 0; i < steps; i++)
        {
            var (c0, c1) = (a + along * i / steps, a + along * (i + 1) / steps);
            var (mn, mx) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
            foreach (var c in (Vector3[])[c0 - side - th, c0 + side - th, c0 - side + th, c0 + side + th, c1 - side - th, c1 + side - th, c1 - side + th, c1 + side + th])
                (mn, mx) = (Vector3.Min(mn, c), Vector3.Max(mx, c));
            var inset = new Vector3(0.004f, 0.004f, 0.008f); // (along the bay, more: a bay's place rounds to the centimetre, so neighbours' ends may come 5 mm nearer: the 2026-11-07 lesson)
            var (lo, hi) = (mn + inset, mx - inset);
            (lo.Z, hi.Z) = (MathF.Max(lo.Z, zLo), MathF.Min(hi.Z, zHi));
            p.Hide(lo, hi);
        }
    }

    private static Vector3 At(Vector2 p, float s, float z) => new(s * p.X, p.Y, z);

    /// <summary>The section's point k at a fraction <paramref name="u"/> along the bay (a lofted bay's own place there).</summary>
    private static Vector2 Lerp(Section a, Section b, int k, float u) => Vector2.Lerp(a.P[k], b.P[k], u);

    /// <summary>
    /// A caged bay's sides and roof (phase 5): rails along the bay at every point of its section (the lowest 3 cm up, off the floor's plane) and between them every
    /// pattern's spacing, bows across at the bay's front, middle and (closed) back, and the pattern's infill. A door bay keeps its flat frame round the doorway.
    /// </summary>
    private static void CageSides(Builder p, Shape sh)
    {
        var (a, b, len, door, pat) = (sh.A, sh.B, sh.Length, sh.Door, sh.Cage);
        var colour = pat == 't' ? Timber : BarSteel;
        var n = a.P.Length;
        var (ma, mb) = (Mitres(a), Mitres(b));
        // Rows below the doorway's top on its side are the frame's (+x of a door bay).
        bool Doorway(float s, Vector2 q0, Vector2 q1) => door && s > 0 && MathF.Max(q0.Y, q1.Y) <= DoorTop + 0.05f;
        Vector2 Lift(Vector2 q, int k) => k == 0 ? q with { Y = q.Y + 0.03f } : q; // (the floor rail off the floor's plane)
        var spacing = pat switch { 's' => 0.42f, _ => 0.48f };
        foreach (var s in (float[])[-1, 1])
        {
            // Rails along the bay at each point, and between them on long rows.
            for (var k = 0; k < n; k++)
            {
                if (a.P[k].X < 0.001f && s > 0)
                    continue; // (the centreline's rail once)
                var (pa, pb) = (Lift(a.P[k], k), Lift(b.P[k], k));
                if (door && s > 0 && pa.Y < DoorTop + 0.05f)
                    continue; // (the doorway's side: its frame, below)
                var m = Vector3.Normalize(new Vector3(s * (ma[k].X + mb[k].X) / 2, (ma[k].Y + mb[k].Y) / 2, 0));
                Bar(p, At(pa, s, 0), At(pb, s, len), m, 2 * RailHalf, 2 * RailHalf, colour);
            }
            if (door && s > 0)
                Bar(p, new Vector3(DoorPlane, DoorTop + 0.03f, 0.125f), new Vector3(DoorPlane, DoorTop + 0.03f, len - 0.125f), Vector3.UnitX, 2 * RailHalf, 2 * RailHalf, colour); // (the lintel)
            for (var k = 0; k + 1 < n; k++)
            {
                var (a0, a1, b0, b1) = (Lift(a.P[k], k), a.P[k + 1], Lift(b.P[k], k), b.P[k + 1]);
                if (Doorway(s, a0, a1) || a0.X < 0.001f && a1.X < 0.001f)
                    continue;
                var nr = RowNormal(a0, a1);
                var wall = MathF.Abs(a1.Y - a0.Y) >= MathF.Abs(a1.X - a0.X);
                var gap = pat == 's' && wall && (a0.Y + a1.Y) / 2 < 1.6f ? 0.14f : spacing; // (Militia slats below 1.6 m)
                var count = (int)MathF.Ceiling(Vector2.Distance(a0, a1) / gap) - 1;
                for (var i = 1; i <= count; i++)
                {
                    var u = (float)i / (count + 1);
                    var (qa, qb) = (Vector2.Lerp(a0, a1, u), Vector2.Lerp(b0, b1, u));
                    if (door && s > 0 && qa.Y < DoorTop + 0.05f)
                        continue;
                    var nn = new Vector3(s * nr.X, nr.Y, 0);
                    if (pat == 's' && gap < 0.2f)
                        Bar(p, At(qa, s, 0) + nn * 0.004f, At(qb, s, len) + nn * 0.004f, nn, 0.09f, 0.02f, SlatGreen); // (a slat: a flat board, its faces off the rails')
                    else
                        Bar(p, At(qa, s, 0), At(qb, s, len), nn, 0.035f, 0.035f, colour, hide: wall);
                }
            }
            // Bows across: inside each end (a neighbour's bow stands against this one's, faces to faces: a bay doesn't know if it stands turned round) and the middle.
            var stations = new List<float> { PostHalf, len - PostHalf };
            if (len >= 0.7f)
                stations.Add(len / 2);
            foreach (var z in stations)
            {
                var u = z / len;
                for (var k = 0; k + 1 < n; k++)
                {
                    var (q0, q1) = (Lerp(a, b, k, u), Lerp(a, b, k + 1, u));
                    if (Doorway(s, q0, q1) || q0.X < 0.001f && q1.X < 0.001f)
                        continue;
                    if (door && s > 0 && q0.Y < DoorTop + 0.05f)
                        q0 = new Vector2(DoorPlane, DoorTop);
                    var nr = RowNormal(q0, q1);
                    var upright = MathF.Abs(q1.Y - q0.Y) >= MathF.Abs(q1.X - q0.X);
                    Bar(p, At(q0, s, z), At(q1, s, z), new Vector3(s * nr.X, nr.Y, 0), 2 * PostHalf, 2 * PostHalf, colour, hide: upright, zLo: 0.009f, zHi: len - 0.009f); // (a roof bow drawn only: the rails hold)
                }
            }
            // The faction's infill.
            for (var k = 0; k + 1 < n; k++)
            {
                var (a0, a1, b0, b1) = (a.P[k], a.P[k + 1], b.P[k], b.P[k + 1]);
                if (Doorway(s, a0, a1) || a0.X < 0.001f && a1.X < 0.001f)
                    continue;
                if (door && s > 0 && a0.Y < DoorTop + 0.05f)
                    (a0, b0) = (new Vector2(DoorPlane, DoorTop), new Vector2(DoorPlane, DoorTop));
                var wall = MathF.Abs(a1.Y - a0.Y) >= MathF.Abs(a1.X - a0.X);
                var nr = RowNormal(a0, a1);
                var nn = new Vector3(s * nr.X, nr.Y, 0);
                if (pat == 'w' && wall && a1.Y - a0.Y > 0.4f)
                {
                    // A welded diagonal across each half of the row, alternating, just inside the rails' outer face (drawn only: the rails hold).
                    var halves = len >= 0.7f ? 2 : 1;
                    for (var h = 0; h < halves; h++)
                    {
                        var (u0, u1) = ((float)h / halves, (float)(h + 1) / halves);
                        var up = (h + k) % 2 == 0;
                        var (lo, hi) = (Vector2.Lerp(a0, b0, up ? u0 : u1), Vector2.Lerp(a1, b1, up ? u1 : u0));
                        Bar(p, At(lo, s, len * (up ? u0 : u1)) - nn * 0.012f, At(hi, s, len * (up ? u1 : u0)) - nn * 0.012f, nn, 0.03f, 0.018f, colour, hide: false);
                    }
                }
                else if (pat == 't' && wall && a1.Y - a0.Y > 0.3f)
                {
                    // Timber stakes every 0.2 m along the row, between the bows, just outside the rails.
                    var stakes = Math.Max(1, (int)MathF.Round(len / 0.2f));
                    for (var i = 0; i < stakes; i++)
                    {
                        var u = (i + 0.5f) / stakes;
                        if (MathF.Abs(u * len - len / 2) < 0.06f || u * len < 0.06f || len - u * len < 0.06f)
                            continue; // (a bow there)
                        var (q0, q1) = (Vector2.Lerp(a0, b0, u), Vector2.Lerp(a1, b1, u));
                        Bar(p, At(q0, s, u * len) + nn * 0.03f, At(q1, s, u * len) + nn * 0.03f, nn, 0.06f, 0.024f, Timber);
                    }
                }
            }
        }
        if (door)
        {
            // The flat frame round the doorway, as a skinned door bay's (Sides).
            foreach (var (fz0, fz1) in new[] { (0f, 0.125f), (len - 0.125f, len) })
            {
                p.Facet([new(DoorPlane, 0, fz0), new(DoorPlane, DoorTop, fz0), new(DoorPlane, DoorTop, fz1), new(DoorPlane, 0, fz1)], Vector3.UnitX, Rust);
                p.Hide(new Vector3(DoorPlane - 0.03f, 0, MathF.Max(fz0, 0.01f)), new Vector3(DoorPlane + 0.03f, DoorTop, MathF.Min(fz1, len - 0.01f)));
            }
            p.Hide(new Vector3(DoorPlane - 0.03f, DoorTop, 0), new Vector3(DoorPlane + 0.03f, DoorTop + 0.1f, len));
        }
        // The roof's lever pad hangs under the highest bow's middle.
        p.Centre = MathF.Min(p.Centre, MathF.Min(a.Top, b.Top) - 0.05f);
    }

    /// <summary>A caged bay's closed end: uprights every 0.3 m under the section and rails every 0.48 m across, a doorway left open in a back with one (phase 2's flag).</summary>
    private static void CageCap(Builder p, Section s, float z, char pat, bool doorway = false)
    {
        var colour = pat == 't' ? Timber : BarSteel;
        var half = s.P[0].X;
        var count = (int)MathF.Ceiling(2 * half / 0.3f);
        for (var i = 1; i < count; i++)
        {
            var x = -half + 2 * half * i / count;
            if (doorway && MathF.Abs(x) < RearJamb + 0.02f)
                continue;
            var top = s.YAt(MathF.Abs(x));
            if (top < 0.1f)
                continue;
            var c = new Vector3(x, (0.03f + top - 0.02f) / 2, z);
            Box(p, c - new Vector3(0.015f, (top - 0.05f) / 2, 0.015f), c + new Vector3(0.015f, (top - 0.05f) / 2, 0.015f), colour);
        }
        for (var y = 0.48f; y < s.Top - 0.15f; y += 0.48f)
        {
            var x = s.XAt(y);
            if (x < 0.1f)
                continue;
            if (doorway && y < DoorTop)
            {
                foreach (var sx in (float[])[-1, 1])
                    if (x - 0.03f > RearJamb + 0.05f)
                    {
                        var (x0, x1) = (sx * (RearJamb + 0.02f), sx * (x - 0.03f));
                        Box(p, new Vector3(MathF.Min(x0, x1), y - 0.0125f, z - 0.0125f), new Vector3(MathF.Max(x0, x1), y + 0.0125f, z + 0.0125f), colour);
                    }
                continue;
            }
            Box(p, new Vector3(-x + 0.03f, y - 0.0125f, z - 0.0125f), new Vector3(x - 0.03f, y + 0.0125f, z + 0.0125f), colour);
        }
        if (doorway)
            foreach (var sx in (float[])[-1, 1]) // (the jambs and the lintel)
                Box(p, new Vector3(sx * RearJamb - 0.02f, 0.003f, z - 0.02f), new Vector3(sx * RearJamb + 0.02f, DoorTop, z + 0.02f), Rust);
    }

    /// <summary>A plain drawn box, solid (a bar of a cage's end, a stair's tread).</summary>
    private static void Box(Builder p, Vector3 mn, Vector3 mx, string colour, bool paint = true)
    {
        var b = new JsonObject { ["min"] = V(mn.X, mn.Y, mn.Z), ["max"] = V(mx.X, mx.Y, mx.Z), ["color"] = colour };
        if (!paint)
            b["paintable"] = false;
        p.Boxes.Add(b);
    }

    // ---- Upper floors -------------------------------------------------------------------------------------------

    /// <summary>
    /// A level roof that is an upper floor (phase 5): the panel in pieces round the hatch (none when its sizes are 0), each with a hidden slab whose top is the panel's,
    /// so a player stands on what is drawn. The outer corners keep the walls' mitres; the hatch's edges are cut straight.
    /// </summary>
    private static void FloorRoof(Builder p, Vector2 a0, Vector2 b0, float z0, float z1, Vector3 oAm, Vector3 oAp, Vector3 oBp, Vector3 oBm, (float X0, float X1, float Z0, float Z1) h)
    {
        var y = a0.Y;
        var up = Vector3.UnitY;
        void Piece(Vector3[] q, Vector3[] off, float xlo, float xhi, float zlo, float zhi)
        {
            if (xhi - xlo < 0.02f || zhi - zlo < 0.02f)
                return;
            p.Panel(q, Vector3.UnitY, Roof, false, off);
            p.Hide(new Vector3(xlo, y - 0.05f, zlo), new Vector3(xhi, y + FloorRise, zhi)); // (its top the sockets' plane, a centimetre over the panel: nothing stood on it shares the panel's plane)
        }
        var w = MathF.Max(a0.X, b0.X);
        p.Centre = MathF.Min(p.Centre, y - 0.05f);
        if (h.X1 - h.X0 < 0.01f || h.Z1 - h.Z0 < 0.01f)
        {
            Piece([new(-a0.X, y, z0), new(a0.X, y, z0), new(b0.X, y, z1), new(-b0.X, y, z1)], [oAm, oAp, oBp, oBm], -w, w, z0, z1);
            return;
        }
        var (hx0, hx1, hz0, hz1) = (MathF.Max(h.X0, -MathF.Min(a0.X, b0.X)), MathF.Min(h.X1, MathF.Min(a0.X, b0.X)), MathF.Max(z0, h.Z0), MathF.Min(z1, h.Z1)); // (a hatch to the wall's top where the roof is narrower)
        Piece([new(-a0.X, y, z0), new(hx0, y, z0), new(hx0, y, z1), new(-b0.X, y, z1)], [oAm, up, up, oBm], -w, hx0, z0, z1);
        Piece([new(hx1, y, z0), new(a0.X, y, z0), new(b0.X, y, z1), new(hx1, y, z1)], [up, oAp, oBp, up], hx1, w, z0, z1);
        Piece([new(hx0, y, z0), new(hx1, y, z0), new(hx1, y, hz0), new(hx0, y, hz0)], [up, up, up, up], hx0, hx1, z0, hz0);
        Piece([new(hx0, y, hz1), new(hx1, y, hz1), new(hx1, y, z1), new(hx0, y, z1)], [up, up, up, up], hx0, hx1, hz1, z1);
    }

    /// <summary>How far above an upper floor's roof line its sockets stand: the panel's top and a centimetre (an upper bay's foot row stays off the panel's plane).</summary>
    public const float FloorRise = T / 2 + 0.01f;

    /// <summary>An upper floor's sockets: on the quarter grid from the feet in, 12 cm in from the ends and every quarter on (as a deck's), none in or by the hatch.</summary>
    private static void FloorSockets(Builder p, Shape sh)
    {
        var y = sh.A.Top + FloorRise;
        var foot = FootX(sh);
        var h = sh.Hatch;
        var i = 0;
        var zs = new SortedSet<float>();
        for (var z = 0.12f; z <= sh.Length - 0.12f + 1e-3f; z += 0.25f)
        {
            zs.Add(MathF.Round(z * 100) / 100);
            zs.Add(MathF.Round((sh.Length - z) * 100) / 100); // (and from the back: a bay stood turned round, a left door's, takes its storey turned with it, by its back feet)
        }
        for (var x = -foot; x <= foot + 1e-3f; x += 0.25f)
            foreach (var z in zs)
            {
                if (h is { } hh && x > hh.X0 - 0.15f && x < hh.X1 + 0.15f && z > hh.Z0 - 0.15f && z < hh.Z1 + 0.15f)
                    continue;
                p.Sock($"up{i++}", new Vector3(MathF.Round(x * 100) / 100, y, MathF.Round(z * 100) / 100), "+y"); // (on the floor slab's top)
            }
    }

    // ---- Stairs -------------------------------------------------------------------------------------------------

    /// <summary>A stair: its rise to the floor above, its steps (the last is the floor above itself) and its width, metres.</summary>
    public sealed record StairShape(float Rise, int Steps, float Width)
    {
        public const float Going = 0.25f; // a tread's depth (a ship ladder's)

        /// <summary>From its first tread's front to its last tread's back.</summary>
        public float Run => (Steps - 1) * Going;

        public string Key() => string.Create(CultureInfo.InvariantCulture, $"{Prefix}{StairWord} {Cm(Rise)} {Steps} {Cm(Width)})");
    }

    public static StairShape? StairOf(string name)
    {
        if (!name.StartsWith(Prefix + StairWord + " ", StringComparison.Ordinal) || !name.EndsWith(')'))
            return null;
        var w = name[(Prefix.Length + StairWord.Length + 1)..^1].Split(' ');
        if (w.Length != 3 || !int.TryParse(w[0], NumberStyles.None, CultureInfo.InvariantCulture, out var rise) || !int.TryParse(w[1], NumberStyles.None, CultureInfo.InvariantCulture, out var steps)
            || !int.TryParse(w[2], NumberStyles.None, CultureInfo.InvariantCulture, out var width))
            return null;
        if (rise is < 100 or > 400 || steps is < 3 or > 16 || width is < 40 or > 120 || rise / (float)steps > 40f)
            return null; // (a step at most 0.4 m: a player steps up 0.55)
        return new StairShape(rise / 100f, steps, width / 100f);
    }

    /// <summary>
    /// A stair's part: treads on two stringers, each tread with a hidden block under it down to the floor (so it is walked up as a ship ladder is, never under), its
    /// feet on the floor's quarter grid at its front and back.
    /// </summary>
    private static string StairJson(string name, StairShape st)
    {
        var p = new Builder();
        var hw = st.Width / 2;
        var r = st.Rise / st.Steps;
        for (var k = 1; k < st.Steps; k++)
        {
            var (z0, z1, top) = ((k - 1) * StairShape.Going, k * StairShape.Going, k * r);
            Box(p, new Vector3(-hw, top - 0.05f, z0), new Vector3(hw, top, z1), Tread);
            p.Hide(new Vector3(-hw + 0.01f, 0.01f, z0 + 0.005f), new Vector3(hw - 0.01f, top - 0.05f, z1 - 0.005f));
        }
        // The stringers: plates either side, from the floor at the front to the last tread at the back, 3 mm off the floor.
        foreach (var s in (float[])[-1, 1])
        {
            var x = s * (hw + 0.015f);
            var (yb, yt) = ((st.Steps - 1) * r - 0.28f, (st.Steps - 1) * r + 0.06f);
            p.Facet([new(x, 0.003f, 0), new(x, 0.25f, 0), new(x, yt, st.Run), new(x, MathF.Max(0.003f, yb), st.Run)], new Vector3(s, 0, 0), Rust, 0.02f);
        }
        p.Pad("foot0", new Vector3(0, 0, 0.125f), "-y", 0.02f);
        p.Pad("foot1", new Vector3(0, 0, st.Run - 0.125f), "-y", 0.02f);
        var mass = MathF.Round(Math.Clamp(18 * st.Steps * st.Width, 20, 200), 1);
        return new JsonObject
        {
            ["format"] = "call-of-slop-construct-part/1",
            ["name"] = name,
            ["description"] = $"A stair of {st.Steps - 1} treads up {st.Rise:0.##} m to an upper floor's hatch, made by the vehicle kit for one body.",
            ["group"] = "Vehicle Body",
            ["section"] = "Hull bays",
            ["role"] = "frame",
            ["tool"] = "welder",
            ["mass"] = mass,
            ["health"] = MathF.Round(Math.Clamp(mass * 3, 60, 400)),
            ["armour"] = 10,
            ["material"] = "steel",
            ["color"] = Tread,
            ["attachSeconds"] = 2.0,
            ["walk"] = "ladder",
            ["stage"] = true,
            ["boxes"] = p.Boxes,
            ["sockets"] = p.Sockets,
        }.ToJsonString();
    }

    // ---- Side pods ----------------------------------------------------------------------------------------------

    /// <summary>
    /// A side pod (phase 5): its kind, the bay's length, its bottom and top over the floor, how far out it stands past the skin, and the bay's sections in the pod's own
    /// order (the -x side's is the bay's back first), as a trim's.
    /// </summary>
    public sealed record PodShape(char Kind, float Length, float Low, float High, float Out, Section A, Section B)
    {
        public string Key()
        {
            var sb = new StringBuilder(Prefix).Append(PodWord).Append(Kind).Append(' ');
            Num(sb, Length);
            Num(sb, Low);
            Num(sb, High);
            Num(sb, Out);
            return sb.Append(' ').Append(A.Key()).Append(' ').Append(B.SameAs(A) ? "=" : B.Key()).Append(')').ToString();
        }

        /// <summary>The bay's waist socket the pod stands on, in the bay's frame (+x side), as a trim's.</summary>
        public Vector3 Pad
        {
            get
            {
                var y = (A.WaistY + B.WaistY) / 2;
                return new Vector3((MathF.Max(A.XAt(y), 0) + MathF.Max(B.XAt(y), 0)) / 2, y, Length / 2);
            }
        }
    }

    public static PodShape? PodOf(string name)
    {
        if (!name.StartsWith(Prefix + PodWord, StringComparison.Ordinal) || !name.EndsWith(')'))
            return null;
        var words = name[(Prefix.Length + PodWord.Length)..^1].Split(' ');
        if (words.Length != 4 || words[0].Length != 1 || !PodKinds.Contains(words[0][0]) || Nums(words[1], 4) is not { } n || Section.Parse(words[2]) is not { } a)
            return null;
        var b = words[3] == "=" ? a : Section.Parse(words[3]);
        if (b == null || b.P.Length != a.P.Length || n[0] is < 0.5f or > 2f || n[1] is < 0 or > 1.2f || n[2] < n[1] + 0.3f || n[2] > 1.9f || n[3] is < 0.15f or > 0.6f)
            return null;
        return new PodShape(words[0][0], n[0], n[1], n[2], n[3], a, b);
    }

    /// <summary>The pod's profile out from the skin (x, the distance out; y over the floor), bottom first and round the outside to the top: the faction's shape.</summary>
    private static Vector2[] PodProfile(PodShape s)
    {
        var (lo, hi, d) = (s.Low, s.High, s.Out);
        var h = hi - lo;
        return s.Kind switch
        {
            'w' => [new(0, lo), new(d, lo), new(d, hi - 0.35f * h), new(0.25f * d, hi)], // (a welded box, its lid sloped down to the skin)
            'b' => [new(0, lo + 0.05f), new(0.55f * d, lo), new(0.9f * d, lo + 0.18f * h), new(d, lo + 0.45f * h), new(0.92f * d, lo + 0.75f * h), new(0.6f * d, hi - 0.04f * h), new(0, hi)], // (a smooth blister)
            'k' => [new(0, lo), new(d, lo), new(d, hi - 0.08f), new(d + 0.05f, hi - 0.06f), new(d + 0.05f, hi), new(0, hi)], // (a kiosk: square, a sign band proud along its top)
            'l' => [new(0, lo), new(0.75f * d, lo), new(d, hi - 0.1f * h), new(0.85f * d, hi), new(0, hi)], // (a locker flaring out to a rounded lid)
            'a' => [new(0, lo), new(0.65f * d, lo), new(d, lo + 0.3f * h), new(d, hi - 0.3f * h), new(0.65f * d, hi), new(0, hi)], // (armour: chamfered top and bottom)
            _ => [new(0, lo), new(d, lo), new(d, hi), new(0, hi)], // (a luggage locker: a plain box)
        };
    }

    /// <summary>
    /// A pod's part (phase 5), in its socket's frame (the bay's waist socket, its "+x0", at the origin; x out from the skin): the faction's profile lofted along the
    /// bay from 5 cm in at each end, its back a plate standing <see cref="TrimGap"/> off the skin's widest between its bottom and top at each end, mitred where its
    /// facets meet, capped; a hidden block inside it (it stops a round, a player stands on it) and the trims' small stand boxes at the socket.
    /// </summary>
    private static string? PodJson(string name, PodShape s)
    {
        var p = new Builder();
        var pad = s.Pad;
        var prof = PodProfile(s);
        var (z0, z1) = (0.05f, s.Length - 0.05f);
        float Base(Section sec) => sec.WidestBetween(s.Low, s.High) + T / 2 + TrimGap;
        var (ba, bb) = (Base(s.A), Base(s.B));
        Vector3 W(Vector2 q, float bx, float z) => new(bx + q.X - pad.X, q.Y - pad.Y, z - pad.Z);
        var n = prof.Length;
        var normals = new Vector2[n];
        for (var i = 0; i < n; i++)
        {
            var (q0, q1) = (prof[i], prof[(i + 1) % n]);
            normals[i] = Vector2.Normalize(new Vector2(q1.Y - q0.Y, -(q1.X - q0.X)));
        }
        var mitre = new Vector2[n];
        for (var i = 0; i < n; i++)
            mitre[i] = Mitre(normals[(i + n - 1) % n], normals[i]);
        var colour = s.Kind switch { 'l' => Wood, 'k' => Steel, _ => Steel };
        for (var i = 0; i < n; i++)
        {
            var (q0, q1) = (prof[i], prof[(i + 1) % n]);
            var (m0, m1) = (new Vector3(mitre[i], 0), new Vector3(mitre[(i + 1) % n], 0));
            p.Facet([W(q0, ba, z0), W(q1, ba, z0), W(q1, bb, z1), W(q0, bb, z1)], new Vector3(normals[i], 0), colour, T, true, [m0, m1, m1, m0]);
        }
        foreach (var (bx, z) in new[] { (ba, z0), (bb, z1) })
            p.Facet(prof.Select(q => W(q, bx, z)).ToArray(), Vector3.UnitZ, colour, T);
        // The faction's details on it.
        var (lo, hi, d) = (s.Low, s.High, s.Out);
        var bm = MathF.Max(ba, bb);
        switch (s.Kind)
        {
            case 'k': // (a window in its face, a centimetre proud)
                p.Facet([W(new(d + T / 2 + 0.006f, lo + 0.25f * (hi - lo)), bm, z0 + 0.15f), W(new(d + T / 2 + 0.006f, hi - 0.25f * (hi - lo)), bm, z0 + 0.15f),
                    W(new(d + T / 2 + 0.006f, hi - 0.25f * (hi - lo)), bm, z1 - 0.15f), W(new(d + T / 2 + 0.006f, lo + 0.25f * (hi - lo)), bm, z1 - 0.15f)], Vector3.UnitX, Window, 0.012f, false);
                break;
            case 'w': // (a welded strap round it at its middle)
            case 'a':
            {
                var zm = (z0 + z1) / 2;
                for (var i = 0; i + 1 < n; i++)
                {
                    var (q0, q1) = (prof[i], prof[i + 1]);
                    if (q0.X < 0.01f && q1.X < 0.01f)
                        continue;
                    var nn = new Vector3(normals[i], 0);
                    var bx = (ba + bb) / 2;
                    Bar(p, W(q0, bx, zm) + nn * (T / 2 + 0.008f), W(q1, bx, zm) + nn * (T / 2 + 0.008f), nn, 0.05f, 0.012f, Dark, hide: false, paint: false);
                }
                break;
            }
            case 'l': // (clinker laps: a lap a step out every 12 cm up its face)
                for (var y = lo + 0.1f; y < hi - 0.15f; y += 0.12f)
                {
                    var x = d + T / 2 + 0.006f + 0.004f * (int)((y - lo) / 0.12f);
                    p.Facet([W(new(x, y), bm, z0 + 0.04f), W(new(x, y + 0.09f), bm, z0 + 0.04f), W(new(x, y + 0.09f), bm, z1 - 0.04f), W(new(x, y), bm, z1 - 0.04f)], Vector3.UnitX, Wood, 0.012f, false);
                }
                break;
        }
        // Inside it: a block a round stops on, out past the skin; and the small boxes it stands by at the socket, as a trim's.
        var inner = MathF.Max(ba, bb) - T / 2 + 0.03f;
        var outer = MathF.Max(ba, bb) + 0.5f * d;
        p.Hide(new Vector3(inner - pad.X, lo + 0.06f - pad.Y, z0 + 0.03f - pad.Z), new Vector3(outer - pad.X, hi - 0.06f - pad.Y, z1 - 0.03f - pad.Z));
        if (StandSpot(s.A, s.B, pad) is not { } spot)
            return null;
        p.Boxes.Add(new JsonObject { ["min"] = V(0, spot.Y - 0.01f, -0.01f), ["max"] = V(0.02f, spot.Y + 0.01f, 0.01f), ["hidden"] = true });
        if (spot.Out > 0)
            p.Boxes.Add(new JsonObject { ["min"] = V(spot.Out, -0.01f, -0.01f), ["max"] = V(spot.Out + 0.02f, 0.01f, 0.01f), ["hidden"] = true });
        p.Sock("base", Vector3.Zero, "-x");
        var what = s.Kind switch { 'w' => "a welded box", 'b' => "a smooth blister", 'k' => "a kiosk with a window", 'l' => "a clinker locker", 'a' => "an armoured stowage pod", _ => "a luggage locker" };
        var vol = (outer - inner) * (hi - lo) * (z1 - z0);
        return PartJson(name, $"A side pod, {what}, cut for one bay by the vehicle kit. Hangs on the bay's waist socket.", "Hull trim", MathF.Round(Math.Clamp(30 + vol * 60, 20, 120), 1), p);
    }

    /// <summary>A stair's or a pod's part from its key (null: not one of theirs; Ok false: one no machine would write).</summary>
    private static (bool Ok, ConstructPartDef? Def)? MakeSpace(string name)
    {
        string? json;
        if (name.StartsWith(Prefix + StairWord + " ", StringComparison.Ordinal))
            json = StairOf(name) is { } st ? StairJson(name, st) : null;
        else if (name.StartsWith(Prefix + PodWord, StringComparison.Ordinal))
            json = PodOf(name) is { } pd ? PodJson(name, pd) : null;
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
}
