using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A conveyor's turns (docs/sandbox/constructs/power-and-links.md, "Auto-routing"): a roller bed can't turn in less room than
/// its width, so between two bends a conveyor needs at least its width, and from a port to its first bend half of it. A new
/// conveyor opens to the most both its ends take and its turns leave room for (<see cref="ConveyorMax"/>), and one whose turns
/// leave room for no conveyor at all (under 10 cm) is refused: the server's <see cref="Check"/> and the client's preview run the
/// same function. Saved routes and blueprints are data: nothing here is checked again on them.
/// </summary>
public static partial class ConstructRoutes
{
    public const float ConveyorBendDeg = 10f; // a bend under this is a slope (a pipe's or conveyor's 3.5 degree slack), not a corner

    /// <summary>How wide a new conveyor from this output (to this input, or -1) may open by its ends, in metres.</summary>
    public static float ConveyorWidth(Construct c, int from, byte fromPort, int to = -1, byte toPort = 255)
    {
        if (to >= 0)
        {
            ConstructConveyor.Limit(c, from, fromPort, to, toPort, out var w, out _);
            return w / 100f;
        }
        var o = ConstructConveyor.Opening(c, from, fromPort).X;
        return Math.Max(ConstructConveyor.MinSizeCm, (int)(o * 100 + 0.01f) / ConstructConveyor.StepCm * ConstructConveyor.StepCm) / 100f;
    }

    /// <summary>
    /// The widest bed a path's turns leave room for (metres; <see cref="float.MaxValue"/> without a bend). <paramref name="pts"/>
    /// in their hosts' frames; the first point is a port, the last a port or an open end (whose next bend isn't known yet). From
    /// <paramref name="checkFrom"/>: the segments before were judged as they were laid.
    /// </summary>
    public static float ConveyorRoom(Construct c, ReadOnlySpan<Vector3> pts, ReadOnlySpan<int> hosts, int checkFrom = 0)
    {
        var n = pts.Length;
        var room = float.MaxValue;
        if (n < 3)
            return room;
        var cos = MathF.Cos(ConveyorBendDeg * MathF.PI / 180f);
        var start = Math.Max(0, checkFrom - 2);
        var bentBefore = start > 0 && Bent(c, pts, hosts, start, cos); // (the first point is a port: no corner there)
        for (var i = start; i < n - 1; i++)
        {
            var len = Vector3.Distance(Rest(c, pts, hosts, i + 1), Rest(c, pts, hosts, i));
            var bentAfter = i + 2 < n && Bent(c, pts, hosts, i + 1, cos);
            var share = (bentBefore ? 0.5f : 0f) + (bentAfter ? 0.5f : 0f);
            if (share > 0f)
                room = MathF.Min(room, len / share);
            bentBefore = bentAfter;
        }
        return room;
    }

    private static Vector3 Rest(Construct c, ReadOnlySpan<Vector3> p, ReadOnlySpan<int> h, int i) =>
        c.ToRest(h[i] >= 0 && h[i] < c.PartCount ? c.Parts[h[i]].Seg : 0, p[i]);

    private static bool Bent(Construct c, ReadOnlySpan<Vector3> p, ReadOnlySpan<int> h, int i, float cos)
    {
        var d0 = Rest(c, p, h, i) - Rest(c, p, h, i - 1);
        var d1 = Rest(c, p, h, i + 1) - Rest(c, p, h, i);
        return d0.LengthSquared() > 1e-8f && d1.LengthSquared() > 1e-8f && Vector3.Dot(Vector3.Normalize(d0), Vector3.Normalize(d1)) < cos;
    }

    /// <summary>Why a conveyor's turns are too tight for any conveyor, or null.</summary>
    public static string? ConveyorTurns(Construct c, ReadOnlySpan<Vector3> pts, ReadOnlySpan<int> hosts, int checkFrom = 0)
    {
        var room = ConveyorRoom(c, pts, hosts, checkFrom);
        var least = ConstructConveyor.MinSizeCm / 100f;
        return room + 0.005f >= least ? null : $"too tight a turn for a conveyor: a bed needs as much room to turn as it is wide ({least:0.00} m at least, {room:0.00} m here)";
    }

    /// <summary>A whole link's room for its bed (<see cref="ConveyorRoom(Construct, ReadOnlySpan{Vector3}, ReadOnlySpan{int}, int)"/>), from its points.</summary>
    public static float ConveyorRoom(Construct c, int route)
    {
        var n = c.Routes[route].Count + 2;
        var room = float.MaxValue;
        if (n < 3)
            return room;
        var cos = MathF.Cos(ConveyorBendDeg * MathF.PI / 180f);
        var a = RestPoint(c, route, -1);
        var b = RestPoint(c, route, 0);
        var bentBefore = false;
        for (var i = 0; i < n - 1; i++)
        {
            var next = i + 2 < n ? RestPoint(c, route, i + 1) : b;
            var d0 = b - a;
            var d1 = next - b;
            var bentAfter = i + 2 < n && d0.LengthSquared() > 1e-8f && d1.LengthSquared() > 1e-8f && Vector3.Dot(Vector3.Normalize(d0), Vector3.Normalize(d1)) < cos;
            var share = (bentBefore ? 0.5f : 0f) + (bentAfter ? 0.5f : 0f);
            if (share > 0f)
                room = MathF.Min(room, d0.Length() / share);
            bentBefore = bentAfter;
            a = b;
            b = next;
        }
        return room;
    }

    /// <summary>The largest a conveyor may be set to (cm): what both its ends take, no wider than its turns leave room for.</summary>
    public static void ConveyorMax(Construct c, int route, out ushort w, out ushort h)
    {
        ConstructConveyor.Limit(c, route, out w, out h);
        w = TurnCap(w, ConveyorRoom(c, route));
    }

    /// <summary>A width (cm) no wider than <paramref name="room"/> (m) allows, on the 5 cm steps, at least the smallest conveyor.</summary>
    public static ushort TurnCap(ushort w, float room) =>
        room >= w / 100f ? w : (ushort)Math.Max(ConstructConveyor.MinSizeCm, (int)(room * 100f + 0.01f) / ConstructConveyor.StepCm * ConstructConveyor.StepCm);
}
