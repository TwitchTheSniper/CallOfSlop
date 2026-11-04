using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The appraiser's milestones for mining and making (docs/story/STORY.md, "Milestones"). Small steady work never becomes
/// an event: what a player's drills, saws and buckets gather and what their refining machines make is summed in the
/// constructs (<see cref="ConstructWorld.Yield"/>) and, once a second, added to the story world as the Mined and Refined
/// relations (player to resource, in kilograms). Only crossing 1 t, 10 t or 100 t of one resource records an event (Mine,
/// Refine). An assembler's finished part counts in the Made relation (player to part), and the first part a player makes
/// on each machine tier records a Make. Resources, parts and tiers are Thing entities, found again by name.
/// </summary>
public sealed partial class Game
{
    /// <summary>The milestones, in kilograms.</summary>
    public static readonly int[] StoryTonnes = [1_000, 10_000, 100_000];

    private int _vMine, _vRefine, _vMake;
    private int _rMined, _rRefined, _rMade, _rMadeOn;
    private int _storyWorkTicks;
    private readonly int[] _storyMaterial = new int[256]; // a material id -> its entity (-1: not looked up yet)
    private readonly int[] _storyTier = new int[ConstructParts.MaxTier + 1];
    private readonly Dictionary<ushort, int> _storyPart = new();

    /// <summary>The verbs and relations the milestones use (from <see cref="StoryReset"/>, with the rest).</summary>
    private void StoryWorkIds(StoryRules rules)
    {
        (_vMine, _vRefine, _vMake) = (rules.VerbId("Mine"), rules.VerbId("Refine"), rules.VerbId("Make"));
        _rMined = rules.Relation("Mined")?.Id ?? -1;
        _rRefined = rules.Relation("Refined")?.Id ?? -1;
        _rMade = rules.Relation("Made")?.Id ?? -1;
        _rMadeOn = rules.Relation("MadeOn")?.Id ?? -1;
    }

    /// <summary>The entity ids the milestones keep (from <see cref="StoryForget"/>).</summary>
    private void StoryWorkForget()
    {
        Array.Fill(_storyMaterial, -1);
        Array.Fill(_storyTier, -1);
        _storyPart.Clear();
        _storyWorkTicks = 0;
        Constructs.YieldCount = 0;
    }

    /// <summary>Once a second (from <see cref="StoryTick"/>): what was gathered and refined since, and any milestone it crossed.</summary>
    private void StoryWorkTick()
    {
        if (++_storyWorkTicks < World.TickRate)
            return;
        _storyWorkTicks = 0;
        var world = Constructs;
        for (var i = 0; i < world.YieldCount; i++)
        {
            ref readonly var y = ref world.Yields[i];
            StoryYield(y.Kind, y.Owner, y.Material, y.Units);
        }
        world.YieldCount = 0;
    }

    private void StoryYield(CYieldKind kind, byte owner, byte material, int units)
    {
        var refined = kind == CYieldKind.Refined;
        var rel = refined ? _rRefined : _rMined;
        if (Story is not { } s || rel < 0 || StoryByte(owner) is not (var who and >= 0) || StoryMaterial(material) is not (var thing and >= 0))
            return;
        var kg = (long)MathF.Round(units * RecyclingTable.Current.WeightOf(material));
        if (kg <= 0)
            return;
        var was = s.World.Get(who, rel, thing);
        var now = (int)Math.Min(int.MaxValue, was + kg);
        s.World.Set(who, rel, thing, now);
        foreach (var t in StoryTonnes)
            if (was < t && now >= t)
                s.Record(refined ? _vRefine : _vMine, who, owner: who, place: StoryPlace(), item: thing, value: t / 1000);
    }

    /// <summary>An assembler finished a part (a <see cref="CNoteKind.Made"/> note): counted, and the first on its tier a Make.</summary>
    private void StoryMade(in CNote n)
    {
        if (Story is not { } s || StoryByte(n.By) is not (var who and >= 0) || StoryPart(n.Other) is not (var part and >= 0))
            return;
        if (_rMade >= 0)
            s.World.Set(who, _rMade, part, (int)Math.Min(int.MaxValue, s.World.Get(who, _rMade, part) + 1L));
        if (_rMadeOn < 0 || StoryTierOf(n.Tier) is not (var tier and >= 0) || s.World.Get(who, _rMadeOn, tier) > 0)
            return;
        s.World.Set(who, _rMadeOn, tier, 1);
        s.Record(_vMake, who, StoryConstruct(n.Body), who, StoryPlace(), part, n.Tier);
    }

    /// <summary>A bulk material as a Thing ("iron ore"). -1: none.</summary>
    private int StoryMaterial(byte material)
    {
        if (Story is not { } s || material == 0)
            return -1;
        var id = _storyMaterial[material];
        if (s.World.Valid(id))
            return id;
        var name = RecyclingTable.Current.NameOf(material);
        return _storyMaterial[material] = s.World.Get(StoryKind.Thing, StoryNameKey("material:" + name), name, s.Time);
    }

    /// <summary>A part as a Thing ("Gear Set"). -1: none.</summary>
    private int StoryPart(ushort def)
    {
        if (Story is not { } s)
            return -1;
        if (_storyPart.TryGetValue(def, out var id) && s.World.Valid(id))
            return id;
        string name;
        try
        {
            name = ConstructParts.Def(def).Name;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            return -1;
        }
        return _storyPart[def] = s.World.Get(StoryKind.Thing, StoryNameKey("part:" + name), name, s.Time);
    }

    /// <summary>A machine tier as a Thing ("tier 2 machines"). -1: none.</summary>
    private int StoryTierOf(byte tier)
    {
        if (Story is not { } s || tier < 1 || tier >= _storyTier.Length)
            return -1;
        var id = _storyTier[tier];
        if (s.World.Valid(id))
            return id;
        var name = $"tier {tier} machines";
        return _storyTier[tier] = s.World.Get(StoryKind.Thing, StoryNameKey(name), name, s.Time);
    }
}
