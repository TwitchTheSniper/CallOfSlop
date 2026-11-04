using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Creative or Survival (docs/sandbox/CONSTRUCTS.md, "Creative or Survival"): the host's lobby option, the one place every
/// peer asks which rules a Sandbox world plays by, and the start kit a survival world gets. The option travels in the
/// Welcome and the lobby state, so a client or a late joiner works the rules out exactly as the server does, from its
/// first tick. A map that sets <c>"infiniteParts": false</c> stays survival whatever the option says.
/// </summary>
public sealed partial class Game
{
    /// <summary>The host's lobby option: this game plays Sandbox as survival (server: Settings.SandboxSurvival; client: as last heard).</summary>
    public bool SandboxSurvivalOption { get; private set; }

    /// <summary>Is the world creative (endless parts: what noclip, the physics gun, the remover and the look line call creative)? The same answer on every peer.</summary>
    public bool SandboxInfiniteParts => !SandboxSurvivalOption && (World.Map.Sandbox?.InfiniteParts ?? true);

    /// <summary>This map plays survival by itself: the lobby's option has no say.</summary>
    public bool SandboxMapSurvival => World.Map.Sandbox is { InfiniteParts: false };

    /// <summary>The parts menu (and its blueprints) is on: the map's flag; survival chosen in the lobby turns it off, since a menu would make parts from nothing.</summary>
    public bool SandboxPartsMenu => SandboxOn && !SandboxSurvivalOption && World.Map.Sandbox is { PartsMenu: true };

    /// <summary>Server: the option as the game starts (the session's settings). Clients hear it instead.</summary>
    public void SetSandboxSurvival(bool survival)
    {
        if (IsServer)
            SandboxSurvivalOption = survival;
    }

    /// <summary>Host, in the lobby: Creative or Survival for the next match. What the old rules left in hands and inventories goes at its start, as with any new match.</summary>
    public void LobbySetSandboxSurvival(bool survival)
    {
        if (!IsServer || !InLobby || _lobbyCountdownUntil > 0 || survival == SandboxSurvivalOption)
            return;
        SandboxSurvivalOption = survival;
        SandboxShareRules();
        MarkLobbyDirty();
    }

    /// <summary>Client: what the host's Welcome or lobby state says.</summary>
    private void HearSandboxSurvival(bool survival)
    {
        if (survival == SandboxSurvivalOption)
            return;
        SandboxSurvivalOption = survival;
        SandboxShareRules();
    }

    /// <summary>Both sides: the rules' flags on what reads them, from the one answer above.</summary>
    private void SandboxShareRules()
    {
        var infinite = SandboxInfiniteParts;
        Constructs.CreativeRecycling = infinite; // (Recyclers: instant and unpowered in creative)
        SandboxHands.RemoverInSurvival = World.Map.Sandbox?.RemoverInSurvival ?? true;
        SandboxHands.Survival = !infinite && !(World.Map.Sandbox is { PartsMenu: true } && !SandboxSurvivalOption); // (physical storage: salvage comes into the hands)
        if (SandboxHands.InfiniteParts != infinite)
        {
            SandboxHands.InfiniteParts = infinite;
            SandboxInv.SetInfinite(infinite);
        }
    }

    private bool _sbKitBuilt;
    private readonly bool[] _sbKitTold = new bool[256];

    /// <summary>Server, as a player spawns: the first time this match, where the kit is.</summary>
    private void SandboxTellKit(Player p)
    {
        if (!_sbKitBuilt || p.Bot != null || _sbKitTold[p.Id])
            return;
        _sbKitTold[p.Id] = true;
        SandboxHands.Notices.Add((p.Id, "Survival: your start kit stands near the parts rack"));
    }

    /// <summary>
    /// Server, at the start of a match after the map's constructs: the start kit, what a fresh survival world can't make for
    /// itself. The map's own <c>"survivalStart"</c> list, or with the lobby's option the shipped default
    /// (Constructs/SurvivalStart.json); each piece goes on the nearest clear, level ground to the parts rack.
    /// </summary>
    private void SandboxStartKit(SandboxMap s)
    {
        _sbKitBuilt = false;
        Array.Clear(_sbKitTold);
        var kit = s.SurvivalStart ?? (SandboxSurvivalOption ? SurvivalStart.Current.Items : null);
        if (kit == null)
            return;
        var from = s.RackParts.Count > 0 ? s.Rack : World.Map.Spawns.Count > 0 ? World.Map.Spawns[0] : Vector3.Zero;
        foreach (var name in kit)
        {
            var c = SandboxKitPiece(name, from);
            if (c == null)
            {
                Log($"[sandbox] survival start: {name} could not be placed");
                continue;
            }
            _sbKitBuilt = true;
            Constructs.Events.Add($"survival start: {name} as construct {c.Id}");
        }
    }

    private Construct? SandboxKitPiece(string name, Vector3 from)
    {
        var ground = ConstructWorld.Ground(World.Query, from.X, from.Z);
        Construct? c;
        if (ConstructBuild.FindBlueprint(name) is { } bp)
            c = ConstructBuild.BuildOnGround(Constructs, bp, ground, 0, out _);
        else if (ConstructParts.Find(name) is { } def)
            c = ConstructBuild.PlaceNew(Constructs, def, ground, 0);
        else
            return null;
        if (c == null)
            return null;
        var mid = new Vector2(c.LocalMin.X + c.LocalMax.X, c.LocalMin.Z + c.LocalMax.Z) * 0.5f;
        var reach = new Vector2(c.LocalMax.X - c.LocalMin.X, c.LocalMax.Z - c.LocalMin.Z).Length() * 0.5f;
        // Rings round the rack: the first spot in the arena, level, and clear of what is already there.
        for (var r = reach + 5f; r < 120f; r += 3f)
            for (var k = 0; k < 16; k++)
            {
                var a = k * MathF.Tau / 16;
                var at = from + new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * r;
                if (!SandboxKitSpotClear(c, at, reach))
                    continue;
                var g = ConstructWorld.Ground(World.Query, at.X, at.Z);
                c.Pos += new Vector3(at.X - mid.X - ground.X, g.Y - ground.Y, at.Z - mid.Y - ground.Z);
                c.PrevPos = c.NetPos = c.NetFromPos = c.Pos;
                c.NetHistReset();
                if (!c.Anchored)
                {
                    c.Frozen = true; // (a rig stands as built until someone picks it up with the physics gun)
                    c.Rebuild();
                }
                return c;
            }
        Constructs.Remove(c); // (no room here: this map isn't made for a camp)
        return null;
    }

    private bool SandboxKitSpotClear(Construct self, Vector3 at, float reach)
    {
        var map = World.Map;
        if (MathF.Abs(at.X) + reach + 2f > map.HalfX || MathF.Abs(at.Z) + reach + 2f > map.HalfZ)
            return false;
        foreach (var b in Constructs.Bodies)
        {
            if (b == self)
                continue;
            var near = reach + b.Radius + 1f;
            if (Vector3.DistanceSquared(b.WorldCom with { Y = 0 }, at with { Y = 0 }) < near * near)
                return false;
        }
        var low = float.MaxValue;
        var high = float.MinValue;
        Span<Vector2> probes = [new(0, 0), new(reach, reach), new(-reach, reach), new(reach, -reach), new(-reach, -reach)];
        foreach (var d in probes)
        {
            var y = ConstructWorld.Ground(World.Query, at.X + d.X, at.Z + d.Y).Y;
            low = MathF.Min(low, y);
            high = MathF.Max(high, y);
        }
        return high - low < 0.5f;
    }
}
