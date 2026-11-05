using System.Diagnostics;
using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// Saved worlds in the game (docs/sandbox/WORLD-SAVES.md). The host's Sandbox game may play a <see cref="WorldSession"/>:
/// a saved world, or a new one. Other modes, and a Sandbox game with no world, never touch any of this.
/// <list type="bullet">
/// <item>A save: on the tick a plain copy of the state (<see cref="WorldSnapshot"/>), then bytes, Brotli and an atomic file
/// write on a worker. The world saves on a timer, on Save now, on Back to lobby, on a mode or map change and on quitting.</item>
/// <item>A load: on the first tick after a match has started (the map restored, everyone reset), so it stands on the map
/// before anyone plays; clients get it like joiners do. A new world builds the map's set as ever.</item>
/// <item>People: found by their saved key (<see cref="Settings.WorldKey"/>, sent in the Hello) or Steam id. Someone who
/// leaves keeps their things in their record; what they built stays theirs (an owner byte from
/// <see cref="WorldSession.OfflineBase"/> up until they return).</item>
/// </list>
/// </summary>
public sealed partial class Game
{
    /// <summary>The world this host plays (null: not a saved world; a client never has one).</summary>
    public WorldSession? WorldSession { get; private set; }

    /// <summary>The world's name, on every peer ("": none; the lobby state carries it).</summary>
    public string WorldShownName { get; private set; } = "";

    /// <summary>Who this PC is to a world (Settings.WorldKey).</summary>
    public ulong LocalWorldKey;

    /// <summary>Minutes between autosaves (0: only on Save now, Back to lobby and quitting).</summary>
    public int WorldAutosaveMinutes = 5;

    private readonly ulong[] _worldKeys = new ulong[256];
    private (ulong Ns, int Warp, string System, ShipSnap[] Ships)? _worldSpace;

    private ulong WorldKeyOf(Player p) => p.Id == LocalId ? LocalWorldKey : _worldKeys[p.Id];

    /// <summary>Do we know who this is (a person whose Hello has come)? Until then they have no record to find or make.</summary>
    private bool WorldKnown(Player p) => p.Bot == null && (p.Id == LocalId || _worldKeys[p.Id] != 0 || p.SteamId != 0);

    // ---- Choosing a world -------------------------------------------------------------------------------------------

    /// <summary>A world to play from the list: checked here, so a bad file is said in the lobby, not at the start of play.</summary>
    public static WorldSession? WorldOpen(WorldEntry e, out string why)
    {
        why = "";
        if (e.Problem.Length > 0)
        {
            why = $"{e.Meta.Name}: {e.Problem}";
            return null;
        }
        try
        {
            WorldStore.Read(e.Folder, out var meta);
            if (Maps.All.All(m => m.Name != meta.MapName))
            {
                why = $"{meta.Name} is on {meta.MapName}, which this build doesn't have.";
                return null;
            }
            return new WorldSession { Folder = e.Folder, Meta = meta, HasSave = true, PlayedBefore = meta.PlayedSeconds };
        }
        catch (WorldFileException ex)
        {
            why = $"{e.Meta.Name}: {ex.Message}";
            return null;
        }
    }

    /// <summary>A new world on a map; its file is made as play starts.</summary>
    public static WorldSession WorldNew(string name, Map map, bool survival)
    {
        var clean = WorldStore.CleanName(name);
        return new WorldSession
        {
            Folder = WorldStore.FolderFor(clean),
            Meta = new WorldMeta { Name = clean, MapName = map.Name, Survival = survival, CreatedUtc = DateTime.UtcNow.Ticks },
        };
    }

    /// <summary>Starting a session: this game plays <paramref name="world"/> (the map and mode are already its own).</summary>
    public void WorldAttach(WorldSession? world)
    {
        if (!IsServer)
            return;
        WorldSession = world;
        WorldShownName = world?.Meta.Name ?? "";
        if (world == null)
            return;
        SandboxSurvivalOption = world.Meta.Survival;
        SandboxShareRules();
        world.Applied = !world.HasSave;
        world.SaveAtStart = !world.HasSave;
        if (world.HasSave)
            Constructs.Clear(); // (the map's set, built as the session opened: this world has its own)
        Constructs.Removed.Clear();
    }

    /// <summary>Host, in the lobby: this world from now on (null: none). Its map and mode come with it and stay locked. False, and a reason in <paramref name="why"/>, when it can't.</summary>
    public bool LobbySetWorld(WorldSession? world, out string why)
    {
        why = "";
        if (!IsServer || !InLobby || _lobbyCountdownUntil > 0)
            return false;
        WorldDetach();
        if (world != null)
        {
            if (Maps.All.FirstOrDefault(m => m.Name == world.Meta.MapName) is not { } map || !LobbySetMap(map, false))
            {
                why = $"{world.Meta.MapName} can't be played by everyone here.";
                return false;
            }
            if (Mode != GameMode.Sandbox && !LobbySetMode(GameMode.Sandbox))
            {
                why = "This map doesn't play Sandbox.";
                return false;
            }
            WorldAttach(world);
            world.Applied = false;
        }
        MarkLobbyDirty();
        return true;
    }

    /// <summary>The world goes (its last save on its way first).</summary>
    private void WorldDetach()
    {
        if (WorldSession is not { } ws)
            return;
        WorldFinishWrite(ws);
        WorldSession = null;
        WorldShownName = "";
        MarkLobbyDirty();
    }

    // ---- Saving -----------------------------------------------------------------------------------------------------

    /// <summary>Saves now: the tick's copy, the rest on the worker. <paramref name="wait"/>: back only when the file is written. False when there's nothing to save.</summary>
    public bool WorldSaveNow(bool wait = false)
    {
        if (!IsServer || WorldSession is not { Applied: true } ws || !SandboxOn)
            return false;
        if (ws.Writing is { IsCompleted: false } && !wait)
        {
            ws.SaveQueued = true;
            return true;
        }
        WorldFinishWrite(ws);
        WorldCapture(ws);
        if (wait)
            WorldFinishWrite(ws);
        return true;
    }

    /// <summary>The save in the host's menu.</summary>
    public void WorldSaveButton()
    {
        if (WorldSaveNow())
            SandboxSay("World saved");
    }

    private void WorldCapture(WorldSession ws)
    {
        try
        {
            WorldCopy(ws);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log($"World {ws.Meta.Name} wasn't saved: {ex.Message}", true); // (a save never takes the game down)
            Console.WriteLine($"[worlds] {ex}");
        }
    }

    /// <summary>
    /// The tick's copy of the world, written on the worker. With <paramref name="stashFolder"/>, a copy of the planetoid
    /// being left (only the bodies <paramref name="keep"/> takes, into its own snapshot) written now, before this returns
    /// (GameSpaceLanding.cs); the reason it failed, or null.
    /// </summary>
    private string? WorldCopy(WorldSession ws, string? stashFolder = null, Func<Construct, bool>? keep = null)
    {
        var clock = Stopwatch.StartNew();
        var s = stashFolder != null ? new WorldSnapshot() : ws.Snapshot;
        Constructs.CaptureInto(s, keep);
        foreach (var p in Players.Values)
            if (WorldKnown(p))
                WorldCatch(p, WorldRecordOf(ws, p));
        s.Players = new WorldPlayer[ws.Players.Count];
        Array.Clear(s.OwnerRecord);
        for (var k = 0; k < ws.Players.Count; k++)
        {
            var rec = ws.Players[k];
            s.Players[k] = rec.Copy();
            if (rec.Online != 255)
                s.OwnerRecord[rec.Online] = (ushort)(k + 1);
            if (rec.Offline != 255)
                s.OwnerRecord[rec.Offline] = (ushort)(k + 1);
        }
        s.OpCount = 0;
        s.GroundOnPlanet = World.Map.Planetoid != null;
        if (DigGroundIfMade is { } g)
        {
            WorldSnapshot.Grow(ref s.Ops, g.Ops.Count);
            g.Ops.CopyTo(s.Ops);
            s.OpCount = g.Ops.Count;
        }
        s.Resources = World.Map.ResourcesIfMade?.Capture();
        s.PlanetCentre = World.Map.Planetoid?.Center;
        StoryCapture(s); // (GameStory.cs)
        s.SpaceSystem = "";
        s.ShipCount = 0;
        if (Space?.Flight is { } f)
        {
            s.SpaceSystem = f.System.Name;
            s.SpaceNs = f.Clock.Now.Ns;
            s.SpaceWarp = f.RequestedWarp;
            WorldSnapshot.Grow(ref s.Ships, f.Ships.Count);
            for (var k = 0; k < f.Ships.Count; k++)
            {
                f.Ships.RelativeOf(k, out var primary, out var r, out var v);
                s.Ships[s.ShipCount++] = new ShipSnap { Id = f.Ships.Ids[k], Primary = primary, R = r, V = v };
            }
        }
        s.AwayCount = 0;
        if (Space is { Flight: not null } sp)
            for (var gi = 0; gi < sp.Away.Length; gi++)
            {
                if (gi == sp.GalaxyIndex || sp.Away[gi] is not { } af || af.Ships.Count == 0)
                    continue; // (ships in other systems, phase 6 jumps: saved with their system's name)
                WorldSnapshot.Grow(ref s.AwayShips, s.AwayCount + af.Ships.Count);
                for (var k = 0; k < af.Ships.Count; k++)
                {
                    af.Ships.RelativeOf(k, out var primary, out var r, out var v);
                    s.AwayShips[s.AwayCount++] = new ShipSnap { Id = af.Ships.Ids[k], Primary = primary, R = r, V = v, System = af.System.Name };
                }
            }
        var m = stashFolder != null ? WorldMetaCopy(ws.Meta) : ws.Meta;
        m.MapName = World.Map.Name;
        m.SavedUtc = DateTime.UtcNow.Ticks;
        m.Build = BuildInfo.Label;
        m.MapHash = World.Map.ContentHash;
        m.Bodies = s.BodyCount;
        m.Parts = s.PartCount;
        m.Players = ws.Players.Count;
        m.PlayedSeconds = ws.PlayedBefore + (ws.PlayStarted >= 0 ? SandboxNow - ws.PlayStarted : 0);
        s.Meta = m;
        s.CaptureMs = clock.Elapsed.TotalMilliseconds;
        if (stashFolder != null)
        {
            Directory.CreateDirectory(Path.Combine(WorldStore.Root, stashFolder));
            return WorldSaveWriter.Run(stashFolder, s);
        }
        var folder = ws.Folder;
        ws.Writing = Task.Run(() => WorldSaveWriter.Run(folder, s));
        return null;
    }

    private static WorldMeta WorldMetaCopy(WorldMeta m) => new()
    {
        Name = m.Name, MapName = m.MapName, Build = m.Build, Survival = m.Survival, MapHash = m.MapHash, CreatedUtc = m.CreatedUtc,
        SavedUtc = m.SavedUtc, PlayedSeconds = m.PlayedSeconds, Bodies = m.Bodies, Parts = m.Parts, Players = m.Players,
    };

    /// <summary>What a person has now, into their record.</summary>
    private void WorldCatch(Player p, WorldPlayer rec)
    {
        rec.Name = p.Name;
        rec.SteamId = p.SteamId != 0 ? p.SteamId : rec.SteamId;
        rec.HasPlace = p.Alive && !InLobby;
        rec.Pos = p.Position;
        var riding = p.Alive && p.Sim.RideUp != default;
        (rec.RideBody, rec.RideSeg, rec.RideLocal, rec.RideUp) = (riding ? p.Sim.RideBody : (ushort)0, p.Sim.RideSeg, p.Sim.RideLocal, p.Sim.RideUp);
        ref var hand = ref SandboxHands.Hand(p.Id);
        var spaceSeat = p.Alive && hand.Seated && Constructs.Get(hand.SeatBody) is { SpaceShip: not 0 };
        (rec.SpaceSeatBody, rec.SpaceSeatPart) = spaceSeat ? (hand.SeatBody, hand.SeatPart) : ((ushort)0, (ushort)0);
        rec.Yaw = p.Yaw;
        rec.Pitch = p.Pitch;
        rec.Health = p.Health;
        ref var h = ref SandboxHands.Hand(p.Id);
        rec.Tool = h.Tool;
        rec.Held = h.Held;
        var inv = SandboxInv.Of(p.Id);
        var parts = new List<(ushort, ushort)>();
        for (var i = 0; i < ConstructParts.All.Count && i < inv.Parts.Length; i++)
            if (inv.Parts[i] > 0)
                parts.Add(((ushort)i, inv.Parts[i]));
        rec.Parts = parts.ToArray();
        var mats = new List<(byte, ushort)>();
        for (var i = 0; i < inv.Materials.Length; i++)
            if (inv.Materials[i] > 0)
                mats.Add(((byte)i, inv.Materials[i]));
        rec.Materials = mats.ToArray();
        WorldStores.Copy(SandboxHands.Pack(p.Id), rec.Pack);
        rec.Builds = ZPlayers.TryGetValue(p.Id, out var zp) ? zp.Slots.Select(b => b?.ToText() ?? "").ToArray() : [];
    }

    /// <summary>A person's record: theirs if they've been here, else a new one.</summary>
    private WorldPlayer WorldRecordOf(WorldSession ws, Player p)
    {
        foreach (var r in ws.Players)
            if (r.Online == p.Id)
                return r;
        var rec = ws.Find(WorldKeyOf(p), p.SteamId) ?? ws.Add(WorldKeyOf(p), p.SteamId, p.Name);
        rec.Online = p.Id;
        return rec;
    }

    private void WorldFinishWrite(WorldSession ws)
    {
        if (ws.Writing == null)
            return;
        if (ws.Finish() is { } why)
        {
            Log($"World {ws.Meta.Name} wasn't saved: {why}", true);
            SandboxSay("World NOT saved");
        }
        else
            ws.HasSave = true;
    }

    /// <summary>Before anything clears the world (a new match, a mode or map change): what was played goes to disk.</summary>
    private void WorldBeforeReset()
    {
        if (!IsServer || WorldSession is not { Live: true } ws)
            return;
        WorldSaveNow();
        ws.PlayedBefore = ws.Meta.PlayedSeconds;
        ws.PlayStarted = -1;
        ws.Live = false;
    }

    /// <summary>The host leaves: the world's last save, finished before the game goes on closing.</summary>
    private void WorldOnExit()
    {
        if (!IsServer || WorldSession is not { } ws)
            return;
        if (ws.Live)
            WorldSaveNow(wait: true);
        else
            WorldFinishWrite(ws);
    }

    // ---- The tick ---------------------------------------------------------------------------------------------------

    /// <summary>Host, every tick of a session with a world: a load that is due. A save's end is noticed here too.</summary>
    private void WorldTick()
    {
        if (WorldSession is not { } ws)
            return;
        if (!ws.Applied && SandboxOn && !InLobby)
            try
            {
                WorldApply(ws);
            }
            catch (Exception ex) when (ex is WorldFileException or IndexOutOfRangeException or ArgumentException or InvalidOperationException)
            {
                Log($"World {ws.Meta.Name} didn't load: {ex.Message}", true); // (never saved over: the game plays on without it, the files stay)
                Console.WriteLine($"[worlds] {ex}");
                WorldDetach();
            }
        if (ws.Writing is { IsCompleted: true })
            WorldFinishWrite(ws);
    }

    /// <summary>Host, the end of a playing tick: the autosave.</summary>
    internal void WorldTickPlaying(double now)
    {
        if (WorldSession is not { Applied: true } ws)
            return;
        if (!ws.Live)
        {
            ws.Live = true;
            ws.PlayStarted = now;
            ws.NextAutosave = now + WorldAutosaveMinutes * 60.0;
            foreach (var rec in ws.Players)
                rec.Spawned = rec.Online != 255 && Players.TryGetValue(rec.Online, out var p) && p.Alive; // (a warmup's spawns end here; who isn't in yet still comes back to their place)
        }
        if (ws.SaveAtStart)
        {
            ws.SaveAtStart = false;
            WorldSaveNow();
        }
        if (ws.SaveQueued && ws.Writing is null or { IsCompleted: true })
        {
            ws.SaveQueued = false;
            WorldSaveNow();
        }
        if (WorldAutosaveMinutes > 0 && now >= ws.NextAutosave)
        {
            ws.NextAutosave = now + WorldAutosaveMinutes * 60.0;
            WorldSaveNow();
        }
    }

    // ---- Loading ----------------------------------------------------------------------------------------------------

    private void WorldApply(WorldSession ws)
    {
        ws.Applied = true;
        _sbKitBuilt = false;
        WorldFinishWrite(ws);
        if (_landing is { } plan)
        {
            _landing = null;
            WorldLandApply(ws, plan); // (the group landed on another planetoid: GameSpaceLanding.cs)
            return;
        }
        if (!ws.HasSave)
        {
            foreach (var p in Players.Values)
                if (WorldKnown(p))
                    WorldArrive(ws, p);
            return;
        }
        var clock = Stopwatch.StartNew();
        byte[] body;
        try
        {
            body = WorldStore.Read(ws.Folder, out var meta);
            ws.Meta = meta;
            ws.PlayedBefore = meta.PlayedSeconds;
        }
        catch (WorldFileException ex)
        {
            Log($"World {ws.Meta.Name} can't be read: {ex.Message}", true); // (never written over: the world goes, the files stay)
            WorldDetach();
            if (World.Map.Sandbox is { } sandbox)
                Constructs.Populate(sandbox, World.Query);
            return;
        }
        var readMs = clock.Elapsed.TotalMilliseconds;
        var names = new WorldNamesRead();
        var kept = 0;
        var lost = 0;
        ws.Players.Clear();
        Constructs.Clear();
        Constructs.PowerRequired = World.Map.Sandbox?.PowerRequired ?? false;
        Constructs.Resources = World.Map.Resources;
        var r = new SaveReader(body) { Shift = WorldShift(body) };
        ushort nextTag = 1;
        while (r.Has(8))
        {
            var tag = r.U32();
            var length = r.I32();
            var end = r.Pos + length;
            if (length < 0 || !r.Has(length))
                throw new WorldFileException("a section that doesn't fit");
            var whole = r.End;
            r.End = end;
            try
            {
                WorldReadSection(tag, r, ws, names, ref kept, ref lost, ref nextTag);
            }
            catch (WorldFileException ex)
            {
                Console.WriteLine($"[worlds] {ws.Meta.Name}: section {tag:x8} left out: {ex.Message}"); // (the rest of the world still loads)
                lost++;
            }
            r.End = whole;
            r.Pos = end;
        }
        foreach (var (name, n) in names.Missing)
            Console.WriteLine($"[worlds] {ws.Meta.Name}: left out {n} x {name}: this build doesn't have it");
        Constructs.AfterLoad(nextTag);
        if (names.Skipped > 0)
            Constructs.RelinkAfterSkips();
        Constructs.StructureDirty.Clear();
        Constructs.Removed.Clear();
        var loaded = clock.Elapsed.TotalMilliseconds;
        foreach (var p in Players.Values)
            if (WorldKnown(p))
                WorldArrive(ws, p);
        foreach (var (guid, _) in _clients.Values)
            if (guid.G != 0)
            {
                SendTerrainDigState(guid);
                SendResourceState(guid);
                SendSandboxState(guid);
            }
        ws.LastBody = $"read {readMs:0} ms, built {loaded - readMs:0} ms";
        Console.WriteLine($"[worlds] {ws.Meta.Name}: loaded {kept} constructs, {Constructs.PartTally} parts, {ws.Players.Count} people, {DigOps} ground ops: {ws.LastBody}{(lost > 0 ? $", {lost} left out" : "")}");
        Log($"World {ws.Meta.Name}", true);
    }

    /// <summary>
    /// How far a world file's points move to stand where they did on this map: a planetoid's centre is the World's
    /// origin now (PLANETS.md "Origin"); a file from before that (no Origin section) had it at (0, -radius, 0).
    /// </summary>
    internal static Vector3 WorldShift(byte[] body)
    {
        if (World.Map.Planetoid is not { } pl)
            return Vector3.Zero;
        var saved = new Vector3(0, -(float)pl.Radius, 0);
        var r = new SaveReader(body);
        while (r.Has(8))
        {
            var tag = r.U32();
            var length = r.I32();
            if (length < 0 || !r.Has(length))
                break;
            var end = r.Pos + length;
            if (tag == WorldSaveWriter.Origin && length >= 12)
            {
                saved = r.Vec();
                break;
            }
            r.Pos = end;
        }
        return pl.Center - saved;
    }

    /// <summary>One tagged section of a world file into the game (WorldApply; a landing reads two files: GameSpaceLanding.cs).</summary>
    private void WorldReadSection(uint tag, SaveReader r, WorldSession ws, WorldNamesRead names, ref int kept, ref int lost, ref ushort nextTag)
    {
        switch (tag)
        {
            case WorldSaveWriter.Names:
                names.Read(r);
                break;
            case WorldSaveWriter.Misc:
                nextTag = r.U16();
                break;
            case WorldSaveWriter.Players:
            {
                var n = r.Count(8);
                for (var k = 0; k < n; k++)
                {
                    var rec = WorldSaveWriter.ReadPlayer(r, names);
                    rec.Offline = k + WorldSession.OfflineBase < 255 ? (byte)(k + WorldSession.OfflineBase) : (byte)255;
                    ws.Players.Add(rec);
                }
                break;
            }
            case WorldSaveWriter.Riders:
            {
                var n = r.Count(35);
                for (var k = 0; k < n; k++)
                {
                    var key = r.U64();
                    var deck = r.U16();
                    var seg = r.U8();
                    var local = r.Vec();
                    var up = r.Vec();
                    foreach (var rec in ws.Players)
                        if (rec.Key == key && float.IsFinite(local.X + local.Y + local.Z + up.X + up.Y + up.Z) && up.LengthSquared() > 0.5f)
                            (rec.RideBody, rec.RideSeg, rec.RideLocal, rec.RideUp) = (deck, seg, local, up);
                }
                break;
            }
            case WorldSaveWriter.SpaceSeats:
            {
                var n = r.Count(12);
                for (var k = 0; k < n; k++)
                {
                    var key = r.U64();
                    var seatBody = r.U16();
                    var seatPart = r.U16();
                    foreach (var rec in ws.Players)
                        if (rec.Key == key)
                            (rec.SpaceSeatBody, rec.SpaceSeatPart) = (seatBody, seatPart);
                }
                break;
            }
            case WorldSaveWriter.Stores:
            {
                var n = r.Count(8);
                for (var k = 0; k < n; k++)
                {
                    var handle = r.U16();
                    if (handle == 0 || handle > ConstructWorld.MaxStores)
                        throw new WorldFileException($"store {handle}");
                    ConstructWorld.ReadStore(r, Constructs.Stores[handle], names);
                    Constructs.Stores[handle].Live = true;
                }
                break;
            }
            case WorldSaveWriter.Bodies:
            {
                var n = r.Count(32);
                Constructs.BodyCap = Math.Max(Constructs.BodyCap, n + 64);
                Constructs.PartCap = Math.Max(Constructs.PartCap, (int)Math.Min(ws.Meta.Parts + 4096L, ConstructWorld.MostParts));
                for (var k = 0; k < n; k++)
                {
                    var len = r.I32();
                    var next = r.Pos + len;
                    try
                    {
                        if (Constructs.ReadBody(r, names, rec => WorldOwnerByte(ws, rec), next) != null)
                            kept++;
                    }
                    catch (Exception ex) when (ex is WorldFileException or IndexOutOfRangeException or ArgumentException or InvalidOperationException)
                    {
                        lost++;
                        Console.WriteLine($"[worlds] {ws.Meta.Name}: construct {k} skipped: {ex.Message}");
                    }
                    r.Pos = next;
                }
                break;
            }
            case WorldSaveWriter.Links:
                Constructs.ReadLinks(r);
                break;
            case WorldSaveWriter.Connectors:
                Constructs.ReadConnectors(r);
                break;
            case WorldSaveWriter.Ground:
                WorldReadGround(r);
                break;
            case WorldSaveWriter.Resources:
                WorldReadResources(r, ws);
                break;
            case WorldSaveWriter.Space:
                WorldReadSpace(r);
                break;
            case WorldSaveWriter.SpaceAway:
                WorldReadSpaceAway(r);
                break;
            case WorldSaveWriter.Story:
                StoryRead(r, ws.Meta.Name); // (GameStory.cs)
                break;
        }
    }

    private static byte WorldOwnerByte(WorldSession ws, ushort record) =>
        record == 0 || record > ws.Players.Count ? (byte)255 : ws.Players[record - 1].Offline;

    private void WorldReadGround(SaveReader r)
    {
        var planet = r.U8() != 0;
        var n = r.Count(17);
        if (DigGround is not { } g || planet != (World.Map.Planetoid != null))
        {
            if (n > 0)
                Console.WriteLine($"[worlds] {n} ground ops left out: this map's ground isn't the kind that was dug");
            return;
        }
        for (var k = 0; k < n; k++)
        {
            var kind = (DigKind)r.U8();
            var at = r.At();
            var radius = r.F32();
            if (kind <= DigKind.FillSand)
                g.Apply(new DigOp(kind, at, radius));
        }
    }

    private void WorldReadResources(SaveReader r, WorldSession ws)
    {
        var s = new ResourceFieldState { Seed = r.U32(), Deposits = r.I32(), Clock = r.I64() };
        s.Worked = new (int, int)[r.Count(8)];
        for (var k = 0; k < s.Worked.Length; k++)
            s.Worked[k] = (r.I32(), r.I32());
        s.Felled = new uint[r.Count(4)];
        for (var k = 0; k < s.Felled.Length; k++)
            s.Felled[k] = r.U32();
        s.Regrow = new (uint, long)[r.Count(12)];
        for (var k = 0; k < s.Regrow.Length; k++)
            s.Regrow[k] = (r.U32(), r.I64());
        s.Cut = new (uint, int)[r.Count(8)];
        for (var k = 0; k < s.Cut.Length; k++)
            s.Cut[k] = (r.U32(), r.I32());
        if (World.Map.Resources?.Restore(s) is { } note)
            Console.WriteLine($"[worlds] {ws.Meta.Name}: {note}");
    }

    private void WorldReadSpace(SaveReader r)
    {
        var system = r.Str();
        var ns = r.I64();
        var warp = r.I32();
        var ships = new ShipSnap[r.Count(50)];
        for (var k = 0; k < ships.Length; k++)
            ships[k] = new ShipSnap { Id = r.U16(), Primary = r.I32(), R = new Vec3d(r.F64(), r.F64(), r.F64()), V = new Vec3d(r.F64(), r.F64(), r.F64()) };
        _worldSpace = ((ulong)ns, warp, system, ships);
        _spaceMap = null; // (ServerTickSpace makes the bubble again, at the saved time)
    }

    /// <summary>Ships in other star systems (phase 6 jumps), each with its system's name.</summary>
    private ShipSnap[]? _worldAway;

    private void WorldReadSpaceAway(SaveReader r)
    {
        var ships = new ShipSnap[r.Count(52)];
        for (var k = 0; k < ships.Length; k++)
            ships[k] = new ShipSnap
            {
                System = r.Str(), Id = r.U16(), Primary = r.I32(), R = new Vec3d(r.F64(), r.F64(), r.F64()), V = new Vec3d(r.F64(), r.F64(), r.F64()),
            };
        _worldAway = ships;
    }

    /// <summary>Space's side of a load, as the bubble is made: the clock it was at. Null: a fresh start.</summary>
    private SpaceFlight? WorldSpaceFlight(StarSystem system)
    {
        if (_worldSpace is not { } w || w.System != system.Name)
            return null;
        var f = new SpaceFlight(system, SpaceShipCapacity, new SpaceTime((long)w.Ns));
        f.RequestWarp(w.Warp, fromHost: true);
        return f;
    }

    /// <summary>
    /// The bubble is made: the saved ships are back in their systems (the bubble's, or another the ship had jumped to), and
    /// their constructs follow them (ships get new ids). A ship in another system is parked again (GameSpaceJump.cs); one
    /// far out in the bubble's own is parked again by the first tick. A ship whose system or body this build doesn't have
    /// is lost, and its construct comes back into the bubble where it was saved.
    /// </summary>
    private void WorldSpaceShips()
    {
        if (_worldSpace is not { } w || Space is not { Flight: { } f } s)
            return;
        _worldSpace = null;
        var away = _worldAway ?? [];
        _worldAway = null;
        foreach (var c in Constructs.Bodies)
        {
            if (c.SpaceShip == 0)
                continue;
            var id = c.SpaceShip;
            c.SpaceShip = 0;
            c.SpaceAway = 0;
            var was = Array.FindIndex(w.Ships, x => x.Id == id);
            if (was >= 0)
            {
                ref var sh = ref w.Ships[was];
                var i = (uint)sh.Primary < (uint)f.System.Count ? f.Ships.AddRelative(sh.Primary, sh.R, sh.V) : -1;
                c.SpaceShip = i < 0 ? (ushort)0 : f.Ships.Ids[i];
                continue;
            }
            was = Array.FindIndex(away, x => x.Id == id);
            var g = was < 0 ? -1 : GalaxyIndexByName(away[was].System);
            if (g < 0 || g == s.GalaxyIndex)
            {
                if (was >= 0)
                    Console.WriteLine($"[worlds] construct {c.Id}'s ship was in {away[was].System}, which this build doesn't have: it is back in the bubble");
                continue;
            }
            var af = s.AwayFlight(g);
            ref var a = ref away[was];
            var j = (uint)a.Primary < (uint)af.System.Count ? af.Ships.AddRelative(a.Primary, a.R, a.V) : -1;
            if (j < 0)
                continue;
            c.SpaceShip = af.Ships.Ids[j];
            SpacePark(c, g);
            Console.WriteLine($"[worlds] construct {c.Id} is a ship in {af.System.Name} again (ship {c.SpaceShip})");
        }
    }

    private static int GalaxyIndexByName(string? name)
    {
        for (var g = 0; g < Galaxy.Count; g++)
            if (Galaxy.System(g).Name == name)
                return g;
        return -1;
    }

    // ---- People -----------------------------------------------------------------------------------------------------

    /// <summary>Server: a person's Hello said who they are.</summary>
    private void WorldHeard(Player p, ulong key)
    {
        _worldKeys[p.Id] = key;
        if (WorldSession is { Applied: true } ws && SandboxOn)
            WorldArrive(ws, p);
    }

    /// <summary>A person is in the world: their record found or made, their things and their constructs back with them.</summary>
    private void WorldArrive(WorldSession ws, Player p)
    {
        var found = ws.Players.Exists(r => r.Online == p.Id);
        var rec = WorldRecordOf(ws, p);
        if (found)
            return;
        rec.Spawned = false;
        foreach (var c in Constructs.Bodies)
        {
            if (rec.Offline != 255 && c.Owner == rec.Offline)
            {
                c.Owner = p.Id;
                Constructs.StructureDirty.Add(c.Id);
            }
            if (rec.Offline != 255 && c.FrozenBy == rec.Offline)
                c.FrozenBy = p.Id;
        }
        var inv = SandboxInv.Of(p.Id);
        inv.Clear();
        foreach (var (def, count) in rec.Parts)
            inv.Parts[def] = count;
        foreach (var (material, count) in rec.Materials)
            inv.Materials[material] = count;
        inv.Version++;
        var pack = SandboxHands.Pack(p.Id);
        var handle = pack.Handle;
        WorldStores.Copy(rec.Pack, pack);
        pack.Handle = handle;
        pack.Live = true;
        pack.Version++;
        SandboxHands.PackDirty.Add(p.Id);
        ref var h = ref SandboxHands.Hand(p.Id);
        h.Tool = rec.Tool;
        h.Held = rec.Held;
        for (var s = 0; s < rec.Builds.Length; s++)
            if (rec.Builds[s].Length > 0 && ParseBuild(rec.Builds[s], out var build, out _) != null)
                GiveSandboxBuild(p, s, build!, full: true);
        if (p.Alive)
            WorldPlace(p, rec);
    }

    /// <summary>A person's place in the world, when it's still free.</summary>
    private void WorldPlace(Player p, WorldPlayer rec)
    {
        if (rec.HasPlace && rec.SpaceSeatBody != 0 && Constructs.Get(rec.SpaceSeatBody) is { SpaceShip: not 0 } ship && WorldReseat(p, rec))
        {
            p.Teleport(ship.WorldCom); // (in their seat out in space: the seat places them from here on)
            p.Yaw = rec.Yaw;
            p.Pitch = rec.Pitch;
            return;
        }
        var deck = rec.RideBody != 0 && Constructs.Get(rec.RideBody) is { } c && rec.RideSeg <= c.JointCount ? c : null;
        if (!rec.HasPlace || deck == null && World.Blocked(rec.Pos))
            return;
        p.Teleport(rec.Pos);
        if (deck != null)
            ConstructRide.Rejoin(ref p.Sim, deck, rec.RideSeg, rec.RideUp, rec.RideLocal); // (aboard, where they stood: the construct's pose decides the rest)
        p.Yaw = rec.Yaw;
        p.Pitch = rec.Pitch;
        if (rec.Health > 0)
            p.Health = Math.Min((int)rec.Health, p.Health);
    }

    /// <summary>
    /// A person who sat in a ship out in space sits in it again (standing up out there would drop them from orbit), if the
    /// construct is still a ship and the seat is free and whole. False: they are placed as anyone else.
    /// </summary>
    private bool WorldReseat(Player p, WorldPlayer rec)
    {
        if (rec.SpaceSeatBody == 0 || Constructs.Get(rec.SpaceSeatBody) is not { SpaceShip: not 0 } c || rec.SpaceSeatPart >= c.PartCount)
            return false;
        ref var seat = ref c.Parts[rec.SpaceSeatPart];
        var part = (int)rec.SpaceSeatPart;
        if (c.DefOf(part).Role != PartRole.Seat || c.DefOf(part).RemoteReceiver || seat.Broken || seat.SitterPlus1 != 0 || part == c.DriveSeat && c.Driver != 255)
            return false;
        ref var h = ref SandboxHands.Hand(p.Id);
        if (h.Seated)
            return false;
        h.Edit = EditKind.None;
        h.HasSeatLook = false;
        h.SeatBody = c.Id;
        h.SeatPart = (ushort)part;
        seat.SitterPlus1 = (byte)(p.Id + 1);
        if (part == c.DriveSeat)
        {
            c.Driver = p.Id;
            c.DriverSeat = part;
        }
        Console.WriteLine($"[worlds] {p.Name} sits in construct {c.Id} again (a ship in space)");
        return true;
    }

    /// <summary>Server, a spawn in a world: a person not yet back in play is put where they were (a warmup's and a countdown's spawns too).</summary>
    private void WorldOnSpawn(Player p)
    {
        if (p.Bot != null || WorldSession is not { Applied: true } ws)
            return;
        foreach (var rec in ws.Players)
            if (rec.Online == p.Id)
            {
                if (rec.Spawned)
                    return;
                SandboxHands.Hand(p.Id).Held = rec.Held;
                WorldPlace(p, rec);
                rec.Spawned = Phase == MatchPhase.Playing;
                return;
            }
    }

    /// <summary>Server: someone leaves a world. True: what they have stays in their record and what they built stays theirs for when they return.</summary>
    private bool WorldLeaves(Player p)
    {
        if (!WorldKnown(p) || WorldSession is not { Applied: true } ws)
            return false;
        var rec = WorldRecordOf(ws, p);
        WorldCatch(p, rec);
        _worldKeys[p.Id] = 0;
        rec.Online = 255;
        rec.Spawned = false;
        foreach (var c in Constructs.Bodies)
        {
            if (c.Owner == p.Id)
            {
                c.Owner = rec.Offline;
                Constructs.StructureDirty.Add(c.Id);
            }
            if (c.FrozenBy == p.Id)
                c.FrozenBy = rec.Offline;
        }
        return true;
    }
}
