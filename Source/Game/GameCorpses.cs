using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Corpses that pile up and that players walk over (Sandbox; docs/sandbox/CORPSES.md). Bodies fall on each other in the
/// ragdoll pool (<see cref="Ragdolls"/>, <see cref="Ragdoll.Others"/>), which every PC runs from the kill it hears. When a
/// body falls asleep on the host, its pose (rounded as the wire carries it) goes to every client in a
/// <see cref="Msg.Corpse"/>, which takes that pose for its own body (or makes one: a joiner gets every settled body), and
/// when the host's body goes (faded, recycled) the clients' fades too. So the host's "Bodies and gore" setting holds for
/// the bodies, and everyone has the same pile.
///
/// A settled body is ground: after each movement tick (server, the client's prediction and its replay, the same code) a
/// player whose feet are over a body's particles stands on top of them (at most a step up, <see cref="World.StepHeight"/>),
/// and a pile taller than a step is a wall they are slid round, so they climb a heap from its low side. Only settled
/// bodies count (the falling ones differ a little between PCs), so prediction and the server agree. Fixed arrays, no
/// allocation; a world with no bodies pays one loop over the pool's flags.
/// </summary>
public sealed partial class Game
{
    /// <summary>A body's particles stand this much proud of their collision radius for feet (m): clothes and the boxes drawn round them.</summary>
    public const float CorpseSkin = 0.03f;

    /// <summary>The disc under a player's feet that stands on a body (m): smaller than the body, so an edge doesn't hold you up.</summary>
    public const float CorpseFoot = 0.22f;

    /// <summary>The most a pile pushes a player sideways in one tick (m).</summary>
    public const float CorpseMaxPush = 0.25f;

    /// <summary>Players stood on bodies and pushed off piles (self-test, logs).</summary>
    public long CorpseStands, CorpsePushes;

    private PacketWriter? _corpseW;
    private readonly ushort[] _corpseGone = new ushort[64];
    private readonly Vector3[] _corpsePose = new Vector3[RagdollRig.Count];

    private const byte CorpseSettled = 1, CorpseGoneOp = 2;

    /// <summary>After a tick of <paramref name="p"/>'s movement (Sandbox): on top of the settled bodies under their feet, and out of piles taller than a step.</summary>
    private void CorpsePlayer(Player p, ref MoveState s)
    {
        if (!SandboxOn || s.Noclip || s.MantleTicks > 0 || s.RideBody != 0 || s.RideUp != Vector3.Zero || !Ragdolls.AnyWalkable(this))
            return;
        var feet = s.Position;
        var up = GravityFrames.Curved ? GravityFrames.UpAt(feet) : Vector3.UnitY;
        var h = World.HeightOf(s.Stance);
        var reach = new Vector3(World.PlayerRadius + 0.25f);
        var head = feet + up * h;
        var lo = Vector3.Min(feet, head) - reach;
        var hi = Vector3.Max(feet, head) + reach;
        var radius = RagdollRig.Radius;
        var rise = float.NegativeInfinity;
        var push = Vector3.Zero;
        for (var k = 0; k < Ragdolls.Capacity; k++)
        {
            var b = Ragdolls.Body(k);
            if (!Ragdolls.Walkable(b))
                continue;
            var sim = b.Sim;
            if (sim.BoundsMin.X > hi.X || sim.BoundsMax.X < lo.X || sim.BoundsMin.Y > hi.Y || sim.BoundsMax.Y < lo.Y || sim.BoundsMin.Z > hi.Z || sim.BoundsMax.Z < lo.Z)
                continue;
            for (var j = 0; j < RagdollRig.Count; j++)
            {
                var r = radius[j] + CorpseSkin;
                var d = sim.Pos[j] - feet;
                var along = Vector3.Dot(d, up);
                if (along - r >= h || along + r <= -0.01f)
                    continue; // over their head, or under their feet
                var lat = d - up * along;
                var lat2 = lat.LengthSquared();
                var contact = r + World.PlayerRadius;
                if (lat2 >= contact * contact)
                    continue;
                var latLen = MathF.Sqrt(lat2);
                var off = MathF.Max(0f, latLen - CorpseFoot);
                var top = off < r ? along + MathF.Sqrt(r * r - off * off) : float.NegativeInfinity;
                if (top <= World.StepHeight && off < r)
                {
                    rise = MathF.Max(rise, top); // under the feet, low enough to step on
                    continue;
                }
                if (along + r <= World.StepHeight || latLen < 1e-4f)
                    continue; // beside the feet and low (walking on it steps up), or dead centre (no side to push to)
                push += -lat / latLen * (contact - latLen); // a pile taller than a step: slid round it
            }
        }
        if (push != Vector3.Zero)
        {
            var len = push.Length();
            var dir = push / len;
            var to = feet + dir * MathF.Min(len, CorpseMaxPush);
            if (!World.Blocked(to, h)) // (never into a wall)
            {
                s.Position = feet = to;
                var vn = Vector3.Dot(s.Velocity, dir);
                if (vn < 0)
                    s.Velocity -= dir * vn;
                CorpsePushes++;
            }
        }
        if (rise <= -0.01f)
            return;
        var vUp = Vector3.Dot(s.Velocity, up);
        if (rise > 0 && !World.Blocked(feet + up * rise, h))
            s.Position = feet + up * rise;
        else if (rise > 0)
            return; // no headroom over it: the body stays under them
        if (vUp > 1f)
            return; // jumping off it
        if (vUp < 0)
            s.Velocity -= up * vUp;
        s.Grounded = true;
        CorpseStands++;
    }

    // ---- The host's settled bodies to the clients ------------------------------------------------------------------

    /// <summary>Every frame (Program, after the pool's step): the host sends the bodies that settled and the ones that went.</summary>
    public void CorpseFrame(double now)
    {
        if (!IsServer)
            return;
        if (Net.IsOffline || Net.ConnectionCount == 0)
        {
            var c = 0;
            while (Ragdolls.TakeSettled(ref c) != null) { } // (nobody to tell)
            Ragdolls.TakeGone(_corpseGone);
            return;
        }
        var cursor = 0;
        while (Ragdolls.TakeSettled(ref cursor) is { } b)
            Net.Broadcast(WriteCorpse(b), PacketReliability.ReliableOrdered);
        var gone = Ragdolls.TakeGone(_corpseGone);
        if (gone == 0)
            return;
        var w = _corpseW ??= new PacketWriter(256);
        w.Begin(Msg.Corpse).U8(CorpseGoneOp).U8((byte)gone);
        for (var i = 0; i < gone; i++)
            w.U16(_corpseGone[i]);
        Net.Broadcast(w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Server: a joiner gets every body that has settled.</summary>
    private void SendCorpseState(RakNetGUID to)
    {
        var cursor = 0;
        while (Ragdolls.NextSettled(ref cursor) is { } b)
            Net.SendTo(to, WriteCorpse(b), PacketReliability.ReliableOrdered);
    }

    /// <summary>
    /// <see cref="Msg.Corpse"/> settled: u8 1, u16 the host's number, u8 owner (255 a zombie), u16 the zombie's id, u8 look,
    /// vec the first particle, then each other particle's offset from it in millimetres (3 x i16).
    /// </summary>
    internal ReadOnlySpan<byte> WriteCorpse(RagdollBody b)
    {
        var w = _corpseW ??= new PacketWriter(256);
        var pos = b.Sim.Pos;
        w.Begin(Msg.Corpse).U8(CorpseSettled).U16(b.Serial).U8(b.Owner).U16(b.ZId).U8((byte)b.Look).Vec(pos[0]);
        for (var i = 1; i < RagdollRig.Count; i++)
        {
            var d = (pos[i] - pos[0]) * 1000f;
            w.U16((ushort)Ragdolls.Clamp16(d.X)).U16((ushort)Ragdolls.Clamp16(d.Y)).U16((ushort)Ragdolls.Clamp16(d.Z));
        }
        return w.Span;
    }

    /// <summary>Client: a <see cref="Msg.Corpse"/> (after the id).</summary>
    internal void ReadCorpse(ref PacketReader r)
    {
        if (!r.Has(1))
            return;
        var op = r.U8();
        if (op == CorpseGoneOp)
        {
            if (!r.Has(1))
                return;
            var n = r.U8();
            for (var i = 0; i < n && r.Has(2); i++)
                Ragdolls.HostGone(r.U16(), Ragdolls.Now);
            return;
        }
        if (op != CorpseSettled || !r.Has(2 + 1 + 2 + 1 + 12 + (RagdollRig.Count - 1) * 6))
            return;
        var serial = r.U16();
        var owner = r.U8();
        var zid = r.U16();
        var look = r.U8();
        var o = r.Vec();
        _corpsePose[0] = o;
        for (var i = 1; i < RagdollRig.Count; i++)
            _corpsePose[i] = o + new Vector3((short)r.U16(), (short)r.U16(), (short)r.U16()) / 1000f;
        if (look > (byte)RagdollLook.Zombie || !float.IsFinite(o.X + o.Y + o.Z))
            return;
        Ragdolls.HostSettled(this, serial, owner, zid, (RagdollLook)look, _corpsePose, Ragdolls.Now);
    }
}
