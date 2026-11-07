using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The IT Department's fax line (docs/story/STORY.md, "The IT Department's fax"; the parts: Constructs/OfficeParts.cs; the words:
/// Story/Office/IT.json). Server, once a second: a Fax Machine on a battery's circuit counts the seconds it has been plugged in, and when
/// its next page is due (the first within the hour) it springs to life and prints it to the players near it. A player who types in chat
/// at a plugged-in Computer emails the IT Department instead of chatting; its answer comes back on the nearest plugged-in fax.
/// What a fax has printed is its own (saved with the world: the page shown, the story's step, the seconds towards the next).
/// </summary>
public sealed partial class Game
{
    /// <summary>Who hears a fax print (m), how close a player stands to type at a computer (m), how far a reply finds a fax (m).</summary>
    public const float FaxHearing = 30f, ComputerReach = 2.5f, ReplyReach = 150f;

    /// <summary>
    /// A page on its way: from near which computer (or, <see cref="Follow"/>, to wherever player <see cref="To"/> is: a
    /// notice about something they did), which page, when it is due, when IT gives up on finding a fax, and whom it is for.
    /// </summary>
    private struct ItMail
    {
        public Vector3 From;
        public int Page;
        public double Due, Until;
        public byte To; // the player it is for (255: whoever reads it)
        public bool Follow;
    }

    private const int MaxMail = 16;
    private readonly ItMail[] _itMail = new ItMail[MaxMail];
    private int _itMailCount;

    /// <summary>Pages printed, and emails sent, this session (the self-test and the log read them).</summary>
    public int FaxPagesPrinted, ItEmailsSent;

    /// <summary>Server, every tick: once a second, the faxes and the replies due.</summary>
    private void SandboxOfficeTick(double now)
    {
        if (ServerTick % World.TickRate != 0 || ItDesk.Script is not { } desk)
            return;
        OfficeParts.Host = true;
        foreach (var c in Constructs.Bodies)
        {
            if (!c.Electrical)
                continue;
            for (var k = 0; k < c.PanelParts.Length; k++)
            {
                int i = c.PanelParts[k];
                if (i >= c.PartCount || c.Parts[i].D.Office != OfficeKind.Fax || !OfficeParts.PluggedIn(c, i))
                    continue;
                ref var p = ref c.Parts[i];
                if (p.Rounds >= desk.StepCount)
                    continue; // (the story is told: it still takes replies)
                if (p.Count < ushort.MaxValue)
                    p.Count++;
                if (p.Count >= desk.DueAfter(p.Rounds, FaxSeed(p, i)) && FaxPrintStep(c, i, p.Rounds, desk, now))
                {
                    p.Rounds++;
                    p.Count = 0;
                }
            }
        }
        FactionNoticesTick(desk, now); // (what the faction has to say about what was done to it: GameStoryFactions.cs)
        for (var m = _itMailCount - 1; m >= 0; m--)
        {
            ref var mail = ref _itMail[m];
            if (now < mail.Due)
                continue;
            var to = mail.To != 255 && Players.TryGetValue(mail.To, out var tp) ? tp : null;
            if (mail.Follow && to == null)
            {
                _itMail[m] = _itMail[--_itMailCount]; // (they left: the notice is dropped)
                continue;
            }
            var (fc, fi) = NearestFax(mail.Follow ? to!.Position : mail.From, ReplyReach);
            if (fc != null && FaxPrint(fc, fi, mail.Page, desk, now, to) || now > mail.Until)
                _itMail[m] = _itMail[--_itMailCount];
        }
    }

    /// <summary>
    /// A story page comes due on a fax: who reads it (the fax's owner, else the nearest player), the page as it reads for
    /// them (its alternatives ask the faction's standing and memory: Story/Office/IT.json), and the first one is the
    /// faction noticing its fax on their circuit (a Keep). False: nobody is there to read it yet.
    /// </summary>
    private bool FaxPrintStep(Construct c, int i, int step, ItDesk desk, double now)
    {
        var at = OfficePartAt(c, i);
        var reader = Players.TryGetValue(c.Owner, out var owner) && owner.Bot == null && Vector3.DistanceSquared(owner.Position, at) <= FaxHearing * FaxHearing ? owner : NearestPerson(at, FaxHearing);
        var (us, them) = DeskParty(desk, reader);
        var page = desk.PageFor(step, Story?.World, us, them);
        if (page < 0 || !FaxPrint(c, i, page, desk, now, reader))
            return page < 0;
        if (step == 0)
            StoryKeep(desk, reader, c, i); // (GameStoryFactions.cs)
        return true;
    }

    private Player? NearestPerson(Vector3 at, float reach)
    {
        Player? best = null;
        var bestD = reach * reach;
        foreach (var p in Players.Values)
            if (p.Bot == null && p.Alive && Vector3.DistanceSquared(p.Position, at) is var d && d <= bestD)
                (best, bestD) = (p, d);
        return best;
    }

    /// <summary>A fax's own seed: where it sits on its construct, so it keeps it through a save.</summary>
    private static uint FaxSeed(in CPart p, int i) =>
        (uint)i * 0x85ebca6bu ^ BitConverter.SingleToUInt32Bits(p.Pos.X) ^ BitConverter.SingleToUInt32Bits(p.Pos.Y) * 31u ^ BitConverter.SingleToUInt32Bits(p.Pos.Z) * 131u;

    private static Vector3 OfficePartAt(Construct c, int i)
    {
        ref var p = ref c.Parts[i];
        c.WorldFrame(p.Seg, out var q, out var t);
        return t + Vector3.Transform(p.Pos, q);
    }

    /// <summary>The nearest plugged-in office part of a kind within <paramref name="reach"/> of a place.</summary>
    private (Construct? C, int Part) NearestOffice(OfficeKind kind, Vector3 at, float reach)
    {
        (Construct? C, int Part) best = (null, -1);
        var bestD = reach * reach;
        foreach (var c in Constructs.Bodies)
        {
            if (!c.Electrical)
                continue;
            for (var k = 0; k < c.PanelParts.Length; k++)
            {
                int i = c.PanelParts[k];
                if (i >= c.PartCount || c.Parts[i].D.Office != kind || !OfficeParts.PluggedIn(c, i))
                    continue;
                var d = Vector3.DistanceSquared(OfficePartAt(c, i), at);
                if (d <= bestD)
                    (best, bestD) = ((c, i), d);
            }
        }
        return best;
    }

    private (Construct? C, int Part) NearestFax(Vector3 at, float reach) => NearestOffice(OfficeKind.Fax, at, reach);

    /// <summary>A page prints: its lines go to the players near the fax, and its panel shows it. False: nobody is there to read it yet, so it waits.</summary>
    private bool FaxPrint(Construct c, int i, int page, ItDesk desk, double now, Player? reader = null)
    {
        if (desk.Page(page) is not { } lines)
            return true; // (gone from the file: nothing to wait for)
        var at = OfficePartAt(c, i);
        if (!AnyoneNear(at, FaxHearing))
            return false;
        c.Parts[i].State = (byte)(page + 1);
        FaxPagesPrinted++;
        StoryFaxed(desk, reader ?? NearestPerson(at, FaxHearing)); // (the faction faxed them: "per my last fax")
        Console.WriteLine($"[office] fax page {page} prints: {lines[0]}");
        for (var k = 0; k < lines.Length; k++)
            OfficeLine(at, FaxHearing, k == 0 ? $"{OfficeParts.Prefix}{desk.From}: {lines[k]}" : OfficeParts.Prefix + lines[k], now, null);
        return true;
    }

    private bool AnyoneNear(Vector3 at, float reach)
    {
        foreach (var p in Players.Values)
            if (p.Bot == null && p.Alive && Vector3.DistanceSquared(p.Position, at) <= reach * reach)
                return true;
        return false;
    }

    /// <summary>A system line for the players within <paramref name="reach"/> of a place, or for one player only.</summary>
    private void OfficeLine(Vector3 at, float reach, string text, double now, Player? only)
    {
        text = ChatRules.Clean(text);
        if (text.Length == 0)
            return;
        bool Hears(Player p) => only != null ? p.Id == only.Id : p.Bot == null && Vector3.DistanceSquared(p.Position, at) <= reach * reach;
        if (Local is { Bot: null } me && Hears(me))
            AddChat(ChatRules.SystemId, "", text, now, ChatFlags.System);
        if (Net.IsOffline)
            return;
        _w.Begin(Msg.ChatLine).U8(ChatRules.SystemId).Str(text).U8((byte)ChatFlags.System).U8(NoTeam);
        foreach (var cl in _clients.Values)
            if (Players.TryGetValue(cl.Id, out var to) && Hears(to))
                Net.SendTo(cl.Guid, _w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>
    /// Server: a chat line from a player standing at a plugged-in Computer is an email to the IT Department, not chat: they alone see it
    /// went, and the answer is queued for the nearest fax. False: not at a computer, so it is chat.
    /// </summary>
    private bool SandboxEmail(Player from, string text, double now)
    {
        if (!SandboxOn || from.Bot != null)
            return false;
        var (pc, pi) = NearestOffice(OfficeKind.Computer, from.Position, ComputerReach);
        if (pc == null)
            return false;
        if (pc.Parts[pi].State > 0 && ScamEmail(from, pc, pi, text, now))
            return true; // (a hooked computer: the scammers read your email now, GameSandboxScams.cs)
        if (ItDesk.Script is not { } desk)
            return false;
        var at = OfficePartAt(pc, pi);
        ItEmailsSent++;
        Console.WriteLine($"[office] {from.Name} emails IT: {text}");
        OfficeLine(at, 0, $"[EMAIL] To: {desk.From}. {text}", now, from);
        StoryEmail(desk, from, text); // (the faction reads it: its deeds, GameStoryFactions.cs)
        var (us, them) = DeskParty(desk, from);
        var page = desk.ReplyTo(text, Story?.World, us, them);
        var fax = NearestFax(at, ReplyReach).C != null;
        OfficeLine(at, 0, fax ? desk.Sent : desk.NoFax, now, from);
        if (page >= 0 && _itMailCount < MaxMail)
        {
            var due = now + desk.ReplyAfter((uint)ItEmailsSent * 2654435761u ^ (uint)text.Length);
            _itMail[_itMailCount++] = new ItMail { From = at, Page = page, Due = due, Until = due + 1800, To = from.Id };
        }
        return true;
    }
}
