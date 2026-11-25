using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using CallOfSlop.ShipDesigner;

namespace CallOfSlop;

/// <summary>Where the Ship Designer's yard goes this frame (ImGui pixels) and what it shows; Program draws it behind the UI.</summary>
public sealed record ShipDesignerFrame(float2 Min, float2 Max, IReadOnlyList<DesignObj> Objs, Matrix4x4 View, Matrix4x4 Proj, Vector3 Eye);

/// <summary>
/// Main menu > SHIP DESIGNER: the web designer (tools/ship-designer, Artifact "Ship designer") as a screen of the game, like for like.
/// The design, its commands and every mesh are the designer's own C# (Source/ShipDesigner, through <see cref="DesignSession"/>);
/// this is the page's script ported: the four modes, the panels, the yard (an orbit view; drag bodies, station rings, pieces, rooms
/// and doors; sliders that apply on release) and the checks. Partial files: Scene (what is drawn and picked), Input (the yard's
/// mouse and keys), Panels (left, inspector, checks).
/// </summary>
public sealed partial class Ui
{
    /// <summary>The Ship Designer is up (main menu only).</summary>
    public bool ShipDesignerOpen;

    /// <summary>The yard to draw this frame (null when the screen isn't up).</summary>
    public ShipDesignerFrame? ShipYardFrame;

    private DesignSession? _sd;
    private JsonDocument? _sdDoc, _sdCatDoc;
    private JsonElement S, CAT;
    private bool _sdLoaded;

    // The page's state (index.html: sel, mode, view, deck).
    private string? _selKind;
    private int _selId, _selStation = -1;
    private string _sdMode = "hull";
    private bool _vXray, _vRooms = true, _vLinks = true, _vPerson = true, _vHull = true, _vParts = true;
    private int _sdDeck;
    private bool _canUndo, _canRedo;

    // A drag sends a command a frame at most; its first is an undo step, the rest merge into it.
    private JsonObject? _sdPending;
    private bool _dragFirst = true;

    private string _sdToast = "";
    private bool _sdToastInfo;
    private double _sdToastUntil, _sdNow;
    private string _sdTip = "";
    private bool _sdFileOpen;
    private ImInputString? _sdFileText;
    private string _sdFileMsg = "";

    private static readonly Dictionary<string, string> SdTips = new()
    {
        ["hull"] = "Click a hull to pick it and move it with the arrows. Drag a blue station ring along the ship. Shape a station's profile on the left.",
        ["pieces"] = "Drag a piece from the left onto the hull. Drag a piece to move it; drag it off the ship to take it off.",
        ["connectors"] = "Ladders and lifts between decks: pick a room and add one going down or up, or pick Ladder or Lift on the left and click a deck.",
        ["interior"] = "Pick a deck. Drag on the deck with Draw rooms on (or hold Shift) to lay a room. Drag a room to move it, or its edge or corner to size it.",
    };

    /// <summary>Main menu > Ship Designer: opens on the design kept last time (shipdesign.json), else the Zero Day.</summary>
    public void OpenShipDesigner()
    {
        ShipDesignerOpen = true;
        if (_sd != null)
            return;
        _sd = new DesignSession();
        _sdCatDoc = JsonDocument.Parse(DesignSession.Catalogue());
        CAT = _sdCatDoc.RootElement;
        var saved = ShipDesignStore.Load();
        JsonElement a = default;
        if (saved != null)
            a = Cmd(new JsonObject { ["op"] = "load", ["json"] = saved });
        if (saved == null || Str(a, "error").Length > 0)
            Take(_sd.Scene());
        _sdTip = SdTips[_sdMode];
        _sdFramePending = true;
    }

    /// <summary>Back to the main menu (the bar's Back and Esc). The design is kept as it is.</summary>
    public void CloseShipDesigner()
    {
        if (_sdFileOpen)
        {
            _sdFileOpen = false;
            return;
        }
        if (_mounting != null)
        {
            StopMount();
            return;
        }
        ShipDesignerOpen = false;
        ShipYardFrame = null;
        _sdHeld.Clear();
    }

    // ---------- the C# side (the page's take, cmd, dragCmd, flush)

    private JsonElement Take(string answerText)
    {
        var doc = JsonDocument.Parse(answerText);
        var a = doc.RootElement;
        var scene = a.GetProperty("scene");
        // Hulls left out of a light answer are drawn from the last ones: keep which surfaces each copy had.
        foreach (var h in Arr(scene, "hulls"))
            if (h.TryGetProperty("copies", out var cs) && cs.ValueKind == JsonValueKind.Array)
                _hullCopies[I(h, "id")] = cs.EnumerateArray().Select(c => (I(c, "copy"), Len(c, "skin"), Len(c, "fore"), Len(c, "aft"))).ToList();
        _sdDoc?.Dispose(); // (after the old one is let go nothing may read it: the scene below is the new one)
        _sdDoc = doc;
        S = scene;
        _canUndo = B(a, "undo");
        _canRedo = B(a, "redo");
        var err = Str(a, "error");
        if (err.Length > 0)
            Toast(err);
        _sdLoaded = true;
        Rebuild();
        if (Arr(S, "hulls").All(h => h.TryGetProperty("copies", out var c) && c.ValueKind == JsonValueKind.Array))
            ShipDesignStore.Save(_sd!.Save());
        return a;
    }

    private static bool Len(JsonElement c, string surf) => c.TryGetProperty(surf, out var m) && m.TryGetProperty("p", out var p) && p.GetArrayLength() > 0;

    private JsonElement Cmd(JsonObject op, bool merge = false)
    {
        op["merge"] = merge;
        return Take(_sd!.Apply(op.ToJsonString()));
    }

    private void DragCmd(JsonObject op) => _sdPending = op;

    private JsonElement? Flush()
    {
        if (_sdPending == null)
            return null;
        var op = _sdPending;
        _sdPending = null;
        var a = Cmd(op, !_dragFirst);
        _dragFirst = false;
        return a;
    }

    private void Toast(string msg, bool info = false)
    {
        _sdToast = msg;
        _sdToastInfo = info;
        _sdToastUntil = _sdNow + 3.2;
    }

    // ---------- reading the scene (JSON, as the page reads it)

    private static IEnumerable<JsonElement> Arr(JsonElement e, string k) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray() : [];

    private static bool Has(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    private static double D(JsonElement e, string k, double or = 0) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : or;

    private static int I(JsonElement e, string k, int or = 0) => (int)Math.Round(D(e, k, or));

    private static bool B(JsonElement e, string k, bool or = false) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) ? v.ValueKind == JsonValueKind.True || (v.ValueKind != JsonValueKind.False && or) : or;

    private static string Str(JsonElement e, string k, string or = "") =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : or;

    private static JsonElement Get(JsonElement e, string k) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) ? v : default;

    private static Vector3 V3(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Array || a.GetArrayLength() < 3)
            return Vector3.Zero;
        return new Vector3((float)a[0].GetDouble(), (float)a[1].GetDouble(), (float)a[2].GetDouble());
    }

    private static double[] Nums(JsonElement a) => a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Select(x => x.GetDouble()).ToArray() : [];

    private static JsonElement ById(IEnumerable<JsonElement> list, int id) => list.FirstOrDefault(x => I(x, "id") == id);

    private static bool Found(JsonElement e) => e.ValueKind == JsonValueKind.Object;

    private static string Fmt(double v) => (Math.Round(v * 100) / 100).ToString(CultureInfo.InvariantCulture);

    private bool Stacked => B(S, "stacked");

    private bool Inside => _sdMode is "interior" or "connectors";

    private static string RoomName(JsonElement r) => Str(r, "label") is { Length: > 0 } l ? l : Str(r, "kind");

    // ---------- selection

    private void Select(string? kind, int id, bool redraw = true)
    {
        _selKind = kind;
        _selId = id;
        _selStation = -1;
        if (redraw)
            Rebuild();
    }

    private void SelectStation(int hull, int i)
    {
        _selKind = "hull";
        _selId = hull;
        _selStation = i;
        Rebuild();
    }

    /// <summary>A mode picked from outside (the --ship-designer command line).</summary>
    public void ShipDesignerMode(string m)
    {
        if (SdTips.ContainsKey(m))
            SetMode(m);
    }

    private void SetMode(string m)
    {
        if (_mounting != null)
            StopMount();
        _sdMode = m;
        if (m != "hull" && _selKind == "hull")
            Select(null, 0, false);
        _sdTip = SdTips[m];
        Rebuild();
    }

    // ---------- the screen

    private static readonly float4 SdBg = new(0.106f, 0.137f, 0.161f, 1f), SdPanel = new(0.075f, 0.094f, 0.110f, 0.98f), SdLine = new(0.2f, 0.25f, 0.29f, 1f);
    private static readonly float4 SdOn = new(0.16f, 0.34f, 0.55f, 1f), SdOff = new(0.15f, 0.19f, 0.22f, 1f), SdAmber = new(1f, 0.69f, 0.18f, 1f), SdDimText = new(0.58f, 0.64f, 0.68f, 1f);
    private static readonly float4 SdBad = new(1f, 0.42f, 0.31f, 1f), SdWarn = new(1f, 0.76f, 0.35f, 1f), SdGood = new(0.36f, 0.82f, 0.48f, 1f);

    /// <summary>Draws the Ship Designer (main menu): the bar, the left panel, the yard, the inspector and the checks.</summary>
    public void DrawShipDesigner(double now)
    {
        ShipYardFrame = null;
        if (!ShipDesignerOpen || _sd == null)
            return;
        _sdNow = now;
        Flush(); // (the page's animation frame: a drag's command, once a frame at most)
        MountTick();

        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(vp.WorkPos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(vp.WorkSize, ImGuiCond.Always);
        var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBackground
                    | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(0, 0));
        ImGui.PushStyleColor(ImGuiCol.Button, SdOff);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new float4(0.22f, 0.29f, 0.34f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, SdOn);
        if (ImGui.Begin("SHIP DESIGNER##sd"u8, flags))
        {
            var p0 = ImGui.GetWindowPos();
            var size = ImGui.GetWindowSize();
            var barH = 44 * _scale;
            var sideW = MathF.Min(330 * _scale, size.X * 0.27f);
            var dl = ImGui.GetWindowDrawList();
            var p1 = p0 + new float2(size.X, barH);
            ImDrawListExtensions.AddRectFilled(dl, in p0, in p1, Sd8(SdPanel), 0, default);
            ImGui.SetCursorScreenPos(p0 + new float2(10 * _scale, 9 * _scale));
            DrawSdBar(size.X - 20 * _scale);

            var top = p0.Y + barH;
            var yardMin = new float2(p0.X + sideW, top);
            var yardMax = new float2(p0.X + size.X - sideW, p0.Y + size.Y);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(10 * _scale, 10 * _scale));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, SdPanel);
            ImGui.SetCursorScreenPos(new float2(p0.X, top));
            if (ImGui.BeginChild("##sdleft"u8, new float2(sideW, size.Y - barH), ImGuiChildFlags.AlwaysUseWindowPadding, ImGuiWindowFlags.None))
            {
                ImGui.PushTextWrapPos(0);
                if (_sdLoaded)
                    LeftPanel();
                ImGui.PopTextWrapPos();
            }
            ImGui.EndChild();
            ImGui.SetCursorScreenPos(new float2(yardMax.X, top));
            if (ImGui.BeginChild("##sdright"u8, new float2(sideW, size.Y - barH), ImGuiChildFlags.AlwaysUseWindowPadding, ImGuiWindowFlags.None))
            {
                ImGui.PushTextWrapPos(0);
                if (_sdLoaded)
                {
                    Inspector();
                    ImGui.Separator();
                    Checks();
                }
                ImGui.PopTextWrapPos();
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar();

            Yard(yardMin, yardMax);
            if (_sdFileOpen)
                DesignFileDialog();
        }
        ImGui.End();
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();
    }

    private static ImColor8 Sd8(float4 c) => new((byte)(c.X * 255), (byte)(c.Y * 255), (byte)(c.Z * 255), (byte)(c.W * 255));

    private static ImColor8 Hex8(uint hex, byte a = 255) => new((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex, a);

    /// <summary>A button lit when <paramref name="on"/>, as the page's "on" class.</summary>
    private static bool OnButton(string label, bool on, bool disabled = false)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, on ? SdOn : SdOff);
        if (disabled)
            ImGui.BeginDisabled();
        var hit = ImGui.Button(label);
        if (disabled)
            ImGui.EndDisabled();
        ImGui.PopStyleColor();
        return hit && !disabled;
    }

    private void DrawSdBar(float width)
    {
        ImGui.PushFont(default, 20);
        ImGui.Text("Ship Designer"u8);
        ImGui.PopFont();
        ImGui.SameLine();
        ImGuiSafe.TextColored(SdDimText, _sdLoaded ? Str(S, "name") : "loading");
        ImGui.SameLine(0, 24 * _scale);
        foreach (var (k, t) in new[] { ("hull", "Hull"), ("pieces", "Pieces"), ("interior", "Interior"), ("connectors", "Connectors") })
        {
            if (OnButton($"{t}##sdm{k}", _sdMode == k))
                SetMode(k);
            ImGui.SameLine();
        }
        var right = ImGui.GetWindowPos().X + width - 470 * _scale;
        ImGui.SameLine(MathF.Max(ImGui.GetCursorPosX() + 20 * _scale, right - ImGui.GetWindowPos().X));
        ImGui.SetNextItemWidth(130 * _scale);
        if (ImGui.BeginCombo("##sdstart"u8, "New from..."u8))
        {
            foreach (var s in Arr(CAT, "starts"))
                if (ImGui.Selectable(s.GetString()!))
                {
                    Cmd(new JsonObject { ["op"] = "start", ["name"] = s.GetString() });
                    Select(null, 0);
                    _sdFramePending = true;
                }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (OnButton("Undo##sdundo", false, !_canUndo))
            Take(_sd!.Undo());
        if (ImGui.IsItemHovered())
            ImGuiSafe.SetTooltip("Ctrl+Z (Ctrl+C, Ctrl+X, Ctrl+V copy, cut and paste what is picked)");
        ImGui.SameLine();
        if (OnButton("Redo##sdredo", false, !_canRedo))
            Take(_sd!.Redo());
        if (ImGui.IsItemHovered())
            ImGuiSafe.SetTooltip("Ctrl+Y or Ctrl+Shift+Z");
        ImGui.SameLine();
        if (ImGui.Button("Design file##sdfile"u8))
        {
            _sdFileText ??= new ImInputString(1 << 21);
            ImGuiSafe.SetInput(_sdFileText, _sd!.Save());
            _sdFileMsg = "";
            _sdFileOpen = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("BACK [Esc]##sdback"u8))
            CloseShipDesigner();
    }

    private void DesignFileDialog()
    {
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(vp.WorkPos + vp.WorkSize * 0.5f, ImGuiCond.Always, new float2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new float2(MathF.Min(760 * _scale, vp.WorkSize.X - 40), MathF.Min(560 * _scale, vp.WorkSize.Y - 40)));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(14 * _scale, 12 * _scale));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, SdPanel);
        if (ImGui.Begin("Design file##sdfiledlg"u8, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove))
        {
            ImGuiSafe.TextColoredWrapped(SdDimText, "The design as JSON. Copy it to keep it (the game will read this file), or paste one in and load it.");
            var avail = ImGui.GetContentRegionAvail();
            var box = new float2(avail.X, avail.Y - 60 * _scale);
            ImGui.InputTextMultiline("##sdjson"u8, _sdFileText!, in box, ImGuiInputTextFlags.None, null!, IntPtr.Zero);
            if (ImGui.Button("Copy##sdcopy"u8))
            {
                SafeClipboard.Sink(ImGuiSafe.Text(_sdFileText!));
                _sdFileMsg = "Copied.";
            }
            ImGui.SameLine();
            if (ImGui.Button("Load this text##sdload"u8))
            {
                var a = Cmd(new JsonObject { ["op"] = "load", ["json"] = ImGuiSafe.Text(_sdFileText!) });
                var err = Str(a, "error");
                _sdFileMsg = err.Length > 0 ? "Not loaded: " + err : "Loaded.";
                if (err.Length == 0)
                {
                    Select(null, 0);
                    _sdFramePending = true;
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Close##sdclose"u8))
                _sdFileOpen = false;
            if (_sdFileMsg.Length > 0)
                ImGuiSafe.TextColored(SdDimText, _sdFileMsg);
        }
        ImGui.End();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
    }

    // ---------- controls that apply on release (the page's slider, num, colour)

    private readonly Dictionary<string, float> _sdHeld = [];
    private readonly Dictionary<string, float3> _sdHeldColour = [];
    private readonly Dictionary<string, ImInputString> _sdTexts = [];
    private string? _sdTextEditing;

    /// <summary>
    /// A slider whose command goes when it is let go (Karl, 2026-11-24: dragging lagged); dragging only moves the shown value.
    /// <paramref name="v"/> in the slider's own units (cm, %, m), snapped to <paramref name="step"/>.
    /// </summary>
    private void Slider(string label, double v, double min, double max, double step, string unit, Action<double> done, string id = "")
    {
        var key = label + "##sl" + id;
        var shown = _sdHeld.TryGetValue(key, out var held) ? held : (float)v;
        ImGui.Text(label);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        var fmt = (step >= 1 ? "%.0f" : step >= 0.1 ? "%.1f" : "%.2f") + unit.Replace("%", "%%");
        ImGui.SliderFloat("##" + key, ref shown, (float)min, (float)Math.Max(min, max), fmt, ImGuiSliderFlags.AlwaysClamp);
        if (step > 0)
            shown = (float)(Math.Round(shown / step) * step);
        if (ImGui.IsItemActive())
            _sdHeld[key] = shown;
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            _sdHeld.Remove(key);
            done(shown);
        }
        else if (!ImGui.IsItemActive())
            _sdHeld.Remove(key);
    }

    /// <summary>A number box that applies when it is left or Enter is pressed.</summary>
    private void Num(string id, double v, Action<double> set, double step = 0.05, float width = 0)
    {
        var key = "##num" + id;
        var shown = _sdHeld.TryGetValue(key, out var held) ? held : (float)v;
        ImGui.SetNextItemWidth(width > 0 ? width : -1);
        ImGui.InputFloat(key, ref shown, (float)step, 0, step >= 1 ? "%.0f" : "%.2f", ImGuiInputTextFlags.None);
        if (ImGui.IsItemActive())
            _sdHeld[key] = shown;
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            _sdHeld.Remove(key);
            if (float.IsFinite(shown))
                set(shown);
        }
        else if (!ImGui.IsItemActive())
            _sdHeld.Remove(key);
    }

    /// <summary>A colour swatch whose command goes when the picker is let go.</summary>
    private void Colour(string id, string hex, Action<string> set)
    {
        var key = "##col" + id;
        var c = DesignMesh.Hex(hex);
        var col = _sdHeldColour.TryGetValue(key, out var h) ? h : new float3(c.R / 255f, c.G / 255f, c.B / 255f);
        if (ImGui.ColorEdit3(key, ref col, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
            _sdHeldColour[key] = col;
        if (_sdHeldColour.ContainsKey(key) && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            _sdHeldColour.Remove(key);
            set($"#{(int)Math.Round(col.X * 255):x2}{(int)Math.Round(col.Y * 255):x2}{(int)Math.Round(col.Z * 255):x2}");
        }
    }

    /// <summary>A text box that applies when it is left.</summary>
    private void TextBox(string id, string value, Action<string> set, string hint = "")
    {
        if (!_sdTexts.TryGetValue(id, out var t))
            _sdTexts[id] = t = new ImInputString(65);
        if (_sdTextEditing != id)
            ImGuiSafe.SetInput(t, value);
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##txt" + id, hint, t, ImGuiInputTextFlags.None);
        if (ImGui.IsItemActive())
            _sdTextEditing = id;
        else if (_sdTextEditing == id)
            _sdTextEditing = null;
        if (ImGui.IsItemDeactivatedAfterEdit())
            set(ImGuiSafe.Text(t));
    }

    private static void Hint(string text) => ImGuiSafe.TextColoredWrapped(SdDimText, text);

    private static void H2(string text, string n = "")
    {
        ImGui.Spacing();
        ImGuiSafe.TextColored(new float4(0.93f, 0.95f, 0.96f, 1f), text);
        if (n.Length > 0)
        {
            ImGui.SameLine();
            ImGuiSafe.TextColored(SdDimText, n);
        }
    }

    private static void H3(string text)
    {
        ImGui.Spacing();
        ImGuiSafe.TextColored(SdAmber, text);
    }
}
