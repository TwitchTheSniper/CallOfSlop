namespace CallOfSlop;

/// <summary>
/// The Scammers (docs/story/STORY.md, "The Scammers"; the words: Story/Office/Scammers.json; the screen and the click: Constructs/OfficeParts.cs).
/// A plugged-in Computer's screen carries two adverts. Clicking one hooks the computer: the advert's scam writes to the players at it,
/// a message every half a minute or so, and whatever they type there goes to the scammers, not IT. Fall for it ("ok", "yes", a gift
/// card) and they take a share of the computer's battery and vanish. The cure is IT's: turn it off and on again (cut its power).
/// The IT Department notices, and faxes about it. State is the computer part's own and saved with the world; nothing new on the wire.
/// </summary>
public sealed partial class Game
{
    /// <summary>Who reads a hooked computer's messages (m).</summary>
    public const float ScamHearing = 10f;

    /// <summary>An answer on its way to a hooked computer.</summary>
    private struct ScamMail
    {
        public ushort Body;
        public int Part, Scam, Reply;
        public double Due;
    }

    private readonly ScamMail[] _scamMail = new ScamMail[MaxMail];
    private int _scamMailCount;

    /// <summary>Scam messages delivered, and batteries emptied, this session (the log reads them).</summary>
    public int ScamMessages, ScamsFallenFor;

    /// <summary>Server, every tick: once a second, the hooked computers and the answers due.</summary>
    private void SandboxScamTick(double now)
    {
        if (ServerTick % World.TickRate != 0 || !SandboxOn || ScamDesk.Script is not { } desk)
            return;
        foreach (var c in Constructs.Bodies)
            for (var k = 0; k < c.PanelParts.Length; k++)
            {
                int i = c.PanelParts[k];
                if (i >= c.PartCount || c.Parts[i].State == 0 || c.Parts[i].D.Office != OfficeKind.Computer)
                    continue;
                ScamStep(c, i, desk, now);
            }
        for (var m = _scamMailCount - 1; m >= 0; m--)
        {
            if (now < _scamMail[m].Due)
                continue;
            ScamAnswer(_scamMail[m], desk, now);
            _scamMail[m] = _scamMail[--_scamMailCount];
        }
        ColonyPcTick(); // (the duty desk's computer mended: GameColonyPc.cs)
    }

    /// <summary>One hooked computer, a second on: cured if it lost power, else its next message when it is due and someone is there to read it.</summary>
    private void ScamStep(Construct c, int i, ScamDesk desk, double now)
    {
        ref var p = ref c.Parts[i];
        var at = OfficePartAt(c, i);
        if (!OfficeParts.PluggedIn(c, i) || p.State > desk.Scams.Length)
        {
            Console.WriteLine($"[office] a hooked computer lost power: cured");
            p.State = 0;
            p.Rounds = p.Count = 0;
            OfficeLine(at, ScamHearing, OfficeParts.Pc + desk.Cured, now, null);
            return;
        }
        var scam = desk.Scams[p.State - 1];
        if (p.Count < ushort.MaxValue)
            p.Count++;
        if (p.Rounds >= scam.Messages.Length)
        {
            if (ColonyPcHolds(c, i, ref p))
                return; // (the duty desk's computer starts over: GameColonyPc.cs)
            if (p.Count >= desk.EveryMin)
            {
                p.State = 0;
                p.Rounds = p.Count = 0;
                OfficeLine(at, ScamHearing, OfficeParts.Pc + desk.Closed, now, null);
            }
            return;
        }
        if (p.Count < desk.DueAfter(p.Rounds, OfficeParts.Seed(p, i)) || !AnyoneNear(at, ScamHearing))
            return;
        if (p.Rounds == 0 && !IsColonyPc(c, i))
            ItNoticesMalware(at, now); // (the colony's computer starts over again and again: IT isn't faxed each time)
        ScamLines(at, scam, scam.Messages[p.Rounds], now);
        p.Rounds++;
        p.Count = 0;
    }

    private void ScamLines(System.Numerics.Vector3 at, ScamDesk.Scam scam, string[] lines, double now)
    {
        ScamMessages++;
        Console.WriteLine($"[office] {scam.From} writes: {lines[0]}");
        for (var k = 0; k < lines.Length; k++)
            OfficeLine(at, ScamHearing, k == 0 ? $"{OfficeParts.Inbox}{scam.From}: {lines[k]}" : OfficeParts.Inbox + lines[k], now, null);
    }

    /// <summary>The IT Department sees everything: a hooked computer gets a fax about it, if a fax is near.</summary>
    private void ItNoticesMalware(System.Numerics.Vector3 at, double now)
    {
        if (ItDesk.Script is not { Malware: not null } it || _itMailCount >= MaxMail)
            return;
        var due = now + it.ReplyAfter((uint)ScamMessages * 2246822519u);
        _itMail[_itMailCount++] = new ItMail { From = at, Page = it.MalwarePage, Due = due, Until = due + 600, To = 255 };
    }

    /// <summary>Server: a player typed at a hooked computer. The scammers read it, and answer soon. False: the hook is stale (it's IT's again).</summary>
    private bool ScamEmail(Player from, Construct pc, int pi, string text, double now)
    {
        if (ScamDesk.Script is not { } desk || pc.Parts[pi].State > desk.Scams.Length)
            return false;
        var scam = pc.Parts[pi].State - 1;
        var at = OfficePartAt(pc, pi);
        Console.WriteLine($"[office] {from.Name} emails the scammers: {text}");
        OfficeLine(at, 0, $"[EMAIL] To: {desk.Scams[scam].From}. {text}", now, from);
        OfficeLine(at, 0, desk.Sent, now, from);
        var reply = desk.ReplyTo(text);
        if (reply >= 0 && _scamMailCount < MaxMail)
            _scamMail[_scamMailCount++] = new ScamMail
            {
                Body = pc.Id, Part = pi, Scam = scam, Reply = reply,
                Due = now + desk.ReplyAfter((uint)text.Length * 2654435761u ^ (uint)ScamMessages),
            };
        return true;
    }

    /// <summary>The story hears that the computer's owner fell for it: a Hooked event against the faction behind the desk (a valued customer now).</summary>
    private void StoryHooked(Construct c, ScamDesk desk)
    {
        if (Story is not { } s || s.Rules.VerbId("Hooked") is not (var v and >= 0) || s.Factions.Find(desk.From) is not (var f and >= 0) || StoryByte(c.Owner) is not (var who and >= 0))
            return;
        s.Record(v, who, s.Factions.EntityOf(f), place: StoryPlace());
    }

    /// <summary>An answer arrives, if the computer is still hooked by the same scam. One that takes: the battery pays, and the scammers are gone.</summary>
    private void ScamAnswer(in ScamMail mail, ScamDesk desk, double now)
    {
        if (Constructs.Get(mail.Body) is not { } c || mail.Part >= c.PartCount || c.Parts[mail.Part].D.Office != OfficeKind.Computer
            || c.Parts[mail.Part].State != mail.Scam + 1 || mail.Reply >= desk.Replies.Length)
            return;
        var at = OfficePartAt(c, mail.Part);
        var reply = desk.Replies[mail.Reply];
        ScamLines(at, desk.Scams[mail.Scam], reply.Lines, now);
        if (reply.Take <= 0)
            return;
        ScamsFallenFor++;
        StoryHooked(c, desk);
        ColonyPcPaid(c, mail.Part);
        var left = c.Charge * (1 - reply.Take); // (UseCharge settles later, so the share is worked out here)
        if (c.ChargeCapacity > 0)
            c.UseCharge(c.Charge * reply.Take);
        var percent = c.ChargeCapacity > 0 ? (int)MathF.Round(Math.Clamp(left / c.ChargeCapacity, 0, 1) * 100) : 0;
        Console.WriteLine($"[office] the scammers took {reply.Take:P0} of a battery: {percent}% left");
        ref var p = ref c.Parts[mail.Part];
        p.State = 0;
        p.Rounds = p.Count = 0;
        OfficeLine(at, ScamHearing, OfficeParts.Pc + desk.Robbed.Replace("{percent}", percent.ToString()), now, null);
    }
}
