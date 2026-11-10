using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>What a death means in a survival world (docs/sandbox/DEATH.md). The byte is in saves and on the wire: add at the end.</summary>
public enum SurvivalDeath : byte
{
    Respawn, // back at the world's spawn points, as before these options
    Checkpoint, // back at the last checkpoint: the last bunk you lay in, else the penal colony's cell block
    Permadeath, // one life: dead is dead in this world
}

/// <summary>Where a respawn puts someone, as their client is told (<see cref="Msg.Fate"/>).</summary>
public enum RespawnSpot : byte
{
    Spawn, // the world's spawn points
    Colony, // the penal colony's cell block (GameSandboxOpening.cs)
    Bunk, // their bunk
}

/// <summary>
/// Survival world options for death (docs/sandbox/DEATH.md), chosen when the world is made and kept with it: respawn, respawn
/// at the last checkpoint, or permadeath, and whether a death drops the backpack. A checkpoint is the last Bunk the player
/// lay in (on any construct: a ship's bunk travels with it), else, once the penal colony stands, its cell block (where the
/// story first wakes them). The server decides; each player's client hears where they will wake and whether they are dead
/// for good (<see cref="Msg.Fate"/>). Creative worlds keep the plain respawn whatever the option says.
/// </summary>
public sealed partial class Game
{
    /// <summary>The world's death rule (server: the settings or the world's own; client: as last heard).</summary>
    public SurvivalDeath SandboxDeathRule { get; private set; }

    /// <summary>A death keeps the backpack's contents instead of dropping them as pickups.</summary>
    public bool SandboxKeepPack { get; private set; }

    /// <summary>The rule in force: survival only.</summary>
    public bool SandboxPermadeath => SandboxDeathRule == SurvivalDeath.Permadeath && !SandboxInfiniteParts;

    public bool SandboxCheckpoints => SandboxDeathRule == SurvivalDeath.Checkpoint && !SandboxInfiniteParts;

    /// <summary>Client: the local player is dead for good (permadeath).</summary>
    public bool SandboxFallenLocal { get; private set; }

    /// <summary>Client: where the local player wakes after a death.</summary>
    public RespawnSpot SandboxRespawnSpot { get; private set; }

    /// <summary>The rule and the backpack flag as one byte (saves, the Welcome, the lobby).</summary>
    public static byte DeathByte(SurvivalDeath rule, bool keepPack) => (byte)((byte)rule | (keepPack ? 0x80 : 0));

    public static (SurvivalDeath Rule, bool KeepPack) FromDeathByte(int b) => ((SurvivalDeath)Math.Min(b & 0x7F, (int)SurvivalDeath.Permadeath), (b & 0x80) != 0);

    public byte SandboxDeathByte => DeathByte(SandboxDeathRule, SandboxKeepPack);

    public static string DeathLabel(SurvivalDeath rule) => rule switch
    {
        SurvivalDeath.Checkpoint => "Last checkpoint",
        SurvivalDeath.Permadeath => "Permadeath",
        _ => "Respawn",
    };

    public static string DeathHint(SurvivalDeath rule) => rule switch
    {
        SurvivalDeath.Checkpoint => "After a death you wake in the last Bunk you lay in, or in the penal colony's cell block.",
        SurvivalDeath.Permadeath => "One life. Dead is dead in this world: you watch your friends from then on.",
        _ => "After a death you come back at the world's spawn points.",
    };

    /// <summary>Server: the option as the game starts (the session's settings). Clients hear it instead.</summary>
    public void SetSandboxDeath(SurvivalDeath rule, bool keepPack)
    {
        if (!IsServer)
            return;
        SandboxDeathRule = rule;
        SandboxKeepPack = keepPack;
    }

    /// <summary>Host, in the lobby: the death rules for the next match. A saved world's are its own.</summary>
    public void LobbySetSandboxDeath(SurvivalDeath rule, bool keepPack)
    {
        if (!IsServer || !InLobby || _lobbyCountdownUntil > 0 || WorldSession != null || rule == SandboxDeathRule && keepPack == SandboxKeepPack)
            return;
        SandboxDeathRule = rule;
        SandboxKeepPack = keepPack;
        MarkLobbyDirty();
    }

    /// <summary>Client: what the host's Welcome or lobby state says.</summary>
    private void HearSandboxDeath(int b) => (SandboxDeathRule, SandboxKeepPack) = FromDeathByte(b);

    // ---- Server: each player's checkpoint and fate ------------------------------------------------------------------

    /// <summary>A bunk someone lay in: its construct, segment and place in the rest frame (parts are renumbered, places stay).</summary>
    private struct SbBunk
    {
        public ushort Body;
        public byte Seg;
        public Vector3 Pos;
    }

    private readonly SbBunk[] _sbBunk = new SbBunk[256];
    private readonly bool[] _sbFallen = new bool[256]; // dead for good
    private readonly bool[] _sbDied = new bool[256]; // died since their last spawn: the next spawn is a respawn
    private readonly byte[] _sbFateSent = new byte[256]; // what their client last heard (0: nothing yet)

    /// <summary>Server: is this player dead for good?</summary>
    public bool SandboxFallen(byte id) => _sbFallen[id];

    /// <summary>Both sides, a new match: nobody has a checkpoint or a fate until a world's records bring theirs.</summary>
    private void SandboxDeathResetAll()
    {
        Array.Clear(_sbBunk);
        Array.Clear(_sbFallen);
        Array.Clear(_sbDied);
        Array.Clear(_sbFateSent);
        SandboxFallenLocal = false;
        SandboxRespawnSpot = RespawnSpot.Spawn;
    }

    /// <summary>Server: someone left (their record has what they had); the id is free for the next.</summary>
    internal void SandboxDeathLeave(byte id)
    {
        _sbBunk[id] = default;
        _sbFallen[id] = _sbDied[id] = false;
        _sbFateSent[id] = 0;
    }

    /// <summary>Server, the tick a person is seen dead: the backpack, permadeath, and where they will wake.</summary>
    private void SandboxDied(Player p)
    {
        var fallen = p.Bot == null && SandboxPermadeath && !OpeningHas(p.Id); // (a death in the opening is the story's, not theirs)
        if (fallen || !SandboxKeepPack || SandboxInfiniteParts || p.Bot != null)
            SandboxHands.DropPack(Constructs, p.Id, p.Position, p.Yaw); // (a death drops the backpack's contents as pickups; dead for good, always)
        if (p.Bot != null)
            return;
        _sbDied[p.Id] = true;
        if (fallen && !_sbFallen[p.Id])
        {
            _sbFallen[p.Id] = true;
            Log($"[death] {p.Name} is dead for good (permadeath)");
            SandboxHands.Notices.Add((p.Id, "Permadeath: you are dead for good in this world"));
        }
        SandboxFateSend(p);
    }

    private readonly float[] _sbHeal = new float[256]; // health a medical seat has given and not yet paid out (whole points go to the sitter)

    /// <summary>Server, the seat loop: <paramref name="p"/> sits in a working medical seat that heals <paramref name="rate"/> health a second (INTERIORS.md, the medical bay).</summary>
    private void SandboxSeatHeal(Player p, float rate)
    {
        ref var acc = ref _sbHeal[p.Id];
        if (p.Health >= World.MaxHealth)
        {
            acc = 0;
            return;
        }
        acc += rate * World.TickDt;
        if (acc < 1f)
            return;
        var whole = (int)acc;
        acc -= whole;
        p.Health = Math.Min(World.MaxHealth, p.Health + whole);
    }

    /// <summary>Server, the seat loop: <paramref name="p"/> lies in bunk <paramref name="seat"/> of <paramref name="c"/>. Their checkpoint from now on.</summary>
    private void SandboxBunkSeen(Player p, Construct c, int seat)
    {
        if (p.Bot != null)
            return;
        ref var b = ref _sbBunk[p.Id];
        ref var part = ref c.Parts[seat];
        if (b.Body == c.Id && b.Seg == part.Seg && Vector3.DistanceSquared(b.Pos, part.Pos) < 0.0025f)
            return;
        b = new SbBunk { Body = c.Id, Seg = part.Seg, Pos = part.Pos };
        if (SandboxCheckpoints)
            SandboxHands.Notices.Add((p.Id, "Checkpoint: you will wake in this bunk"));
        SandboxFateSend(p);
    }

    /// <summary>The bunk a player's checkpoint names, as it stands now (whole, on its construct), or -1.</summary>
    private int SandboxBunkOf(byte id, out Construct? c)
    {
        ref var b = ref _sbBunk[id];
        c = b.Body != 0 ? Constructs.Get(b.Body) : null;
        if (c == null || c.Wreck)
            return -1;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var part = ref c.Parts[i];
            if (part.Seg == b.Seg && !part.Broken && part.D.Bunk && Vector3.DistanceSquared(part.Pos, b.Pos) < 0.0025f)
                return i;
        }
        return -1;
    }

    private bool SandboxColonyStands => _opColony != 0 && Constructs.Get(_opColony) != null;

    /// <summary>Server: where a respawn would put this player now.</summary>
    private RespawnSpot SandboxSpotOf(byte id) =>
        !SandboxCheckpoints ? RespawnSpot.Spawn
        : SandboxBunkOf(id, out _) >= 0 ? RespawnSpot.Bunk
        : SandboxColonyStands ? RespawnSpot.Colony
        : RespawnSpot.Spawn;

    /// <summary>Server: tell a person's client where they will wake and whether they are dead for good, when that changed.</summary>
    private void SandboxFateSend(Player p)
    {
        if (p.Bot != null)
            return;
        var spot = SandboxSpotOf(p.Id);
        var fate = (byte)(0x80 | (_sbFallen[p.Id] ? 0x40 : 0) | (byte)spot);
        if (fate == _sbFateSent[p.Id])
            return;
        _sbFateSent[p.Id] = fate;
        if (p.Id == LocalId)
        {
            HearFate(fate);
            return;
        }
        if (GuidOf(p) is not { } guid)
            return;
        _w.Begin(Msg.Fate).U8(fate);
        Net.SendTo(guid, _w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Client: a <see cref="Msg.Fate"/> (after the id).</summary>
    private void ReadFate(ref PacketReader r) => HearFate(r.U8());

    private void HearFate(byte fate)
    {
        SandboxFallenLocal = (fate & 0x40) != 0;
        SandboxRespawnSpot = (RespawnSpot)Math.Min(fate & 0x3F, (int)RespawnSpot.Bunk);
    }

    /// <summary>The line beside the Respawn button: where they will wake (null: the mode's own hint).</summary>
    public string? SandboxRespawnLine =>
        !SandboxOn || SandboxInfiniteParts ? null
        : SandboxFallenLocal ? "Permadeath: you are dead for good in this world."
        : SandboxRespawnSpot switch
        {
            RespawnSpot.Bunk => "You will wake in your bunk.",
            RespawnSpot.Colony => "You will wake in the penal colony's cell block. Lie in a Bunk to make it your checkpoint.",
            _ => SandboxDeathRule == SurvivalDeath.Checkpoint ? "You will wake at the world's spawn points. Lie in a Bunk to make it your checkpoint." : null,
        };

    /// <summary>Server, as a person spawns after a death (SandboxOnSpawn, after a saved world's own place): at their checkpoint.</summary>
    private void SandboxRespawnAt(Player p)
    {
        if (p.Bot != null || !_sbDied[p.Id])
            return;
        _sbDied[p.Id] = false;
        if (!SandboxCheckpoints)
            return;
        var seat = SandboxBunkOf(p.Id, out var c);
        if (seat >= 0 && c != null)
        {
            SandboxWakeInBunk(p, c, seat);
            SandboxFateSend(p);
            return;
        }
        if (_sbBunk[p.Id].Body != 0)
        {
            _sbBunk[p.Id] = default;
            SandboxHands.Notices.Add((p.Id, "Your bunk is gone: lie in another to make it your checkpoint"));
        }
        if (SandboxColonyStands)
            OpeningToColony(p); // (the cell block's floor, where the story first woke them: GameSandboxOpening.cs)
        SandboxFateSend(p);
    }

    /// <summary>Server: into the bunk, lying there as if they had slept; up with Use as from any seat. Taken: on top of it.</summary>
    private void SandboxWakeInBunk(Player p, Construct c, int seat)
    {
        var feet = ConstructEdits.SeatFeet(c, seat);
        p.Teleport(feet);
        p.Sim.Velocity = c.PointVel(feet); // (a ship's bunk: moving with it)
        if (SandboxHands.Use(Constructs, p.Id, p.Eye, c.Id, seat) is null)
            return;
        c.WorldFrame(c.Parts[seat].Seg, out var q, out _);
        p.Teleport(feet + Vector3.Transform(Vector3.UnitY, q) * 1.0f); // (someone is in it: on top)
        p.Sim.Velocity = c.PointVel(feet);
    }

    // ---- Saved worlds -----------------------------------------------------------------------------------------------

    /// <summary>Server: what goes in a person's record when the world is saved or they leave.</summary>
    private void DeathCatch(Player p, WorldPlayer rec)
    {
        ref var b = ref _sbBunk[p.Id];
        (rec.BunkBody, rec.BunkSeg, rec.BunkPos) = (b.Body, b.Seg, b.Pos);
        rec.Fallen = _sbFallen[p.Id];
        rec.Died = _sbDied[p.Id] || !p.Alive && !rec.Fallen;
    }

    /// <summary>Server: a person is back in the world: their checkpoint and fate with them.</summary>
    private void DeathArrive(Player p, WorldPlayer rec)
    {
        _sbBunk[p.Id] = new SbBunk { Body = rec.BunkBody, Seg = rec.BunkSeg, Pos = rec.BunkPos };
        _sbFallen[p.Id] = rec.Fallen && SandboxPermadeath;
        _sbDied[p.Id] = rec.Died && !rec.HasPlace;
        _sbFateSent[p.Id] = 0;
        SandboxFateSend(p);
        if (_sbFallen[p.Id])
            SandboxHands.Notices.Add((p.Id, "Permadeath: you died in this world, and dead is dead"));
    }

    /// <summary>A save's death section (after Players): each person's bunk and fate.</summary>
    private static void WorldReadDeath(SaveReader r, WorldSession ws)
    {
        var n = r.Count(32);
        for (var k = 0; k < n; k++)
        {
            var key = r.U64();
            var steam = r.U64();
            var flags = r.U8();
            var body = r.U16();
            var seg = r.U8();
            var pos = r.Vec();
            if (!float.IsFinite(pos.X + pos.Y + pos.Z))
                continue;
            foreach (var rec in ws.Players)
                if (rec.Key == key && rec.SteamId == steam)
                {
                    rec.Fallen = (flags & 1) != 0;
                    rec.Died = (flags & 2) != 0;
                    (rec.BunkBody, rec.BunkSeg, rec.BunkPos) = (body, seg, pos);
                    break;
                }
        }
    }
}
