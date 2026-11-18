using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// Host: station berth clamp arms (ConstructClampArms.cs; Karl, 2026-11-18). A ship that comes slowly within an arm's reach of its
/// free berth, at the end of the approach lane or flown by hand, is reached for; when the head touches the hull the ship is taken:
/// its order ends (docked), its drives and its own flight stop, and the arm draws it in to its berth pose over a few seconds
/// (<see cref="StationDrawSpeed"/>). Moored, it is posed from the station each tick and nothing of its own moves it; a craft it
/// lets go (a Shuttle) flies as normal.
/// </summary>
public sealed partial class Game
{
    /// <summary>How fast the arm draws a ship in (m/s, eased at both ends), and the shortest and longest draw (s).</summary>
    public const float StationDrawSpeed = 3f, StationDrawMin = 3f, StationDrawMax = 20f;

    private readonly List<int> _armTouched = new(16);

    /// <summary>Does this berth of a station have a clamp arm (so a ship moored there is drawn in, not set down at once)?</summary>
    public static bool StationBerthArm(Construct st, int berth)
    {
        if (ConstructClampArms.Of(st) is not { } set)
            return false;
        for (var k = 0; k < set.N; k++)
            if (set.Arms[k].Berth == berth)
                return true;
        return false;
    }

    /// <summary>Host, each tick: every station's arms stepped; an arm that touched a ship takes it.</summary>
    private void StationArmsStep(SpaceFlight f)
    {
        foreach (var st in Constructs.Bodies)
        {
            if (!st.IsStation || st.Wreck || st.SpaceShip == 0 || st.SpaceAway != 0 || st.ClampArms is { N: 0, PartCount: var pc } && pc == st.PartCount)
                continue;
            _armTouched.Clear();
            ConstructClampArms.Step(Constructs, st, World.TickDt, _armTouched, host: true);
            if (st.ClampArms != null)
                st.ClampArms.HostStepped = Environment.TickCount64;
            foreach (var k in _armTouched)
                StationArmTake(f, st, st.ClampArms!.Arms[k]);
        }
    }

    private void StationArmTake(SpaceFlight f, Construct st, in ClampArm a)
    {
        if (Constructs.Get(a.Ship) is not { } ship || a.Berth >= st.PartCount || st.Parts[a.Berth].Broken)
            return;
        var d = st.DefOf(a.Berth);
        if (!d.IsDock || !StationMoorable(st, ship, d) || !NavDockFree(Constructs, st, a.Berth, ship) || !BerthFits(st, a.Berth, ship, out _) || f.Ships.IndexOfId(ship.SpaceShip) is not (var i and >= 0))
        {
            StationArmRefused(st, a.Berth, ship, d);
            return;
        }
        Log($"[station] the clamp arm of berth {a.Berth} of {StationName(st)} took construct {ship.Id}, {Vector3.Distance(ship.WorldCom, st.WorldCom):0} m from its middle");
        if (NavOf(ship.Id) is { } o)
            NavEnd(o, ship, $"it has docked with {StationName(st)}", docked: o.Kind == NavKind.Dock);
        StationMoor(f, i, ship, st, a.Berth);
    }

    /// <summary>Host: the head touched a ship its berth won't take: logged once, and the arm lets go of it until it leaves reach.</summary>
    private void StationArmRefused(Construct st, int berth, Construct ship, ConstructPartDef d)
    {
        var set = st.ClampArms!;
        for (var k = 0; k < set.N; k++)
        {
            ref var arm = ref set.Arms[k];
            if (arm.Berth != berth || arm.Ship != ship.Id)
                continue;
            arm.Refused = ship.Id;
            arm.Ship = 0;
        }
        var why = !d.IsDock ? "not a berth" : ship.Mass > d.DockMaxMass ? $"{ship.Mass:0} kg, over the berth's {d.DockMaxMass:0} kg" : !StationMoorable(st, ship, d) ? "not moorable now (anchored, held, frozen or just cast off)" : !NavDockFree(Constructs, st, berth, ship) ? "the berth is taken" : !BerthFits(st, berth, ship, out _) ? "too big for the berth" : "not in the flight";
        Log($"[station] the clamp arm of berth {berth} of {StationName(st)} let go of construct {ship.Id}: {why}");
    }

    /// <summary>Host: a ship just moored at a berth with an arm starts where it is and is drawn in (StationMoor sets where it ends).</summary>
    private static void StationDrawFrom(Construct ship, Construct station, Vector3 comNow, Quaternion rotNow)
    {
        var inv = Quaternion.Conjugate(station.Rot);
        ship.MoorFromAt = Vector3.Transform(comNow - station.Pos, inv);
        ship.MoorFromRot = Quaternion.Normalize(inv * rotNow);
        var secs = Math.Clamp(Vector3.Distance(ship.MoorFromAt, ship.MoorAt) / StationDrawSpeed, StationDrawMin, StationDrawMax);
        ship.MoorDraw = 0;
        ship.MoorDrawRate = 1f / secs;
    }

    /// <summary>A moored ship's place and turn in its station's frame now: on its way in while the arm draws it, else its berth's.</summary>
    private static void StationMoorPose(Construct c, out Vector3 at, out Quaternion rot)
    {
        if (c.MoorDraw >= 1)
        {
            (at, rot) = (c.MoorAt, c.MoorRot);
            return;
        }
        var s = c.MoorDraw * c.MoorDraw * (3 - 2 * c.MoorDraw); // (eased: no jolt when the clamp takes it, none as it seats)
        at = Vector3.Lerp(c.MoorFromAt, c.MoorAt, s);
        rot = Quaternion.Normalize(Quaternion.Slerp(c.MoorFromRot, c.MoorRot, s));
    }
}
