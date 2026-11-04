using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The build stations in the game (docs/sandbox/CONSTRUCTS.md "Build stations"): each player's view of their aids at the
/// beacons' rate and to their client on a change, the actions (<see cref="CActKind.BuildAid"/>) and the HUD's lines.
/// </summary>
public sealed partial class Game
{
    private readonly BuildAidView _noAids = new();

    /// <summary>A player's aids (a shared empty view for the unknown).</summary>
    public BuildAidView SandboxAids(byte player) => player == byte.MaxValue ? _noAids : SandboxHands.AidView(player);

    private BuildAidView AidViewOf(byte player) => SandboxHands.AidView(player);

    /// <summary>THE client's check: is this aid on for the local player? (<see cref="BuildAids.Free"/> is the one rule.)</summary>
    public bool SandboxAidLive(BuildAid aid) => SandboxOn && (BuildAids.Free(SandboxInfiniteParts) || SandboxAids(LocalId).State[(int)aid] == BeaconState.On);

    /// <summary>Server, every tick after the constructs: each player's aids at the view rate, and any change to their client.</summary>
    private void SandboxAidsTick()
    {
        var beacons = Constructs.Beacons;
        var free = BuildAids.Free(SandboxInfiniteParts);
        if (ServerTick % ConstructBeacons.ViewEvery == 0)
            foreach (var p in Players.Values)
            {
                if (p.Bot != null)
                    continue;
                var v = SandboxHands.AidView(p.Id);
                SandboxHands.GantryCheck(Constructs, p.Id, p.Eye, p.Alive);
                if (p.Alive)
                    beacons.AidStates(Constructs, p.Id, p.Eye, v);
                else
                    Array.Clear(v.Next);
                v.Commit(free);
            }
        foreach (var (_, cl) in _clients)
        {
            if (SandboxHands.AidViews[cl.Id] is not { Unsent: true } v)
                continue;
            v.Unsent = false;
            v.Write(SbW);
            Net.SendTo(cl.Guid, SbW.Span, PacketReliability.ReliableOrdered);
        }
    }

    /// <summary>Server: a joiner's copy is empty.</summary>
    private void SendSandboxAids(RakNetGUID to)
    {
        if (_clients.TryGetValue(to.G, out var cl) && SandboxHands.AidViews[cl.Id] is { } v)
            v.Unsent = true;
    }

    /// <summary>Both sides: a new match, a mode switch: nothing is on, the gantry lets go.</summary>
    private void SandboxAidsReset(byte player) => SandboxHands.AidViews[player]?.Clear();

    /// <summary>Server: one build-aid action. Why it was refused, or null.</summary>
    private string? SandboxAidAct(Player p, ref PacketReader r, Vector3 eye)
    {
        var op = (BuildAidOp)r.U8();
        var hands = SandboxHands;
        switch (op)
        {
            case BuildAidOp.Select:
                return SandboxSelectAct(p, eye, ref r); // (the selector and the desk: GameSandboxSelect.cs)
            case BuildAidOp.Mirror:
            {
                var axis = r.U8();
                var offset = r.F32();
                if (axis > ConstructMirror.Axes || !float.IsFinite(offset))
                    return "bad plane";
                if (hands.AidWhy(Constructs, p.Id, eye, BuildAid.Mirror) is { } why)
                    return why;
                offset = Math.Clamp(offset, -64, 64);
                var v = hands.AidView(p.Id);
                v.OwnAxis = axis;
                v.OwnOffset = offset;
                Constructs.Beacons.SetTables(Constructs, p.Id, eye, axis, offset);
                Constructs.Beacons.AidStates(Constructs, p.Id, eye, v);
                if (v.HasTable)
                {
                    v.TableAxis = axis; // (the table's setting reads back as set)
                    v.TableOffset = offset;
                }
                v.Commit(BuildAids.Free(SandboxInfiniteParts));
                return null;
            }
            case BuildAidOp.Line:
            {
                var s = new LineSpec { Body = r.U16(), Part = r.U16(), Socket = r.U8(), Variant = r.U8(), DirU = r.U8(), PitchU = r.F32(), CountU = r.U16(), DirV = r.U8(), PitchV = r.F32(), CountV = r.U16() };
                return hands.BuildLine(Constructs, p.Id, eye, s);
            }
            case BuildAidOp.Replace:
            {
                var body = r.U16();
                var part = r.U16();
                return hands.ReplacePart(Constructs, p.Id, eye, body, part, r.U16());
            }
            case BuildAidOp.Gantry:
                return hands.Gantry(Constructs, p.Id, eye, r.U16());
        }
        return "bad aid request";
    }

    /// <summary>Starts a client's (or the host's own) build-aid action: the writer, with its kind and op in. Finish with <see cref="SandboxAidSend"/>.</summary>
    public PacketWriter SandboxAidBegin(BuildAidOp op)
    {
        SbW.Begin(Msg.CAct).U8((byte)CActKind.BuildAid).U8((byte)op);
        return SbW;
    }

    public void SandboxAidSend(PacketWriter w) => SandboxSendBuilt(w);

    /// <summary>Server, at the top of each action: the gantry's reach, for a player it holds.</summary>
    private void SandboxAidReach(Player p, Vector3 eye)
    {
        var hands = SandboxHands;
        if (hands.AidViews[p.Id] is not { GantryBody: not 0 })
        {
            hands.GantryReach(false, default, 0);
            return;
        }
        var range = BeaconTable.Current.Range("gantry");
        if (BuildAids.Free(SandboxInfiniteParts) && !Constructs.Beacons.AidStation(Constructs, p.Id, eye, BuildAid.Gantry, out _, out _))
            hands.GantryReach(true, eye, range); // (creative with no gantry: its range from where you stand)
        else if (Constructs.Beacons.AidStation(Constructs, p.Id, eye, BuildAid.Gantry, out var spot, out var r))
            hands.GantryReach(true, spot, r);
        else
            hands.GantryReach(false, default, 0);
    }
}
