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
        if (FedFrom(world, c, k, out var sc, out var s))
            return TypeOf(sc!, s);
        if (g.MagPart >= 0 && g.MagPart < c.PartCount && PackRounds(c.Parts[g.MagPart].Rounds) > 0)
            return PackType(c.Parts[g.MagPart].Rounds);
        return g.BoxCount > 0 && c.GunBoxes[k * Construct.BoxesPerGun] < c.PartCount ? TypeOf(c, c.GunBoxes[k * Construct.BoxesPerGun]) : 0;
    }

    /// <summary>Is this part a gun's magazine (a builder part of the "feed" category: its display name is Magazine)? A loaded one carries rounds.</summary>
    public static bool IsMagazine(ConstructPartDef d) => d.Role == PartRole.GunPart && Parts.Find(d.GunPartName) is { Category: PartCategory.Feed };

    /// <summary>A magazine's rounds and type in one ushort (a part's Rounds, a hand's MagPack): no new field on the wire or in a save.</summary>
    public static ushort Pack(int rounds, int type) => (ushort)(Math.Clamp(rounds, 0, MagMost) | Math.Clamp(type, 0, 31) << 11);

    public const int MagMost = 2047;

    public static int PackRounds(ushort pack) => pack & MagMost;

    public static int PackType(ushort pack) => pack >> 11;

    /// <summary>What a magazine holds: its builder file's "mag".</summary>
    public static int Capacity(ConstructPartDef d)
    {
        if (Parts.Find(d.GunPartName) is not { } p)
            return 0;
        var w = new WeaponDef { Name = "", Class = WeaponClass.AssaultRifle, Damage = 1, RangeM = 1, Rpm = 1, Mag = 30, Reload = 1, HipSpreadDeg = 1 };
        foreach (var e in p.Effects)
            if (e.Field.Name == nameof(WeaponDef.Mag))
                e.ApplyTo(w);
        return Math.Clamp(w.Mag, 1, MagMost);
    }

    /// <summary>Server: F with a magazine in hand on a box, crate or Ammo Container: filled from it, its type with it. Null when that isn't what this is, so F does what it did; else the line to show.</summary>
    public static string? FillInHand(ConstructWorld world, ref SandboxHand h, Vector3 eye, ushort body, int part)
    {
        if (ConstructParts.Get(h.Held) is not { } held || !IsMagazine(held) || world.Get(body) is not { } c || part >= c.PartCount || !IsSource(c.DefOf(part)))
            return null;
        if (Vector3.Distance(eye, ConstructRender.PartCentre(c, part)) > ConstructBuild.UseReach + 1f)
            return SandboxKeys.Line(held.Name, null, "too far from it");
        var pack = h.MagFor == held.Id ? h.MagPack : (ushort)0;
        var type = TypeOf(c, part);
        var have = PackRounds(pack);
        if (have > 0 && PackType(pack) != type)
            return SandboxKeys.Line(held.Name, $"{have} {Types[PackType(pack)]} in it", "another type: fit it and fire it off first");
        var got = Take(world, c, part, Capacity(held) - have);
        if (got <= 0 && have == 0)
            return SandboxKeys.Line(held.Name, null, $"the {c.DefOf(part).Name} is empty");
        (h.MagFor, h.MagPack) = (held.Id, Pack(have + got, type));
        return SandboxKeys.Line(held.Name, $"{have + got}/{Capacity(held)} {Types[type]}");
    }

    /// <summary>Server: a part went on a construct: a magazine has what was in it in the hand (creative: comes full).</summary>
    public static void Fitted(ConstructWorld world, Construct c, int i, ConstructPartDef d, ref SandboxHand h, bool creative)
    {
        if (!IsMagazine(d))
            return;
        var pack = h.MagFor == d.Id ? h.MagPack : creative ? Pack(Capacity(d), 0) : (ushort)0;
        h.MagFor = h.MagPack = 0;
        c.Parts[i].Rounds = pack;
        world.NoteAmmo(c, i);
    }

    /// <summary>Is an ammo feed brought to the gun part <paramref name="part"/> (or a merged receiver's other members): a link into it, or a reel's socket bolted to it? Then it needs no magazine.</summary>
    public static bool HasFeedLink(Construct c, int part, Span<bool> mine)
    {
        for (var r = 0; r < c.RouteCount; r++)
            if (c.Routes[r].Type == RouteType.Feed && c.Routes[r].To < c.PartCount && (c.Routes[r].To == part || mine[c.Routes[r].To]))
                return true;
        for (var l = 0; l < c.LinkCount; l++)
        {
            ref var nl = ref c.Links[l];
            var o = nl.A == part ? nl.B : nl.B == part ? nl.A : -1;
            if (o >= 0 && c.DefOf(o) is { Role: PartRole.Hose, HoseCarry: HoseCarry.Ammo, HoseEnd: HoseEnd.Inlet })
                return true;
        }
        return false;
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

    /// <summary>The store gun <paramref name="k"/>'s feed reaches now, if it has one and it is whole: by a link into the gun, or a socket bolted to it.</summary>
    public static bool FedFrom(ConstructWorld? world, Construct c, int k, out Construct? store, out int part)
    {
        ref var g = ref c.Guns[k];
        store = c;
        part = -1;
        if (g.FeedRoute >= 0 && g.FeedRoute < c.RouteCount)
            return Walk(world, ref store, g.FeedRoute, -1, out part);
        return g.FeedSocket >= 0 && g.FeedSocket < c.PartCount && Walk(world, ref store, -1, g.FeedSocket, out part);
    }

    /// <summary>
    /// The store a feed draws on now, back through switches and ammo reels: from link <paramref name="route"/>, or from the socket
    /// <paramref name="socket"/>. False when it is cut or stretched past a flexible section's limit, a reel is unplugged, or a switch's
    /// chosen input has nothing on it. Bounded: 8 steps, each a walk of the links (asked at a reload, and by a gun waiting for rounds).
    /// </summary>
    private static bool Walk(ConstructWorld? world, ref Construct? store, int route, int socket, out int part)
    {
        part = -1;
        var c = store!;
        for (var guard = 0; guard < 8; guard++)
        {
            int src;
            if (route >= 0)
            {
                ref var r = ref c.Routes[route];
                if (!r.FedEnd || r.Source >= c.PartCount || ConstructRoutes.FlexStopped(c, route) >= 0)
                    return false;
                src = r.Source;
            }
            else
                src = socket;
            if (!c.Parts[src].Working)
                return false;
            var d = c.DefOf(src);
            if (IsSource(d))
            {
                store = c;
                part = src;
                return true;
            }
            int to, port = -1;
            if (d.Role == PartRole.AmmoSwitch)
            {
                to = src;
                port = InPort(d, Selected(c.Parts[src], d));
            }
            else if (d.Role == PartRole.Hose && d.HoseCarry == HoseCarry.Ammo && d.HoseEnd == HoseEnd.Inlet && world != null)
            {
                var h = world.HoseAt(c, src);
                if (h < 0 || !world.Hoses[h].Plugged || world.Get(world.Hoses[h].BodyOut) is not { } rc || world.Hoses[h].PartOut >= rc.PartCount || !rc.Parts[world.Hoses[h].PartOut].Working)
                    return false;
                c = rc;
                to = world.Hoses[h].PartOut;
                for (var l = 0; l < c.LinkCount; l++)
                {
                    ref var nl = ref c.Links[l];
                    var o = nl.A == to ? nl.B : nl.B == to ? nl.A : -1;
                    if (o >= 0 && c.Parts[o].Working && IsSource(c.DefOf(o)))
                    {
                        store = c;
                        part = o; // (a box bolted to the reel)
                        return true;
                    }
                }
            }
            else
                return false;
            route = -1;
            for (var k = 0; k < c.RouteCount; k++)
                if (c.Routes[k].Type == RouteType.Feed && c.Routes[k].To == to && (port < 0 || c.Routes[k].ToPort == port))
                {
                    route = k;
                    break;
                }
            if (route < 0)
                return false;
        }
        return false;
    }

    /// <summary>Is there a loaded store behind this feed (a feed that can cook off)? Any input of a switch counts: rounds lie in all of them.</summary>
    public static bool Loaded(ConstructWorld world, Construct c, int route) => route >= 0 && route < c.RouteCount && c.Routes[route].Source < c.PartCount
        && (IsSource(c.DefOf(c.Routes[route].Source)) ? RoundsIn(world, c, c.Routes[route].Source) > 0 : c.Parts[c.Routes[route].Source].D.Role is PartRole.AmmoSwitch or PartRole.Hose);
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
