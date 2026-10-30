using System.Numerics;

namespace CallOfSlop;

/// <summary>The Sandbox ghost's rotate keys (Keybinds: Rotate left / right / up / down, Roll left / right).</summary>
public enum TurnKey : byte
{
    Left,
    Right,
    Up,
    Down,
    RollLeft,
    RollRight,
}

/// <summary>
/// Turning the ghost part with the arrow keys (board item dev-sandbox-arrow-rotate-ar02; docs/sandbox/CONSTRUCTS.md
/// "Turning the ghost"), the same for every ghost: free placement and attaching to a socket. Each key is a quarter turn
/// relative to the camera: Left / Right about the vertical, Up / Down about the view's right axis (Up tips the part's top
/// towards you, its front rising), Shift + Left / Right roll it about the axis pointing away from you. The camera's axes
/// are snapped to the nearest axis of the frame the part's turn lives in (the new construct's frame, or the construct the
/// socket is on), so the result is always one of the 24 Rot24 turns the network and placement already use. Nothing allocates.
/// </summary>
public static class GhostTurn
{
    /// <summary>The nearest signed axis of <paramref name="v"/> as a direction index (+x 0, -x 1, +y 2...), skipping axis <paramref name="notAxis"/> (0..2; -1 none).</summary>
    public static byte Snap(Vector3 v, int notAxis = -1)
    {
        var best = -1f;
        byte dir = 0;
        for (var a = 0; a < 3; a++)
        {
            if (a == notAxis)
                continue;
            var c = v[a];
            if (MathF.Abs(c) > best)
            {
                best = MathF.Abs(c);
                dir = (byte)(a * 2 + (c < 0 ? 1 : 0));
            }
        }
        return dir;
    }

    /// <summary>
    /// The quarter turn <paramref name="key"/> makes, as a Rot24 turn in the frame whose world rotation is
    /// <paramref name="frame"/>, seen by a camera whose right is <paramref name="camRight"/> (world). The part's new turn is
    /// <c>Rot24.Compose(delta, turn)</c>. <paramref name="camUp"/> is the camera's level up (world +Y, or a planet's up).
    /// </summary>
    public static byte Delta(TurnKey key, Quaternion frame, Vector3 camRight, Vector3 camUp)
    {
        var inv = Quaternion.Conjugate(Quaternion.Normalize(frame));
        var up = Snap(Vector3.Transform(camUp, inv));
        var right = Snap(Vector3.Transform(camRight, inv), up >> 1);
        var u = ConstructParts.DirVector(up);
        var r = ConstructParts.DirVector(right);
        var away = Vector3.Cross(u, r); // (up x right: the way the camera looks, levelled)
        const float q = MathF.PI / 2;
        var (axis, angle) = key switch
        {
            TurnKey.Left => (u, q), // the front swings to the left
            TurnKey.Right => (u, -q),
            TurnKey.Up => (r, q), // the top comes towards you, the front rises
            TurnKey.Down => (r, -q),
            TurnKey.RollLeft => (away, -q), // the top goes to the left
            _ => (away, q),
        };
        return Rot24.Nearest(Quaternion.CreateFromAxisAngle(axis, angle), out _);
    }

    /// <summary>The camera's right (world, level) for a look yaw on flat ground (our yaw turns -Z towards +X).</summary>
    public static Vector3 CameraRight(float yaw) => new(MathF.Cos(yaw), 0, MathF.Sin(yaw));

    /// <summary>
    /// Socket attachment: the variant (own socket and spin) of <paramref name="def"/> on socket <paramref name="socket"/> of
    /// part <paramref name="part"/> that turns the part from <paramref name="fromRot"/> by <paramref name="delta"/>. A turn the
    /// sockets can't make (no socket of the part faces that way, or it would be in the way) is skipped: the next quarter turn
    /// the same way is tried, so repeating a key always comes round. Of the variants giving one turn the lowest is taken, so
    /// a key and its opposite undo each other exactly. -1 when no other turn fits.
    /// </summary>
    public static int AttachVariant(Construct c, ConstructPartDef def, int part, int socket, byte fromRot, byte delta)
    {
        var total = def.Sockets.Length * 4;
        var rot = fromRot;
        for (var step = 1; step < 4; step++)
        {
            rot = Rot24.Compose(delta, rot);
            if (rot == fromRot)
                break;
            for (var v = 0; v < total; v++)
                if (ConstructBuild.Place(c, part, socket, def, v / 4, v % 4, out var g) == null && g.Rot == rot)
                    return v;
        }
        return -1;
    }

    /// <summary>The lowest variant on that socket that gives turn <paramref name="rot"/> (the canonical one), or -1.</summary>
    public static int VariantFor(Construct c, ConstructPartDef def, int part, int socket, byte rot)
    {
        for (var v = 0; v < def.Sockets.Length * 4; v++)
            if (ConstructBuild.Place(c, part, socket, def, v / 4, v % 4, out var g) == null && g.Rot == rot)
                return v;
        return -1;
    }
}
