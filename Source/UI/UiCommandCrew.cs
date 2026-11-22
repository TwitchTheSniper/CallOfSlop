using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// The command view's crews (Karl, 2026-11-18: "visualising crews etc for the ships on the RTS view"): who holds each craft and
/// how it stands with you, its group and admiral, its captain and how many are alive, under its name on the map; a dashed line from
/// each group ship to its flagship; the crew as dots on a craft's hologram (people green, crew in their power's standing colour);
/// and on the card the faction, the command and the crew, member by member for a crew of yours or a prize. A layer the CREWS
/// tickbox turns on and off (on by default). Read from what every peer already has (Game.CrewInfo, Game.CrewStandIns,
/// Game.CrewRosters, the players): nothing new on the wire.
/// </summary>
public sealed partial class Ui
{
    public bool CommandCrewsOn = true;

    /// <summary>How a craft's holders stand with you, as a colour: red below 0 (they fight you), amber wary, cyan neutral, green friendly or yours.</summary>
    private static Vector3 CmdStandColour(int v) => v < 0 ? CmdRed : v < 20 ? CmdAmber : v < 60 ? CmdCyan : CmdGreen;

    /// <summary>Power <paramref name="power"/>'s standing with the local player (-100 to 100), as the faction window reads it.</summary>
    private static int CmdStanding(Game g, byte power)
    {
        if (power == 0 || power > FactionTable.MaxStory)
            return 0;
        var t = g.FactionTable;
        var me = g.LocalId;
        if (me == 255)
            return 0;
        var party = t.Of[me];
        return party != 0 ? t.StoryStanding[party * FactionTable.MaxStory + power - 1] : t.PlayerStanding[me * FactionTable.MaxStory + power - 1];
    }

    /// <summary>A crew of the local player's (or their faction's) on this body: its roster is known.</summary>
    private static bool CmdCrewMine(Game g, ushort body, out Game.CrewRosterView? roster)
    {
        roster = g.CrewRosters.GetValueOrDefault(body);
        return roster != null && g.Local is { } me && Factions.Shares(roster.Owner, me.Id) && (roster.Own || roster.Prize);
    }

    private readonly Dictionary<ushort, (long Key, string Text, Vector3 Colour)> _cmdCrewTags = new(64);

    /// <summary>A craft's crew line under its name on the map ("MILITIA G3    CAPTAIN BEA    CREW 5"), made only when it changes; null: no crew.</summary>
    private string? CmdCrewTag(Game g, ushort body, out Vector3 colour)
    {
        colour = CmdCyan;
        if (!g.CrewInfo.TryGetValue(body, out var i) || i.Alive == 0 && !g.CrewRosters.ContainsKey(body))
            return null;
        var mine = CmdCrewMine(g, body, out var roster);
        var stand = CmdStanding(g, i.Faction);
        var band = stand < 0 ? 0 : stand < 20 ? 1 : stand < 60 ? 2 : 3;
        var key = i.Faction | (long)i.Alive << 8 | (long)i.Group << 16 | (long)(i.Admiral != 0xFFFF ? 1 : 0) << 32 | (long)(mine ? 1 : 0) << 33
                  | (long)(roster is { Prize: true } ? 1 : 0) << 34 | (long)band << 35 | (long)i.Captain << 40;
        if (!_cmdCrewTags.TryGetValue(body, out var tag) || tag.Key != key)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string text;
            if (mine && roster!.Prize)
                text = string.Create(inv, $"PRIZE    {roster.Members.Count} PRISONER{(roster.Members.Count == 1 ? "" : "S")}");
            else if (mine)
                text = string.Create(inv, $"YOUR CREW    {i.Alive} ABOARD");
            else
            {
                var power = CrewPowers.Of(i.Faction)?.Name.ToUpperInvariant() ?? "CREWED";
                text = string.Create(inv, $"{(i.Admiral != 0xFFFF ? "FLAGSHIP  " : "")}{power}{(i.Group != 0 ? $" G{i.Group}" : "")}  {(i.Captain != 0xFFFF ? $"CAPT {CrewNames.Of(i.Captain).ToUpperInvariant()}  " : "")}CREW {i.Alive}");
            }
            _cmdCrewTags[body] = tag = (key, text, mine ? CmdGreen : CmdStandColour(stand));
        }
        colour = tag.Colour;
        return tag.Text;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Groups: a dashed line from each ship of a group to its flagship
    // ---------------------------------------------------------------------------------------------------------------

    private readonly int[] _cmdFlagship = new int[128];

    private void CmdFleet(ImDrawListPtr dl, Game g, float fade, double now)
    {
        if (!CommandCrewsOn || g.CrewInfo.Count == 0)
            return;
        var flags = 0;
        for (var k = 0; k < _cmdCraftCount && flags < _cmdFlagship.Length; k++)
            if (g.CrewInfo.TryGetValue(_cmdCraft[k].Id, out var i) && i.Admiral != 0xFFFF && i.Group != 0 && i.Alive > 0)
                _cmdFlagship[flags++] = k;
        for (var f = 0; f < flags; f++)
        {
            ref var fc = ref _cmdCraft[_cmdFlagship[f]];
            var fi = g.CrewInfo[fc.Id];
            var colour = CmdStandColour(CmdStanding(g, fi.Faction));
            // The flagship's mark: a chevron over it.
            if (CmdOn(fc.At))
            {
                var top = fc.At + new float2(0, -MathF.Max(13 * _scale, fc.Px * 1.08f) - 10 * _scale);
                var d = 6 * _scale;
                CmdGlow(dl, top + new float2(-d, -d * 0.6f), top, colour, fade);
                CmdGlow(dl, top, top + new float2(d, -d * 0.6f), colour, fade);
                CmdGlow(dl, top + new float2(-d, -d * 1.4f), top + new float2(0, -d * 0.8f), colour, 0.7f * fade);
                CmdGlow(dl, top + new float2(0, -d * 0.8f), top + new float2(d, -d * 1.4f), colour, 0.7f * fade);
            }
            for (var k = 0; k < _cmdCraftCount; k++)
            {
                ref var cr = ref _cmdCraft[k];
                if (cr.Id == fc.Id || !g.CrewInfo.TryGetValue(cr.Id, out var i) || i.Faction != fi.Faction || i.Group != fi.Group || i.Alive == 0)
                    continue;
                var gap = fc.At - cr.At;
                var len = gap.Length();
                if (len < 24 * _scale || len > 20000 || !CmdOn(cr.At, 400) && !CmdOn(fc.At, 400))
                    continue;
                var u = gap / len;
                var step = 10 * _scale;
                for (var t = 10 * _scale + (float)(now * 12 % step); t + step * 0.4f < len - 10 * _scale; t += step)
                    CmdLine(dl, cr.At + u * t, cr.At + u * (t + step * 0.4f), colour, 0.35f * fade);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The hologram: where its crew are
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>On a craft's hologram: a dot at each seat a crew member works with no body (the stand-ins), and one at each person or crew body aboard.</summary>
    private void CmdHoloCrew(ImDrawListPtr dl, Game g, Construct c, float2 at, ReadOnlySpan<float2> ax, Vector3 com, float fade, double now)
    {
        if (!CommandCrewsOn)
            return;
        var info = g.CrewInfo.TryGetValue(c.Id, out var ci);
        var crewColour = info ? CmdCrewMine(g, c.Id, out _) ? CmdGreen : CmdStandColour(CmdStanding(g, ci.Faction)) : CmdCyan;
        var pulse = 0.75f + 0.25f * MathF.Sin((float)now * 4f);
        if (g.CrewStandIns.TryGetValue(c.Id, out var seats))
            foreach (var part in seats)
            {
                if (part >= c.PartCount)
                    continue;
                var p = c.Parts[part].Pos - com;
                var pa = at + ax[0] * p.X + ax[1] * p.Y + ax[2] * p.Z;
                ImDrawListExtensions.AddCircleFilled(dl, in pa, 2.6f * _scale, Hud.Colour(crewColour, fade * pulse), 8);
                CmdRing(dl, pa, 4.5f * _scale, crewColour, 0.45f * fade);
            }
        if (c.SpaceAway != 0)
            return; // (players are in the bubble's frame; this ship isn't)
        var reach = c.Radius * c.Radius * 0.6f;
        var back = Quaternion.Conjugate(c.Rot);
        foreach (var (id, p) in g.Players)
        {
            if (!p.Alive)
                continue;
            ref var hand = ref g.SandboxHands.Hand(id);
            if (!(hand.Seated && hand.SeatBody == c.Id) && Vector3.DistanceSquared(p.Position, c.WorldCom) >= reach)
                continue;
            var local = Vector3.Transform(p.Position - c.WorldCom, back);
            var pa = at + ax[0] * local.X + ax[1] * local.Y + ax[2] * local.Z;
            var person = !g.PlayerIsBot(id);
            var colour = person ? CmdGreen : crewColour;
            ImDrawListExtensions.AddCircleFilled(dl, in pa, (person ? 3.2f : 2.6f) * _scale, Hud.Colour(colour, fade), 8);
            if (person)
                CmdRing(dl, pa, 6 * _scale, CmdGreen, 0.6f * fade);
            if (c.Driver == id)
                CmdRing(dl, pa, 8.5f * _scale, colour, 0.5f * fade, 1f, 3, 0.5f, (float)(now * 1.5));
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The card's crew rows
    // ---------------------------------------------------------------------------------------------------------------

    private struct CmdCrewRow
    {
        public string Job, Name, State;
        public float Hp;
        public Vector3 Colour;
    }

    private const int CmdCrewRowsMax = 8;
    private readonly CmdCrewRow[] _cmdCrewRows = new CmdCrewRow[CmdCrewRowsMax];
    private int _cmdCrewRowN, _cmdCrewMore;
    private string _cmdCrewFaction = "", _cmdCrewCommand = "", _cmdCrewPeople = "", _cmdCrewNote = "";
    private Vector3 _cmdCrewColour;

    private static string CmdJobWord(CrewJob j) => j switch
    {
        CrewJob.Pilot => "PILOT",
        CrewJob.Gunner => "GUNNER",
        CrewJob.Engineer => "ENGINEER",
        CrewJob.Loader => "LOADER",
        CrewJob.Spotter => "SPOTTER",
        CrewJob.Crane => "CRANE HAND",
        CrewJob.Hauler => "HAULER",
        CrewJob.Builder => "BUILDER",
        CrewJob.Admiral => "ADMIRAL",
        CrewJob.Interrogator => "QUESTIONER",
        CrewJob.Miner => "MINER",
        CrewJob.Officer => "OFFICER",
        CrewJob.Navigator => "NAVIGATOR",
        CrewJob.Sensors => "SENSORS",
        CrewJob.DamageControl => "DAMAGE CONTROL",
        CrewJob.Signals => "SIGNALS",
        _ => "SPARE",
    };

    private static string CmdStateWord(CrewState s, CrewJob j, bool prize, bool officer) => s switch
    {
        CrewState.Station => j switch { CrewJob.Pilot => "AT THE HELM", CrewJob.Gunner => "ON A GUN", CrewJob.Spare => "RESTING", _ => "AT STATION" },
        CrewState.Walking => "MOVING",
        CrewState.Fighting => "FIGHTING",
        CrewState.Surrendered => prize ? officer ? "PRISONER, OFFICER" : "PRISONER" : "SURRENDERED",
        CrewState.Scuttling => "SCUTTLING",
        CrewState.Dead => "DEAD",
        _ => "OFF DUTY",
    };

    /// <summary>The card's crew: who holds it, its command, the people aboard and (a crew of yours, a prize) its members. Four times a second.</summary>
    private void CmdCardCrew(Game g, Construct c)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        _cmdCrewRowN = _cmdCrewMore = 0;
        _cmdCrewNote = "";
        var info = g.CrewInfo.TryGetValue(c.Id, out var ci);
        var mine = CmdCrewMine(g, c.Id, out var roster);

        // Who holds it.
        if (mine && roster!.Prize)
        {
            _cmdCrewFaction = "PRIZE: YOURS, TAKEN" + (info && CrewPowers.Of(ci.Faction) is { } was ? " FROM " + was.Name.ToUpperInvariant() : "");
            _cmdCrewColour = CmdGreen;
        }
        else if (mine)
        {
            _cmdCrewFaction = "YOUR CREW" + (roster!.HoldFire ? "    HOLDING FIRE" : "");
            _cmdCrewColour = CmdGreen;
        }
        else if (info && CrewPowers.Of(ci.Faction) is { } def)
        {
            var v = CmdStanding(g, ci.Faction);
            var word = v <= -50 ? "AT WAR WITH YOU" : v < 0 ? "HOSTILE" : v < 20 ? "WARY" : v < 60 ? "NEUTRAL" : "FRIENDLY";
            _cmdCrewFaction = string.Create(inv, $"{def.Name.ToUpperInvariant()}    {word} ({v:+0;-0;0})");
            _cmdCrewColour = CmdStandColour(v);
        }
        else
        {
            _cmdCrewFaction = c.Owner != 255 ? "PLAYER BUILT" : "NOBODY";
            _cmdCrewColour = CmdDim;
        }

        // Its command: its group and admiral, its captain.
        _cmdCrewCommand = "";
        if (info && !mine)
        {
            var admiral = ci.Admiral;
            if (admiral == 0xFFFF && ci.Group != 0)
                foreach (var (_, o) in g.CrewInfo)
                    if (o.Faction == ci.Faction && o.Group == ci.Group && o.Admiral != 0xFFFF)
                    {
                        admiral = o.Admiral;
                        break;
                    }
            var parts = new System.Text.StringBuilder(64);
            if (ci.Group != 0)
                parts.Append(inv, $"GROUP {ci.Group}");
            if (ci.Admiral != 0xFFFF)
                parts.Append(parts.Length > 0 ? ", " : "").Append("ADMIRAL ").Append(CrewNames.Of(ci.Admiral).ToUpperInvariant()).Append("'S FLAGSHIP");
            else if (admiral != 0xFFFF)
                parts.Append(" UNDER ADMIRAL ").Append(CrewNames.Of(admiral).ToUpperInvariant());
            _cmdCrewCommand = parts.ToString();
        }

        // People aboard: in its seats, and standing within its hull.
        var sb = new System.Text.StringBuilder(96);
        var standing = 0;
        foreach (var (id, p) in g.Players)
        {
            if (!p.Alive || g.PlayerIsBot(id))
                continue;
            ref var hand = ref g.SandboxHands.Hand(id);
            if (hand.Seated && hand.SeatBody == c.Id)
            {
                sb.Append(sb.Length > 0 ? ", " : "").Append(p.Name.ToUpperInvariant());
                if (c.Driver == id || p.InShell)
                    sb.Append(" (").Append(c.Driver == id ? "PILOT" : "").Append(c.Driver == id && p.InShell ? ", " : "").Append(p.InShell ? "SHELL" : "").Append(')');
            }
            else if (c.SpaceAway == 0 && Vector3.DistanceSquared(p.Position, c.WorldCom) < c.Radius * c.Radius * 0.6f)
                standing++;
        }
        if (standing > 0)
            sb.Append(sb.Length > 0 ? ", " : "").Append(inv, $"{standing} ON DECK");
        _cmdCrewPeople = sb.Length > 0 ? sb.ToString() : "";

        // The crew: member by member when you hold it, else what can be seen from outside.
        var alive = info ? ci.Alive : 0;
        if (mine)
        {
            var members = roster!.Members;
            for (var n = 0; n < members.Count; n++)
            {
                var m = members[n];
                if (_cmdCrewRowN == CmdCrewRowsMax)
                {
                    _cmdCrewMore = members.Count - n;
                    break;
                }
                ref var row = ref _cmdCrewRows[_cmdCrewRowN++];
                row.Job = m.Officer ? "OFFICER" : CmdJobWord(m.Job);
                row.Name = m.Name.ToUpperInvariant();
                row.Hp = m.Hp / 255f;
                row.State = CmdStateWord(m.State, m.Job, roster.Prize, m.Officer);
                row.Colour = m.State is CrewState.Fighting or CrewState.Scuttling ? CmdRed : m.State == CrewState.Surrendered ? CmdAmber : m.State == CrewState.Station ? CmdCyan : CmdDim;
            }
            if (members.Count == 0)
                _cmdCrewNote = "NOBODY ABOARD";
        }
        else if (alive > 0)
        {
            var seated = g.CrewStandIns.TryGetValue(c.Id, out var seats) ? seats.Length : 0;
            _cmdCrewNote = string.Create(inv, $"{(ci.Captain != 0xFFFF ? $"CAPTAIN {CrewNames.Of(ci.Captain).ToUpperInvariant()}    " : "")}{alive} ALIVE{(seated > 0 ? $", {seated} AT STATIONS" : "")}");
        }
        else if (_cmdCrewPeople.Length == 0)
            _cmdCrewNote = "NOBODY ABOARD";
    }

    /// <summary>How many card rows the crew takes (for the card's height).</summary>
    private int CmdCrewLines() => 1 + (_cmdCrewCommand.Length > 0 ? 1 : 0) + (_cmdCrewPeople.Length > 0 ? 1 : 0) + (_cmdCrewNote.Length > 0 ? 1 : 0)
                                  + _cmdCrewRowN + (_cmdCrewMore > 0 ? 1 : 0);

    private CmdLabel _cmdCrewMoreText;

    /// <summary>The card's crew rows, from <paramref name="x"/> (the head) and <paramref name="col"/> (the text) down from <paramref name="y"/>.</summary>
    private void CmdCardCrewDraw(ImDrawListPtr dl, float x, float col, ref float y, float line, float fade)
    {
        CmdText(dl, new float2(x, y), 10.5f, CmdDim, fade, "FACTION", 0, false);
        CmdText(dl, new float2(x + col, y - 1 * _scale), 12, _cmdCrewColour, fade, _cmdCrewFaction, 0, false);
        y += line;
        if (_cmdCrewCommand.Length > 0)
        {
            CmdText(dl, new float2(x, y), 10.5f, CmdDim, fade, "COMMAND", 0, false);
            CmdText(dl, new float2(x + col, y - 1 * _scale), 12, CmdWhite, fade, _cmdCrewCommand, 0, false);
            y += line;
        }
        var head = "CREW";
        if (_cmdCrewPeople.Length > 0)
        {
            CmdText(dl, new float2(x, y), 10.5f, CmdDim, fade, "PEOPLE", 0, false);
            CmdText(dl, new float2(x + col, y - 1 * _scale), 12, CmdGreen, fade, _cmdCrewPeople, 0, false);
            y += line;
        }
        if (_cmdCrewNote.Length > 0)
        {
            CmdText(dl, new float2(x, y), 10.5f, CmdDim, fade, head, 0, false);
            CmdText(dl, new float2(x + col, y - 1 * _scale), 12, _cmdCrewNote.StartsWith("NOBODY", StringComparison.Ordinal) ? CmdDim : CmdCyan, fade, _cmdCrewNote, 0, false);
            y += line;
            head = "";
        }
        for (var n = 0; n < _cmdCrewRowN; n++)
        {
            ref var row = ref _cmdCrewRows[n];
            CmdText(dl, new float2(x, y), 10.5f, CmdDim, fade, n == 0 ? head : "", 0, false);
            CmdText(dl, new float2(x + col, y), 9.5f, CmdDim, fade, row.Job, 0, false);
            CmdText(dl, new float2(x + col + 70 * _scale, y - 1 * _scale), 12, CmdWhite, fade, row.Name, 0, false);
            // Its health: ten cells.
            var b0 = new float2(x + col + 150 * _scale, y + 2 * _scale);
            var cw = 4.5f * _scale;
            var hc = row.Hp < 0.25f ? CmdRed : row.Hp < 0.6f ? CmdAmber : CmdCyan;
            for (var i = 0; i < 10; i++)
            {
                var c0 = b0 + new float2(i * cw, 0);
                var lit = (i + 0.5f) / 10 <= row.Hp + 0.001f;
                dl.AddRectFilled(c0, c0 + new float2(cw - 1.5f * _scale, 9 * _scale), Hud.Colour(hc, (lit ? 0.9f : 0.14f) * fade));
            }
            CmdText(dl, new float2(b0.X + 10 * cw + 8 * _scale, y), 10, row.Colour, fade, row.State, 0, false);
            y += line;
        }
        if (_cmdCrewMore > 0)
        {
            if (_cmdCrewMoreText.Stale(_cmdCrewMore))
                _cmdCrewMoreText.Text = "AND " + _cmdCrewMore + " MORE (CREW WINDOW)";
            CmdText(dl, new float2(x + col, y), 10.5f, CmdDim, fade, _cmdCrewMoreText.Text!, 0, false);
            y += line;
        }
    }

    /// <summary>The CREWS tickbox, left of MISSILES.</summary>
    private void CmdCrewsButton(ImDrawListPtr dl, float2 q0, float2 size, float fade)
    {
        if (CmdButton(dl, q0, q0 + size, "   CREWS", CmdGreen, fade, CommandCrewsOn))
            CommandCrewsOn = !CommandCrewsOn;
        var box = 10 * _scale;
        var b0 = new float2(q0.X + 9 * _scale, q0.Y + (size.Y - box) * 0.5f);
        dl.AddRect(b0, b0 + new float2(box, box), Hud.Colour(CommandCrewsOn ? CmdWhite : CmdGreen, fade * 0.9f), 0, 0, _scale);
        if (CommandCrewsOn)
        {
            CmdLine(dl, b0 + new float2(box * 0.18f, box * 0.52f), b0 + new float2(box * 0.42f, box * 0.78f), CmdWhite, fade, 1.6f);
            CmdLine(dl, b0 + new float2(box * 0.42f, box * 0.78f), b0 + new float2(box * 0.85f, box * 0.2f), CmdWhite, fade, 1.6f);
        }
    }
}
