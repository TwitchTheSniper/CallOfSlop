using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// The command view's FACTIONS layer (docs/sandbox/design/faction-controllers.md section 7 and phase 4; Karl's answer 3): each planet ringed in its
/// holder's colour (dashed, in the leader's colour and white, when contested) with the holder's tag and share; the faction groups this player knows of
/// (fog of war, the user's answer 7: on their world within their sensors, and those the news named lately, dim and marked with how old the sighting is)
/// with what they seem to be doing; a group of a power hostile to the player that is out to take a site, or close by, is red and flagged THREAT. All from
/// <see cref="Game.FactionMap"/> (sent once a second); labels are made only when the map changes. Off, it costs one bool check a frame.
/// </summary>
public sealed partial class Ui
{
    /// <summary>Is the factions layer shown (kept while the view is closed and opened again).</summary>
    public bool CommandFactionsOn;

    private readonly int[] _cmdFacBody = new int[PowerControllers.MaxPlanets];
    private readonly CmdLabel[] _cmdFacPlanet = new CmdLabel[PowerControllers.MaxPlanets];
    private readonly CmdLabel[] _cmdFacGroup = new CmdLabel[FactionMapView.MaxGroups];
    private int _cmdFacVersion = -1;
    private StarSystem? _cmdFacSys;
    private CmdLabel _cmdFacNone;

    /// <summary>What the layer shows now (the window tool's <c>factions layer</c>): planets drawn, groups known.</summary>
    public (int Planets, int Groups) CommandFactionCounts(Game g) => (g.FactionMap.PlanetCount, g.FactionMap.GroupCount);

    private static Vector3 CmdPowerColour(byte power) =>
        CrewPowers.Of(power) is { } d ? new Vector3((d.Colour >> 16 & 255) / 255f, (d.Colour >> 8 & 255) / 255f, (d.Colour & 255) / 255f) : new Vector3(0.7f, 0.7f, 0.7f);

    private static string CmdPowerTag(byte power) => CrewPowers.Of(power) is { } d ? (d.Tag.Length > 0 ? d.Tag : d.Name).ToUpperInvariant() : "?";

    private void CmdFactions(ImDrawListPtr dl, Game g, SpaceSession s, float fade, double now)
    {
        if (!CommandFactionsOn)
            return;
        var v = g.FactionMap;
        var sys = s.System;
        if (v.Version != _cmdFacVersion || !ReferenceEquals(sys, _cmdFacSys))
        {
            _cmdFacVersion = v.Version;
            _cmdFacSys = sys;
            for (var i = 0; i < v.PlanetCount; i++)
            {
                _cmdFacBody[i] = sys.IndexOf(v.Planet[i]);
                var key = v.Holder[i] * 1000L + v.Leader[i] * 1000000L + v.Share[i];
                if (_cmdFacPlanet[i].Stale(key))
                    _cmdFacPlanet[i].Text = v.Holder[i] != 0 ? $"{CmdPowerTag(v.Holder[i])} {v.Share[i] * 100 / 255}%" : v.Leader[i] != 0 ? $"CONTESTED ({CmdPowerTag(v.Leader[i])} {v.Share[i] * 100 / 255}%)" : "CONTESTED";
            }
            for (var i = 0; i < v.GroupCount; i++)
            {
                ref var gr = ref v.Groups[i];
                var guess = gr.Task switch { FarTask.Take => "STRIKE", FarTask.Defend => "DEFENCE", FarTask.Hold => "GARRISON", FarTask.Home => "RETURNING", _ => "GROUP" };
                var key = gr.Power | (long)gr.Task << 8 | (long)gr.Ships << 16 | (gr.Stale ? 1L << 24 : 0) | (gr.Threat ? 1L << 25 : 0) | (long)(gr.Age / 30) << 26;
                if (_cmdFacGroup[i].Stale(key))
                    _cmdFacGroup[i].Text = $"{(gr.Threat ? "THREAT  " : "")}{CmdPowerTag(gr.Power)} {guess} x{gr.Ships}{(gr.Stale ? $"  ({gr.Age / 60} MIN AGO)" : "")}";
            }
        }
        // The planets, ringed by who holds them.
        for (var i = 0; i < v.PlanetCount; i++)
        {
            var b = _cmdFacBody[i];
            if (b < 0)
                continue;
            var at = CmdP(sys.PositionOf(b, _cvTime));
            var r = MathF.Max((float)(sys.Bodies[b].Radius * _cvScale), 5 * _scale) + 13 * _scale;
            if (!CmdOn(at, r + 40))
                continue;
            if (v.Holder[i] != 0)
                CmdRing(dl, at, r, CmdPowerColour(v.Holder[i]), 0.85f * fade, 2f);
            else
            {
                CmdRing(dl, at, r, v.Leader[i] != 0 ? CmdPowerColour(v.Leader[i]) : CmdDim, 0.8f * fade, 2f, 12, 0.5f);
                CmdRing(dl, at, r, CmdWhite, 0.55f * fade, 2f, 12, 0.5f, MathF.PI / 12);
            }
            CmdText(dl, at + new float2(0, r + 3 * _scale), 10.5f, v.Holder[i] != 0 ? CmdPowerColour(v.Holder[i]) : CmdWhite, 0.9f * fade, _cmdFacPlanet[i].Text ?? "", 1);
        }
        // The groups known: on this world where they are (or were), else beside their planet, or on their way between two.
        var fan = 0;
        for (var i = 0; i < v.GroupCount; i++)
        {
            ref var gr = ref v.Groups[i];
            float2 at;
            if (gr.Here)
                at = CmdP(_cvPose.ToEcliptic(Vec3d.From(gr.Pos)));
            else if (gr.Planet < v.PlanetCount && _cmdFacBody[gr.Planet] is var pb and >= 0)
            {
                var c = CmdP(sys.PositionOf(pb, _cvTime));
                var r = MathF.Max((float)(sys.Bodies[pb].Radius * _cvScale), 5 * _scale) + 26 * _scale;
                var ang = -0.6f + fan++ * 0.45f;
                at = c + new float2(MathF.Cos(ang), MathF.Sin(ang)) * r;
            }
            else if (gr.ToPlanet < v.PlanetCount && _cmdFacBody[gr.ToPlanet] is var tb and >= 0)
            {
                var to = CmdP(sys.PositionOf(tb, _cvTime));
                at = to + new float2(-30 * _scale, -18 * _scale) * (1 - gr.Progress / 255f) * 4;
                CmdLine(dl, at, to, CmdPowerColour(gr.Power), 0.4f * fade);
            }
            else
                continue;
            if (!CmdOn(at))
                continue;
            var col = gr.Threat ? CmdRed : CmdPowerColour(gr.Power);
            var a = (gr.Stale ? 0.5f : 0.95f) * fade;
            var h = 5 * _scale;
            ImDrawListExtensions.AddTriangleFilled(dl, at + new float2(0, -h), at + new float2(h, h * 0.8f), at + new float2(-h, h * 0.8f), Hud.Colour(col, a));
            if (gr.Threat)
                CmdBrackets(dl, at, h + 4 * _scale, CmdRed, a);
            CmdText(dl, at + new float2(h + 5 * _scale, -6 * _scale), 10, col, a, _cmdFacGroup[i].Text ?? "");
        }
    }

    private void CmdFactionsButton(ImDrawListPtr dl, Game g, float2 q0, float2 size, float fade)
    {
        if (CmdButton(dl, q0, q0 + size, "FACTIONS", CmdAmber, fade, CommandFactionsOn))
        {
            CommandFactionsOn = !CommandFactionsOn;
            _cmdFacVersion = -1;
        }
        if (CommandFactionsOn && g.FactionMap.PlanetCount == 0)
        {
            if (_cmdFacNone.Stale(1))
                _cmdFacNone.Text = "NO FACTION TERRITORY HERE";
            CmdText(dl, new float2(q0.X + size.X * 0.5f, q0.Y + size.Y + 22 * _scale), 10, CmdAmber, 0.8f * fade, _cmdFacNone.Text!, 1);
        }
    }
}
