using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The robot shell's panel, bottom left while this PC's player wears one (Karl, 2026-11-18: "a UI similar to the RTS screen"): the
/// command view's glass plate (UiCommand.cs), a hologram of the shell with each part lit by its condition, and a row for its health
/// and each of its eight works (ShellWear.cs) with a bar and a percentage. It replaces the HUD's ROBOT SHELL and SHELL WEAR lines.
/// Cyan is sound, amber worn, red failing or failed (and blinking). Drawn on the foreground list before the shell's glitches, so the
/// static and the brown-outs cover it too. Every string is made once: nothing allocates a frame.
/// </summary>
public sealed partial class Ui
{
    private static readonly string[] SpPct = MakeSpPct();
    private static readonly string[] SpRows = ["HULL", "L LEG", "R LEG", "L ARM", "R ARM", "POWER", "CPU", "OPTICS", "PLATING"];
    private byte _spFrame = 255;
    private string _spFrameName = "";

    private static string[] MakeSpPct()
    {
        var s = new string[101];
        for (var k = 0; k <= 100; k++)
            s[k] = k + "%";
        return s;
    }

    /// <summary>A condition's colour: cyan sound, amber worn, red failing.</summary>
    private static Vector3 SpColour(int pct, float worn) => pct <= 15 ? CmdRed : pct < worn ? CmdAmber : CmdCyan;

    private static int SpWork(Player me, ShellWork w) => ShellWear.Pct(me.ShellWear[(int)w]);

    private void DrawShellPanel(Game game, float2 pos, float2 size)
    {
        if (PauseOpen || MenuOpen || CommandOpen || !game.SandboxOn || game.Local is not { Alive: true, InShell: true } me)
            return;
        var dl = ImGui.GetForegroundDrawList();
        var s = _scale;
        var now = Brutal.GlfwApi.Glfw.Time;
        var worn = ShellWear.Current.WornPct;
        var blink = 0.72f + 0.28f * MathF.Sin((float)(now * 7.0));

        Span<int> pct = stackalloc int[9];
        pct[0] = Math.Clamp(me.Health * 100 / World.MaxHealth, 0, 100);
        for (var k = 0; k < ShellWear.Works; k++)
            pct[k + 1] = SpWork(me, (ShellWork)k);
        var worst = 100;
        foreach (var v in pct)
            worst = Math.Min(worst, v);

        // The plate: above the health bar, under the chat.
        var w = 292 * s;
        var h = 196 * s;
        var p0 = new float2(pos.X + 20 * s, pos.Y + size.Y - 48 * s - h);
        var p1 = p0 + new float2(w, h);
        var edge = SpColour(worst, worn);
        CmdPlate(dl, p0, p1, 1f, edge == CmdCyan ? CmdCyan : edge);

        // The heading: what it is, its frame, and its health large on the right.
        if (_spFrame != me.ShellFrame)
        {
            _spFrame = me.ShellFrame;
            _spFrameName = ShellFrames.Of(me.ShellFrame).Name.ToUpperInvariant();
        }
        CmdText(dl, p0 + new float2(12 * s, 7 * s), 13.5f, CmdWhite, 0.95f, "ROBOT SHELL");
        CmdText(dl, p0 + new float2(12 * s, 23 * s), 10.5f, CmdDim, 0.9f, _spFrameName);
        var hullCol = SpColour(pct[0], 50);
        CmdText(dl, new float2(p1.X - 22 * s, p0.Y + 6 * s), 19f, hullCol, pct[0] <= 15 ? blink : 1f, SpPct[pct[0]], 2);
        CmdLine(dl, p0 + new float2(10 * s, 40 * s), new float2(p1.X - 10 * s, p0.Y + 40 * s), CmdDim, 0.5f);

        // The hologram of the shell, seen from behind: head (processor, optics), body (health, power cell, plating), arms, legs.
        var cx = p0.X + 54 * s;
        var top = p0.Y + 52 * s;
        var head = Math.Min(pct[6], pct[7]);
        var body = Math.Min(pct[0], Math.Min(pct[5], pct[8]));
        SpPart(dl, new float2(cx - 9 * s, top), new float2(cx + 9 * s, top + 17 * s), head, worn, blink);
        SpPart(dl, new float2(cx - 15 * s, top + 21 * s), new float2(cx + 15 * s, top + 62 * s), body, worn, blink);
        SpPart(dl, new float2(cx - 26 * s, top + 22 * s), new float2(cx - 18 * s, top + 60 * s), pct[3], worn, blink);
        SpPart(dl, new float2(cx + 18 * s, top + 22 * s), new float2(cx + 26 * s, top + 60 * s), pct[4], worn, blink);
        SpPart(dl, new float2(cx - 14 * s, top + 65 * s), new float2(cx - 3 * s, top + 112 * s), pct[1], worn, blink);
        SpPart(dl, new float2(cx + 3 * s, top + 65 * s), new float2(cx + 14 * s, top + 112 * s), pct[2], worn, blink);
        CmdLine(dl, new float2(cx - 30 * s, top + 4 * s), new float2(cx - 13 * s, top + 4 * s), CmdDim, 0.4f); // (the visor's line)
        CmdBrackets(dl, new float2(cx, top + 56 * s), 40 * s, CmdDim, 0.55f, 1f);
        var sweep = (float)(now * 0.45 % 1.0);
        var sy = top - 2 * s + sweep * 118 * s;
        CmdLine(dl, new float2(cx - 36 * s, sy), new float2(cx + 36 * s, sy), CmdCyan, 0.22f * MathF.Sin(sweep * MathF.PI), 1.2f); // (a scan going down it)

        // The rows: a label, a bar, the percentage.
        var lx = p0.X + 108 * s;
        var bx0 = p0.X + 178 * s;
        var bx1 = p1.X - 50 * s;
        for (var k = 0; k < 9; k++)
        {
            var y = p0.Y + 46 * s + k * 14.6f * s;
            var v = pct[k];
            var col = k == 0 ? hullCol : SpColour(v, worn);
            var failed = k > 0 && me.ShellWear[k - 1] >= ShellWear.Failed;
            var a = failed || v <= 15 ? blink : 1f;
            CmdText(dl, new float2(lx, y), 11f, col == CmdCyan ? CmdWhite : col, 0.9f * a, SpRows[k], 0, false);
            var by = y + 7 * s;
            dl.AddRectFilled(new float2(bx0, by - 1.5f * s), new float2(bx1, by + 1.5f * s), Hud.Colour(CmdDim, 0.25f));
            if (v > 0)
                dl.AddRectFilled(new float2(bx0, by - 1.5f * s), new float2(bx0 + (bx1 - bx0) * v / 100f, by + 1.5f * s), Hud.Colour(col, 0.9f * a));
            CmdText(dl, new float2(p1.X - 10 * s, y), 11f, col, a, failed ? "FAIL" : SpPct[v], 2, false);
        }
        CmdText(dl, new float2(p0.X + 12 * s, p1.Y - 16 * s), 10f, CmdDim, 0.8f, "ESC MENU: LEAVE THE SHELL", 0, false);
    }

    /// <summary>One part of the shell's hologram: glass lit by its condition, a glowing edge.</summary>
    private void SpPart(ImDrawListPtr dl, float2 a, float2 b, int pct, float worn, float blink)
    {
        var c = SpColour(pct, worn);
        var alpha = pct <= 15 ? blink : 1f;
        dl.AddRectFilled(a, b, Hud.Colour(c, (0.10f + 0.22f * pct / 100f) * alpha));
        CmdGlow(dl, a, new float2(b.X, a.Y), c, 0.9f * alpha, 1f);
        CmdGlow(dl, new float2(b.X, a.Y), b, c, 0.9f * alpha, 1f);
        CmdGlow(dl, b, new float2(a.X, b.Y), c, 0.9f * alpha, 1f);
        CmdGlow(dl, new float2(a.X, b.Y), a, c, 0.9f * alpha, 1f);
    }
}
