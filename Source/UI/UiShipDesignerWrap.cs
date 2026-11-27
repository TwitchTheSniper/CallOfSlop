using Brutal.ImGuiApi;

namespace CallOfSlop;

/// <summary>The Ship Designer's wraparound windows (Karl, 2026-11-27: cockpit glass round a body, its frame fitting itself to the skin;
/// ShipDesigner/DesignWrapWindows.cs): the list on a body and the panel that sets one.</summary>
public sealed partial class Ui
{
    /// <summary>The wraparound windows on body <paramref name="hull"/> (0: none picked) and the button that adds one there.</summary>
    private void WrapList(int hull)
    {
        if (hull == 0)
            return;
        ImGui.Separator();
        var ws = Arr(S, "wrapWindows").Where(w => I(w, "hull") == hull).ToList();
        H3("Wraparound windows");
        for (var i = 0; i < ws.Count; i++)
        {
            if (OnButton($"Window {i + 1}{(B(ws[i], "ok", true) ? "" : " (off the skin)")}##sdww{I(ws[i], "id")}", _selKind == "wrapwin" && _selId == I(ws[i], "id")))
                Select("wrapwin", I(ws[i], "id"));
            Row();
        }
        if (ImGui.Button("+ Wraparound window##sdwwadd"u8))
        {
            var a = Cmd(C("addWrapWindow", ("hull", hull)));
            if (Str(a, "error").Length > 0)
                Toast(Str(a, "error"));
            else
            {
                Select("wrapwin", I(a, "id"));
                Toast("Glass round the body, its frame fitted to the skin. Set it on the right.", true);
            }
        }
        if (ImGui.IsItemHovered())
            ImGuiSafe.SetTooltip("Cockpit glass that wraps round this body near the bow");
    }

    private void WrapInspector()
    {
        var w = ById(Arr(S, "wrapWindows"), _selId);
        if (!Found(w))
            return;
        var wid = I(w, "id");
        void Sl(string label, string key, double v, double min, double max, double step, string unit, double scale = 1, double off = 0) =>
            Slider(label, Math.Round(v * scale / step) * step, min, max, step, unit, x => Cmd(C("setWrapWindow", ("id", wid), (key, x / scale + off))), "ww" + key);
        H2("Wraparound window", B(w, "ok", true) ? $"{I(w, "panels")} pane{(I(w, "panels") == 1 ? "" : "s")}" : "off the skin");
        if (!B(w, "ok", true))
            Hint(Str(w, "why"));
        Hint("Glass round the body from a ring on its skin. The sill and header fit themselves back to the skin, and the skin behind the glass opens into the rooms there. Sizes apply when you let go.");
        double front = D(w, "front"), back = D(w, "back");
        Sl("Along the body", "z", D(w, "z") - front, 0, Math.Max(0.1, back - front), 0.05, " m", 1, front); // (from the bow; the window keeps its body's own z)
        H3("Placing");
        Sl("Turn", "turn", D(w, "turn"), -180, 180, 1, " deg");
        Sl("Wrap", "span", D(w, "span"), 20, 330, 1, " deg");
        Sl("Tilt back", "rake", D(w, "rake"), 0, 85, 1, " deg");
        Hint("Tilt 0 stands the glass upright, looking straight ahead.");
        Sl("Height", "height", D(w, "height"), 20, 300, 5, " cm", 100);
        H3("Panes");
        Slider("Panes", I(w, "panels"), 1, 12, 1, "", x => Cmd(C("setWrapWindow", ("id", wid), ("panels", (int)Math.Round(x)))), "wwpanels");
        Sl("Beading", "bead", D(w, "bead"), 2, 30, 1, " cm", 100);
        Sl("Frame", "frame", D(w, "frame"), 2, 30, 1, " cm", 100);
        Checkbox("Flat panes##sdwwflat", B(w, "flat", true), v => Cmd(C("setWrapWindow", ("id", wid), ("flat", v))));
        H3("Curve");
        Sl("Bow forward", "bow", D(w, "bow"), 0, 200, 5, " cm", 100);
        Sl("Curve starts", "bowStart", D(w, "bowStart"), 2, 100, 1, "%", 100);
        Hint("Bow stands the middle forward of the ends. Curve starts is how far out from the middle the glass begins to curve; beyond it, it runs straight to the ends.");
        if (ImGui.Button("Delete##sdwwdel"u8))
        {
            Cmd(C("delWrapWindow", ("id", wid)));
            Select(null, 0);
        }
    }
}
