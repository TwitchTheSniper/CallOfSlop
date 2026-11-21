using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The seat controls panel, bottom right while this PC's player sits in a seat (Karl, 2026-11-21: "a UI on the bottom-right, similar to the
/// other UIs in style, where it grows with the amount of controls there are ... separate them into their relevant areas"). The command
/// view's glass plate (UiCommand.cs), as the vitals panel bottom left: a heading with the seat and how many links leave it, then a section
/// for each area the seat works (Game/GameSeatControls.cs builds them from its links), each row the keys on keycaps, what they do and a
/// state. It grows with its rows; the radar's scope stands above it. Nothing allocates a frame: the rows are remade only when they change.
/// </summary>
public sealed partial class Ui
{
    private float _seatPanelLeft; // the plate's left edge this frame (0: not drawn): the held item's lines stand beside it
    private void DrawSeatPanel(Game game, float2 pos, float2 size)
    {
        game.SeatPanelShown = false;
        RadarHud.ClearBelow = 0;
        _seatPanelLeft = 0;
        if (PauseOpen || MenuOpen || CommandOpen || game.SeatControls() is not { } sp)
            return;
        game.SeatPanelShown = true;
        var dl = ImGui.GetForegroundDrawList();
        var s = _scale;
        const float headH = 19f, rowH = 17f;
        var h = 48f;
        var status = game.SeatStatus;
        var arc = game.Local is { } me ? game.ArtilleryHudLine(me) : null; // (a shell gun in the seat: where it lands, GameSandboxShells.cs)
        var arcRows = arc is { Length: > 0 } ? 1 : 0;
        var w = 300 * s;
        if (arcRows > 0)
            w = MathF.Max(w, SpWidth(arc!, 11f * s));
        if (status.Count + arcRows > 0)
        {
            h += headH + 4 + (status.Count + arcRows) * rowH;
            for (var k = 0; k < status.Count; k++)
                w = MathF.Max(w, SpWidth(status[k].Text, 11f * s)); // (the panel widens to its longest line, up to half the screen)
            w = MathF.Min(w, MathF.Max(300 * s, size.X * 0.5f));
        }
        for (var k = 0; k < sp.Count; k++)
            h += sp.Rows[k].Heading ? headH + (k > 0 ? 4 : 0) : rowH;
        h = (h + 8) * s;
        var p1 = new float2(pos.X + size.X - 20 * s, pos.Y + size.Y - 30 * s);
        var p0 = p1 - new float2(w, h);
        RadarHud.ClearBelow = 30 * s + h + 44 * s; // (the scope's line of text under it clears the plate too)
        _seatPanelLeft = p0.X;
        CmdPlate(dl, p0, p1, 1f, CmdCyan);

        // The heading: what the panel is, the seat, and how many links leave it.
        CmdText(dl, p0 + new float2(12 * s, 7 * s), 13.5f, CmdWhite, 0.95f, "SEAT CONTROLS");
        CmdText(dl, p0 + new float2(12 * s, 23 * s), 10.5f, CmdDim, 0.9f, sp.Title);
        CmdText(dl, new float2(p1.X - 22 * s, p0.Y + 9 * s), 11f, CmdCyan, 0.9f, sp.Links, 2);
        CmdLine(dl, p0 + new float2(10 * s, 40 * s), new float2(p1.X - 10 * s, p0.Y + 40 * s), CmdDim, 0.5f);

        var y = p0.Y + 46 * s;

        // STATUS: the craft's lines that were the centre box's (speed and charge, thrust, the orbit, the gun, what won't go).
        if (status.Count + arcRows > 0)
        {
            CmdText(dl, new float2(p0.X + 12 * s, y + 3 * s), 10.5f, CmdCyan, 0.95f, "STATUS", 0, false);
            CmdLine(dl, new float2(p0.X + 12 * s, y + headH * s - 2 * s), new float2(p1.X - 12 * s, y + headH * s - 2 * s), CmdDim, 0.35f);
            y += headH * s;
            for (var k = 0; k < status.Count; k++)
            {
                var c = status[k].Color;
                var col = c.X > 0.85f && c.Y < 0.5f ? CmdRed : c.X > 0.85f && c.Y < 0.85f ? CmdAmber : c.Y > 0.8f && c.X < 0.6f ? CmdGreen : c.W < 0.9f || c.X < 0.8f ? CmdDim : CmdWhite;
                CmdText(dl, new float2(p0.X + 14 * s, y + 2 * s), 11f, col, 0.92f, status[k].Text, 0, false);
                y += rowH * s;
            }
            if (arcRows > 0)
            {
                CmdText(dl, new float2(p0.X + 14 * s, y + 2 * s), 11f, CmdAmber, 0.92f, arc!, 0, false);
                y += rowH * s;
            }
            y += 4 * s;
        }

        var keyX = p0.X + 14 * s;
        var textX = p0.X + 112 * s;
        for (var k = 0; k < sp.Count; k++)
        {
            ref var r = ref sp.Rows[k];
            if (r.Heading)
            {
                if (k > 0)
                    y += 4 * s;
                CmdText(dl, new float2(p0.X + 12 * s, y + 3 * s), 10.5f, CmdCyan, 0.95f, r.Text, 0, false);
                if (r.State.Length > 0)
                    CmdText(dl, new float2(p1.X - 12 * s, y + 4 * s), 9.5f, CmdDim, 0.85f, r.State, 2, false);
                CmdLine(dl, new float2(p0.X + 12 * s, y + headH * s - 2 * s), new float2(p1.X - 12 * s, y + headH * s - 2 * s), CmdDim, 0.35f);
                y += headH * s;
                continue;
            }
            var x = keyX;
            if (r.Key1.Length > 0)
                x = SpKeycap(dl, x, y, r.Key1);
            if (r.Key2.Length > 0)
            {
                CmdText(dl, new float2(x + 2 * s, y + 2 * s), 10.5f, CmdDim, 0.8f, "/", 0, false);
                SpKeycap(dl, x + 10 * s, y, r.Key2);
            }
            var col = r.Tone == 1 ? CmdCyan : r.Tone == 2 ? CmdAmber : CmdDim;
            CmdText(dl, new float2(textX, y + 2 * s), 11f, CmdWhite, 0.9f, r.Text, 0, false);
            if (r.State.Length > 0)
                CmdText(dl, new float2(p1.X - 12 * s, y + 2.5f * s), 10f, col, 0.95f, r.State, 2, false);
            if (r.Light > 0)
                SpLamp(dl, new float2(p1.X - 18 * s, y + 8 * s), r.Light == 1);
            y += rowH * s;
        }
    }

    /// <summary>The room a status line takes on the plate: its width with margins either side (Hud.TextWidth runs a few per cent short of
    /// what the small text draws at, so a little over).</summary>
    private float SpWidth(string text, float size) => Hud.TextWidth(text, size) * 1.08f + 30 * _scale;

    /// <summary>An on/off setting's lamp: lit green with a glow while on, a dark lens with a faint rim while off.</summary>
    private void SpLamp(ImDrawListPtr dl, float2 c, bool on)
    {
        var s = _scale;
        if (on)
        {
            ImDrawListExtensions.AddCircleFilled(dl, in c, 8 * s, Hud.Colour(CmdGreen, 0.16f), 20);
            ImDrawListExtensions.AddCircleFilled(dl, in c, 4.5f * s, Hud.Colour(CmdGreen, 0.95f), 16);
            ImDrawListExtensions.AddCircleFilled(dl, in c, 2f * s, Hud.Colour(CmdWhite, 0.8f), 12);
        }
        else
        {
            ImDrawListExtensions.AddCircleFilled(dl, in c, 4.5f * s, Hud.Colour(CmdBack, 0.95f), 16);
            ImDrawListExtensions.AddCircle(dl, in c, 4.5f * s, Hud.Colour(CmdDim, 0.7f), 16, s);
        }
    }

    /// <summary>A key on a small keycap at <paramref name="x"/>; returns where the next thing goes.</summary>
    private float SpKeycap(ImDrawListPtr dl, float x, float y, string key)
    {
        var s = _scale;
        var tw = Hud.TextWidth(key, 10f * s);
        var a = new float2(x, y + 1 * s);
        var b = new float2(x + MathF.Max(tw + 8 * s, 15 * s), y + 15 * s);
        dl.AddRectFilled(a, b, Hud.Colour(CmdCyan, 0.10f), 2 * s);
        dl.AddRect(a, b, Hud.Colour(CmdCyan, 0.55f), 2 * s, 0, s);
        CmdText(dl, new float2((a.X + b.X) * 0.5f, y + 2.5f * s), 10f, CmdWhite, 0.95f, key, 1, false);
        return b.X + 3 * s;
    }
}
