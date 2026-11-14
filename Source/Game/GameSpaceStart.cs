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
        var speed = (float)Math.Sqrt(e.System.Bodies[body].Mu / r);
        ship.Vel = fwd * speed;
        ship.PrevPos = ship.Pos;
        ship.Wake();
        _opShip = ship.Id;
        _opMiddle = _opGate = _opWake = _opCrash = from;
        Constructs.Events.Add($"opening: the ship ({script.Ship}) as construct {ship.Id}");
        Log($"[opening] the {script.Ship} is construct {ship.Id}, {(r - pl.Radius) / 1000:0.0} km up over {from.X:0},{from.Z:0}, going round at {speed:0} m/s");
        return true;
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
