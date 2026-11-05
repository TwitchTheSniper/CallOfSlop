using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Ropes and springs on screen (docs/sandbox/CONSTRUCTS.md, "Ropes and springs"), drawn from what the construct list says, never
/// simulated: a rope is a parabola through its sag and one straight piece when taut; a spring a coil of a fixed number of turns
/// that stretches with its ends (a plain damper, stiffness 0, two tubes that telescope); further off they lose pieces. A rope that
/// snaps whips its two halves back to their ends (<see cref="Whip"/>). Also the tool's ghost and the aimed line (<see cref="DrawOverlay"/>).
/// </summary>
public static class ConnectorRender
{
    public const int CurvePieces = 12, CoilTurns = 7, CoilSides = 6;
    public const float CoilNear = 30f, DetailFar = 90f; // metres: a coil's full look; beyond the far one, a single rod
    public const double WhipSeconds = 0.45;

    private static readonly Vector3 RopeColour = new(0.62f, 0.52f, 0.36f), SteelColour = new(0.42f, 0.44f, 0.47f), TubeColour = new(0.2f, 0.21f, 0.23f);

    // The tool's ghost (Program.SandboxConnectors.cs sets it each frame) and the line under the crosshair.
    public static bool PreviewOn, PreviewOk;
    public static Vector3 PreviewA, PreviewB;
    public static ConnectorKind PreviewKind;
    public static byte PreviewSize;
    public static int AimConnector = -1;

    private struct WhipFx
    {
        public Vector3 A, B;
        public ConnectorKind Kind;
        public byte Size;
        public double Born;
    }

    private static readonly WhipFx[] Whips = new WhipFx[16];
    private static int _whipNext;

    /// <summary>The clock whips are drawn by (the game's, set each frame).</summary>
    public static double Now;

    public static void Whip(Vector3 a, Vector3 b, ConnectorKind kind, byte size, double now)
    {
        Now = now;
        Whips[_whipNext] = new WhipFx { A = a, B = b, Kind = kind, Size = size, Born = now };
        _whipNext = (_whipNext + 1) % Whips.Length;
    }

    /// <summary>How far a rope of <paramref name="length"/> sags between ends <paramref name="span"/> apart (a parabola of about that arc length; 0 when taut or length is 0).</summary>
    public static float Sag(float span, float length)
    {
        var slack = length - span;
        return slack <= 0.005f ? 0 : MathF.Sqrt(3f / 8f * span * slack + slack * slack / 4f); // (the second term: nearly on top of each other, it hangs by half its length)
    }

    /// <summary>A point along a connector's line: <paramref name="t"/> 0 to 1; a rope's <paramref name="length"/> gives its sag.</summary>
    public static Vector3 CurvePoint(Vector3 a, Vector3 b, float length, float t)
    {
        var p = Vector3.Lerp(a, b, t);
        p.Y -= 4f * Sag(Vector3.Distance(a, b), length) * t * (1 - t);
        return p;
    }

    private static void Rod(Renderer gfx, Vector3 a, Vector3 b, float radius, Vector3 colour)
    {
        var d = b - a;
        var len = d.Length();
        if (len > 1e-4f)
            gfx.Draw(Matrix4x4.CreateScale(radius * 2f, radius * 2f, len + radius * 0.5f) * RouteRender.Basis(d / len) * Matrix4x4.CreateTranslation((a + b) * 0.5f), colour);
    }

    /// <summary>Every rope and spring, and any whip still in the air. Returns the pieces drawn.</summary>
    public static int Draw(Renderer gfx, ConstructWorld world, float alpha, bool blendTicks)
    {
        var n = 0;
        var cam = gfx.CameraPosition;
        gfx.BeginBatch();
        for (var k = 0; k < world.ConnectorCount; k++)
        {
            ref var cn = ref world.Connectors[k];
            if (!Drawn(world, cn.A, alpha, blendTicks, out var a) || !Drawn(world, cn.B, alpha, blendTicks, out var b))
                continue;
            n += One(gfx, cn.Kind, cn.Size, a, b, cn.Length, cn.Stiffness, MathF.Min(Vector3.DistanceSquared(a, cam), Vector3.DistanceSquared(b, cam)));
        }
        for (var i = 0; i < Whips.Length; i++)
        {
            ref var w = ref Whips[i];
            var age = Now - w.Born;
            if (w.Born <= 0 || age < 0 || age > WhipSeconds)
                continue;
            var f = (float)(age / WhipSeconds);
            var r = ConstructWorld.ConnectorSizes[w.Size].Radius;
            var mid = (w.A + w.B) * 0.5f;
            var kick = Vector3.Normalize(Vector3.Cross(w.B - w.A + new Vector3(1e-3f, 0, 0), Vector3.UnitY) + new Vector3(0, 0.4f, 0)) * (1 - f) * MathF.Sin(f * 18f) * 0.15f * Vector3.Distance(w.A, w.B);
            var colour = w.Kind == ConnectorKind.Rope ? RopeColour : SteelColour;
            for (var s = 0; s < 2; s++)
            {
                var end = s == 0 ? w.A : w.B;
                var tip = Vector3.Lerp(mid, end, f * f) + (s == 0 ? kick : -kick);
                n += Wavy(gfx, end, tip, r, colour);
            }
        }
        gfx.EndBatch();
        return n;
    }

    private static bool Drawn(ConstructWorld world, in ConnectorEnd e, float alpha, bool blendTicks, out Vector3 p)
    {
        if (e.Body == 0)
        {
            p = e.Local;
            return true;
        }
        if (world.Get(e.Body) is not { } c || e.Part >= c.PartCount)
        {
            p = default;
            return false;
        }
        p = ConstructRender.DrawnPoint(c, e.Part, e.Local, alpha, blendTicks);
        return float.IsFinite(p.X + p.Y + p.Z);
    }

    private static int Wavy(Renderer gfx, Vector3 a, Vector3 b, float r, Vector3 colour)
    {
        var mid = (a + b) * 0.5f + new Vector3(0, 0.12f * Vector3.Distance(a, b), 0);
        Rod(gfx, a, mid, r, colour);
        Rod(gfx, mid, b, r, colour);
        return 2;
    }

    private static int One(Renderer gfx, ConnectorKind kind, byte size, Vector3 a, Vector3 b, float length, float stiffness, float dist2)
    {
        var r = ConstructWorld.ConnectorSizes[Math.Min((int)size, 3)].Radius;
        var far = dist2 > DetailFar * DetailFar;
        if (kind == ConnectorKind.Rope)
            return Rope(gfx, a, b, length, r, far);
        if (kind == ConnectorKind.Cylinder)
            return Ram(gfx, a, b, stiffness, r, far);
        if (far)
        {
            Rod(gfx, a, b, r * 2f, SteelColour);
            return 1;
        }
        return stiffness <= 0 ? Damper(gfx, a, b, r) : Coil(gfx, a, b, r, dist2 > CoilNear * CoilNear ? CoilSides / 2 : CoilSides);
    }

    /// <summary>A cylinder: its barrel from the head, as long as the ram is at its shortest, a rod out of it to the far end, a ball on each.</summary>
    private static int Ram(Renderer gfx, Vector3 a, Vector3 b, float shortest, float r, bool far)
    {
        var d = b - a;
        var len = d.Length();
        if (len < 1e-3f)
            return 0;
        var bore = r * 3.2f;
        if (far)
        {
            Rod(gfx, a, b, bore, TubeColour);
            return 1;
        }
        var end = a + d * MathF.Min(1f, MathF.Min(shortest * 0.95f, len) / len);
        Rod(gfx, a, end, bore, TubeColour);
        Rod(gfx, a + d * 0.02f, b, r * 1.5f, SteelColour);
        var ball = Matrix4x4.CreateScale(bore * 2.2f);
        gfx.Draw(ball * Matrix4x4.CreateTranslation(a), SteelColour);
        gfx.Draw(ball * Matrix4x4.CreateTranslation(b), SteelColour);
        return 4;
    }

    private static int Rope(Renderer gfx, Vector3 a, Vector3 b, float length, float r, bool far)
    {
        var knot = Matrix4x4.CreateScale(r * 3f);
        gfx.Draw(knot * Matrix4x4.CreateTranslation(a), RopeColour);
        gfx.Draw(knot * Matrix4x4.CreateTranslation(b), RopeColour);
        var pieces = Sag(Vector3.Distance(a, b), length) <= 0 ? 1 : far ? 3 : CurvePieces;
        var prev = a;
        for (var i = 1; i <= pieces; i++)
        {
            var p = CurvePoint(a, b, length, i / (float)pieces);
            Rod(gfx, prev, p, r, RopeColour);
            prev = p;
        }
        return pieces + 2;
    }

    private static int Coil(Renderer gfx, Vector3 a, Vector3 b, float r, int sides)
    {
        var d = b - a;
        var len = d.Length();
        if (len < 1e-3f)
            return 0;
        var z = d / len;
        var x = MathF.Abs(z.Y) < 0.95f ? Vector3.Normalize(Vector3.Cross(Vector3.UnitY, z)) : Vector3.UnitX;
        var y = Vector3.Cross(z, x);
        var coil = r * 5f;
        var plate = Matrix4x4.CreateScale(coil * 2.4f, coil * 2.4f, r) * RouteRender.Basis(z);
        gfx.Draw(plate * Matrix4x4.CreateTranslation(a), TubeColour);
        gfx.Draw(plate * Matrix4x4.CreateTranslation(b), TubeColour);
        var steps = CoilTurns * sides;
        var prev = a + x * coil;
        for (var i = 1; i <= steps; i++)
        {
            var t = i / (float)steps;
            var ang = t * CoilTurns * MathF.Tau;
            var p = a + z * (len * t) + (x * MathF.Cos(ang) + y * MathF.Sin(ang)) * coil;
            Rod(gfx, prev, p, r * 0.8f, SteelColour);
            prev = p;
        }
        return steps + 2;
    }

    private static int Damper(Renderer gfx, Vector3 a, Vector3 b, float r)
    {
        var mid = (a + b) * 0.5f;
        Rod(gfx, a, mid + (b - a) * 0.08f, r * 2.2f, TubeColour); // (the tube, past the middle)
        Rod(gfx, mid, b, r * 1.1f, SteelColour); // (the rod coming out of it)
        return 2;
    }

    /// <summary>The ghost of what the tool is tying (green or red) and the line under the crosshair, through everything.</summary>
    public static void DrawOverlay(Renderer gfx, ConstructWorld world)
    {
        var aim = AimConnector >= 0 && AimConnector < world.ConnectorCount;
        if (!PreviewOn && !aim)
            return;
        gfx.SetDepthTest(false);
        if (aim && world.EndPoint(world.Connectors[AimConnector].A, out var a) && world.EndPoint(world.Connectors[AimConnector].B, out var b))
        {
            ref var cn = ref world.Connectors[AimConnector];
            var prev = a;
            var r = ConstructWorld.ConnectorSizes[cn.Size].Radius;
            for (var i = 1; i <= CurvePieces; i++)
            {
                var p = CurvePoint(a, b, cn.Kind == ConnectorKind.Rope ? cn.Length : 0, i / (float)CurvePieces);
                RouteRender.Line(gfx, prev, p, new Vector3(1f, 1f, 0.4f), 0.8f, r);
                prev = p;
            }
        }
        if (PreviewOn)
        {
            var colour = PreviewOk ? new Vector3(0.35f, 1f, 0.45f) : new Vector3(1f, 0.3f, 0.25f);
            var r = ConstructWorld.ConnectorSizes[PreviewSize].Radius;
            RouteRender.Line(gfx, PreviewA, PreviewB, colour, 0.75f, r);
            gfx.Draw(Matrix4x4.CreateScale(MathF.Max(r * 4f, 0.04f)) * Matrix4x4.CreateTranslation(PreviewA), colour, 0.85f, mode: 2);
            gfx.Draw(Matrix4x4.CreateScale(MathF.Max(r * 4f, 0.04f)) * Matrix4x4.CreateTranslation(PreviewB), colour, 0.85f, mode: 2);
        }
        gfx.SetDepthTest(true);
    }
}
