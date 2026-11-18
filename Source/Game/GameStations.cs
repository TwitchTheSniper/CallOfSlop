using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// Space stations as places (docs/sandbox/constructs/station.md; Karl, 2026-11-18: Caledon Highport "should be visible on the RTS
/// view as a station", and ships dock at its berths by hand and with the command view's Dock order). A station is a construct
/// built of station pieces (<see cref="Construct.IsStation"/>); its berths are its docking ports (a Docking Clamp, Hatch or Lock),
/// armed from the start (their "startOn"), so the Dock order (GameNavDock.cs) and a pilot flying in slowly are both held.
/// </summary>
public sealed partial class Game
{
    /// <summary>A station's name: the section its pieces are listed under (Caledon Highport), else "Station".</summary>
    public static string StationName(Construct c)
    {
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.Parts[i].D;
            if (d.IsStationPiece && d.Section.Length > 0)
                return d.Section;
        }
        return "Station";
    }

    /// <summary>Any peer: a station's berths (its docking ports that work) and how many of them hold no craft.</summary>
    public static int StationBerths(ConstructWorld world, Construct c, out int free)
    {
        var all = 0;
        free = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            if (p.Broken || !c.DefOf(i).IsDock)
                continue;
            all++;
            var held = ConstructCargo.DockTaken(world, c, i, null);
            if (!held)
                free++;
        }
        return all;
    }

    /// <summary>
    /// Host: can <paramref name="ship"/> be moored at a station berth of kind <paramref name="port"/>? As a dock catches a craft
    /// (ConstructCargo.Catchable), except that it may carry craft of its own (the Zero Day's Shuttle and Limpet): a berth holds a
    /// ship by mooring it, not by carrying it, so nothing is carried two deep.
    /// </summary>
    public bool StationMoorable(Construct station, Construct ship, ConstructPartDef port) =>
        ship != station && !ship.IsStation && ship.MooredTo == 0 && ship.CarriedBy == 0 && ship.PartCount > 0 && ship.SpaceShip != 0 && ship.SpaceAway == 0
        && !ship.Anchored && !ship.Pinned && !ship.Frozen && ship.GrabbedBy == 255 && Constructs.CarrierClock >= ship.DockAway
        && ship.Mass <= port.DockMaxMass && (ship.StabCount > 0 || ship.ThrusterCount > 0);

    /// <summary>
    /// Host: <paramref name="ship"/> (ship <paramref name="i"/> of the flight) moored at berth <paramref name="part"/> of a station:
    /// set in the middle of the berth's box, upright with it and its length along the box's, then held there in the station's frame
    /// by <see cref="StationMoorStep"/> until the pilot's key or a new order casts it off.
    /// </summary>
    public void StationMoor(SpaceFlight f, int i, Construct ship, Construct station, int part)
    {
        var (comNow, rotNow) = (ship.WorldCom, ship.Rot); // (a berth with a clamp arm draws it in from here: GameStationArms.cs)
        var centre = NavDockCentre(station, part, out var up, out _);
        ConstructCargo.Box(station, part, out _, out var half);
        station.WorldFrame(station.Parts[part].Seg, out var q, out _);
        // The box's long way, as near as it can be to the way the ship's long axis points now.
        var local = Rot24.Apply(station.Parts[part].Rot, Vector3.UnitY);
        var boxLong = MathF.Abs(local.X) < 0.5f && half.X >= half.Z || MathF.Abs(local.X) >= 0.5f && half.Y >= half.Z ? Vector3.UnitX : Vector3.UnitZ;
        if (MathF.Abs(local.Z) >= 0.5f)
            boxLong = half.X >= half.Y ? Vector3.UnitX : Vector3.UnitY;
        var longW = Vector3.Normalize(Vector3.Transform(boxLong, q));
        var size = ship.LocalMax - ship.LocalMin;
        var shipLong = size.X > size.Z ? Vector3.UnitX : Vector3.UnitZ;
        if (Vector3.Dot(Vector3.Transform(shipLong, ship.Rot), longW) < 0)
            longW = -longW;
        longW = Vector3.Normalize(longW - up * Vector3.Dot(longW, up));
        Vector3 x, z;
        if (shipLong == Vector3.UnitZ)
        {
            z = longW;
            x = Vector3.Cross(up, z);
        }
        else
        {
            x = longW;
            z = Vector3.Cross(x, up);
        }
        ship.Rot = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0, up.X, up.Y, up.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1)));
        ship.Pos = centre - Vector3.Transform(ship.Com, ship.Rot);
        ship.PrevPos = ship.Pos;
        ship.PrevRot = ship.Rot;
        ship.AngVel = station.AngVel;
        ship.NavThrottle = -1;
        ship.NavAim = Vector3.Zero;
        var inv = Quaternion.Conjugate(station.Rot);
        ship.MooredTo = station.Id;
        ship.MoorPart = (ushort)part;
        ship.MoorAt = Vector3.Transform(centre - station.Pos, inv);
        ship.MoorRot = Quaternion.Normalize(inv * ship.Rot);
        ship.MoorDraw = 1;
        if (StationBerthArm(station, part))
            StationDrawFrom(ship, station, comNow, rotNow);
        ship.AngVel = station.AngVel;
        StationMoorFlight(f, i, ship, station);
        if (ship.MoorDraw < 1)
            ship.Pos = comNow - Vector3.Transform(ship.Com, ship.Rot); // (it stays where the clamp took it, not in the berth, this tick)
        Constructs.Events.Add($"construct {station.Id}: the {station.DefOf(part).Name} moored construct {ship.Id}");
        Log($"[station] construct {ship.Id} moored at {StationName(station)} (construct {station.Id}), berth {part} ({station.DefOf(part).Name})");
    }

    /// <summary>Host: a moored ship let go (a new order, or the station or ship gone): it stays where it is, and the berth leaves it alone a while.</summary>
    public void StationCastOff(Construct ship)
    {
        if (ship.MooredTo == 0)
            return;
        Log($"[station] construct {ship.Id} cast off from construct {ship.MooredTo}");
        ship.MoorAway = ship.MooredTo; // (lying slow in the berth's box still, it isn't moored again until it has flown out of it)
        ship.MoorAwayPart = ship.MoorPart;
        ship.MooredTo = 0;
        ship.DockAway = Constructs.CarrierClock + ConstructCarrier.AwayTicks;
    }

    /// <summary>Host, each tick before the flight steps: moored ships take the station's motion (its flight's state, the berth's offset, its turn).</summary>
    private void StationMoorStep(SpaceFlight f)
    {
        if (++_stationScan >= StationScanEvery)
        {
            _stationScan = 0;
            StationMoorScan(f);
            StationMoorRecheck();
        }
        StationArmsStep(f); // (GameStationArms.cs)
        if (ServerTick % World.TickRate == 7)
            StationMarketTick(); // (GameStationMarket.cs)
        foreach (var c in Constructs.Bodies)
        {
            if (c.MooredTo == 0)
                continue;
            if (Constructs.Get(c.MooredTo) is not { Wreck: false, SpaceShip: not 0, SpaceAway: 0 } st || c.SpaceShip == 0 || c.SpaceAway != 0 || c.CarriedBy != 0
                || c.MoorPart >= st.PartCount || st.Parts[c.MoorPart].Broken || !st.DefOf(c.MoorPart).IsDock || f.Ships.IndexOfId(c.SpaceShip) is not (var i and >= 0))
            {
                StationCastOff(c); // (the station or its berth is gone, or the ship came into a sky)
                continue;
            }
            StationMoorFlight(f, i, c, st);
        }
    }

    /// <summary>Host: a moored ship's turn and flight state set from its station's, so the two step together.</summary>
    private void StationMoorFlight(SpaceFlight f, int i, Construct c, Construct st)
    {
        var j = f.Ships.IndexOfId(st.SpaceShip);
        if (j < 0)
            return;
        var pose = Space!.Bubble.PoseAt(f.Ships.Time);
        if (c.MoorDraw < 1)
            c.MoorDraw = MathF.Min(1, c.MoorDraw + c.MoorDrawRate * World.TickDt); // (the clamp arm drawing it in: GameStationArms.cs)
        StationMoorPose(c, out var moorAt, out var moorRot);
        c.Rot = Quaternion.Normalize(st.Rot * moorRot);
        c.AngVel = st.AngVel;
        var off = Vector3.Transform(moorAt, st.Rot) - Vector3.Transform(st.Com, st.Rot); // (from the station's centre of mass, as its flight goes)
        f.Ships.RelativeOf(j, out var primary, out var r, out var v);
        f.Ships.SetState(i, primary, r + pose.Rot.Rotate(Vec3d.From(off)), v + pose.Rot.Rotate(Vec3d.From(Vector3.Cross(st.AngVel, off))));
        f.Ships.Thrust[i] = f.Ships.Thrust[j];
        c.NavThrottle = -1;
    }

    /// <summary>
    /// Host: a moored ship's berth is a part index, and parts are renumbered when others come off: a ship no longer mostly inside its
    /// berth's box looks for the berth whose box holds it, else it is cast off.
    /// </summary>
    private void StationMoorRecheck()
    {
        foreach (var c in Constructs.Bodies)
        {
            if (c.MooredTo == 0 || Constructs.Get(c.MooredTo) is not { } st)
                continue;
            if (c.MoorDraw < 1 && c.MoorPart < st.PartCount && st.DefOf(c.MoorPart).IsDock)
                continue; // (still on its way in on the clamp arm: GameStationArms.cs)
            if (c.MoorPart < st.PartCount && st.DefOf(c.MoorPart).IsDock && ConstructCargo.MostlyInside(st, c.MoorPart, c))
                continue;
            var found = -1;
            for (var i = 0; i < st.PartCount && found < 0; i++)
                if (st.DefOf(i).IsDock && !st.Parts[i].Broken && ConstructCargo.MostlyInside(st, i, c))
                    found = i;
            if (found >= 0)
                c.MoorPart = (ushort)found;
            else
                StationCastOff(c);
        }
    }

    /// <summary>How often (ticks) the host looks for ships come slowly into an empty station berth by hand.</summary>
    public const int StationScanEvery = 6;
    private int _stationScan;

    /// <summary>
    /// Host: a ship flown by hand into an armed, empty berth's box and slow against it there is moored (as a dock takes a craft
    /// that settles in it). Bounded: the stations (a few) by their berths by the ships in space near them.
    /// </summary>
    private void StationMoorScan(SpaceFlight f)
    {
        foreach (var st in Constructs.Bodies)
        {
            if (!st.IsStation || st.Wreck || st.SpaceShip == 0 || st.SpaceAway != 0)
                continue;
            var reach = st.Radius + 60;
            for (var part = 0; part < st.PartCount; part++)
            {
                ref var p = ref st.Parts[part];
                var d = st.DefOf(part);
                if (!d.IsDock || !ConstructCargo.IsFrozen(p) || !ConstructPower.On(st, part))
                    continue;
                ConstructCargo.Box(st, part, out var mid, out var half);
                st.WorldFrame(p.Seg, out var q, out var t);
                var centre = t + Vector3.Transform(mid, q);
                foreach (var b in Constructs.Bodies)
                {
                    if (b.SpaceShip == 0 || b == st || b.MooredTo != 0 || Vector3.DistanceSquared(b.WorldCom, st.WorldCom) > reach * reach)
                        continue;
                    var r = half.Length() + b.Radius;
                    var away = b.MoorAway == st.Id && b.MoorAwayPart == part;
                    if (Vector3.DistanceSquared(b.WorldCom, centre) > r * r)
                    {
                        if (away)
                            b.MoorAway = 0; // (it has left the berth it was cast off from)
                        continue;
                    }
                    if (away || !StationMoorable(st, b, d) || NavOf(b.Id) is { Kind: NavKind.Dock })
                        continue; // (one under a Dock order is moored by its order: GameNavDock.cs)
                    if ((b.Vel - st.PointVel(b.WorldCom)).Length() > MathF.Max(NavDockSlow, d.DockSettle) || !ConstructCargo.MostlyInside(st, part, b) || !NavDockFree(Constructs, st, part, b))
                        continue;
                    if (f.Ships.IndexOfId(b.SpaceShip) is var i and >= 0)
                    {
                        if (NavOf(b.Id) is { } o)
                            NavEnd(o, b, "it was flown into a berth of " + StationName(st)); // (any order it had ends)
                        StationMoor(f, i, b, st, part);
                    }
                }
            }
        }
    }
}
