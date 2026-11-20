using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A tool's screen, built into the tool's body (tool space): the card's plane (<see cref="At"/>, facing <see cref="Normal"/>, its top
/// <see cref="Up"/>), its size and text rows, and the housing it sits in: a block <see cref="Depth"/> deep behind the card's plane, from
/// <see cref="HousingLo"/> to <see cref="HousingHi"/> across it (right, up from <see cref="At"/>), wider than the screen on the side the
/// tool is, so it runs into the tool's body. In the colour of the box it runs into, with a bezel round the glass in the tool's dark trim.
/// <see cref="Fit"/> finds it once, when the model is made (docs/sandbox/constructs/overview-and-playing.md "A tool's screen"). Drawn by
/// ToolRender.EmitScreen (<see cref="Boxes"/> boxes); the card by Program.DrawToolCard.
/// </summary>
public sealed record ToolDisplay(Vector3 At, Vector3 Normal, Vector3 Up, Vector2 Size, int Rows, float Depth, Vector2 HousingLo, Vector2 HousingHi, Vector3 Housing, Vector3 Bezel)
{
    public static readonly Vector2 DefaultSize = new(0.14f, 0.084f);
    public const int DefaultRows = 6;

    /// <summary>The grip stands here in first person (ToolRender.FirstPerson).</summary>
    public static readonly Vector3 ViewGrip = new(0.21f, -0.22f, -0.44f);

    /// <summary>Where a hand tool's screen is wanted in first person (camera space): the same on every tool, low and left of the grip, under the tool's line to the crosshair, so it is found without looking.</summary>
    public static readonly Vector3 HandTarget = new(0.075f, -0.16f, -0.4f);

    /// <summary>Boxes the screen draws: the housing, the glass and the bezel's four sides.</summary>
    public const int Boxes = 6;

    /// <summary>The bezel's width round the glass, how far it stands proud of the card, and how far back it goes (metres).</summary>
    public const float Rim = 0.007f, Proud = 0.004f, BezelBack = 0.01f;

    /// <summary>The glass's front and back behind the card's plane, and how far past the opening it runs under the bezel.</summary>
    public const float GlassFront = 0.002f, GlassBack = 0.008f, GlassOver = 0.003f;

    /// <summary>The housing's front behind the card's plane, and how far past the bezel's outer edge it shows round it, in the tool's colour (no shared planes: z-fighting.md).</summary>
    public const float HousingFront = 0.006f, Lip = 0.009f;

    private const float Clear = Proud + 0.003f; // nothing of the tool stands nearer the eye than this in front of the bezel
    private const float MaxBridge = 0.16f; // the housing reaches the body within this across the card, or the screen isn't fitted
    private const float Embed = 0.012f; // and runs this far into it
    private const float Band = 0.12f; // body this far behind the card's plane can carry the housing

    /// <summary>
    /// The screen on a model of <paramref name="boxes"/> turned by <paramref name="hold"/>, its grip at <paramref name="viewAt"/> in first
    /// person: within a few centimetres of <paramref name="target"/> (camera space), as near it as its face is clear of the tool, facing
    /// the eye; then its housing widened on the side the body is nearest, until it runs into the body. Null when the body is out of reach
    /// (the file is refused). Runs once a model; allocates then only.
    /// </summary>
    public static ToolDisplay? Fit(ToolBox[] boxes, Quaternion hold, Vector3 viewAt, Vector3 target, Vector2 size, int rows)
    {
        var want = target - viewAt;
        // Points through every box (held space: the hold applied, the grip at the origin), spaced well under the screen's size.
        var pts = new List<(Vector3 P, int Box)>();
        for (var i = 0; i < boxes.Length; i++)
        {
            var b = boxes[i];
            int nx = Steps(b.Size.X), ny = Steps(b.Size.Y), nz = Steps(b.Size.Z);
            for (var x = 0; x < nx; x++)
            for (var y = 0; y < ny; y++)
            for (var z = 0; z < nz; z++)
            {
                var local = b.Center + b.Size * new Vector3(x / (float)(nx - 1) - 0.5f, y / (float)(ny - 1) - 0.5f, z / (float)(nz - 1) - 0.5f);
                var at = Vector3.Transform(local, hold);
                if ((at - want).LengthSquared() < 0.35f * 0.35f) // (far parts of a long arm neither block nor carry it)
                    pts.Add((at, i));
            }
        }
        var eye = -viewAt;
        float halfW = size.X * 0.5f + Rim + Lip, halfH = size.Y * 0.5f + Rim + Lip;
        // The place: the nearest to the target whose face is clear of the tool (pushed toward the eye when the tool is in the way, which costs).
        var best = float.MaxValue;
        Vector3 s = default, n = default, u = default, r = default;
        for (var dy = -3; dy <= 3; dy++)
        for (var dx = -3; dx <= 3; dx++)
        {
            var start = want + new Vector3(dx * 0.01f, dy * 0.01f, 0);
            var at = start;
            Vector3 cn = default, cu = default, cr = default;
            for (var pass = 0; pass < 4; pass++)
            {
                Basis(eye - at, out cn, out cu, out cr);
                var excess = float.MinValue;
                foreach (var (p, _) in pts)
                {
                    var d = p - at;
                    if (MathF.Abs(Vector3.Dot(d, cr)) <= halfW && MathF.Abs(Vector3.Dot(d, cu)) <= halfH)
                        excess = MathF.Max(excess, Vector3.Dot(d, cn) + Clear);
                }
                if (excess <= 0)
                    break;
                at += cn * excess;
            }
            var push = (at - start).Length();
            var score = (at - want).LengthSquared() + 40 * push * push;
            if (score < best)
                (best, s, n, u, r) = (score, at, cn, cu, cr);
        }
        // The housing: widened toward the body's nearest point across the card (right, left, up or down), within the band behind it.
        Span<float> gap = [float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue];
        Span<float> deep = stackalloc float[4];
        Span<int> onto = [-1, -1, -1, -1];
        foreach (var (p, box) in pts)
        {
            if (boxes[box].Glow || boxes[box].Spin)
                continue;
            var d = p - s;
            var back = -Vector3.Dot(d, n);
            if (back < Clear || back > Band)
                continue;
            float a = Vector3.Dot(d, r), b = Vector3.Dot(d, u);
            for (var k = 0; k < 4; k++)
            {
                var (along, across, half, crossHalf) = k switch { 0 => (a, b, halfW, halfH), 1 => (-a, b, halfW, halfH), 2 => (b, a, halfH, halfW), _ => (-b, a, halfH, halfW) };
                if (MathF.Abs(across) > crossHalf - 0.01f)
                    continue;
                var g = MathF.Max(0, along - half);
                if (g < gap[k] - 0.002f || (g < gap[k] + 0.002f && back < deep[k]))
                    (gap[k], deep[k], onto[k]) = (g, back, box);
            }
        }
        var side = 0;
        for (var k = 1; k < 4; k++)
            if (gap[k] < gap[side] - 0.01f) // (the tool's side first: it is on the right of a hand tool's screen)
                side = k;
        if (onto[side] < 0 || gap[side] > MaxBridge)
            return null;
        Vector2 lo = new(-halfW, -halfH), hi = new(halfW, halfH);
        var reach = gap[side] + Embed;
        switch (side)
        {
            case 0: hi.X += reach; break;
            case 1: lo.X -= reach; break;
            case 2: hi.Y += reach; break;
            default: lo.Y -= reach; break;
        }
        var depth = Math.Clamp(deep[side] + 0.03f, BezelBack + 0.01f, Band);
        // In the tool's own paint: its biggest coloured box (a dark grip is not its colour), else the box it runs into.
        var housing = boxes[onto[side]].Color;
        var most = 0f;
        foreach (var b in boxes)
            if (!b.Glow && Luma(b.Color) > 0.16f && b.Size.X * b.Size.Y * b.Size.Z is var v && v > most)
                (most, housing) = (v, b.Color);
        housing *= 0.88f; // (a shade under the body's paint: a moulding of its own, set into it)
        var bezel = housing * 0.42f;
        foreach (var b in boxes)
            if (!b.Glow && Luma(b.Color) < 0.2f && Luma(b.Color) < Luma(bezel) + 0.1f && (b.Color - housing).LengthSquared() > 0.01f)
                bezel = b.Color * 0.85f; // (the tool's own dark trim, where it has one, a shade under it: not the box itself)
        var inv = Quaternion.Conjugate(hold);
        return new ToolDisplay(Vector3.Transform(s, inv), Vector3.Transform(n, inv), Vector3.Transform(u, inv), size, rows, depth, lo, hi, housing, bezel);
    }

    private static readonly Vector3 Turn = new(0.18f, -0.08f, 0f);

    private static int Steps(float size) => Math.Clamp((int)MathF.Ceiling(size / 0.012f) + 1, 2, 16);

    private static float Luma(Vector3 c) => 0.3f * c.X + 0.59f * c.Y + 0.11f * c.Z;

    /// <summary>
    /// The card's frame facing <paramref name="toEye"/> (held space, which is the camera's axes): its normal, its top (as near up as it
    /// can be) and its right. Turned a little right and down of the eye, so the housing's side and top show running into the tool.
    /// </summary>
    private static void Basis(Vector3 toEye, out Vector3 n, out Vector3 u, out Vector3 r)
    {
        n = Vector3.Normalize(Vector3.Normalize(toEye) + Turn);
        u = Vector3.Normalize(Vector3.UnitY - n * n.Y);
        r = Vector3.Cross(u, n);
    }
}
