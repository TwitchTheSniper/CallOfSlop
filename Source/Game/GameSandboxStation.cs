using System.Numerics;

namespace CallOfSlop;

/// <summary>Sandbox: the Building Station's actions (ConstructStation.cs; docs/sandbox/constructs/production.md "Building station").</summary>
public sealed partial class Game
{
    /// <summary>Server: CActKind.Station.</summary>
    private string? SandboxStationAct(ref PacketReader r, Player p, Vector3 eye)
    {
        var body = r.U16();
        var part = r.U16();
        var op = r.U8();
        var n = r.U16();
        var name = r.Str();
        if (op > (byte)StationOp.Give || name.Length > 96)
            return "a bad action";
        return SandboxHands.StationAct(Constructs, p.Id, eye, body, part, (StationOp)op, n, name, Constructs.Get(body) is { } c ? SandboxConfigRefusal(c, p.Id) : null);
    }

    /// <summary>Client (or the host itself): an action at a Building Station. The server checks reach, power and who may, and says why not.</summary>
    public void SandboxStation(ushort body, int part, StationOp op, ushort n = 0, string name = "")
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Station).U16(body).U16((ushort)part).U8((byte)op).U16(n).Str(name);
        SandboxCraftSend(w);
    }
}

public sealed partial class Game
{
    /// <summary>This peer: a weld's end for each part a Building Station fits, a link's sound for each link it lays (from the jobs as heard).</summary>
    private void StationSounds()
    {
        foreach (var c in Constructs.Bodies)
        {
            if (c.PieceRackCount == 0)
                continue;
            for (var k = 0; k < c.PieceRackCount; k++)
            {
                int part = c.PieceRacks[k];
                if (part >= c.PartCount || c.Parts[part].Station is not { } j || (j.HeardFit == j.FitSeq && j.HeardLink == j.LinkSeq))
                    continue;
                ConstructStation.Bay(c, part, out var floor, out _, out _);
                if (j.HeardFit != j.FitSeq)
                    BuildSounds.Play(BuildSound.WeldDone, floor);
                if (j.HeardLink != j.LinkSeq)
                    BuildSounds.Play(BuildSound.LinkCable, floor);
                (j.HeardFit, j.HeardLink) = (j.FitSeq, j.LinkSeq);
            }
        }
    }
}

public sealed partial class Game
{
    /// <summary>
    /// The window tool (host): the world is saved to a throwaway world file and loaded again in place, through the real save and the
    /// real load (WorldSaveNow, WorldApply), so what a Building Station was doing must come back and go on. What happened, in words.
    /// </summary>
    internal string StationReloadProbe()
    {
        if (!IsServer || !SandboxOn)
            return "not the host";
        if (WorldSession == null)
        {
            var made = WorldNew("station probe " + Environment.ProcessId, World.Map, SandboxSurvivalOption);
            WorldAttach(made);
            made.Live = true;
        }
        var ws = WorldSession!;
        var before = Constructs.Bodies.Count;
        if (!WorldSaveNow(wait: true))
            return "the world wasn't saved";
        ws.HasSave = true;
        Constructs.Clear();
        WorldApply(ws);
        return $"saved to {ws.Folder} and loaded: {before} bodies before, {Constructs.Bodies.Count} after";
    }
}

public sealed partial class Game
{
    /// <summary>The window tool (host): the world saved under a name in the save folder (--save-dir), through the real save.</summary>
    private static string StationProbeDir => Path.Combine(Path.GetTempPath(), "callofslop-station-probe");

    internal string StationSaveProbe(string name)
    {
        if (!IsServer || !SandboxOn)
            return "not the host";
        if (WorldSession == null)
        {
            var made = WorldNew(name, World.Map, SandboxSurvivalOption);
            WorldAttach(made);
            made.Live = true;
        }
        if (!WorldSaveNow(wait: true))
            return "the world wasn't saved";
        // (the tool plays on a throwaway copy of the saves: the world's folder is copied out, for another process to load)
        var from = Path.Combine(WorldStore.Root, WorldSession!.Folder);
        Directory.CreateDirectory(StationProbeDir);
        foreach (var file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(StationProbeDir, Path.GetFileName(file)), true);
        return $"saved as {WorldSession.Folder}, copied to {StationProbeDir}";
    }

    /// <summary>The window tool (host, a fresh process): a saved world loaded over this one, through the real load.</summary>
    internal string StationLoadProbe(string name)
    {
        if (!IsServer || !SandboxOn)
            return "not the host";
        var into = Path.Combine(WorldStore.Root, WorldStore.FolderFor(WorldStore.CleanName(name)));
        if (Directory.Exists(StationProbeDir))
        {
            Directory.CreateDirectory(into);
            foreach (var file in Directory.GetFiles(StationProbeDir))
                File.Copy(file, Path.Combine(into, Path.GetFileName(file)), true);
        }
        var entry = WorldStore.List().Find(e => e.Meta.Name.Equals(WorldStore.CleanName(name), StringComparison.OrdinalIgnoreCase));
        if (entry == null || WorldOpen(entry, out var why) is not { } ws)
            return "no such world";
        WorldSession = ws;
        Constructs.Clear();
        WorldApply(ws);
        return $"loaded {ws.Folder}: {Constructs.Bodies.Count} bodies";
    }
}
