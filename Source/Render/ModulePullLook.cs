using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The physics gun pulling a fitted module off its socket, as everyone sees it (Sean, 2026-11-21): the module rocks and shudders
/// on its seat while the beam strains on it, and goes on shaking as it slides out, so the player can tell it is coming off.
/// Drawn only: the server's pull (<see cref="ModuleGuide"/>) is untouched. Filled each frame from the hands
/// (<see cref="SandboxHand.PullShip"/>..., sent in the mode-state tail), so a joined player sees anyone's pull.
/// <list type="bullet">
/// <item>Straining (the module still fitted, <see cref="SandboxHand.PullBody"/> 0): the module's own parts on the ship are walked,
/// not merged (<see cref="Walks"/>, <see cref="DynOf"/>), and each is drawn through <see cref="OfPart"/>, rocking about the plate's face.</item>
/// <item>Out of its seat (its own body): the whole body rocks about its middle (<see cref="OfBody"/>).</item>
/// </list>
/// Nothing allocates: a fixed list of at most <see cref="Max"/> pulls a frame.
/// </summary>
public static class ModulePullLook
{
    public const int Max = 8;
    public const float Degrees = 2.6f; // the rock's amplitude at full shake
    public const float Lift = 0.006f; // m: the shudder out along the socket's normal at full shake

    private struct Entry
    {
        public ushort Ship, Body;
        public int Module; // straining: the module's index on the ship
        public Matrix4x4 W; // in the frame of the body that is drawn (the ship's, or the module's own)
    }

    private static readonly Entry[] _e = new Entry[Max];
    public static int Count;

    public static void Begin() => Count = 0;

    /// <summary>
    /// A pull to draw this frame: the socket on plate part <paramref name="plate"/> of <paramref name="ship"/>, the module still on it
    /// (<paramref name="body"/> null) or out on its own body; <paramref name="shake"/> 0-255 as the server sent it.
    /// </summary>
    public static void Add(Construct ship, int plate, Construct? body, byte shake, double now)
    {
        if (Count >= Max || (uint)plate >= (uint)ship.PartCount || ship.DefOf(plate).ModSocket < 0)
            return;
        if (!ModuleSockets.PlateRest(ship, plate, out var rot, out var face, out _))
            return;
        var n = Vector3.Normalize(Rot24.Apply(rot, Vector3.UnitY)); // (the socket's normal, the ship's frame)
        var s = shake / 255f;
        var level = body == null ? 0.15f + 0.85f * s : s; // (held but not yet pulled on: a faint tremble)
        ref var e = ref _e[Count];
        e.Ship = ship.Id;
        e.Body = body?.Id ?? 0;
        e.Module = -1;
        Vector3 pivot;
        if (body == null)
        {
            var si = ModuleSockets.SocketOfPlate(ship, plate);
            if (si < 0 || ship.ModSockets[si].Module < 0)
                return;
            e.Module = ship.ModSockets[si].Module;
            pivot = face;
        }
        else
        {
            n = Vector3.Normalize(Vector3.Transform(Vector3.Transform(n, ship.Rot), Quaternion.Conjugate(body.Rot))); // (into the module's frame)
            pivot = body.Com;
        }
        e.W = Wiggle(n, pivot, level, (float)(now % 1000.0) + Count * 1.7f);
        Count++;
    }

    /// <summary>A small rock about two axes across <paramref name="n"/> through <paramref name="pivot"/>, and a shudder along it.</summary>
    private static Matrix4x4 Wiggle(Vector3 n, Vector3 pivot, float level, float t)
    {
        var a = MathF.Abs(n.Y) < 0.9f ? Vector3.Normalize(Vector3.Cross(n, Vector3.UnitY)) : Vector3.Normalize(Vector3.Cross(n, Vector3.UnitX));
        var b = Vector3.Cross(n, a);
        var amp = Degrees * MathF.PI / 180f * level;
        var t1 = amp * MathF.Sin(t * MathF.Tau * 7.3f);
        var t2 = amp * 0.8f * MathF.Sin(t * MathF.Tau * 9.1f + 1.3f);
        var lift = Lift * level * (0.5f + 0.5f * MathF.Sin(t * MathF.Tau * 11.7f));
        var r = Quaternion.CreateFromAxisAngle(a, t1) * Quaternion.CreateFromAxisAngle(b, t2);
        return Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateTranslation(pivot + n * lift);
    }

    /// <summary>The wiggle of a module's own body, drawn out of its seat (premultiply the body's matrix with it).</summary>
    public static bool OfBody(Construct c, out Matrix4x4 w)
    {
        for (var k = 0; k < Count; k++)
            if (_e[k].Body == c.Id && c.Id != 0)
            {
                w = _e[k].W;
                return true;
            }
        w = Matrix4x4.Identity;
        return false;
    }

    /// <summary>The wiggle of part <paramref name="i"/> of a ship whose fitted module is strained on (premultiply the body's matrix with it).</summary>
    public static bool OfPart(Construct c, int i, out Matrix4x4 w)
    {
        for (var k = 0; k < Count; k++)
            if (_e[k].Body == 0 && _e[k].Ship == c.Id && ModuleSockets.ModuleOfPart(c, i) == _e[k].Module)
            {
                w = _e[k].W;
                return true;
            }
        w = Matrix4x4.Identity;
        return false;
    }

    /// <summary>Is part <paramref name="i"/> of <paramref name="c"/> on a module being strained on (walked, not merged)?</summary>
    public static bool Walks(Construct c, int i) => Count > 0 && OfPart(c, i, out _);

    /// <summary>A body's share of the merged plan's key: which of its modules is strained on now (0: none).</summary>
    public static long DynOf(Construct c)
    {
        for (var k = 0; k < Count; k++)
            if (_e[k].Body == 0 && _e[k].Ship == c.Id)
                return (_e[k].Module + 1L) * 0x3779B97F4A7C15L;
        return 0;
    }

    /// <summary>A point in <paramref name="c"/>'s frame (the beam's grip) where it is drawn, wiggled with what it is on.</summary>
    public static Vector3 Point(Construct c, Vector3 local, bool ofModule)
    {
        if (Count == 0)
            return local;
        if (ofModule ? OfBody(c, out var w) : PointOnShip(c, local, out w))
            return Vector3.Transform(local, w);
        return local;
    }

    private static bool PointOnShip(Construct c, Vector3 local, out Matrix4x4 w)
    {
        for (var k = 0; k < Count; k++)
            if (_e[k].Body == 0 && _e[k].Ship == c.Id)
            {
                w = _e[k].W;
                return true;
            }
        w = Matrix4x4.Identity;
        return false;
    }
}
