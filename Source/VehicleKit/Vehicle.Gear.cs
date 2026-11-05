using System.Numerics;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// A vehicle's running gear, as a recipe a style lists: what's sprung how at each end. A recipe is whole because its choices go together: how far out an arm throws a wheel decides how wide
/// the body may be between the wheels and how low its floor (<see cref="Floor"/>). Front and rear needn't match. <see cref="Tag"/> adds a free-rolling axle behind the driven one, <see cref="Tandem"/> a second driven one that far behind it (a 6x6).
/// </summary>
public sealed record Gear(string Name, string FrontSpring, string FrontWheel, string RearSpring, string RearWheel, string Differential = "Wide Differential", float FrontWing = 0.5f, float Floor = 0.625f, string? Tag = null, string? TagWheel = null, float Tandem = 0);

public sealed partial class Vehicle
{
    /// <summary>The recipe this seed rolled from the style's light or heavy list (by <see cref="IsHeavy"/>).</summary>
    public Gear PickGear(params string[] only)
    {
        var list = IsHeavy ? Style.HeavyGear : Style.LightGear;
        var fits = only.Length == 0 ? list : list.Where(g => only.Contains(g.Name)).ToArray();
        return Dice.Fork("gear").Pick(fits.Length > 0 ? fits : throw new VehicleKitException($"style {Style.Name} has no gear named {string.Join(", ", only)}"));
    }

    /// <summary>Which of a spring's sockets bolts to a frame: a strut hangs from its upper side, everything else by its mount.</summary>
    private static string BoltBy(ConstructPartDef spring) => Array.Exists(spring.Sockets, s => s.Name == "side") ? "side" : "mount";

    /// <summary>An unpowered axle behind the differential at <paramref name="z"/>: a stub beam out from each tail rail, a spring and a wheel on it. Takes a third of the load off the driven axle and stretches the silhouette.</summary>
    public Vehicle TagAxle(float z, string spring, string wheel)
    {
        var at = RailZ(z);
        var wing = Def(Diff!.Def.Name).BoundsMax.X > 0.7f ? 0.5f : 0.25f;
        foreach (var side in new[] { -1f, 1f })
        {
            var beam = Put(BeamName(wing), new Vector3(side * (0.375f + wing * 0.5f), 0, at), Orient.Along(Vector3.UnitX), 0.03f);
            var tip = Array.FindIndex(beam.Def.Sockets, s => s.Name == "+z");
            C.SocketAt(beam.Index, tip, out var p, out _);
            Corner(beam, MathF.Abs(FromRest(p).X) > MathF.Abs(CentreOf(beam).X) ? "+z" : "-z", spring, wheel);
        }
        return this;
    }

    /// <summary>A Tow Hitch across the end of the tail rails: <see cref="BlueprintScript.Tow"/> couples to its ball.</summary>
    public Vehicle TowHitch()
    {
        Hitch = Onto(TailL[^1], "+z", "Tow Hitch", "-z0");
        return this;
    }

    public VPart? Hitch { get; private set; }

    /// <summary>A towed frame's drawbar: a beam forward from the middle of the front cross beam, a Tow Eye on its tip.</summary>
    public Vehicle Drawbar()
    {
        var bar = Stand($"{Beam} 1 m", NearestFacing(FrontCross!, -Vector3.UnitZ, new Vector3(0, 0, FrontAxleZ)), Orient.Along(Vector3.UnitZ), FrontCross!, "+z");
        Onto(bar, "-z", "Tow Eye", "+z");
        return this;
    }

    /// <summary>Two ammo crates of different types on the floor by a switch, a lever choosing, and a feed to <paramref name="gun"/>; more guns take the first crate's feed. A protected store and one fire door, the way a player would wire it.</summary>
    public Vehicle Feeds(Vector3 crateA, Vector3 crateB, string typeA, string typeB, VPart leverOn, string leverSocket, VPart gun, params VPart[] more) => Feeds(crateA, crateB, typeA, typeB, leverOn, leverSocket, null, gun, more);

    /// <summary>As above, with the first reel on the floor at <paramref name="reelAt"/>: for crates shut in a hutch, whose own tops have no room.</summary>
    public Vehicle Feeds(Vector3 crateA, Vector3 crateB, string typeA, string typeB, VPart leverOn, string leverSocket, Vector3? reelAt, VPart gun, params VPart[] more)
    {
        var a = Crate(crateA, typeA);
        var b = Crate(crateB, typeB);
        var sw = Onto(a, "front", "Ammo Switch 2", "bottom");
        Link("feed", a, sw, "Ammo out", "Ammo in 1", laid: true);
        Link("feed", b, sw, "Ammo out", "Ammo in 2", laid: true);
        (VPart On, string Socket)[] spots = [(a, "top"), (b, "top"), (b, "back"), (a, "back")];
        Belt(spots, sw, gun, reelAt);
        var lever = Onto(leverOn, leverSocket, "Lever", "-y");
        Wire(lever, sw);
        foreach (var g in more)
            Belt(spots, b, g);
        foreach (var g in (VPart[])[gun, .. more])
            for (var k = 0; k < C.GunCount; k++)
                if (C.Guns[k].Part == g.Index && C.Guns[k].Problem.Length > 0)
                    throw new VehicleKitException($"a fed gun: {C.Guns[k].Problem}");
        return this;
    }

    /// <summary>An ammo reel on <paramref name="on"/>, fed from <paramref name="source"/>, its belt plugged into a socket on the gun: it follows the gun however the turret turns, as a laid link can't past its flexible section's reach.</summary>
    private void Belt((VPart On, string Socket)[] spots, VPart source, VPart gun, Vector3? at = null)
    {
        var part = Vector3.Distance(CentreOf(spots[0].On), CentreOf(gun)) < 6f ? "Ammo Reel" : "Large Ammo Reel";
        var reel = at is { } floor ? Put(part, floor, Orient.Forward, 0.4f, null, "-y", onTop: true) : null;
        var why = "no spot for it";
        foreach (var (on, socket) in reel == null ? spots : [])
            try
            {
                reel = Onto(on, socket, part, "-y");
                break;
            }
            catch (VehicleKitException ex)
            {
                why = ex.Message; // (taken or in the way: the next spot)
            }
        if (reel == null)
            throw new VehicleKitException($"a gun's ammo reel: {why}");
        Link("feed", source, reel, "Ammo out", "Ammo in", laid: true);
        var inlet = OnAFace(gun, ["left", "right", "top", "back"], "Ammo Socket", Weapon ?? throw new VehicleKitException("no gun to feed"));
        Hose(reel, inlet);
    }

    private VPart Crate(Vector3 at, string type)
    {
        var c = Put("Ammo Crate", at, Orient.Forward, 0.2f, null, "bottom", onTop: true);
        Config(c, ("ammoType", Array.IndexOf(ConstructAmmo.Types, type)));
        return c;
    }

    /// <summary>Where a wheel is, in vehicle coordinates.</summary>
    public Vector3 WheelAt(VPart wheel) => CentreOf(wheel);

    /// <summary>A stance's floor: rails to floor by one sill or two.</summary>
    public static int SillsFor(float floor) => floor < 0.5f ? 1 : 2;
}
