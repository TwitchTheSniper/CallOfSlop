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
