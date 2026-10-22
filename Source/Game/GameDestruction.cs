using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Destructible buildings, server-authoritative (Buildings/README.md, "Destruction"; the chunks are <see cref="MapDestruction"/>).
/// Blasts (grenades, launchers, kill streaks, exploding props and mines) do most of the work; bullets chip only the weak
/// materials (wood, plaster) and melee breaks those too, as Materials/destruction.json says. The server decides which chunks
/// break and tells everyone (<see cref="Msg.StructBreak"/>: the chunks the hit broke); every PC then breaks them and runs the
/// same support flood (<see cref="Map.CollapseStructure"/>), so what falls is the same everywhere without being sent. A
/// player who joins mid-match gets every broken chunk (<see cref="Msg.StructState"/>, a bit per chunk). Falling pieces
/// land on whoever is under them (server); debris, dust and sound are cosmetic on every PC. Off in Zombies and when the
/// host's "Destruction" setting is off; everything is whole again at the start of every match.
/// </summary>
public sealed partial class Game
{
    /// <summary>The host's "Destruction" setting: off, nothing breaks (the chunks still stand, the map is the same).</summary>
    public bool DestructionEnabled = true;

    /// <summary>Most chunk exposure rays one blast casts (the nearest-first order of the loop keeps it fair).</summary>
    public const int MaxBlastRays = 512;

    /// <summary>Debris pieces one break event may throw (shared over its chunks), and falling slabs shown per event.</summary>
    public const int MaxDebrisPerEvent = 150, MaxSlabsPerEvent = 48;

    /// <summary>Most chunk ids in one <see cref="Msg.StructBreak"/> packet (2 bytes each).</summary>
    public const int StructBreakBatch = 500;

    /// <summary>Is destruction on for this game on this map (the server's view)?</summary>
    public bool DestructionLive => DestructionEnabled && DestructionAllowedIn(Mode) && World.Map.Destruction != null;

    /// <summary>Modes that break buildings: all but Zombies (its rounds are built around doors, barricades and fixed routes).</summary>
    public static bool DestructionAllowedIn(GameMode mode) => mode != GameMode.Zombies;

    /// <summary>Chunks broken and fallen since this game started (for the self-test and stats).</summary>
    public int ChunksBroken { get; private set; }

    /// <inheritdoc cref="ChunksBroken"/>
    public int ChunksFallen { get; private set; }

    /// <summary>Falling pieces that landed on someone, and the damage they did (self-test).</summary>
    public int FallHits { get; private set; }

    private readonly List<int> _dHits = new(), _dBroken = new(), _dFallen = new(), _dProps = new(), _dDoors = new(), _dWindows = new();

    private struct FallingPiece
    {
        public Box Box;
        public float FloorY, Damage;
        public double At;
        public byte Attacker, KillId;
    }

    private FallingPiece[] _falls = [];
    private int _fallCount;
    private Map? _fallMap;
    private int _fallMatch = -1;

    // ---- Server: damage --------------------------------------------------------------------------------------------

    /// <summary>Server: a bullet from <paramref name="origin"/> along <paramref name="dir"/> that stopped at <paramref name="dist"/> chips the chunk it hit (weak materials only).</summary>
    internal void ShotStructures(Player? shooter, Vector3 origin, Vector3 dir, float dist, float damage, byte killId, double now)
    {
        if (!IsServer || !DestructionLive || !(damage > 0))
            return;
        var d = World.Map.Destruction!;
        if (!World.Query.Raycast(origin, dir, dist + 0.05f, QueryFilter.Shots, out var hit) || hit.Kind != ShapeKind.Box || hit.Distance < dist - 0.06f
            || !d.IsChunk(hit.Shape.Index, out var c))
            return;
        c = d.Target(c);
        var mult = d.MaterialOf(c).Mult[(int)StructureHit.Bullet];
        if (mult <= 0 || d.Broken[c])
            return;
        if (World.Map.DamageChunk(c, damage * mult))
        {
            _dHits.Clear();
            _dHits.Add(c);
            ServerBreakChunks(_dHits, StructureHit.Bullet, origin, shooter, killId, now);
        }
    }

    /// <summary>Server: a melee swing that hit nobody: the chunk in reach along the aim takes it (wood and plaster give way). True if it hit one that can break.</summary>
    internal bool MeleeStructures(Player? attacker, Vector3 origin, Vector3 dir, double now)
    {
        if (!IsServer || !DestructionLive)
            return false;
        var d = World.Map.Destruction!;
        if (!World.Query.Raycast(origin, dir, MeleeRange + 0.3f, QueryFilter.Shots, out var hit) || hit.Kind != ShapeKind.Box || !d.IsChunk(hit.Shape.Index, out var c))
            return false;
        c = d.Target(c);
        var mult = d.MaterialOf(c).Mult[(int)StructureHit.Melee];
        if (mult <= 0 || d.Broken[c])
            return false;
        if (World.Map.DamageChunk(c, MeleeObjectDamage * mult))
        {
            _dHits.Clear();
            _dHits.Add(c);
            ServerBreakChunks(_dHits, StructureHit.Melee, origin, attacker, MeleeKillId, now);
        }
        return true;
    }

    /// <summary>
    /// Server: a blast at <paramref name="center"/>. Every chunk in reach that the blast can see (nothing else in between)
    /// takes <paramref name="maxDamage"/> x its material's multiplier for <paramref name="hit"/>, falling off with distance
    /// like it does for players. What breaks breaks; what loses its support falls.
    /// </summary>
    internal void BlastStructures(Player? owner, Vector3 center, float radius, float maxDamage, StructureHit hit, byte killId, double now)
    {
        if (!IsServer || !DestructionLive || !(radius > 0) || !(maxDamage > 0))
            return;
        var map = World.Map;
        var d = map.Destruction!;
        _dHits.Clear();
        var rays = 0;
        foreach (var st in d.Structures)
        {
            var bb = st.Bounds;
            if (Vector3.Distance(Vector3.Clamp(center, bb.Min, bb.Max), center) > radius)
                continue;
            for (var c = st.ChunkStart; c < st.ChunkStart + st.ChunkCount; c++)
            {
                if (d.Broken[c] || (d.Flags[c] & (MapDestruction.FlagSkin | MapDestruction.FlagFixed)) != 0)
                    continue;
                var mult = d.MaterialOf(c).Mult[(int)hit];
                if (mult <= 0)
                    continue;
                var b = d.Original[c];
                var closest = Vector3.Clamp(center, b.Min, b.Max);
                var to = closest - center;
                var dist = to.Length();
                if (dist > radius)
                    continue;
                var dmg = maxDamage * mult * MathF.Pow(1f - dist / radius, 1.4f);
                if (dmg < d.MaxHp[c] * 0.02f && dmg < d.Hp[c])
                    continue; // too little to matter
                if (dist > 0.3f)
                {
                    if (rays >= MaxBlastRays)
                        continue;
                    rays++;
                    if (World.Query.Raycast(center, to / dist, dist, QueryFilter.Shots, out var rh) && rh.Distance < dist - 0.08f
                        && !(rh.Kind == ShapeKind.Box && d.IsChunk(rh.Shape.Index, out var hc) && d.Target(hc) == c))
                        continue; // shielded
                }
                if (map.DamageChunk(c, dmg))
                    _dHits.Add(c);
            }
        }
        if (_dHits.Count > 0)
            ServerBreakChunks(_dHits, hit, center, owner, killId, now);
    }

    /// <summary>Server: breaks <paramref name="chunks"/> (sorted by id), one event per structure, then lets what lost its support fall.</summary>
    private void ServerBreakChunks(List<int> chunks, StructureHit hit, Vector3 from, Player? by, byte killId, double now)
    {
        var map = World.Map;
        var d = map.Destruction!;
        var i = 0;
        while (i < chunks.Count)
        {
            var s = d.Owner[chunks[i]];
            var j = i;
            while (j < chunks.Count && d.Owner[chunks[j]] == s)
                j++;
            ApplyStructureBreaks(map, s, chunks, i, j - i, hit, from, true);
            if (_dBroken.Count > 0)
            {
                var st = d.Structures[s];
                for (var at = 0; at < _dBroken.Count; at += StructBreakBatch)
                {
                    WriteStructureBreak(_w, s, hit, from, st.ChunkStart, _dBroken, at, Math.Min(StructBreakBatch, _dBroken.Count - at));
                    Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
                }
                ScheduleFalls(_dFallen, by, killId, now);
                NoteStructureChanged(s, now); // bots re-plan around it (GameDestructionNav.cs)
            }
            i = j;
        }
    }

    /// <summary>
    /// Both sides: breaks <paramref name="count"/> chunks of structure <paramref name="s"/> from <paramref name="chunks"/>
    /// (at <paramref name="start"/>), runs the collapse and plays the effects. Leaves what broke in <c>_dBroken</c> and what
    /// fell in <c>_dFallen</c>.
    /// </summary>
    private void ApplyStructureBreaks(Map map, int s, List<int> chunks, int start, int count, StructureHit hit, Vector3 from, bool server)
    {
        _dBroken.Clear();
        _dFallen.Clear();
        _dProps.Clear();
        _dDoors.Clear();
        _dWindows.Clear();
        for (var k = start; k < start + count; k++)
            if (map.BreakChunk(chunks[k], _dDoors, _dWindows))
                _dBroken.Add(map.Destruction!.Target(chunks[k]));
        if (_dBroken.Count == 0)
            return;
        map.CollapseStructure(s, _dFallen, _dProps, _dDoors, _dWindows);
        ChunksBroken += _dBroken.Count;
        ChunksFallen += _dFallen.Count;
        if (ReferenceEquals(map, World.Map))
            OnStructureBroken(map, hit, from);
        _ = server;
    }

    // ---- Server: falling pieces ------------------------------------------------------------------------------------

    private void EnsureFallState()
    {
        var map = World.Map;
        var cap = MaterialDestruction.Current.Rules.MaxFalling;
        if (_falls.Length != cap)
            _falls = new FallingPiece[cap];
        if (!ReferenceEquals(_fallMap, map) || _fallMatch != MatchNumber)
        {
            _fallCount = 0;
            _fallMap = map;
            _fallMatch = MatchNumber;
        }
    }

    /// <summary>Server: the fallen chunks drop straight down; each lands (and hurts whoever is under it) when it would reach the floor below.</summary>
    private void ScheduleFalls(List<int> fallen, Player? by, byte killId, double now)
    {
        if (fallen.Count == 0)
            return;
        EnsureFallState();
        var d = World.Map.Destruction!;
        var rules = d.Table.Rules;
        for (var pass = 0; pass < 2; pass++) // floor and roof slabs first: they're what lands on people
        foreach (var c in fallen)
        {
            if (_fallCount >= _falls.Length)
                break;
            if ((d.Flags[c] & MapDestruction.FlagSkin) != 0 || ((d.Flags[c] & MapDestruction.FlagFloor) != 0) != (pass == 0))
                continue;
            var b = d.Original[c];
            var size = b.Half * 2;
            var tonnes = size.X * size.Y * size.Z * d.MaterialOf(c).Density;
            var start = new Vector3(b.Center.X, b.Min.Y - 0.02f, b.Center.Z);
            var drop = World.Query.Raycast(start, -Vector3.UnitY, 40f, QueryFilter.Shots);
            var floor = start.Y - drop;
            var h = MathF.Max(0.3f, b.Min.Y - floor);
            var dmg = MathF.Min(rules.FallDamageMax, rules.FallDamage * tonnes * MathF.Sqrt(h));
            if (dmg < 4f)
                continue;
            _falls[_fallCount++] = new FallingPiece
            {
                Box = b, FloorY = floor, Damage = dmg, At = now + MathF.Sqrt(2f * h / World.Gravity), Attacker = by?.Id ?? byte.MaxValue, KillId = killId,
            };
        }
    }

    /// <summary>Server, every tick: falling pieces that reached the floor land on whoever stands under them.</summary>
    private void ServerTickDestruction(double now)
    {
        ServerTickDestructionNav(); // bots' nav graph follows the holes (GameDestructionNav.cs)
        if (_fallCount == 0)
            return;
        EnsureFallState();
        for (var i = 0; i < _fallCount; i++)
        {
            ref var f = ref _falls[i];
            if (f.At > now)
                continue;
            var b = f.Box;
            float x0 = b.Min.X + 0.1f, x1 = b.Max.X - 0.1f, z0 = b.Min.Z + 0.1f, z1 = b.Max.Z - 0.1f;
            float y0 = f.FloorY - 0.1f, y1 = f.FloorY + 1.9f;
            Players.TryGetValue(f.Attacker, out var attacker);
            foreach (var victim in Players.Values)
            {
                if (!victim.Alive)
                    continue;
                var hb = World.Hitbox(victim.Position, victim.Sim.Stance, victim.Yaw);
                if (hb.Max.X <= x0 || hb.Min.X >= x1 || hb.Max.Z <= z0 || hb.Min.Z >= z1 || hb.Max.Y <= y0 || hb.Min.Y >= y1)
                    continue;
                var amount = (int)MathF.Round(f.Damage);
                if (amount <= 0)
                    continue;
                if (DamagePlayer(attacker ?? victim, victim, amount, false, f.KillId, now, DamageKind.Explosive, new Vector3(b.Center.X, f.FloorY + 3f, b.Center.Z)))
                    FallHits++;
            }
            _falls[i] = _falls[--_fallCount];
            i--;
        }
    }

    /// <summary>Falling pieces the server is still waiting on (self-test).</summary>
    public int FallsPending => _fallCount;

    /// <summary>Self-test: the server breaks <paramref name="chunks"/> (any order) as if a hit of <paramref name="hit"/> from <paramref name="from"/> did, nobody's.</summary>
    public void SelfTestBreakChunks(List<int> chunks, StructureHit hit, Vector3 from, double now)
    {
        var sorted = new List<int>(chunks);
        sorted.Sort();
        ServerBreakChunks(sorted, hit, from, null, 0, now);
    }

    /// <summary>Self-test: runs the per-tick destruction work at <paramref name="now"/>.</summary>
    public void SelfTestTickDestruction(double now) => ServerTickDestruction(now);

    // ---- Effects (both sides) --------------------------------------------------------------------------------------

    /// <summary>Debris, dust, falling slabs and sound for what just broke (<c>_dBroken</c>) and fell (<c>_dFallen</c>).</summary>
    private void OnStructureBroken(Map map, StructureHit hit, Vector3 from)
    {
        var d = map.Destruction!;
        var kind = hit switch { StructureHit.Bullet => PropDamage.Bullet, StructureHit.Melee => PropDamage.Melee, _ => PropDamage.Explosion };
        var per = Math.Max(2, MaxDebrisPerEvent / Math.Max(1, _dBroken.Count + _dFallen.Count / 2));
        var centre = Vector3.Zero;
        var loudest = -1;
        foreach (var c in _dBroken)
        {
            var b = d.Original[c];
            var m = d.MaterialOf(c);
            var col = map.Colors[d.Solid[c]];
            var seed = c * 7919 + (int)(PropDebris.Spawned & 0xFFFF);
            PropDebris.SpawnBox(b, from, kind, seed, Math.Min(m.DebrisCount, per), m.DebrisMin, m.DebrisMax, hit == StructureHit.Bullet ? 3f : 5f, col, col * 0.7f);
            PropDebris.SpawnBox(b, from, PropDamage.Melee, seed + 1, 1, 0.12f, 0.28f, 1.2f, m.Dust, m.Dust * 0.9f); // a puff of dust
            centre += b.Center;
            if (loudest < 0 || m.Hp > d.MaterialOf(loudest).Hp)
                loudest = c;
        }
        if (_dBroken.Count > 0)
        {
            centre /= _dBroken.Count;
            var m = d.MaterialOf(loudest);
            Audio.Effect3D(m.Sound, centre, Math.Clamp(0.55f + _dBroken.Count * 0.05f, 0.55f, 1f));
            if (_dBroken.Count >= 3)
                Audio.Effect3D(Sfx.BreakSoft, centre, 0.7f);
        }
        if (_dFallen.Count > 0)
        {
            var fc = Vector3.Zero;
            var slabs = 0;
            foreach (var c in _dFallen)
            {
                var b = d.Original[c];
                fc += b.Center;
                if ((d.Flags[c] & MapDestruction.FlagSkin) != 0)
                    continue;
                var col = map.Colors[d.Solid[c]];
                var seed = c * 104729 + (int)(PropDebris.Spawned & 0xFFFF);
                if (slabs < MaxSlabsPerEvent)
                {
                    PropDebris.SpawnSlab(b, col, seed);
                    slabs++;
                }
                else
                    PropDebris.SpawnBox(b, b.Center + new Vector3(0, 1, 0), PropDamage.Melee, seed, 2, 0.1f, 0.35f, 1.5f, col, col * 0.7f);
            }
            fc /= _dFallen.Count;
            Audio.Effect3D(Sfx.CrateLand, fc, Math.Clamp(0.6f + _dFallen.Count * 0.02f, 0.6f, 1f));
            Audio.Effect3D(d.MaterialOf(_dFallen[0]).Sound, fc, 0.9f);
            if (_dFallen.Count >= 12)
                Audio.Effect3D(Sfx.Explosion, fc, 0.45f); // a big section coming down rumbles
        }
        for (var k = 0; k < _dWindows.Count && k < 6; k++)
            OnWindowBroken(_dWindows[k], from, kind);
        for (var k = 0; k < _dDoors.Count && k < 6; k++)
            OnDoorBroken(_dDoors[k], from, kind);
        foreach (var p in _dProps)
            OnPropBroken(p, from, kind);
    }

    // ---- Packets ---------------------------------------------------------------------------------------------------

    /// <summary>Bytes of a <see cref="Msg.StructBreak"/> header after its id: structure, hit kind, from, count.</summary>
    public const int StructBreakHeader = 2 + 1 + 12 + 2;

    /// <summary>A break event: structure, what hit it, where from, and the chunks the hit broke (ids within the structure).</summary>
    public static void WriteStructureBreak(PacketWriter w, int s, StructureHit hit, Vector3 from, int chunkStart, List<int> chunks, int at, int n)
    {
        w.Begin(Msg.StructBreak).U16((ushort)s).U8((byte)hit).Vec(from).U16((ushort)n);
        for (var k = 0; k < n; k++)
            w.U16((ushort)(chunks[at + k] - chunkStart));
    }

    /// <summary>
    /// Every structure with broken chunks, a packet each: structure, chunk count, then a bit per chunk (set: broken or
    /// fallen). Full state: a joining client needs nothing else. Returns the packets written.
    /// </summary>
    public static int WriteStructureState(PacketWriter w, Map map, Action<PacketWriter> send)
    {
        if (map.Destruction is not { } d)
            return 0;
        var packets = 0;
        foreach (var st in d.Structures)
        {
            if (st.BrokenCount == 0)
                continue;
            w.Begin(Msg.StructState).U16((ushort)st.Index).U16((ushort)st.ChunkCount);
            for (var k = 0; k < st.ChunkCount; k += 8)
            {
                byte bits = 0;
                for (var j = 0; j < 8 && k + j < st.ChunkCount; j++)
                    if (d.Broken[st.ChunkStart + k + j])
                        bits |= (byte)(1 << j);
                w.U8(bits);
            }
            send(w);
            packets++;
        }
        return packets;
    }

    /// <summary>Client: applies one <see cref="Msg.StructState"/> packet (after the id). No debris: those broke before you came.</summary>
    public static void ReadStructureState(Map map, ref PacketReader r)
    {
        if (map.Destruction is not { } d || !r.Has(4))
            return;
        int s = r.U16();
        int n = r.U16();
        if ((uint)s >= (uint)d.Structures.Count || n != d.Structures[s].ChunkCount)
            return; // not this map's structure (a stale packet or junk)
        var st = d.Structures[s];
        for (var k = 0; k < n && r.Has(1); k += 8)
        {
            var bits = r.U8();
            for (var j = 0; j < 8 && k + j < n; j++)
                if ((bits & (1 << j)) != 0)
                    map.BreakChunk(st.ChunkStart + k + j);
        }
    }

    /// <summary>Server: sends a joining player the broken chunks of this match.</summary>
    private void SendStructureState(RakNetGUID to)
    {
        if (World.Map.Destruction == null)
            return;
        WriteStructureState(_w, World.Map, w => Net.SendTo(to, w.Span, PacketReliability.ReliableOrdered));
    }

    private void OnStructurePacket(byte id, ref PacketReader r)
    {
        switch (id)
        {
            case (byte)Msg.StructBreak when !IsServer && r.Has(StructBreakHeader):
                ReadStructureBreak(World.Map, ref r);
                break;
            case (byte)Msg.StructState when !IsServer:
                ReadStructureState(World.Map, ref r);
                break;
        }
    }

    /// <summary>Client: a <see cref="Msg.StructBreak"/> (after the id): breaks the chunks, runs the same collapse as the server, plays the effects.</summary>
    internal void ReadStructureBreak(Map map, ref PacketReader r)
    {
        if (map.Destruction is not { } d || !r.Has(StructBreakHeader))
            return;
        int s = r.U16();
        var hit = (StructureHit)r.U8();
        var from = r.Vec();
        int n = r.U16();
        if ((uint)s >= (uint)d.Structures.Count || hit >= StructureHit.Count)
            return;
        var st = d.Structures[s];
        _dHits.Clear();
        for (var k = 0; k < n && r.Has(2); k++)
        {
            int local = r.U16();
            if (local < st.ChunkCount)
                _dHits.Add(st.ChunkStart + local);
        }
        if (_dHits.Count > 0)
            ApplyStructureBreaks(map, s, _dHits, 0, _dHits.Count, hit, from, false);
    }
}
