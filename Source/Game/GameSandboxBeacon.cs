using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Inventory Beacons in a match (ConstructBeacons.cs; docs/sandbox/CONSTRUCTS.md "The Inventory Beacon"): what each player's
/// beacons show them. The server keeps a <see cref="BeaconView"/> per player who has met a beacon and sends its changes to
/// that player alone; the host reads its own directly; a client's copy is what it heard. Only in survival: in creative
/// parts are endless, and a beacon does nothing for anyone but its look line.
/// </summary>
public sealed partial class Game
{
    private readonly BeaconView?[] _sbBeacons = new BeaconView?[256];
    private static readonly BeaconView NoBeacon = new();

    /// <summary>What this player's beacons show them (a client: only the local player's is heard).</summary>
    public BeaconView SandboxBeacon(byte player) => _sbBeacons[player] ?? NoBeacon;

    private BeaconView BeaconViewOf(byte player) => _sbBeacons[player] ??= new BeaconView();

    /// <summary>The local player's beacon network is theirs to use (the Tab inventory opens on it).</summary>
    public bool SandboxBeaconOn => SandboxOn && LocalId != byte.MaxValue && !SandboxInfiniteParts && SandboxBeacon(LocalId).On;

    /// <summary>The Tab inventory may open here: the map's parts menu, or a beacon in range.</summary>
    public bool SandboxInventoryAllowed => SandboxPartsMenu || SandboxBeaconOn;

    /// <summary>Why the Tab inventory won't open.</summary>
    public string SandboxNoMenuWhy => SandboxBeacon(LocalId).Line is { Length: > 0 } why ? why : SandboxSurvivalOption ? "the parts menu is off in survival" : "the parts menu is off on this map";

    /// <summary>Server, every tick after the constructs: each player's view at its rate, and its changes to its client.</summary>
    private void SandboxBeaconTick()
    {
        var beacons = Constructs.Beacons;
        var survival = !SandboxInfiniteParts;
        if (ServerTick % ConstructBeacons.ViewEvery == 0)
            foreach (var p in Players.Values)
            {
                if (p.Bot != null || _sbBeacons[p.Id] == null && (beacons.Count == 0 || !survival))
                    continue;
                var v = BeaconViewOf(p.Id);
                if (survival && p.Alive && beacons.Count > 0)
                    beacons.Update(Constructs, p.Id, p.Eye, v);
                else if (v.State != BeaconState.None)
                {
                    v.Clear();
                    v.Reset = v.Unsent = true;
                }
            }
        foreach (var (_, cl) in _clients)
        {
            if (_sbBeacons[cl.Id] is not { } v || !ConstructBeacons.Pending(v))
                continue;
            ConstructBeacons.Write(SbW, v);
            Net.SendTo(cl.Guid, SbW.Span, PacketReliability.ReliableOrdered);
        }
    }

    /// <summary>Server: a joiner's copy is empty: their view goes whole at its next look.</summary>
    private void SendSandboxBeacon(RakNetGUID to)
    {
        if (_clients.TryGetValue(to.G, out var cl) && _sbBeacons[cl.Id] is { } v)
        {
            v.Clear();
            v.Forget();
        }
    }

    /// <summary>Both sides: a new match, a mode switch, a player gone: nothing shows.</summary>
    private void SandboxBeaconReset(byte player)
    {
        if (_sbBeacons[player] is not { } v)
            return;
        v.Clear();
        if (IsServer)
            v.Forget();
    }

    private SbText _sbBeaconLine;

    /// <summary>The beacon's line on the HUD: what it reaches, or the reason it is off.</summary>
    private void SandboxBeaconLine(Player me, List<HudLine> lines, System.Numerics.Vector4 dim)
    {
        if (SandboxInfiniteParts)
            return;
        var v = SandboxBeacon(me.Id);
        if (_sbBeaconLine.Stale(v.Version))
            _sbBeaconLine.Text = v.Line;
        if (_sbBeaconLine.Text.Length > 0)
            lines.Add(new HudLine(_sbBeaconLine.Text, dim));
    }

    /// <summary>Client (or host): take a material off the network, up to a load.</summary>
    public void SandboxTakeMaterial(byte material) => SandboxSend(CActKind.BeaconMaterial, part: material);
}
