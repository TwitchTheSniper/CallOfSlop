using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Landmines and props that come back, server-authoritative (Props/README.md, "Landmines").
///
/// A landmine is a breakable, explosive prop with a pressure plate (<see cref="PropBreakable.TriggerRadius"/>). Every server
/// tick the living players, bots and zombies' feet are checked against each mine that's still there: feet on the plate
/// light it through the same fuse machinery as a fuel drum (<see cref="LightProp"/>, which every PC hears as a click) and it
/// goes off after its fuse (0.35 s): enough for a quick player to throw themselves clear of the worst of it. Shot, knifed
/// or caught in another blast, it goes off too. The blast is neutral: it hurts everyone (see <see cref="PropExplode"/>);
/// whoever last hit the mine gets the kills they could have made anyway, everyone else's death is nobody's kill
/// ("Landmine" in the feed, no XP).
///
/// Bots know where every mine is (they're part of the map): nav nodes next to one cost extra (<see cref="MineHazard"/>),
/// and close up they step round it (<see cref="BotAvoidMines"/>). Zombies don't care and set them off.
///
/// Any breakable prop placed with the Prop op's "respawn" comes back that many seconds after it broke (once nobody is
/// standing on its spot), within the match; every match starts with every prop whole anyway.
/// </summary>
public sealed partial class Game
{
    /// <summary>Kill-feed id for a neutral blast: a landmine, or anything a mine nobody shot set off ("Landmine").</summary>
    public const byte LandmineKillId = 254;

    /// <summary>How hard (m/s at the centre) and how far a landmine kill throws the body when the blast itself isn't known (<see cref="Knockback.ForKill"/>).</summary>
    public const float LandmineRagdollPush = 12f, LandmineRagdollRadius = 5f;

    /// <summary>Extra route cost (metres of walking) for a nav node next to a landmine: bots go round unless there's no other way.</summary>
    public const float MineNodeCost = 40f;

    /// <summary>How far (metres, beyond the plate) bots keep their nav routes and their steps from a landmine.</summary>
    public const float MineAvoidMargin = 0.65f;

    /// <summary>Mines set off by someone stepping on them (server), and clicks heard (every PC), since this game started (self-test).</summary>
    public int MinesTriggered { get; private set; }

    /// <inheritdoc cref="MinesTriggered"/>
    public int MinesClicked { get; private set; }

    /// <summary>Props that came back after "respawn" seconds (server), and zombies a neutral blast killed (self-test).</summary>
    public int PropsRespawned { get; private set; }

    /// <inheritdoc cref="PropsRespawned"/>
    public int ZombiesKilledByMines { get; private set; }

    /// <summary>Players killed by prop blasts (barrels, mines) since this game started, and the last one (killer, victim, kill id; self-test).</summary>
    public int PropBlastKills { get; private set; }

    /// <inheritdoc cref="PropBlastKills"/>
    public (byte Killer, byte Victim, byte KillId) LastPropBlastKill { get; private set; }

    private readonly Dictionary<int, byte> _mineLastHit = new();
    private readonly List<(int Index, double At)> _propRespawns = new();

    /// <summary>Props waiting to come back (self-test).</summary>
    public int PropRespawnsPending => _propRespawns.Count;

    private void ClearMineState()
    {
        _mineLastHit.Clear();
        _propRespawns.Clear();
    }

    /// <summary>Server: <paramref name="by"/> hit mine <paramref name="i"/>; its blast is theirs, even if it was stepped on later.</summary>
    private void NoteMineHit(int i, Player by)
    {
        EnsurePropFuseState();
        _mineLastHit[i] = by.Id;
    }

    /// <summary>
    /// Server: <paramref name="by"/> hit mine <paramref name="i"/> while it was already clicking. If nobody had set it off
    /// (stepped on, or lit by a neutral blast), the blast becomes theirs; one that's already someone's stays theirs.
    /// </summary>
    private void CreditLitMine(int i, Player by)
    {
        NoteMineHit(i, by);
        for (var k = 0; k < _propFuses.Count; k++)
            if (_propFuses[k].Index == i && _propFuses[k].Attacker == byte.MaxValue)
                _propFuses[k] = _propFuses[k] with { Attacker = by.Id, Env = false };
    }

    // ---- Server: pressure plates ----------------------------------------------------------------------------

    /// <summary>Server, every tick: props due back come back; mines with feet on their plate click.</summary>
    private void ServerTickMines(double now)
    {
        if (_propRespawns.Count > 0)
            ServerTickPropRespawns(now);
        var map = World.Map;
        var mines = map.TriggerProps;
        if (mines.Length == 0 || !DamageEnabled)
            return;
        foreach (var i in mines)
        {
            if (map.IsPropBroken(i) || IsPropLit(i))
                continue;
            var inst = map.PropInstances[i];
            var br = inst.Def.Breakable!;
            if (!MineStepped(inst, br))
                continue;
            Player? credit = null;
            if (_mineLastHit.Count > 0 && ReferenceEquals(_fuseMap, map) && _fuseMatch == MatchNumber && _mineLastHit.TryGetValue(i, out var id))
                Players.TryGetValue(id, out credit);
            MinesTriggered++;
            LightProp(i, br.ExplodeFuse, credit, now);
        }
    }

    /// <summary>Are a living player's, bot's or zombie's feet on mine <paramref name="inst"/>'s plate? No allocation.</summary>
    private bool MineStepped(in PropInstance inst, PropBreakable br)
    {
        foreach (var p in Players.Values)
            if (p.Alive && OnPlate(inst, br, p.Position))
                return true;
        foreach (var z in Horde)
            if (z.State != ZState.Dead && OnPlate(inst, br, z.Position))
                return true;
        return false;
    }

    /// <summary>Feet at <paramref name="feet"/> press the plate: within its radius across, and from just below its base to its trigger height above.</summary>
    public static bool OnPlate(in PropInstance inst, PropBreakable br, Vector3 feet)
    {
        var y0 = inst.Position.Y;
        if (feet.Y < y0 - 0.3f || feet.Y > y0 + br.TriggerHeight * inst.Scale)
            return false;
        var r = br.TriggerRadius * inst.Scale;
        var dx = feet.X - inst.Position.X;
        var dz = feet.Z - inst.Position.Z;
        return dx * dx + dz * dz <= r * r;
    }

    // ---- Server: zombies in a neutral blast -----------------------------------------------------------------

    /// <summary>
    /// Server: a mine's blast on the horde (walls shield, as for grenades). Zombies are the credited player's kills (and
    /// points) when someone shot the mine, otherwise they just die.
    /// </summary>
    private void BlastZombiesNeutral(Player? credit, Vector3 center, float radius, float maxDamage, double now)
    {
        BlastPve(credit, center, radius, maxDamage, now, LandmineKillId); // co-op enemies too (GamePve.cs)
        if (Horde.Count == 0)
            return;
        for (var k = 0; k < Horde.Count; k++)
        {
            var z = Horde[k];
            if (z.State == ZState.Dead || !DamageEnabled)
                continue;
            var to = z.Position + new Vector3(0, 1.1f, 0) - center;
            var dist = to.Length();
            if (dist > radius)
                continue;
            if (dist > 0.3f && World.Query.Raycast(center, to / dist, dist, QueryFilter.Shots) < dist - 0.05f)
                continue;
            var amount = (int)MathF.Round(maxDamage * MathF.Pow(1f - dist / radius, 1.4f));
            if (amount <= 0)
                continue;
            if (credit != null)
            {
                BlastLimbs(z, center, radius, maxDamage); // (ZombieLimbs.cs)
                if (DamageZombie(credit, z, amount, false, melee: false, now, explosive: true))
                    ZombiesKilledByMines++;
                SeverLimbs(z, credit, now);
                continue;
            }
            z.Health -= amount;
            if (z.Health > 0)
                continue;
            z.Health = 0;
            SetZState(z, ZState.Dead, now);
            CrowdNoteKill();
            ZombiesKilledByMines++;
        }
    }

    // ---- Server: props coming back ("respawn") --------------------------------------------------------------

    /// <summary>Server: prop <paramref name="i"/> just broke; with a "respawn" time it comes back that long from now.</summary>
    private void ScheduleRespawn(int i, double now)
    {
        var s = World.Map.PropRespawn(i);
        if (s <= 0)
            return;
        EnsurePropFuseState();
        _propRespawns.Add((i, now + s));
    }

    private void ServerTickPropRespawns(double now)
    {
        if (!ReferenceEquals(_fuseMap, World.Map) || _fuseMatch != MatchNumber)
        {
            _propRespawns.Clear(); // a new match or map: everything is whole again anyway
            return;
        }
        for (var k = _propRespawns.Count - 1; k >= 0; k--)
        {
            var (i, at) = _propRespawns[k];
            if (at > now)
                continue;
            if (PropSpotTaken(i))
            {
                _propRespawns[k] = (i, now + 1.0); // someone is standing there: try again in a second
                continue;
            }
            _propRespawns.RemoveAt(k);
            ServerRestoreProp(i);
        }
    }

    /// <summary>Is anyone (alive) standing on or right next to prop <paramref name="i"/>'s spot? A mine never comes back under someone's feet.</summary>
    private bool PropSpotTaken(int i)
    {
        var inst = World.Map.PropInstances[i];
        var b = inst.Bounds;
        var grow = 0.9f + (inst.Def.Breakable is { IsMine: true } br ? br.TriggerRadius * inst.Scale : 0f);
        bool Near(Vector3 feet) =>
            feet.X > b.Min.X - grow && feet.X < b.Max.X + grow && feet.Z > b.Min.Z - grow && feet.Z < b.Max.Z + grow
            && feet.Y > b.Min.Y - 2f && feet.Y < b.Max.Y + 0.5f;
        foreach (var p in Players.Values)
            if (p.Alive && Near(p.Position))
                return true;
        foreach (var z in Horde)
            if (z.State != ZState.Dead && Near(z.Position))
                return true;
        return false;
    }

    /// <summary>Server: puts prop <paramref name="i"/> back and tells everyone.</summary>
    private void ServerRestoreProp(int i)
    {
        var map = World.Map;
        if (!map.RestoreProp(i))
            return;
        if ((uint)i < (uint)_propLit.Length)
            _propLit[i] = false;
        _mineLastHit.Remove(i);
        PropsRespawned++;
        WritePropBreak(_w, i, (PropDamage)PropRestoreFlag, map.PropInstances[i].Bounds.Center);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Client: a prop with "respawn" is back.</summary>
    internal static void ApplyPropRestore(Map map, int i)
    {
        if ((uint)i < (uint)map.PropInstances.Count)
            map.RestoreProp(i);
    }

    // ---- Bots ------------------------------------------------------------------------------------------------

    private float[] _mineHazard = [];
    private NavGraph? _hazardNav;
    private Map? _hazardMap;
    private int _hazardVersion = -1;
    private readonly List<int> _hazardNodes = new();

    /// <summary>Times the bots' mine-avoidance costs were worked out again (a mine went off or came back; self-test).</summary>
    public int MineHazardBuilds { get; private set; }

    /// <summary>
    /// Extra route cost per node of <paramref name="nav"/> (<see cref="MineNodeCost"/> for every node within a mine's
    /// plate plus <see cref="MineAvoidMargin"/> and a body's width), or null on a map without mines. Worked out again only
    /// when a prop broke or came back, so a search costs one array lookup per edge.
    /// </summary>
    internal float[]? MineHazard(NavGraph nav)
    {
        var map = World.Map;
        var mines = map.TriggerProps;
        if (mines.Length == 0 || nav.Nodes.Length == 0)
            return null;
        if (ReferenceEquals(nav, _hazardNav) && ReferenceEquals(map, _hazardMap) && map.PropBreakVersion == _hazardVersion)
            return _mineHazard;
        if (_mineHazard.Length != nav.Nodes.Length)
            _mineHazard = new float[nav.Nodes.Length];
        else
            Array.Clear(_mineHazard);
        foreach (var i in mines)
        {
            if (map.IsPropBroken(i))
                continue;
            var inst = map.PropInstances[i];
            var br = inst.Def.Breakable!;
            _hazardNodes.Clear();
            nav.Within(inst.Position, br.TriggerRadius * inst.Scale + World.PlayerRadius + MineAvoidMargin, _hazardNodes);
            foreach (var n in _hazardNodes)
                if (MathF.Abs(nav.Nodes[n].Y - inst.Position.Y) < 1.5f)
                    _mineHazard[n] = MineNodeCost;
        }
        _hazardNav = nav;
        _hazardMap = map;
        _hazardVersion = map.PropBreakVersion;
        MineHazardBuilds++;
        return _mineHazard;
    }

    /// <summary>
    /// A bot about to walk onto a mine steps round it: the part of its walk towards the mine goes, and a push away from it
    /// comes in, within the plate plus <see cref="MineAvoidMargin"/> and a body's width. No allocation.
    /// </summary>
    private void BotAvoidMines(Player p, ref Vector3 wish)
    {
        var map = World.Map;
        var mines = map.TriggerProps;
        if (mines.Length == 0)
            return;
        var pos = p.Position;
        foreach (var i in mines)
        {
            if (map.IsPropBroken(i))
                continue;
            var inst = map.PropInstances[i];
            var br = inst.Def.Breakable!;
            var reach = br.TriggerRadius * inst.Scale + World.PlayerRadius + MineAvoidMargin;
            var dx = inst.Position.X - pos.X;
            var dz = inst.Position.Z - pos.Z;
            var d2 = dx * dx + dz * dz;
            if (d2 > reach * reach || MathF.Abs(inst.Position.Y - pos.Y) > 1.5f)
                continue;
            var d = MathF.Sqrt(d2);
            var n = d > 0.01f ? new Vector3(dx / d, 0, dz / d) : new Vector3(1, 0, 0);
            var into = Vector3.Dot(wish, n);
            if (into > 0)
            {
                // No closer: the part of the walk towards the mine turns sideways instead (the side the bot already
                // leans to; head-on, the same side every time), so a bot with the mine dead ahead slides round it
                // rather than stopping and backing off forever.
                wish -= n * into;
                var side = new Vector3(-n.Z, 0, n.X);
                var lean = Vector3.Dot(wish, side);
                wish += side * (lean < -1e-3f ? -into : into);
            }
            wish -= n * ((reach - d) / reach * 2.5f); // and away
        }
    }

    // ---- Self-test hooks -------------------------------------------------------------------------------------

    /// <summary>Self-test: one server tick of the pressure plates and respawns (what <see cref="ServerTickStep"/> runs).</summary>
    public void SelfTestTickMines(double now) => ServerTickMines(now);

    /// <summary>Self-test: one server tick of the fuses (what <see cref="ServerTickStep"/> runs).</summary>
    public void SelfTestTickPropFuses(double now) => ServerTickPropFuses(now);

    /// <summary>Self-test: every prop whole and no fuse, last hitter or respawn pending (what a new match does).</summary>
    public void SelfTestResetProps()
    {
        World.Map.RestoreProps();
        ClearPropFuses();
    }

    /// <summary>Self-test: a bot's walk direction <paramref name="wish"/> at <paramref name="p"/>'s spot after stepping round mines.</summary>
    public Vector3 SelfTestAvoidMines(Player p, Vector3 wish)
    {
        BotAvoidMines(p, ref wish);
        return wish;
    }

    /// <summary>Self-test: bot <paramref name="p"/> plans a route to <paramref name="goal"/> on <paramref name="nav"/>; returns the path's node count.</summary>
    public int SelfTestBotPlan(Player p, NavGraph nav, Vector3 goal, double now)
    {
        if (p.Bot is not { } b)
            return 0;
        BotPlan(p, b, nav, goal, now);
        return b.PathLen;
    }
}
