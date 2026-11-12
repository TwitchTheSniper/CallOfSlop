using System.Numerics;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// Lore in the colony (docs/story/STORY.md "Lore"; the engine is Source/Story/StoryLore.cs, data Story/Lore): small things
/// in the colony's buildings a player can look at closely, each a part of its own (the part kit's lore parts,
/// Constructs/Parts/Lore). The host stands each item with an <c>"at"</c> where it belongs, a pinned construct of one part
/// inside its building, once per world (the story's Thing "placed lore ID" notes it, so one taken away isn't put back);
/// the use key on it opens the words on the looker's own screen (Program.StoryLore.cs) and tells the host, which counts
/// the look (the relation Inspected, so a topic may open with someone: "?them Inspected 'lore edie-tally'"), does what
/// the first look does, and has whoever is named to notice say their line when their body is near. No protocol of its
/// own: the look is the ordinary <see cref="CActKind.Use"/> on the part. Host only.
/// </summary>
public sealed partial class Game
{
    /// <summary>How far from the colony's middle a lore item is looked for when a world loads (m).</summary>
    public const float LoreFindReach = 250f;

    private ushort[] _loreBody = new ushort[LoreRules.MaxItems]; // server: each item's construct (0: none yet)
    private bool _loreScanned;
    private double _loreAt;

    /// <summary>Both sides, a new match (from <see cref="OpeningReset"/>): nothing found yet.</summary>
    private void LoreReset()
    {
        Array.Clear(_loreBody);
        _loreScanned = false;
        _loreAt = 0;
    }

    /// <summary>The construct of one of the colony's buildings, by its blueprint's name (0: none here).</summary>
    private ushort LoreBuilding(string name) => name switch
    {
        "Penal Colony" => _opColony,
        "Colony Barracks" => _opBarracks,
        "Remote Works" => _dpWorks,
        _ => 0,
    };

    /// <summary>Server, from <see cref="StoryTick"/>: every few seconds, each item with a place stands there once (and is found again after a load).</summary>
    private void LoreTick(StoryEngine s, double now)
    {
        if (now < _loreAt || !_opOn || _opColony == 0)
            return;
        _loreAt = now + 3;
        var items = s.Rules.Lore.Items;
        if (!_loreScanned)
        {
            _loreScanned = true;
            foreach (var c in Constructs.Bodies)
                if (c.PartCount == 1 && s.Rules.Lore.ByPart(c.DefOf(0).Name) is var k and >= 0 && _loreBody[k] == 0
                    && Vector3.DistanceSquared(c.WorldCom, _opMiddle) < LoreFindReach * LoreFindReach)
                    _loreBody[k] = c.Id;
        }
        for (var k = 0; k < items.Length; k++)
        {
            var t = items[k];
            if (t.At.Length != 3 || _loreBody[k] != 0 && Constructs.Get(_loreBody[k]) != null)
                continue;
            _loreBody[k] = 0;
            var placed = StoryFactions.Key("loreplaced:" + t.Id);
            if (s.World.Find(StoryKind.Thing, placed) >= 0)
                continue; // (it stood once: taken away since, it stays away)
            if (Constructs.Get(LoreBuilding(t.In)) is not { } host || ConstructParts.Find(t.Part) is not { } def)
                continue;
            var rot = Quaternion.Normalize(host.Rot * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -t.Yaw * MathF.PI / 180f));
            var c = ConstructBuild.PlaceNew(Constructs, def, new FreePlacement { Pos = host.ToWorld(new Vector3(t.At[0], t.At[1], t.At[2])), Rot = rot, Anchor = true });
            if (c == null)
            {
                Log($"[lore] the {t.Part} ({t.Id}) wasn't placed in the {t.In}");
                continue;
            }
            _loreBody[k] = c.Id;
            s.World.Get(StoryKind.Thing, placed, "placed lore " + t.Id, s.Time);
            Constructs.Events.Add($"lore: the {t.Part} as construct {c.Id}");
        }
    }

    /// <summary>
    /// Server, from the <see cref="CActKind.Use"/> act: the part used is a lore item. Counts the look, does what the first
    /// look does, and has whoever notices say so. True when it was one (nothing else uses the part).
    /// </summary>
    private bool LoreUse(Player p, ushort body, ushort part, Vector3 eye)
    {
        if (Story is not { } s || Constructs.Get(body) is not { } c || part >= c.PartCount)
            return false;
        var item = s.Rules.Lore.ByPart(c.DefOf(part).Name);
        if (item < 0)
            return false;
        if (Vector3.Distance(eye, c.ToWorld(c.Parts[part].Pos)) > ConstructBuild.UseReach + 2f || StoryWho(p) is not (var who and >= 0))
            return true;
        if (s.Lore.Inspect(who, item) != 0)
            return true; // (looked at before: only the words again, on their screen)
        var t = s.Rules.Lore.Items[item];
        Log($"[lore] {p.Name} looked at the {t.Part} ({t.Id})");
        if (t.NoticedBy.Length > 0 && s.World.Find(t.NoticedBy) is var person and >= 0 && StoryBodyOf(person) is { Alive: true } them
            && Vector3.DistanceSquared(them.Position, p.Position) <= t.NoticedReach * t.NoticedReach && s.Lore.Noticed(who, item, person) is { } line)
            s.Talk.SayFor(person, who, line, s.Time, -1, -1);
        return true;
    }
}
