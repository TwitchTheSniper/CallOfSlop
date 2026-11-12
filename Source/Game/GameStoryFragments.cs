using System.Numerics;
using Brutal.RakNetApi;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The story's fragments and the sealed data centres (docs/story/FRAGMENTS.md; data Story/Fragments/Fragments.json, read by
/// <see cref="FragmentScript"/>). A fragment is a record a player has found: the story world holds it (a Thing named by the
/// fragment's id, the relation <c>Fragment</c> from the player towards it, its value the source's entity plus one, and a Recover
/// event; the value is -1 with no source), so it is saved with the story and storylets can ask for it. Found in any order.
/// <para>
/// A site (a data centre) is a blueprint the host stands on level ground near the penal colony with the rest of the colony's
/// buildings. Its Data Centre Terminal (a logic part, kind <c>cipher</c>) is dead until the player fits a battery to the building's
/// frame; powered, it puts out a carrier (1 to 9, a new one every few seconds) and wants the site's sum of it back on its key input,
/// for a few carriers in a row: a Math Block or two, built and wired by the player. Decoded (the part's State bit 0, saved with the
/// world), any player who stands at the powered terminal copies its fragment. Coming near a site gives a Lead on it.
/// </para>
/// Host only; each player hears what they hold in <see cref="Msg.Fragments"/>, which their Logbook (F1) shows.
/// </summary>
public sealed partial class Game
{
    /// <summary>How near the terminal a player stands to copy its record, and how near the site to find it (m).</summary>
    public const float FragmentCopyReach = 3f, FragmentArriveReach = 30f;

    private const byte TerminalDecoded = 1; // (CPart.State)

    private readonly int[] _fgTerm = new int[FragmentScript.MaxSites]; // server: each site's terminal part (-1: look again)
    private readonly double[] _fgSlotAt = new double[FragmentScript.MaxSites]; // when the next carrier comes
    private readonly int[] _fgSlot = new int[FragmentScript.MaxSites], _fgMatched = new int[FragmentScript.MaxSites], _fgLocks = new int[FragmentScript.MaxSites];
    private readonly bool[] _fgPowered = new bool[FragmentScript.MaxSites];
    private readonly Player?[] _fgSentTo = new Player?[256]; // who each player slot's log was last sent to, and its hash
    private readonly uint[] _fgSentHash = new uint[256];
    private double _fgNextLook, _fgNextPlayers;
    private readonly Random _fgRng = new(0x5eed);
    private int _fgRelFragment = -2, _fgRelLead = -2, _vRecover = -1, _vDecode = -1;
    private StoryEngine? _fgFor;

    // ---- Every peer: this player's own log, as the server tells it ----

    /// <summary>The fragments the local player holds (an index into <see cref="FragmentScript.Fragments"/>, and where it came from).</summary>
    public readonly List<(int Fragment, string Source)> FragmentsHeld = new(FragmentScript.MaxFragments);

    /// <summary>The local player's leads (things they learnt of), and whether what the lead points to has been found.</summary>
    public readonly List<(string Name, bool Found)> FragmentLeads = new(16);

    /// <summary>Both sides, a new match (from <see cref="OpeningReset"/>): no sites, nothing held.</summary>
    private void FragmentsReset()
    {
        Array.Clear(FragmentScript.SiteBodies);
        Array.Fill(_fgTerm, -1);
        Array.Clear(_fgSlot);
        Array.Clear(_fgMatched);
        Array.Clear(_fgLocks);
        Array.Clear(_fgPowered);
        Array.Clear(_fgSentTo);
        _fgHere = false;
        FragmentsHeld.Clear();
        FragmentLeads.Clear();
        StoryPathsReset(); // (the Logbook's other page: GameStoryPaths.cs)
        _fgFor = null;
    }

    // ---- The sites ----------------------------------------------------------------------------------------------------

    /// <summary>Server, with the colony (a fresh story world, or a world from before the sites): every site not standing yet.</summary>
    private void FragmentSitesBuild()
    {
        var script = FragmentScript.Current;
        if (script.Problem.Length > 0)
        {
            Log($"[fragments] {script.Problem}: no data centres");
            return;
        }
        if (_opColony == 0)
            return;
        for (var k = 0; k < script.Sites.Length; k++)
        {
            var site = script.Sites[k];
            if (site.Map.Length > 0 || Constructs.Get(FragmentScript.SiteBodies[k]) != null)
                continue; // (a site on another world stands when the group first comes there: FragmentSitesHere)
            if (SandboxKitPiece(site.Blueprint, _opMiddle, site.From, site.To, level: 1.2f) is not { } c)
            {
                Log($"[fragments] no level ground for the {site.Name} ({site.Blueprint}) {site.From:0} to {site.To:0} m from the colony");
                continue;
            }
            FragmentScript.SiteBodies[k] = c.Id;
            _fgTerm[k] = -1;
            Constructs.Events.Add($"fragments: the {site.Name} as construct {c.Id}");
            Log($"[fragments] the {site.Name} is construct {c.Id} at {c.WorldCom.X:0},{c.WorldCom.Z:0}, {Vector3.Distance(c.WorldCom, _opMiddle):0} m from the colony");
        }
    }

    private bool _fgHere;

    /// <summary>How near a world's site a building with a terminal counts as that site's (m).</summary>
    public const float FragmentSiteReach = 90f;

    /// <summary>How far east of a world's survey post its site's building stands (m).</summary>
    public const float FragmentSiteAside = 14f;

    /// <summary>
    /// Server, once a match when the world is in: the sites of the planetoid being played (a site's <c>"map"</c>,
    /// docs/planets/WORLDS.md). A site's building is found again by where it stands (a construct with a terminal within
    /// <see cref="FragmentSiteReach"/> of the site: nothing of it is saved, so it survives landings and leaving); the first
    /// time the story comes to this world it is built, upright on the ground there, and the story notes the site (its
    /// Thing), so one taken apart since isn't built again.
    /// </summary>
    private void FragmentSitesHere(StoryEngine s)
    {
        var script = FragmentScript.Current;
        if (script.Problem.Length > 0 || World.Map.Planetoid is not { } pl)
            return;
        // The sites on this world first, then the colony's own (the story's home world can hold both, as Glacier does).
        for (var k = 0; k < script.Sites.Length; k++)
        {
            var site = script.Sites[k];
            if (site.Map.Length == 0)
                continue;
            if (!site.Map.Equals(World.Map.Name, StringComparison.OrdinalIgnoreCase))
                continue;
            double lat = site.Lat * Math.PI / 180, lon = site.Lon * Math.PI / 180;
            var dir = new Vector3((float)(Math.Cos(lat) * Math.Sin(lon)), (float)Math.Sin(lat), (float)(Math.Cos(lat) * Math.Cos(lon)));
            var hollow = site.Inside && pl.Hollow;
            var ground = pl.GroundUnder(pl.Center + dir * (hollow ? (float)pl.InnerRadius - 2f : (float)(pl.Radius + pl.MaxHeight + 50)));
            if (hollow)
                ground -= dir * 0.05f; // (just clear of the wall, where up is to the centre)
            else
            {
                // Beside the site's survey post, not on it (east of it; the level ground is wider than this).
                var east = Vector3.Cross(Vector3.UnitY, dir);
                if (east.LengthSquared() > 1e-6f)
                    ground = pl.GroundUnder(ground + Vector3.Normalize(east) * FragmentSiteAside + dir * 50f);
            }
            Construct? found = null;
            foreach (var c in Constructs.Bodies)
                if (Vector3.DistanceSquared(c.WorldCom, ground) < FragmentSiteReach * FragmentSiteReach && FragmentTerminal(k, c) >= 0)
                    found = c;
            _fgTerm[k] = -1;
            var key = StoryNameKey("site:" + site.Id);
            if (found == null && s.World.Find(StoryKind.Thing, key) < 0 && ConstructBuild.FindBlueprint(site.Blueprint) is { } bp)
            {
                found = ConstructBuild.BuildOnGround(Constructs, bp, ground, 0, out var why);
                if (found == null)
                {
                    Log($"[fragments] the {site.Name} ({site.Blueprint}) wasn't built on {World.Map.Name}: {why}");
                    continue;
                }
                if (!found.Anchored)
                {
                    found.Frozen = true;
                    found.Rebuild();
                }
                FragmentSiteThing(s, site);
                Constructs.Events.Add($"fragments: the {site.Name} as construct {found.Id}");
                Log($"[fragments] the {site.Name} is construct {found.Id} on {World.Map.Name} at latitude {site.Lat:0.#}, longitude {site.Lon:0.#}{(hollow ? ", inside" : "")}");
            }
            FragmentScript.SiteBodies[k] = found?.Id ?? 0;
        }
        for (var k = 0; k < script.Sites.Length; k++)
        {
            // The colony's own site, back from a trip (the landing brings the colony's builds back, not which is which):
            // a building with a terminal that isn't another site's.
            if (script.Sites[k].Map.Length > 0 || Constructs.Get(FragmentScript.SiteBodies[k]) != null)
                continue;
            foreach (var c in Constructs.Bodies)
                if (c.Anchored | c.Frozen && !FragmentSiteTaken(script, c.Id) && FragmentTerminal(k, c) >= 0)
                {
                    FragmentScript.SiteBodies[k] = c.Id;
                    break;
                }
        }
    }

    /// <summary>Is this construct already a site's building?</summary>
    private static bool FragmentSiteTaken(FragmentScript script, ushort id)
    {
        for (var j = 0; j < script.Sites.Length; j++)
            if (FragmentScript.SiteBodies[j] == id)
                return true;
        return false;
    }

    /// <summary>Server, a save: each site's id and construct.</summary>
    private (string Site, ushort Body)[] FragmentSitesCapture()
    {
        var sites = FragmentScript.Current.Sites;
        var n = 0;
        for (var k = 0; k < sites.Length; k++)
            if (FragmentScript.SiteBodies[k] != 0 && sites[k].Map.Length == 0)
                n++;
        var o = new (string, ushort)[n];
        n = 0;
        for (var k = 0; k < sites.Length; k++)
            if (FragmentScript.SiteBodies[k] != 0 && sites[k].Map.Length == 0)
                o[n++] = (sites[k].Id, FragmentScript.SiteBodies[k]);
        return o;
    }

    /// <summary>Server, a world loading (its constructs in): the sites it had, by id; any it lacks are built.</summary>
    private void FragmentSitesLoad((string Site, ushort Body)[] saved)
    {
        var sites = FragmentScript.Current.Sites;
        foreach (var (id, body) in saved)
            for (var k = 0; k < sites.Length; k++)
                if (sites[k].Id == id && sites[k].Map.Length == 0 && Constructs.Get(body) != null)
                    FragmentScript.SiteBodies[k] = body;
        if (_opColony != 0)
            FragmentSitesBuild();
    }

    /// <summary>A site's terminal (the first cipher part on its construct), or -1.</summary>
    private int FragmentTerminal(int k, Construct c)
    {
        var t = _fgTerm[k];
        if (t >= 0 && t < c.PartCount && c.Parts[t].D.Logic == CLogic.Cipher)
            return t;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D.Logic == CLogic.Cipher)
                return _fgTerm[k] = i;
        return -1;
    }

    // ---- The host's tick ----------------------------------------------------------------------------------------------

    /// <summary>Server, from <see cref="StoryTick"/>: the terminals four times a second, the players once a second.</summary>
    private void FragmentsTick(StoryEngine s, double now)
    {
        if (now < _fgNextLook)
            return;
        _fgNextLook = now + 0.25;
        var script = FragmentScript.Current;
        if (script.Sites.Length == 0 && script.Fragments.Length == 0)
            return;
        if (!ReferenceEquals(_fgFor, s))
        {
            _fgFor = s;
            _fgRelFragment = s.Rules.Relation("Fragment")?.Id ?? -1;
            _fgRelLead = s.Rules.Relation("Lead")?.Id ?? -1;
            _vRecover = s.Rules.VerbId("Recover");
            _vDecode = s.Rules.VerbId("Decode");
        }
        if (!_fgHere && Phase == MatchPhase.Playing && _landing == null && WorldSession is null or { Applied: true })
        {
            _fgHere = true;
            FragmentSitesHere(s); // (its own method: the search and the build are once a match)
            SettlementsHere(s); // (the factions' homes on this world: GameStorySettlements.cs)
        }
        for (var k = 0; k < script.Sites.Length; k++)
            FragmentTerminalTick(s, k, script.Sites[k], now);
        if (now < _fgNextPlayers)
            return;
        _fgNextPlayers = now + 1;
        foreach (var p in Players.Values)
        {
            if (p.Bot != null || p.Id >= MaxPlayers)
                continue;
            var who = StoryWho(p);
            if (who < 0)
                continue;
            for (var k = 0; k < script.Sites.Length; k++)
                FragmentSiteNear(s, p, who, k, script.Sites[k]);
            FragmentSendIfChanged(s, p, who);
        }
    }

    /// <summary>One site's terminal: power, the carrier, the key heard, the locks, decoded.</summary>
    private void FragmentTerminalTick(StoryEngine s, int k, FragmentScript.Site site, double now)
    {
        if (Constructs.Get(FragmentScript.SiteBodies[k]) is not { } c || FragmentTerminal(k, c) is not (>= 0 and var t))
            return;
        ref var p = ref c.Parts[t];
        var on = !p.Broken && c.Electrical && ConstructPower.On(c, t);
        var decoded = (p.State & TerminalDecoded) != 0;
        if (on && !_fgPowered[k] && !decoded)
            FragmentSayNear(c.WorldCom, site.Power);
        _fgPowered[k] = on;
        if (!on || decoded)
        {
            p.Mem = 0;
            _fgLocks[k] = 0;
            _fgSlotAt[k] = 0;
            p.Reading = FragmentScript.Reading(decoded ? FragmentScript.DecodedBand : 0, 0);
            return;
        }
        if (now >= _fgSlotAt[k])
        {
            if (_fgSlot[k] > 0 && _fgMatched[k] != _fgSlot[k])
                _fgLocks[k] = 0; // (a carrier went by unanswered: start again)
            var carrier = p.Mem;
            while (carrier == p.Mem)
                carrier = _fgRng.Next(1, 10);
            p.Mem = carrier;
            _fgSlot[k]++;
            _fgSlotAt[k] = now + site.Every;
        }
        var key = p.InCount > 0 ? p.In : 0;
        var settled = now >= _fgSlotAt[k] - site.Every + 0.3; // (the carrier has been out long enough to come back through a few blocks)
        if (settled && _fgMatched[k] != _fgSlot[k] && p.InCount > 0 && MathF.Abs(key - site.Answer(p.Mem)) < 0.05f)
        {
            _fgMatched[k] = _fgSlot[k];
            if (++_fgLocks[k] >= site.Locks)
            {
                p.State |= TerminalDecoded;
                p.Mem = 0;
                var by = FragmentNearest(c.WorldCom, 40);
                if (by != null && StoryWho(by) is var who and >= 0 && _vDecode >= 0)
                    s.Record(_vDecode, who, target: FragmentSiteThing(s, site), place: StoryPlace());
                FragmentSayNear(c.WorldCom, site.Decoded);
                Log($"[fragments] the {site.Name}'s terminal is decoded{(by != null ? $" by {by.Name}" : "")}");
                p.Reading = FragmentScript.Reading(FragmentScript.DecodedBand, 0);
                return;
            }
        }
        p.Reading = FragmentScript.Reading(_fgLocks[k], key);
    }

    /// <summary>A player and a site, once a second: near it, a lead on it; at its decoded, powered terminal, its fragment.</summary>
    private void FragmentSiteNear(StoryEngine s, Player p, int who, int k, FragmentScript.Site site)
    {
        if (Constructs.Get(FragmentScript.SiteBodies[k]) is not { } c)
            return;
        if (Vector3.DistanceSquared(p.Position, c.WorldCom) > FragmentArriveReach * FragmentArriveReach)
            return;
        var thing = FragmentSiteThing(s, site);
        if (_fgRelLead >= 0 && s.World.Get(who, _fgRelLead, thing) == 0)
        {
            s.World.Set(who, _fgRelLead, thing, 1);
            if (site.Arrive.Length > 0)
                SandboxHands.Notices.Add((p.Id, site.Arrive));
        }
        if (FragmentTerminal(k, c) is not (>= 0 and var t))
            return;
        ref var part = ref c.Parts[t];
        if ((part.State & TerminalDecoded) == 0 || part.Broken || !c.Electrical || !ConstructPower.On(c, t))
            return;
        if (Vector3.DistanceSquared(p.Position, c.SegToWorld(part.Seg, part.Pos)) > FragmentCopyReach * FragmentCopyReach)
            return;
        if (FragmentGive(p, site.Fragment, thing) && site.Copied.Length > 0)
            SandboxHands.Notices.Add((p.Id, site.Copied));
    }

    /// <summary>A site's story Thing (its name: "the Sealed Data Centre" in a chronicle line), made the first time.</summary>
    private static int FragmentSiteThing(StoryEngine s, FragmentScript.Site site) =>
        s.World.Get(StoryKind.Thing, StoryNameKey("site:" + site.Id), site.Name, s.Time);

    /// <summary>
    /// Server: gives a player a fragment (an index into <see cref="FragmentScript.Fragments"/>) from <paramref name="source"/> (an
    /// entity: a site, a person; -1 nobody). Any fragment, in any order; false when they hold it already or there is no story.
    /// </summary>
    public bool FragmentGive(Player p, int fragment, int source)
    {
        if (Story is not { } s || (uint)fragment >= (uint)FragmentScript.Current.Fragments.Length || StoryWho(p) is not (>= 0 and var who))
            return false;
        if (_fgRelFragment == -2 || !ReferenceEquals(_fgFor, s))
        {
            _fgFor = s;
            _fgRelFragment = s.Rules.Relation("Fragment")?.Id ?? -1;
            _fgRelLead = s.Rules.Relation("Lead")?.Id ?? -1;
            _vRecover = s.Rules.VerbId("Recover");
            _vDecode = s.Rules.VerbId("Decode");
        }
        if (_fgRelFragment < 0)
            return false;
        var id = FragmentScript.Current.Fragments[fragment].Id;
        var thing = s.World.Find(id) is var f and >= 0 ? f : s.World.Get(StoryKind.Thing, StoryNameKey("fragment:" + id), id, s.Time);
        if (s.World.Get(who, _fgRelFragment, thing) != 0)
            return false;
        if (_vRecover >= 0)
            s.Record(_vRecover, who, target: source, place: StoryPlace(), item: thing);
        s.World.Set(who, _fgRelFragment, thing, s.World.Valid(source) ? source + 1 : -1); // (never 0: that is no pair)
        Log($"[fragments] {p.Name} recovered {id} from {s.World.NameOf(source)}");
        FragmentSendIfChanged(s, p, who);
        return true;
    }

    private Player? FragmentNearest(Vector3 at, float within)
    {
        Player? best = null;
        var bd = within * within;
        foreach (var p in Players.Values)
            if (p.Bot == null && Vector3.DistanceSquared(p.Position, at) is var d && d < bd)
                (best, bd) = (p, d);
        return best;
    }

    private void FragmentSayNear(Vector3 at, string text)
    {
        if (text.Length == 0)
            return;
        foreach (var p in Players.Values)
            if (p.Bot == null && Vector3.DistanceSquared(p.Position, at) < FragmentArriveReach * FragmentArriveReach)
                SandboxHands.Notices.Add((p.Id, text));
    }

    // ---- The log to each player (Msg.Fragments) ----------------------------------------------------------------------

    /// <summary>
    /// Server: what a player holds and their leads, when it changed since they last heard it. Msg.Fragments: u8 sites, u16 each
    /// construct; u8 held, (u8 fragment, str source) each; u8 leads, (str name, u8 found) each.
    /// </summary>
    private void FragmentSendIfChanged(StoryEngine s, Player p, int who)
    {
        var script = FragmentScript.Current;
        var w = SbW;
        w.Begin(Msg.Fragments);
        w.U8((byte)script.Sites.Length);
        for (var k = 0; k < script.Sites.Length; k++)
            w.U16(FragmentScript.SiteBodies[k]);
        var heldAt = w.Length;
        w.U8(0);
        int held = 0, leads = 0;
        if (_fgRelFragment >= 0)
        {
            var (a, b) = s.World.Range(who, _fgRelFragment);
            var e = s.World[who];
            for (var i = a; i < b && held < FragmentScript.MaxFragments; i++)
            {
                var f = script.FromThing(s.World.NameOf(e.Pairs[i].Target));
                if (f < 0)
                    continue;
                w.U8((byte)f).Str(e.Pairs[i].Value > 0 ? s.World.NameOf(e.Pairs[i].Value - 1) : "");
                held++;
            }
        }
        w.PatchU8(heldAt, (byte)held);
        var leadsAt = w.Length;
        w.U8(0);
        if (_fgRelLead >= 0)
        {
            var (a, b) = s.World.Range(who, _fgRelLead);
            var e = s.World[who];
            for (var i = a; i < b && leads < 32; i++)
            {
                var name = s.World.NameOf(e.Pairs[i].Target);
                w.Str(name).U8(FragmentLeadFound(s, who, name) ? (byte)1 : (byte)0);
                leads++;
            }
        }
        w.PatchU8(leadsAt, (byte)leads);
        var hash = 2166136261u;
        foreach (var x in w.Span)
            hash = (hash ^ x) * 16777619u;
        if (_fgSentTo[p.Id] == p && _fgSentHash[p.Id] == hash)
            return;
        _fgSentTo[p.Id] = p;
        _fgSentHash[p.Id] = hash;
        if (p.Id == LocalId)
        {
            var r = new PacketReader(w.Span);
            r.U8(); // (the message id)
            ReadFragments(ref r);
            return;
        }
        if (GuidOf(p) is { } guid)
            Net.SendTo(guid, w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>A lead is found when a site it names (by its name or its "lead") gave this player its fragment.</summary>
    private bool FragmentLeadFound(StoryEngine s, int who, string name)
    {
        foreach (var site in FragmentScript.Current.Sites)
            if (site.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || site.Lead.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                var id = FragmentScript.Current.Fragments[site.Fragment].Id;
                if (s.World.Find(id) is var f and >= 0 && s.World.Get(who, _fgRelFragment, f) != 0)
                    return true;
            }
        return false;
    }

    /// <summary>Client (and the host's own): a <see cref="Msg.Fragments"/> (after its id).</summary>
    private void ReadFragments(ref PacketReader r)
    {
        var script = FragmentScript.Current;
        if (!r.Has(1))
            return;
        var n = r.U8();
        for (var k = 0; k < n && r.Has(2); k++)
        {
            var body = r.U16();
            if (k < FragmentScript.MaxSites && !IsServer)
                FragmentScript.SiteBodies[k] = body;
        }
        FragmentsHeld.Clear();
        var held = r.Has(1) ? r.U8() : 0;
        for (var k = 0; k < held && r.Has(2); k++)
        {
            var f = r.U8();
            var source = r.Text(120);
            if (f < script.Fragments.Length)
                FragmentsHeld.Add((f, source));
        }
        FragmentLeads.Clear();
        var leads = r.Has(1) ? r.U8() : 0;
        for (var k = 0; k < leads && r.Has(2); k++)
        {
            var name = r.Text(120);
            var found = r.Has(1) && r.U8() != 0;
            FragmentLeads.Add((name, found));
        }
    }
}
