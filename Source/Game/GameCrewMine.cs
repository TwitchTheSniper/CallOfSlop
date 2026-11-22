using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Crew miners (the user, 2026-11-17: extraction follows the ore; "tool racks for NPCs", so crew carry portable drills to where the ore is;
/// docs/sandbox/design/factory-generator-notes.md "Tool racks and crew miners"). A crewed body with a Tool Rack has a Miner's station at it
/// (CrewStationKind.Rack). Its miner, every so often: picks the nearest ore spot near the post that a drill can work and nobody else is at
/// (a 3 m spot not worked out, outside the post's own bounds and its drills' reach), takes a portable drill off the rack (or, when one is
/// free, wears a Mining Shell from the body's Shell Control Chair), walks off the post to the spot, drills a load (a portable drill
/// <see cref="DrillRate"/> a second into its arms up to <see cref="DrillLoad"/>; a shell its arm's rate into its hopper), walks back to the
/// post's bin, tips the load in, and hangs the drill back (or leaves the shell standing by the bin and wakes in the chair). A bodied miner
/// walks it (CrewNav on the post, straight steering on the ground, put there after <see cref="StuckSeconds"/> with no headway, logged);
/// a record (nobody near) does it on timers. Server only; fixed arrays, nothing allocated in a steady tick; at most <see cref="MaxMiners"/>
/// at work at once, looked at <see cref="MineEvery"/> ticks apart.
/// </summary>
public sealed partial class Game
{
    public const int MaxMiners = 64, MineEvery = 10;

    /// <summary>A portable drill: units a second, the most a miner carries back (about a Material Bundle's load), its tier (the Mining Shell arm's).</summary>
    public const float DrillRate = 10;

    public const int DrillLoad = 600, DrillTier = 2;

    /// <summary>A crew miner in a Mining Shell goes back to tip when its hopper holds this much (two spots' worth, 80 s of its arm), not only when full: the post's buffer is fed steadily.</summary>
    public const int ShellLoad = 2400;

    /// <summary>How far from its rack a miner looks for ore (m), how far it keeps off the post's own bounds (its drills' arms and gantries reach out 3 m).</summary>
    public const float MineReach = 40, MineClear = 4;

    /// <summary>Seconds a miner rests between trips, and with no headway before it is put where it was going.</summary>
    public const float MineRest = 5, StuckSeconds = 4;

    private enum MineStage : byte
    {
        None,
        ToEdge, // a bodied rack miner walks the post's floor (CrewNav) to the edge nearest the spot
        ToSpot, // ... then over the ground to the spot
        Drill,
        Back, // over the ground to the edge again (a shell: to the bin)
        ToBin, // CrewNav to the bin's foot
        ToRack, // ... and to the rack, to hang the drill back (or from the chair after a shell, home)
    }

    private struct MineJob
    {
        public int Member;
        public ushort Body, RackTag, BinTag, ChairTag;
        public sbyte Hook; // the rack's hook the drill came off (-1: none, a shell)
        public MineStage Stage;
        public bool Bodied, Shell, Waited;
        public Vector3 Spot; // the spot's ground point (world)
        public uint SpotKey;
        public Vector3 Edge; // where it steps off and back on (the post's rest frame)
        public byte Mat;
        public int Carry, Mined;
        public float Acc, Best;
        public long Until, Stuck, Next;
    }

    private readonly MineJob[] _mine = new MineJob[MaxMiners];
    private int _mineHigh;

    /// <summary>A tool's counts: trips done, units tipped into bins, fallbacks (put there for want of headway).</summary>
    public int MineTrips, MineTipped, MinePutThere;

    private int MineOf(int m)
    {
        for (var k = 0; k < _mineHigh; k++)
            if (_mine[k].Member == m && _mine[k].Body != 0)
                return k;
        return -1;
    }

    /// <summary>Server, from the crew's work slice (GameCrewWork.cs): each rack's miner on this body starts a trip when it is due.</summary>
    private void CrewMiner(Construct c, CrewShip s)
    {
        MineRacks(c);
        for (var st = 0; st < s.StCount; st++)
        {
            if (s.StKind[st] != CrewStationKind.Rack || s.StMember[st] is not (var m and >= 0) || Crew.State[m] != CrewState.Station)
                continue;
            var k = MineOf(m);
            if (k >= 0 && _mine[k].Stage != MineStage.None)
                continue;
            if (k < 0)
            {
                k = MineSlot();
                if (k < 0)
                    return; // (every slot busy: the rest wait)
                _mine[k] = new MineJob { Member = m, Body = c.Id, Hook = -1, Next = Crew.Tick + (long)(MineRest * World.TickRate) };
                continue;
            }
            if (Crew.Tick < _mine[k].Next)
                continue;
            _mine[k].Next = Crew.Tick + (long)(MineRest * World.TickRate);
            MineStart(c, s, st, m, k);
        }
    }

    /// <summary>
    /// The body's racks' empty hooks follow the trips under way: a hook marked out with no trip that took it (a world saved with miners out:
    /// trips aren't saved; a crew that went) has its drill back. Each look of the work slice; a few racks a body.
    /// </summary>
    private void MineRacks(Construct c)
    {
        for (var k = 0; k < c.ToolRackCount; k++)
        {
            int part = c.ToolRacks[k];
            if (part >= c.PartCount || !c.DefOf(part).IsToolRack || ToolRacks.Out(c.Parts[part]) is var o && o == 0)
                continue;
            var live = 0;
            for (var i = 0; i < _mineHigh; i++)
                if (_mine[i].Body == c.Id && _mine[i].Hook >= 0 && _mine[i].Stage != MineStage.None && _mine[i].RackTag == c.Parts[part].Tag)
                    live |= 1 << _mine[i].Hook;
            for (var h = 0; h < c.DefOf(part).RackHooks.Length; h++)
                if ((o & ~live & 1 << h) != 0 && ToolRacks.Give(Constructs, c, part, h))
                    Console.WriteLine($"[crew] construct {c.Id}: the {c.DefOf(part).Name}'s hook {h + 1} had no miner out: its drill is back");
        }
    }

    private int MineSlot()
    {
        for (var k = 0; k < _mineHigh; k++)
            if (_mine[k].Body == 0)
                return k;
        return _mineHigh < MaxMiners ? _mineHigh++ : -1;
    }

    /// <summary>A trip: a spot, a bin, a drill (or a shell), and off.</summary>
    private void MineStart(Construct c, CrewShip s, int st, int m, int k)
    {
        ref var j = ref _mine[k];
        int rack = s.StPart[st];
        if (Constructs.Resources is not { } field || field.Planet != null)
            return; // (no ore here, or a round world: crew miners work flat ground for now)
        var bin = MineBin(c, 0);
        if (bin < 0)
            return;
        var from = ConstructRender.PartCentre(c, rack);
        if (!MineSpot(c, field, from, DrillTier, 0, out var spot, out var key))
            return;
        Player? body = null;
        j.Bodied = Crew.Player[m] != 0 && Players.TryGetValue((byte)(Crew.Player[m] - 1), out body) && body.Alive;
        if (j.Bodied && body != null && SandboxHands.Hand(body.Id).Seated)
            CrewStandAt(body, c, CrewWorld.PartFoot(c, rack)); // (never sets off from a seat: up at its rack first)
        j.Shell = false;
        j.Hook = -1;
        j.ChairTag = 0;
        if (j.Bodied && MineShell(c, Players[(byte)(Crew.Player[m] - 1)], out var chair))
        {
            j.Shell = true;
            j.ChairTag = c.Parts[chair].Tag;
        }
        else
        {
            var hook = ToolRacks.Take(Constructs, c, rack);
            if (hook < 0)
            {
                if (!j.Waited)
                    Console.WriteLine($"[crew] construct {c.Id}: its miner {Crew.Uid[m]} waits for a drill: the {c.DefOf(rack).Name} is empty");
                j.Waited = true;
                return; // (no drill on the rack: it waits)
            }
            j.Waited = false;
            j.Hook = (sbyte)hook;
        }
        j.RackTag = c.Parts[rack].Tag;
        j.BinTag = c.Parts[bin].Tag;
        j.Spot = spot;
        j.SpotKey = key;
        j.Carry = 0;
        j.Mat = 0;
        j.Acc = 0;
        j.Mined = 0;
        j.Edge = MineEdge(c, spot);
        var walk = Vector3.Distance(from, spot) * 1.4f / CrewWorld.WalkSpeed;
        if (!j.Bodied)
        {
            j.Stage = MineStage.ToSpot;
            j.Until = Crew.Tick + (long)(walk * World.TickRate);
        }
        else if (j.Shell)
        {
            j.Stage = MineStage.ToSpot;
            MineSteerStart(ref j);
        }
        else
        {
            SandboxHands.Hand((byte)(Crew.Player[m] - 1)).Tool = ConstructTool.GroundDrill;
            j.Stage = Crew.Errand(c, s, m, j.Edge, this) ? MineStage.ToEdge : MineStage.ToSpot;
            j.Until = Crew.Tick + ErrandWait;
            if (j.Stage == MineStage.ToSpot)
                MineSteerStart(ref j);
        }
        Console.WriteLine($"[crew] construct {c.Id}: its miner {Crew.Uid[m]} {(j.Shell ? "wakes in a Mining Shell" : "takes a drill off the rack")} for ore {Vector3.Distance(from, spot):0} m off ({(j.Bodied ? "walks" : "goes")})");
    }

    private void MineSteerStart(ref MineJob j)
    {
        j.Best = float.MaxValue;
        j.Stuck = 0;
    }

    /// <summary>The post's bin a miner tips into: a Storage Volume first, else a plain bin (no machine, no drill), that takes ore of <paramref name="mat"/> (0: any) with room; -1: none.</summary>
    private int MineBin(Construct c, byte mat)
    {
        var best = -1;
        var volume = false;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            var d = p.D;
            if (p.Broken || !d.IsBin || d.IsGatherer || d.IsGrower || d.IsRecycler || d.IsRefiner || d.IsAuger || d.ShellBody || d.IsAssembler)
                continue;
            if (Constructs.StoreOf(c, i) is not { } st || st.KindN > 0 || mat != 0 && (st.Amount > 0 && st.Material != mat || !ConstructStorage.BinTakes(d, mat)))
                continue;
            if (st.Amount >= ConstructStorage.Capacity(c, i))
                continue;
            if (best < 0 || d.IsVolumeStore && !volume)
                (best, volume) = (i, d.IsVolumeStore);
        }
        return best;
    }

    /// <summary>
    /// The nearest ore spot within <see cref="MineReach"/> of <paramref name="from"/> a tier <paramref name="tier"/> drill can work: over a deposit
    /// that isn't deep, not worked out, not another miner's, and clear of the post's own bounds by <see cref="MineClear"/>. Rings of spots outward,
    /// at most (2 x 14 + 1)^2 looks; only when a trip starts.
    /// </summary>
    private bool MineSpot(Construct c, ResourceField field, Vector3 from, int tier, uint not, out Vector3 spot, out uint key)
    {
        spot = default;
        key = 0;
        var size = ResourceField.SpotSize;
        int cx = (int)MathF.Floor(from.X / size), cz = (int)MathF.Floor(from.Z / size);
        var rings = (int)(MineReach / size);
        for (var r = 0; r <= rings; r++)
        {
            var best = float.MaxValue;
            for (var ix = cx - r; ix <= cx + r; ix++)
                for (var iz = cz - r; iz <= cz + r; iz++)
                {
                    if (Math.Abs(ix - cx) != r && Math.Abs(iz - cz) != r)
                        continue; // (the ring's edge only)
                    float x = (ix + 0.5f) * size, z = (iz + 0.5f) * size;
                    var d2 = (x - from.X) * (x - from.X) + (z - from.Z) * (z - from.Z);
                    if (d2 >= best || d2 > MineReach * MineReach)
                        continue;
                    var p = new Vector3(x, field.Ground(x, z), z);
                    var k = ResourceField.SpotKey(p with { Y = 0 });
                    if (k == not || field.TappableAt(x, z) is not { Deep: false } kind || kind.MinTier > tier || field.WorkedOut(p) || MineTaken(k))
                        continue;
                    var local = c.ToLocal(p);
                    if (local.X > c.LocalMin.X - MineClear && local.X < c.LocalMax.X + MineClear && local.Z > c.LocalMin.Z - MineClear && local.Z < c.LocalMax.Z + MineClear)
                        continue; // (under the post, or where its own drills reach)
                    best = d2;
                    (spot, key) = (p, k);
                }
            if (best < float.MaxValue)
                return true;
        }
        return false;
    }

    private bool MineTaken(uint key)
    {
        for (var k = 0; k < _mineHigh; k++)
            if (_mine[k].Body != 0 && _mine[k].Stage is MineStage.ToEdge or MineStage.ToSpot or MineStage.Drill && _mine[k].SpotKey == key)
                return true;
        return false;
    }

    /// <summary>The point on the post's floor nearest the spot (its rest frame, at the floor's top under it): where a bodied miner steps off and on.</summary>
    private static Vector3 MineEdge(Construct c, Vector3 spot)
    {
        var l = c.ToLocal(spot);
        var x = Math.Clamp(l.X, c.LocalMin.X + 0.6f, c.LocalMax.X - 0.6f);
        var z = Math.Clamp(l.Z, c.LocalMin.Z + 0.6f, c.LocalMax.Z - 0.6f);
        return new Vector3(x, c.LocalMin.Y + 0.4f, z);
    }

    /// <summary>A free Shell Control Chair on the body with a Mining Shell (a frame with a drill) in reach on its channel, the body sat in it and woken in the shell. False: none.</summary>
    private bool MineShell(Construct c, Player p, out int chair)
    {
        chair = -1;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var cp = ref c.Parts[i];
            if (cp.D.ShellRange <= 0 || cp.Broken || cp.SitterPlus1 != 0)
                continue;
            if (ConstructShell.Find(Constructs, c, i, p.Id, out var part, out _) is not { } shell || shell.PartCount != 1 || part != 0
                || ShellFrames.Of(ShellFrames.FrameOf(shell.Parts[0].D)).Find(ShellModuleKind.Drill) == null)
                continue; // (a Robot Shell has no drill; a docked shell stays docked for a person)
            if (!CrewSit(p, c, i))
                continue;
            ShellEnter(p, ref SandboxHands.Hand(p.Id), c, i, shell, crew: true);
            if (!p.InShell)
                return false;
            chair = i;
            return true;
        }
        return false;
    }

    /// <summary>Server, each tick: miners at work, a look every <see cref="MineEvery"/> ticks.</summary>
    private void CrewMineTick()
    {
        if (ServerTick % MineEvery != 3 || _mineHigh == 0)
            return;
        for (var k = 0; k < _mineHigh; k++)
            if (_mine[k].Body != 0)
                MineStep(k);
    }

    private void MineStep(int k)
    {
        ref var j = ref _mine[k];
        var m = j.Member;
        var c = Constructs.Get(j.Body);
        var s = c != null ? Crew.ShipOf(c.Id) : null;
        if (c == null || c.Wreck || s == null || m < 0 || m >= Crew.High || Crew.Ship[m] < 0 || Crew.Ships[Crew.Ship[m]] != s || Crew.State[m] != CrewState.Station
            || Crew.Station[m] < 0 || Crew.Station[m] >= s.StCount || s.StKind[Crew.Station[m]] != CrewStationKind.Rack)
        {
            MineEnd(k, c, "its miner left the job");
            return;
        }
        if (j.Stage == MineStage.None)
            return;
        Player? p = null;
        if (j.Bodied && (Crew.Player[m] == 0 || !Players.TryGetValue((byte)(Crew.Player[m] - 1), out p) || !p.Alive || j.Shell && !p.InShell))
        {
            if (j.Shell)
            {
                MineEnd(k, c, "its miner's shell link went");
                return;
            }
            j.Bodied = false; // (nobody near any more: the rest of the trip on timers)
            j.Until = Crew.Tick + 10 * World.TickRate;
            p = null;
        }
        var field = Constructs.Resources;
        switch (j.Stage)
        {
            case MineStage.ToEdge:
                if (!Crew.ErrandDone(m) && Crew.Tick < j.Until)
                    return;
                j.Stage = MineStage.ToSpot;
                MineSteerStart(ref j);
                return;
            case MineStage.ToSpot:
                if (!j.Bodied ? Crew.Tick < j.Until : !MineArrived(ref j, p!, j.Spot, 1.2f))
                    return;
                j.Stage = MineStage.Drill;
                j.Acc = 0;
                return;
            case MineStage.Drill:
            {
                if (field == null)
                {
                    MineHome(k, c, s);
                    return;
                }
                var full = MineDrill(ref j, p, field);
                if (!full && field.WorkedOut(j.Spot) && MineSpot(c, field, j.Spot, DrillTier, j.SpotKey, out var next, out var key)
                    && Vector3.Distance(next, ConstructRender.PartCentre(c, CrewWorld.PartByTag(c, j.RackTag) is var rk and >= 0 ? rk : 0)) <= MineReach)
                {
                    (j.Spot, j.SpotKey) = (next, key); // (worked out with room left: on to the next spot)
                    j.Stage = MineStage.ToSpot;
                    j.Until = Crew.Tick + (long)(ResourceField.SpotSize * 1.4f / CrewWorld.WalkSpeed * World.TickRate);
                    MineSteerStart(ref j);
                    return;
                }
                if (!full && !field.WorkedOut(j.Spot))
                    return;
                j.Stage = MineStage.Back;
                var bin = CrewWorld.PartByTag(c, j.BinTag);
                j.Until = Crew.Tick + (long)(Vector3.Distance(j.Spot, bin >= 0 ? ConstructRender.PartCentre(c, bin) : c.WorldCom) * 1.4f / CrewWorld.WalkSpeed * World.TickRate);
                MineSteerStart(ref j);
                if (p != null)
                    p.Pitch = 0;
                return;
            }
            case MineStage.Back:
            {
                var bin = CrewWorld.PartByTag(c, j.BinTag);
                if (j.Bodied)
                {
                    var to = j.Shell && bin >= 0 ? ConstructRender.PartCentre(c, bin) : c.ToWorld(j.Edge);
                    if (!MineArrived(ref j, p!, to, j.Shell ? 3f : 1f))
                        return;
                }
                else if (Crew.Tick < j.Until)
                    return;
                if (j.Shell || !j.Bodied || bin < 0 || !Crew.Errand(c, s, m, CrewWorld.PartFoot(c, bin), this))
                {
                    MineTip(c, j.Bodied ? p : null, ref j);
                    MineHome(k, c, s);
                    return;
                }
                j.Stage = MineStage.ToBin;
                j.Until = Crew.Tick + ErrandWait;
                return;
            }
            case MineStage.ToBin:
                if (!Crew.ErrandDone(m) && Crew.Tick < j.Until)
                    return;
                MineTip(c, p, ref j);
                MineHome(k, c, s);
                return;
            case MineStage.ToRack:
                if (j.Bodied ? !Crew.ErrandDone(m) && Crew.Tick < j.Until : Crew.Tick < j.Until)
                    return;
                MineEnd(k, c, null);
                return;
        }
    }

    /// <summary>
    /// A drilling look: the deposit under the spot into the miner's arms (a portable drill: <see cref="DrillRate"/> a second, one material, up to
    /// <see cref="DrillLoad"/>) or its shell's hopper (the drill arm's own tap, GameShellHopper.cs). True: full (or the material changed).
    /// </summary>
    private bool MineDrill(ref MineJob j, Player? p, ResourceField field)
    {
        if (j.Shell && p != null)
        {
            ref var hp = ref SandboxHands.Hoppers[p.Id];
            p.Pitch = -1.2f;
            ShellDrillTap(p, j.Spot, SandboxNow); // (its rate by time between looks: MineEvery ticks is well inside its gap)
            j.Mined = j.Carry = hp.Amount;
            j.Mat = hp.Material;
            return hp.Capacity <= 0 || hp.Amount >= Math.Min(hp.Capacity, ShellLoad);
        }
        if (p != null)
            p.Pitch = -1.0f;
        if (field.TappableAt(j.Spot.X, j.Spot.Z) is not { } kind || kind.Deep || kind.MinTier > DrillTier)
            return true;
        var mat = RecyclingTable.Current.IdOf(kind.Material);
        if (j.Carry > 0 && mat != j.Mat)
            return true; // (another ore: back with what it has)
        j.Acc += DrillRate * MineEvery / World.TickRate;
        var whole = Math.Min((int)j.Acc, DrillLoad - j.Carry);
        if (whole <= 0)
            return j.Carry >= DrillLoad;
        j.Acc -= whole;
        var got = field.Tap(j.Spot.X, j.Spot.Z, whole, out var m);
        if (got > 0)
        {
            j.Mat = m;
            j.Carry += got;
            j.Mined += got;
        }
        return j.Carry >= DrillLoad;
    }

    /// <summary>The load into the post's bin (another with room if that one is full or holds another ore); what is left spills as a Material Bundle by it.</summary>
    private void MineTip(Construct c, Player? p, ref MineJob j)
    {
        var carry = j.Carry;
        var mat = j.Mat;
        if (j.Shell && p != null)
        {
            ref var hp = ref SandboxHands.Hoppers[p.Id];
            (carry, mat) = (hp.Amount, hp.Material);
        }
        if (carry <= 0 || mat == 0)
            return;
        var left = carry;
        for (var tries = 0; tries < 4 && left > 0; tries++)
        {
            var bin = CrewWorld.PartByTag(c, j.BinTag) is var b0 and >= 0 && MineBinTakes(c, b0, mat) ? b0 : MineBin(c, mat);
            if (bin < 0)
                break;
            var put = Constructs.Deposit(c, bin, left, mat);
            if (put <= 0)
                break;
            left -= put;
            j.BinTag = c.Parts[bin].Tag;
        }
        var names = RecyclingTable.Current;
        if (left > 0)
        {
            var at = p?.Position ?? ConstructRender.PartCentre(c, CrewWorld.PartByTag(c, j.BinTag) is var bb and >= 0 ? bb : 0);
            if (!Constructs.SpillBundle(mat, left, at + GravityFrames.UpAt(at) * 1.2f, Vector3.Zero))
                Console.WriteLine($"[crew] construct {c.Id}: its miner's {left} {names.NameOf(mat)} lost (no room in the world)");
            else
                Console.WriteLine($"[crew] construct {c.Id}: the bins are full: its miner set {left} {names.NameOf(mat)} down as a bundle");
        }
        MineTipped += carry - left;
        MineTrips++;
        Console.WriteLine($"[crew] construct {c.Id}: its miner {Crew.Uid[j.Member]} tipped {carry - left} {names.NameOf(mat)} into the {(CrewWorld.PartByTag(c, j.BinTag) is var bi and >= 0 ? c.DefOf(bi).Name : "bin")}");
        j.Carry = 0;
        if (j.Shell && p != null)
        {
            ref var hp = ref SandboxHands.Hoppers[p.Id];
            hp.Amount -= Math.Min(hp.Amount, carry - left);
            if (left > 0)
                hp.Amount = 0; // (spilt above)
            if (hp.Amount == 0)
                hp.Material = 0;
        }
    }

    private bool MineBinTakes(Construct c, int bin, byte mat) =>
        Constructs.StoreOf(c, bin) is { KindN: 0 } st && (st.Amount == 0 || st.Material == mat) && ConstructStorage.BinTakes(c.DefOf(bin), mat) && st.Amount < ConstructStorage.Capacity(c, bin);

    /// <summary>Home: the shell left standing by the bin (the body wakes in the chair and walks back to the rack), or the drill carried back to the rack.</summary>
    private void MineHome(int k, Construct c, CrewShip s)
    {
        ref var j = ref _mine[k];
        var m = j.Member;
        var rack = CrewWorld.PartByTag(c, j.RackTag);
        j.Stage = MineStage.ToRack;
        j.Until = Crew.Tick + (j.Bodied ? ErrandWait : (long)((rack >= 0 ? Vector3.Distance(j.Spot, ConstructRender.PartCentre(c, rack)) : 10) * 1.4f / CrewWorld.WalkSpeed * World.TickRate));
        if (!j.Bodied || Crew.Player[m] == 0 || !Players.TryGetValue((byte)(Crew.Player[m] - 1), out var p))
            return;
        if (j.Shell && p.InShell)
        {
            ShellReturn(p, lost: false, "");
            j.Shell = false; // (out of it: the shell stands by the bin for the next trip)
            CrewStandAt(p, c, CrewWorld.PartFoot(c, CrewWorld.PartByTag(c, j.ChairTag) is var ch and >= 0 ? ch : (rack >= 0 ? rack : 0)));
        }
        if (rack < 0 || !Crew.Errand(c, s, m, CrewWorld.PartFoot(c, rack), this))
        {
            if (rack >= 0 && c.ToLocal(p.Position) is var l && (l.X < c.LocalMin.X || l.X > c.LocalMax.X || l.Z < c.LocalMin.Z || l.Z > c.LocalMax.Z || l.Y < c.LocalMin.Y - 1))
                CrewStandAt(p, c, CrewWorld.PartFoot(c, rack)); // (off the post with no way on: put back at the rack, as CrewNav would)
            j.Until = Crew.Tick + 2 * World.TickRate;
        }
    }

    /// <summary>The trip ends (done, or cut short: <paramref name="why"/>): the drill back on its hook, a shell let go, an unfinished load spilt.</summary>
    private void MineEnd(int k, Construct? c, string? why)
    {
        ref var j = ref _mine[k];
        Player? p = null;
        if (j.Member >= 0 && j.Member < Crew.High && Crew.Player[j.Member] != 0)
            Players.TryGetValue((byte)(Crew.Player[j.Member] - 1), out p);
        if (p != null && j.Shell && p.InShell)
            ShellReturn(p, lost: false, ""); // (the hopper's load goes back into the standing shell, or spills: HopperOff)
        if (j.Shell)
            j.Carry = 0; // (a shell's load was its hopper's: ShellReturn put it in the shell or spilt it, never again here)
        if (p != null && c != null && SandboxHands.Hand(p.Id).Seated && c.Parts.Length > 0 && SandboxHands.Hand(p.Id).SeatBody == c.Id
            && SandboxHands.Hand(p.Id).SeatPart < c.PartCount && c.DefOf(SandboxHands.Hand(p.Id).SeatPart).ShellRange > 0)
        {
            // Back in the Shell Control Chair (a trip cut short, a shell destroyed): up again, at its rack if it still works it, else by the chair.
            var home = Crew.State[j.Member] == CrewState.Station && CrewWorld.PartByTag(c, j.RackTag) is var rk and >= 0 ? rk : SandboxHands.Hand(p.Id).SeatPart;
            CrewStandAt(p, c, CrewWorld.PartFoot(c, home));
        }
        if (p != null && SandboxHands.Hand(p.Id).Tool == ConstructTool.GroundDrill)
            SandboxHands.Hand(p.Id).Tool = ConstructTool.Hands;
        if (c != null && j.Hook >= 0 && CrewWorld.PartByTag(c, j.RackTag) is var rack and >= 0)
            ToolRacks.Give(Constructs, c, rack, j.Hook); // (a miner who falls: the drill is found and hung back; the rack never runs dry)
        if (j.Carry > 0 && p != null && why != null)
            Constructs.SpillBundle(j.Mat, j.Carry, p.Position + GravityFrames.UpAt(p.Position), Vector3.Zero);
        if (why != null && j.Stage != MineStage.None)
            Console.WriteLine($"[crew] construct {j.Body}: {why} ({j.Stage}, carrying {j.Carry})");
        var keep = why == null && c != null;
        var (m, body) = (j.Member, j.Body);
        j = default;
        j.Member = -1;
        j.Hook = -1;
        if (keep)
            (j.Member, j.Body, j.Next) = (m, body, Crew.Tick + (long)(MineRest * World.TickRate)); // (the same miner, resting before its next trip)
    }

    /// <summary>A bodied miner steering over the ground towards <paramref name="to"/> (from <see cref="CrewMineBody"/>): there within <paramref name="near"/> m across; put there after <see cref="StuckSeconds"/> with no headway.</summary>
    private bool MineArrived(ref MineJob j, Player p, Vector3 to, float near)
    {
        var d = new Vector2(to.X - p.Position.X, to.Z - p.Position.Z).Length();
        if (d < near)
            return true;
        if (d < j.Best - 0.1f)
        {
            j.Best = d;
            j.Stuck = Crew.Tick;
            return false;
        }
        if (j.Stuck == 0)
            j.Stuck = Crew.Tick;
        if (Crew.Tick - j.Stuck < (long)(StuckSeconds * World.TickRate))
            return false;
        MinePutThere++;
        Console.WriteLine($"[crew] construct {j.Body}: its miner {Crew.Uid[j.Member]} stuck {d:0.0} m short ({j.Stage}): put there");
        var at = to;
        if (Constructs.Resources is { } f && j.Stage is MineStage.ToSpot or MineStage.Drill)
            at.Y = f.Ground(at.X, at.Z) + 0.05f;
        p.Teleport(at + Vector3.UnitY * 0.05f);
        return true;
    }

    /// <summary>
    /// A crew body's tick while its miner's trip is on the ground (from CrewBodyTick): the keys that walk it straight at the spot or back,
    /// still while it drills. False: not a miner off the post now.
    /// </summary>
    private bool CrewMineBody(Player p)
    {
        if (p.CrewSlot < 0 || _mineHigh == 0 || MineOf(p.CrewSlot) is not (var k and >= 0))
            return false;
        ref var j = ref _mine[k];
        if (!j.Bodied || j.Stage is not (MineStage.ToSpot or MineStage.Drill or MineStage.Back) || Constructs.Get(j.Body) is not { } c)
            return false;
        if (SandboxHands.Hand(p.Id).Seated)
            return false;
        var cmd = new InputCmd { Seq = p.LastInput.Seq + 1, SpeedPct = 75, RideBody = p.Sim.RideBody, Yaw = p.Yaw, Pitch = p.Pitch, Weapon = p.Weapon };
        if (j.Stage != MineStage.Drill)
        {
            var to = j.Stage == MineStage.ToSpot ? j.Spot : j.Shell && CrewWorld.PartByTag(c, j.BinTag) is var bin and >= 0 ? ConstructRender.PartCentre(c, bin) : c.ToWorld(j.Edge);
            var flat = new Vector3(to.X - p.Position.X, 0, to.Z - p.Position.Z);
            if (flat.LengthSquared() > 0.04f)
            {
                cmd.Yaw = MathF.Atan2(flat.X, -flat.Z);
                var local = Vector3.Transform(flat, Quaternion.Conjugate(c.Rot));
                cmd.RideYaw = MathF.Atan2(local.X, -local.Z);
                cmd.Buttons = Buttons.Forward;
                cmd.Pitch = 0;
                if (flat.LengthSquared() < 1.5f * 1.5f)
                    cmd.SpeedPct = 50;
            }
        }
        else
            cmd.Pitch = j.Shell ? -1.2f : -1.0f;
        ApplyCmd(p, cmd);
        return true;
    }

    /// <summary>A tool's line (allocates): miners at work and what they did.</summary>
    public string MineLine()
    {
        var on = 0;
        var stages = new System.Text.StringBuilder();
        for (var k = 0; k < _mineHigh; k++)
            if (_mine[k].Body != 0)
            {
                on++;
                stages.Append($" [{Crew.Uid[_mine[k].Member]} {_mine[k].Stage}{(_mine[k].Shell ? " shell" : "")}{(_mine[k].Bodied ? " bodied" : "")} carry {_mine[k].Carry}]");
            }
        return $"miners {on}:{stages}; trips {MineTrips}, tipped {MineTipped}, put there {MinePutThere}";
    }

    /// <summary>Every miner's trip ends (a new world).</summary>
    private void MineReset()
    {
        Array.Clear(_mine);
        _mineHigh = 0;
        MineTrips = MineTipped = MinePutThere = 0;
    }
}
