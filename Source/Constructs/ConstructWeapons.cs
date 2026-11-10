using System.Numerics;

namespace CallOfSlop;

/// <summary>One gun of a construct, worked out from its parts when the construct changes (<see cref="ConstructWeapons.BuildGuns"/>).</summary>
public struct CGun
{
    public ushort Part;
    public byte Seg; // the gun part (a receiver or a heavy gun) and its segment
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
    public short Seat; // the gunner's seat that fires and aims it (when nothing is wired into it), or -1
    public const ushort NoSensor = ushort.MaxValue;
    public ushort Sensor; // a proximity sensor wired into it: a sentry, pointing at the nearest player in the sensor's range (255: none)
    public byte AimJoints; // turntables and motor hinges up its chain that a gunner or a sensor can point
    public ushort PerShot; // rounds a shot takes from its load (a merged receiver's: N squared; 0 or 1: one)
    public byte MuzzleCount; // a splitter gun's barrel tips (Construct.GunMuzzles; 0: every round leaves Muzzle)
    public byte MergeN; // a merged receiver's N (0: a single receiver or a heavy gun; ConstructMerge.cs)
    public ShellKind Shell; // a shell gun's shell (GunEffect.Shell; ConstructShells.cs)
    public float Crater; // and the crater it digs (m)
    public short FeedRoute; // the ammo feed into it (-1: none; ConstructAmmo.cs)
    public short FeedSocket; // an ammo reel's socket bolted to it (-1: none)
    public short MagPart; // the magazine on its receiver (-1: none): rounds a player brought (ConstructAmmo.cs)
    public float RoundV; // what its rounds leave at (m/s; 0: they fly straight: flames, rockets)
    public short Director; // the fire director wired into it (-1: none): it aims where the director's gunner looks
    public short Turret; // the Turret Module wired into it (-1: none): its seat (Seat) aims and fires it (ConstructModules.cs)
    public SightKind Sight; // a sight bolted to it (ConstructOrdnance.cs)
    public float SightReach, SightCos, FuseRange; // how far it sees, the cosine of its cone, its fuse setting (m)
    public int FuseTicks; // its next shell's age when it bursts (0: only on what it meets)
    public ushort Track; // the body its gunner has in view (0: none; rebuilt with the guns, kept on each PC for itself)
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
    LinkSnap, // a world link tore off a moving construct (protocol 95): where, and its type in Part (a pipe spills, a cable sparks)
    ConnectorSnap, // a rope or spring went (protocol 134): its ends (At, To), its kind and size in Part (kind | size << 4)
    ShellOut, // a shell left a gun (protocol 150): body, part, its id, where (At) and its velocity (To), its kind in Muzzle
    ShellLand, // a shell went off: its id, where, the blast's radius, its kind in Muzzle
    RoundOut, // a round passed its reach and flies on: where, and how fast (ConstructRounds.cs)
    ShellStop, // a bomb hit and waits for its fuse: its id, where (protocol 178)
    ShellSteer, // a missile went for a flare: its id, the flare's in Target, where (At) and how fast (To)
}

/// <summary>One thing the guns did this tick (the server sends them as Msg.CFire; the host draws its own straight away).</summary>
public struct CFireEvent
{
    public CFireKind Kind;
    public ushort Body;
    public ushort Part;
    public byte Muzzle; // a shot: which of the gun's muzzles it left (protocol 103)
    public Vector3 At; // a shot's end, a blast's centre
    public CFireHit Hit;
    public float Radius; // a blast's
    public Vector3 Half; // a shatter's: the glass part's world bounds' half size
    public Vector3 To; // a connector snap's other end; a shell's velocity
    public ushort Shell; // a shell's id
    public float Thrust, Turn; // a rocket's or missile's, from their gun
    public ushort Burn, Target; // ticks it burns, the body it steers for (ShellSteer: the flare)
    public byte Ext; // ShellOut: 1, a designed weapon's flight follows; ShellSteer: 1 an aim point, 2 a new stage, 4 a guide's update (not a flare)
    public ShellFlags Flags;
    public float AirPush, TurnAir, TurnBurn, Drag, Lift, MinSpeed;
    public ushort Wait, Deploy;
    public int Life;
    public Vector3 Aim;
    public ShellLookKind LookKind; // ShellOut (protocol 184): the model it is drawn with in the air (ShellLooks.cs)
    public uint Look; // ...a part file's id, or a designed weapon's key hashed
    public Quaternion Rot; // ...how it hung on its rack (its part's world turn)
}

/// <summary>A blast waiting to go off (an ammo box cooking off: a beat after it is holed).</summary>
public struct CCookOff
{
    public Vector3 At;
    public float Radius, Damage;
    public int Ticks;
    public ushort Body; // where it came from (credit)
    public PropBreakable? Bomb; // an explosive part going off (its own blast, ConstructWorld.Arm), else a cook-off
    public byte Look; // ...a designed weapon's blast look (MegaBlasts), for a massive one
}

/// <summary>Who and what a construct gun can hit besides construct parts and the map (the game: players, bots, zombies).</summary>
public interface IConstructTargets
{
    /// <summary>The nearest target along a ray (not riding <paramref name="ignoreBody"/>), before <paramref name="maxDist"/>.</summary>
    bool RayTarget(Vector3 o, Vector3 d, float maxDist, ushort ignoreBody, out float dist, out int id);

    /// <summary>A round hit target <paramref name="id"/> (from <see cref="RayTarget"/>) for <paramref name="damage"/>.</summary>
    void HitTarget(int id, float damage, GunEffect effects, Construct by, int gunPart, Vector3 at, Vector3 dir);

    /// <summary>A flying round (ConstructRounds.cs) came down on target <paramref name="id"/> with <paramref name="damage"/> left.</summary>
    void RoundHit(int id, float damage, byte by, byte weapon, Vector3 at, Vector3 dir);

    /// <summary>A blast hurts every target in range (walls shield).</summary>
    void BlastTargets(Vector3 at, float radius, float damage, Construct? by);

    /// <summary>An explosive part (a bomb) goes off at <paramref name="at"/>: the game's explosive-prop blast (players, props, buildings, constructs, a crater).</summary>
    void ExplodePart(Vector3 at, PropBreakable blast, Construct? by);

    /// <summary>Fire reached the map at the end of a ray: breakable and explosive props along it take the heat, as from a hand gun.</summary>
    void FlameWorld(Vector3 from, Vector3 dir, float dist, float damage, Construct by, int gunPart);

    /// <summary>A round of <paramref name="damage"/> hit the ground at <paramref name="at"/>: it digs, if the rate limit lets it.</summary>
    void RoundDig(Vector3 at, float damage, Construct by, int gunPart);

    /// <summary>A shell went off: the players and zombies in reach and the crater, at once (the parts are walked by <see cref="ConstructWorld.StepShells"/>).</summary>
    void ShellBlast(Vector3 at, float radius, float damage, float crater, bool burns, Construct? by, int gunPart);

    /// <summary>A massive blast's shock front went from <paramref name="from"/> to <paramref name="to"/> m this tick (from -1 on its first): the players and zombies it passed (ConstructMegaBlast.cs).</summary>
    void MegaFront(Vector3 at, float radius, float damage, float from, float to, Construct? by, int gunPart);

    /// <summary>A massive blast's crater of <paramref name="crater"/> m: dug in bands over ticks (Game.MegaDigTick).</summary>
    void MegaGround(Vector3 at, float crater);

    /// <summary>A massive blast began at <paramref name="at"/> (its radius as capped, its look, the player byte it is credited to): the story hears of an antimatter one.</summary>
    void MegaStarted(Vector3 at, float radius, byte look, byte by) { }
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
    public const int ArcPieces = 6; // straight pieces of a falling round's path, at most
    public const float ArcPieceMin = 40f; // ...each this long at least: a short range is one or two
    public const float OpticSpread = 0.6f; // an optic steadies a gun: this share of its spread
    public const float SentryHoldFire = 0.14f; // a sentry fires once it points within this (radians) of its target
    public const float SentryAimHeight = 1.1f; // a sentry aims at a player's chest (this above the feet)
    public const float DirectorReach = 1000f; // how far a fire director's gunner's look reaches for what the guns aim at

    private static uint _seq;

    // ---- The numbers (rebuilt with the construct) --------------------------------------------------------------

    /// <summary>Every gun of a construct: its numbers, its feed, the seat and joints that work it (allocates: only when the construct changes).</summary>
    public static void BuildGuns(Construct c, ConstructWorld? world = null)
    {
        var again = false;
        BuildGunsOnce(c, world, ref again);
        if (again)
        {
            again = false;
            BuildGunsOnce(c, world, ref again);
        }
    }

    private static void BuildGunsOnce(Construct c, ConstructWorld? world, ref bool again)
    {
        c.GunCount = 0;
        c.Burning = 0;
        for (var j = 0; j < c.JointCount; j++)
            c.Joints[j].Aimed = false;
        using var scratch = Scratch.Begin();
        Span<bool> taken = scratch.Take<bool>(c.PartCount);
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Burn > 0)
                c.Burning++; // (any part can burn)
        foreach (var i in c.Specials) // (guns are special parts)
        {
            if (c.Parts[i].D.Role != PartRole.Gun)
                continue;
            if (c.GunCount >= Construct.MaxGuns)
            {
                Caps.Hit("guns a construct", Construct.MaxGuns); // (past this a gun is a plain part)
                continue;
            }
            if (c.MergeOf[i] != 0 && !c.MergeLead(i))
                continue; // (a merged receiver's other members: its lead is the gun)
            c.ReserveGuns(c.GunCount + 1);
            ref var g = ref c.Guns[c.GunCount];
            g = new CGun { Part = (ushort)i, Seg = c.Parts[i].Seg, Problem = "", Seat = -1, Sensor = CGun.NoSensor, Director = -1, Turret = -1, FeedRoute = -1, FeedSocket = -1, MagPart = -1 };
            using var gunScratch = Scratch.Begin(); // (a frame a gun: given back each turn of the loop, as the old stackalloc never was)
            Span<bool> mine = gunScratch.Take<bool>(c.PartCount);
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
            if (c.Parts[i].AmmoType == 0 && g.Def != null && ConstructAmmo.TypeOfGun(world, c, c.GunCount) is var adopt and > 0)
            {
                c.Parts[i].AmmoType = (byte)(adopt + 1); // (a gun loaded from a typed store before anything said so: a loaded world, a fresh gun)
                again = true;
            }
            Controls(c, ref g);
            c.GunCount++;
        }
        if (c.GunCount > 0)
            c.Recoils ??= new CRecoil[Construct.MaxRecoils]; // (gr01: made with the guns, never at a shot)
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
        mine[i] = true;
        var merged = c.MergeLead(i);
        var ownSockets = d.Sockets.Length;
        if (merged)
            for (var k = 0; k < c.PartCount; k++)
                if (c.MergeOf[k] == c.MergeOf[i])
                    mine[k] = taken[k] = true; // (its members: boxes bolted to any of them feed it)
        int barrel = -1, feed = -1, ammo = -1, optic = -1, action = -1;
        Span<int> extras = stackalloc int[8]; // other builder parts on the receiver (grips, stocks, upgrades...): any free socket the builder's rules allow
        var extraCount = 0;
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
                case PartCategory.Ammo:
                    mine[o] = true; // (nothing on a mounted gun sets its rounds: an old Ammo part is left alone, and a box on it still feeds: ConstructAmmo.cs)
                    continue;
                case PartCategory.Optic: slot = ref optic; break;
                case PartCategory.Action: slot = ref action; break;
                case PartCategory.Muzzle:
                    if (problem.Length == 0)
                        problem = $"a {c.Parts[o].D.Name} goes on a barrel, not on the receiver";
                    continue;
                default:
                    if (extraCount < extras.Length)
                        extras[extraCount++] = o;
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
        Fit(root, "action", action >= 0 ? Parts.Find(c.Parts[action].D.GunPartName)! : act); // (an action bolted on replaces the stock one)
        if (action >= 0)
            mine[action] = true;
        if (feed >= 0)
            Fit(root, "feed", Parts.Find(c.Parts[feed].D.GunPartName)!);
        Fit(root, "ammo", ConstructAmmo.PartOf(Math.Max(0, c.Parts[i].AmmoType - 1)) ?? Parts.Find("FMJ") ?? throw new InvalidOperationException("no FMJ ammo part"));
        Span<int> muzzleParts = stackalloc int[16];
        var muzzles = 0;
        var tips = new Dictionary<BuildNode, int>(); // a barrel node -> the part its rounds leave from (its muzzle device, else the barrel)
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
                    // A generated splitter has an output socket per builder output ("front-N": the Nth); a hand-made one fills them in turn.
                    var at = SplitterOutput(c, barrel, o, bp);
                    if (at < 0)
                    {
                        while (socket < bp.Sockets.Length && bp.Sockets[socket].Type != "barrel")
                            socket++;
                        at = socket++;
                    }
                    if (at >= bp.Sockets.Length || node.Child(bp.Sockets[at].Name) != null)
                    {
                        if (problem.Length == 0)
                            problem = $"more barrels than the {bp.Name} has outputs";
                        break;
                    }
                    var sock = bp.Sockets[at];
                    if (BuildRules.SocketWhy(node, sock, sb) is { } bad)
                    {
                        if (problem.Length == 0)
                            problem = bad.TrimEnd('.');
                        continue;
                    }
                    var bn = new BuildNode(sb);
                    node.Attach(sock.Name, bn);
                    var before = muzzles;
                    MuzzleOn(c, o, bn, ref problem, taken, mine, muzzleParts, ref muzzles);
                    tips[bn] = muzzles > before ? muzzleParts[muzzles - 1] : o; // (the barrel's own muzzle device, else the barrel)
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
        for (var k = 0; k < extraCount; k++)
        {
            var o = extras[k];
            var xp = Parts.Find(c.Parts[o].D.GunPartName)!;
            // Stocks and foregrips hang off the workbench's grip, not the receiver (wa01: they never fitted a bare receiver, so a
            // gun with one was always "broken"): checked against the plain one-receiver grip every receiver stands on here.
            if ((xp.Category == PartCategory.Stock || xp.Category == PartCategory.Grip) && StandInGrip(rec) is { } stand)
            {
                if (FitAny(stand, xp) is { } gw)
                {
                    if (problem.Length == 0)
                        problem = gw;
                }
                else
                    mine[o] = true;
            }
            else if (FitAny(root, xp) is { } why)
            {
                if (problem.Length == 0)
                    problem = why;
            }
            else
                mine[o] = true;
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
        g.MagPart = (short)feed;
        var direct = feed < 0 && ConstructAmmo.HasFeedLink(c, i, mine); // (an ammo feed straight into the gun: it needs no magazine)
        if (problem.Length == 0 && feed < 0 && !direct)
            problem = "no magazine or ammo feed on the receiver";
        var res = problem.Length == 0 ? BuildStats.ReceiverStats(d.Name, root, null, ChargeOf(c, i, d)) : null;
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
        if (direct)
        {
            var rules = ConstructWeaponParts.Current;
            w.Mag = Math.Clamp((int)MathF.Round(w.Rpm / 60f * rules.FeedReadySeconds), 1, 200); // (its ready rounds: a cut feed leaves these)
            w.Reload = rules.FeedReload;
        }
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
        ConstructHarpoon.GunNumbers(ref g, w); // (a flame's or a harpoon's reach, scaled by a merge: ConstructHarpoon.cs)
        if ((flags & GunEffect.Shell) != 0)
            ConstructShells.GunNumbers(ref g, w, ShellOf(root)); // (a shell's blast, speed and crater: ConstructShells.cs)
        // A splitter's barrels each fire from their own tip (scaled with the parts: Front uses their drawn bounds).
        if (tips.Count > 1)
        {
            var gk = c.GunCount;
            var count = 0;
            foreach (var b in res.Barrels)
            {
                if (count >= Construct.MaxMuzzles || !tips.TryGetValue(b.Leaf.Barrel, out var tipPart))
                    continue;
                c.GunMuzzles[gk * Construct.MaxMuzzles + count] = Front(c, tipPart, g.Forward);
                c.GunMuzzlePellets[gk * Construct.MaxMuzzles + count] = (byte)Math.Clamp(b.Pellets, 1, 30);
                count++;
            }
            g.MuzzleCount = (byte)(count > 1 ? count : 0);
        }
        g.Def = w;
    }

    /// <summary>
    /// Fits a builder part onto the first free socket of <paramref name="node"/> the builder's rules allow
    /// (<see cref="BuildRules.SocketWhy"/>); null when it went on, else why not (the first socket's reason).
    /// </summary>
    /// <summary>The plain one-receiver grip (first by the builder's order) a Sandbox receiver stands in for the rules: a grip node with <paramref name="rec"/> in its receiver socket, or null if the library has none.</summary>
    internal static BuildNode? StandInGrip(PartDef rec)
    {
        var standIn = Parts.All.FirstOrDefault(p => p.Category == PartCategory.Grip && p.Sockets.Count(x => x.Type == "receiver") == 1 && p.Sockets.Any(x => x.Type == "stock") && p.Sockets.Any(x => x.Type == "foregrip"));
        if (standIn == null)
            return null;
        var grip = new BuildNode(standIn);
        var rs = Array.Find(standIn.Sockets, x => x.Type == "receiver")!;
        grip.Attach(rs.Name, new BuildNode(rec));
        return grip;
    }

    internal static string? FitAny(BuildNode node, PartDef part)
    {
        string? first = null;
        foreach (var s in node.Part.Sockets)
        {
            if (node.Child(s.Name) != null)
                continue;
            if (BuildRules.SocketWhy(node, s, part) is { } why)
            {
                first ??= why.TrimEnd('.');
                continue;
            }
            node.Attach(s.Name, part);
            return null;
        }
        return first ?? $"the {node.Part.Name} has no free socket for the {part.Name}";
    }

    /// <summary>Which socket of builder splitter <paramref name="bp"/> barrel part <paramref name="barrel"/> sits on, by the splitter's "front-N" socket it is linked to; -1 when it is not on one (a hand-made splitter).</summary>
    private static int SplitterOutput(Construct c, int splitter, int barrel, PartDef bp)
    {
        for (var k = 0; k < c.LinkCount; k++)
        {
            ref var l = ref c.Links[k];
            var sock = l.A == splitter && l.B == barrel ? l.SA : l.B == splitter && l.A == barrel ? l.SB : -1;
            if (sock < 0 || sock >= c.DefOf(splitter).Sockets.Length)
                continue;
            var name = c.DefOf(splitter).Sockets[sock].Name;
            if (!c.DefOf(splitter).Generated || !name.StartsWith("front-", StringComparison.Ordinal) || !int.TryParse(name.AsSpan(6), out var nth))
                return -1;
            for (var i = 0; i < bp.Sockets.Length; i++)
                if (bp.Sockets[i].Type == "barrel" && --nth == 0)
                    return i;
        }
        return -1;
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
        if (ConstructAmmo.PartOf(Math.Max(0, c.Parts[i].AmmoType - 1)) is { } ap)
        {
            BuildStats.ApplyAmmo(ap, w, BoreClass.Default); // (what it loaded: ConstructAmmo.cs)
            flags |= EffectsOf(ap.Flags);
        }
        for (var k = 0; k < c.LinkCount; k++)
        {
            var o = GunNeighbour(c, i, k);
            if (o < 0 || taken[o] || !c.Parts[o].Working || c.Parts[o].D.Role != PartRole.GunPart || Parts.Find(c.Parts[o].D.GunPartName) is not { Category: PartCategory.Ammo })
                continue;
            mine[o] = taken[o] = true; // (an old Ammo part: left alone, and a box bolted to it still feeds)
        }
        taken[i] = true;
        CopySound(w, (d.GunSound.Length > 0 ? Weapons.Find(d.GunSound) : null) ?? Weapons.DefaultPrimary);
        ref var p = ref c.Parts[i];
        g.Forward = Rot24.Apply(p.Rot, -Vector3.UnitZ);
        g.Muzzle = p.Pos + Rot24.Apply(p.Rot, d.GunMuzzle);
        g.Shots = Math.Max(1, w.Pellets);
        if (d.GunShell >= 0)
        {
            flags |= GunEffect.Shell; // (a heavy gun's own shell: triggers, feeds and reels as ever; ConstructOrdnance.cs)
            g.Shell = (ShellKind)d.GunShell;
            g.Crater = d.GunCrater;
        }
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

    /// <summary>A gun's powder charge: its receiver's, or a merged receiver's first member set away from the standard load (any member's panel sets it).</summary>
    private static int ChargeOf(Construct c, int i, ConstructPartDef d)
    {
        var charge = (int)MathF.Round(ConstructConfig.Get(c.Parts[i], d, CfgKey.Charge, PowderCharge.Default));
        if (charge != PowderCharge.Default || c.MergeOf[i] == 0)
            return charge;
        for (var m = 0; m < c.PartCount; m++)
            if (c.MergeOf[m] == c.MergeOf[i] && (int)MathF.Round(ConstructConfig.Get(c.Parts[m], c.DefOf(m), CfgKey.Charge, PowderCharge.Default)) is var other && other != PowderCharge.Default)
                return other;
        return charge;
    }

    private static void Finish(Construct c, ref CGun g, WeaponDef w)
    {
        g.RoundV = (g.Effects & GunEffect.Shell) != 0 ? g.Speed
            : (g.Effects & GunEffect.Flame) != 0 || g.Fx is GunFx.Rocket or GunFx.Flame ? 0
            : g.Speed > 0 ? g.Speed
            : ConstructWeaponParts.Current.RoundSpeed * MathF.Max(0.25f, w.BulletSpeed);
        if (g.RoundV > 0 && (g.Effects & GunEffect.Shell) == 0)
        {
            g.RoundV *= w.ChargeV;
            g.MaxRange *= MathF.Max(1f, w.ChargeV); // (a stronger round flies further; a weak one still reaches what its arc reaches)
        }
        if (g.RoundV > 0)
            g.Kick *= w.ChargeV;
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

    private static ShellKind ShellOf(BuildNode root)
    {
        var kind = ShellKind.Explosive;
        foreach (var n in root.Walk())
            foreach (var f in n.Part.Flags)
                kind = f switch { "siege" => ShellKind.Siege, "smokeshell" => ShellKind.Smoke, "incendiary" => ShellKind.Incendiary, _ => kind };
        return kind;
    }

    private static string Plural(PartCategory c) => c switch { PartCategory.Ammo => "ammo parts", PartCategory.Barrel or PartCategory.Splitter => "barrels", _ => c.ToString().ToLowerInvariant() + "s" };

    public static GunEffect EffectsOf(string[] flags)
    {
        var e = GunEffect.None;
        foreach (var f in flags)
            e |= f switch { "penetrating" => GunEffect.Penetrating, "explosive" => GunEffect.Explosive, "incendiary" => GunEffect.Incendiary, "flame" => GunEffect.Flame, "harpoon" => GunEffect.Harpoon, "artillery" => GunEffect.Shell, _ => GunEffect.None };
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
        using var scratch = Scratch.Begin();
        Span<bool> seen = scratch.Take<bool>(c.PartCount);
        Span<ushort> queue = scratch.Take<ushort>(c.PartCount);
        int head = 0, tail = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (mine[i])
            {
                seen[i] = true;
                queue[tail++] = (ushort)i;
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
                if (role == PartRole.Hose && c.Parts[o].D.HoseCarry == HoseCarry.Ammo && c.Parts[o].D.HoseEnd == HoseEnd.Inlet)
                    g.FeedSocket = (short)o; // (a reel's socket bolted on: it feeds as a link would)
                else if (role == PartRole.AmmoBox)
                    c.GunBoxes[k * Construct.BoxesPerGun + g.BoxCount++] = (ushort)o;
                else if (role == PartRole.FeedChute)
                    queue[tail++] = (ushort)o;
            }
        }
        for (var r = 0; r < c.RouteCount; r++)
            if (c.Routes[r].Type == RouteType.Feed && c.Routes[r].To < c.PartCount && mine[c.Routes[r].To])
            {
                g.FeedRoute = (short)r; // (an ammo feed into the gun, or into one of its parts)
                break;
            }
    }

    /// <summary>The Turret Module wired into the gun and its seat (a seat with no module works no gun), a fire director, a sentry's sensor, and the joints they point.</summary>
    private static void Controls(Construct c, ref CGun g)
    {
        for (var w = 0; w < c.WireCount; w++)
        {
            var to = c.Wires[w].To;
            if (to >= c.PartCount || to != g.Part && (c.MergeOf[g.Part] == 0 || c.MergeOf[to] != c.MergeOf[g.Part]) || c.Wires[w].From >= c.PartCount)
                continue; // (a merged receiver: a wire into any of its members)
            var from = c.Wires[w].From;
            var logic = c.Parts[from].D.Logic;
            if (logic == CLogic.Director)
                g.Director = (short)from;
            else if (logic == CLogic.TurretModule && g.Turret < 0 && c.Wires[w].FromPort == 0 && ConstructModules.SeatOf(c, from) is var seat and >= 0)
            {
                g.Turret = (short)from;
                g.Seat = (short)seat;
            }
            else if (logic == CLogic.Proximity && g.Sensor == CGun.NoSensor)
                g.Sensor = from;
        }
        ConstructOrdnance.SightOn(c, ref g);
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
    public static int BoxRounds(Construct c, int k, ConstructWorld? world = null)
    {
        ref var g = ref c.Guns[k];
        var n = 0;
        for (var b = 0; b < g.BoxCount; b++)
        {
            int box = c.GunBoxes[k * Construct.BoxesPerGun + b];
            if (box < c.PartCount && c.Parts[box].Working)
                n += c.Parts[box].Rounds;
        }
        if (ConstructAmmo.FedFrom(world, c, k, out var sc, out var src))
            n += ConstructAmmo.RoundsIn(world, sc!, src);
        if (g.MagPart >= 0 && g.MagPart < c.PartCount && c.Parts[g.MagPart].Working)
            n += ConstructAmmo.PackRounds(c.Parts[g.MagPart].Rounds); // (what a player brought)
        if ((g.Effects & GunEffect.Flame) != 0)
            n += ConstructHarpoon.FuelRounds(c); // (a flame burns the construct's own fuel too)
        return n;
    }

    /// <summary>Where gun <paramref name="k"/>'s muzzle <paramref name="j"/> is (rest frame): a splitter gun's barrel tip, else its one muzzle.</summary>
    public static Vector3 MuzzleRest(Construct c, int k, int j) =>
        c.Guns[k].MuzzleCount > 0 && (uint)j < c.Guns[k].MuzzleCount ? c.GunMuzzles[k * Construct.MaxMuzzles + j] : c.Guns[k].Muzzle;

    /// <summary>The muzzle a gun's round number <paramref name="s"/> of a shot leaves from (each barrel's pellets in turn).</summary>
    public static int MuzzleOfShot(Construct c, int k, int s)
    {
        var n = c.Guns[k].MuzzleCount;
        for (var j = 0; j < n; j++)
        {
            s -= c.GunMuzzlePellets[k * Construct.MaxMuzzles + j];
            if (s < 0)
                return j;
        }
        return Math.Max(0, n - 1);
    }

    /// <summary>Where gun <paramref name="k"/>'s muzzle is and which way it points, in the world now.</summary>
    public static void MuzzleWorld(Construct c, int k, out Vector3 muzzle, out Vector3 fwd) => MuzzleWorld(c, k, 0, out muzzle, out fwd);

    /// <summary>Where gun <paramref name="k"/>'s muzzle <paramref name="j"/> is and which way it points, in the world now.</summary>
    public static void MuzzleWorld(Construct c, int k, int j, out Vector3 muzzle, out Vector3 fwd)
    {
        ref var g = ref c.Guns[k];
        c.WorldFrame(g.Seg, out var q, out var t);
        muzzle = t + Vector3.Transform(MuzzleRest(c, k, j), q);
        fwd = Vector3.Normalize(Vector3.Transform(g.Forward, q));
    }

    // ---- The step (server) ----------------------------------------------------------------------------------------

    /// <summary>One tick of every gun, burning part and cook-off (server, before the logic: wires carry last tick's values).</summary>
    public static void Step(ConstructWorld world, IWorldQuery query, float dt)
    {
        if (!world.FlushedFires)
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
        world.StepFeedBurns(); // (a cut, loaded ammo feed burning along itself: ConstructAmmo.cs)
        world.StepShells(query, dt); // (artillery in the air: ConstructShells.cs)
        world.StepRounds(query, dt, server: true); // (rounds past their reach: ConstructRounds.cs)
    }

    private static void StepGuns(ConstructWorld world, Construct c, IWorldQuery query)
    {
        var lastDirector = -1; // (one look along a director's gunner's view a tick, however many guns it points)
        var directed = Vector3.Zero;
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
            var seated = g.Turret >= 0 && g.Turret < c.PartCount && c.Parts[g.Turret].Count != 0 && g.Seat >= 0 && g.Seat < c.PartCount && (c.Parts[g.Seat].Keys & CPart.Occupied) != 0 && c.Parts[g.Seat].Working;
            var wiredIn = p.InCount;
            var signal = p.In;
            if (g.MergeN > 1)
                for (var m = 0; m < c.PartCount; m++)
                    if (m != g.Part && c.MergeOf[m] == c.MergeOf[g.Part])
                    {
                        wiredIn += c.Parts[m].InCount; // (a merged receiver: a wire into any of its members is its trigger)
                        signal += c.Parts[m].In;
                    }
            var trigger = wiredIn > 0 && signal >= 0.5f; // (its Turret Module's guns output is a wire like any other)
            if (seated && g.AimJoints > 0)
            {
                // At what the gunner looks at (the guns converge on it: no parallax from the gun sitting off the eye).
                Aim(c, k, LaidWay(world, c, k, query), force: true);
            }
            else if (g.Director >= 0 && g.AimJoints > 0 && g.Director < c.PartCount && c.Parts[g.Director].Count is var manned and > 0 && manned <= c.PartCount)
            {
                if (g.Director != lastDirector && g.Sight != SightKind.Lead)
                {
                    lastDirector = g.Director;
                    directed = GunnerPoint(world, c, manned - 1, query, DirectorReach);
                }
                MuzzleWorld(c, k, out var muzzle, out _);
                var point = g.Sight == SightKind.Lead ? ConstructOrdnance.SightPoint(world, c, ref g, manned - 1, muzzle, query, DirectorReach) : directed;
                if (Aim(c, k, AimWay(c, k, muzzle, point), force: false) > SentryHoldFire)
                    trigger = false; // (held until it bears, like a sentry)
            }
            else if (g.Sensor != CGun.NoSensor && g.AimJoints > 0 && Nearest(world, c, g.Sensor) is { } target)
            {
                MuzzleWorld(c, k, out var muzzle, out _);
                var off = Aim(c, k, AimWay(c, k, muzzle, target + new Vector3(0, SentryAimHeight, 0)), force: false);
                if (off > SentryHoldFire)
                    trigger = false; // (a sentry turns onto its target first)
            }
            if (g.Seat >= 0 && g.Seat < c.PartCount && (c.Parts[g.Seat].Keys & ConstructParts.KeyReload) != 0 && p.Reload == 0 && p.Rounds > 0 && ConstructAmmo.TypeOfGun(world, c, k) + 1 != Math.Max((byte)1, p.AmmoType))
            {
                p.Rounds = 0; // (the gunner's reload clears what is in it, for the type the switch has chosen: those rounds are lost)
                world.NoteAmmo(c, g.Part);
            }
            var per = Math.Max((ushort)1, g.PerShot);
            if (p.Rounds < per && p.Reload == 0 && BoxRounds(c, k, world) > 0)
            {
                p.Reload = g.ReloadTicks;
                world.NoteAmmo(c, g.Part);
            }
            if ((g.Effects & GunEffect.Harpoon) != 0 && ConstructHarpoon.LineOut(world, c, ref g, trigger))
                continue; // (a line is out: the trigger reels, the reload key cuts: ConstructHarpoon.cs)
            if (!trigger || p.Cool > 0 || p.Rounds < per || p.Reload > 0)
                continue;
            if ((g.Effects & GunEffect.Shell) != 0 && !world.ShellRoom)
                continue; // (as many shells in the air as the world holds: it waits, the round not spent)
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
        // What it loads sets its rounds: a feed's store first, else the nearest box with any in it, and it takes only that type.
        var from = c;
        if (!ConstructAmmo.FedFrom(world, c, k, out var store, out var first) || ConstructAmmo.RoundsIn(world, store!, first) <= 0)
        {
            first = -1;
            for (var b = 0; b < g.BoxCount && first < 0; b++)
                if (ConstructAmmo.RoundsIn(world, c, c.GunBoxes[k * Construct.BoxesPerGun + b]) > 0)
                    first = c.GunBoxes[k * Construct.BoxesPerGun + b];
        }
        else
            from = store!;
        // A magazine a player brought goes in before the boxes (a feed that works goes first: it takes the magazine's place).
        var hand = (from == c && g.FeedRoute < 0 && g.FeedSocket < 0 || first < 0) && g.MagPart >= 0 && g.MagPart < c.PartCount && c.Parts[g.MagPart].Working && ConstructAmmo.PackRounds(c.Parts[g.MagPart].Rounds) > 0;
        if (first >= 0 || hand)
        {
            var type = hand ? ConstructAmmo.PackType(c.Parts[g.MagPart].Rounds) : ConstructAmmo.TypeOf(from, first);
            if (Math.Max((byte)1, p.AmmoType) != type + 1)
            {
                p.Rounds = 0; // (another type: what was left is cleared)
                p.AmmoType = (byte)(type + 1);
                BuildGuns(c, world); // (its numbers: once, at the reload; c.Guns is reused)
                g = ref c.Guns[k];
                p = ref c.Parts[g.Part];
            }
            else
                p.AmmoType = (byte)(type + 1);
        }
        var want = g.Mag - p.Rounds;
        if (hand)
        {
            ref var mag = ref c.Parts[g.MagPart];
            var take = Math.Min(want, ConstructAmmo.PackRounds(mag.Rounds));
            mag.Rounds = ConstructAmmo.Pack(ConstructAmmo.PackRounds(mag.Rounds) - take, ConstructAmmo.PackType(mag.Rounds));
            p.Rounds += (ushort)take;
            world.NoteAmmo(c, g.MagPart);
        }
        else if (first >= 0 && want > 0)
        {
            var got = ConstructAmmo.Take(world, from, first, want);
            p.Rounds += (ushort)got;
            want -= got;
        }
        for (var b = 0; b < g.BoxCount && want > 0 && first >= 0 && !hand; b++)
        {
            int box = c.GunBoxes[k * Construct.BoxesPerGun + b];
            if (box >= c.PartCount || from == c && box == first || !c.Parts[box].Working || ConstructAmmo.TypeOf(c, box) != ConstructAmmo.TypeOf(from, first))
                continue;
            var got = ConstructAmmo.Take(world, c, box, want);
            p.Rounds += (ushort)got;
            want -= got;
        }
        if (want > 0 && (g.Effects & GunEffect.Flame) != 0)
            p.Rounds += (ushort)ConstructHarpoon.TakeFuel(c, want);
        world.NoteAmmo(c, g.Part);
    }

    /// <summary>
    /// Which way gun <paramref name="k"/>'s gunner lays it this tick: at the point the gunner looks at (the guns converge on it: no parallax from the gun
    /// sitting off the eye), or the lead sight's point, along the arc that lands there. The vehicle kit's aim check judges a gun by this (VehicleChecks.Aim).
    /// </summary>
    public static Vector3 LaidWay(ConstructWorld world, Construct c, int k, IWorldQuery query)
    {
        ref var g = ref c.Guns[k];
        MuzzleWorld(c, k, out var muzzle, out _);
        var point = g.Sight == SightKind.Lead ? ConstructOrdnance.SightPoint(world, c, ref g, g.Seat, muzzle, query, g.MaxRange) : GunnerPoint(world, c, g.Seat, query, g.MaxRange);
        return AimWay(c, k, muzzle, point);
    }

    /// <summary>Which way to point gun <paramref name="k"/> at a point: straight at it, or along the arc that lands there for a shell gun. Holding the seat's aim key takes the high arc.</summary>
    private static Vector3 AimWay(Construct c, int k, Vector3 muzzle, Vector3 point)
    {
        ref var g = ref c.Guns[k];
        if ((g.Effects & GunEffect.Shell) != 0 && c.DefOf(g.Part).GunThrust > 0)
            return point - muzzle; // (a rocket flies at it under its own push)
        if ((g.Effects & GunEffect.Shell) == 0)
            return g.RoundV > 0 && ConstructShells.Lead(muzzle, point, g.RoundV, false, out var flat) ? flat : point - muzzle; // (direct fire allows for the drop)
        var high = g.Seat >= 0 && g.Seat < c.PartCount && (c.Parts[g.Seat].Keys & ConstructParts.KeyAim) != 0;
        ConstructShells.Lead(muzzle, point, g.Speed, high, out var dir);
        return dir;
    }

    /// <summary>The gunner's eye in seat <paramref name="seat"/> (the seat's feet point, up its up by the eye height).</summary>
    public static Vector3 SeatEye(Construct c, int seat)
    {
        if (c.Parts[seat].D.RemoteReceiver && RemoteSight(c, seat, 1f, false, out var sight))
            return sight; // (a remote gunner looks along the gun, not from inside the turret: the pilot's view is the same point)
        c.WorldFrame(c.Parts[seat].Seg, out var q, out _);
        var up = Vector3.Transform(Vector3.UnitY, q * Rot24.Quat(c.Parts[seat].Rot));
        return ConstructEdits.SeatFeet(c, seat) + up * World.EyeHeight;
    }

    public const float SightAhead = 0.3f, SightAbove = 0.3f; // (m: the remote sight past the muzzle and above the bore)

    /// <summary>
    /// A remote gunner's sight: just past and above the muzzle of the first working gun that Drone Receiver <paramref name="seat"/> aims, so the
    /// view (Program.PilotCamera) and the aim (<see cref="GunnerPoint"/>) both start clear of the turret's own housing and the crosshair is where
    /// the guns converge. <paramref name="drawn"/>: at the construct as drawn this frame (<see cref="ConstructRemote.CameraPose"/>'s blend), else as simulated.
    /// False when the seat aims no gun.
    /// </summary>
    public static bool RemoteSight(Construct c, int seat, float alpha, bool drawn, out Vector3 eye, bool blendTicks = false)
    {
        eye = default;
        for (var k = 0; k < c.GunCount; k++)
        {
            ref var g = ref c.Guns[k];
            if (g.Seat != seat || g.AimJoints == 0 || g.Part >= c.PartCount || c.Parts[g.Part].Broken)
                continue;
            // The height is chosen on the simulated pose, as the server chooses it, and placed on the drawn one.
            c.WorldFrame(g.Seg, out var q, out var t);
            SightFrame(c, k, q, t, out var muzzle, out var fwd, out var up);
            var h = SightHeight(c, k, muzzle, fwd, up);
            if (drawn)
            {
                var pos = blendTicks ? Vector3.Lerp(c.PrevPos, c.Pos, alpha) : c.Pos;
                var rot = blendTicks ? Quaternion.Slerp(c.PrevRot, c.Rot, alpha) : c.Rot;
                var sq = Quaternion.Identity;
                var st = Vector3.Zero;
                if (g.Seg > 0 && g.Seg <= c.JointCount)
                    ConstructRender.SegPoseBlended(c, g.Seg, blendTicks ? alpha : 1f, out sq, out st);
                SightFrame(c, k, rot * sq, pos + Vector3.Transform(st, rot), out muzzle, out _, out up);
            }
            eye = muzzle + up * h;
            return true;
        }
        return false;
    }

    /// <summary>Gun <paramref name="k"/>'s muzzle moved <see cref="SightAhead"/> along its bore, the bore, and up square to it, for its segment's frame.</summary>
    private static void SightFrame(Construct c, int k, Quaternion q, Vector3 t, out Vector3 muzzle, out Vector3 fwd, out Vector3 up)
    {
        fwd = Vector3.Transform(c.Guns[k].Forward, q);
        var fl = fwd.Length();
        fwd = fl > 1e-4f ? fwd / fl : Vector3.Transform(-Vector3.UnitZ, q);
        up = Vector3.Transform(Vector3.UnitY, q);
        up -= fwd * Vector3.Dot(up, fwd); // (above the bore, square to it; a gun pointing straight up uses its body's back)
        var ul = up.Length();
        up = ul > 1e-3f ? up / ul : Vector3.Transform(Vector3.UnitZ, q);
        muzzle = t + Vector3.Transform(MuzzleRest(c, k, 0), q) + fwd * SightAhead;
    }

    private static readonly float[] SightHeights = [SightAbove, 0.7f, 1.2f]; // (m above the bore: the first whose look along the gun is clear of its own body)
    private const float SightClear = 12f; // (m along the gun that must be clear of the construct's own parts)

    /// <summary>How far above the bore the remote sight sits: the lowest height whose line along the gun meets none of the construct's own parts (a beam, a cab roof it fires over, a shield's lip), else the lowest.</summary>
    private static float SightHeight(Construct c, int k, Vector3 muzzle, Vector3 fwd, Vector3 up)
    {
        for (var h = 0; h < SightHeights.Length; h++)
            if (OwnRayClear(c, c.Guns[k].Part, muzzle + up * SightHeights[h], fwd, SightClear))
                return SightHeights[h];
        return SightAbove;
    }

    [ThreadStatic] private static int[]? _sightParts; // (made once per thread: the server's tick and the view may both ask)

    /// <summary>Does a ray miss every solid box of the construct (bar the gun's own receiver)?</summary>
    private static bool OwnRayClear(Construct c, int gun, Vector3 o, Vector3 dir, float len)
    {
        var into = _sightParts ??= new int[Construct.MaxParts];
        var n = c.PartsOnRay(c.ToLocal(o), Vector3.Transform(dir, Quaternion.Conjugate(c.Rot)), len, into); // (as ConstructWorld.Raycast: only the parts near the ray)
        for (var pi = 0; pi < n; pi++)
        {
            var i = into[pi];
            if (i == gun || c.Parts[i].Broken)
                continue;
            c.WorldFrame(c.Parts[i].Seg, out var q, out var t);
            var iq = Quaternion.Conjugate(q);
            var lo = Vector3.Transform(o - t, iq);
            var ld = Vector3.Transform(dir, iq);
            var boxes = c.SolidCount(i);
            for (var bi = 0; bi < boxes; bi++)
            {
                c.SolidBox(i, bi, out var centre, out var half);
                if (Geometry.RayAabb(centre - half, centre + half, lo, ld, len) is not null)
                    return false;
            }
        }
        return true;
    }

    /// <summary>What the gunner in <paramref name="seat"/> looks at: the map or another construct along their look, else as far as the gun reaches.</summary>
    public static Vector3 GunnerPoint(ConstructWorld world, Construct c, int seat, IWorldQuery query, float reach)
    {
        var eye = SeatEye(c, seat);
        var look = GravityFrames.Forward(c.WorldCom, c.Parts[seat].AimYaw, c.Parts[seat].AimPitch);
        var d = query.Raycast(eye, look, reach, QueryFilter.Shots);
        if (world.Raycast(eye, look, d, out var hit, c, ignoreCargo: true) && hit.Distance < d) // (its own load, riding frozen in its cargo volumes, is part of it: never what its gunner aims at)
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
        var range = ConstructConfig.Get(c.Parts[sensor], d, CfgKey.Radius, d.Settings.Length > 0 ? d.Settings[Math.Min(c.Parts[sensor].Setting, (byte)(d.Settings.Length - 1))] : 6f);
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
    internal static void Fire(ConstructWorld world, Construct c, int k, IWorldQuery query)
    {
        var g = c.Guns[k]; // (a copy: a hit can rebuild this construct)
        var seq = ++_seq;
        if (g.Kick > 0)
            Recoil(c, k); // (before the rounds: what they hit can't have moved the gun yet)
        var damage = (float)g.Def!.Damage;
        if ((g.Effects & GunEffect.Shell) != 0)
        {
            MuzzleWorld(c, k, out _, out var along);
            ConstructShells.Launch(world, c, k, Scatter(along, g.Spread, (uint)(c.Id * 73856093) ^ (uint)(g.Part * 19349663) ^ (seq * 83492791u)), query);
            world.ShotsFired++;
            return;
        }
        for (var s = 0; s < g.Shots; s++)
        {
            var mz = MuzzleOfShot(c, k, s);
            MuzzleWorld(c, k, mz, out var muzzle, out var sfwd);
            var dir = Scatter(sfwd, g.Spread, (uint)(c.Id * 73856093) ^ (uint)(g.Part * 19349663) ^ (seq * 83492791u) ^ (uint)(s * 2654435761u));
            world.DamageBy = c.Owner; // (a construct's gun: its owner's doing, for the story)
            var hit = Round(world, c, g, query, muzzle, dir, damage, out var end);
            world.DamageBy = 255;
            world.AddFire(new CFireEvent { Kind = CFireKind.Shot, Body = c.Id, Part = g.Part, Muzzle = (byte)mz, At = end, Hit = hit });
        }
        world.ShotsFired++;
    }

    /// <summary>
    /// Board item gr01: a shot's recoil as a real impulse. Each barrel tip (a splitter's every muzzle, or the one muzzle)
    /// pushes back along the barrel with the gun's kick (N s; a merged receiver's is already scaled), at the tip, on the
    /// gun's segment: through the joints up its chain and the body (<see cref="ConstructPhysics.AddRecoil"/>). An off-centre
    /// gun twists its vehicle, a turret kicks back, an elevation hinge climbs. Nothing for a body that can't move (anchored,
    /// with no moving joint under the gun) or rides as cargo.
    /// </summary>
    private static void Recoil(Construct c, int k)
    {
        ref var g = ref c.Guns[k];
        if (c.CarriedBy != 0 || c.Anchored && !SegMoves(c, g.Seg))
            return;
        var n = Math.Max(1, (int)g.MuzzleCount);
        for (var j = 0; j < n; j++)
        {
            MuzzleWorld(c, k, j, out var at, out var fwd);
            ConstructPhysics.AddRecoil(c, g.Seg, at, -fwd * g.Kick);
        }
    }

    /// <summary>Is there a joint that moves (not a freeform weld or a steering pivot) up segment <paramref name="seg"/>'s chain?</summary>
    private static bool SegMoves(Construct c, int seg)
    {
        for (var guard = 0; seg > 0 && seg <= c.JointCount && guard <= Construct.MaxJoints; guard++)
        {
            ref var jt = ref c.Joints[seg - 1];
            if (!jt.Freeform && jt.Type != JointType.Steering)
                return true;
            seg = jt.ParentSeg;
        }
        return false;
    }

    /// <summary>The camera shake a gunner feels from one pull of a gun with total kick <paramref name="kick"/> (N s, every barrel's): a cannon's about 0.6, an MG's a tremor.</summary>
    public static float GunnerShake(float kick) => kick <= 0 ? 0 : MathF.Min(0.6f, 0.6f * MathF.Sqrt(kick / 900f));

    /// <summary>One pull's whole kick (every barrel's).</summary>
    public static float PullKick(in CGun g) => g.Kick * Math.Max(1, (int)g.MuzzleCount);

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
            // Rounds fall: a few straight pieces of the arc, each cast as a straight round was, the first that meets something ending it.
            // (At most ArcPieces a round, whatever the gun; a short range is one piece.)
            ConstructHit ch = default;
            var tid = -1;
            var what = 0;
            float best;
            var pieces = g.RoundV > 0 ? Math.Clamp((int)MathF.Ceiling(range / ArcPieceMin), 1, ArcPieces) : 1;
            var vel = dir * g.RoundV;
            for (var arc = 0; ; arc++)
            {
                var seg = range / pieces;
                if (g.RoundV > 0)
                {
                    var t = seg / g.RoundV;
                    var pull = ConstructShells.GravityAt(pos);
                    var chord = vel * t + pull * (0.5f * t * t);
                    seg = chord.Length();
                    dir = chord / seg;
                    vel += pull * t;
                }
                best = query.Raycast(pos, dir, seg, QueryFilter.Shots);
                what = best < seg - 1e-3f ? 1 : 0; // 1: the map
                if (world.Raycast(pos, dir, best, out var cHit, own) && cHit.Distance < best)
                {
                    best = cHit.Distance;
                    ch = cHit;
                    what = 2;
                }
                if (world.Targets != null && world.Targets.RayTarget(pos, dir, best, own.Id, out var td, out var id) && td < best)
                {
                    best = td;
                    tid = id;
                    what = 3;
                }
                if (world.ShellCount > 0 && world.RayShell(pos, dir, best, out var sd, out var sk) && sd < best)
                {
                    best = sd;
                    tid = sk;
                    what = 4; // (a bomb or missile in the air: ConstructCookOffs.cs)
                }
                if (what != 0 || arc + 1 >= pieces)
                {
                    if (what == 0 && g.RoundV > 0 && (g.Effects & (GunEffect.Flame | GunEffect.Harpoon)) == 0 && pierce == 0)
                        world.RoundFlies(pos + dir * seg, vel, left * g.Def!.DamageMultAt(travelled + seg), g.RoundV, own.Owner, g.Def.Id, own.Id); // (nothing in its reach: it flies on, ConstructRounds.cs)
                    break;
                }
                pos += dir * seg;
                travelled += seg;
            }
            var at = pos + dir * best;
            travelled += best;
            end = at;
            var dealt = left * g.Def!.DamageMultAt(travelled);
            if ((g.Effects & GunEffect.Harpoon) != 0 && pierce == 0 && what != 0 && !(what == 2 && ch.Glass))
                ConstructHarpoon.Tie(world, query, own, g, from, what == 2 ? ch.Body : null, ch.Part, what == 3 ? tid : -1, at); // (before the damage: tied by the part's tag, so a part that breaks takes the rope with it)
            if (what == 3)
            {
                world.Targets!.HitTarget(tid, dealt, g.Effects, own, g.Part, at, dir);
                hit |= CFireHit.Target;
                break;
            }
            if (what == 4)
            {
                world.HurtShell(tid, dealt);
                hit |= CFireHit.Target;
                break;
            }
            if (what != 2)
            {
                if (what == 1)
                {
                    hit |= CFireHit.World;
                    if (dealt >= ConstructWeaponParts.Current.DigDamage)
                        world.Targets?.RoundDig(at, dealt, own, g.Part); // (a heavy round chews the ground: rate-limited, GameConstructWeapons.cs)
                    if ((g.Effects & GunEffect.Flame) != 0)
                        world.Targets?.FlameWorld(pos, dir, best + 0.05f, dealt, own, g.Part);
                }
                break;
            }
            // A construct part.
            var body = ch.Body;
            var part = ch.Part;
            if (ch.TrackPlus1 > 0)
            {
                ConstructTracks.Damage(world, body, ch.TrackPlus1 - 1, dealt); // (a track: at zero it comes off)
                hit |= CFireHit.Part;
                break;
            }
            if (ch.RoutePlus1 > 0)
            {
                ConstructRoutes.Damage(world, body, ch.RoutePlus1 - 1, ch.RouteSeg, dealt, blast: (g.Effects & GunEffect.Incendiary) != 0); // (a routed link's segment: a cable, a pipe, a shaft; fire lights what a cut pipe leaks)
                hit |= CFireHit.Part;
                break;
            }
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
            if (body.SealCount > 0 && (g.Effects & (GunEffect.Incendiary | GunEffect.Flame)) != 0)
                ConstructSeal.IgniteAt(world, body, at); // (it landed in an open box of something that burns)
            if ((g.Effects & GunEffect.Flame) != 0)
                world.DamagePart(body, part, ConstructHarpoon.HeatThrough(dealt, armour), out _, raw: true); // (heat: armour slows it, a bullet's rule would stop it dead)
            else
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
                    if (o >= 0 && c.Parts[o].Burn == 0 && world.CanBurn(c, o) && !c.Parts[o].Broken)
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
            if (at.Bomb != null && at.Bomb.ExplodeRadius >= ConstructWorld.MegaRadius)
                world.MegaCookOff(at); // (a massive warhead set off where it hangs: a shock front, not a prop's blast; ConstructMegaBlast.cs)
            else if (at.Bomb != null)
                world.Targets?.ExplodePart(at.At, at.Bomb, world.Get(at.Body));
            else
                Explode(world, at.At, at.Radius, at.Damage, world.Get(at.Body), CFireKind.CookOff);
        }
    }
}
