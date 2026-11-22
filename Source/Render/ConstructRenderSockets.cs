using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Module sockets seen (docs/sandbox/design/module-sockets.md 2.4, Sean 2026-11-21): what a socket's well and room are, drawn on the part.
/// <list type="bullet">
/// <item>The lock slider's knob on the frame's side, at its end by its state (red locked, green free).</item>
/// <item>Depth marks round a dug socket's liner every <see cref="MarkStep"/> m, heavier each metre (not with Hide connector on).</item>
/// <item>The floor of an empty Flush or Well socket, where its module will seat.</item>
/// <item>With the socket's settings panel open (or a module held near it): the ghost well, seen through the hull it will cut, and the
/// room's wire box; with a module held near, the box green when it fits, red when not, and each piece of the module that would stick out
/// of the room drawn red where it would be.</item>
/// </list>
/// Blended pass, after the wires. Bodies within <see cref="Range"/> only; allocates nothing.
/// </summary>
public static partial class ConstructRender
{
    public const float SocketRange = 18f; // m: sockets drawn this near the eye
    public const float MarkStep = 0.25f; // m between depth marks
    public static Construct? SocketHeld; // the local player's held body (Program.UpdateModuleCutFx sets it each frame)
    public static ConstructWorld? SocketWorld;

    private static readonly Vector3 MarkC = new(0.95f, 0.8f, 0.3f), FloorC = new(0.16f, 0.17f, 0.19f), GhostC = new(0.35f, 0.8f, 1f),
        RoomOk = new(0.35f, 1f, 0.45f), RoomBad = new(1f, 0.3f, 0.25f), LockOn = new(0.85f, 0.18f, 0.15f), LockOff = new(0.25f, 0.75f, 0.3f);

    /// <summary>Once a frame in the blended pass.</summary>
    public static void DrawSockets(Renderer gfx, ConstructWorld world)
    {
        var eye = gfx.CameraPosition;
        var held = SocketHeld is { } sh && world.Get(sh.Id) == sh && sh.Modules.Length > 0 ? sh : null;
        foreach (var c in world.Bodies)
        {
            if (c.ModSockets.Length == 0 || Vector3.Distance(c.WorldCom, eye) - c.Radius > SocketRange)
                continue;
            for (var si = 0; si < c.ModSockets.Length; si++)
            {
                var s = c.ModSockets[si];
                if (s.Stand || s.Plate >= c.PartCount || c.Parts[s.Plate].Broken)
                    continue;
                DrawSocket(gfx, c, si, s, eye, held);
            }
        }
    }

    private static void DrawSocket(Renderer gfx, Construct c, int si, in CModSocket s, Vector3 eye, Construct? held)
    {
        var d = c.DefOf(s.Plate);
        if (!ModuleSockets.PlateRest(c, s.Plate, out var pr, out var at, out _))
            return;
        var n = Vector3.Normalize(Vector3.Transform(Rot24.Apply(pr, Vector3.UnitY), c.Rot));
        var across = Vector3.Normalize(Vector3.Transform(Rot24.Apply(pr, Vector3.UnitX), c.Rot));
        var up = Vector3.Normalize(Vector3.Transform(Rot24.Apply(pr, Vector3.UnitZ), c.Rot));
        var face = c.ToWorld(at);
        if (Vector3.Distance(face, eye) > SocketRange)
            return;
        var hide = ModuleSockets.HideSet(c, s.Plate);
        // The lock's knob (not on a Wall Bay Socket, which has no side controls); with the plate not drawn (a dug socket's module
        // fills its frame), its eject button too, so it is still there to press.
        if (!hide && !d.ModNoHide && !d.ModCells) // (a console bay has no side controls)
        {
            var locked = ModuleSockets.Locked(c, s.Plate);
            Freeform.PartWorld(c, s.Plate, out var pp, out var pq);
            if (s.Seat > 0 && s.Module >= 0)
            {
                var bt = ModuleSockets.ControlAt(d, ModuleSockets.EjectZ);
                gfx.Draw(Matrix4x4.CreateScale(0.014f, 0.068f, 0.068f) * Matrix4x4.CreateFromQuaternion(pq) * Matrix4x4.CreateTranslation(pp + Vector3.Transform(bt + new Vector3(0.003f, 0, 0), pq)), new Vector3(0.17f, 0.18f, 0.2f));
                gfx.Draw(Matrix4x4.CreateScale(0.012f, 0.044f, 0.044f) * Matrix4x4.CreateFromQuaternion(pq) * Matrix4x4.CreateTranslation(pp + Vector3.Transform(bt + new Vector3(0.014f, 0, 0), pq)), new Vector3(0.78f, 0.22f, 0.18f));
            }
            var k = ModuleSockets.ControlAt(d, locked ? ModuleSockets.LockZ1 - 0.035f : ModuleSockets.LockZ0 + 0.035f) + new Vector3(0.016f, 0, 0);
            var m = Matrix4x4.CreateScale(0.022f, 0.026f, 0.05f) * Matrix4x4.CreateFromQuaternion(pq) * Matrix4x4.CreateTranslation(pp + Vector3.Transform(k, pq));
            gfx.Draw(m, locked ? LockOn : LockOff);
        }
        float hw = s.Wm * 0.5f, hl = s.Lm * 0.5f;
        // The depth marks round the liner, and an empty well's floor.
        if (s.Style != ModStyle.Proud && !hide && s.InTheWay < 0)
        {
            float mw = hw + ModuleCut.Margin * 0.5f - 0.006f, ml = hl + ModuleCut.Margin * 0.5f - 0.006f;
            var thick = ModuleSockets.Thickness(d);
            for (var k = 1; k * MarkStep < s.Behind - 0.01f; k++)
            {
                var depth = k * MarkStep;
                if (depth <= thick + 0.01f)
                    continue; // (inside the frame)
                var metre = k % 4 == 0;
                Ring(gfx, face - n * depth, across, up, mw, ml, metre ? 0.012f : 0.006f, MarkC, metre ? 0.95f : 0.6f);
            }
            if (s.Module < 0 && s.Seat > 0)
                gfx.Draw(Box(face - n * (s.Seat + 0.006f), across, up, n, hw + ModuleCut.Margin * 0.5f - 0.004f, hl + ModuleCut.Margin * 0.5f - 0.004f, 0.005f), FloorC);
        }
        // The ghost well and the room: the panel open on this plate, or a module held near it.
        var panel = PreviewBody == c && PreviewPart == s.Plate;
        var near = s.Module < 0 && held != null && held != c && HeldNear(c, s, held, face);
        if (!panel && !near)
            return;
        var roomC = GhostC;
        if (near)
            roomC = HeldFits(c, si, s, held!) ? RoomOk : RoomBad;
        gfx.SetDepthTest(false); // (seen through the hull it cuts)
        if (s.Behind > 0)
        {
            var mid = face - n * (s.Behind * 0.5f);
            gfx.Draw(Box(mid, across, up, n, hw + ModuleCut.Margin * 0.5f, hl + ModuleCut.Margin * 0.5f, s.Behind * 0.5f), GhostC, 0.16f, mode: 2);
            for (var k = 1; k * MarkStep < s.Behind - 0.01f; k++)
                Ring(gfx, face - n * (k * MarkStep), across, up, hw + 0.02f, hl + 0.02f, k % 4 == 0 ? 0.012f : 0.005f, GhostC, k % 4 == 0 ? 0.9f : 0.5f);
            Ring(gfx, face - n * s.Behind, across, up, hw + 0.02f, hl + 0.02f, 0.014f, GhostC, 1f); // (its floor)
        }
        // The room's wire box: from the floor (or the face) up its height.
        var lo = s.Behind > 0 ? -s.Behind : 0f;
        var hi = s.Height;
        float rw = hw + (s.Style == ModStyle.Proud ? ModuleSocketTable.Current.Side : ModuleCut.WellSide), rl = hl + (s.Style == ModStyle.Proud ? ModuleSocketTable.Current.Side : ModuleCut.WellSide);
        WireBox(gfx, face, across, up, n, rw, rl, lo, hi, roomC);
        if (near)
            Overhang(gfx, c, si, s, held!);
        gfx.SetDepthTest(true);
    }

    /// <summary>Is held body <paramref name="held"/>'s free module of socket <paramref name="s"/>'s size within the gun's catch of its face?</summary>
    private static bool HeldNear(Construct c, in CModSocket s, Construct held, Vector3 face)
    {
        var plug = held.Modules[0].Plug;
        if ((uint)plug >= (uint)held.PartCount || !ModuleSockets.Matches(s, held.DefOf(plug)))
            return false;
        held.SocketAt(plug, held.DefOf(plug).ModSocket, out var pat, out _);
        return Vector3.Distance(held.ToWorld(pat), face) < ModuleGuide.CatchRange + 1f;
    }

    private static int _fitStamp = -1, _fitHeld, _fitShip, _fitPlate;
    private static bool _fitOk;

    /// <summary>Would the held module fit the socket at its seat (the server's rules, worked out again only when either body changes)?</summary>
    private static bool HeldFits(Construct c, int si, in CModSocket s, Construct held)
    {
        var stamp = c.RebuildStamp * 31 + held.RebuildStamp;
        if (stamp == _fitStamp && _fitHeld == held.Id && _fitShip == c.Id && _fitPlate == s.Plate)
            return _fitOk;
        (_fitStamp, _fitHeld, _fitShip, _fitPlate) = (stamp, held.Id, c.Id, s.Plate);
        var plug = held.Modules[0].Plug;
        _fitOk = ConstructEdits.SeatPose(c, s, held, plug, out _, out _, out var rot, out var pos) == null
                 && ModuleSockets.FitRefusal(c, si, held, 0, rot, pos, byHand: false, creative: true, moduleOnly: true) == null;
        return _fitOk;
    }

    /// <summary>The held module's pieces that would stick out of the socket's room at its seat, drawn red where they would be.</summary>
    private static void Overhang(Renderer gfx, Construct c, int si, in CModSocket s, Construct held)
    {
        var plug = held.Modules[0].Plug;
        if (ConstructEdits.SeatPose(c, s, held, plug, out var turn, out var rel, out _, out _) != null)
            return;
        Vector3 lo = s.RoomMin - new Vector3(0.006f), hi = s.RoomMax + new Vector3(0.006f);
        var shown = 0;
        for (var j = 0; j < held.PartCount && shown < 24; j++)
        {
            ModuleSockets.RestBounds(held, j, out var mn, out var mx);
            var c0 = rel + Rot24.Apply(turn, (mn + mx) * 0.5f);
            var h0 = Rot24.ApplyHalf(turn, (mx - mn) * 0.5f);
            Vector3 a = c0 - h0, b = c0 + h0;
            if (a.X >= lo.X && a.Y >= lo.Y && a.Z >= lo.Z && b.X <= hi.X && b.Y <= hi.Y && b.Z <= hi.Z)
                continue;
            // Only the part outside the room, as a box in the ship's frame.
            var oa = a;
            var ob = b;
            for (var k = 0; k < 3; k++)
                if (a[k] >= lo[k] && b[k] <= hi[k])
                    continue;
                else if (b[k] > hi[k])
                    (oa[k], ob[k]) = (MathF.Max(a[k], hi[k]), b[k]);
                else
                    (oa[k], ob[k]) = (a[k], MathF.Min(b[k], lo[k]));
            var m = Matrix4x4.CreateScale(Vector3.Max(ob - oa, new Vector3(0.01f))) * Matrix4x4.CreateFromQuaternion(c.Rot) * Matrix4x4.CreateTranslation(c.ToWorld((oa + ob) * 0.5f));
            gfx.Draw(m, RoomBad, 0.45f, mode: 2);
            shown++;
        }
    }

    /// <summary>A unit box's model: middle <paramref name="mid"/>, half sizes along across, up and the normal.</summary>
    private static Matrix4x4 Box(Vector3 mid, Vector3 across, Vector3 up, Vector3 n, float hx, float hz, float hy)
    {
        var x = across * (2 * hx);
        var y = n * (2 * hy);
        var z = up * (2 * hz);
        return new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, mid.X, mid.Y, mid.Z, 1);
    }

    /// <summary>A thin rectangle round a well at <paramref name="at"/> (half sizes hw by hl), lines <paramref name="t"/> thick.</summary>
    private static void Ring(Renderer gfx, Vector3 at, Vector3 across, Vector3 up, float hw, float hl, float t, Vector3 colour, float opacity)
    {
        var n = Vector3.Cross(across, up);
        gfx.Draw(Box(at + up * hl, across, up, n, hw, t * 0.5f, t * 0.5f), colour, opacity, mode: 2);
        gfx.Draw(Box(at - up * hl, across, up, n, hw, t * 0.5f, t * 0.5f), colour, opacity, mode: 2);
        gfx.Draw(Box(at + across * hw, across, up, n, t * 0.5f, hl, t * 0.5f), colour, opacity, mode: 2);
        gfx.Draw(Box(at - across * hw, across, up, n, t * 0.5f, hl, t * 0.5f), colour, opacity, mode: 2);
    }

    /// <summary>A box's twelve edges: across +-hw, up +-hl, along the normal from <paramref name="lo"/> to <paramref name="hi"/> over <paramref name="face"/>.</summary>
    private static void WireBox(Renderer gfx, Vector3 face, Vector3 across, Vector3 up, Vector3 n, float hw, float hl, float lo, float hi, Vector3 colour)
    {
        const float t = 0.01f;
        Ring(gfx, face + n * lo, across, up, hw, hl, t, colour, 0.9f);
        Ring(gfx, face + n * hi, across, up, hw, hl, t, colour, 0.9f);
        var mid = (lo + hi) * 0.5f;
        var h = MathF.Max(0.005f, (hi - lo) * 0.5f);
        for (var sx = -1; sx <= 1; sx += 2)
            for (var sz = -1; sz <= 1; sz += 2)
                gfx.Draw(Box(face + n * mid + across * (sx * hw) + up * (sz * hl), across, up, n, t * 0.5f, t * 0.5f, h), colour, 0.9f, mode: 2);
    }
}
