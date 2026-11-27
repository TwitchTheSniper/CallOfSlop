using System.Globalization;
using System.Text;

namespace CallOfSlop.ShipDesigner;

/// <summary>
/// Triangles for the designer's view: positions and a colour a vertex, written as JSON for the page. A frame maps a piece's own axes (X across,
/// Y out of the skin, Z towards the bow) into the ship; every shape is faceted (flat normals, as the game's hull facets).
/// </summary>
public sealed class DesignMesh
{
    public readonly List<double> P = [];
    public readonly List<byte> C = [];
    public int Tris => P.Count / 9;

    /// <summary>
    /// The door leaves drawn in it (walkable designs, step 4: DesignParts.Doors.cs makes each one a door of its own in the game): a run of its
    /// triangles each, From to To, and the leaf's number within its piece. Null: none.
    /// </summary>
    public List<(int From, int To, int Leaf)>? Leaves;

    /// <summary>Draws a door leaf (<paramref name="draw"/>'s triangles), tagged as leaf <paramref name="leaf"/> of what this mesh draws.</summary>
    public void Leaf(int leaf, Action draw)
    {
        var from = Tris;
        draw();
        if (Tris > from)
            (Leaves ??= []).Add((from, Tris, leaf));
    }

    /// <summary>
    /// The glass drawn in it (Karl, 2026-11-27: windows weren't see-through in the game): a run of its triangles each, From to To. The game
    /// draws these blended after everything solid and casts no shadow from them (DesignParts.LookBuilder marks them). Null: none.
    /// </summary>
    public List<(int From, int To)>? Panes;

    /// <summary>Draws glass (<paramref name="draw"/>'s triangles), tagged as a pane.</summary>
    public void Pane(Action draw)
    {
        var from = Tris;
        draw();
        if (Tris > from)
            (Panes ??= []).Add((from, Tris));
    }

    /// <summary>Is triangle <paramref name="t"/> glass (<see cref="Panes"/>)?</summary>
    public bool InPane(int t)
    {
        if (Panes is not { } ps)
            return false;
        foreach (var q in ps)
            if (t >= q.From && t < q.To)
                return true;
        return false;
    }

    // The frame: origin and the three axes.
    private double _ox, _oy, _oz;
    private double[] _x = [1, 0, 0], _y = [0, 1, 0], _z = [0, 0, 1];
    private double _scale = 1;

    public void Frame(double ox, double oy, double oz, double[] xa, double[] ya, double[] za, double scale = 1, double across = 1)
    {
        (_ox, _oy, _oz, _x, _y, _z, _scale, _across) = (ox, oy, oz, xa, ya, za, scale, across);
    }

    private double _across = 1; // an extra scale along x and z (a piece's hole size, Karl 2026-11-25)

    public void World() => Frame(0, 0, 0, [1, 0, 0], [0, 1, 0], [0, 0, 1]);

    private (double, double, double) Map(double x, double y, double z)
    {
        x *= _scale * _across; y *= _scale; z *= _scale * _across;
        return (_ox + x * _x[0] + y * _y[0] + z * _z[0], _oy + x * _x[1] + y * _y[1] + z * _z[1], _oz + x * _x[2] + y * _y[2] + z * _z[2]);
    }

    public static (byte R, byte G, byte B) Hex(string hex)
    {
        if (hex.Length == 7 && hex[0] == '#' && int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            return ((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return (200, 200, 200);
    }

    public static (byte, byte, byte) Tone((byte R, byte G, byte B) c, double f) =>
        ((byte)Math.Clamp(c.R * f, 0, 255), (byte)Math.Clamp(c.G * f, 0, 255), (byte)Math.Clamp(c.B * f, 0, 255));

    /// <summary>A triangle in the frame's space.</summary>
    public void Tri((double, double, double) a, (double, double, double) b, (double, double, double) c, (byte R, byte G, byte B) col, bool mapped = true)
    {
        Vertex(a, col, mapped);
        Vertex(b, col, mapped);
        Vertex(c, col, mapped);
    }

    private void Vertex((double X, double Y, double Z) v, (byte R, byte G, byte B) col, bool mapped)
    {
        var (wx, wy, wz) = mapped ? Map(v.X, v.Y, v.Z) : v;
        P.Add(wx); P.Add(wy); P.Add(wz);
        C.Add(col.R); C.Add(col.G); C.Add(col.B);
    }

    public void Quad((double, double, double) a, (double, double, double) b, (double, double, double) c, (double, double, double) d, (byte, byte, byte) col, bool mapped = true)
    {
        Tri(a, b, c, col, mapped);
        Tri(a, c, d, col, mapped);
    }

    /// <summary>An axis-aligned box in the frame's space.</summary>
    public void Box(double x0, double y0, double z0, double x1, double y1, double z1, string colour)
    {
        var c = Hex(colour);
        var v = new (double, double, double)[8];
        for (var i = 0; i < 8; i++)
            v[i] = ((i & 1) == 0 ? x0 : x1, (i & 2) == 0 ? y0 : y1, (i & 4) == 0 ? z0 : z1);
        Quad(v[0], v[2], v[3], v[1], Tone(c, 0.8), true); // -z
        Quad(v[4], v[5], v[7], v[6], Tone(c, 0.95), true); // +z
        Quad(v[0], v[4], v[6], v[2], Tone(c, 0.85), true); // -x
        Quad(v[1], v[3], v[7], v[5], Tone(c, 0.9), true); // +x
        Quad(v[0], v[1], v[5], v[4], Tone(c, 0.7), true); // -y
        Quad(v[2], v[6], v[7], v[3], c, true); // +y
    }

    /// <summary>A frustum along the frame's Y from y0 (radius r0) to y1 (radius r1), n sides, capped where capped.</summary>
    public void Frustum(double y0, double r0, double y1, double r1, int n, string colour, bool cap0 = true, bool cap1 = true, double cx = 0, double cz = 0)
    {
        var c = Hex(colour);
        for (var i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n, b = 2 * Math.PI * (i + 1) / n;
            var shade = Tone(c, 0.75 + 0.25 * (0.5 + 0.5 * Math.Cos(a - 0.8)));
            var p0 = (cx + r0 * Math.Cos(a), y0, cz + r0 * Math.Sin(a));
            var p1 = (cx + r0 * Math.Cos(b), y0, cz + r0 * Math.Sin(b));
            var q0 = (cx + r1 * Math.Cos(a), y1, cz + r1 * Math.Sin(a));
            var q1 = (cx + r1 * Math.Cos(b), y1, cz + r1 * Math.Sin(b));
            Quad(p0, q0, q1, p1, shade);
            if (cap0 && r0 > 0)
                Tri((cx, y0, cz), p1, p0, Tone(c, 0.7));
            if (cap1 && r1 > 0)
                Tri((cx, y1, cz), q0, q1, c);
        }
    }

    /// <summary>A cylinder along the frame's Z (a barrel) from z0 to z1 at (x, y).</summary>
    public void Barrel(double x, double y, double z0, double z1, double r, int n, string colour)
    {
        var c = Hex(colour);
        for (var i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n, b = 2 * Math.PI * (i + 1) / n;
            var p0 = (x + r * Math.Cos(a), y + r * Math.Sin(a), z0);
            var p1 = (x + r * Math.Cos(b), y + r * Math.Sin(b), z0);
            var q0 = (x + r * Math.Cos(a), y + r * Math.Sin(a), z1);
            var q1 = (x + r * Math.Cos(b), y + r * Math.Sin(b), z1);
            Quad(p0, p1, q1, q0, Tone(c, 0.75 + 0.25 * Math.Sin(a + 1)));
            Tri((x, y, z1), q0, q1, Tone(c, 0.6));
        }
    }

    /// <summary>Copies every triangle mirrored across the plane x = cx (world), winding reversed.</summary>
    public void MirrorFrom(int fromTri, double cx)
    {
        var end = Tris;
        for (var t = fromTri; t < end; t++)
            for (var v = 2; v >= 0; v--)
            {
                var i = (t * 3 + v) * 3;
                P.Add(2 * cx - P[i]); P.Add(P[i + 1]); P.Add(P[i + 2]);
                C.Add(C[i]); C.Add(C[i + 1]); C.Add(C[i + 2]);
            }
    }

    /// <summary>The mesh as JSON; with a key (the page's cache: the same key is the same mesh), that first as "k".</summary>
    public void Write(StringBuilder sb, string? key = null)
    {
        sb.Append(key is null ? "{\"p\":[" : "{\"k\":\"" + key + "\",\"p\":[");
        for (var i = 0; i < P.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(Math.Round(P[i], 3).ToString(CultureInfo.InvariantCulture));
        }
        sb.Append("],\"c\":[");
        for (var i = 0; i < C.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(C[i]);
        }
        sb.Append("]}");
    }
}
