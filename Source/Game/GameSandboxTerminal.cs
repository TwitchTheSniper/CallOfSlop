using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Smart Terminals in a match (ConstructTerminal.cs; docs/sandbox/CONSTRUCTS.md "The Smart Terminal"): the server keeps a
/// <see cref="TerminalView"/> per player who has stood at one and sends its changes to that player alone; a client's copy
/// is what it heard. Works in creative and survival: a pull moves real stock either way.
/// </summary>
public sealed partial class Game
{
    private readonly TerminalView?[] _sbTerminals = new TerminalView?[256];
    private static readonly TerminalView NoTerminal = new();

    /// <summary>What the terminal this player stands at shows (a client: only the local player's is heard).</summary>
    public TerminalView SandboxTerminal(byte player) => _sbTerminals[player] ?? NoTerminal;

    private TerminalView TerminalViewOf(byte player) => _sbTerminals[player] ??= new TerminalView();

    /// <summary>Server, every tick: pulls under way; at the view rate what each player stands at; its changes to its client.</summary>
    private void SandboxTerminalTick()
    {
        SandboxHands.TerminalStep(Constructs);
        var beacons = Constructs.Beacons;
        if (ServerTick % ConstructBeacons.ViewEvery == 0)
            foreach (var p in Players.Values)
            {
                if (p.Bot != null || _sbTerminals[p.Id] == null && beacons.TerminalCount == 0)
                    continue;
                var v = TerminalViewOf(p.Id);
                if (p.Alive)
                    beacons.UpdateTerminal(Constructs, p.Id, p.Eye, v);
                else
                    ConstructBeacons.LeaveTerminal(v);
            }
        foreach (var (_, cl) in _clients)
        {
            if (_sbTerminals[cl.Id] is not { } v || !ConstructBeacons.Pending(v.Stock))
                continue;
            ConstructBeacons.Write(SbW, v.Stock, v.Handle);
            Net.SendTo(cl.Guid, SbW.Span, PacketReliability.ReliableOrdered);
        }
    }

    /// <summary>Server: a joiner's copy is empty: their view goes whole at its next look.</summary>
    private void SendSandboxTerminal(RakNetGUID to)
    {
        if (_clients.TryGetValue(to.G, out var cl) && _sbTerminals[cl.Id] is { } v)
        {
            v.Handle = 0;
            v.Stock.Clear();
            v.Stock.Forget();
        }
    }

    /// <summary>Both sides: a new match, a mode switch, a player gone: nothing shows.</summary>
    private void SandboxTerminalReset(byte player)
    {
        if (_sbTerminals[player] is not { } v)
            return;
        v.Handle = 0;
        v.Stock.Clear();
        if (IsServer)
            v.Stock.Forget();
    }

    /// <summary>Client (or host): ask the terminal for a row's item, to the hands or its output.</summary>
    public void SandboxTerminalPull(ushort body, int part, in TerminalRow row, bool hands) =>
        SandboxSend(CActKind.TerminalPull, body: body, part: (ushort)part, body2: row.Id, socket: (byte)((hands ? 1 : 0) | (row.Material ? 2 : 0)));
}
