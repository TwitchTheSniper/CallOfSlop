using System.Numerics;

namespace CallOfSlop;

/// <summary>What a launch event says a shell is drawn as (a wire byte, protocol 184).</summary>
public enum ShellLookKind : byte
{
    None, // a plain box (a cannon's shell, a bomblet), or a rocket's stand-in
    Part, // a part file's model: CFireEvent.Look is its id (the same on every PC)
    Design, // a designed weapon's run-time part: CFireEvent.Look is its key hashed (ConstructPartDef.ExHash; ids are each machine's own)
}

/// <summary>
/// How bombs and missiles look in the air (docs/sandbox/CONSTRUCTS.md, "Modular ordnance: in the air"): the model they had on the
/// rack, turned as they hung there and eased onto their way over the first moments, nose (-Z) along their velocity. The launch event
/// carries the model and the turn; every PC turns its own copy each tick the same way (<see cref="Turn"/>), so host and clients
/// draw alike. Drawing is ConstructRenderShells.cs. Nothing here allocates per tick.
/// </summary>
public static class ShellLooks
{
    public const int AlignTicks = 45; // ticks from the rack's turn to flying nose first
    public const float AlignSpeed = 1.5f; // m/s below which it keeps its turn (let go from a hover: it tips over as it falls)

    // A launcher's rocket or missile has no model of its own (the Missile Rail and Rocket Pod are the launchers): a stand-in made
    // from the bench's own sections, the same on every PC.
    private const string RocketKey = Ordnance.Prefix + "M9:Impact Fuse+Warhead HE+Tail Fins+Solid Motor";
    private const string MissileKey = Ordnance.Prefix + "M12:Proximity Fuse+Warhead HE+Canards+Tail Fins+Solid Motor";
    private static ConstructPartDef? _rocket, _missile;
    private static bool _rocketTried, _missileTried;

    /// <summary>Does a launch of this kind carry a look on the wire?</summary>
    public static bool Carries(ShellKind k) => k is ShellKind.Bomb or ShellKind.Rocket or ShellKind.Missile;

    /// <summary>Server: the launch event of part <paramref name="part"/> (still on its rack) carries its model and its turn.</summary>
    public static CFireEvent WithLook(this CFireEvent e, Construct c, int part)
    {
        var d = c.DefOf(part);
        if (d.IsOrdnance)
            (e.LookKind, e.Look) = (ShellLookKind.Design, d.ExHash);
        else if (!d.RuntimeMade)
            (e.LookKind, e.Look) = (ShellLookKind.Part, d.Id);
        else
            return e;
        c.WorldFrame(c.Parts[part].Seg, out var q, out _);
        e.Rot = Quaternion.Normalize(q * Rot24.Quat(c.Parts[part].Rot));
        return e;
    }

    public static void Write(PacketWriter w, in CFireEvent e)
    {
        w.U8((byte)e.LookKind);
        if (e.LookKind == ShellLookKind.None)
            return;
        if (e.LookKind == ShellLookKind.Part)
            w.U16((ushort)e.Look);
        else
            w.U32(e.Look);
        var q = e.Rot.W < 0 ? Quaternion.Negate(e.Rot) : e.Rot; // (a byte a part: a degree or so, and it eases onto its way anyway)
        w.U8(Snorm(q.X)).U8(Snorm(q.Y)).U8(Snorm(q.Z)).U8(Snorm(q.W));
    }

    public static void Read(ref PacketReader r, ref CFireEvent e)
    {
        var kind = r.U8();
        if (kind > (byte)ShellLookKind.Design)
            throw new MalformedPacketException($"a shell's look of kind {kind}");
        e.LookKind = (ShellLookKind)kind;
        if (e.LookKind == ShellLookKind.None)
            return;
        e.Look = e.LookKind == ShellLookKind.Part ? r.U16() : r.U32();
        var q = new Quaternion(Unorm(r.U8()), Unorm(r.U8()), Unorm(r.U8()), Unorm(r.U8()));
        e.Rot = q.LengthSquared() > 0.25f ? Quaternion.Normalize(q) : Quaternion.Identity;
    }

    private static byte Snorm(float v) => (byte)(sbyte)Math.Clamp((int)MathF.Round(v * 127f), -127, 127);

    private static float Unorm(byte b) => (sbyte)b / 127f;

    /// <summary>The model a launch event names (null: drawn as a box) and the turn it starts at.</summary>
    public static ConstructPartDef? Resolve(in CFireEvent e, out Quaternion rot)
    {
        rot = e.Rot;
        switch (e.LookKind)
        {
            case ShellLookKind.Part:
                return e.Look < (uint)ConstructParts.All.Count && ConstructParts.All[(int)e.Look] is { Boxes.Length: > 0 } part ? part : null;
            case ShellLookKind.Design:
                return Ordnance.ByHash(e.Look);
        }
        // A launcher's rocket or missile (not a designed one's warheads): its stand-in, nose along its way.
        var kind = (ShellKind)e.Muzzle;
        if (kind is not (ShellKind.Rocket or ShellKind.Missile) || (e.Ext & 1) != 0)
            return null;
        rot = Facing(e.To);
        return kind == ShellKind.Rocket ? Stand(ref _rocket, ref _rocketTried, RocketKey) : Stand(ref _missile, ref _missileTried, MissileKey);
    }

    private static ConstructPartDef? Stand(ref ConstructPartDef? def, ref bool tried, string key)
    {
        if (!tried)
        {
            tried = true;
            def = Ordnance.Parse(key); // (null if the sections are renamed: the box it always was)
            if (def == null)
                Console.WriteLine($"[ordnance] no stand-in model for launched rockets: \"{key}\" doesn't make a design");
        }
        return def;
    }

    /// <summary>Every PC, each tick a shell with a look flies: its nose eased onto its way, keeping its roll (the least turn that gets it there), fully along it after <see cref="AlignTicks"/>.</summary>
    public static void Turn(ref CShell s)
    {
        s.PrevRot = s.Rot;
        var speed = s.Vel.Length();
        if (speed < AlignSpeed)
            return;
        var way = s.Vel / speed;
        var nose = Vector3.Transform(-Vector3.UnitZ, s.Rot);
        var onto = Quaternion.Normalize(Quaternion.Concatenate(s.Rot, FromTo(nose, way)));
        var k = MathF.Min(1f, (s.Age + 1) / (float)AlignTicks);
        s.Rot = k >= 1f ? onto : Quaternion.Normalize(Quaternion.Slerp(s.Rot, onto, k * k)); // (slow off the rack, quickening: no snap)
    }

    /// <summary>The least turn taking direction <paramref name="a"/> onto <paramref name="b"/> (both unit).</summary>
    public static Quaternion FromTo(Vector3 a, Vector3 b)
    {
        var d = Vector3.Dot(a, b);
        if (d > 0.999999f)
            return Quaternion.Identity;
        if (d < -0.9999f)
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(a, MathF.Abs(a.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX)), MathF.PI);
        var c = Vector3.Cross(a, b);
        return Quaternion.Normalize(new Quaternion(c.X, c.Y, c.Z, 1f + d));
    }

    /// <summary>The turn pointing -Z along <paramref name="v"/>, level (its up nearest the world's).</summary>
    public static Quaternion Facing(Vector3 v)
    {
        if (v.LengthSquared() < 1e-6f)
            return Quaternion.Identity;
        var z = -Vector3.Normalize(v);
        var x = MathF.Abs(z.Y) < 0.95f ? Vector3.Normalize(Vector3.Cross(Vector3.UnitY, z)) : Vector3.UnitX;
        var y = Vector3.Cross(z, x);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1)));
    }
}

public sealed partial class ConstructWorld
{
    /// <summary>Every PC, at a launch event: the shell's model and its turn on the rack (host and clients alike).</summary>
    public void SetShellLook(in CFireEvent e)
    {
        var k = ShellAt(e.Shell);
        if (k < 0)
            return;
        ref var s = ref Shells[k];
        s.Look = ShellLooks.Resolve(e, out var rot);
        s.Rot = s.PrevRot = rot;
    }
}
