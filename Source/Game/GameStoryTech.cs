using System.Numerics;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The story factions' technologies on the game's side (docs/story/STORY.md "Technologies"; the engine's side:
/// Source/Story/StoryFactions.cs, data: "teaches" in Story/Factions). A part a faction teaches is built by an Assembler only
/// for an owner the faction has taught it (themselves or their player faction), which it does once its standing with them
/// first comes to the technology's tier: no money, standing is what buys it. A faction that teaches tells the players it
/// taught on their HUD, and the radio can tell of it (a Teach event). Survival only (creative builds everything); host only.
/// An Antimatter Warhead set off with other people in its reach is an Atrocity: its owner and their group become Notorious.
/// </summary>
public sealed partial class Game
{
    private StoryRules? _techRules; // the rules the map below was made from
    private readonly Dictionary<ushort, (int Faction, int Tech)[]> _techOf = new(); // part id: the technologies that hold it

    /// <summary>The technologies that hold each part, made again when the story's rules change (a part name that isn't a part is logged once).</summary>
    private Dictionary<ushort, (int Faction, int Tech)[]> TechMap(StoryEngine s)
    {
        if (_techRules == s.Rules)
            return _techOf;
        _techRules = s.Rules;
        _techOf.Clear();
        var fs = s.Rules.Factions.Factions;
        for (var f = 0; f < fs.Length; f++)
            foreach (var t in fs[f].Teaches)
                foreach (var name in t.Parts)
                {
                    if (ConstructParts.Find(name) is not { } d)
                    {
                        Console.WriteLine($"[story] {fs[f].Name} teaches {t.Name}: no part called \"{name}\" (Story/Factions)");
                        continue;
                    }
                    _techOf[d.Id] = _techOf.TryGetValue(d.Id, out var had) ? [.. had, (f, t.Id)] : [(f, t.Id)];
                }
        return _techOf;
    }

    /// <summary>
    /// The Assembler's gate (<see cref="ConstructWorld.Taught"/>): a part no faction teaches is built for anyone; one that a
    /// faction does, only for an owner who has learnt it (from any faction that teaches it). No story: everything is built.
    /// </summary>
    private bool AssemblerTaught(byte owner, ConstructPartDef part)
    {
        if (Story is not { } s || !TechMap(s).TryGetValue(part.Id, out var techs))
            return true;
        var who = StoryByte(owner);
        if (who < 0)
            return false; // (nobody's, or an owner the story doesn't know: nobody taught them)
        foreach (var (f, t) in techs)
            if (s.Factions.Knows(who, f, t))
                return true;
        return false;
    }

    /// <summary>Once a second: what the factions taught, told on the HUD of every player here it was taught to (a player, or everyone in a group).</summary>
    private void StoryTaughtTick(StoryEngine s)
    {
        var taught = s.Factions.Taught;
        for (var k = 0; k < taught.Count; k++)
        {
            var (f, t, party, _) = taught[k];
            var def = s.Factions.Def(f);
            var tech = def.Teaches[t];
            var parts = string.Join(", ", tech.Parts.Take(4)) + (tech.Parts.Length > 4 ? $" and {tech.Parts.Length - 4} more" : "");
            var text = $"{def.Name} taught you {tech.Name}: your Assemblers build {parts}";
            Console.WriteLine($"[story] {def.Name} taught {s.World.NameOf(party)} {tech.Name}");
            foreach (var p in Players.Values)
                if (p.Bot == null && _storyWho[p.Id] is var who and >= 0 && (who == party || s.Factions.Party(who) == party))
                    SandboxHands.Notices.Add((p.Id, text));
        }
        taught.Clear();
    }

    /// <summary>A player's group was set (they joined one, or the story met them): they are taught what their standing already earns.</summary>
    private static void StoryTeachEarned(StoryEngine s, int who)
    {
        s.Factions.TeachEarned(who, who);
        if (s.Factions.Party(who) is var g && g != who)
            s.Factions.TeachEarned(g, who);
    }

    /// <summary>
    /// A massive blast began (<see cref="IConstructTargets.MegaStarted"/>): an antimatter one set off by a player with anyone
    /// else in its reach (a player, a story person's body) is an Atrocity: they and their group are Notorious.
    /// </summary>
    void IConstructTargets.MegaStarted(Vector3 at, float radius, byte look, byte by)
    {
        if (!IsServer || look != MegaBlasts.Antimatter || Story is not { } s || s.Rules.Factions.Atrocity < 0)
            return;
        if (!Players.TryGetValue(by, out var bomber) || bomber.Bot != null || StoryWho(bomber) is not (var who and >= 0))
            return;
        var near = 0;
        foreach (var p in Players.Values)
            if (p.Alive && p.Id != by && Vector3.DistanceSquared(p.Chest, at) <= radius * radius)
                near++;
        Console.WriteLine($"[story] {bomber.Name} set off an antimatter blast with {near} other{(near == 1 ? "" : "s")} in reach");
        if (near > 0)
            s.Record(s.Rules.Factions.Atrocity, who, place: StoryPlace());
    }
}
