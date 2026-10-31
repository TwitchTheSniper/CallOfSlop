using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Snappy building's markers (board item dev-sandbox-snappy-build-sb01; docs/sandbox/CONSTRUCTS.md, "Snappy building"): with a
/// part held, every free socket of the aimed construct it can use (found once per construct change: <see cref="SnapCache"/>),
/// coloured and shaped by connection type and fading with distance; and the held part's own connection points on its ghost,
/// the one that mates bigger and the functional ones that line up with a socket (the connections placing it will make)
/// ringed in green. A snap flashes. The input (Program.SandboxSnap.cs) sets the state each frame; nothing allocates here.
/// </summary>
public static class SnapRender
{
    public static readonly SnapCache Cache = new();
    public static bool Active;
    public static Construct? Body;
    public static ConstructPartDef? Held;
    public static double FlashAt = -10;
    public static readonly Lineup[] Lines = new Lineup[8];
    public static int LineCount;
    public static int MarkersDrawn; // (the last frame; the self-test reads it)

    private const int MaxMarkers = 160;
    private static readonly OwnPoint[] Own = new OwnPoint[32];

    public static void Clear()
    {
        Active = false;
        Body = null;
        Held = null;
        LineCount = 0;
    }

    /// <summary>The marker alpha at <paramref name="dist"/> metres: full near, gone at <see cref="SnapBuild.ShowRange"/> (0: hidden).</summary>
    public static float Fade(float dist) => Math.Clamp((SnapBuild.ShowRange - dist) / 4f, 0f, 1f);

    public static void Draw(Renderer gfx, double now)
    {
        if (!Active || Held is not { } def)
            return;
        MarkersDrawn = 0;
        var c = Body;
        if (c == null)
        {
            DrawFree(gfx, def);
            return;
        }
        var eye = gfx.CameraPosition;
        var body = Matrix4x4.CreateFromQuaternion(c.Rot) * Matrix4x4.CreateTranslation(c.Pos);
        var selected = ConstructRender.GhostSelected;
        var selPart = -1;
        var selSocket = -1;
        if (selected >= 0 && ConstructRender.GhostLive(c, selected))
        {
            selPart = ConstructRender.Ghosts[selected].Part;
            selSocket = ConstructRender.Ghosts[selected].Socket;
        }
        var flash = (float)(now - FlashAt);
        // (a) every free socket the held part can use.
        for (var k = 0; k < Cache.Count && MarkersDrawn < MaxMarkers; k++)
        {
            if (Cache.Part[k] >= c.PartCount)
                break; // (stale for a frame: the cache refreshes on the next input)
            c.SegPose(Cache.Seg[k], out var q, out var t);
            var local = t + Vector3.Transform(Cache.At[k], q);
            var world = c.ToWorld(local);
            var fade = Fade(Vector3.Distance(world, eye));
            if (fade <= 0f)
                continue;
            var dirW = Vector3.Transform(Vector3.Transform(ConstructParts.DirVector(Cache.Dir[k]), q), c.Rot);
            var chosen = Cache.Part[k] == selPart && Cache.Socket[k] == selSocket;
            var side = JointSides.SocketSide(c.Parts[Cache.Part[k]].D, Cache.Socket[k]);
            var col = side == 0 ? SnapBuild.Colour(Cache.Type[k]) : SideColour(side);
            Marker(gfx, Cache.Type[k], world + dirW * 0.03f, dirW, col, (chosen ? 0.95f : 0.6f) * fade, chosen ? 0.13f : 0.085f, side);
            if (chosen && flash is >= 0 and < 0.2f)
            {
                var f = flash / 0.2f;
                gfx.Draw(Matrix4x4.CreateScale(0.14f + 0.45f * f) * Matrix4x4.CreateTranslation(world), new Vector3(1f, 1f, 1f), 0.55f * (1 - f), mode: 2);
            }
            MarkersDrawn++;
        }
        // (b) the held part's own points, on its ghost.
        var n = SnapBuild.OwnPoints(def, Own);
        if (selPart >= 0)
        {
            ref var g = ref ConstructRender.Ghosts[selected];
            c.SegPose(c.Parts[g.Part].Seg, out var q, out var t);
            var frame = Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t) * body;
            var sc = g.Scale > 1 ? g.Scale : 1f;
            var single = SnapBuild.SinglePurpose(def);
            for (var o = 0; o < n; o++)
            {
                if (single && !Own[o].Usable)
                    continue;
                var at = g.Pos + Rot24.Apply(g.Rot, def.Sockets[o].At * sc);
                var dir = ConstructParts.DirVector(Rot24.ApplyDir(g.Rot, def.Sockets[o].Dir));
                var world = Vector3.Transform(at, frame);
                var dirW = Vector3.TransformNormal(dir, frame);
                var mates = o == g.DefSocket;
                var lined = false;
                for (var l = 0; l < LineCount; l++)
                    lined |= Lines[l].OwnSocket == o;
                var side = JointSides.SocketSide(def, o);
                var col = side == 0 ? SnapBuild.Colour(Own[o].Type) : SideColour(side);
                Marker(gfx, Own[o].Type, world + dirW * 0.03f, dirW, col, mates ? 1f : 0.8f, mates ? 0.12f : 0.08f, side);
                if (mates)
                    gfx.Draw(Matrix4x4.CreateScale(0.05f) * Matrix4x4.CreateTranslation(world + dirW * 0.03f), new Vector3(1f, 1f, 1f), 1f, mode: 2);
                if (lined)
                    gfx.Draw(Matrix4x4.CreateScale(0.2f) * Matrix4x4.CreateTranslation(world + dirW * 0.03f), new Vector3(0.3f, 1f, 0.45f), 0.4f + 0.2f * MathF.Sin((float)now * 9f), mode: 2);
            }
        }
    }

    /// <summary>Free placement: the held part's own points on the ghost standing on the surface.</summary>
    private static void DrawFree(Renderer gfx, ConstructPartDef def)
    {
        var n = SnapBuild.OwnPoints(def, Own);
        if (ConstructRender.PlaceAt is { } ground)
        {
            var frame = Matrix4x4.CreateFromQuaternion(Rot24.Quat(ConstructRender.PlaceTurn)) * Matrix4x4.CreateFromQuaternion(ConstructRender.PlaceRot) * Matrix4x4.CreateTranslation(ground);
            var single = SnapBuild.SinglePurpose(def);
            for (var o = 0; o < n; o++)
            {
                if (single && !Own[o].Usable)
                    continue;
                var world = Vector3.Transform(def.Sockets[o].At, frame);
                var dirW = Vector3.TransformNormal(ConstructParts.DirVector(def.Sockets[o].Dir), frame);
                var side = JointSides.SocketSide(def, o);
                Marker(gfx, Own[o].Type, world + dirW * 0.03f, dirW, side == 0 ? SnapBuild.Colour(Own[o].Type) : SideColour(side), 0.8f, 0.08f, side);
            }
        }
    }

    private static Vector3 SideColour(byte side) => side == JointSides.Leaf ? JointSides.LeafColour : JointSides.BaseColour;

    /// <summary>One marker: its shape says the type (cube: a mount, bar with a tip: a shaft, diamond: a gun part, cross: fuel, power or data, flat disc: a wheel hub or hinge leaf).</summary>
    private static void Marker(Renderer gfx, ConnType type, Vector3 at, Vector3 dir, Vector3 colour, float alpha, float size, byte side = 0)
    {
        var len = dir.Length();
        var z = len < 1e-4f ? Vector3.UnitY : dir / len;
        var x = MathF.Abs(z.Y) < 0.95f ? Vector3.Normalize(Vector3.Cross(Vector3.UnitY, z)) : Vector3.UnitX;
        var y = Vector3.Cross(z, x);
        var basis = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        void Box(Vector3 scale, Vector3 offset, float a = 1f) =>
            gfx.Draw(Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(offset) * basis * Matrix4x4.CreateTranslation(at), colour, alpha * a, mode: 2);
        if (side == JointSides.Leaf)
        {
            // A joint's moving side (orange): a flat disc with a bar across it, so it never reads as a plain mount.
            Box(new Vector3(size * 2.2f, size * 2.2f, size * 0.35f), Vector3.Zero);
            Box(new Vector3(size * 3.4f, size * 0.5f, size * 0.7f), new Vector3(0, 0, size * 0.3f));
            return;
        }
        if (side == JointSides.Base)
        {
            // A joint's base (cool steel): a squat plate with a bolt at each corner, the fixed look of a mount.
            Box(new Vector3(size * 2f, size * 2f, size * 0.5f), Vector3.Zero);
            for (var k = 0; k < 4; k++)
                Box(new Vector3(size * 0.5f), new Vector3((k & 1) == 0 ? -size * 0.85f : size * 0.85f, (k & 2) == 0 ? -size * 0.85f : size * 0.85f, size * 0.3f));
            return;
        }
        switch (type)
        {
            case ConnType.ShaftIn or ConnType.ShaftOut:
                Box(new Vector3(size * 0.35f, size * 0.35f, size * 3f), new Vector3(0, 0, size * 1.2f));
                Box(new Vector3(size * 0.9f, size * 0.9f, size * 0.6f), new Vector3(0, 0, type == ConnType.ShaftOut ? size * 2.7f : 0));
                break;
            case ConnType.Barrel or ConnType.Muzzle or ConnType.Optic or ConnType.Feed:
                gfx.Draw(Matrix4x4.CreateScale(size * 1.1f) * Matrix4x4.CreateFromYawPitchRoll(0.785f, 0.615f, 0) * Matrix4x4.CreateTranslation(at), colour, alpha, mode: 2);
                break;
            case ConnType.Fuel or ConnType.Power or ConnType.Data:
                Box(new Vector3(size * 2.2f, size * 0.4f, size * 0.4f), Vector3.Zero);
                Box(new Vector3(size * 0.4f, size * 2.2f, size * 0.4f), Vector3.Zero);
                break;
            case ConnType.Hub or ConnType.Leaf:
                Box(new Vector3(size * 2.2f, type == ConnType.Hub ? size * 2.2f : size * 0.8f, size * 0.35f), Vector3.Zero);
                break;
            default:
                Box(new Vector3(size), Vector3.Zero);
                break;
        }
    }
}
