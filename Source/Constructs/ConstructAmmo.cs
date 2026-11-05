using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Ammo on constructs (docs/sandbox/CONSTRUCTS.md, "Ammo feeds"). A mounted gun's rounds are chosen by what feeds it, not by a part
/// on it: Ammo Boxes, Crates and Ammo Containers carry an ammo type (a setting), which is one of the weapon builder's Ammo parts,
/// so its stats and flags go through the same <see cref="BuildStats"/> stages. A gun is fed by what is bolted to it (the old way)
/// or by an ammo feed: a link (<see cref="RouteType.Feed"/>) from a store, through any number of Ammo Switches. A switch is an
/// input with a data wire choosing which of its feeds goes on to its one output. A gun takes its type from what it loads, at the
/// reload; the numbers change there (<see cref="ConstructWeapons.BuildGuns"/>), so nothing is worked out per shot.
/// A loaded feed is explosive: when a segment is cut, fire runs along it both ways (<see cref="ConstructWorld.StartFeedBurn"/>).
/// </summary>
public static class ConstructAmmo
{
    /// <summary>The ammo types: wire and save order (a box's setting is an index here): new ones go at the end. Each names a part in Parts/Ammo.</summary>
    public static readonly string[] Types =
    [
        "FMJ", "Armour Piercing", "Explosive Tip", "Dragon's Breath", "Hollow Point", "Slug", "Buckshot", "Frag", "Match Grade", "Subsonic", "Plasma", "Shock", "Sabot",
        "Thermobaric", "Depleted Core", "Pocket Sun", "Rubber Duck", "Bowling Ball", "Napalm Gel", "Barbed Head", "High Explosive Shell", "Siege Shell", "Smoke Shell", "Incendiary Shell",
    ];

    public const int RoundsPerUnit = 10; // a unit of bulk "ammo" in a container (Recycling.json) holds this many rounds
    public const string Material = "ammo";

    /// <summary>Does this part hold rounds a gun can draw (a box, a crate, an Ammo Container)?</summary>
    public static bool IsSource(ConstructPartDef d) => d.Role == PartRole.AmmoBox || d.AmmoStore;

    /// <summary>The type a source holds (an index in <see cref="Types"/>).</summary>
    public static int TypeOf(Construct c, int part)
    {
        var d = c.DefOf(part);
        var t = (int)ConstructConfig.Get(c.Parts[part], d, CfgKey.AmmoType, 0);
        return Math.Clamp(t, 0, Types.Length - 1);
    }

    /// <summary>What gun <paramref name="k"/> would load now (0 when nothing says otherwise).</summary>
    public static int TypeOfGun(ConstructWorld? world, Construct c, int k)
    {
        ref var g = ref c.Guns[k];
        if (g.FeedRoute >= 0 && g.FeedRoute < c.RouteCount && SourceOf(c, g.FeedRoute) is var s and >= 0)
            return TypeOf(c, s);
        return g.BoxCount > 0 && c.GunBoxes[k * Construct.BoxesPerGun] < c.PartCount ? TypeOf(c, c.GunBoxes[k * Construct.BoxesPerGun]) : 0;
    }

    /// <summary>The Ammo part of a type (the build's "ammo" socket), or null when its file is gone.</summary>
    public static PartDef? PartOf(int type) => Parts.Find(Types[Math.Clamp(type, 0, Types.Length - 1)]);

    private static int AmmoMaterial => RecyclingTable.Current.IdOf(Material);

    /// <summary>Rounds in a source.</summary>
    public static int RoundsIn(ConstructWorld? world, Construct c, int part)
    {
        if (part >= c.PartCount || !c.Parts[part].Working)
            return 0;
        var d = c.DefOf(part);
        if (d.Role == PartRole.AmmoBox)
            return c.Parts[part].Rounds;
        return world?.StoreOf(c, part) is { } st && st.Material == AmmoMaterial ? st.Amount * RoundsPerUnit : 0;
    }

    /// <summary>Takes up to <paramref name="want"/> rounds from a source. The rounds taken.</summary>
    public static int Take(ConstructWorld world, Construct c, int part, int want)
    {
        var d = c.DefOf(part);
        if (d.Role == PartRole.AmmoBox)
        {
            var take = Math.Min(want, c.Parts[part].Rounds);
            c.Parts[part].Rounds -= (ushort)take;
            world.NoteAmmo(c, part);
            return take;
        }
        if (world.StoreOf(c, part) is not { } st || st.Material != AmmoMaterial || st.Amount <= 0)
            return 0;
        var units = Math.Min((want + RoundsPerUnit - 1) / RoundsPerUnit, st.Amount);
        world.Withdraw(c, part, units);
        return Math.Min(want, units * RoundsPerUnit); // (a part-used unit's rounds are spent)
    }

    /// <summary>Which of a switch's feeds goes on: 0 for the first, by its data input (rounded, held at the last).</summary>
    public static int Selected(in CPart p, ConstructPartDef d)
    {
        var n = 0;
        foreach (var np in d.NamedPorts)
            if (np.Type == RouteType.Feed && np.In)
                n++;
        return Math.Clamp((int)MathF.Round(p.In), 0, Math.Max(0, n - 1));
    }

    /// <summary>The nth feed input's link byte on a switch.</summary>
    private static int InPort(ConstructPartDef d, int nth)
    {
        foreach (var np in d.NamedPorts)
            if (np.Type == RouteType.Feed && np.In && nth-- == 0)
                return np.Byte;
        return -1;
    }

    /// <summary>
    /// The store feed <paramref name="route"/> draws on now, back through its switches; -1 when it is cut, or a switch's chosen input
    /// has nothing on it. Bounded: a chain of 8 switches, each a walk of the links (asked at a reload, and a few times a second by an empty gun).
    /// </summary>
    public static int SourceOf(Construct c, int route)
    {
        for (var guard = 0; guard < 8 && route >= 0; guard++)
        {
            ref var r = ref c.Routes[route];
            if (!r.FedEnd || r.Source >= c.PartCount || !c.Parts[r.Source].Working)
                return -1;
            int src = r.Source;
            var d = c.DefOf(src);
            if (IsSource(d))
                return src;
            if (d.Role != PartRole.AmmoSwitch)
                return -1;
            var port = InPort(d, Selected(c.Parts[src], d));
            route = -1;
            for (var k = 0; k < c.RouteCount; k++)
                if (c.Routes[k].Type == RouteType.Feed && c.Routes[k].To == src && c.Routes[k].ToPort == port)
                {
                    route = k;
                    break;
                }
        }
        return -1;
    }

    /// <summary>Is there a loaded store behind this feed (a feed that can cook off)? Any input of a switch counts: rounds lie in all of them.</summary>
    public static bool Loaded(ConstructWorld world, Construct c, int route) => route >= 0 && route < c.RouteCount && c.Routes[route].Source < c.PartCount
        && (IsSource(c.DefOf(c.Routes[route].Source)) ? RoundsIn(world, c, c.Routes[route].Source) > 0 : c.Parts[c.Routes[route].Source].D.Role == PartRole.AmmoSwitch);
}

/// <summary>A cut feed burning along itself, a segment a step, both ways from the cut.</summary>
public struct CFeedBurn
{
    public ushort Body;
    public ushort Route;
    public short Up, Down; // the next segment each way (-1: that way is done)
    public float RunUp, RunDown; // metres left each way
    public int Ticks; // to the next step
}

public sealed partial class ConstructWorld
{
    public const int MaxFeedBurns = 16;

    private readonly CFeedBurn[] _feedBurns = new CFeedBurn[MaxFeedBurns];
    private int _feedBurnCount;
    private bool _feedBurning; // (a step's own cut starts nothing)

    /// <summary>Feed cook-offs seen (hand checks).</summary>
    public int FeedBlasts;

    /// <summary>Segment <paramref name="seg"/> of loaded feed <paramref name="route"/> was cut: it starts burning along the feed.</summary>
    public void StartFeedBurn(Construct c, int route, int seg)
    {
        if (_feedBurning || _feedBurnCount >= MaxFeedBurns || !ConstructAmmo.Loaded(this, c, route))
            return;
        var r = ConstructWeaponParts.Current;
        _feedBurns[_feedBurnCount++] = new CFeedBurn { Body = c.Id, Route = (ushort)route, Up = (short)(seg + 1), Down = (short)(seg - 1), RunUp = r.FeedCookRun, RunDown = r.FeedCookRun, Ticks = 1 };
    }

    /// <summary>Each tick with the guns: each burn's next segments go up.</summary>
    public void StepFeedBurns()
    {
        var rules = ConstructWeaponParts.Current;
        for (var k = _feedBurnCount - 1; k >= 0; k--)
        {
            ref var b = ref _feedBurns[k];
            if (--b.Ticks > 0)
                continue;
            b.Ticks = Math.Max(1, (int)(rules.FeedCookStep * World.TickRate));
            if (Get(b.Body) is not { } c || b.Route >= c.RouteCount || c.Routes[b.Route].Type != RouteType.Feed)
            {
                b.Up = b.Down = -1;
            }
            else
            {
                BurnStep(c, ref b, ref b.Up, ref b.RunUp, 1, rules);
                BurnStep(c, ref b, ref b.Down, ref b.RunDown, -1, rules);
            }
            if (b.Up < 0 && b.Down < 0)
                _feedBurns[k] = _feedBurns[--_feedBurnCount];
        }
    }

    private void BurnStep(Construct c, ref CFeedBurn b, ref short seg, ref float run, int way, ConstructWeaponParts.Rules rules)
    {
        if (seg < 0)
            return;
        ref var r = ref c.Routes[b.Route];
        if (seg > r.Count || run <= 0)
        {
            seg = -1;
            return;
        }
        var from = ConstructRoutes.Point(c, b.Route, seg - 1, out _);
        var to = ConstructRoutes.Point(c, b.Route, seg, out var pseg);
        run -= Vector3.Distance(from, to);
        _feedBurning = true;
        c.WorldFrame(pseg, out var q, out var t);
        ConstructWeapons.Explode(this, t + Vector3.Transform((from + to) * 0.5f, q), rules.FeedCookRadius, rules.FeedCookDamage, c, CFireKind.CookOff);
        if (IsLive(c) && b.Route < c.RouteCount)
            ConstructRoutes.Damage(this, c, b.Route, seg, ConstructRoutes.Health(RouteType.Feed) * 2f);
        _feedBurning = false;
        FeedBlasts++;
        seg = (short)(seg + way);
    }

    private void ClearFeedBurns() => _feedBurnCount = 0;
}
