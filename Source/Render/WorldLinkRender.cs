using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// World links and hoses on screen (board item wl01; docs/sandbox/CONSTRUCTS.md, "World links", "Hoses"): each world
/// link drawn with the routed links' own segment and elbow pieces (RouteRender), each hose off its reel as a sagging rope
/// (a catenary-like curve of a few thin boxes, drawn, never simulated) from its reel to its receiver (in the holder's
/// hands, or plugged into an inlet), and the linking tool's preview of a world link being routed.
/// </summary>
public static class WorldLinkRender
{
    public const int RopePieces = 12;

    // The linking tool's preview of a world link (Program.SandboxLinks.cs fills it): world points.
    public static readonly Vector3[] PreviewPts = new Vector3[Construct.MaxNodesPerRoute + 3];
    public static int PreviewCount;
    public static bool PreviewOk;
    public static RouteType PreviewType;
    public static byte PreviewStyle;
    public static int AimLink = -1; // the world link under the crosshair (outlined)

    /// <summary>Where a player's hands are on this PC (the hose to a held receiver ends there); null: not drawn.</summary>
    public static Func<byte, Vector3?>? HandOf;

    public static Vector3 HoseColour(HoseCarry h) => h switch
    {
        HoseCarry.Fuel => new Vector3(0.12f, 0.12f, 0.12f),
        HoseCarry.Water => new Vector3(0.2f, 0.45f, 0.8f),
        HoseCarry.Power => new Vector3(0.85f, 0.45f, 0.1f),
        _ => new Vector3(0.25f, 0.6f, 0.3f),
    };

    /// <summary>Every world link and hose (lit). Returns the pieces drawn.</summary>
    public static int Draw(Renderer gfx, ConstructWorld world)
    {
        if (world.WorldLinkCount == 0 && world.HoseCount == 0)
            return 0;
        var n = 0;
        gfx.BeginBatch();
        for (var k = 0; k < world.WorldLinkCount; k++)
        {
            ref var l = ref world.WorldLinks[k];
            var radius = ConstructRoutes.Radius(l.Type, l.Style);
            if (!world.WorldPoint(k, -1, out var a))
                continue;
            var dirPrev = Vector3.Zero;
            for (var i = 0; i <= l.Count; i++)
            {
                if (!world.WorldPoint(k, i, out var b))
                    break;
                var d = b - a;
                var len = d.Length();
                if (len > 1e-4f)
                {
                    var z = d / len;
                    RouteRender.Piece(gfx, l.Type, l.Style, a, b, z, radius, dim: l.Type is RouteType.Data or RouteType.Power && !ConstructRender.ShowWires);
                    n++;
                    if (dirPrev != Vector3.Zero && Vector3.Dot(dirPrev, z) < 0.9998f)
                        RouteRender.Elbow(gfx, l.Type, l.Style, a, dirPrev, z, radius);
                    dirPrev = z;
                }
                a = b;
            }
        }
        for (var k = 0; k < world.HoseCount; k++)
        {
            ref var h = ref world.Hoses[k];
            if (world.Get(h.BodyOut) is not { } rc || h.PartOut >= rc.PartCount || rc.DefOf(h.PartOut) is not { Role: PartRole.Hose } rd)
                continue;
            var from = ConstructWorld.HosePoint(rc, h.PartOut);
            Vector3 to;
            if (h.Plugged && world.Get(h.BodyIn) is { } ic && h.PartIn < ic.PartCount)
                to = ConstructWorld.HosePoint(ic, h.PartIn);
            else if (h.Holder != 255 && HandOf?.Invoke(h.Holder) is { } hand)
                to = hand;
            else
                continue;
            n += Rope(gfx, from, to, rd.HoseLength, HoseColour(rd.HoseCarry), rd.HoseCarry is HoseCarry.Fuel or HoseCarry.Water ? 0.03f : 0.018f);
            if (!h.Plugged)
            {
                // The receiver in the hands: a nozzle or a plug.
                gfx.Draw(Matrix4x4.CreateScale(0.07f, 0.07f, 0.16f) * Matrix4x4.CreateTranslation(to), new Vector3(0.75f, 0.7f, 0.2f));
                n++;
            }
        }
        gfx.EndBatch();
        return n;
    }

    /// <summary>A hose from <paramref name="a"/> to <paramref name="b"/> sagging by its slack (a parabola close to a catenary; nothing simulated).</summary>
    private static int Rope(Renderer gfx, Vector3 a, Vector3 b, float length, Vector3 colour, float radius)
    {
        var span = Vector3.Distance(a, b);
        var slack = MathF.Max(0f, length - span);
        var sag = MathF.Min(1.2f, MathF.Sqrt(3f / 8f * span * slack)); // (a parabola of about that arc length)
        var prev = a;
        var n = 0;
        for (var i = 1; i <= RopePieces; i++)
        {
            var t = i / (float)RopePieces;
            var p = Vector3.Lerp(a, b, t) - new Vector3(0, 4f * sag * t * (1f - t), 0);
            var d = p - prev;
            var len = d.Length();
            if (len > 1e-4f)
                gfx.Draw(Matrix4x4.CreateScale(radius * 2f, radius * 2f, len + radius) * RouteRender.Basis(d / len) * Matrix4x4.CreateTranslation((prev + p) * 0.5f), colour);
            prev = p;
            n++;
        }
        return n;
    }

    /// <summary>The preview of a world link being routed, and the aimed one: unlit, through everything (only with the linking tool).</summary>
    public static void DrawOverlay(Renderer gfx, ConstructWorld world)
    {
        if (!ConstructRender.ShowWires || PreviewCount < 2 && (AimLink < 0 || AimLink >= world.WorldLinkCount))
            return;
        gfx.SetDepthTest(false);
        if (AimLink >= 0 && AimLink < world.WorldLinkCount && world.WorldPoint(AimLink, -1, out var a))
        {
            ref var l = ref world.WorldLinks[AimLink];
            for (var i = 0; i <= l.Count && world.WorldPoint(AimLink, i, out var b); i++)
            {
                RouteRender.Line(gfx, a, b, new Vector3(1f, 1f, 0.4f), 0.8f, ConstructRoutes.Radius(l.Type, l.Style) * 0.8f);
                a = b;
            }
        }
        if (PreviewCount >= 2)
        {
            var colour = PreviewOk ? new Vector3(0.35f, 1f, 0.45f) : new Vector3(1f, 0.3f, 0.25f);
            var radius = ConstructRoutes.Radius(PreviewType, PreviewStyle);
            for (var i = 1; i < PreviewCount; i++)
            {
                RouteRender.Line(gfx, PreviewPts[i - 1], PreviewPts[i], i == PreviewCount - 1 ? colour : new Vector3(0.9f, 0.9f, 0.4f), 0.75f, radius);
                gfx.Draw(Matrix4x4.CreateScale(radius * 2.4f) * Matrix4x4.CreateTranslation(PreviewPts[i]), i == PreviewCount - 1 ? colour : new Vector3(1f, 0.85f, 0.3f), 0.85f, mode: 2);
            }
        }
        gfx.SetDepthTest(true);
    }
}
