using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Crews at work inside their ships (docs/sandbox/design/bot-brain.md, phase 4): engineers and loaders, server only. An NPC ship's drives break
/// down as a player's do (GameSandboxBreakdowns.cs), on its own clock while its crew is awake; its engineer, at the Engineer's Panel or the drive's
/// works, walks to the groaning part and sees to it in time, repairs a part that broke down, and mends a works piece shot out (no cost: the
/// crew's own spares). Dead or gone, nobody does, and the ship stays broken. A loader carries rounds from the fullest ammo box or magazine
/// to the emptiest one that feeds a gun, a load at a time. A slice of the crewed bodies a look, four looks a second.
/// </summary>
public sealed partial class Game
{
    /// <summary>Seconds an engineer takes at a part, after the walk: a warning seen to, a broken-down part repaired, a shot-out works piece mended; a loader's carry.</summary>
    public const float EngSeeTo = 3, EngRepair = 12, EngMend = 20, LoadEvery = 8;

    /// <summary>Rounds a loader carries at a time, at most.</summary>
    public const int LoadRounds = 200;

    private int _crewWorkAt;

    /// <summary>Server, each tick: a slice of the awake crews' engineers and loaders, and their ships' breakdown clocks.</summary>
    private void CrewWorkTick()
    {
        if (ServerTick % BdStep != 1 || Crew.ShipHigh == 0)
            return;
        var t = Breakdowns.Current;
        var clock = SandboxSurvivalOption && t.Problem.Length == 0;
        var n = Math.Min(CrewWorld.ShipsPerTick, Crew.ShipHigh);
        for (var k = 0; k < n; k++)
        {
            if (_crewWorkAt >= Crew.ShipHigh)
                _crewWorkAt = 0;
            if (Crew.Ships[_crewWorkAt++] is not { Tier: not CrewTier.Asleep, Faction: not 0 } s || Constructs.Get(s.Body) is not { Wreck: false, PartCount: > 0 } c)
                continue;
            if (clock && BdDue(c) && BreakdownShip(c))
            {
                c.BdTick = ServerTick;
                BreakdownStep(c, t); // (an NPC ship's own clock: its crew is aboard)
            }
            CrewEngineer(c, s);
            CrewLoader(c, s);
        }
    }

    /// <summary>The member at the ship's first station of <paramref name="kind"/>, there now (-1: none).</summary>
    private int CrewAt(CrewShip s, CrewStationKind kind)
    {
        for (var st = 0; st < s.StCount; st++)
            if (s.StKind[st] == kind && s.StMember[st] is var m and >= 0 && Crew.State[m] == CrewState.Station)
                return m;
        return -1;
    }

    private int CrewStationOf(CrewShip s, CrewStationKind kind)
    {
        for (var st = 0; st < s.StCount; st++)
            if (s.StKind[st] == kind)
                return st;
        return -1;
    }

    private void CrewEngineer(Construct c, CrewShip s)
    {
        if (CrewAt(s, CrewStationKind.Works) < 0)
        {
            s.EngPart = -1; // (nobody: whatever they were at waits)
            return;
        }
        var tick = Crew.Tick;
        if (s.EngPart >= 0)
        {
            if (tick < s.EngDone)
                return;
            int part = s.EngPart;
            s.EngPart = -1;
            if (part >= c.PartCount)
                return;
            ref var p = ref c.Parts[part];
            if (BreakdownPart(c) == part)
            {
                c.BdDef = 0;
                Console.WriteLine($"[crew] construct {c.Id}: its engineer saw to the {BreakdownWhere(c, part)} in time");
                BreakdownSend(c, part, BreakdownStage.Fixed, true);
            }
            else if (p.Broken && p.Health > 0 && BreakdownKind(p.D))
            {
                p.Broken = false;
                CrewMended(c);
                Console.WriteLine($"[crew] construct {c.Id}: its engineer repaired the {BreakdownWhere(c, part)}");
                BreakdownSend(c, part, BreakdownStage.Repaired, true);
            }
            else if (WorksWrecked(c, part))
            {
                p.Broken = false;
                p.Health = p.D.Health;
                CrewMended(c);
                Console.WriteLine($"[crew] construct {c.Id}: its engineer mended the {p.D.Name}");
            }
            return;
        }
        // The next job: a warning first, then a part broken down, then a works piece shot out.
        var job = BreakdownPart(c);
        var secs = EngSeeTo;
        if (job < 0 && BreakdownFailed(c) is var f and >= 0)
            (job, secs) = (f, EngRepair);
        if (job < 0)
            for (var k = 0; k < c.WorksPieceCount; k++)
                if (WorksWrecked(c, c.WorksPieces[k]))
                {
                    (job, secs) = (c.WorksPieces[k], EngMend);
                    break;
                }
        if (job < 0)
            return;
        var st = CrewStationOf(s, CrewStationKind.Works);
        var walk = st >= 0 ? Vector3.Distance(ConstructRender.PartCentre(c, s.StPart[st]), ConstructRender.PartCentre(c, job)) * 1.4f / CrewWorld.WalkSpeed : 0;
        s.EngPart = (short)job;
        s.EngDone = tick + (long)((walk + secs) * World.TickRate);
        Console.WriteLine($"[crew] construct {c.Id}: its engineer goes to the {BreakdownWhere(c, job)} ({walk + secs:0} s)");
    }

    private void CrewMended(Construct c)
    {
        c.Dirty = true;
        Constructs.StructureDirty.Add(c.Id);
        c.Wake();
    }

    private void CrewLoader(Construct c, CrewShip s)
    {
        if (Crew.Tick < s.LoadAt || CrewAt(s, CrewStationKind.Magazine) < 0)
            return;
        s.LoadAt = Crew.Tick + (long)(LoadEvery * World.TickRate);
        // The emptiest box that feeds a gun, under a third full, and the fullest other source.
        int low = -1, high = -1;
        float lowShare = 1f / 3;
        var most = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            if (!p.Working || p.D.Role != PartRole.AmmoBox)
                continue;
            var cap = ConstructAmmo.Capacity(p.D);
            if (cap <= 0)
                continue;
            var share = p.Rounds / (float)cap;
            if (share < lowShare)
                (low, lowShare) = (i, share);
        }
        if (low < 0)
            return;
        for (var i = 0; i < c.PartCount; i++)
            if (i != low && ConstructAmmo.RoundsIn(Constructs, c, i) is var r && r > most)
                (high, most) = (i, r);
        if (high < 0)
            return;
        ref var box = ref c.Parts[low];
        var room = ConstructAmmo.Capacity(box.D) - box.Rounds;
        var got = ConstructAmmo.Take(Constructs, c, high, Math.Min(LoadRounds, Math.Min(room, most / 2 + 1)));
        if (got <= 0)
            return;
        box.Rounds = (ushort)Math.Min(ushort.MaxValue, box.Rounds + got);
        Constructs.NoteAmmo(c, low);
        Console.WriteLine($"[crew] construct {c.Id}: its loader carried {got} rounds from the {c.DefOf(high).Name} to the {box.D.Name}");
    }

    /// <summary>A tool (allocates): the first crewed ship's breakdown clock runs out now; <paramref name="fail"/>: its part breaks down at once.</summary>
    public string CrewBreakFirst(bool fail)
    {
        for (var i = 0; i < Crew.ShipHigh; i++)
            if (Crew.Ships[i] is { Faction: not 0 } s && Constructs.Get(s.Body) is { } c && BreakdownShip(c))
            {
                BreakdownStart(c, Breakdowns.Current);
                var part = BreakdownPart(c);
                if (part < 0)
                    return $"construct {c.Id}: nothing to break down";
                if (fail)
                    BreakdownFail(c, part);
                return $"construct {c.Id}: the {BreakdownWhere(c, part)} {(fail ? "broke down" : "starts to go")}";
            }
        return "no NPC crewed ship that breaks down";
    }

    /// <summary>A tool (allocates): every member with job <paramref name="job"/> on the first crewed body dies.</summary>
    public string CrewKillJob(string job)
    {
        for (var i = 0; i < Crew.ShipHigh; i++)
            if (Crew.Ships[i] is { MemberCount: > 0 } s)
            {
                var n = 0;
                for (var k = s.MemberCount - 1; k >= 0; k--)
                    if (Crew.Job[s.Members[k]].ToString().Equals(job, StringComparison.OrdinalIgnoreCase) && Crew.State[s.Members[k]] != CrewState.Dead)
                    {
                        Crew.Kill(s.Members[k], this);
                        n++;
                    }
                return $"construct {s.Body}: {n} {job} killed; {Crew.Describe(s)}";
            }
        return "no crewed body";
    }
}
