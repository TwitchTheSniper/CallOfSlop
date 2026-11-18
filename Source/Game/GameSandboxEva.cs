using System.Numerics;

namespace CallOfSlop;

/// <summary>What a part is to the spacewalk rules ("eva" in its file; Source/PartKit/Generators/EvaParts.cs).</summary>
public enum EvaPart : byte
{
    None,
    Locker, // a Space Suit Locker: F puts a suit on or hangs it back up
    Dock, // an MMU Dock: a seat the pack rides on and refuels in
    Mmu, // the Manned Manoeuvring Unit itself, on a dock
}

/// <summary>
/// The spacewalk rules (Karl, 2026-11-17; docs/sandbox/EVA.md). <see cref="Player.Eva"/>'s bits: a space suit, and on it
/// the MMU. Vacuum is past the space bubble's edge round a body (where ships orbit) or wherever the air is under
/// <see cref="VacuumBelow"/> (open space, the top of a planetoid's sky), inside a ship as well as out (Karl: until there are pressurised rooms). There a person in no suit holds their breath for
/// <see cref="BreathSeconds"/>, then chokes; a suit's air lasts <see cref="SuitSeconds"/>. A robot shell needs no air, and
/// neither do NPC bodies (bots: the crews) for now. Shared by the server (the damage) and a joined client (its own HUD).
/// </summary>
public static class Eva
{
    public const byte Suit = 1, Mmu = 2; // Player.Eva's bits
    public const float VacuumBelow = 0.05f; // air thinner than this (share of sea level) can't be breathed
    public const float BreathSeconds = 20f, SuitSeconds = 30 * 60f; // breath held, a full suit's air
    public const float BreathBack = 5f, SuitRefill = 90f; // seconds of each back a second in air (a suit fills from the air or its dock)
    public const int ChokeDamage = 10; // a second, out of breath
    public const ushort MmuFull = 3600; // the pack's tank: twenty minutes of thrust (Karl, 2026-11-18: x20), in units of PilotGear.MmuBurnEvery ticks (MoveState.JetFuel while worn)
    public const float MmuLitres = 120f; // fuel a full tank takes from the ship (twenty times the old minute's 6 L)
    public const int MmuFillTicks = 4; // tank a tick on a fed dock: full in 15 s
    public const uint IssueTicks = 10 * World.TickRate; // a spawn this recent out in vacuum is given a suit (the ship starts, a world rejoined in orbit)

    /// <summary>Is the air at this point too thin to breathe: past the space bubble's edge round a star system's body (where ships orbit), or thinner than <see cref="VacuumBelow"/>?</summary>
    public static bool Vacuum(Vector3 at) =>
        GravityFrames.Point is { OrbitFrom: > 0 } pg && Vector3.DistanceSquared(at, pg.Center) > pg.OrbitFrom * pg.OrbitFrom || ConstructAir.Density(at) < VacuumBelow;

    /// <summary>Does this player breathe: a person, not a robot shell, not an NPC, alive, not flying in creative's noclip.</summary>
    public static bool Breathes(Player p) => p.Bot == null && !p.InShell && p.Alive && !p.Sim.Noclip;

    /// <summary>
    /// One tick of air for <paramref name="p"/>; <paramref name="shipAir"/>: aboard a pressurised ship (ShipAir.cs), whose air they
    /// breathe and whose oxygen fills their suit. True when they are out of breath this tick (the server hurts them once a second).
    /// <paramref name="suitFilled"/>: the seconds of suit air taken this tick (the server bills the ship for them).
    /// </summary>
    public static bool Step(Player p, bool shipAir, out float suitFilled)
    {
        var dt = World.TickDt;
        suitFilled = 0;
        if (!Breathes(p))
        {
            p.Breath = BreathSeconds;
            return false;
        }
        var suited = (p.Eva & Suit) != 0;
        var vacuum = !shipAir && Vacuum(p.Position);
        if (suited && !vacuum && p.SuitAir < SuitSeconds)
        {
            var was = p.SuitAir;
            p.SuitAir = MathF.Min(SuitSeconds, p.SuitAir + SuitRefill * dt);
            if (shipAir && Vacuum(p.Position))
                suitFilled = p.SuitAir - was; // (from the ship's oxygen; a planet's air is free)
        }
        if (!vacuum)
        {
            p.Breath = MathF.Min(BreathSeconds, p.Breath + BreathBack * dt);
            return false;
        }
        if (suited && p.SuitAir > 0)
        {
            p.SuitAir = MathF.Max(0, p.SuitAir - dt);
            p.Breath = MathF.Min(BreathSeconds, p.Breath + BreathBack * dt);
            return false;
        }
        p.Breath = MathF.Max(0, p.Breath - dt);
        return p.Breath <= 0;
    }

    /// <summary>The HUD's air line for the local player, or null when there is nothing to say.</summary>
    public static string? AirLine(Player p, bool shipAir)
    {
        if (!Breathes(p))
            return null;
        var vacuum = !shipAir && Vacuum(p.Position);
        if ((p.Eva & Suit) != 0)
        {
            var share = p.SuitAir / SuitSeconds;
            if (p.SuitAir <= 0 && vacuum)
                return p.Breath > 0 ? $"SUIT AIR GONE: {p.Breath:0} s of breath (get to air)" : "SUFFOCATING: get to air";
            return vacuum || share < 0.999f ? $"Suit air {share * 100:0}% ({p.SuitAir / 60:0} min)" + (shipAir && share < 0.999f ? ", filling from the ship" : "") : null;
        }
        if (!vacuum)
            return null;
        return p.Breath > 0 ? $"NO AIR: {p.Breath:0} s of breath (put on a space suit)" : "SUFFOCATING: put on a space suit";
    }
}

/// <summary>
/// The spacewalk gear's server side (docs/sandbox/EVA.md): the Space Suit Locker's F, the MMU Dock's (back in, the pack goes
/// back on the dock or you strap into the one there), its detach key (fly off with the pack), refuelling a docked pack from
/// the ship, breath and choking, and what a death does to the gear. Who wears what goes to every peer in the roster.
/// </summary>
public sealed partial class Game
{
    private readonly byte[] _evaChoke = new byte[256]; // ticks to the next choke's damage

    /// <summary>Server, F on a part: the spacewalk gear's turn. <paramref name="handled"/> false: not theirs, the plain use goes on (a dock: sitting in it).</summary>
    private string? SandboxEvaUse(Player p, ushort body, ushort part, out bool handled)
    {
        handled = false;
        if (Constructs.Get(body) is { } ac && part < ac.PartCount && ac.DefOf(part).Air != AirPart.None && p.Alive)
        {
            handled = true; // (a Life Support Unit or an Oxygen Tank: its reading, GameShipAir.cs)
            return Vector3.Distance(p.Eye, ConstructEdits.ReachPoint(ac, part, p.Eye)) > ConstructBuild.UseReach + 0.75f ? "too far away" : ShipAirUse(p, ac, part);
        }
        if (Constructs.Get(body) is not { } c || part >= c.PartCount || c.DefOf(part).Eva is not (EvaPart.Locker or EvaPart.Dock) || !p.Alive)
            return null;
        var d = c.DefOf(part);
        handled = true;
        if (Vector3.Distance(p.Eye, ConstructEdits.ReachPoint(c, part, p.Eye)) > ConstructBuild.UseReach + 0.75f)
            return "too far away";
        if (c.Parts[part].Broken)
            return $"the {d.Name} is wrecked";
        if (d.Eva == EvaPart.Locker)
        {
            if (p.InShell)
                return "a robot shell needs no space suit";
            if ((p.Eva & Eva.Mmu) != 0)
                return "dock the MMU first: back into an empty MMU Dock";
            p.Eva ^= Eva.Suit;
            if ((p.Eva & Eva.Suit) != 0)
            {
                // The locker fills the suit from the ship's air, or from a planet's; with neither it comes as it was hung up (Karl, 2026-11-18).
                var air = ShipAir.AirBody(Constructs, c);
                if (ShipAir.Pressurised(air) || !Eva.Vacuum(p.Position))
                {
                    if (air != null && Eva.Vacuum(p.Position) && !SandboxInfiniteParts)
                        ShipAir.Take(Constructs, air, (int)MathF.Ceiling((Eva.SuitSeconds - p.SuitAir) * ShipAir.SuitFillCost));
                    p.SuitAir = Eva.SuitSeconds;
                }
            }
            BroadcastRoster();
            SandboxHands.Notices.Add((p.Id, (p.Eva & Eva.Suit) != 0 ? $"Space suit on: its air lasts {p.SuitAir / 60:0} minutes in vacuum" : "Space suit hung back up"));
            Constructs.Events.Add($"construct {c.Id}: {p.Name} {((p.Eva & Eva.Suit) != 0 ? "put on" : "took off")} a space suit");
            return null;
        }
        // An MMU Dock.
        if ((p.Eva & Eva.Suit) == 0 && !p.InShell)
            return "put on a space suit first (a Space Suit Locker)";
        if (c.SeatOf(p.Id) == part)
            return null;
        var docked = DockedMmu(c, part);
        if ((p.Eva & Eva.Mmu) != 0)
        {
            if (docked >= 0)
                return "this dock already holds an MMU";
            if (!DockMmu(c, part, p.MmuFuel))
                return "the MMU won't go on the dock";
            p.Eva &= unchecked((byte)~Eva.Mmu);
            p.Sim.JetFuel = p.MmuFuel = 0;
            BroadcastRoster();
            Constructs.Events.Add($"construct {c.Id}: {p.Name} docked an MMU");
        }
        else if (docked < 0)
            return "this dock is empty: fit a Manned Manoeuvring Unit on it";
        handled = false; // (the plain use sits them in it: strapped in)
        return null;
    }

    /// <summary>The MMU on dock <paramref name="dock"/> (on its "mmu" socket), or -1.</summary>
    internal static int DockedMmu(Construct c, int dock)
    {
        for (var e = c.FirstLink(dock); e >= 0; e = c.NextLink(e))
        {
            var other = c.OtherEnd(e);
            if (other < c.PartCount && c.DefOf(other).Eva == EvaPart.Mmu && !c.Parts[other].Broken)
                return other;
        }
        return -1;
    }

    /// <summary>A pack goes onto the dock: a new MMU part on its "mmu" socket holding <paramref name="fuel"/>.</summary>
    private bool DockMmu(Construct c, int dock, ushort fuel)
    {
        if (ConstructParts.Find("Manned Manoeuvring Unit") is not { } def)
            return false;
        var dd = c.DefOf(dock);
        var ds = Array.FindIndex(dd.Sockets, s => s.Name == "mmu");
        var ms = Array.FindIndex(def.Sockets, s => s.Name == "mount");
        if (ds < 0 || ms < 0)
            return false;
        ref readonly var dp = ref c.Parts[dock];
        // The pack's mount meets the dock's socket, turned as the dock is.
        var at = dp.Pos + Rot24.Apply(dp.Rot, dd.Sockets[ds].At - def.Sockets[ms].At);
        var i = Constructs.AddPart(c, def, ConstructWorld.Quantise(at), dp.Rot, dp.Seg);
        if (i < 0)
            return false;
        Constructs.AddLink(c, dock, ds, i, ms);
        c.Parts[i].Rounds = fuel;
        Constructs.NoteAmmo(c, i);
        c.Wake();
        return true;
    }

    /// <summary>Server, the seat loop: <paramref name="p"/> sits in MMU dock <paramref name="seat"/>. The pack refuels; the detach key flies them off with it. True when they left.</summary>
    private bool SandboxEvaSeated(Player p, Construct c, int seat, ushort keys, ushort keysWere)
    {
        var mmu = DockedMmu(c, seat);
        if (mmu < 0)
            return false;
        ref var m = ref c.Parts[mmu];
        if (m.Rounds < Eva.MmuFull && SandboxInfiniteParts)
        {
            m.Rounds = Eva.MmuFull; // (creative: endless parts, endless fuel)
            Constructs.NoteAmmo(c, mmu);
        }
        else if (m.Rounds < Eva.MmuFull && c.ReachesFuel(seat) && c.Fuel + c.FluidFuel > 0)
        {
            var add = (ushort)Math.Min(Eva.MmuFillTicks, Eva.MmuFull - m.Rounds);
            c.UseFuel(add * Eva.MmuLitres / Eva.MmuFull);
            m.Rounds += add;
            if (m.Rounds == Eva.MmuFull || ServerTick % 30 == 0)
                Constructs.NoteAmmo(c, mmu);
        }
        if ((keys & ~keysWere & ConstructParts.KeyEject) == 0)
            return false;
        if ((p.Eva & Eva.Suit) == 0 && !p.InShell)
        {
            SandboxHands.Notices.Add((p.Id, "put on a space suit before you fly the MMU"));
            return false;
        }
        var fuel = m.Rounds;
        var dock = c.Parts[seat];
        Constructs.RemovePart(c, mmu); // (the pack comes off the dock with them)
        p.Eva |= Eva.Mmu;
        SandboxStand(p, teleport: true);
        p.Sim.JetFuel = p.MmuFuel = fuel;
        p.Sim.Gear = SandboxWorn(p);
        // A gentle push off the dock, forward out of it, with whatever the ship was doing: in its frame when they stand in it
        // (out in space the pack floats in the ship's frame: World.SimulateMmu), else in the world.
        var outOf = Rot24.Apply(dock.Rot, -Vector3.UnitZ);
        if (p.Sim.RideBody == c.Id && p.Sim.RideUp != Vector3.Zero && c.FrameOf(p.Sim.RideSeg) == c.FrameOf(dock.Seg))
            p.Sim.RideVel += outOf * 0.6f;
        else
        {
            c.WorldFrame(dock.Seg, out var q, out _);
            p.Sim.Velocity += Vector3.Transform(outOf, q) * 0.6f;
        }
        p.Sim.Grounded = false;
        BroadcastRoster();
        Constructs.Events.Add($"construct {c.Id}: {p.Name} flew off on an MMU ({fuel * 100 / Eva.MmuFull}% fuel)");
        return true;
    }

    /// <summary>Server, each tick for every player: breath, and the worn MMU's gear bit.</summary>
    private void SandboxEvaTick(Player p, double now)
    {
        var shipAir = OnShipAir(p); // (aboard a pressurised ship: GameShipAir.cs)
        if ((p.Eva & Eva.Suit) == 0 && p.Bot == null && !p.InShell && p.Alive && ServerTick - p.SpawnTick < Eva.IssueTicks && Eva.Vacuum(p.Position))
            SandboxEvaIssue(p); // (woken or respawned out in vacuum, as the ship starts do: in a suit, not choking)
        ref var choke = ref _evaChoke[p.Id];
        var choking = Eva.Step(p, shipAir, out var suitFilled);
        ShipAirBillSuit(p, suitFilled);
        if (!choking)
        {
            choke = 0;
            return;
        }
        if (choke > 0)
        {
            choke--;
            return;
        }
        choke = World.TickRate;
        if (DamagePlayer(p, p, Eva.ChokeDamage, false, SuicideKillId, now, DamageKind.Melee))
            ServerSystemChat($"{p.Name} suffocated", now);
    }

    /// <summary>Server: a person who comes into play out in vacuum wears a space suit, full.</summary>
    private void SandboxEvaIssue(Player p)
    {
        p.Eva |= Eva.Suit;
        p.SuitAir = Eva.SuitSeconds;
        p.Breath = Eva.BreathSeconds;
        BroadcastRoster();
    }

    /// <summary>Server, a death: the suit comes off, and a worn MMU floats where they died as a loose part.</summary>
    private void SandboxEvaDied(Player p)
    {
        if (p.Eva == 0)
            return;
        if ((p.Eva & Eva.Mmu) != 0 && ConstructParts.Find("Manned Manoeuvring Unit") is { } def
            && Constructs.Create(p.Position + new Vector3(0, 1f, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, -p.Yaw)) is { } loose)
        {
            Constructs.AddPart(loose, def, Vector3.Zero, 0);
            loose.Parts[0].Rounds = p.MmuFuel;
            loose.Vel = p.Sim.Velocity;
        }
        p.Eva = 0;
        p.Sim.JetFuel = p.MmuFuel = 0;
        BroadcastRoster();
    }
}

public sealed partial class Game
{
    private SbText _sbEva, _sbEvaDock, _sbShipAir;

    /// <summary>The HUD's spacewalk lines: the air, the MMU's fuel and keys; strapped into a dock, its lines alone (true).</summary>
    private bool SandboxEvaHud(Player me, in SandboxHand h, List<HudLine> lines, Vector4 white, Vector4 dim, Vector4 orange, Vector4 red)
    {
        if (h.Seated && Constructs.Get(h.SeatBody) is { } c && c.SeatOf(me.Id) is var seat and >= 0 && c.DefOf(seat).Eva == EvaPart.Dock)
        {
            var mmu = DockedMmu(c, seat);
            var fuel = mmu >= 0 ? c.Parts[mmu].Rounds * 100 / Eva.MmuFull : -1;
            var fed = SandboxInfiniteParts || c.ReachesFuel(seat) && c.Fuel + c.FluidFuel > 0;
            if (_sbEvaDock.Stale(fuel * 4 + (fed ? 1 : 0) + 2))
                _sbEvaDock.Text = mmu < 0 ? "MMU Dock: empty" : $"MMU fuel {fuel}%" + (fuel >= 100 ? ", full" : fed ? ", filling" : ", no fuel reaches the dock (pipe fuel to its Fuel in)");
            lines.Add(new HudLine(_sbEvaDock.Text, mmu >= 0 && !fed && fuel < 100 ? orange : white));
            lines.Add(new HudLine(mmu >= 0 ? SandboxKeys.EvaDock : SandboxKeys.GetOut, dim));
            return true;
        }
        var ship = Eva.Breathes(me) ? ShipAirOf(me) : null;
        if (ship != null)
        {
            // The ship's air (GameShipAir.cs): how long it lasts for everyone aboard. A joined client counts the people it sees aboard.
            ShipAir.Survey(ship);
            if (!IsServer)
            {
                var n = 0;
                foreach (var o in Players.Values)
                    if (o.Alive && !o.InShell && ShipAirOf(o) == ship)
                        n++;
                ship.AirAboard = (byte)Math.Min(255, n);
            }
            if (_sbShipAir.Stale(ship.AirUnits * 1024L + ship.AirAboard * 2 + (ship.AirWorks ? 1 : 0) + ((long)ship.AirMax << 32)))
                _sbShipAir.Text = ShipAir.Line(ship);
            lines.Add(new HudLine(_sbShipAir.Text, ship.AirUnits <= 0 ? red : ship.AirUnits * 4 <= ship.AirMax ? orange : white));
        }
        var shipAir = ShipAir.Pressurised(ship);
        var air = Eva.Breathes(me) && (me.Eva & Eva.Suit) != 0 | (!shipAir && Eva.Vacuum(me.Position));
        var mmuOn = (me.Eva & Eva.Mmu) != 0;
        if (!air && !mmuOn)
            return false;
        var key = (long)me.Eva + ((long)MathF.Ceiling(me.Breath) << 2) + ((long)MathF.Ceiling(me.SuitAir / 60) << 8) + ((long)(me.Sim.JetFuel * 100 / Eva.MmuFull) << 20) + (Eva.Vacuum(me.Position) ? 1L << 30 : 0) + (shipAir ? 1L << 31 : 0);
        if (_sbEva.Stale(key))
        {
            var line = Eva.AirLine(me, shipAir);
            var pack = mmuOn ? $"MMU fuel {me.Sim.JetFuel * 100 / Eva.MmuFull}%" : null;
            _sbEva.Text = line != null && pack != null ? $"{line}, {pack}" : line ?? pack ?? "";
        }
        if (_sbEva.Text.Length > 0)
            lines.Add(new HudLine(_sbEva.Text, me.Breath < Eva.BreathSeconds || me.SuitAir <= 0 && !shipAir && Eva.Vacuum(me.Position) ? red : (me.Eva & Eva.Suit) != 0 && me.SuitAir < 300 ? orange : white));
        if (mmuOn && !h.Seated)
            lines.Add(new HudLine(SandboxKeys.EvaFly, dim));
        return false;
    }
}
