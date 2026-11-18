using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>What one peer knows of the factions' map (the command view's FACTIONS layer, the Faction window's planets): who holds each planet, and the
/// groups this player knows of (fog of war: the user's answer 7). The host fills its own; a client's comes in <see cref="Msg.FactionMap"/>.</summary>
public sealed class FactionMapView
{
    public const int MaxGroups = 32;

    public struct Group
    {
        public byte Power, Ships, Planet, ToPlanet, Progress; // Planet/ToPlanet: into this view's planets (255: none); Progress: a transit's share, 0-255
        public FarTask Task;
        public bool Here, Stale, Threat, Real; // Here: Pos is on this peer's world
        public Vector3 Pos;
        public ushort Age; // seconds since it was seen (stale: since the news named it)
    }

    public int PlanetCount;
    public readonly string[] Planet = new string[PowerControllers.MaxPlanets];
    public readonly byte[] Holder = new byte[PowerControllers.MaxPlanets], Leader = new byte[PowerControllers.MaxPlanets], Share = new byte[PowerControllers.MaxPlanets];
    public readonly Group[] Groups = new Group[MaxGroups];
    public int GroupCount;
    public int Version; // bumped on every change (the UI's labels)
}

/// <summary>
/// Territory and the far layer, the game's side (docs/sandbox/design/faction-controllers.md phase 4; the minds are Source/Crew/CrewTerritory.cs): once a
/// second each player is told who holds each planet and the faction groups they know of: those on their world within their sensors' reach, and those the
/// news named in the last minutes (stale: where they were then). Missions, stocks and unseen groups are never sent. Server only but the read.
/// </summary>
public sealed partial class Game
{
    /// <summary>This peer's view of the factions' map (the host's for its own player; a client's as sent).</summary>
    public readonly FactionMapView FactionMap = new();

    /// <summary>A player's sensors reach this far (m) for faction groups on their world.</summary>
    public const float FactionSensorRange = 8000;

    /// <summary>The news names a group for this long (s): a client is shown where it was then.</summary>
    public const float FactionReportSeconds = 300;

    private readonly FactionMapView _mapScratch = new();

    /// <summary>Server, once a second: each player's view of the map, sent (the host's own filled in place).</summary>
    private void CrewTerritoryTick()
    {
        if (ServerTick % World.TickRate != 7 || Controllers.PlanetCount == 0)
            return;
        foreach (var p in Players.Values)
        {
            if (p.Bot != null)
                continue;
            if (p.Id == LocalId)
            {
                MapFor(p, FactionMap);
                continue;
            }
            if (Net.IsOffline || GuidOfId(p.Id) is not { } to)
                continue;
            MapFor(p, _mapScratch);
            WriteFactionMap(SbW, _mapScratch);
            Net.SendTo(to, SbW.Span, PacketReliability.Unreliable);
        }
    }

    private void MapFor(Player p, FactionMapView v)
    {
        var c = Controllers;
        v.PlanetCount = c.PlanetCount;
        for (var i = 0; i < c.PlanetCount; i++)
        {
            v.Planet[i] = c.Territory.Planets[i];
            (v.Holder[i], v.Leader[i], v.Share[i]) = (c.PlanetHolder[i], c.PlanetLeader[i], (byte)Math.Clamp(c.PlanetShare[i] * 255, 0, 255));
        }
        v.GroupCount = 0;
        var here = World.Map.Name;
        var at = p.Position;
        var now = ((IPowerHost)this).SpaceSeconds is var sp and >= 0 ? sp : c.Tick / (double)World.TickRate;
        for (var i = 0; i < PowerControllers.MaxFar && v.GroupCount < FactionMapView.MaxGroups; i++)
        {
            ref var g = ref c.Far[i];
            if (g.Task == FarTask.None || g.State == FarState.Gather)
                continue;
            var onHere = g.Planet >= 0 && g.PosKnown && c.Territory.Planets[g.Planet].Equals(here, StringComparison.OrdinalIgnoreCase);
            var seen = onHere && p.Alive && Vector3.Distance(at, g.Pos) < FactionSensorRange;
            var reported = g.ReportedAt != 0 && c.Tick - g.ReportedAt < (long)(FactionReportSeconds * World.TickRate);
            if (!seen && !reported)
                continue;
            ref var o = ref v.Groups[v.GroupCount++];
            var standing = StandingOf(p, g.Power);
            o = new FactionMapView.Group
            {
                Power = g.Power, Ships = g.Ships, Task = g.Task, Real = g.Body == 2, Here = seen || onHere && reported, Stale = !seen,
                Planet = (byte)(g.Planet >= 0 ? g.Planet : 255), ToPlanet = (byte)(g.ToPlanet >= 0 ? g.ToPlanet : 255),
                Pos = seen ? g.Pos : g.ReportPos,
                Age = (ushort)(seen ? 0 : Math.Min(65535, (c.Tick - g.ReportedAt) / World.TickRate)),
                Progress = (byte)(g.State == FarState.Transit && g.Arrive > g.Depart ? Math.Clamp((now - g.Depart) / (g.Arrive - g.Depart) * 255, 0, 255) : 0),
                Threat = standing < 0 && (g.Task == FarTask.Take || seen && Vector3.Distance(at, g.Pos) < 3000),
            };
        }
        v.Version++;
    }

    /// <summary>How power <paramref name="power"/> sees this player (their faction's standing, else their own).</summary>
    private int StandingOf(Player p, byte power)
    {
        if (power == 0 || power > FactionTable.MaxStory)
            return 0;
        var t = FactionTable;
        var party = t.Of[p.Id];
        return party != 0 ? t.StoryStanding[party * FactionTable.MaxStory + power - 1] : t.PlayerStanding[p.Id * FactionTable.MaxStory + power - 1];
    }

    /// <summary><see cref="Msg.FactionMap"/>: u8 planets (text name, u8 holder, u8 leader, u8 share 0-255), u8 groups (u8 power, u8 task, u8 flags (1 here, 2 stale,
    /// 4 threat, 8 real), u8 planet, u8 to-planet, u8 progress, u8 ships, 3 x f32 place, u16 age s).</summary>
    private static void WriteFactionMap(PacketWriter w, FactionMapView v)
    {
        w.Begin(Msg.FactionMap).U8((byte)v.PlanetCount);
        for (var i = 0; i < v.PlanetCount; i++)
            w.Str(v.Planet[i]).U8(v.Holder[i]).U8(v.Leader[i]).U8(v.Share[i]);
        w.U8((byte)v.GroupCount);
        for (var i = 0; i < v.GroupCount; i++)
        {
            ref var g = ref v.Groups[i];
            w.U8(g.Power).U8((byte)g.Task).U8((byte)((g.Here ? 1 : 0) | (g.Stale ? 2 : 0) | (g.Threat ? 4 : 0) | (g.Real ? 8 : 0))).U8(g.Planet).U8(g.ToPlanet).U8(g.Progress).U8(g.Ships)
                .F32(g.Pos.X).F32(g.Pos.Y).F32(g.Pos.Z).U16(g.Age);
        }
    }

    /// <summary>Client: the factions' map as this player knows it (after the id).</summary>
    internal void ReadFactionMap(ref PacketReader r)
    {
        var v = FactionMap;
        int n = r.U8();
        v.PlanetCount = Math.Min(n, PowerControllers.MaxPlanets);
        for (var i = 0; i < n; i++)
        {
            var name = r.Str();
            var (h, l, s) = (r.U8(), r.U8(), r.U8());
            if (i >= PowerControllers.MaxPlanets)
                continue;
            if (v.Planet[i] != name)
                v.Planet[i] = name; // (the same name as last time: the old string is kept)
            (v.Holder[i], v.Leader[i], v.Share[i]) = (h, l, s);
        }
        int gn = r.U8();
        v.GroupCount = 0;
        for (var i = 0; i < gn; i++)
        {
            var g = new FactionMapView.Group { Power = r.U8(), Task = (FarTask)r.U8() };
            var f = r.U8();
            (g.Here, g.Stale, g.Threat, g.Real) = ((f & 1) != 0, (f & 2) != 0, (f & 4) != 0, (f & 8) != 0);
            (g.Planet, g.ToPlanet, g.Progress, g.Ships) = (r.U8(), r.U8(), r.U8(), r.U8());
            g.Pos = new Vector3(r.F32(), r.F32(), r.F32());
            g.Age = r.U16();
            if (v.GroupCount < FactionMapView.MaxGroups)
                v.Groups[v.GroupCount++] = g;
        }
        v.Version++;
    }
}
