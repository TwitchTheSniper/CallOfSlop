using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>A body's own fields in a <see cref="WorldSnapshot"/>; its parts, links, joints and routes are slices of the snapshot's arrays.</summary>
public struct BodySnap
{
    public ushort Id, CarriedBy, CarryTag, CarryPart, SpaceShip;
    public byte Owner, FrozenBy, CarrySeg;
    public bool Wreck, Asleep, Damaged, Pinned, Frozen, HoverOn;
    public int Root, Gear;
    public float Fuel, Charge, HoverY;
    public Vector3 Pos, Vel, AngVel, CarryPos;
    public Quaternion Rot, CarryRot;
    public int Parts, PartCount, Links, LinkCount, Joints, JointCount, Routes, RouteCount, Nodes, Tracks, TrackCount, TrackParts;
}

/// <summary>One ship off the map's bubble, as space keeps it.</summary>
public struct ShipSnap
{
    public ushort Id;
    public int Primary;
    public Vec3d R, V;
    public string System; // (the Away section only: the star system it flies in, by name; null in the bubble's own)
}

/// <summary>
/// A world on one tick (docs/sandbox/WORLD-SAVES.md): plain copies of the arrays the tick owns, so the worker thread turns
/// them into bytes while the game plays on. The arrays are kept for the next save (<see cref="Grow{T}"/>), so a save
/// allocates only when the world has grown. Blittable structs copy in bulk: a <see cref="Array.Copy(Array, Array, int)"/> a
/// body is all the tick pays.
/// </summary>
public sealed class WorldSnapshot
{
    public WorldMeta Meta = new();
    public BodySnap[] Bodies = new BodySnap[256];
    public int BodyCount;
    public CPart[] Parts = new CPart[4096];
    public int PartCount;
    public CLink[] Links = new CLink[4096];
    public int LinkCount;
    public CJoint[] Joints = new CJoint[256];
    public int JointCount;
    public CRoute[] Routes = new CRoute[256];
    public int RouteCount;
    public CRouteNode[] Nodes = new CRouteNode[1024];
    public int NodeCount;
    public CTrack[] Tracks = [];
    public int TrackCount;
    public ushort[] TrackParts = [];
    public int TrackPartCount;
    public CStore[] Stores = [];
    public int StoreCount;
    public CCoupling[] Couplings = [];
    public int CouplingCount;
    public CXWire[] XWires = [];
    public int XWireCount;
    public WLink[] WorldLinks = [];
    public int WorldLinkCount;
    public WNode[] WorldNodes = [];
    public int WorldNodeCount;
    public CHose[] Hoses = [];
    public int HoseCount;
    public CConnector[] Connectors = [];
    public int ConnectorCount;
    public ushort NextConnector;
    public ushort NextTag;
    public DigOp[] Ops = [];
    public int OpCount;
    public bool GroundOnPlanet;
    public ResourceFieldState? Resources;
    public WorldPlayer[] Players = [];
    public readonly ushort[] OwnerRecord = new ushort[256]; // an owner byte -> its player record, plus one (0: nobody's)
    public long SpaceNs;
    public int SpaceWarp;
    public string SpaceSystem = "";
    public ShipSnap[] Ships = [];
    public int ShipCount;
    public ShipSnap[] AwayShips = []; // ships in other star systems (phase 6 jumps), with their system's name
    public int AwayCount;

    /// <summary>A planetoid map's centre in the World (null: not a planetoid), so a later build that moves the origin can move the save with it.</summary>
    public Vector3? PlanetCentre;
    public Story.StorySave? Story, StoryCopy; // the story layer's state (Game/GameStory.cs; null: none), and the copy kept between saves
    public double CaptureMs;
    public SaveWriter? Scratch, Head; // (the worker's)

    /// <summary>Room for <paramref name="n"/>; what a pooled array holds doesn't matter (a save overwrites it). Grows by half again so the next save needn't.</summary>
    public static void Grow<T>(ref T[] a, int n)
    {
        if (n > a.Length)
            a = new T[n + n / 2];
    }
}

/// <summary>What the world keeps of a person (docs/sandbox/WORLD-SAVES.md): found again by their saved key or Steam id.</summary>
public sealed class WorldPlayer
{
    public ulong Key, SteamId;
    public string Name = "";
    public bool HasPlace; // alive when last seen: they come back there
    public Vector3 Pos;
    public float Yaw, Pitch, Health;
    public ConstructTool Tool;
    public ushort Held = ConstructParts.None; // the part in the hands (this run's id)
    public (ushort Def, ushort Count)[] Parts = [];
    public (byte Material, ushort Count)[] Materials = [];
    public CStore Pack = new();
    public string[] Builds = []; // the built weapons in their slots, as text ("": none)
    public bool Spawned; // it has been spawned since it arrived: its place is used up
    public ushort RideBody; // standing in a construct when last seen: its id (stable in a save), and where in its frame (ConstructRideMove.cs)
    public byte RideSeg;
    public Vector3 RideLocal, RideUp;
    public ushort SpaceSeatBody, SpaceSeatPart; // seated in a ship out in space when last seen: they sit there again (there is nowhere to stand)
    public byte Online = 255; // the player id they have now (255: not here)
    public byte Offline = 255; // the owner byte their constructs carry while they're away (255: more people than bytes)

    public WorldPlayer Copy()
    {
        var c = (WorldPlayer)MemberwiseClone();
        c.Pack = new CStore();
        WorldStores.Copy(Pack, c.Pack);
        return c;
    }
}

/// <summary>A world in play on the host: its save, who has been in it, and the one save running on the worker.</summary>
public sealed class WorldSession
{
    /// <summary>Owner bytes from here up mean someone who is away (player ids stay under <see cref="Game.MaxPlayers"/>; 255: nobody).</summary>
    public const int OfflineBase = 64;

    public string Folder = "";
    public WorldMeta Meta = new();
    public bool HasSave; // the file exists: the world loads from it, the map's constructs don't build
    public bool Applied; // the world stands in the map (loaded, or a new one's set built)
    public bool Live; // play has run since: there is something to save
    public bool SaveAtStart; // a new world: saved as soon as play starts, so the list shows it
    public readonly List<WorldPlayer> Players = new();
    public readonly WorldSnapshot Snapshot = new();
    public Task<string?>? Writing;
    public double NextAutosave;
    public double PlayedBefore, PlayStarted = -1;
    public bool SaveQueued; // (a save asked for while one is on its way)
    public string LastBody = ""; // what the last save measured, for the log

    /// <summary>Their record, found by key or Steam id, or null.</summary>
    public WorldPlayer? Find(ulong key, ulong steam)
    {
        foreach (var p in Players)
            if (p.Online == 255 && (steam != 0 && p.SteamId == steam || key != 0 && p.Key == key))
                return p;
        return null;
    }

    /// <summary>A record for someone new.</summary>
    public WorldPlayer Add(ulong key, ulong steam, string name)
    {
        var r = new WorldPlayer { Key = key, SteamId = steam, Name = name };
        r.Offline = Players.Count + OfflineBase < 255 ? (byte)(Players.Count + OfflineBase) : (byte)255;
        Players.Add(r);
        return r;
    }

    /// <summary>Waits for the save on its way; the reason it failed, or null.</summary>
    public string? Finish()
    {
        if (Writing is not { } t)
            return null;
        Writing = null;
        try
        {
            return t.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}

/// <summary>Copies of what a store holds, for a snapshot.</summary>
public static class WorldStores
{
    public static void Copy(CStore from, CStore to)
    {
        to.Handle = from.Handle;
        to.Live = from.Live;
        to.SlotCount = from.SlotCount;
        Array.Copy(from.Def, to.Def, CStore.MaxSlots);
        Array.Copy(from.Rot, to.Rot, CStore.MaxSlots);
        Array.Copy(from.Count, to.Count, CStore.MaxSlots);
        Array.Copy(from.Health, to.Health, CStore.MaxSlots);
        to.Amount = from.Amount;
        to.Material = from.Material;
        to.Held = from.Held;
        to.InAmount = from.InAmount;
        to.InMaterial = from.InMaterial;
        to.Blocked = from.Blocked;
        to.JobTicks = from.JobTicks;
        to.JobAmount = from.JobAmount;
        to.JobMaterial = from.JobMaterial;
        to.MixN = from.MixN;
        if (from.MixN > 0)
        {
            to.MixMat ??= new byte[CStore.MaxMix];
            to.MixAmount ??= new int[CStore.MaxMix];
            Array.Copy(from.MixMat!, to.MixMat, from.MixN);
            Array.Copy(from.MixAmount!, to.MixAmount, from.MixN);
        }
        to.KindN = from.KindN;
        if (from.KindN > 0)
        {
            to.KindDef ??= new ushort[CStore.MaxKinds];
            to.KindCount ??= new int[CStore.MaxKinds];
            Array.Copy(from.KindDef!, to.KindDef, from.KindN);
            Array.Copy(from.KindCount!, to.KindCount, from.KindN);
        }
    }
}
