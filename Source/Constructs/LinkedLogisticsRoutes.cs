using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>One stop of a route: a Cargo Dock (its world, body and part) and when the ship leaves it (the user's answer 7: any combination, first met).</summary>
public struct LinkStop
{
    public string World, Name;
    public ushort Body, Part;
    public byte Flags; // LinkRoutes.LeaveTime, LeaveFill, LeaveDone
    public ushort Minutes; // LeaveTime: minutes waited at the dock
    public byte Fill; // LeaveFill: % of the ship's whole cargo room (or its payload limit)
}

public enum RoutePhase : byte
{
    Idle, // not started (or suspended)
    Flying, // on its autopilot to above the next dock (same planetoid)
    Docking, // over the dock, sinking until the clamp takes it
    Docked, // held: trading; leaves when a rule is met
    Leaving, // let go, climbing off the dock
    Orbit, // to the next stop's world through space (the command view's order: GameLinkRoutes.cs)
    Settling, // over a stop it can't land at (another world, or from space): the trade is settled at the dock's rates
    Done, // a one-off run is over
}

/// <summary>A Route Computer's route (saved on its part, the LLNK block): 2 to 4 stops, where it is, its last 8 trips' fuel.</summary>
public sealed class LinkRoute
{
    public const int MaxStops = 4, Trips = 8;
    public readonly LinkStop[] Stops = new LinkStop[MaxStops];
    public int N;
    public byte At; // the stop it flies to, or is at
    public RoutePhase Phase;
    public double Since; // when the phase began (LinkLedger.Now)
    public bool Suspended, OneOff, InOrbit, OrderSet; // (OrderSet: its leg's nav order is the route's own: GameSandboxLinked.cs)
    public float Alt; // Docking: metres over the dock it holds now
    public readonly float[] Fuel = new float[Trips];
    public int FuelN;
    public float DepartFuel = -1;
    public string Why = "";
    public uint SentBy; // a Dispatcher's id that sent it (a one-off run), 0: the player's own
    public bool FuelShort;
}

/// <summary>Which lines a docked ship takes on: those for its route's stops, or (no route: the player flies it) every line waiting here.</summary>
public readonly struct LinkRouteFilter(LinkRoute? route)
{
    public bool Takes(LinkLine l)
    {
        if (route == null || route.N == 0)
            return true;
        for (var i = 0; i < route.N; i++)
            if (route.Stops[i].World == l.CWorld && route.Stops[i].Body == l.CBody)
                return true;
        return false;
    }
}

/// <summary>
/// Standing routes (docs/sandbox/design/linked-logistics.md 6.2 to 6.4; the user's answers 5 and 7): a ship with a Route Computer and a
/// working Flight Stabilizer flies its stops in turn. A stop on the planetoid the ship is on is flown for real: its autopilot to 30 m over
/// the dock, then down at 2 m/s until the Docking Clamp takes it; held, it trades through its Cargo Port; when a leaving rule is met
/// (and its fuel will do: 25% over its last trips' most) the clamp lets go and it climbs off. A stop on another world is flown through
/// space by the command view's order (GameLinkRoutes.cs) to a low orbit there, and the stop's trade is settled on the ledger at the dock's
/// rates (cargo moved by the manifest), whether that world is open or closed; it isn't landed. A pilot touching the keys suspends it.
/// Once a second per Route Computer, no allocation unless a text changes.
/// </summary>
public static class LinkRoutes
{
    public const byte LeaveTime = 1, LeaveFill = 2, LeaveDone = 4;
    public const float Over = 30f, SinkRate = 2f, CatchLow = 2f; // metres over the dock it comes in at; m/s it sinks; the lowest it sinks to
    public const double CatchWait = 30;
    public const float LiftOff = 4f; // metres it climbs off a dock before it heads away
    public const double SpinUp = 0; // seconds its drive runs on the dock before the clamp lets go // seconds at its lowest with no clamp taking it: said on its card
    public const float PayloadSpare = 1.2f; // lift-off thrust kept spare: 20%

    /// <summary>A stop's default rules: full (95%) or 10 minutes or nothing more to do.</summary>
    public static uint DefaultRules => LeaveFill | LeaveTime | LeaveDone | 10u << 8 | 95u << 24;

    public static void Unpack(uint b, ref LinkStop s)
    {
        s.Flags = (byte)(b & 7);
        s.Minutes = (ushort)Math.Clamp((b >> 8) & 0xFFFF, 1, 600);
        s.Fill = (byte)Math.Clamp(b >> 24, 1, 100);
    }

    public static uint Pack(in LinkStop s) => s.Flags | (uint)s.Minutes << 8 | (uint)s.Fill << 24;

    public static LinkRoute? RouteOf(Construct ship)
    {
        if (ship.LinkParts == 0)
            return null;
        for (var i = 0; i < ship.PartCount; i++)
            if (ship.DefOf(i).LinkKind == LinkKind.Route && !ship.Parts[i].Broken && ship.Parts[i].Link?.Route is { } r)
                return r;
        return null;
    }

    public static LinkRouteFilter Filter(Construct ship) => new(RouteOf(ship));

    public static bool Flying(Construct ship) => RouteOf(ship) is { Suspended: false, Phase: RoutePhase.Flying or RoutePhase.Docking or RoutePhase.Leaving or RoutePhase.Orbit };

    /// <summary>The most a ship may weigh to lift off the strongest-gravity world on its route with 20% spare thrust (0: no limit known).</summary>
    public static float PayloadLimit(Construct ship)
    {
        var force = ConstructPhysics.NavDrive(ship, out _, out _, out _);
        if (!(force > 0))
            return 0;
        var g = 9.81f;
        if (RouteOf(ship) is { } r)
            for (var i = 0; i < r.N; i++)
                if (Maps.Find(r.Stops[i].World)?.Planetoid is { } pl)
                    g = MathF.Max(g, pl.Strength);
        return force / (g * PayloadSpare);
    }

    public static bool PayloadTakes(Construct ship, ConstructPartDef d)
    {
        var limit = PayloadLimit(ship);
        return limit <= 0 || ship.Mass + d.Mass <= limit;
    }

    // ---- Flying (server, once a second) ---------------------------------------------------------------------------------------

    public static void Step(ConstructWorld world, string key)
    {
        foreach (var (bid, part) in world.LinkState.Parts)
        {
            if (world.Get(bid) is not { } c || part >= c.PartCount || c.DefOf(part).LinkKind != LinkKind.Route || c.Parts[part].Link is not { } lp)
                continue;
            lp.Docks = DocksFor(world, key, lp.Docks);
            ref var p = ref c.Parts[part];
            var r = lp.Route;
            p.Out = r is { Suspended: false, N: >= 2 } && r.Phase is not (RoutePhase.Idle or RoutePhase.Done) ? 1 : 0;
            p.Out1 = r is { Phase: RoutePhase.Docked or RoutePhase.Settling } ? 1 : 0;
            p.Mem = r is { FuelShort: true } ? 1 : 0;
            if (r == null || r.N < 2 || r.Suspended || r.Phase is RoutePhase.Idle or RoutePhase.Done || !LinkedLogistics.Working(world, c, part))
                continue;
            if (Game.NavProblem(c) is { } np && c.CarriedBy == 0)
            {
                r.Why = $"can't fly: {np}";
                continue;
            }
            if (c.Driver != 255 && (c.DriveKeys & (Buttons.Forward | Buttons.Back | Buttons.Left | Buttons.Right | Buttons.Jump | Buttons.Crouch)) != 0)
            {
                Suspend(world, c, r, "suspended: a pilot took the controls - RESUME on its card");
                continue;
            }
            Fly(world, key, c, part, lp, r);
        }
    }

    private static void Suspend(ConstructWorld world, Construct c, LinkRoute r, string why)
    {
        r.Suspended = true;
        r.Why = why;
        if (c.CarriedBy == 0)
            ConstructFlight.NavRelease(world, c);
        Console.WriteLine($"[linked] {c.DisplayName()}'s route: {why}");
    }

    private static void Phase(LinkRoute r, RoutePhase p)
    {
        r.Phase = p;
        r.Since = LinkLedger.Now;
        r.InOrbit = false;
    }

    private static void Fly(ConstructWorld world, string key, Construct c, int part, LinkPart lp, LinkRoute r)
    {
        ref var stop = ref r.Stops[Math.Min(r.At, r.N - 1)];
        var dockBody = stop.World == key ? world.Get(stop.Body) : null;
        var local = dockBody != null && c.SpaceShip == 0 && stop.Part < dockBody.PartCount && dockBody.DefOf(stop.Part).LinkKind == LinkKind.Dock;
        var now = LinkLedger.Now;
        if ((r.Phase is RoutePhase.Flying or RoutePhase.Orbit || r.Phase == RoutePhase.Leaving && now - r.Since >= SpinUp) && c.CarriedBy != 0 && (dockBody == null || c.CarriedBy != dockBody.Id || r.Phase == RoutePhase.Leaving))
        {
            ConstructCarrier.LetGo(world, c); // (a clamp it left took it again before it climbed out of its pull: let go once more)
            ConstructFlight.NavEngage(world, c, FlightMode.Hover, default, ConstructPhysics.HoldShown(ConstructPhysics.HoldHeight(c.WorldCom) + LiftOff)); // (a gentle climb first: a craft near its lift limit that asks for all of it at once has nothing left to keep level)
            r.Why = "climbing off the dock";
            return;
        }
        switch (r.Phase)
        {
            case RoutePhase.Flying:
            {
                if (!local)
                {
                    Phase(r, RoutePhase.Orbit); // (another world, or out in space: through space, the command view's order)
                    r.Why = "";
                    break;
                }
                if (c.CarriedBy == dockBody!.Id)
                {
                    Arrive(world, c, r, RoutePhase.Docked);
                    break;
                }
                var at = LinkDocks.PartAt(dockBody, stop.Part);
                var up = ConstructPhysics.HoldUp(at);
                var off = c.WorldCom - at;
                off -= up * Vector3.Dot(off, up);
                ConstructFlight.NavEngage(world, c, FlightMode.Waypoint, at, ConstructPhysics.HoldShown(ConstructPhysics.HoldHeight(at) + Over));
                r.Why = $"flying to {stop.Name}: {off.Length():0} m";
                if (off.Length() < 4f && c.Vel.Length() < 3f)
                {
                    Phase(r, RoutePhase.Docking);
                    r.Alt = Over;
                }
                break;
            }
            case RoutePhase.Docking:
            {
                if (!local)
                {
                    Phase(r, RoutePhase.Flying);
                    break;
                }
                if (c.CarriedBy == dockBody!.Id)
                {
                    ConstructFlight.NavRelease(world, c);
                    Arrive(world, c, r, RoutePhase.Docked);
                    break;
                }
                var at = LinkDocks.PartAt(dockBody, stop.Part);
                r.Alt = MathF.Max(CatchLow, r.Alt - SinkRate);
                ConstructFlight.NavEngage(world, c, FlightMode.Waypoint, at, ConstructPhysics.HoldShown(ConstructPhysics.HoldHeight(at) + r.Alt));
                r.Why = r.Alt > CatchLow || now - r.Since < CatchWait ? $"docking at {stop.Name}" : $"waiting over {stop.Name}: no Docking Clamp takes it (is one armed, with this ship in its reach?)";
                break;
            }
            case RoutePhase.Docked:
            case RoutePhase.Settling:
            {
                if (r.Phase == RoutePhase.Docked && (!local || c.CarriedBy != dockBody!.Id))
                {
                    if (c.CarriedBy != 0)
                        ConstructCarrier.LetGo(world, c); // (held by another clamp than its stop's)
                    Phase(r, RoutePhase.Leaving); // (let go by someone else, or the dock went)
                    break;
                }
                if (r.Phase == RoutePhase.Settling)
                    Settle(world, key, c, lp, r, stop);
                if (!Leave(world, key, c, r, stop, out var why))
                {
                    r.Why = why;
                    break;
                }
                var need = FuelNeed(r);
                r.FuelShort = need > 0 && c.Fuel < need;
                if (r.FuelShort)
                {
                    r.Why = $"fuel short: needs about {need:0} l, has {c.Fuel:0}{(r.Phase == RoutePhase.Docked ? " - refuelling at the dock" : "")}";
                    break;
                }
                if (r.Phase == RoutePhase.Docked && ChargeWait(world, c))
                {
                    r.Why = $"charging at the dock: {ConstructPower.Share(c) * 100:0}%, leaves at {ChargeLeave * 100:0}%";
                    break;
                }
                Depart(world, c, r);
                break;
            }
            case RoutePhase.Leaving:
                if (c.CarriedBy == 0 && (now - r.Since >= SpinUp + 3 || dockBody == null || Vector3.Distance(c.WorldCom, LinkDocks.PartAt(dockBody, stop.Part)) > 6))
                    Phase(r, RoutePhase.Flying); // (off the dock: away at once, before a clamp's pull takes it back)
                else
                    r.Why = c.CarriedBy != 0 ? "spinning up on the dock" : "climbing off the dock";
                break;
            case RoutePhase.Orbit:
                if (local)
                {
                    Phase(r, RoutePhase.Flying); // (back on the planetoid of its stop)
                    break;
                }
                if (r.InOrbit)
                    Arrive(world, c, r, RoutePhase.Settling); // (Why is the nav order's phase meanwhile: GameSandboxLinked.cs)
                break;
        }
    }

    private static void Arrive(ConstructWorld world, Construct c, LinkRoute r, RoutePhase phase)
    {
        if (r.DepartFuel >= 0 && r.DepartFuel > c.Fuel)
        {
            Array.Copy(r.Fuel, 0, r.Fuel, 1, LinkRoute.Trips - 1);
            r.Fuel[0] = r.DepartFuel - c.Fuel;
            r.FuelN = Math.Min(LinkRoute.Trips, r.FuelN + 1);
        }
        r.DepartFuel = -1;
        Phase(r, phase);
        r.Why = phase == RoutePhase.Docked ? "docked" : "trading from orbit";
        Console.WriteLine($"[linked] {c.DisplayName()}: at stop {r.At + 1} ({r.Stops[r.At].Name}), {(phase == RoutePhase.Docked ? "docked" : "settled from orbit")}");
    }

    private static void Depart(ConstructWorld world, Construct c, LinkRoute r)
    {
        r.DepartFuel = c.Fuel;
        var last = r.At + 1 >= r.N;
        if (last && r.OneOff)
        {
            Phase(r, RoutePhase.Done);
            r.Why = "run done";
            Console.WriteLine($"[linked] {c.DisplayName()}: its run is done");
            return;
        }
        r.At = (byte)(last ? 0 : r.At + 1);
        if (r.Phase == RoutePhase.Docked)
        {
            // Its drive spins up while the clamp still holds it, then the clamp lets go (Leaving): let go first, a craft drops on its
            // hanging gear before its rotors bite, and may lie on its side.
            ConstructFlight.NavEngage(world, c, FlightMode.Hover, default, ConstructPhysics.HoldShown(ConstructPhysics.HoldHeight(c.WorldCom))); // (where it is: no climb asked of it while it is held, so nothing winds up)
            Phase(r, RoutePhase.Leaving);
        }
        else
            Phase(r, RoutePhase.Flying);
        r.Why = $"leaving for {r.Stops[r.At].Name}";
        Console.WriteLine($"[linked] {c.DisplayName()}: leaves for stop {r.At + 1} ({r.Stops[r.At].Name})");
    }

    /// <summary>The share of its batteries a ship held by a clamp that charges waits for before it leaves (its lift fans or rotors run on them).</summary>
    public const float ChargeLeave = 0.5f;

    /// <summary>Does a docked ship wait to charge? Only under its share, with batteries, held by a construct whose clamp charges and that has charge to give.</summary>
    private static bool ChargeWait(ConstructWorld world, Construct c)
    {
        if (c.ChargeCapacity <= 0 || ConstructPower.Share(c) >= ChargeLeave || world.Get(c.CarriedBy) is not { } holder || holder.Charge <= 1f)
            return false;
        var clamp = ConstructCargo.CarriedIn(holder, c);
        return clamp >= 0 && holder.Parts[clamp].D.DockCharge > 0 && ConstructPower.On(holder, clamp);
    }

    /// <summary>Fuel a leg needs: 25% over the most its last trips used (none known: no wait).</summary>
    private static float FuelNeed(LinkRoute r)
    {
        var most = 0f;
        for (var i = 0; i < r.FuelN; i++)
            most = MathF.Max(most, r.Fuel[i]);
        return most * 1.25f;
    }

    /// <summary>Is a leaving rule met? (unloading for this stop finishes first, unless the time is up). Why it stays when not.</summary>
    private static bool Leave(ConstructWorld world, string key, Construct c, LinkRoute r, in LinkStop stop, out string why)
    {
        var waited = LinkLedger.Now - r.Since;
        var unloading = 0;
        var loading = 0;
        if (LinkDocks.PortOf(c) is var port and >= 0 && c.Parts[port].Link is { } plp)
            foreach (var m in plp.Manifest)
                if (m.Aboard > 0 && LinkLedger.Line(m.Line) is { } l && l.CWorld == stop.World && l.CBody == stop.Body)
                    unloading += m.Aboard;
        var filter = new LinkRouteFilter(r);
        foreach (var l in LinkLedger.Lines)
            if (l.State is LinkLineState.Open or LinkLineState.Posted && l.Use == 0 && l.SWorld == stop.World && l.SBody == stop.Body && filter.Takes(l))
                loading += l.Ready + (int)l.Est;
        var fill = LinkDocks.ShipFill(world, c);
        var limit = PayloadLimit(c);
        var full = fill * 100 >= stop.Fill || limit > 0 && c.Mass >= limit * 0.98f;
        var timeUp = waited >= stop.Minutes * 60.0;
        if ((stop.Flags & LeaveTime) != 0 && timeUp)
        {
            why = "";
            return true;
        }
        if (unloading > 0)
        {
            why = $"unloading at {stop.Name}: {unloading} to go";
            return false;
        }
        if ((stop.Flags & LeaveFill) != 0 && full || (stop.Flags & LeaveDone) != 0 && loading == 0 || stop.Flags == 0)
        {
            why = "";
            return true;
        }
        var left = Math.Max(0, stop.Minutes * 60 - (int)waited);
        why = $"loading at {stop.Name}, {fill * 100:0}% full{((stop.Flags & LeaveFill) != 0 ? $", leaves at {stop.Fill}%" : "")}{((stop.Flags & LeaveTime) != 0 ? $" or in {left / 60}:{left % 60:00}" : "")}";
        return false;
    }

    /// <summary>A stop it can't land at: the trade is settled at the dock's rates, the cargo moved by the manifest (the user's answer 5).</summary>
    private static void Settle(ConstructWorld world, string key, Construct c, LinkPart lp, LinkRoute r, in LinkStop stop)
    {
        var port = LinkDocks.PortOf(c);
        if (port < 0 || c.Parts[port].Link is not { } plp)
        {
            r.Why = "no Cargo Port: nothing can be traded";
            return;
        }
        var moves = LinkDocks.MovesASecond;
        // Unloading: the dock's world gets the parts when it runs (at once if it is open): LinkLedger.Wake.
        for (var m = 0; m < plp.Manifest.Count && moves > 0; m++)
        {
            var ml = plp.Manifest[m];
            if (ml.Aboard == 0 || ml.Use != 0 || LinkLedger.Line(ml.Line) is not { } l || l.CWorld != stop.World || l.CBody != stop.Body || ConstructParts.Get(ml.Kind) is not { } d)
                continue;
            while (ml.Aboard > 0 && moves > 0 && LinkDocks.TakeFromShip(world, c, d))
            {
                ml.Delivered++;
                l.Delivered++;
                l.DeliverPending++;
                moves--;
            }
            plp.Manifest[m] = ml;
            plp.Version++;
            if (l.Outstanding == 0 && l.State == LinkLineState.Open)
            {
                l.State = LinkLineState.Closed;
                l.ClosedAt = LinkLedger.Now;
                l.Note = $"delivered to {stop.Name} (settled from orbit)";
            }
        }
        // Loading: what is ready there, real or estimated.
        var filter = new LinkRouteFilter(r);
        foreach (var l in LinkLedger.Lines)
        {
            if (moves <= 0)
                break;
            if (l.State is not (LinkLineState.Open or LinkLineState.Posted) || l.Use != 0 || l.SWorld != stop.World || l.SBody != stop.Body || !filter.Takes(l) || ConstructParts.Get(l.Kind) is not { } d)
                continue; // (a line posted to a closed world is still Posted: its estimate is what there is)
            while (moves > 0 && (l.Ready > 0 || l.Est >= 1) && PayloadTakes(c, d))
            {
                if (!LinkDocks.PutIntoShip(world, c, d))
                    return;
                if (l.Ready > 0)
                {
                    l.Ready--;
                    l.LedgerTaken++;
                }
                else
                {
                    l.Est -= 1;
                    l.EstLoaded++;
                }
                l.Loaded++;
                l.Ship = c.DisplayName();
                (l.ShipSeen, l.ShipWorld) = (LinkLedger.Now, key);
                LinkDocks.AddManifest(plp, l, 1);
                moves--;
            }
        }
    }

    // ---- What its card says ---------------------------------------------------------------------------------------------------

    private static readonly StringBuilder Sb = new(256);

    public static string Status(ConstructWorld world, Construct c, int part, LinkPart lp)
    {
        var sb = Sb.Clear();
        if (Game.NavProblem(c) is { } np && c.CarriedBy == 0)
            sb.Append("can't fly: ").Append(np).Append('\n');
        if (lp.Route is not { N: > 0 } r)
            return sb.Append("No route: add stops in the command view (the ship's card, ROUTE)").ToString().TrimEnd('\n');
        sb.Append("ROUTE").Append(r.OneOff ? " (one run)" : "").Append(": ");
        sb.Append(r.Suspended ? "SUSPENDED" : r.Phase switch
        {
            RoutePhase.Idle => "not started",
            RoutePhase.Done => "done",
            _ => r.Why,
        }).Append('\n');
        for (var i = 0; i < r.N; i++)
        {
            ref readonly var s = ref r.Stops[i];
            sb.Append(i == r.At ? "> " : "  ").Append(i + 1).Append(". ").Append(s.Name).Append(" (").Append(LinkLedger.Title(s.World)).Append(") leaves: ");
            var any = false;
            if ((s.Flags & LeaveFill) != 0)
                (any, _) = (true, sb.Append(s.Fill).Append("% full"));
            if ((s.Flags & LeaveTime) != 0)
                (any, _) = (true, sb.Append(any ? " or " : "").Append(s.Minutes).Append(" min"));
            if ((s.Flags & LeaveDone) != 0)
                (any, _) = (true, sb.Append(any ? " or " : "").Append("nothing more to do"));
            if (!any)
                sb.Append("at once");
            sb.Append('\n');
        }
        if (r.FuelN > 0)
        {
            sb.Append("Fuel a trip (last ").Append(r.FuelN).Append("):");
            for (var i = 0; i < r.FuelN; i++)
                sb.Append(' ').Append(r.Fuel[i].ToString("0"));
            sb.Append(" l\n");
        }
        var limit = PayloadLimit(c);
        if (limit > 0)
            sb.Append($"Payload limit {limit / 1000:0.0} t (now {c.Mass / 1000:0.0} t)\n");
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>A docked route ship's leaving line for the dock's screen, or "".</summary>
    public static string LeaveText(ConstructWorld world, Construct ship) =>
        RouteOf(ship) is { Phase: RoutePhase.Docked or RoutePhase.Settling } r ? "ROUTE: " + r.Why : "";

    /// <summary>The docks a route may stop at: this world's, and every linked network's (from the ledger), made again only when they change.</summary>
    private static LinkDockRef[] DocksFor(ConstructWorld world, string key, LinkDockRef[] old)
    {
        var list = DockScratch;
        list.Clear();
        foreach (var (bid, part) in world.LinkState.Parts)
            if (list.Count < 32 && world.Get(bid) is { } c && part < c.PartCount && c.DefOf(part).LinkKind == LinkKind.Dock)
                list.Add(new LinkDockRef { World = key, Body = bid, Part = part, Name = LinkedLogistics.NameOf(c, part) });
        foreach (var e in LinkLedger.Ends.Values)
            if (list.Count < 32 && e.World != key && e.Report.HasDock && e.Report.DockPart != ushort.MaxValue)
            {
                var dup = false;
                foreach (var d in list)
                    dup |= d.World == e.World && d.Body == e.Body && d.Part == e.Report.DockPart;
                if (!dup)
                    list.Add(new LinkDockRef { World = e.World, Body = e.Body, Part = e.Report.DockPart, Name = e.Name });
            }
        if (list.Count == old.Length)
        {
            var same = true;
            for (var i = 0; i < old.Length && same; i++)
                same = old[i].World == list[i].World && old[i].Body == list[i].Body && old[i].Part == list[i].Part && old[i].Name == list[i].Name;
            if (same)
                return old;
        }
        return [.. list];
    }

    private static readonly List<LinkDockRef> DockScratch = new(32);

    // ---- What a player does (the command view's ROUTE) ---------------------------------------------------------------------

    public static string? Act(ConstructWorld world, Construct c, int part, LinkPart lp, LinkOp op, uint a, uint b, string text)
    {
        var r = lp.Route;
        switch (op)
        {
            case LinkOp.RouteClear:
                lp.Route = null;
                if (c.CarriedBy == 0)
                    ConstructFlight.NavRelease(world, c);
                lp.Version++;
                return null;
            case LinkOp.RouteAdd:
            {
                r = lp.Route ??= new LinkRoute();
                if (r.N >= LinkRoute.MaxStops)
                    return $"a route has at most {LinkRoute.MaxStops} stops";
                var body = (ushort)(a >> 16);
                var dp = (ushort)(a & 0xFFFF);
                LinkDockRef? found = null;
                foreach (var d in lp.Docks)
                    if (d.World == text && d.Body == body && d.Part == dp)
                        found = d;
                if (found is not { } dock)
                    return "that Cargo Dock isn't known here (on this planetoid, or linked to it)";
                var s = new LinkStop { World = dock.World, Body = dock.Body, Part = dock.Part, Name = dock.Name };
                Unpack(b == 0 ? DefaultRules : b, ref s);
                r.Stops[r.N++] = s;
                lp.Version++;
                return null;
            }
            case LinkOp.RouteStart:
                if (r is not { N: >= 2 })
                    return "a route needs two stops";
                if (r.Phase is RoutePhase.Idle or RoutePhase.Done)
                {
                    r.At = 0;
                    Phase(r, c.CarriedBy != 0 ? RoutePhase.Docked : RoutePhase.Flying);
                }
                r.Suspended = false;
                r.Why = "";
                lp.Version++;
                Console.WriteLine($"[linked] {c.DisplayName()}'s route started: {r.N} stops");
                return null;
            case LinkOp.RouteStop:
                if (r == null)
                    return "it has no route";
                Suspend(world, c, r, "suspended - RESUME on its card");
                lp.Version++;
                return null;
            case LinkOp.RouteRules:
                if (r == null || a >= r.N)
                    return "no such stop";
                Unpack(b, ref r.Stops[a]);
                lp.Version++;
                return null;
        }
        return "a bad action";
    }

    // ---- Saves --------------------------------------------------------------------------------------------------------------

    public static void Save(SaveWriter w, LinkRoute? r)
    {
        if (r == null)
        {
            w.U8(0);
            return;
        }
        w.U8((byte)r.N);
        for (var i = 0; i < r.N; i++)
        {
            ref readonly var s = ref r.Stops[i];
            w.Str(s.World).Str(s.Name).U16(s.Body).U16(s.Part).U32(Pack(s));
        }
        w.U8(r.At).U8((byte)r.Phase).U8((byte)((r.Suspended ? 1 : 0) | (r.OneOff ? 2 : 0))).U32(r.SentBy).F32(r.DepartFuel);
        w.U8((byte)r.FuelN);
        for (var i = 0; i < r.FuelN; i++)
            w.F32(r.Fuel[i]);
    }

    public static LinkRoute? Load(SaveReader rd)
    {
        int n = rd.U8();
        if (n == 0)
            return null;
        var r = new LinkRoute();
        for (var i = 0; i < n; i++)
        {
            var s = new LinkStop { World = rd.Str(), Name = rd.Str(), Body = rd.U16(), Part = rd.U16() };
            Unpack(rd.U32(), ref s);
            if (r.N < LinkRoute.MaxStops)
                r.Stops[r.N++] = s;
        }
        r.At = (byte)Math.Min(rd.U8(), Math.Max(0, r.N - 1));
        r.Phase = (RoutePhase)rd.U8();
        var f = rd.U8();
        (r.Suspended, r.OneOff) = ((f & 1) != 0, (f & 2) != 0);
        r.SentBy = rd.U32();
        r.DepartFuel = rd.F32();
        r.FuelN = Math.Min((int)rd.U8(), LinkRoute.Trips);
        for (var i = 0; i < r.FuelN; i++)
            r.Fuel[i] = rd.F32();
        if (r.Phase is RoutePhase.Docking or RoutePhase.Leaving)
            r.Phase = RoutePhase.Flying; // (a load: it takes its leg up again from the top)
        r.Since = LinkLedger.Now;
        return r;
    }
}
