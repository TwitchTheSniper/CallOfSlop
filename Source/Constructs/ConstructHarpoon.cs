using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Flames and harpoons on a construct gun (docs/sandbox/CONSTRUCTS.md, "Flame and harpoon"), the gun's side: both are weapon
/// builder receivers, so these are only the numbers and steps a bullet doesn't have. A flame's reach grows with a merged
/// receiver and its heat gets past armour; it burns ammo boxes or the construct's own fuel. A harpoon's round ties a real rope
/// (a <see cref="CConnector"/> with <see cref="CConnector.Line"/> set): while it is out the trigger reels in and the gunner's
/// reload key cuts it. The rules are data: Constructs/WeaponParts.json. Nothing here allocates.
/// </summary>
public static class ConstructHarpoon
{
    private static ConstructWeaponParts.Rules R => ConstructWeaponParts.Current;

    /// <summary>A flame's or a harpoon's numbers on gun <paramref name="g"/>, after a merge scaled it. A merge's range power is 0, so N here is the only thing that gives a big receiver reach.</summary>
    public static void GunNumbers(ref CGun g, WeaponDef w)
    {
        var steps = Math.Max(1, (int)g.MergeN) - 1f;
        if ((g.Effects & GunEffect.Flame) != 0)
        {
            w.RangeM *= 1f + R.FlameReachPerStep * steps;
            g.MaxRange = w.RangeM * R.FlameReach;
            g.Fx = GunFx.Flame;
            g.Kick = 0;
        }
        else if ((g.Effects & GunEffect.Harpoon) != 0)
        {
            w.RangeM *= 1f + R.HarpoonReachPerStep * steps;
            g.MaxRange = MathF.Min(w.RangeM, ConstructWorld.ConnectorMaxLength);
            g.Fx = GunFx.Harpoon;
            g.Speed = 80f;
        }
    }

    /// <summary>The rope a launcher pays out: a hand gun's S; a receiver on a construct M, a 2x2 merge L, anything bigger XL (<see cref="ConstructWorld.ConnectorSizes"/>).</summary>
    public static byte RopeSize(int mergeN) => (byte)Math.Clamp(mergeN, 1, 3);

    /// <summary>The rounds of fire in the construct's fuel.</summary>
    public static int FuelRounds(Construct c) => (int)(c.Fuel * R.FlameRoundsPerLitre);

    /// <summary>Takes up to <paramref name="want"/> rounds' worth of the construct's fuel. The rounds taken.</summary>
    public static int TakeFuel(Construct c, int want)
    {
        var take = Math.Min(want, FuelRounds(c));
        c.Fuel = MathF.Max(0, c.Fuel - take / R.FlameRoundsPerLitre);
        return take;
    }

    /// <summary>What a lick of flame does to a part through <paramref name="armour"/>: never stopped as a small bullet would be, and half as much at the rules' armour.</summary>
    public static float HeatThrough(float damage, float armour) => damage / (1f + armour / R.FlameArmourHalf);

    /// <summary>
    /// A gun's harpoon struck: a rope from its muzzle to what it hit: <paramref name="body"/>'s part, player <paramref name="target"/>,
    /// or the map (neither). A zombie isn't tied (only the wound).
    /// </summary>
    public static void Tie(ConstructWorld world, IWorldQuery query, Construct own, in CGun g, Vector3 muzzle, Construct? body, int part, int target, Vector3 at)
    {
        if (target >= Game.MaxPlayers)
            return;
        var from = new ConnectorEnd { Body = own.Id, Part = g.Part, Local = ConstructWorld.PartLocal(own, g.Part, muzzle) };
        ConnectorEnd head;
        if (target >= 0)
            head = new ConnectorEnd { Player = (byte)(target + 1), Local = at };
        else if (body != null && part < body.PartCount)
            head = new ConnectorEnd { Body = body.Id, Part = (ushort)part, Local = ConstructWorld.PartLocal(body, part, at) };
        else if (body != null)
            return;
        else
            head = new ConnectorEnd { Local = at };
        world.TieLine(query, RopeSize(g.MergeN), from, head, R.HarpoonReel);
    }

    /// <summary>A harpoon gun with a line out, this tick: true (it can't fire) and the trigger reels it; the gunner's reload key cuts it.</summary>
    public static bool LineOut(ConstructWorld world, Construct c, ref CGun g, bool trigger)
    {
        var k = world.LineOf(c.Id, g.Part);
        if (k < 0)
            return false;
        if (g.Seat >= 0 && g.Seat < c.PartCount && (c.Parts[g.Seat].Keys & ConstructParts.KeyReload) != 0)
        {
            world.CutLine(k);
            return true;
        }
        ref var cn = ref world.Connectors[k];
        cn.TriggerHeld &= trigger; // (the pull that fired it doesn't reel: let go first)
        cn.Reeling = trigger && !cn.TriggerHeld;
        return true;
    }
}

/// <summary>Harpoon lines in the connector list: a rope with a launcher at A and a barbed head at B, and ends on players.</summary>
public sealed partial class ConstructWorld
{
    /// <summary>How far past its length a line lets a player go: the stretch is what pulls what it holds (<see cref="PlayerPullStiffness"/>).</summary>
    public const float PlayerEndGive = 0.6f;

    /// <summary>How hard a player on a line pulls what the other end holds: N/m of stretch (at the most, the give's worth: about a man's weight and a half).</summary>
    public const float PlayerPullStiffness = 2500f;

    public const float PlayerPullDamping = 300f;
    public const float LineMinLength = 1.5f; // a reel stops with this much rope out
    public const float ReelStall = 1.5f; // metres of stretch a reel gives up at
    public const int ReelSyncTicks = 6; // a line being reeled tells the clients its length this often

    /// <summary>Connectors with an end on a player: while 0 the players' moves and ticks never look at the list.</summary>
    public int PlayerEndCount;

    /// <summary>Counts the ends on players again (the list changed).</summary>
    public void RecountPlayerEnds()
    {
        PlayerEndCount = 0;
        for (var k = 0; k < ConnectorCount; k++)
            if (Connectors[k].A.Player != 0 || Connectors[k].B.Player != 0)
                PlayerEndCount++;
    }

    /// <summary>Ties a harpoon line: a rope of size <paramref name="size"/>, the launcher at A, the head at B, the trigger that fired it still held. Its index, or -1.</summary>
    public int TieLine(IWorldQuery? q, byte size, in ConnectorEnd launcher, in ConnectorEnd head, float reel)
    {
        if (!EndPoint(launcher, out var a) || !EndPoint(head, out var b) || Vector3.Distance(a, b) < ConnectorMinLength)
            return -1;
        var k = AddConnector(q, ConnectorKind.Rope, size, launcher, head);
        if (k < 0)
            return -1;
        ref var cn = ref Connectors[k];
        cn.Line = true;
        cn.ReelSpeed = reel;
        cn.TriggerHeld = true;
        return k;
    }

    /// <summary>The line gun part <paramref name="part"/> of a construct has out, or -1.</summary>
    public int LineOf(ushort body, ushort part)
    {
        for (var k = 0; k < ConnectorCount; k++)
            if (Connectors[k].Line && Connectors[k].A.Body == body && Connectors[k].A.Part == part)
                return k;
        return -1;
    }

    /// <summary>The line player <paramref name="id"/> holds the launcher of, or -1.</summary>
    public int LineHeldBy(byte id)
    {
        for (var k = 0; k < ConnectorCount; k++)
            if (Connectors[k].A.Player == id + 1)
                return k;
        return -1;
    }

    /// <summary>The line stuck in player <paramref name="id"/>, or -1.</summary>
    public int LineIn(byte id)
    {
        for (var k = 0; k < ConnectorCount; k++)
            if (Connectors[k].B.Player == id + 1)
                return k;
        return -1;
    }

    /// <summary>A line is let go: it whips back as a snapped one does.</summary>
    public void CutLine(int k) => SnapConnector(k, null);

    /// <summary>A reel taking in rope (in <see cref="StepConnectors"/>). It stalls once the rope is that far behind the gap: both ends are held, and more would only tear it.</summary>
    private void ReelLine(ref CConnector cn, float dist)
    {
        if (!cn.Reeling || cn.Length <= LineMinLength || dist > cn.Length + ReelStall)
            return;
        cn.Length = MathF.Max(LineMinLength, cn.Length - cn.ReelSpeed * World.TickDt);
        if (--cn.ReelSync <= 0)
        {
            cn.ReelSync = ReelSyncTicks;
            TouchConnector(ref cn);
        }
    }
}
