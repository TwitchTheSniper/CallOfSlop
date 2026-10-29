using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombies use reach" (board item dev-use-height-limit-uh01; Source/Game/ZombiesReach.cs): the use key
/// reaches a thing only in 3D (the eye within a head's height of its height band) and in sight (no wall or floor between).
/// <list type="bullet">
/// <item>Every Zombies map: every machine, wall buy, casino table, trader spot, salvage pile, window, door (from both
/// sides) and map logic button or lever can be used from somewhere on its own floor, so the rule left nothing out of
/// reach.</item>
/// <item>Ashgrave Cathedral: a machine is used from in front on its floor, but not from the floor above or below at the
/// same x/z (no prompt, and a use the server is told is held there buys nothing), nor through a wall.</item>
/// </list>
/// </summary>
public static partial class SelfTest
{
    private readonly record struct ReachThing(string What, ZUseKind Kind, int Index, Vector3 At, float BandLo, float BandHi, float Range, Box? DoorBox, Vector3 Side, Vector3 OtherSide)
    {
        /// <summary>A map logic button or lever (its own 3D reach and hot spot).</summary>
        public bool ZLogic => Kind == ZUseKind.None;
    }

    private static void CheckZombiesUseReach()
    {
        var now = 0.0;
        Map? cathedral = null;
        List<(ReachThing T, Vector3 Stand)>? fronts = null;
        List<(ReachThing T, Vector3 Stand)>? walled = null;
        foreach (var map in Maps.All)
        {
            if (map.Zombies is not { } zm)
                continue;
            var (f, w) = ReachSweep(map, zm);
            if (map.Name == CathedralName)
            {
                cathedral = map;
                fronts = f;
                walled = w;
            }
        }
        if (cathedral is not { Zombies: not null } || fronts == null || walled == null)
        {
            Fail($"Zombies use reach: '{CathedralName}' is missing");
            return;
        }

        // A machine with a floor above or below the spot it's used from (the Cathedral has six levels).
        World.SetMap(cathedral);
        cathedral.CloseAllDoors();
        (ReachThing T, Vector3 Front, Vector3? Above, Vector3? Below)? pick = null;
        foreach (var (t, stand) in fronts.Where(x => x.T.Kind is ZUseKind.SlotMachine or ZUseKind.Perk or ZUseKind.Workbench or ZUseKind.Power).OrderBy(x => x.T.Kind != ZUseKind.SlotMachine))
        {
            var above = FloorFrom(stand, up: true);
            var below = FloorFrom(stand, up: false);
            if (above == null && below == null)
                continue;
            if (pick == null || (above != null && below != null && (pick.Value.Above == null || pick.Value.Below == null)))
                pick = (t, stand, above, below);
            if (above != null && below != null && t.Kind == ZUseKind.SlotMachine)
                break;
        }
        if (pick is not { } pk)
        {
            Fail($"{CathedralName}: no machine with a floor above or below it to test the reach rule on");
            return;
        }

        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: cathedral);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        g.ZPower = true;
        var zp = g.ZP(me);
        bool Is(ZUse? u) => u is { } x && x.Kind == pk.T.Kind && x.Index == pk.T.Index;

        me.Teleport(pk.Front);
        var front = g.FindZUse(me);
        var usableFront = Is(front);
        zp.Points = 50000;
        g.SelfTestUse(me, now);
        var paidFront = 50000 - zp.Points;
        ZLine($"use reach: {pk.T.What} at {pk.T.At}: from in front ({pk.Front}) '{front?.Text}', a use paid {paidFront}");
        if (!usableFront || paidFront <= 0 && pk.T.Kind != ZUseKind.Power && pk.T.Kind != ZUseKind.Workbench)
            Fail($"{CathedralName}: {pk.T.What} should be usable from in front on its own floor ({pk.Front}): got '{front?.Text}', paid {paidFront}");
        ZTicks(g, ref now, 8); // (a pull still spinning would refuse the next one)
        g.ZPower = true;
        if (pk.T.Kind == ZUseKind.SlotMachine && g.ZSlots[pk.T.Index].State != ZSlotState.Idle)
            Fail($"{CathedralName}: {pk.T.What} still spinning 8 s after a pull");

        foreach (var (label, spot) in new[] { ("above", pk.Above), ("below", pk.Below) })
        {
            if (spot is not { } at)
                continue;
            me.Teleport(at);
            var u = g.FindZUse(me);
            zp.Points = 50000;
            g.SelfTestUse(me, now); // what a client's "use held" message makes the server do (ZombiesNet.cs), forged from the wrong floor
            var paid = 50000 - zp.Points;
            var bet = pk.T.Kind == ZUseKind.SlotMachine ? g.ServerSlotBet(me, pk.T.Index) : "n/a";
            ZLine($"  from the floor {label} ({at}): prompt '{u?.Text}', a forged use paid {paid}, slot bet change '{bet}'");
            if (Is(u))
                Fail($"{CathedralName}: {pk.T.What} offers its prompt to a player on the floor {label} ({at})");
            if (paid != 0)
                Fail($"{CathedralName}: the server took {paid} points for a use of {pk.T.What} from the floor {label}");
            if (bet == null)
                Fail($"{CathedralName}: the server changed {pk.T.What}'s bet from the floor {label}");
        }
        if (pk.Above == null || pk.Below == null)
            Fail($"{CathedralName}: no machine with a floor both above and below it (only {(pk.Above == null ? "below" : "above")} {pk.T.What})");

        // Through a wall: a spot in flat range and at the right height whose sight line a wall cuts.
        var wall = walled.FirstOrDefault(x => x.T.Kind is ZUseKind.SlotMachine or ZUseKind.Perk or ZUseKind.Workbench or ZUseKind.Box or ZUseKind.WallBuy or ZUseKind.Casino);
        if (wall.T.What == null)
            Fail($"{CathedralName}: no machine with a wall between it and a spot in range (expected some against the walls)");
        else
        {
            me.Teleport(wall.Stand);
            var u = g.FindZUse(me);
            var casino = wall.T.Kind == ZUseKind.Casino ? g.AtCasinoTable(me, wall.T.Index) : false;
            ZLine($"  through a wall: {wall.T.What} at {wall.T.At} from {wall.Stand} ({FlatLen(wall.T.At - wall.Stand):0.00} m flat): prompt '{u?.Text}'");
            if (u is { } x && x.Kind == wall.T.Kind && x.Index == wall.T.Index || casino)
                Fail($"{CathedralName}: {wall.T.What} is usable through a wall from {wall.Stand}");
        }
    }

    private static float FlatLen(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);

    /// <summary>A free spot to stand on the next floor up or down at the same x/z as <paramref name="stand"/>, or null.</summary>
    private static Vector3? FloorFrom(Vector3 stand, bool up)
    {
        for (var k = 0; k < 20; k++)
        {
            var y = up ? stand.Y + 2.2f + k * 0.5f : stand.Y - 1.0f - k * 0.5f;
            var p = new Vector3(stand.X, y, stand.Z);
            if (World.Blocked(p))
                continue;
            var ground = FloorBelow(p.X, p.Y, p.Z);
            if (y - ground >= 2.95f)
                continue; // nothing under it within reach of the probe
            var feet = p with { Y = ground };
            if ((up ? ground > stand.Y + 1.8f : ground < stand.Y - 1.8f) && !World.Blocked(feet + new Vector3(0, 0.05f, 0)))
                return feet;
        }
        return null;
    }

    /// <summary>Everything a map's use key works on, with the flat range and height band FindZUse gives it.</summary>
    private static List<ReachThing> ReachThings(Map map, ZombiesMap zm)
    {
        var rules = ZombiesRules.Current;
        var list = new List<ReachThing>();
        void Spot(string what, ZUseKind kind, int i, ZSpot s, float range) => list.Add(new(what, kind, i, s.At, 0, Game.FloorBand, range, null, default, default));
        for (var i = 0; i < zm.BoxSpots.Count; i++)
            Spot($"box spot {i}", ZUseKind.Box, i, zm.BoxSpots[i], 1.9f);
        for (var i = 0; i < zm.PerkMachines.Count; i++)
            Spot($"perk machine {zm.PerkMachines[i].Perk}", ZUseKind.Perk, i, zm.PerkMachines[i], Game.UseRange);
        for (var i = 0; i < zm.Benches.Count; i++)
            Spot($"workbench {i}", ZUseKind.Workbench, i, zm.Benches[i], 1.9f);
        if (zm.SlotUpgrade is { } su)
            Spot("slot upgrade", ZUseKind.SlotUpgrade, 0, su, Game.UseRange);
        if (zm.Power is { } pw)
            Spot("power switch", ZUseKind.Power, 0, pw, Game.UseRange);
        for (var i = 0; i < zm.SlotMachines.Count; i++)
            Spot($"slot machine {i}", ZUseKind.SlotMachine, i, zm.SlotMachines[i], Game.UseRange);
        for (var i = 0; i < zm.CasinoTables.Count; i++)
            Spot($"casino table {i}", ZUseKind.Casino, i, zm.CasinoTables[i], rules.CasinoUseRange);
        for (var i = 0; i < zm.TraderSpots.Count; i++)
            Spot($"trader spot {i}", ZUseKind.Trader, i, zm.TraderSpots[i], rules.TraderUseRange);
        for (var i = 0; i < zm.Salvage.Count; i++)
            Spot($"salvage pile {i}", ZUseKind.Salvage, i, zm.Salvage[i], Game.UseRange);
        for (var i = 0; i < zm.WallBuys.Count; i++)
            list.Add(new($"wall buy {zm.WallBuys[i].Build}", ZUseKind.WallBuy, i, zm.WallBuys[i].At, Game.WallBuyBandLo, Game.WallBuyBandHi, Game.UseRange, null, default, default));
        for (var i = 0; i < zm.Windows.Count; i++)
            list.Add(new($"window {i}", ZUseKind.Repair, i, zm.Windows[i].Inside, 0, Game.FloorBand, 1.3f, null, default, default));
        for (var i = 0; i < zm.Doors.Count; i++)
        {
            var d = zm.Doors[i];
            list.Add(new($"door {d.Name} (side A)", ZUseKind.Door, i, d.SideA, 0, 0, 1.9f, d.Box, d.SideA, d.SideB));
            list.Add(new($"door {d.Name} (side B)", ZUseKind.Door, i, d.SideB, 0, 0, 1.9f, d.Box, d.SideB, d.SideA));
        }
        if (map.Logic is { } logic)
            for (var i = 0; i < logic.Entities.Count; i++)
                if (logic.Entities[i] is { Kind: LogicKind.Button or LogicKind.Lever } e)
                    list.Add(new($"logic {e.Kind} '{e.Name}'", ZUseKind.None, i, e.At, 0, 0, e.Reach, null, default, default));
        return list;
    }

    /// <summary>
    /// Every thing on <paramref name="map"/> must be usable from some free spot near it. Returns, per thing, the first such
    /// spot, and spots in range and at the right height where a wall cuts the sight line (for the through-a-wall check).
    /// </summary>
    private static (List<(ReachThing, Vector3)> Fronts, List<(ReachThing, Vector3)> Walled) ReachSweep(Map map, ZombiesMap zm)
    {
        World.SetMap(map);
        map.CloseAllDoors();
        var fronts = new List<(ReachThing, Vector3)>();
        var walled = new List<(ReachThing, Vector3)>();
        var bad = new List<string>();
        var things = ReachThings(map, zm);
        foreach (var t in things)
        {
            Vector3? found = null;
            var wallSeen = false;
            var door = t.DoorBox != null;
            // Where to look round: a door from the doorway's face on this side, everything else from its spot.
            var centre = door ? Vector3.Clamp(t.Side, t.DoorBox!.Value.Min, t.DoorBox!.Value.Max) with { Y = t.Side.Y } : t.At;
            var floorRef = door ? t.Side.Y : t.ZLogic ? t.At.Y - 1.6f : t.At.Y + t.BandLo;
            for (var r = 0.3f; r <= MathF.Max(t.Range, 1.0f) + 0.6f && (found == null || !wallSeen && !door && !t.ZLogic); r += 0.2f)
                for (var k = 0; k < 24; k++)
                {
                    var x = centre.X + MathF.Sin(k * MathF.Tau / 24) * r;
                    var z = centre.Z + MathF.Cos(k * MathF.Tau / 24) * r;
                    var ground = FloorBelow(x, floorRef + 1.6f, z);
                    if (MathF.Abs(ground - floorRef) > (t.ZLogic ? 1.6f : 0.7f))
                        continue; // another floor (or a hole)
                    var feet = new Vector3(x, ground, z);
                    if (World.Blocked(feet + new Vector3(0, 0.05f, 0)))
                        continue;
                    var eye = feet + new Vector3(0, World.EyeHeight, 0);
                    bool inRange, height, sight;
                    if (door)
                    {
                        if (Vector3.DistanceSquared(feet, t.Side) > Vector3.DistanceSquared(feet, t.OtherSide))
                            continue; // the other side
                        var b = t.DoorBox!.Value;
                        var closest = Vector3.Clamp(feet + new Vector3(0, 1, 0), b.Min, b.Max);
                        inRange = FlatLen(closest - feet) <= t.Range;
                        height = eye.Y >= b.Min.Y - Game.ReachBelow && eye.Y <= b.Max.Y + Game.ReachAbove;
                        sight = Game.ZReachFrom(eye, closest with { Y = b.Min.Y }, 0, b.Max.Y - b.Min.Y);
                    }
                    else if (t.ZLogic)
                    {
                        inRange = Vector3.Distance(eye, t.At) <= t.Range;
                        height = Game.LogicInReach(eye, t.At);
                        sight = height && Game.ZClearSight(eye, t.At);
                    }
                    else
                    {
                        inRange = FlatLen(t.At - feet) <= t.Range;
                        height = eye.Y >= t.At.Y + t.BandLo - Game.ReachBelow && eye.Y <= t.At.Y + t.BandHi + Game.ReachAbove;
                        sight = Game.ZReachFrom(eye, t.At, t.BandLo, t.BandHi);
                    }
                    if (!inRange || !height)
                        continue;
                    if (sight)
                    {
                        found ??= feet;
                        continue;
                    }
                    if (!wallSeen && !door && !t.ZLogic && FlatLen(t.At - feet) <= t.Range - 0.3f && WallBetween(eye, t))
                    {
                        wallSeen = true;
                        walled.Add((t, feet));
                    }
                }
            if (found is { } f)
                fronts.Add((t, f));
            else
                bad.Add($"{t.What} at {t.At}");
        }
        ZLine($"use reach: {map.Name}: {things.Count - bad.Count} of {things.Count} things usable from their own floor, {walled.Count} with a wall-cut spot in range");
        foreach (var b in bad)
            Fail($"{map.Name}: {b} can't be used from anywhere on its floor under the reach rule (move its interact point)");
        return (fronts, walled);
    }

    /// <summary>Is it a map wall (not the thing's own prop) that cuts the sight line from <paramref name="eye"/> to <paramref name="t"/>?</summary>
    private static bool WallBetween(Vector3 eye, ReachThing t)
    {
        var target = new Vector3(t.At.X, Math.Clamp(eye.Y, t.At.Y + t.BandLo, t.At.Y + t.BandHi), t.At.Z);
        var to = target - eye;
        var dist = to.Length();
        return World.Query.Raycast(eye, to / dist, dist - 0.2f, QueryFilter.All.Without(QueryLayers.Props | QueryLayers.Clip)) < dist - 0.25f;
    }
}
