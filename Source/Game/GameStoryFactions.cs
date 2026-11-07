using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The factions' side of the appraiser (docs/story/STORY.md "Factions"; the engine's side: Source/Story/StoryFactions.cs, data
/// Story/Factions). Constructs a faction claims (its vehicles, by name), the groups the players are in (their player
/// factions), what players do on a faction's channels (an email to it, its fax plugged in), the pages it faxes them, and
/// the notices it sends about what was done to it. Host only, like the rest of the story; nothing new goes on the wire.
/// </summary>
public sealed partial class Game
{
    private readonly (int Who, int Version)[] _storyGroup = new (int, int)[256]; // per player slot: the entity and the factions' version their group was set for

    /// <summary>The faction whose words a desk is (its "from"), as (its entity, the reader's group): (-1, -1) without a story or such a faction.</summary>
    private (int Us, int Them) DeskParty(ItDesk desk, Player? reader)
    {
        if (Story is not { } s || s.Factions.Find(desk.From) is not (var f and >= 0))
            return (-1, -1);
        var who = reader != null ? StoryWho(reader) : -1;
        return (s.Factions.EntityOf(f), who >= 0 ? s.Factions.Party(who) : -1);
    }

    /// <summary>Once a second: each player's group in the story is their player faction (or none).</summary>
    private void StoryGroupsTick()
    {
        if (Story is not { } s || ServerTick % World.TickRate != 0)
            return;
        var t = FactionTable;
        foreach (var p in Players.Values)
        {
            if (p.Bot != null || StoryWho(p) is not (var who and >= 0) || _storyGroup[p.Id] == (who, t.Version))
                continue;
            _storyGroup[p.Id] = (who, t.Version);
            var id = t.Of[p.Id];
            s.Factions.SetGroup(who, id != 0 && t.Names[id].Length > 0 ? s.Factions.Group(t.Names[id]) : -1);
        }
    }

    /// <summary>A player emailed a faction from a computer: an Email event (its deeds read the words).</summary>
    private void StoryEmail(ItDesk desk, Player from, string text)
    {
        if (Story is not { } s || s.Factions.Find(desk.From) is not (var f and >= 0) || StoryWho(from) is not (var who and >= 0))
            return;
        s.Factions.Email(f, who, text, StoryPlace());
    }

    /// <summary>A faction's fax printed for someone: a Fax event, from the faction to their group.</summary>
    private void StoryFaxed(ItDesk desk, Player? reader)
    {
        if (Story is not { } s || reader == null || s.Rules.Factions.Fax < 0)
            return;
        var (us, them) = DeskParty(desk, reader);
        if (us >= 0 && them >= 0)
            s.Record(s.Rules.Factions.Fax, us, them, place: StoryPlace());
    }

    /// <summary>A fax's first page: the faction has noticed its fax on this player's circuit (a Keep).</summary>
    private void StoryKeep(ItDesk desk, Player? reader, Construct c, int part)
    {
        if (Story is not { } s || reader == null || s.Rules.Factions.Keep < 0 || s.Factions.Find(desk.From) is not (var f and >= 0))
            return;
        var keeper = Players.TryGetValue(c.Owner, out var o) && o.Bot == null ? o : reader;
        if (StoryWho(keeper) is not (var who and >= 0))
            return;
        var name = c.DefOf(part).Name;
        var item = s.World.Get(StoryKind.Thing, StoryNameKey("part:" + name), name, s.Time);
        s.Record(s.Rules.Factions.Keep, who, s.Factions.EntityOf(f), place: StoryPlace(), item: item);
    }

    /// <summary>
    /// Once a second (the office's tick): what the factions have to say about what was done to them. A deed or a tier its
    /// standing came to that the desk has a notice for (Story/Office/IT.json "notices") is faxed to the player who did it,
    /// wherever they are, on the nearest plugged-in fax, a reply's time later.
    /// </summary>
    private void FactionNoticesTick(ItDesk desk, double now)
    {
        if (Story is not { } s)
            return;
        var notices = s.Factions.Notices;
        for (var k = 0; k < notices.Count; k++)
        {
            var n = notices[k];
            var def = s.Factions.Def(n.Faction);
            if (!def.Name.Equals(desk.From, StringComparison.OrdinalIgnoreCase) || _itMailCount >= MaxMail)
                continue;
            var on = def.NoticeName(n.Deed, n.Tier);
            var page = desk.NoticeFor(on, s.World, s.Factions.EntityOf(n.Faction), n.Party);
            if (page < 0 || PlayerOfStory(n.Actor) is not { } to)
                continue;
            Console.WriteLine($"[office] {def.Name} has a notice for {to.Name}: {on}");
            var due = now + desk.ReplyAfter((uint)(k + 1) * 2654435761u ^ (uint)s.Factions.Deeds);
            _itMail[_itMailCount++] = new ItMail { Page = page, Due = due, Until = due + 3600, To = to.Id, Follow = true };
        }
        notices.Clear();
    }

    /// <summary>The player here whose story entity this is, or null.</summary>
    private Player? PlayerOfStory(int entity)
    {
        foreach (var p in Players.Values)
            if (p.Bot == null && _storyWho[p.Id] == entity && entity >= 0)
                return p;
        return null;
    }
}
