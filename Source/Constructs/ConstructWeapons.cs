using System.Numerics;

namespace CallOfSlop;

/// <summary>One gun of a construct, worked out from its parts when the construct changes (<see cref="ConstructWeapons.BuildGuns"/>).</summary>
public struct CGun
{
    public byte Part, Seg; // the gun part (a receiver or a heavy gun) and its segment
    public WeaponDef? Def; // its numbers (the weapon builder's stat pipeline for a receiver); null: it can't fire (Problem says why)
    public string Problem; // why it won't fire ("" when it can)
    public Vector3 Muzzle, Forward; // in its segment's rest frame
    public int Shots; // rounds out of the muzzles a shot (barrels x pellets)
    public float Spread; // radians, half the cone
    public float MaxRange;
    public ushort Interval, ReloadTicks, Mag; // ticks between shots, ticks to reload, rounds a load
    public GunEffect Effects;
    public GunFx Fx;
    public float Blast, BlastDamage, Speed, Kick; // the splash of an explosive round, the tracer's speed, the push back on the body
    public byte BoxCount; // ammo boxes its feed chain reaches (Construct.GunBoxes, BoxesPerGun a gun)
    public sbyte Seat; // the gunner's seat that fires and aims it (when nothing is wired into it), or -1
    public byte Sensor; // a proximity sensor wired into it: a sentry, pointing at the nearest player in the sensor's range (255: none)
    public byte AimJoints; // turntables and motor hinges up its chain that a gunner or a sensor can point
    public ushort PerShot; // rounds a shot takes from its load (a merged receiver's: N squared; 0 or 1: one)
    public byte MergeN; // a merged receiver's N (0: a single receiver or a heavy gun; ConstructMerge.cs)
}

/// <summary>What a construct gun's shot met (the fire events' flags).</summary>
[Flags]
public enum CFireHit : byte
{
    None = 0,
    World = 1,
    Part = 2,
    Target = 4, // a player, a bot or a zombie
    Through = 8, // it went through at least one part (penetrating)
    Burns = 16, // incendiary
}

/// <summary>What a fire event is (Msg.CFire).</summary>
public enum CFireKind : byte
{
    Shot, // a gun fired one round: body, part, where it ended
    Blast, // an explosive round's splash: where, how big
    CookOff, // an ammo box (or a burning tank) blew up: where, how big
    Shatter, // a glass part broke (protocol 87): where, and its world bounds' half size (shards burst out of it)
}

/// <summary>One thing the guns did this tick (the server sends them as Msg.CFire; the host draws its own straight away).</summary>
public struct CFireEvent
{
    public CFireKind Kind;
    public ushort Body;
    public byte Part;
    public Vector3 At; // a shot's end, a blast's centre
    public CFireHit Hit;
    public float Radius; // a blast's
    public Vector3 Half; // a shatter's: the glass part's world bounds' half size
}

/// <summary>A blast waiting to go off (an ammo box cooking off: a beat after it is holed).</summary>
public struct CCookOff
{
    public Vector3 At;
    public float Radius, Damage;
    public int Ticks;
    public ushort Body; // where it came from (credit)
}

/// <summary>Who and what a construct gun can hit besides construct parts and the map (the game: players, bots, zombies).</summary>
public interface IConstructTargets
{
    /// <summary>The nearest target along a ray (not riding <paramref name="ignoreBody"/>), before <paramref name="maxDist"/>.</summary>
    bool RayTarget(Vector3 o, Vector3 d, float maxDist, ushort ignoreBody, out float dist, out int id);

    /// <summary>A round hit target <paramref name="id"/> (from <see cref="RayTarget"/>) for <paramref name="damage"/>.</summary>
    void HitTarget(int id, float damage, GunEffect effects, Construct by, int gunPart, Vector3 at, Vector3 dir);

    /// <summary>A blast hurts every target in range (walls shield).</summary>
    void BlastTargets(Vector3 at, float radius, float damage, Construct? by);
}

/// <summary>
/// Construct weapons (milestone 3, decision 10; docs/sandbox/CONSTRUCTS.md, "Construct weapons"), server only, every tick
/// before the logic: each gun's trigger (a wired signal, else its gunner's fire key), the gunner's (or a sentry's) aim
/// on the turntables and motor hinges up the gun's chain, its feed (rounds loaded from the ammo boxes its feed chain
/// reaches, a reload when it runs dry), and its shots: hitscan rounds against the map, construct parts and targets,
/// with the weapon builder's ammo flags (penetrating rounds go on through a part into what is behind it, explosive ones
/// splash, incendiary ones set fuel, wood and ammo alight). Burning parts burn down; holed ammo boxes cook off. Fixed
/// arrays only: nothing allocates per tick (the numbers are worked out when a construct changes).
/// </summary>
public static class ConstructWeapons
{
    /// <summary>What weapon builder parts go on a construct gun.</summary>
    public static readonly PartCategory[] GunPartCategories =
        [PartCategory.Barrel, PartCategory.Splitter, PartCategory.Feed, PartCategory.Ammo, PartCategory.Muzzle, PartCategory.Optic];

    public const float BurnSeconds = 12f; // how long a part burns
    public const float CookOffDelay = 0.35f; // a holed box goes up this long after
    public const int MaxPierce = 3; // parts a penetrating round goes through
    public const float OpticSpread = 0.6f; // an optic steadies a gun: this share of its spread
    public const float SentryHoldFire = 0.14f; // a sentry fires once it points within this (radians) of its target
    public const float SentryAimHeight = 1.1f; // a sentry aims at a player's chest (this above the feet)

    private static uint _seq;

    // ---- The numbers (rebuilt with the construct) --------------------------------------------------------------

    /// <summary>Every gun of a construct: its numbers, its feed, the seat and joints that work it (allocates: only when the construct changes).</summary>
    public static void BuildGuns(Construct c)
    {
        c.GunCount = 0;
        c.Burning = 0;
        for (var j = 0; j < c.JointCount; j++)
            c.Joints[j].Aimed = false;
        Span<bool> taken = stackalloc bool[Construct.MaxParts];
        for (var i = 0; i < c.PartCount; i++)
        {
            if (c.Parts[i].Burn > 0)
                c.Burning++;
            if (c.Parts[i].D.Role != PartRole.Gun || c.GunCount >= Construct.MaxGuns)
                continue;
            if (c.MergeOf[i] != 0 && !c.MergeLead(i))
                continue; // (a merged receiver's other members: its lead is the gun)
            ref var g = ref c.Guns[c.GunCount];
            g = new CGun { Part = (byte)i, Seg = c.Parts[i].Seg, Problem = "", Seat = -1, Sensor = 255 };
            Span<bool> mine = stackalloc bool[Construct.MaxParts];
            try
            {
                if (c.Parts[i].D.IsReceiverGun)
                    BuildReceiver(c, i, ref g, taken, mine);
                else
                    BuildHeavy(c, i, ref g, taken, mine);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NullReferenceException)
            {
                g.Def = null;
                g.Problem = $"the gun doesn't add up ({ex.Message})";
            }
            if (g.Def is { } w)
                Finish(c, ref g, w);
            Feed(c, c.GunCount, mine);
            Controls(c, ref g);
            c.GunCount++;
        }
    }

    private static int GunNeighbour(Construct c, int part, int k)
    {
        ref var l = ref c.Links[k];
        return l.A == part ? l.B : l.B == part ? l.A : -1;
    }

    /// <summary>A gun on a weapon builder receiver: its barrel (or splitter and barrels), muzzle devices, feed, ammo and optic are the gun parts bolted to it, put together as the weapon builder would.</summary>
    private static void BuildReceiver(Construct c, int i, ref CGun g, Span<bool> taken, Span<bool> mine)
    {
        var d = c.Parts[i].D;
        var rec = Parts.Find(d.GunReceiver)!;
        var act = Parts.Find(d.GunAction)!;
        var root = new BuildNode(rec);
        var problem = "";
        void Fit(BuildNode parent, string socket, PartDef part)
        {
            var s = Array.Find(parent.Part.Sockets, x => x.Name.Equals(socket, StringComparison.OrdinalIgnoreCase));
            if (s == null)
            {
                if (problem.Length == 0)
                    problem = $"the {parent.Part.Name} has no {socket} socket for the {part.Name}";
                return;
            }
            if (BuildRules.SocketWhy(parent, s, part) is { } why)
            {
                if (problem.Length == 0)
                    problem = why.TrimEnd('.');
                return;
            }
            parent.Attach(s.Name, part);
        }
        Fit(root, "action", act);
        mine[i] = true;
        var merged = c.MergeLead(i);
        var ownSockets = d.Sockets.Length;
        if (merged)
            for (var k = 0; k < c.PartCount; k++)
                if (c.MergeOf[k] == c.MergeOf[i])
                    mine[k] = taken[k] = true; // (its members: boxes bolted to any of them feed it)
        int barrel = -1, feed = -1, ammo = -1, optic = -1;
        // The gun parts bolted straight onto the receiver.
        for (var k = 0; k < c.LinkCount; k++)
        {
            var o = GunNeighbour(c, i, k);
            if (o < 0 || taken[o] || !c.Parts[o].Working || c.Parts[o].D.Role != PartRole.GunPart || Parts.Find(c.Parts[o].D.GunPartName) is not { } wp)
                continue;
            if (merged && (c.Links[k].A == i ? c.Links[k].SA : c.Links[k].SB) < ownSockets)
                continue; // (a merged receiver takes gun parts on its centre sockets only)
            ref var slot = ref barrel;
            switch (wp.Category)
            {
                case PartCategory.Barrel or PartCategory.Splitter: slot = ref barrel; break;
                case PartCategory.Feed: slot = ref feed; break;
                case PartCategory.Ammo: slot = ref ammo; break;
                case PartCategory.Optic: slot = ref optic; break;
                default:
                    if (problem.Length == 0)
                        problem = $"a {c.Parts[o].D.Name} goes on a barrel, not on the receiver";
                    continue;
            }
            if (slot >= 0)
            {
                if (problem.Length == 0)
                    problem = $"two {Plural(wp.Category)} on one receiver";
                continue;
            }
            slot = o;
        }
        if (feed >= 0)
            Fit(root, "feed", Parts.Find(c.Parts[feed].D.GunPartName)!);
        Fit(root, "ammo", ammo >= 0 ? Parts.Find(c.Parts[ammo].D.GunPartName)! : Parts.Find("FMJ") ?? throw new InvalidOperationException("no FMJ ammo part"));
        Span<int> muzzleParts = stackalloc int[16];
        var muzzles = 0;
        if (barrel >= 0)
        {
            var bp = Parts.Find(c.Parts[barrel].D.GunPartName)!;
            var node = new BuildNode(bp);
            mine[barrel] = true;
            muzzleParts[muzzles++] = barrel;
            if (bp.Category == PartCategory.Splitter)
            {
                // Its barrels: the barrel parts bolted to the splitter, onto its barrel sockets in turn.
                var socket = 0;
                for (var k = 0; k < c.LinkCount; k++)
                {
                    var o = GunNeighbour(c, barrel, k);
                    if (o < 0 || o == i || taken[o] || mine[o] || !c.Parts[o].Working || c.Parts[o].D.Role != PartRole.GunPart || Parts.Find(c.Parts[o].D.GunPartName) is not { Category: PartCategory.Barrel } sb)
                        continue;
                    while (socket < bp.Sockets.Length && bp.Sockets[socket].Type != "barrel")
                        socket++;
                    if (socket >= bp.Sockets.Length)
                    {
                        if (problem.Length == 0)
                            problem = $"more barrels than the {bp.Name} has outputs";
                        break;
                    }
                    var sock = bp.Sockets[socket++];
                    if (BuildRules.SocketWhy(node, sock, sb) is { } bad)
                    {
                        if (problem.Length == 0)
                            problem = bad.TrimEnd('.');
                        continue;
                    }
                    var bn = new BuildNode(sb);
                    node.Attach(sock.Name, bn);
                    MuzzleOn(c, o, bn, ref problem, taken, mine, muzzleParts, ref muzzles);
                    mine[o] = true;
                    if (muzzles < muzzleParts.Length)
                        muzzleParts[muzzles++] = o;
                }
            }
            else
                MuzzleOn(c, barrel, node, ref problem, taken, mine, muzzleParts, ref muzzles);
            var bs = Array.Find(rec.Sockets, x => x.Type == "barrel");
            if (bs == null)
                problem = problem.Length > 0 ? problem : $"the {rec.Name} takes no barrel";
            else if (BuildRules.SocketWhy(root, bs, bp) is { } why)
                problem = problem.Length > 0 ? problem : why.TrimEnd('.');
            else
                root.Attach(bs.Name, node);
        }
        if (feed >= 0)
            mine[feed] = true;
        if (ammo >= 0)
            mine[ammo] = true;
        if (optic >= 0)
            mine[optic] = true;
        for (var k = 0; k < c.PartCount; k++)
            if (mine[k])
                taken[k] = true;
        if (problem.Length == 0 && barrel < 0)
            problem = "no barrel on the receiver";
        if (problem.Length == 0 && feed < 0)
            problem = "no feed (a magazine or a belt box) on the receiver";
        var res = problem.Length == 0 ? BuildStats.ReceiverStats(d.Name, root) : null;
        if (res != null && res.Barrels.Count == 0)
            problem = "no barrel on the receiver";
        // Where it points: the receiver's -Z; its muzzle: the front of the furthest barrel or muzzle device.
        ref var rp = ref c.Parts[i];
        g.Forward = Rot24.Apply(rp.Rot, -Vector3.UnitZ);
        g.Muzzle = Front(c, i, g.Forward);
        var best = Vector3.Dot(g.Muzzle, g.Forward);
        for (var k = 0; k < muzzles; k++)
        {
            var front = Front(c, muzzleParts[k], g.Forward);
            if (Vector3.Dot(front, g.Forward) > best)
            {
                best = Vector3.Dot(front, g.Forward);
                g.Muzzle = front;
            }
        }
        if (problem.Length > 0 || res == null)
        {
            g.Problem = problem.Length > 0 ? problem : "no barrel on the receiver";
            return;
        }
        var w = res.Def;
        g.Shots = 0;
        foreach (var b in res.Barrels)
            g.Shots += b.Pellets;
        var flags = GunEffect.None;
        foreach (var n in root.Walk())
            flags |= EffectsOf(n.Part.Flags);
        g.Effects = flags;
        var sound = Weapons.Find(rec.Receiver!.SoundOf) ?? Weapons.DefaultPrimary;
        CopySound(w, sound);
        g.Spread = w.HipSpreadDeg * 0.5f * MathF.PI / 180f * (optic >= 0 ? OpticSpread : 1f);
        g.MaxRange = d.GunMaxRange;
        g.Kick = d.GunKick;
        g.Fx = d.GunFx;
        g.Speed = 0;
        if (merged)
            ConstructMerge.ScaleGun(ref g, w, c.Merges[c.MergeOf[i] - 1].N); // (N squared receivers' worth: ReceiverMerge.json)
        g.Def = w;
    }

    private static void MuzzleOn(Construct c, int barrel, BuildNode node, ref string problem, Span<bool> taken, Span<bool> mine, Span<int> muzzleParts, ref int muzzles)
    {
        for (var k = 0; k < c.LinkCount; k++)
        {
            var o = GunNeighbour(c, barrel, k);
            if (o < 0 || taken[o] || mine[o] || !c.Parts[o].Working || c.Parts[o].D.Role != PartRole.GunPart || Parts.Find(c.Parts[o].D.GunPartName) is not { Category: PartCategory.Muzzle } mp)
                continue;
            var s = Array.Find(node.Part.Sockets, x => x.Type == "muzzle");
            if (s == null || node.Child(s.Name) != null)
            {
                if (problem.Length == 0)
                    problem = s == null ? $"the {node.Part.Name} takes no muzzle device" : "two muzzle devices on one barrel";
                continue;
            }
            if (BuildRules.SocketWhy(node, s, mp) is { } why)
            {
                if (problem.Length == 0)
                    problem = why.TrimEnd('.');
                continue;
            }
            node.Attach(s.Name, mp);
            mine[o] = true;
            if (muzzles < muzzleParts.Length)
                muzzleParts[muzzles++] = o;
        }
    }

    /// <summary>A heavy construct gun: its own numbers, and the ammo gun parts bolted to it (the weapon builder's universal ammo: the round's numbers and flags on top).</summary>
    private static void BuildHeavy(Construct c, int i, ref CGun g, Span<bool> taken, Span<bool> mine)
    {
        var d = c.Parts[i].D;
        mine[i] = true;
        var w = new WeaponDef
        {
            Name = d.Name, Class = WeaponClass.LMG, Damage = (int)MathF.Round(d.GunDamage), RangeM = d.GunRange, Rpm = (int)MathF.Round(d.GunRpm), Mag = d.GunMag,
            Reload = d.GunReload, HipSpreadDeg = d.GunSpread, Pellets = d.GunPellets,
        };
        var flags = EffectsOf(d.GunFlags);
        for (var k = 0; k < c.LinkCount; k++)
        {
            var o = GunNeighbour(c, i, k);
            if (o < 0 || taken[o] || !c.Parts[o].Working || c.Parts[o].D.Role != PartRole.GunPart || Parts.Find(c.Parts[o].D.GunPartName) is not { Category: PartCategory.Ammo } ap)
                continue;
            BuildStats.ApplyAmmo(ap, w, BoreClass.Default);
            flags |= EffectsOf(ap.Flags);
            mine[o] = taken[o] = true;
            break; // (one ammo part a gun)
        }
        taken[i] = true;
        CopySound(w, (d.GunSound.Length > 0 ? Weapons.Find(d.GunSound) : null) ?? Weapons.DefaultPrimary);
        ref var p = ref c.Parts[i];
        g.Forward = Rot24.Apply(p.Rot, -Vector3.UnitZ);
        g.Muzzle = p.Pos + Rot24.Apply(p.Rot, d.GunMuzzle);
        g.Shots = Math.Max(1, w.Pellets);
        g.Effects = flags;
        g.Spread = w.HipSpreadDeg * 0.5f * MathF.PI / 180f;
        g.MaxRange = d.GunMaxRange;
        g.Kick = d.GunKick;
        g.Fx = d.GunFx;
        g.Speed = d.GunSpeed;
        g.Blast = d.GunBlast;
        g.BlastDamage = d.GunBlastDamage;
        g.Def = w;
    }

    private static void Finish(Construct c, ref CGun g, WeaponDef w)
    {
        if ((g.Effects & GunEffect.Explosive) != 0 && g.Blast <= 0)
        {
            g.Blast = 2.5f; // an explosive round: a small splash
            g.BlastDamage = w.Damage * 1.2f;
        }
        var rpm = Math.Clamp(w.Rpm, 1, 3600);
        g.Interval = (ushort)Math.Max(1, (int)MathF.Round(World.TickRate * 60f / rpm));
        g.ReloadTicks = (ushort)Math.Clamp((int)MathF.Round(w.Reload * World.TickRate), 1, 60000);
        g.Mag = (ushort)Math.Clamp(w.Mag, 1, 60000);
        g.Shots = Math.Clamp(g.Shots, 1, 24);
        _ = c;
    }

    private static void CopySound(WeaponDef w, WeaponDef sound)
    {
        w.Id = sound.Id;
        w.Sound = sound.Sound;
        w.Caliber = sound.Caliber;
        w.Bright = sound.Bright;
        w.Tail = sound.Tail;
    }

    private static string Plural(PartCategory c) => c switch { PartCategory.Ammo => "ammo parts", PartCategory.Barrel or PartCategory.Splitter => "barrels", _ => c.ToString().ToLowerInvariant() + "s" };

    public static GunEffect EffectsOf(string[] flags)
    {
        var e = GunEffect.None;
        foreach (var f in flags)
            e |= f switch { "penetrating" => GunEffect.Penetrating, "explosive" => GunEffect.Explosive, "incendiary" => GunEffect.Incendiary, _ => GunEffect.None };
        return e;
    }

    /// <summary>The centre of part <paramref name="i"/>'s face towards <paramref name="fwd"/> (rest frame).</summary>
    private static Vector3 Front(Construct c, int i, Vector3 fwd)
    {
        c.PartBounds(i, out var mn, out var mx);
        var centre = (mn + mx) * 0.5f;
        var half = (mx - mn) * 0.5f;
        return centre + fwd * Vector3.Dot(Vector3.Abs(fwd), half);
    }

    /// <summary>The ammo boxes gun <paramref name="k"/>'s feed reaches: boxes bolted to the gun (or any of its parts), or at the end of a chain of feed chutes; the nearest first.</summary>
    private static void Feed(Construct c, int k, Span<bool> mine)
    {
        ref var g = ref c.Guns[k];
        g.BoxCount = 0;
        Span<bool> seen = stackalloc bool[Construct.MaxParts];
        Span<byte> queue = stackalloc byte[Construct.MaxParts];
        int head = 0, tail = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (mine[i])
            {
                seen[i] = true;
                queue[tail++] = (byte)i;
            }
        while (head < tail && g.BoxCount < Construct.BoxesPerGun)
        {
            int at = queue[head++];
            for (var l = 0; l < c.LinkCount && g.BoxCount < Construct.BoxesPerGun; l++)
            {
                var o = GunNeighbour(c, at, l);
                if (o < 0 || seen[o] || !c.Parts[o].Working)
                    continue;
                seen[o] = true;
                var role = c.Parts[o].D.Role;
                if (role == PartRole.AmmoBox)
                    c.GunBoxes[k * Construct.BoxesPerGun + g.BoxCount++] = (byte)o;
                else if (role == PartRole.FeedChute)
                    queue[tail++] = (byte)o;
            }
        }
    }

    /// <summary>The gunner's seat (a seat on the gun's segment or one up its chain that is a gunner's), a sentry's sensor, and the joints they point.</summary>
    private static void Controls(Construct c, ref CGun g)
    {
        for (int s = g.Seg, guard = 0; guard <= Construct.MaxJoints && g.Seat < 0; guard++)
        {
            for (var i = 0; i < c.PartCount; i++)
                if (c.Parts[i].Seg == s && c.Parts[i].D.Role == PartRole.Seat && c.SeatKindOf(i) == SeatKind.Gunner)
                {
                    g.Seat = (sbyte)i;
                    break;
                }
            if (s <= 0 || s > c.JointCount)
                break;
            s = c.Joints[s - 1].ParentSeg;
        }
        for (var w = 0; w < c.WireCount; w++)
            if (c.Wires[w].To == g.Part && c.Wires[w].From < c.PartCount && c.Parts[c.Wires[w].From].D.Logic == CLogic.Proximity)
            {
                g.Sensor = c.Wires[w].From;
                break;
            }
        g.AimJoints = 0;
        for (int s = g.Seg, guard = 0; s > 0 && s <= c.JointCount && guard <= Construct.MaxJoints; guard++)
        {
            ref var jt = ref c.Joints[s - 1];
            if (jt.Type is JointType.Turntable or JointType.Motor)
                g.AimJoints++;
            s = jt.ParentSeg;
        }
    }

    /// <summary>Rounds left in the boxes gun <paramref name="k"/>'s feed reaches.</summary>
    public static int BoxRounds(Construct c, int k)
    {
        ref var g = ref c.Guns[k];
        var n = 0;
        for (var b = 0; b < g.BoxCount; b++)
        {
            int box = c.GunBoxes[k * Construct.BoxesPerGun + b];
            if (box < c.PartCount && c.Parts[box].Working)
                n += c.Parts[box].Rounds;
        }
        return n;
    }

    /// <summary>Where gun <paramref name="k"/>'s muzzle is and which way it points, in the world now.</summary>
    public static void MuzzleWorld(Construct c, int k, out Vector3 muzzle, out Vector3 fwd)
    {
        ref var g = ref c.Guns[k];
        c.WorldFrame(g.Seg, out var q, out var t);
        muzzle = t + Vector3.Transform(g.Muzzle, q);
        fwd = Vector3.Normalize(Vector3.Transform(g.Forward, q));
    }

    // ---- The step (server) ----------------------------------------------------------------------------------------

    /// <summary>One tick of every gun, burning part and cook-off (server, before the logic: wires carry last tick's values).</summary>
    public static void Step(ConstructWorld world, IWorldQuery query, float dt)
    {
        world.FireCount = 0;
        var bodies = world.Bodies;
        for (var bi = 0; bi < bodies.Count; bi++)
        {
            var c = bodies[bi];
            if (c.Dirty)
                c.Rebuild();
            if (c.GunCount > 0)
                StepGuns(world, c, query);
            if (bi < bodies.Count && bodies[bi] == c && c.Burning > 0)
                StepBurn(world, c, dt);
        }
        StepCookOffs(world);
    }

    private static void StepGuns(ConstructWorld world, Construct c, IWorldQuery query)
    {
        for (var j = 0; j < c.JointCount; j++)
            c.Joints[j].Aimed = false;
        for (var k = 0; k < c.GunCount; k++)
        {
            ref var g = ref c.Guns[k];
            if (g.Part >= c.PartCount)
                continue;
            ref var p = ref c.Parts[g.Part];
            if (p.Broken || g.Def == null)
                continue;
            if (p.Cool > 0)
                p.Cool--;
            if (p.Reload > 0 && --p.Reload == 0)
                Load(world, c, k);
            var seated = g.Seat >= 0 && g.Seat < c.PartCount && (c.Parts[g.Seat].Keys & CPart.Occupied) != 0 && c.Parts[g.Seat].Working;
            var wiredIn = p.InCount;
            var signal = p.In;
            if (g.MergeN > 1)
                for (var m = 0; m < c.PartCount; m++)
                    if (m != g.Part && c.MergeOf[m] == c.MergeOf[g.Part])
                    {
                        wiredIn += c.Parts[m].InCount; // (a merged receiver: a wire into any of its members is its trigger)
                        signal += c.Parts[m].In;
                    }
            var trigger = wiredIn > 0 ? signal >= 0.5f : seated && (c.Parts[g.Seat].Keys & ConstructParts.KeyFire) != 0;
            if (seated && g.AimJoints > 0)
            {
                // At what the gunner looks at (the guns converge on it: no parallax from the gun sitting off the eye).
                MuzzleWorld(c, k, out var muzzle, out _);
                Aim(c, k, GunnerPoint(world, c, g.Seat, query, g.MaxRange) - muzzle, force: true);
            }
            else if (g.Sensor != 255 && g.AimJoints > 0 && Nearest(world, c, g.Sensor) is { } target)
            {
                MuzzleWorld(c, k, out var muzzle, out _);
                var off = Aim(c, k, target + new Vector3(0, SentryAimHeight, 0) - muzzle, force: false);
                if (off > SentryHoldFire)
                    trigger = false; // (a sentry turns onto its target first)
            }
            var per = Math.Max((ushort)1, g.PerShot);
            if (p.Rounds < per && p.Reload == 0 && BoxRounds(c, k) > 0)
            {
                p.Reload = g.ReloadTicks;
                world.NoteAmmo(c, g.Part);
            }
            if (!trigger || p.Cool > 0 || p.Rounds < per || p.Reload > 0)
                continue;
            p.Rounds -= per;
            p.Cool = g.Interval;
            world.NoteAmmo(c, g.Part);
            c.Wake();
            Fire(world, c, k, query);
            if (c.Dirty || !world.Bodies.Contains(c))
                return; // (its own parts were hit: the rest fire next tick)
        }
    }

    /// <summary>Fills gun <paramref name="k"/> from its boxes, nearest first, up to a load.</summary>
    private static void Load(ConstructWorld world, Construct c, int k)
    {
        ref var g = ref c.Guns[k];
        ref var p = ref c.Parts[g.Part];
        var want = g.Mag - p.Rounds;
        for (var b = 0; b < g.BoxCount && want > 0; b++)
        {
            int box = c.GunBoxes[k * Construct.BoxesPerGun + b];
            if (box >= c.PartCount || !c.Parts[box].Working)
                continue;
            var take = Math.Min(want, c.Parts[box].Rounds);
            c.Parts[box].Rounds -= (ushort)take;
            p.Rounds += (ushort)take;
            want -= take;
            world.NoteAmmo(c, box);
        }
        world.NoteAmmo(c, g.Part);
    }

    /// <summary>The gunner's eye in seat <paramref name="seat"/> (the seat's feet point, up its up by the eye height).</summary>
    public static Vector3 SeatEye(Construct c, int seat)
    {
        c.WorldFrame(c.Parts[seat].Seg, out var q, out _);
        var up = Vector3.Transform(Vector3.UnitY, q * Rot24.Quat(c.Parts[seat].Rot));
        return ConstructEdits.SeatFeet(c, seat) + up * World.EyeHeight;
    }

    /// <summary>What the gunner in <paramref name="seat"/> looks at: the map or another construct along their look, else as far as the gun reaches.</summary>
    public static Vector3 GunnerPoint(ConstructWorld world, Construct c, int seat, IWorldQuery query, float reach)
    {
        var eye = SeatEye(c, seat);
        var look = World.Forward(c.Parts[seat].AimYaw, c.Parts[seat].AimPitch);
        var d = query.Raycast(eye, look, reach, QueryFilter.Shots);
        if (world.Raycast(eye, look, d, out var hit, c) && hit.Distance < d)
            d = hit.Distance;
        if (world.Targets != null && world.Targets.RayTarget(eye, look, d, c.Id, out var td, out _) && td < d)
            d = td;
        return eye + look * MathF.Max(d, 2f);
    }

    /// <summary>The nearest living player within the range of sensor part <paramref name="sensor"/> (its feet), or null.</summary>
    private static Vector3? Nearest(ConstructWorld world, Construct c, int sensor)
    {
        if (sensor >= c.PartCount)
            return null;
        var d = c.Parts[sensor].D;
        var range = d.Settings.Length > 0 ? d.Settings[Math.Min(c.Parts[sensor].Setting, (byte)(d.Settings.Length - 1))] : 6f;
        var at = ConstructRender.PartCentre(c, sensor);
        Vector3? best = null;
        var bestD = range * range;
        for (var k = 0; k < world.PlayerSpotCount; k++)
        {
            var d2 = Vector3.DistanceSquared(world.PlayerSpots[k], at);
            if (d2 <= bestD)
            {
                bestD = d2;
                best = world.PlayerSpots[k];
            }
        }
        return best;
    }

    /// <summary>
    /// Points gun <paramref name="k"/> along <paramref name="want"/> (world): each turntable and motor hinge up its chain
    /// turns towards it about its own axis (a turntable for the bearing, a hinge for the elevation). Unless
    /// <paramref name="force"/> (a gunner), a wired joint keeps to its wires. Returns the largest error left (radians).
    /// </summary>
    public static float Aim(Construct c, int k, Vector3 want, bool force)
    {
        ref var g = ref c.Guns[k];
        if (want.LengthSquared() < 1e-6f)
            return 0;
        want = Vector3.Normalize(want);
        MuzzleWorld(c, k, out _, out var f);
        var worst = 0f;
        for (int s = g.Seg, guard = 0; s > 0 && s <= c.JointCount && guard <= Construct.MaxJoints; guard++)
        {
            ref var jt = ref c.Joints[s - 1];
            s = jt.ParentSeg;
            if (jt.Type is not (JointType.Turntable or JointType.Motor) || (!force && jt.Wired))
                continue;
            c.WorldFrame(jt.ParentSeg, out var pq, out _);
            var a = Vector3.Transform(jt.Axis, pq);
            float err;
            if (MathF.Abs(a.Y) < 0.7f)
            {
                // A joint across the gun (a hinge for the elevation): the climb to the target, never over the top to point behind.
                var h = Vector3.Cross(a, Vector3.UnitY);
                if (h.LengthSquared() < 1e-6f)
                    continue;
                h = Vector3.Normalize(h);
                if (Vector3.Dot(f, h) < 0)
                    h = -h;
                var u = Vector3.Cross(a, h);
                err = MathF.Atan2(Vector3.Dot(want, u), MathF.Abs(Vector3.Dot(want, h))) - MathF.Atan2(Vector3.Dot(f, u), Vector3.Dot(f, h));
            }
            else
            {
                // Upright (a turntable for the bearing): all the way round, the short way.
                var fp = f - a * Vector3.Dot(f, a);
                var dp = want - a * Vector3.Dot(want, a);
                if (fp.LengthSquared() < 1e-4f || dp.LengthSquared() < 1e-4f)
                    continue;
                err = MathF.Atan2(Vector3.Dot(a, Vector3.Cross(fp, dp)), Vector3.Dot(fp, dp));
            }
            if (!jt.Aimed || MathF.Abs(err) > MathF.Abs(jt.AimErr))
                jt.AimErr = err;
            jt.Aimed = true;
            worst = MathF.Max(worst, MathF.Abs(err));
        }
        return worst;
    }

    private static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7feb352d;
        x ^= x >> 15;
        x *= 0x846ca68b;
        x ^= x >> 16;
        return x;
    }

    /// <summary>A direction inside the cone of half angle <paramref name="spread"/> about <paramref name="fwd"/> (seeded).</summary>
    private static Vector3 Scatter(Vector3 fwd, float spread, uint seed)
    {
        if (spread <= 0)
            return fwd;
        var right = MathF.Abs(fwd.Y) < 0.95f ? Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY)) : Vector3.UnitX;
        var up = Vector3.Cross(right, fwd);
        var a = Hash(seed) / (float)uint.MaxValue * MathF.Tau;
        var r = MathF.Sqrt(Hash(seed ^ 0x9e3779b9u) / (float)uint.MaxValue) * MathF.Tan(spread);
        return Vector3.Normalize(fwd + (right * MathF.Cos(a) + up * MathF.Sin(a)) * r);
    }

    /// <summary>Gun <paramref name="k"/> fires one shot (every barrel's pellets): each round resolved at once, a fire event each.</summary>
    private static void Fire(ConstructWorld world, Construct c, int k, IWorldQuery query)
    {
        var g = c.Guns[k]; // (a copy: a hit can rebuild this construct)
        MuzzleWorld(c, k, out var muzzle, out var fwd);
        var seq = ++_seq;
        var damage = (float)g.Def!.Damage;
        for (var s = 0; s < g.Shots; s++)
        {
            var dir = Scatter(fwd, g.Spread, (uint)(c.Id * 73856093) ^ (uint)(g.Part * 19349663) ^ (seq * 83492791u) ^ (uint)(s * 2654435761u));
            var hit = Round(world, c, g, query, muzzle, dir, damage, out var end);
            world.AddFire(new CFireEvent { Kind = CFireKind.Shot, Body = c.Id, Part = g.Part, At = end, Hit = hit });
        }
        world.ShotsFired++;
        if (g.Kick > 0 && !c.Anchored && world.Bodies.Contains(c))
        {
            c.Vel -= fwd * (g.Kick / MathF.Max(c.Mass, 20f));
            c.Wake();
        }
    }

    /// <summary>
    /// One round from <paramref name="from"/> along <paramref name="dir"/>: the nearest of the map, a construct part (not its
    /// own construct's) and a target. A part takes the damage (its armour first); a penetrating round goes on through it
    /// (up to <see cref="MaxPierce"/> parts, weaker after each); an incendiary one sets a flammable part alight; an
    /// explosive one splashes where it stops.
    /// </summary>
    private static CFireHit Round(ConstructWorld world, Construct own, in CGun g, IWorldQuery query, Vector3 from, Vector3 dir, float damage, out Vector3 end)
    {
        var hit = CFireHit.None;
        var pos = from;
        var travelled = 0f;
        var left = damage;
        end = from + dir * g.MaxRange;
        if ((g.Effects & GunEffect.Incendiary) != 0)
            hit |= CFireHit.Burns;
        for (var pierce = 0; pierce <= MaxPierce; pierce++)
        {
            var range = g.MaxRange - travelled;
            if (range <= 0.01f)
            {
                end = pos;
                break;
            }
            var best = query.Raycast(pos, dir, range, QueryFilter.Shots);
            var what = best < range - 1e-3f ? 1 : 0; // 1: the map
            ConstructHit ch = default;
            if (world.Raycast(pos, dir, best, out var cHit, own) && cHit.Distance < best)
            {
                best = cHit.Distance;
                ch = cHit;
                what = 2;
            }
            var tid = -1;
            if (world.Targets != null && world.Targets.RayTarget(pos, dir, best, own.Id, out var td, out var id) && td < best)
            {
                best = td;
                tid = id;
                what = 3;
            }
            var at = pos + dir * best;
            travelled += best;
            end = at;
            var dealt = left * g.Def!.DamageMultAt(travelled);
            if (what == 3)
            {
                world.Targets!.HitTarget(tid, dealt, g.Effects, own, g.Part, at, dir);
                hit |= CFireHit.Target;
                break;
            }
            if (what != 2)
            {
                if (what == 1)
                    hit |= CFireHit.World;
                break;
            }
            // A construct part.
            var body = ch.Body;
            var part = ch.Part;
            if (ch.SlotPlus1 > 0)
            {
                world.DamageStored(body, part, ch.SlotPlus1 - 1, dealt); // (a part stored in a container: physical storage)
                hit |= CFireHit.Part;
                break;
            }
            var armour = body.Parts[part].D.Armour;
            var exit = ExitDistance(body, part, at, dir);
            if (ch.Glass)
            {
                // A glass pane: the round breaks it (or chips it) and goes on, as strong as it was.
                world.DamagePart(body, part, dealt, out _);
                hit |= CFireHit.Part | CFireHit.Through;
                pos = at + dir * (exit + 0.01f);
                travelled += exit + 0.01f;
                continue;
            }
            if ((g.Effects & GunEffect.Incendiary) != 0)
                world.Ignite(body, part);
            world.DamagePart(body, part, dealt, out _);
            hit |= CFireHit.Part;
            if ((g.Effects & GunEffect.Penetrating) == 0 || pierce == MaxPierce)
                break;
            left = left * 0.75f - armour * 1.5f; // through it, weaker (thicker armour takes more off)
            if (left < 2f)
                break;
            hit |= CFireHit.Through;
            pos = at + dir * (exit + 0.01f);
            travelled += exit + 0.01f;
        }
        if ((g.Effects & GunEffect.Explosive) != 0 && g.Blast > 0 && (hit & (CFireHit.World | CFireHit.Part | CFireHit.Target)) != 0)
            Explode(world, end - dir * 0.08f, g.Blast, g.BlastDamage, own, CFireKind.Blast);
        return hit;
    }

    /// <summary>How far a ray entering part <paramref name="part"/> at <paramref name="at"/> travels inside it.</summary>
    public static float ExitDistance(Construct c, int part, Vector3 at, Vector3 dir)
    {
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        var iq = Quaternion.Conjugate(q);
        var lo = Vector3.Transform(at - t, iq);
        var ld = Vector3.Transform(dir, iq);
        c.PartBounds(part, out var mn, out var mx);
        var exit = 4f;
        for (var a = 0; a < 3; a++)
        {
            if (MathF.Abs(ld[a]) < 1e-6f)
                continue;
            var tx = ((ld[a] > 0 ? mx[a] : mn[a]) - lo[a]) / ld[a];
            exit = MathF.Min(exit, MathF.Max(0, tx));
        }
        return exit;
    }

    /// <summary>A blast: every construct part in range (ConstructWorld.Blast), every target in range, and a fire event.</summary>
    public static void Explode(ConstructWorld world, Vector3 at, float radius, float damage, Construct? by, CFireKind kind)
    {
        world.AddFire(new CFireEvent { Kind = kind, Body = by?.Id ?? 0, At = at, Radius = radius });
        world.Targets?.BlastTargets(at, radius, damage, by);
        world.Blast(at, radius, damage);
    }

    /// <summary>Burning parts burn down (the armour doesn't help), set flammable parts bolted to them alight half-way, and go out.</summary>
    private static void StepBurn(ConstructWorld world, Construct c, float dt)
    {
        var count = c.PartCount;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            if (p.Burn == 0)
                continue;
            p.Burn--;
            var d = p.D;
            if (p.Burn == (ushort)(BurnSeconds * World.TickRate / 2))
                for (var l = 0; l < c.LinkCount; l++)
                {
                    var o = GunNeighbour(c, i, l);
                    if (o >= 0 && c.Parts[o].Burn == 0 && c.Parts[o].D.Flammable && !c.Parts[o].Broken)
                        world.Ignite(c, o);
                }
            if (p.Burn == 0)
            {
                c.Burning = Math.Max(0, c.Burning - 1);
                world.NoteAmmo(c, i);
            }
            if (p.Broken)
                continue;
            world.DamagePart(c, i, (d.Health * 0.1f + 2f) * dt, out _, raw: true);
            if (c.PartCount != count || c.Dirty)
                return; // (it burnt through: the rest go on next tick)
        }
    }

    private static void StepCookOffs(ConstructWorld world)
    {
        for (var k = 0; k < world.CookOffCount; k++)
        {
            ref var co = ref world.CookOffs[k];
            if (--co.Ticks > 0)
                continue;
            var at = co;
            world.CookOffs[k] = world.CookOffs[--world.CookOffCount];
            k--;
            Explode(world, at.At, at.Radius, at.Damage, world.Get(at.Body), CFireKind.CookOff);
        }
    }
}
