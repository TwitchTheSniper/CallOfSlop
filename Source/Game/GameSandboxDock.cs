using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// The Ship Docking Station (Karl, 2026-11-26; its part: Source/PartKit/Generators/ShipDockParts.cs, "shipDock"): a placeholder for all a
/// player will do with a ship at a dock. For now one thing: a spawned ship design (a body of <c>Design (#hash)</c> parts, DesignParts.cs)
/// whose bounding sphere comes within the station's range of its pad's middle, and which is slow, is docked. The use key on the station
/// opens the Ship Designer on that ship's design (from the shared design table, so a joined client has it); its APPLY TO SHIP sends the
/// edited design back (<see cref="CActKind.DockDesign"/>). The server checks it all again (the station, the player at it, the ship still
/// docked, its owner or the host, a creative world), keeps the new design in its table and its world (to every peer at the next flush,
/// before the body), and builds the new body in a scratch world a slice a tick (<see cref="ConstructBuild.GroundJob"/>). On the tick it is
/// ready the old body goes and the new one takes its id, its pose and its owner, still; the players riding it stay where they are, unless
/// the new hull is where they stand: those are put on the pad.
/// </summary>
public sealed partial class Game
{
    /// <summary>Docked: slower than this (m/s), and turning slower than <see cref="DockSpin"/> (rad/s).</summary>
    public const float DockSlow = 1.5f, DockSpin = 0.3f;

    /// <summary>How far (m, beyond the station's own radius) a player may stand from the station to use it, or to send its edit.</summary>
    public const float DockReach = 12f;

    /// <summary>Rebuilds under way at once on the server (each a ship); one per ship.</summary>
    public const int DockJobsMax = 2;

    /// <summary>The pad's middle in the world.</summary>
    public static Vector3 DockPad(Construct station, int part)
    {
        ref var p = ref station.Parts[part];
        station.WorldFrame(p.Seg, out var q, out var t);
        return t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, p.D.ShipDockPad), q);
    }

    /// <summary>The design ship docked at a station (the nearest), its design's entry, or why none is. Client and server alike; not per tick.</summary>
    public Construct? DockedShip(Construct station, int part, out DesignParts.Entry? design, out string why)
    {
        design = null;
        why = "";
        if (part < 0 || part >= station.PartCount || station.DefOf(part).ShipDockRange <= 0)
        {
            why = "that isn't a Ship Docking Station";
            return null;
        }
        var pad = DockPad(station, part);
        var range = station.DefOf(part).ShipDockRange;
        Construct? best = null;
        var bestD = float.MaxValue;
        var slowNo = false;
        foreach (var b in Constructs.Bodies)
        {
            if (b == station || b.PartCount == 0 || DesignParts.EntryOf(b.DefOf(0)) is null)
                continue;
            var d = Vector3.Distance(pad, b.WorldCom) - b.Radius;
            if (d > range)
                continue;
            if (b.Vel.Length() > DockSlow || b.AngVel.Length() > DockSpin)
            {
                slowNo = true;
                continue;
            }
            if (d < bestD)
                (best, bestD) = (b, d);
        }
        if (best == null)
        {
            why = slowNo ? "the ship isn't still enough to dock: slow it down" : $"no ship design is docked (bring one within {range:0} m of the pad)";
            return null;
        }
        for (var i = 0; i < best.PartCount; i++)
            if (DesignParts.EntryOf(best.DefOf(i)) is not { } e)
            {
                why = $"the docked ship carries a {best.DefOf(i).Name}, which isn't its design's: take it off first";
                return null;
            }
            else if (design == null)
                design = e;
            else if (!ReferenceEquals(design, e))
            {
                why = "the docked ship is made of more than one design";
                return null;
            }
        return best;
    }

    /// <summary>Why this player can't edit this ship at a dock, or null: its owner and the host can (anyone a ship nobody owns).</summary>
    public string? DockEditRefusal(Construct ship, byte player)
    {
        if (!SandboxInfiniteParts)
            return "a ship is rebuilt at the dock only in a creative world for now";
        if (ship.Owner == 255 || ship.Owner == player || (IsServer && player == LocalId))
            return null;
        return $"only its owner ({Players.GetValueOrDefault(ship.Owner)?.Name ?? $"player {ship.Owner}"}) or the host can edit this ship";
    }

    /// <summary>Client: the edited design to the server, to be built into the ship docked at that station.</summary>
    public void SandboxDockSend(ushort station, ushort part, ushort ship, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > DesignParts.MaxText)
        {
            SandboxSay($"the design is too big to send ({bytes.Length / 1024} KB)");
            return;
        }
        var w = new PacketWriter(bytes.Length + 64); // (a rare message, sized for its design)
        w.Begin(Msg.CAct).U8((byte)CActKind.DockDesign).U16(station).U16(part).U16(ship);
        w.U32((uint)bytes.Length).Bytes(bytes);
        SpawnActSend(w);
    }

    private sealed class DockJob
    {
        public ushort Ship, Station, Part;
        public byte Player;
        public string Hash = "", Name = "";
        public ConstructBuild.GroundJob Job = null!;
    }

    private readonly List<DockJob> _dockJobs = new(DockJobsMax);

    /// <summary>Server: CActKind.DockDesign after its kind byte: the edit checked and its rebuild started.</summary>
    private string? SandboxDockAct(Player p, ref PacketReader r)
    {
        var stationId = r.U16();
        var part = r.U16();
        var shipId = r.U16();
        var n = r.U32();
        if (n > DesignParts.MaxText)
            return "that design is too big";
        var json = Encoding.UTF8.GetString(r.Take((int)n));
        if (!p.Alive)
            return "you are dead";
        if (Constructs.Get(stationId) is not { } station || part >= station.PartCount || station.DefOf(part).ShipDockRange <= 0)
            return "the docking station is gone";
        if (Vector3.Distance(p.Position, DockPad(station, part)) > station.Radius + DockReach)
            return "you walked away from the docking station";
        if (DockedShip(station, part, out var old, out var why) is not { } ship)
            return why;
        if (ship.Id != shipId)
            return "another ship is docked there now";
        if (DockEditRefusal(ship, p.Id) is { } no)
            return no;
        if (_dockJobs.Any(j => j.Ship == shipId))
            return "that ship is being rebuilt already";
        if (_dockJobs.Count >= DockJobsMax)
            return "the docks are busy: try again in a moment";
        if (DesignParts.Canon(json) is not { } text || DesignParts.Register(text) is not { } hash)
            return $"that isn't a ship design this build reads ({DesignParts.LastRefusal})";
        if (hash == old!.Hash)
            return "the design is as it was: nothing to rebuild";
        var name = DesignParts.Find(hash)!.Name;
        Constructs.KeepShipDesign(hash); // (to everyone with the next flush, before the rebuilt body)
        _dockJobs.Add(new DockJob
        {
            Ship = shipId, Station = stationId, Part = part, Player = p.Id, Hash = hash, Name = name,
            Job = new ConstructBuild.GroundJob(Constructs, DesignParts.BlueprintOf(hash, name)),
        });
        SandboxHands.Notices.Add((p.Id, $"rebuilding the {name}"));
        Console.WriteLine($"[dock] player {p.Id} rebuilds ship {shipId} (\"{old.Name}\" {old.Hash}) as \"{name}\" ({hash}, {text.Length} bytes)");
        return null;
    }

    /// <summary>Server, each tick: the rebuilds under way, a slice each; a finished one takes its ship's place. Nothing while none is.</summary>
    private void SandboxDockTick()
    {
        for (var k = _dockJobs.Count - 1; k >= 0; k--)
        {
            var j = _dockJobs[k];
            if (!j.Job.Step(VehicleSpawner.SliceMs))
                continue;
            _dockJobs.RemoveAt(k);
            var why = DockSwap(j);
            SandboxHands.Notices.Add((j.Player, why ?? $"the {j.Name} is rebuilt"));
            Console.WriteLine($"[dock] ship {j.Ship}: {why ?? "rebuilt"} ({j.Job.CpuMs:0} ms in {j.Job.Slices} slices, the longest {j.Job.WorstSliceMs:0.0} ms)");
        }
    }

    private string? DockSwap(DockJob j)
    {
        if (j.Job.Body is not { } fresh)
            return $"the {j.Name} couldn't be built: {j.Job.Why}";
        if (Constructs.Get(j.Ship) is not { } ship)
            return "the ship went before its rebuild was ready";
        if (Constructs.Bodies.Count > Constructs.BodyCap || Constructs.PartTally - ship.PartCount + fresh.PartCount > Constructs.PartCap)
            return "the world is too full to rebuild the ship";
        var (pos, rot, owner, id) = (ship.Pos, ship.Rot, ship.Owner, ship.Id);
        var station = Constructs.Get(j.Station);
        var pad = station != null && j.Part < station.PartCount && station.DefOf(j.Part).ShipDockRange > 0 ? DockPad(station, j.Part) : (Vector3?)null;
        foreach (var p in Players.Values)
            if (SandboxHands.Hand(p.Id).GrabBody == id)
                SandboxHands.Release(Constructs, p.Id); // (the physics gun's hold names its parts: let go)
        Constructs.Remove(ship);
        if (j.Job.JoinAt(pos, rot, id, out var why) is not { } c)
        {
            Console.WriteLine($"[dock] REFUSED: ship {id} couldn't be put back ({why}): it is gone");
            return $"the {j.Name} couldn't join the world ({(why.Length > 0 ? why : "the world is full")})";
        }
        c.Owner = owner;
        // Its riders: kept where they stand unless the new hull is there, then onto the pad (or above where they were).
        var on = 0;
        foreach (var p in Players.Values)
        {
            if (!p.Alive || p.Sim.RideBody != id)
                continue;
            var feet = p.Position;
            ConstructRide.Clear(ref p.Sim); // (its parts are new: they land on it again)
            p.Sim.Position = feet;
            if (!InsideHull(c, feet + new Vector3(0, 0.4f, 0)) && !InsideHull(c, feet + new Vector3(0, 1.3f, 0)))
                continue;
            var to = pad is { } at ? at + new Vector3(-2f + (on % 3) * 1.5f, 0.1f, -2f + (on / 3 % 3) * 1.5f) : feet + new Vector3(0, c.Radius * 2 + 1, 0);
            on++;
            p.Teleport(to);
        }
        return null;
    }

    /// <summary>Is a world point inside one of a design body's hulls (with a 0.25 m margin)? At a rebuild only.</summary>
    private static bool InsideHull(Construct c, Vector3 world)
    {
        if (Vector3.Distance(world, c.WorldCom) > c.Radius + 1)
            return false;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            c.WorldFrame(p.Seg, out var q, out var t);
            var local = Vector3.Transform(world - t, Quaternion.Conjugate(q)) - p.Pos;
            if (p.Rot != 0)
                local = Vector3.Transform(local, Quaternion.Conjugate(Rot24.Quat(p.Rot)));
            foreach (var h in ConstructHulls.Of(p.D))
            {
                var inside = true;
                for (var f = 0; f + 1 < h.FaceStart.Length && inside; f++)
                    inside = Vector3.Dot(h.N[f], local - h.V[h.FaceIdx[h.FaceStart[f]]]) < 0.25f;
                if (inside && h.N.Length > 0)
                    return true;
            }
        }
        return false;
    }

    /// <summary>The rebuilds under way go (a new match or world).</summary>
    private void SandboxDockReset() => _dockJobs.Clear();
}
