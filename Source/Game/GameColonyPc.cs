using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The duty desk's computer (docs/story/STORY.md, "The duty desk"; the words: Story/Storylets/Colony.json, "duty-desk"): a guard
/// grumbles that the barracks' computer says it has thousands of viruses and not to turn it off, and the storylet's hook
/// ("pcvirus") hooks that computer with the Scammers' Tech Support scam (GameSandboxScams.cs). Nobody says how to mend it. It has no
/// switch: it runs off the barracks' Battery, and the cure is the Scammers' own (IT's): cut its power. Take the battery off with
/// the attach tool, fit it back, and the machine comes up clean. The colony's computer never gives up on its own (a scam elsewhere
/// closes when it runs out of messages; this one starts again). The quality Fixit carries the rest: 1 asked, 2 mended (a Mend,
/// for a player asked who is near when it comes back on clean after its power was cut), 3 thanked, 4 paid the scammers (they took the battery), 5 told
/// off. State is the computer part's own and the story's, both saved with the world; nothing new on the wire.
/// </summary>
public sealed partial class Game
{
    /// <summary>How near the duty desk's computer a player asked to mend it must be when it comes up clean (m).</summary>
    public const float ColonyPcNear = 15f;

    /// <summary>How long the colony's computer keeps quiet after its last message before the scam starts over (s).</summary>
    private const int ColonyPcRestart = 90;

    private int _vMend = -1, _qFixit = -1;
    private bool _colonyPcWasOff; // (seen without power since it was last on: a loaded world sees it again, so nothing is saved)
    private object? _colonyPcRules;

    /// <summary>The duty desk's computer: the Colony Barracks' first Computer. (null, -1): no barracks, or none in it.</summary>
    private (Construct? C, int Part) ColonyPc()
    {
        if (_opBarracks == 0 || Constructs.Get(_opBarracks) is not { } c)
            return (null, -1);
        for (var k = 0; k < c.PanelParts.Length; k++)
        {
            int i = c.PanelParts[k];
            if (i < c.PartCount && c.Parts[i].D.Office == OfficeKind.Computer)
                return (c, i);
        }
        return (null, -1);
    }

    private bool IsColonyPc(Construct c, int i) => c.Id == _opBarracks && ColonyPc() is var (pc, pi) && pc == c && pi == i;

    /// <summary>Server, the storylet's hook: the duty desk's computer catches Tech Support's virus (unless something already has it).</summary>
    private void ColonyPcInfect(Player by)
    {
        if (ScamDesk.Script is not { } desk || ColonyPc() is not ({ } c, var i))
        {
            Log($"[colony] no duty desk computer to infect for {by.Name}: no barracks, or no Computer in it");
            return;
        }
        ref var p = ref c.Parts[i];
        if (p.State != 0)
        {
            Log("[colony] the duty desk computer is already hooked");
            return;
        }
        var scam = Array.FindIndex(desk.Scams, s => s.Name == "support");
        if (scam < 0)
        {
            Log("[colony] no \"support\" scam in Story/Office/Scammers.json: the duty desk computer stays clean");
            return;
        }
        p.State = (byte)(scam + 1);
        p.Rounds = p.Count = 0;
        Log($"[colony] the duty desk computer has Tech Support's virus (for {by.Name}); its battery is the cure");
    }

    /// <summary>Server, once a second (from the scams' tick): when the computer comes back on clean after its power was cut, the players asked to mend it who are near have mended it.</summary>
    private void ColonyPcTick()
    {
        if (Story is not { } s || ColonyPc() is not ({ } c, var i))
            return;
        if (!ReferenceEquals(_colonyPcRules, s))
        {
            _colonyPcRules = s;
            _vMend = s.Rules.VerbId("Mend");
            _qFixit = s.Rules.Quality("Fixit")?.Id ?? -1;
        }
        if (_vMend < 0 || _qFixit < 0)
            return;
        if (!OfficeParts.PluggedIn(c, i))
        {
            _colonyPcWasOff = true; // (its power cut: the cure, when it comes back clean)
            return;
        }
        if (c.Parts[i].State != 0 || !_colonyPcWasOff)
            return; // (hooked, or clean without ever losing its power: the scammers paid, or never infected)
        _colonyPcWasOff = false;
        var at = OfficePartAt(c, i);
        foreach (var p in Players.Values)
        {
            if (p.Bot != null || !p.Alive || Vector3.DistanceSquared(p.Position, at) > ColonyPcNear * ColonyPcNear
                || StoryEntityOf(p) is not (var who and >= 0) || s.World.Quality(who, _qFixit) != 1)
                continue;
            Log($"[colony] {p.Name} mended the duty desk computer");
            s.World.SetQuality(who, _qFixit, 2); // (the storylet quality: Storylets/Colony.json, so not a verb's effect)
            s.Record(_vMend, who, place: StoryPlace());
        }
    }

    /// <summary>Server: the scammers took the colony computer's battery and left. Those asked to mend it who were near paid for the cure.</summary>
    private void ColonyPcPaid(Construct c, int i)
    {
        if (Story is not { } s || _qFixit < 0 || !IsColonyPc(c, i))
            return;
        var at = OfficePartAt(c, i);
        foreach (var p in Players.Values)
            if (p.Bot == null && Vector3.DistanceSquared(p.Position, at) <= ColonyPcNear * ColonyPcNear
                && StoryEntityOf(p) is (var who and >= 0) && s.World.Quality(who, _qFixit) == 1)
            {
                Log($"[colony] {p.Name} let the scammers clean the duty desk computer: they took the battery");
                s.World.SetQuality(who, _qFixit, 4);
            }
    }

    /// <summary>The colony's computer never runs out of things to say: after its last message and a rest, the scam starts over.</summary>
    private bool ColonyPcHolds(Construct c, int i, ref CPart p)
    {
        if (!IsColonyPc(c, i))
            return false;
        if (p.Count >= ColonyPcRestart)
            p.Rounds = p.Count = 0;
        return true;
    }
}
