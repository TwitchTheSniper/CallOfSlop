using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// A start aboard a ship in orbit (docs/story/STARTS.md "A ship start"; the rust bucket: Story/Starts/RustBucket.json). An
/// opening whose <c>"ship"</c> names a blueprint builds it out past the sky's edge over the start, going round at the speed
/// of a circular orbit, and everyone wakes standing in its frame at the opening's <c>"wake"</c> spots (the ship's own metres).
/// The next space tick makes it a ship of the system's flight (GameSpaceLaunch.cs), and the people in it go round with it,
/// walking its decks: a rider lives in the construct's frame whatever its speed (ConstructRideMove.cs), and nothing clamps a
/// rider to the sky. Server only; nothing new on the wire.
/// </summary>
public sealed partial class Game
{
    private ushort _opShip; // a ship start's ship (0: none): where everyone wakes, and late friends too

    /// <summary>Server, a fresh world's ship start: the ship in orbit over <paramref name="from"/>. False: it couldn't be built (then the start is as a castaway's).</summary>
    private bool OpeningShip(OpeningScript script, Vector3 from)
    {
        if (World.Map.Planetoid is not { } pl || GravityFrames.Point is not { } planet || ConstructBuild.FindBlueprint(script.Ship) is not { } bp
            || !StarSystems.ForMap(World.Map.Name, out var e, out var body))
        {
            Log($"[opening] no ship start on {World.Map.Name}: it needs a planetoid in a star system and the {script.Ship} blueprint");
            return false;
        }
        var centre = planet.Center;
        var up = from - centre;
        up = up.LengthSquared() > 1f ? Vector3.Normalize(up) : Vector3.UnitY;
        // Past the sky's edge (as GameSpaceLaunch.SpaceEdge has it, worked out here: the bubble is made on the first tick), with
        // room so the orbit's low point never brings it back in.
        var edge = MathF.Max(MathF.Max(World.Map.HalfX, World.Map.HalfZ), (float)pl.Radius + 500f);
        var r = MathF.Max((float)pl.Radius + script.Orbit, edge + 2 * SpaceEdgeHysteresis);
        var side = Vector3.Cross(up, Vector3.UnitY);
        if (side.LengthSquared() < 1e-4f)
            side = Vector3.Cross(up, Vector3.UnitX);
        var fwd = Vector3.Normalize(side); // (nose along the orbit; its up the planet's)
        var z = -fwd;
        var x = Vector3.Cross(up, z);
        var rot = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0, up.X, up.Y, up.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1)));
        var ship = ConstructBuild.Build(Constructs, bp, centre + up * r, rot, out var why);
        if (ship == null)
        {
            Log($"[opening] the {script.Ship} didn't build in orbit: {why}");
            return false;
        }
        ship.Rebuild();
        // A circular orbit's speed in the game's frame (the ground's slow turn left out: a metre or two a second off round).
        // Held (the opening's "hold": Karl, 2026-11-20, a start out in open space with two Mainframes): at rest, and its own flight
        // computer holds it there once space is up (OpeningHoldsTick), as company held "away" is; a driver at the controls takes it.
        var speed = script.Hold ? 0f : (float)Math.Sqrt(e.System.Bodies[body].Mu / r);
        ship.Vel = fwd * speed;
        if (script.Hold)
            _opHolds.Add(ship.Id);
        ship.PrevPos = ship.Pos;
        ship.Wake();
        _opShip = ship.Id;
        _opMiddle = _opGate = _opWake = _opCrash = from;
        Constructs.Events.Add($"opening: the ship ({script.Ship}) as construct {ship.Id}");
        Log($"[opening] the {script.Ship} is construct {ship.Id}, {(r - pl.Radius) / 1000:0.0} km up over {from.X:0},{from.Z:0}, {(script.Hold ? "held still" : $"going round at {speed:0} m/s")}");
        foreach (var co in script.Company)
            OpeningCompanion(co, centre, up, fwd, r, speed);
        foreach (var st in script.Stations)
            OpeningStation(st, centre, up, fwd, (float)pl.Radius, e.System.Bodies[body].Mu);
        return true;
    }

    /// <summary>
    /// Server, a ship start's station (the opening's <c>"stations"</c>; Karl, 2026-11-18: Caledon Highport about 40 km out over the
    /// world the start ship goes round): nobody's, uncrewed, on its own circular orbit <see cref="OpeningScript.Station.Orbit"/> m up,
    /// its ring level with the ground below and its docking spindle pointing down at the world. It drifts against the start's lower,
    /// faster orbit. A miss is logged, and the start goes on without it.
    /// </summary>
    private void OpeningStation(in OpeningScript.Station st, Vector3 centre, Vector3 up, Vector3 fwd, float radius, double mu)
    {
        if (ConstructBuild.FindBlueprint(st.Blueprint) is not { } bp)
            return;
        var r = radius + st.Orbit;
        var a = st.Ahead / r;
        var su = up * MathF.Cos(a) + fwd * MathF.Sin(a);
        var sf = fwd * MathF.Cos(a) - up * MathF.Sin(a);
        var z = -sf;
        var x = Vector3.Cross(su, z);
        var rot = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0, su.X, su.Y, su.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1)));
        var c = ConstructBuild.Build(Constructs, bp, centre + su * r, rot, out var why);
        if (c == null)
        {
            Log($"[opening] the station {st.Blueprint} didn't build in orbit: {why}");
            return;
        }
        c.Rebuild();
        c.Owner = 255; // (nobody's)
        c.StationTitle = st.Name;
        var speed = (float)Math.Sqrt(mu / r);
        c.Vel = sf * speed;
        c.PrevPos = c.Pos;
        c.Wake();
        Constructs.Events.Add($"opening: the station {StationName(c)} as construct {c.Id}");
        StationMarketOf(c);
        Log($"[opening] the station {StationName(c)} ({st.Blueprint}) is construct {c.Id}, {st.Orbit / 1000:0.0} km up, going round at {speed:0} m/s");
    }

    /// <summary>
    /// Server, a ship start's company (the opening's <c>"company"</c>; Karl, 2026-11-17: an NPC Zero Day of a neutral faction
    /// near the player's): a nobody's ship on the same circular orbit, <see cref="OpeningScript.Companion.Behind"/> m back along
    /// it (the same orbit, so the gap holds as they go round), crewed by the faction kind named. Its magazines come full and its
    /// banks charged, as every ship the game spawns (ConstructBuild). A miss is logged, and the start goes on without it.
    /// </summary>
    private void OpeningCompanion(in OpeningScript.Companion co, Vector3 centre, Vector3 up, Vector3 fwd, float r, float speed)
    {
        if (ConstructBuild.FindBlueprint(co.Ship) is not { } bp)
            return;
        var still = co.Away > 0;
        var a = still ? 0f : co.Behind / r; // (the angle back along the orbit)
        var cu = up * MathF.Cos(a) - fwd * MathF.Sin(a);
        var cf = fwd * MathF.Cos(a) + up * MathF.Sin(a);
        var z = -cf;
        var x = Vector3.Cross(cu, z);
        var rot = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0, cu.X, cu.Y, cu.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1)));
        var at = centre + cu * (r + co.Away); // (held still: straight out past the player's ship)
        var c = ConstructBuild.Build(Constructs, bp, at, rot, out var why);
        if (c == null)
        {
            Log($"[opening] the company's {co.Ship} didn't build in orbit: {why}");
            return;
        }
        c.Rebuild();
        c.Owner = 255; // (nobody's: the faction's)
        c.Vel = still ? Vector3.Zero : cf * speed;
        c.PrevPos = c.Pos;
        c.Wake();
        var faction = CrewPowers.Find(co.Faction);
        var crew = Crew.Crew(c, ShipAiMode.Strangers, faction: faction);
        if (Crew.Events.Count > 0)
            CrewLog();
        Constructs.Events.Add($"opening: the company's {co.Ship} ({co.Faction}) as construct {c.Id}");
        if (still)
        {
            _opHolds.Add(c.Id); // (its flight computer holds it there once space is up: OpeningHoldsTick)
            Log($"[opening] the company's {co.Ship} is construct {c.Id}, held still {co.Away / 1000:0.0} km out past the ship, crewed by {CrewPowers.Of(faction)?.Name ?? "nobody"} ({(crew != null ? Crew.Alive(crew) : 0)} aboard)");
        }
        else
            Log($"[opening] the company's {co.Ship} is construct {c.Id}, {co.Behind:0} m {(co.Behind >= 0 ? "behind" : "ahead")}, crewed by {CrewPowers.Of(faction)?.Name ?? "nobody"} ({(crew != null ? Crew.Alive(crew) : 0)} aboard)");
    }

    /// <summary>Host: company held still (the opening's <c>"away"</c>), waiting for its order to hold its point.</summary>
    private readonly List<ushort> _opHolds = new(4);

    /// <summary>
    /// Host, each space tick while any wait: a company ship held still gets a Go to order at the point it was built (its own
    /// flight computer holds it there against the planet's pull: NavPhase.Hold), once space is up. Not saved: a loaded world's
    /// ship coasts from where it was.
    /// </summary>
    private void OpeningHoldsTick(SpaceSession s)
    {
        for (var k = _opHolds.Count - 1; k >= 0; k--)
        {
            if (Constructs.Get(_opHolds[k]) is not { PartCount: > 0 } c)
            {
                _opHolds.RemoveAt(k);
                continue;
            }
            if (c.Dirty)
                c.Rebuild();
            var t = s.Flight!.Clock.Now;
            if (!NavState(c, t, out var pos, out _))
                continue;
            var body = s.Bubble.Body;
            s.System.StateOf(body, t, out var bp, out _);
            if (NavApply(s, c, NavKind.GoTo, body, pos - bp, 0, 255, "the opening", null, out _) is { } why)
                Log($"[opening] construct {c.Id} can't hold still: {why}");
            _opHolds.RemoveAt(k);
        }
    }

    /// <summary>Server: a player into the ship, standing in its frame at their wake spot (lying down, as the opening has them). False: no ship.</summary>
    private bool OpeningAboard(Player p)
    {
        if (Constructs.Get(_opShip) is not { PartCount: > 0 } c)
            return false;
        if (c.Dirty)
            c.Rebuild();
        var spots = OpScript.Wake;
        var at = c.ToWorld(spots.Length > 0 ? spots[p.Id % spots.Length] : c.Com);
        p.Teleport(at);
        ConstructRide.Join(ref p.Sim, c, 0, ConstructRide.UpFor(c, GravityFrames.UpAt(at)), Vector3.Zero);
        return true;
    }

    /// <summary>
    /// Server: can this player, seated in a ship in space, stand up in it? Only onto a deck of its own under its own roof (the
    /// ship's frame takes them, ConstructRide.ExitDeck), never beside the seat out in the void or on an open top.
    /// </summary>
    private bool SpaceCanStand(Player p)
    {
        ref var h = ref SandboxHands.Hand(p.Id);
        if (Constructs.Get(h.SeatBody) is not { } c || h.SeatPart >= c.PartCount)
            return false;
        int seat = h.SeatPart;
        var vel = ConstructRide.SurfaceVel(c, c.Parts[seat].Seg, ConstructEdits.SeatFeet(c, seat));
        if (!ConstructRide.ExitDeck(Constructs, c, seat, World.PlayerHeight, vel, out var deck, out _, out var feet))
            return false;
        // Inside its hull: its own roof over the spot (an open top, a tank or a frame, is no place to stand out there).
        var up = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, deck.Rot));
        return Constructs.Raycast(feet + up * 0.5f, up, 6f, out _, only: deck);
    }
}
