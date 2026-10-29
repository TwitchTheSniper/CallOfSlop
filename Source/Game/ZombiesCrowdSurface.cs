using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The crowd as ground (docs/zombies/MASS-HORDE.md, "Crowd physics"): on a crowd map the crowd's density is a height
/// surface (<see cref="CrowdSurface"/>) over the network grid's cells. After each tick of a player's movement (server,
/// the client's prediction and its replay, through <see cref="ZBlockPlayer"/>, like zombie bodies) the surface pushes
/// the player down its slope, towards thinner crowd; a player whose feet are near its top stands on it (the crowd carries
/// them: they can walk over a packed horde and jump off it), and one deep inside is pushed out sideways and lifted up
/// onto it, so nobody is buried in the mass. The server reads its own counts (the ones it sends), a client its picture
/// of the cells (<see cref="CrowdSeen"/>); the two agree within a send. Nothing on other maps, nothing allocated.
/// </summary>
public sealed partial class Game
{
    /// <summary>The surface (server: the sent counts; client: the picture).</summary>
    public readonly CrowdSurface CrowdGround = new();

    /// <summary>Players stood on the crowd, pushed by its slope and lifted out of it (self-test, log).</summary>
    public long CrowdStands, CrowdSlides, CrowdLifts;

    /// <summary>Points the surface at this side's counts (a few fields: every tick is fine). False: no crowd here.</summary>
    private bool CrowdSurfaceBind()
    {
        var t = ZRules.Crowd;
        CrowdGround.Height = t.SurfaceHeight;
        CrowdGround.Packed = t.SurfacePacked;
        CrowdGround.MinHeight = t.SurfaceMin;
        CrowdGround.Terrain = World.Map.Terrain; // (a terrain map: the surface rises from the ground under it)
        if (IsServer && CrowdOn && Crowd.Ready)
        {
            if (Crowd.Total == 0 || _crowdCount.Length == 0)
                return false;
            var cell = t.NetCell;
            var (cols, rows) = Crowd.DensitySize(cell);
            if (_crowdCount.Length != cols * rows)
                return false;
            CrowdGround.Bind(cols, rows, cell, Crowd.MinX, Crowd.MinZ, Crowd.Y, _crowdCount);
            return CrowdGround.Bound;
        }
        var pic = CrowdSeen; // (a client's picture)
        if (pic.Round < 0 || pic.Total == 0)
            return false;
        CrowdGround.Bind(pic.Cols, pic.Rows, pic.Cell, pic.MinX, pic.MinZ, pic.Y, pic.Count);
        return CrowdGround.Bound;
    }

    /// <summary>After a tick of <paramref name="p"/>'s movement (from <see cref="ZBlockPlayer"/>): the crowd surface's push, stand and lift.</summary>
    private void CrowdSurfacePlayer(Player p, ref MoveState s)
    {
        if (Mode != GameMode.Zombies || s.MantleTicks > 0 || IsDowned(p) || !ZRules.Crowd.Surface || !CrowdSurfaceBind())
            return;
        var feet = s.Position;
        var h = CrowdGround.HeightAt(feet.X, feet.Z);
        if (h < CrowdGround.MinHeight)
            return;
        var floor = CrowdGround.FloorAt(feet.X, feet.Z);
        var top = floor + h;
        var t = ZRules.Crowd;
        if (feet.Y > top + 0.05f || feet.Y < floor - 1f)
            return; // in the air above it (it lands next tick), or on another floor
        var (sx, sz) = CrowdGround.SlopeAt(feet.X, feet.Z);
        var dt = World.TickDt;
        var ph = World.HeightOf(s.Stance);
        if (feet.Y >= top - t.SurfaceStep)
        {
            // On it: stood up to its top, the slope sliding them towards thinner crowd.
            if (feet.Y < top && !ZBlockBodyBlocked(new Vector3(feet.X, top, feet.Z), ph))
                s.Position.Y = top;
            if (s.Velocity.Y < 0)
                s.Velocity.Y = 0;
            s.Grounded = true;
            CrowdStands++;
            var slide = new Vector3(-sx, 0, -sz) * (t.SurfacePush * dt);
            if (slide.LengthSquared() > 1e-8f && ZBlockTryPush(ref s.Position, CrowdClampPush(slide), ph))
            {
                CrowdSlides++;
                NotePushed(p); // (the outside log's "pushed", ZombiesOutside.cs)
            }
            return;
        }
        // Deep inside: out sideways down the slope, and up onto it.
        var push = new Vector3(-sx, 0, -sz) * (t.SurfacePush * 2 * dt);
        if (push.LengthSquared() > 1e-8f && ZBlockTryPush(ref s.Position, CrowdClampPush(push), ph))
        {
            CrowdSlides++;
            NotePushed(p);
        }
        var lift = MathF.Min(t.SurfaceLift * dt, top - s.Position.Y);
        if (lift > 0 && !ZBlockBodyBlocked(s.Position + new Vector3(0, lift, 0), ph))
        {
            s.Position.Y += lift;
            if (s.Velocity.Y < 0)
                s.Velocity.Y = 0;
            s.Grounded = true;
            CrowdLifts++;
            NotePushed(p);
        }
    }

    private static Vector3 CrowdClampPush(Vector3 push)
    {
        var len = push.Length();
        return len > ZBlockMaxPush ? push * (ZBlockMaxPush / len) : push;
    }

    /// <summary>Self-test: one tick of <paramref name="p"/>'s movement with <paramref name="cmd"/>, then zombie bodies and the crowd surface.</summary>
    public void SelfTestCrowdSurfaceMove(Player p, ref MoveState s, in InputCmd cmd)
    {
        World.Simulate(ref s, cmd);
        ZBlockPlayer(p, ref s);
    }

    /// <summary>Self-test: the surface's height over the floor at (x, z) on this side (server or client), -1 without a crowd.</summary>
    public float SelfTestCrowdSurfaceHeight(float x, float z) => CrowdSurfaceBind() ? CrowdGround.HeightAt(x, z) : -1f;
}
