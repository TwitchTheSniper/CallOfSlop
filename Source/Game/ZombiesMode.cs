using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>Where a Zombies run is between rounds.</summary>
public enum ZRoundPhase : byte
{
    /// <summary>Before round 1 (a few seconds after the match starts).</summary>
    Waiting,

    /// <summary>Zombies are coming (the round ends when every one of them is dead).</summary>
    Round,

    /// <summary>The short break between rounds.</summary>
    Break,
}

/// <summary>The mystery box.</summary>
public enum ZBoxState : byte
{
    Idle,
    Spinning,

    /// <summary>The gun floats over the box for its buyer to take.</summary>
    Offering,

    /// <summary>The teddy bear: the box refunds its price and flies off to another spot.</summary>
    Teddy,
}

/// <summary>What the interact key would do for a player right now in Zombies.</summary>
public enum ZUseKind : byte
{
    None,
    Revive,
    Door,
    WallBuy,
    WallAmmo,
    Box,
    BoxTake,
    Perk,
    SlotUpgrade,
    Power,
    Workbench,
    Salvage,
    Repair,

    /// <summary>A built weapon on the floor (dropped when someone bled out): pick it up (ZombiesDrops.cs).</summary>
    Pickup,
}

/// <summary>One thing the interact key can use: what, which one, its price and the prompt.</summary>
public readonly record struct ZUse(ZUseKind Kind, int Index, int Cost, string Text, bool Blocked = false)
{
    /// <summary>Held use: the key does it over time (revive, rebuild boards) rather than once.</summary>
    public bool Continuous => Kind is ZUseKind.Revive or ZUseKind.Repair;
}

/// <summary>A player's Zombies run (server-owned; clients get points, downs and perks in <see cref="Msg.ZWorld"/>, parts in <see cref="Msg.ZRun"/>).</summary>
public sealed class ZPlayer
{
    public int Points;
    public bool Downed;
    public double DownedAt, BleedAt, SelfReviveAt = -1;

    /// <summary>Perks held, one bit per perk in the rules' list.</summary>
    public byte Perks;

    /// <summary>The interact key is held (sent by the client in <see cref="Msg.ZUse"/>), since when, and whether this hold already bought something.</summary>
    public bool UseHeld, UseDone;

    public double UseSince;
    public double RepairNext;
    public int RepairPointsThisRound;

    /// <summary>Who this player is reviving and how far along (0-1); and, for a downed player, how far their revive is (clients).</summary>
    public byte Reviving = byte.MaxValue;

    public float ReviveProgress;
    public float BeingRevived;
    public float BleedFraction;

    /// <summary>Loose parts (by name) and scrap, for the workbench.</summary>
    public readonly SortedDictionary<string, int> Parts = new(StringComparer.OrdinalIgnoreCase);

    public int Scrap;

    /// <summary>The build in each slot (0-3 guns, 4 the melee weapon), in text form: what the workbench edits and clients assemble.</summary>
    public readonly WeaponBuild?[] Slots = new WeaponBuild?[5];

    /// <summary>Bled out: comes back with the start gear next round.</summary>
    public bool NeedsStartGear = true;

    public bool Spawned;
    public int Kills, Headshots, Downs, Revives;

    /// <summary>Client: the points last shown (the HUD's +N popups come from the difference).</summary>
    public int ShownPoints = int.MinValue;

    public bool Has(int perk) => perk >= 0 && (Perks & (1 << perk)) != 0;
}

/// <summary>A part lying on the floor (dropped by a zombie): walk over it to pick it up.</summary>
public sealed class ZDrop
{
    public ushort Id;
    public string Part = "";
    public Vector3 At;
    public double Until;
}

/// <summary>
/// The Zombies mode (docs/zombies/ZOMBIES-MODE.md): co-op for 1-4 players against rounds of zombies, World at War style,
/// played on maps with a "zombies" section (<see cref="ZombiesMap"/>). Server-authoritative and deterministic (its own
/// seeded random numbers): rounds and their curve (<see cref="ZombiesRules"/>), spawners in unlocked areas, windows with
/// boards, the points economy, doors and debris that unlock areas, wall buys, the mystery box, perk machines, the
/// weapon-slot upgrade, the power switch, part drops, salvage and the workbench, downs, revives and the game over.
/// The zombies themselves are in ZombieAI.cs, the packets in ZombiesNet.cs, the co-op bots in ZombieBots.cs.
/// </summary>
public sealed partial class Game
{
    /// <summary>Zombies is co-op for up to this many players (people and bots).</summary>
    public const int ZombiesMaxPlayers = 4;

    /// <summary>The most zombies that can be alive at once (the rules file's cap goes up to this).</summary>
    public const int MaxZombies = 64;

    /// <summary>The rules (Parts/Tables/ZombiesRules.json).</summary>
    public ZombiesRules ZRules = ZombiesRules.Current;

    /// <summary>The map's Zombies layout while playing the Zombies mode, else null.</summary>
    public ZombiesMap? ZMap => Mode == GameMode.Zombies ? World.Map.Zombies : null;

    public bool IsZombies => ZMap != null;

    // ---- Run state (server-owned, replicated in ZWorld) ----------------------------------------------
    public int ZRound;
    public ZRoundPhase ZPhase;
    public double ZPhaseLeft; // seconds left of the break (clients count it down)
    public int ZRemaining; // zombies still to come this round (queued + alive)
    public bool ZPower;
    public byte[] ZBoards = [];
    public int BoxSpot, BoxUses;
    public ZBoxState BoxState;
    public double BoxLeft; // seconds left in the box's state
    public int BoxBuild = -1; // offered build (index into StandardBuilds.All)
    public byte BoxOwner = byte.MaxValue;
    public readonly Dictionary<byte, ZPlayer> ZPlayers = new();
    public readonly List<ZDrop> ZDrops = new();

    private int _zToSpawn;
    private double _zPhaseUntil, _zNextSpawn, _boxUntil;
    private int _boxMoveAfter;
    private uint _zRng = 1;
    private int _zRunMatch = -1;
    private ushort _zNextDrop = 1;
    private readonly int[] _salvageRound = new int[64];
    private bool _zAnySpawned;

    /// <summary>The seed the next run starts from (the self-test sets it; otherwise a fixed per-match value, so runs replay the same).</summary>
    public uint ZSeed = 0x5EED2026;

    /// <summary>Self-test: the host (with no one at the keyboard) stays out of the run, so a bots-only run can be played.</summary>
    public bool ZHostSitsOut;

    /// <summary>Server: a line for the log when a run ends (the self-test reads it).</summary>
    public string ZLastResult = "";

    /// <summary>A player's run (created on first use).</summary>
    public ZPlayer ZP(Player p) => ZP(p.Id);

    public ZPlayer ZP(byte id)
    {
        if (!ZPlayers.TryGetValue(id, out var z))
            ZPlayers[id] = z = new ZPlayer { Points = ZRules.StartPoints };
        return z;
    }

    /// <summary>Downed (still alive, on the floor, waiting for a revive).</summary>
    public bool IsDowned(Player p) => ZPlayers.TryGetValue(p.Id, out var z) && z.Downed;

    /// <summary>Standing and fighting: alive and not downed.</summary>
    public bool IsStanding(Player p) => p.Alive && !IsDowned(p);

    // ---- Deterministic random numbers ------------------------------------------------------------------

    private uint ZNext()
    {
        var x = _zRng;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        return _zRng = x == 0 ? 0x9E3779B9u : x;
    }

    private float ZRand() => (ZNext() >> 8) / 16777216f;

    private int ZRange(int n) => n <= 1 ? 0 : (int)(ZNext() % (uint)n);

    // ---- Run lifecycle ----------------------------------------------------------------------------------

    /// <summary>
    /// Server: forget the last run (a new match, a mode change): the world resets (<see cref="ResetZombiesWorld"/>), every
    /// player's run (points, perks, parts) and the built guns go. Nothing happens outside Zombies.
    /// </summary>
    internal void ResetZombiesRun()
    {
        if (Mode != GameMode.Zombies && !_zBuildsGiven)
        {
            World.Map.OpenAllDoors(); // another mode on a Zombies map: every door is open (one big map)
            return;
        }
        _zBuildsGiven = Mode == GameMode.Zombies;
        if (Mode == GameMode.Zombies)
            while (Players.Count > ZombiesMaxPlayers && KickOneBot())
            {
                // co-op for up to 4: extra bots go
            }
        ResetZombiesWorld();
        ZPlayers.Clear();
        _zRunMatch = -1;
        _zAnySpawned = false;
        foreach (var p in Players.Values)
        {
            p.BuiltPrimary = p.BuiltSecondary = null;
            p.BuiltExtra[0] = p.BuiltExtra[1] = null;
            p.ExtraSlots = false;
            p.MeleeBuild = null;
        }
    }

    private bool _zBuildsGiven;

    /// <summary>Both sides: the map and the run state back to the start (doors shut, boards up, no zombies, round 0).</summary>
    internal void ResetZombiesWorld()
    {
        Horde.Clear();
        ZDrops.Clear();
        ZWeaponDrops.Clear();
        ZPlayers.Clear();
        ZRound = 0;
        ZPhase = ZRoundPhase.Waiting;
        ZPhaseLeft = 0;
        ZRemaining = 0;
        ZPower = false;
        BoxState = ZBoxState.Idle;
        BoxBuild = -1;
        BoxOwner = byte.MaxValue;
        BoxUses = 0;
        Array.Clear(_salvageRound);
        World.Map.CloseAllDoors();
        var z = World.Map.Zombies;
        ZBoards = z == null ? [] : z.Windows.Select(w => (byte)w.Boards).ToArray();
        BoxSpot = z?.BoxStart ?? 0;
    }

    /// <summary>Server: the run starts (the match just went live).</summary>
    private void StartZombiesRun(double now)
    {
        _zRunMatch = MatchNumber;
        _zRng = ZSeed ^ (uint)(MatchNumber * 7919);
        ZNext();
        _boxMoveAfter = ZRules.BoxMoveMin + ZRange(ZRules.BoxMoveMax - ZRules.BoxMoveMin + 1);
        ZRound = 0;
        ZPhase = ZRoundPhase.Waiting;
        _zPhaseUntil = now + ZRules.FirstRoundDelay;
        if (NavGraph.Current == NavGraph.Empty)
            NavGraph.Pump(10000); // the zombies' nav graph, now (doors shut), so every run plans on the same graph from its first zombie
        foreach (var p in Players.Values.OrderBy(p => p.Id))
        {
            var zp = ZP(p);
            if (!zp.Spawned && p.Alive)
            {
                zp.Spawned = true;
                if (zp.NeedsStartGear)
                    GiveStartGear(p);
            }
        }
        ModeEvent($"Zombies on {World.Map.Name}: survive as long as you can. Round 1 in {ZRules.FirstRoundDelay:0} seconds.", Sfx.StreakCall);
        Console.WriteLine($"[zombies] run started: {Players.Count} player(s), seed {_zRng:x8}, {World.Map.Zombies!.Spawners.Count} spawners, {World.Map.Zombies.Doors.Count} doors");
    }

    /// <summary>Server, every tick while a Zombies match is playing.</summary>
    internal void ServerTickZombies(double now)
    {
        if (ZMap is not { } zm)
            return;
        if (_zRunMatch != MatchNumber)
            StartZombiesRun(now);

        switch (ZPhase)
        {
            case ZRoundPhase.Waiting or ZRoundPhase.Break when now >= _zPhaseUntil:
                BeginRound(ZRound + 1, now);
                break;
            case ZRoundPhase.Round:
                SpawnTick(zm, now);
                if (_zToSpawn == 0 && AliveZombies == 0)
                    EndRound(now);
                break;
        }
        ZPhaseLeft = ZPhase == ZRoundPhase.Round ? 0 : Math.Max(0, _zPhaseUntil - now);
        ZRemaining = _zToSpawn + AliveZombies;

        TickHorde(zm, now); // ZombieAI.cs
        foreach (var p in Players.Values.OrderBy(p => p.Id))
            TickZPlayer(p, ZP(p), zm, now);
        TickBox(zm, now);
        TickDrops(now);
        TickWeaponDrops(now); // ZombiesDrops.cs

        // The game over: nobody left standing (everyone downed or bled out).
        if (_zAnySpawned && ZRound > 0 && Players.Count > 0 && !Players.Values.Any(p => IsStanding(p) || (p.Alive && ZP(p).SelfReviveAt > 0)))
        {
            var survived = Math.Max(0, ZRound - 1);
            ZLastResult = $"Game over on round {ZRound}: survived {survived} round{(survived == 1 ? "" : "s")}";
            Console.WriteLine($"[zombies] {ZLastResult}, {Players.Values.Sum(p => ZP(p).Kills)} kills");
            EndMatch(null, ZLastResult, now);
            return;
        }
        ServerSendZombies(now); // ZombiesNet.cs
    }

    private void BeginRound(int round, double now)
    {
        ZRound = round;
        ZPhase = ZRoundPhase.Round;
        _zToSpawn = ZRules.ZombieCount(round, Math.Max(1, Players.Count - (ZHostSitsOut ? 1 : 0)));
        _zNextSpawn = now + 1.0;
        foreach (var p in Players.Values)
        {
            ZP(p).RepairPointsThisRound = 0;
            if (!p.Alive && !(ZHostSitsOut && p.Id == LocalId))
            {
                p.RespawnAt = now; // bled out last round (or joined late): back in with the start gear
                p.WantsRespawn = true;
            }
        }
        ModeEvent($"ROUND {round}", Sfx.MortarWhistle);
        ZBanner($"ROUND {round}");
    }

    private void EndRound(double now)
    {
        ZPhase = ZRoundPhase.Break;
        _zPhaseUntil = now + ZRules.BreakSeconds;
        ModeEvent($"Round {ZRound} survived. Next round in {ZRules.BreakSeconds:0} seconds.", Sfx.ReconBeep);
    }

    // ---- Areas -------------------------------------------------------------------------------------------

    /// <summary>Is area <paramref name="area"/> unlocked: the start area, or behind an open door?</summary>
    public bool AreaOpen(int area)
    {
        if (World.Map.Zombies is not { } zm)
            return false;
        if (area == zm.StartArea)
            return true;
        for (var d = 0; d < zm.Doors.Count; d++)
            if ((zm.Doors[d].AreaA == area || zm.Doors[d].AreaB == area) && World.Map.IsDoorOpen(d))
                return true;
        return false;
    }

    /// <summary>The first open door on the way from area <paramref name="from"/> to <paramref name="to"/> (-1: same area or no way).</summary>
    public int DoorTowards(int from, int to)
    {
        if (from == to || World.Map.Zombies is not { } zm)
            return -1;
        Span<int> via = stackalloc int[ZombiesMap.MaxAreas];
        Span<int> queue = stackalloc int[ZombiesMap.MaxAreas];
        via.Fill(-2);
        via[from] = -1;
        int head = 0, tail = 0;
        queue[tail++] = from;
        while (head < tail)
        {
            var a = queue[head++];
            for (var d = 0; d < zm.Doors.Count; d++)
            {
                var door = zm.Doors[d];
                if ((door.AreaA != a && door.AreaB != a) || !World.Map.IsDoorOpen(d))
                    continue;
                var b = door.Other(a);
                if (via[b] != -2)
                    continue;
                via[b] = d;
                if (b == to)
                {
                    // Walk back to the first door out of "from".
                    var cur = b;
                    var first = d;
                    while (true)
                    {
                        var dd = via[cur];
                        if (dd < 0)
                            break;
                        first = dd;
                        cur = zm.Doors[dd].Other(cur);
                    }
                    return first;
                }
                queue[tail++] = b;
            }
        }
        return -1;
    }

    // ---- Players ----------------------------------------------------------------------------------------

    /// <summary>Server, from Spawn: a Zombies player starts in the start area with the start gear (a fresh run, or back after bleeding out).</summary>
    internal void ZombiesOnSpawn(Player p)
    {
        if (World.Map.Zombies is not { } zm)
            return;
        ApplyPerks(p, PerkSet.Empty); // the multiplayer perks don't apply in Zombies; the perk machines do
        p.Grenade = p.Equipment = ThrowableKind.None;
        p.GrenadesLeft = p.EquipmentLeft = 0;
        var zp = ZP(p);
        zp.Downed = false;
        zp.SelfReviveAt = -1;
        // A spawn in the start area, by player id (deterministic; spread so nobody stands on anyone).
        var starts = World.Map.Spawns.Where(s => zm.Areas[zm.StartArea].Contains(s)).ToList();
        if (starts.Count == 0)
            starts = World.Map.Spawns;
        var at = starts[p.Id % starts.Count];
        p.Teleport(at);
        p.Yaw = 0;
        for (var i = 0; i < Player.HistorySize; i++)
            p.History[i] = new HistoryEntry { Position = at, Yaw = p.Yaw };
        if (zp.NeedsStartGear)
            GiveStartGear(p, quiet: _zRunMatch != MatchNumber);
    }

    /// <summary>Server: the start builds ("start" in Parts/Builds): the guns in slots 1-2, the melee weapon on the melee key.</summary>
    private void GiveStartGear(Player p, bool quiet = false)
    {
        var zp = ZP(p);
        zp.NeedsStartGear = false;
        p.ExtraSlots = false;
        p.BuiltExtra[0] = p.BuiltExtra[1] = null;
        zp.Slots[2] = zp.Slots[3] = null;
        var starts = StandardBuilds.From("start").ToList();
        var guns = starts.Where(b => !b.IsMelee).OrderBy(b => AssembleCached(b.Build()).Def?.Class == WeaponClass.Pistol ? 1 : 0).ToList(); // a pistol goes second
        var slot = 0;
        foreach (var b in guns.Take(2))
            GiveBuild(p, slot++, b.Build(), full: true, start: true);
        while (slot < 2)
        {
            // Not enough start guns in the data: a stock gun fills the slot.
            var stock = slot == 0 ? Weapons.DefaultPrimary : Weapons.DefaultSecondary;
            if (slot == 0)
            {
                p.BuiltPrimary = null;
                p.Primary = p.WantPrimary = stock.Id;
            }
            else
            {
                p.BuiltSecondary = null;
                p.Secondary = p.WantSecondary = stock.Id;
            }
            slot++;
        }
        if (starts.FirstOrDefault(b => b.IsMelee) is { } melee)
            GiveBuild(p, 4, melee.Build(), full: true, start: true);
        p.Weapon = p.Primary;
        if (p.Bot is { } brain)
        {
            brain.Ammo = p.DefFor(p.Primary).Mag;
            brain.ReloadUntil = 0;
        }
        if (!quiet && p.Id == LocalId)
            LoadoutNotice = "Zombies: you start with the Wall Rifle, the Wall Pistol and a Machete. Earn points, buy better.";
    }

    /// <summary>Server, every tick for every player: bleeding out, self-revive, the interact key (buy, revive, rebuild), pickups.</summary>
    private void TickZPlayer(Player p, ZPlayer zp, ZombiesMap zm, double now)
    {
        if (!p.Alive)
        {
            zp.Downed = false;
            zp.UseHeld = false;
            return;
        }
        zp.Spawned = true;
        _zAnySpawned = true;
        if (zp.Downed)
        {
            zp.BleedFraction = (float)Math.Clamp((zp.BleedAt - now) / ZRules.BleedoutSeconds, 0, 1);
            if (zp.SelfReviveAt > 0 && now >= zp.SelfReviveAt)
            {
                Revive(p, null, now);
                return;
            }
            if (now >= zp.BleedAt)
                BleedOut(p, zp, now);
            return;
        }

        // Pickups: walk over a part to take it.
        for (var i = ZDrops.Count - 1; i >= 0; i--)
        {
            var d = ZDrops[i];
            var dx = d.At.X - p.Position.X;
            var dz = d.At.Z - p.Position.Z;
            if (dx * dx + dz * dz > ZRules.PickupRange * ZRules.PickupRange || MathF.Abs(d.At.Y - p.Position.Y) > 2f)
                continue;
            ZDrops.RemoveAt(i);
            zp.Parts[d.Part] = zp.Parts.GetValueOrDefault(d.Part) + 1;
            var part = Parts.Find(d.Part);
            ZFeedTo(p, $"Picked up a part: {d.Part} ({(part != null ? PartDef.RarityName(part.Rarity) : "?")}). Fit it at a workbench.", Sfx.HitTick);
            SendZRun(p);
        }

        if (!zp.UseHeld)
        {
            zp.Reviving = byte.MaxValue;
            zp.ReviveProgress = 0;
            zp.UseDone = false;
            return;
        }
        var use = FindZUse(p);
        if (use is not { } u || u.Kind == ZUseKind.None)
        {
            zp.Reviving = byte.MaxValue;
            zp.ReviveProgress = 0;
            return;
        }
        switch (u.Kind)
        {
            case ZUseKind.Revive:
            {
                if (zp.Reviving != u.Index)
                {
                    zp.Reviving = (byte)u.Index;
                    zp.ReviveProgress = 0;
                }
                var mult = PerkMult(zp, d => d.Revive);
                zp.ReviveProgress += World.TickDt / MathF.Max(0.05f, ZRules.ReviveSeconds * mult);
                if (Players.TryGetValue((byte)u.Index, out var downed))
                {
                    ZP(downed).BeingRevived = zp.ReviveProgress;
                    if (zp.ReviveProgress >= 1f)
                    {
                        zp.ReviveProgress = 0;
                        zp.Reviving = byte.MaxValue;
                        zp.Revives++;
                        if (ZRules.RevivePoints > 0)
                            AddPoints(p, ZRules.RevivePoints);
                        Revive(downed, p, now);
                    }
                }
                break;
            }
            case ZUseKind.Repair:
                if (now >= zp.RepairNext && ZBoards[u.Index] < zm.Windows[u.Index].Boards)
                {
                    zp.RepairNext = now + ZRules.RepairSeconds;
                    ZBoards[u.Index]++;
                    if (zp.RepairPointsThisRound + ZRules.RepairPoints <= ZRules.RepairCapPerRound)
                    {
                        zp.RepairPointsThisRound += ZRules.RepairPoints;
                        AddPoints(p, ZRules.RepairPoints);
                    }
                    Audio.Effect3D(Sfx.ImpactWood, zm.Windows[u.Index].Inside + new Vector3(0, 1.2f, 0), 0.8f, MixBus.Zombies);
                }
                break;
            case ZUseKind.Workbench:
                break; // the client opens the workbench itself; its changes come back in ZBench
            default:
                if (!zp.UseDone && now - zp.UseSince >= ZRules.BuyHoldSeconds)
                {
                    zp.UseDone = true;
                    if (!u.Blocked)
                        Buy(p, zp, zm, u, now);
                    else if (p.Id == LocalId)
                        LoadoutNotice = u.Text;
                }
                break;
        }
    }

    /// <summary>Server: the interact key went down or up for <paramref name="p"/>.</summary>
    public void ServerSetUse(Player p, bool held, double now)
    {
        var zp = ZP(p);
        if (held == zp.UseHeld)
            return;
        zp.UseHeld = held;
        zp.UseSince = now;
        zp.UseDone = false;
        zp.RepairNext = now + 0.15;
        if (!held)
        {
            zp.Reviving = byte.MaxValue;
            zp.ReviveProgress = 0;
        }
    }

    /// <summary>A perk effect multiplied over the perks held (1 with none).</summary>
    private float PerkMult(ZPlayer zp, Func<ZombiePerkDef, float> f)
    {
        var m = 1f;
        for (var i = 0; i < ZRules.Perks.Count; i++)
            if (zp.Has(i))
                m *= f(ZRules.Perks[i]);
        return m;
    }

    /// <summary>Reload time multiplier from Zombies perks (Speed Cola) for <paramref name="p"/> (1 outside Zombies).</summary>
    public float ZombieReloadMult(Player? p) => p != null && Mode == GameMode.Zombies && ZPlayers.TryGetValue(p.Id, out var z) ? PerkMult(z, d => d.Reload) : 1f;

    /// <summary>Fire interval multiplier from Zombies perks (Double Tap) for <paramref name="p"/> (1 outside Zombies).</summary>
    public float ZombieFireIntervalMult(Player? p) => p != null && Mode == GameMode.Zombies && ZPlayers.TryGetValue(p.Id, out var z) ? 1f / PerkMult(z, d => d.FireRate) : 1f;

    /// <summary>Server: points for <paramref name="p"/> (the host's own +N popups come straight from here; clients see the change).</summary>
    public void AddPoints(Player p, int amount)
    {
        if (amount == 0)
            return;
        var zp = ZP(p);
        zp.Points = Math.Max(0, zp.Points + amount);
    }

    /// <summary>Server: a zombie's swipe landed on <paramref name="p"/>.</summary>
    public void ZombieHurt(Player p, int damage, double now, Vector3? from = null)
    {
        var zp = ZP(p);
        if (!p.Alive || zp.Downed || !DamageEnabled || now < p.ImmuneUntil)
            return;
        var dmg = Math.Max(1, (int)MathF.Round(damage * PerkMult(zp, d => d.DamageTaken)));
        p.Health -= dmg;
        BloodOnSwipe(p, from ?? p.Position + World.FlatForward(p.Yaw)); // blood marks (BodyBlood.cs)
        p.LastHurtTime = now;
        p.LastCombatTime = now;
        if (p.Health <= 0)
            Down(p, zp, now);
    }

    private void Down(Player p, ZPlayer zp, double now)
    {
        // A self-revive perk (Second Wind) gets you back up on your own, solo or co-op, and is used up by it.
        var selfPerk = -1;
        for (var i = 0; i < ZRules.Perks.Count && selfPerk < 0; i++)
            if (ZRules.Perks[i].SelfRevive && zp.Has(i))
                selfPerk = i;
        var selfRevive = selfPerk >= 0;
        if (selfRevive)
            zp.Perks &= (byte)~(1 << selfPerk); // consumed (even if perks were kept on down)
        zp.Downed = true;
        zp.Downs++;
        zp.DownedAt = now;
        zp.BleedAt = now + ZRules.BleedoutSeconds;
        zp.SelfReviveAt = selfRevive ? now + ZRules.SelfReviveSeconds : -1;
        if (ZRules.LosePerksOnDown)
            zp.Perks = 0; // every perk is lost when you go down
        zp.UseHeld = false;
        zp.BeingRevived = 0;
        p.Health = World.MaxHealth;
        ModeEvent(selfRevive ? $"{p.Name} is down! {ZRules.Perks[selfPerk].Name} gets them up in {ZRules.SelfReviveSeconds:0} seconds." : $"{p.Name} is down! Hold the interact key on them to revive.", Sfx.Death);
    }

    private void Revive(Player p, Player? by, double now)
    {
        var zp = ZP(p);
        zp.Downed = false;
        zp.SelfReviveAt = -1;
        zp.BeingRevived = 0;
        p.Health = World.MaxHealth;
        p.LastHurtTime = now;
        p.ImmuneUntil = Math.Max(p.ImmuneUntil, now + 1.0); // a moment to get up
        ModeEvent(by != null ? $"{by.Name} revived {p.Name}" : $"{p.Name} is back up", Sfx.Respawn);
    }

    private void BleedOut(Player p, ZPlayer zp, double now)
    {
        zp.Downed = false;
        zp.NeedsStartGear = true;
        zp.Points -= (int)(zp.Points * ZRules.BleedoutPointLoss);
        var dropped = ZRules.DropBuildsOnBleedout ? DropBuildsOnBleedout(p, zp, p.Position, now) : 0; // ZombiesDrops.cs
        p.Alive = false;
        p.Health = 0;
        p.Deaths++;
        p.RespawnAt = double.MaxValue; // back at the start of the next round (BeginRound)
        p.WantsRespawn = false;
        ModeEvent(dropped > 0
            ? $"{p.Name} bled out. They'll be back next round; their {(dropped == 1 ? "weapon is" : $"{dropped} weapons are")} on the floor where they went down for {ZRules.BuildDropSeconds / 60:0.#} minutes."
            : $"{p.Name} bled out. They'll be back next round.", Sfx.Death);
    }

    // ---- The interact key ----------------------------------------------------------------------------

    private const float UseRange = 1.7f;

    /// <summary>
    /// What the interact key would do for <paramref name="p"/> right now (both sides: the client shows the prompt, the
    /// server does it). Reviving a downed teammate comes first, then the nearest buyable in reach.
    /// </summary>
    public ZUse? FindZUse(Player p)
    {
        if (ZMap is not { } zm || !p.Alive || IsDowned(p) || Phase != MatchPhase.Playing)
            return null;
        var pos = p.Position;
        var zp = ZP(p);
        var solo = Players.Count <= 1;

        // A downed teammate in reach.
        foreach (var o in Players.Values)
        {
            if (o == p || !o.Alive || !IsDowned(o))
                continue;
            if (Flat(o.Position - pos) <= ZRules.ReviveRange)
                return new ZUse(ZUseKind.Revive, o.Id, 0, $"Hold [E] to revive {o.Name}");
        }

        ZUse? best = null;
        var bestD = float.MaxValue;
        void Consider(float d, float range, ZUse u)
        {
            if (d <= range && d < bestD)
            {
                bestD = d;
                best = u;
            }
        }

        for (var i = 0; i < zm.Doors.Count; i++)
        {
            if (World.Map.IsDoorOpen(i))
                continue;
            var door = zm.Doors[i];
            var closest = Vector3.Clamp(pos + new Vector3(0, 1, 0), door.Box.Min, door.Box.Max);
            var d = Flat(closest - pos);
            var what = door.Debris ? "clear the debris" : "open the door";
            Consider(d, 1.9f, door.NeedsPower && !ZPower
                ? new ZUse(ZUseKind.Door, i, door.Cost, $"{door.Name}: needs the power on", Blocked: true)
                : new ZUse(ZUseKind.Door, i, door.Cost, $"Hold [E] to {what} ({door.Name}) [{door.Cost}]"));
        }
        for (var i = 0; i < zm.WallBuys.Count; i++)
        {
            var wb = zm.WallBuys[i];
            var d = Flat(wb.At - pos);
            if (d > UseRange)
                continue;
            var cost = WallCost(wb);
            var owned = OwnsBuild(p, wb.Build) >= 0;
            if (owned)
            {
                var ammo = WallAmmoCost(wb);
                Consider(d, UseRange, new ZUse(ZUseKind.WallAmmo, i, ammo, $"Hold [E] to buy ammo for the {wb.Build} [{ammo}]"));
            }
            else
                Consider(d, UseRange, new ZUse(ZUseKind.WallBuy, i, cost, $"Hold [E] to buy the {wb.Build} [{cost}]"));
        }
        if (zm.BoxSpots.Count > 0)
        {
            var spot = zm.BoxSpots[Math.Clamp(BoxSpot, 0, zm.BoxSpots.Count - 1)];
            var d = Flat(spot.At - pos);
            var cost = BoxCost(zm);
            switch (BoxState)
            {
                case ZBoxState.Idle:
                    Consider(d, 1.9f, new ZUse(ZUseKind.Box, BoxSpot, cost, $"Hold [E] to spin the mystery box [{cost}]"));
                    break;
                case ZBoxState.Offering when BoxOwner == p.Id && BoxBuild >= 0 && BoxBuild < StandardBuilds.All.Length:
                    Consider(d, 1.9f, new ZUse(ZUseKind.BoxTake, BoxSpot, 0, $"Hold [E] to take the {StandardBuilds.All[BoxBuild].Name}"));
                    break;
            }
        }
        for (var i = 0; i < zm.PerkMachines.Count; i++)
        {
            var m = zm.PerkMachines[i];
            var d = Flat(m.At - pos);
            if (d > UseRange)
                continue;
            var k = ZRules.PerkIndex(m.Perk);
            if (k < 0)
                continue;
            var perk = ZRules.Perks[k];
            var cost = m.Cost > 0 ? m.Cost : perk.Price(solo);
            if (m.NeedsPower && !ZPower)
                Consider(d, UseRange, new ZUse(ZUseKind.Perk, i, cost, $"{perk.Name}: needs the power on", Blocked: true));
            else if (zp.Has(k))
                Consider(d, UseRange, new ZUse(ZUseKind.Perk, i, cost, $"You have {perk.Name}", Blocked: true));
            else if (PerkCount(zp) >= ZRules.MaxPerks)
                Consider(d, UseRange, new ZUse(ZUseKind.Perk, i, cost, $"You can't carry more than {ZRules.MaxPerks} perks", Blocked: true));
            else
                Consider(d, UseRange, new ZUse(ZUseKind.Perk, i, cost, $"Hold [E] to buy {perk.Name} [{cost}]: {perk.Blurb}"));
        }
        if (zm.SlotUpgrade is { } su)
        {
            var d = Flat(su.At - pos);
            var cost = SlotCost(zm);
            if (p.ExtraSlots)
                Consider(d, UseRange, new ZUse(ZUseKind.SlotUpgrade, 0, cost, "You have 4 weapon slots", Blocked: true));
            else if (su.NeedsPower && !ZPower)
                Consider(d, UseRange, new ZUse(ZUseKind.SlotUpgrade, 0, cost, "Weapon slots: needs the power on", Blocked: true));
            else
                Consider(d, UseRange, new ZUse(ZUseKind.SlotUpgrade, 0, cost, $"Hold [E] for 2 more weapon slots [{cost}]"));
        }
        if (zm.Power is { } pw && !ZPower)
            Consider(Flat(pw.At - pos), UseRange, new ZUse(ZUseKind.Power, 0, 0, "Hold [E] to turn on the power"));
        for (var i = 0; i < zm.Benches.Count; i++)
            Consider(Flat(zm.Benches[i].At - pos), 1.9f, new ZUse(ZUseKind.Workbench, i, 0, "Press [E] to use the workbench (your parts and scrap)"));
        for (var i = 0; i < zm.Salvage.Count && i < _salvageRound.Length; i++)
        {
            var s = zm.Salvage[i];
            var scrap = s.Cost > 0 ? s.Cost : ZRules.SalvageScrap;
            Consider(Flat(s.At - pos), UseRange, _salvageRound[i] == ZRound
                ? new ZUse(ZUseKind.Salvage, i, 0, "Picked clean this round", Blocked: true)
                : new ZUse(ZUseKind.Salvage, i, 0, $"Hold [E] to salvage scrap (+{scrap})"));
        }
        foreach (var wd in ZWeaponDrops)
        {
            var d = Flat(wd.At - pos);
            if (d <= UseRange && MathF.Abs(wd.At.Y - pos.Y) < 1.5f && d < bestD)
                Consider(d, UseRange, WeaponDropUse(p, wd)); // ZombiesDrops.cs
        }
        for (var i = 0; i < zm.Windows.Count; i++)
        {
            var w = zm.Windows[i];
            if (i < ZBoards.Length && ZBoards[i] < w.Boards)
                Consider(Flat(w.Inside - pos) + 0.3f, 1.6f, new ZUse(ZUseKind.Repair, i, 0, "Hold [E] to rebuild the barrier"));
        }
        return best;
    }


    private static int PerkCount(ZPlayer zp) => System.Numerics.BitOperations.PopCount(zp.Perks);

    public int WallCost(ZWallBuy wb) => wb.Cost > 0 ? wb.Cost : RunTables.Current.WallCost;

    public int WallAmmoCost(ZWallBuy wb) => wb.AmmoCost > 0 ? wb.AmmoCost : Math.Max(1, (int)MathF.Round(WallCost(wb) * ZRules.AmmoCostFraction / 10f) * 10);

    public int BoxCost(ZombiesMap zm) => zm.BoxCost > 0 ? zm.BoxCost : RunTables.Current.BoxCost;

    public int SlotCost(ZombiesMap zm) => zm.SlotUpgrade is { Cost: > 0 } su ? su.Cost : RunTables.Current.SlotUpgradeCost;

    /// <summary>Server: pays for and does a buy.</summary>
    private void Buy(Player p, ZPlayer zp, ZombiesMap zm, ZUse u, double now)
    {
        if (zp.Points < u.Cost)
        {
            if (p.Id == LocalId)
                LoadoutNotice = $"Not enough points ({zp.Points} of {u.Cost}).";
            Audio.Effect(Sfx.DryFire, 0.5f);
            return;
        }
        switch (u.Kind)
        {
            case ZUseKind.Door:
                zp.Points -= u.Cost;
                OpenDoor(u.Index, p);
                break;
            case ZUseKind.WallBuy when StandardBuilds.Find(zm.WallBuys[u.Index].Build) is { } sb:
                zp.Points -= u.Cost;
                GiveBuild(p, TargetSlot(p), sb.Build(), full: true);
                ZFeedTo(p, $"Bought the {sb.Name}.", Sfx.LevelUp);
                break;
            case ZUseKind.WallAmmo:
            {
                var slot = OwnsBuild(p, zm.WallBuys[u.Index].Build);
                if (slot < 0)
                    return;
                zp.Points -= u.Cost;
                RefillAmmo(p, slot);
                ZFeedTo(p, $"Ammo for the {zm.WallBuys[u.Index].Build}.", Sfx.HitTick);
                break;
            }
            case ZUseKind.Box:
                zp.Points -= u.Cost;
                SpinBox(p, zm, now);
                break;
            case ZUseKind.BoxTake when BoxBuild >= 0 && BoxBuild < StandardBuilds.All.Length:
            {
                var ob = StandardBuilds.All[BoxBuild];
                GiveBuild(p, ob.IsMelee ? 4 : TargetSlot(p), ob.Build(), full: true);
                ZFeedTo(p, $"You took the {ob.Name} ({PartDef.RarityName(ob.Tier)}).", Sfx.LevelUp);
                BoxState = ZBoxState.Idle;
                BoxBuild = -1;
                BoxOwner = byte.MaxValue;
                break;
            }
            case ZUseKind.Perk:
            {
                var k = ZRules.PerkIndex(zm.PerkMachines[u.Index].Perk);
                zp.Points -= u.Cost;
                zp.Perks |= (byte)(1 << k);
                ModeEvent($"{p.Name} drank {ZRules.Perks[k].Name}", Sfx.LevelUp);
                break;
            }
            case ZUseKind.SlotUpgrade:
                zp.Points -= u.Cost;
                ServerBuySlots(p);
                ModeEvent($"{p.Name} bought 2 more weapon slots", Sfx.LevelUp);
                break;
            case ZUseKind.Power:
                ZPower = true;
                ModeEvent($"{p.Name} turned on the power!", Sfx.StreakReady);
                ZBanner("THE POWER IS ON");
                break;
            case ZUseKind.Pickup:
                ClaimWeaponDrop(p, u.Index, now); // ZombiesDrops.cs
                break;
            case ZUseKind.Salvage:
            {
                var s = zm.Salvage[u.Index];
                _salvageRound[u.Index] = ZRound;
                zp.Scrap += s.Cost > 0 ? s.Cost : ZRules.SalvageScrap;
                ZFeedTo(p, $"Salvaged {(s.Cost > 0 ? s.Cost : ZRules.SalvageScrap)} scrap ({zp.Scrap} now).", Sfx.BreakMetal);
                SendZRun(p);
                break;
            }
        }
    }

    /// <summary>Server: opens a door (unlocking the area behind it: its spawners and routes go live).</summary>
    public void OpenDoor(int door, Player? by)
    {
        if (World.Map.Zombies is not { } zm || World.Map.IsDoorOpen(door))
            return;
        World.Map.SetDoorOpen(door, true);
        var d = zm.Doors[door];
        ModeEvent(by != null ? $"{by.Name} opened {d.Name}: {zm.Areas[d.AreaA].Name} - {zm.Areas[d.AreaB].Name}" : $"{d.Name} opened", d.Debris ? Sfx.BreakWood : Sfx.BreakMetal);
        ZDoorOpenedAt[door] = Brutal.GlfwApi.Glfw.Time;
    }

    /// <summary>When each door opened (client clock), for the opening animation.</summary>
    public readonly double[] ZDoorOpenedAt = new double[ZombiesMap.MaxDoors];

    // ---- Weapons ------------------------------------------------------------------------------------------

    /// <summary>The slot holding a build called <paramref name="name"/> (0-3), or -1.</summary>
    public static int OwnsBuild(Player p, string name)
    {
        bool Is(WeaponDef? d) => d != null && d.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
        if (Is(p.BuiltPrimary))
            return 0;
        if (Is(p.BuiltSecondary))
            return 1;
        if (p.ExtraSlots && Is(p.BuiltExtra[0]))
            return 2;
        if (p.ExtraSlots && Is(p.BuiltExtra[1]))
            return 3;
        return -1;
    }

    /// <summary>The slot a new gun goes to: an empty slot 3-4 if you have them, else the one in your hands.</summary>
    public static int TargetSlot(Player p)
    {
        if (p.ExtraSlots && p.BuiltExtra[0] == null)
            return 2;
        if (p.ExtraSlots && p.BuiltExtra[1] == null)
            return 3;
        return SlotOf(p, p.Weapon) is var s and >= 0 ? s : 0;
    }

    /// <summary>Which slot (0-3) weapon id <paramref name="weapon"/> is in for <paramref name="p"/>, or -1.</summary>
    public static int SlotOf(Player p, byte weapon)
    {
        if (weapon == p.Primary)
            return 0;
        if (weapon == p.Secondary)
            return 1;
        if (p.ExtraSlots && p.BuiltExtra[0]?.Id == weapon)
            return 2;
        if (p.ExtraSlots && p.BuiltExtra[1]?.Id == weapon)
            return 3;
        return -1;
    }

    private static readonly Dictionary<string, AssembledWeapon> AssembledCache = new();

    /// <summary>Drops every cached assembled weapon (after a part hot reload: the same text now assembles differently).</summary>
    internal static void ForgetAssembled()
    {
        lock (AssembledCache)
            AssembledCache.Clear();
    }

    /// <summary>A build assembled (cached by its text: the same text always gives the same weapon, on every PC).</summary>
    public static AssembledWeapon AssembleCached(WeaponBuild b)
    {
        var key = b.ToText();
        if (!AssembledCache.TryGetValue(key, out var a))
        {
            if (AssembledCache.Count > 256)
                AssembledCache.Clear();
            AssembledCache[key] = a = WeaponBuilder.Assemble(b);
        }
        return a;
    }

    /// <summary>
    /// Server: puts build <paramref name="b"/> in <paramref name="slot"/> of <paramref name="p"/> (0-3 guns, 4 the melee
    /// weapon) and tells everyone (<see cref="Msg.ZBuild"/>: clients assemble the same text into the same weapon). Each
    /// gun in a player's slots gets its own weapon id (built guns borrow a shipped gun's id for their sounds).
    /// <paramref name="full"/>: a full magazine and full reserve; otherwise the reserve stays as it was (workbench edits).
    /// </summary>
    public bool GiveBuild(Player p, int slot, WeaponBuild b, bool full, bool start = false)
    {
        var a = AssembleCached(b);
        var zp = ZP(p);
        if (slot == 4)
        {
            if (a.Melee == null)
                return false;
            p.MeleeBuild = a.Melee;
            zp.Slots[4] = b.Clone();
            BroadcastBuild(p, 4, 0, b, full, start);
            return true;
        }
        if (a.Def == null || slot is < 0 or > 3 || (slot >= 2 && !p.ExtraSlots))
            return false;
        var def = PlaceBuilt(p, slot, a.Def);
        zp.Slots[slot] = b.Clone();
        p.Weapon = def.Id;
        if (p.Bot is { } brain)
        {
            brain.Ammo = def.Mag;
            brain.ReloadUntil = 0;
        }
        BroadcastBuild(p, slot, def.Id, b, full, start);
        _zBuildsGiven = true;
        return true;
    }

    /// <summary>Both sides: puts a copy of <paramref name="built"/> in a player's slot with an id none of their other slots use.</summary>
    private static WeaponDef PlaceBuilt(Player p, int slot, WeaponDef built, byte? id = null)
    {
        var used = new HashSet<byte>();
        if (slot != 0)
            used.Add(p.Primary);
        if (slot != 1)
            used.Add(p.Secondary);
        if (slot != 2 && p.BuiltExtra[0] != null)
            used.Add(p.BuiltExtra[0]!.Id);
        if (slot != 3 && p.BuiltExtra[1] != null)
            used.Add(p.BuiltExtra[1]!.Id);
        var def = built.Clone();
        def.Id = id ?? (used.Contains(built.Id) ? Weapons.All.First(w => !used.Contains(w.Id)).Id : built.Id);
        switch (slot)
        {
            case 0:
                p.BuiltPrimary = def;
                p.Primary = p.WantPrimary = def.Id;
                p.PrimaryKit = p.WantPrimaryKit = 0;
                break;
            case 1:
                p.BuiltSecondary = def;
                p.Secondary = p.WantSecondary = def.Id;
                p.SecondaryKit = p.WantSecondaryKit = 0;
                break;
            default:
                p.BuiltExtra[slot - 2] = def;
                break;
        }
        return def;
    }

    /// <summary>The local player's slot <paramref name="slot"/> now holds <paramref name="def"/> (in hand straight away).</summary>
    private void LocalSetSlot(int slot, WeaponDef def, bool full, bool start, bool select)
    {
        var old = Slots[slot];
        var mags = start ? ZRules.StartReserveMags : ZRules.ReserveMags;
        Slots[slot] = new WeaponState(def) { Reserve = full ? def.Mag * mags : Math.Max(0, old.Reserve) };
        if (!select)
            return;
        var now = Brutal.GlfwApi.Glfw.Time;
        ActiveSlot = slot;
        EquipUntil = now + def.EquipTime * FrenzyBuffs.Value(Local, def, FrenzyStat.EquipTime); // the Pistol frenzy buff
        NextFireTime = Math.Max(NextFireTime, EquipUntil);
        Audio.Equip(def);
    }

    /// <summary>Server: full ammo for the gun in <paramref name="slot"/> (wall ammo).</summary>
    private void RefillAmmo(Player p, int slot)
    {
        if (p.Bot is { } b)
            b.Ammo = p.DefFor(p.Weapon).Mag;
        if (p.Id == LocalId)
            LocalRefill(slot);
        else
            SendZEvent(p, ZEventKind.Refill, (ushort)slot, "");
    }

    private void LocalRefill(int slot)
    {
        var w = Slots[Math.Clamp(slot, 0, 3)];
        w.Ammo = w.Def.Mag;
        w.Reserve = w.Def.Mag * ZRules.ReserveMags;
        Audio.Reload(w.Def, null);
    }

    // ---- The mystery box -----------------------------------------------------------------------------

    private void SpinBox(Player p, ZombiesMap zm, double now)
    {
        BoxUses++;
        BoxOwner = p.Id;
        BoxState = ZBoxState.Spinning;
        _boxUntil = now + ZRules.BoxSpinSeconds;
        if (BoxUses > _boxMoveAfter && zm.BoxSpots.Count > 1)
            BoxBuild = -2; // the teddy bear (shown when the spin ends)
        else
            BoxBuild = RollBoxBuild(p, ZRound, ZNext());
        Audio.Effect3D(Sfx.StreakCall, zm.BoxSpots[BoxSpot].At, 0.8f);
    }

    /// <summary>
    /// A complete build from the mystery box: its tier rolled from DropTables.json's "boxTiers" for the round, then one of
    /// the "box" builds of that tier the player doesn't already carry (the nearest tier with one, if that tier has none).
    /// Returns an index into <see cref="StandardBuilds.All"/>.
    /// </summary>
    public int RollBoxBuild(Player? p, int round, uint seed)
    {
        var all = StandardBuilds.All;
        var box = Enumerable.Range(0, all.Length).Where(i => all[i].Sources.Contains("box")).ToList();
        if (box.Count == 0)
            return -1;
        var tier = RunTables.Roll(RunTables.Current.BoxTiers, Math.Max(1, round), seed);
        var notOwned = box.Where(i => p == null || OwnsBuild(p, all[i].Name) < 0).ToList();
        if (notOwned.Count == 0)
            notOwned = box;
        for (var step = 0; step < 6; step++)
        {
            foreach (var t in step == 0 ? new[] { (int)tier } : new[] { (int)tier - step, (int)tier + step })
            {
                var pool = notOwned.Where(i => (int)all[i].Tier == t).ToList();
                if (pool.Count > 0)
                    return pool[(int)((seed >> 7) % (uint)pool.Count)];
            }
        }
        return notOwned[(int)(seed % (uint)notOwned.Count)];
    }

    private void TickBox(ZombiesMap zm, double now)
    {
        if (zm.BoxSpots.Count == 0)
            return;
        switch (BoxState)
        {
            case ZBoxState.Spinning when now >= _boxUntil:
                if (BoxBuild == -2)
                {
                    BoxState = ZBoxState.Teddy;
                    _boxUntil = now + ZRules.BoxMoveSeconds;
                    if (Players.TryGetValue(BoxOwner, out var owner))
                        AddPoints(owner, BoxCost(zm)); // the teddy bear refunds the spin
                    ModeEvent("The teddy bear! The mystery box is moving (your points are refunded).", Sfx.JetFlyby);
                }
                else
                {
                    BoxState = ZBoxState.Offering;
                    _boxUntil = now + ZRules.BoxOfferSeconds;
                }
                break;
            case ZBoxState.Offering when now >= _boxUntil:
                BoxState = ZBoxState.Idle;
                BoxBuild = -1;
                BoxOwner = byte.MaxValue;
                break;
            case ZBoxState.Teddy when now >= _boxUntil:
            {
                var next = ZRange(zm.BoxSpots.Count - 1);
                BoxSpot = next >= BoxSpot ? next + 1 : next;
                BoxSpot = Math.Clamp(BoxSpot, 0, zm.BoxSpots.Count - 1);
                BoxState = ZBoxState.Idle;
                BoxBuild = -1;
                BoxOwner = byte.MaxValue;
                BoxUses = 0;
                _boxMoveAfter = ZRules.BoxMoveMin + ZRange(ZRules.BoxMoveMax - ZRules.BoxMoveMin + 1);
                ModeEvent($"The mystery box is now in {zm.Areas[zm.BoxSpots[BoxSpot].Area].Name}.", Sfx.ReconBeep);
                break;
            }
        }
        BoxLeft = BoxState == ZBoxState.Idle ? 0 : Math.Max(0, _boxUntil - now);
    }

    // ---- Parts: drops and the workbench ------------------------------------------------------------------

    /// <summary>Server: a zombie died at <paramref name="at"/> in round <paramref name="round"/>: maybe a part drops (DropTables.json).</summary>
    private void RollDrop(Vector3 at, int round, double now)
    {
        var tables = RunTables.Current;
        var chance = RunTables.RowFor(tables.DropChance, round)?.Chance ?? 0;
        if (ZRand() >= chance)
            return;
        var tier = RunTables.Roll(tables.PartDrops, round, ZNext());
        var pool = Parts.All.Where(p => p.Rarity == tier).ToList();
        if (pool.Count == 0)
            return;
        var part = pool[ZRange(pool.Count)];
        if (ZDrops.Count >= 32)
            ZDrops.RemoveAt(0);
        ZDrops.Add(new ZDrop { Id = _zNextDrop++, Part = part.Name, At = at + new Vector3(0, 0.15f, 0), Until = now + ZRules.DropSeconds });
    }

    private void TickDrops(double now) => ZDrops.RemoveAll(d => d.Until <= now);

    /// <summary>Highest part tier the workbench can craft this round (the tiers the drop table can roll now; never wonder).</summary>
    public Rarity CraftTierCap(int round)
    {
        var row = RunTables.RowFor(RunTables.Current.PartDrops, Math.Max(1, round));
        var best = Rarity.Common;
        if (row != null)
            for (var i = 0; i < row.Weights.Length; i++)
                if (row.Weights[i] > 0 && (Rarity)i != Rarity.Wonder)
                    best = (Rarity)i;
        return best;
    }

    /// <summary>
    /// Server: a workbench change from <paramref name="p"/> (in <see cref="Msg.ZBench"/>, or the host's own): 0 = put
    /// <paramref name="text"/> in slot <paramref name="slot"/> (the parts it adds must be in their loose parts; parts it
    /// drops go back there), 1 = break a loose part down for scrap, 2 = craft a part with scrap. Returns why not, or null.
    /// </summary>
    public string? ServerBench(Player p, int kind, int slot, string text)
    {
        if (ZMap is not { } zm || !p.Alive || IsDowned(p))
            return "Not now.";
        if (!zm.Benches.Any(b => Flat(b.At - p.Position) <= 3.5f))
            return "You're not at a workbench.";
        var zp = ZP(p);
        switch (kind)
        {
            case 0:
            {
                if (slot is < 0 or > 4 || (slot is 2 or 3 && !p.ExtraSlots))
                    return "That slot isn't yours.";
                WeaponBuild nb;
                var dropped = new List<string>();
                try
                {
                    nb = WeaponBuild.Parse(text, Parts.Find, dropped);
                }
                catch (FormatException ex)
                {
                    return $"That isn't a build: {ex.Message}";
                }
                if (dropped.Count > 0)
                    return $"Unknown parts: {string.Join(", ", dropped)}";
                var asm = AssembleCached(nb);
                if (!asm.Ok || (slot == 4) != nb.IsMelee)
                    return asm.Problems.Count > 0 && !asm.Ok ? asm.Problems[0].Message : slot == 4 ? "The melee slot takes a melee build." : "Gun slots take a gun build.";
                // Parts accounting: the new build may use its old parts plus loose ones.
                var oldCount = PartCounts(zp.Slots[slot]);
                var newCount = PartCounts(nb);
                foreach (var (name, n) in newCount)
                {
                    var have = oldCount.GetValueOrDefault(name) + zp.Parts.GetValueOrDefault(name);
                    if (n > have)
                        return $"You don't have the {name} (have {have}, the build needs {n}).";
                }
                foreach (var name in oldCount.Keys.Union(newCount.Keys).ToList())
                {
                    var left = zp.Parts.GetValueOrDefault(name) + oldCount.GetValueOrDefault(name) - newCount.GetValueOrDefault(name);
                    if (left > 0)
                        zp.Parts[name] = left;
                    else
                        zp.Parts.Remove(name);
                }
                GiveBuild(p, slot, nb, full: false);
                SendZRun(p);
                ZFeedTo(p, $"Workbench: {nb.Name} is in slot {(slot == 4 ? "melee" : (slot + 1).ToString())}.", Sfx.HitTick);
                return null;
            }
            case 1:
            {
                if (!zp.Parts.TryGetValue(text, out var n) || n <= 0 || Parts.Find(text) is not { } part)
                    return "You don't have that part loose.";
                if (n == 1)
                    zp.Parts.Remove(text);
                else
                    zp.Parts[text] = n - 1;
                zp.Scrap += Scrap.SalvageValue(part);
                SendZRun(p);
                return null;
            }
            case 2:
            {
                if (Parts.Find(text) is not { } part)
                    return "No such part.";
                var cost = Scrap.CraftCost(part);
                if (cost < 0 || part.Rarity > CraftTierCap(ZRound))
                    return $"The {part.Name} can't be crafted yet (round {ZRound} crafts up to {PartDef.RarityName(CraftTierCap(ZRound))}).";
                if (zp.Scrap < cost)
                    return $"Not enough scrap ({zp.Scrap} of {cost}).";
                zp.Scrap -= cost;
                zp.Parts[part.Name] = zp.Parts.GetValueOrDefault(part.Name) + 1;
                SendZRun(p);
                return null;
            }
        }
        return "Unknown workbench action.";
    }

    private static Dictionary<string, int> PartCounts(WeaponBuild? b)
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (b != null)
            foreach (var n in b.Nodes)
                d[n.Part.Name] = d.GetValueOrDefault(n.Part.Name) + 1;
        return d;
    }

    // ---- Self-test hooks ------------------------------------------------------------------------------

    /// <summary>Self-test: run the Zombies logic for one tick at <paramref name="now"/> (no player movement).</summary>
    public void SelfTestZombiesTick(double now)
    {
        ServerTick++;
        if (Phase == MatchPhase.Playing)
            ServerTickZombies(now);
    }

    /// <summary>Self-test: start the next round now.</summary>
    public void SelfTestBeginRound(int round, double now)
    {
        if (_zRunMatch != MatchNumber)
            StartZombiesRun(now);
        BeginRound(round, now);
    }

    /// <summary>Self-test: the interact key for <paramref name="p"/> as if it were held for a while.</summary>
    public void SelfTestUse(Player p, double now)
    {
        ServerSetUse(p, true, now - 1);
        TickZPlayer(p, ZP(p), ZMap!, now);
        ServerSetUse(p, false, now);
    }

    public void SelfTestDown(Player p, double now) => Down(p, ZP(p), now);

    /// <summary>Self-test: a shot from <paramref name="p"/>'s gun in hand through the server's shot path (no spread).</summary>
    public void SelfTestServerShot(Player p, Vector3 origin, Vector3 dir, double now) => ServerResolveShot(p, p.WeaponDef, origin, dir, 0f, 1, ServerTick, now);

    public int SelfTestBoxMoveAfter
    {
        get => _boxMoveAfter;
        set => _boxMoveAfter = value;
    }

    public int SelfTestToSpawn => _zToSpawn;

    public void SelfTestRollDrop(Vector3 at, int round, double now) => RollDrop(at, round, now);
}
