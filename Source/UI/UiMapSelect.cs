using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The map select screen, opened from the Single player page ("Change map..."). Left: a column of map
/// pack tiles (picture, name, map count, sizes), True Random at the bottom. Right: the open pack's entries, "Random"
/// first (pre-selected whenever a pack is opened), then its maps (picture, size, player limit, description).
/// Click selects, double-click (or Enter, or CHOOSE) takes the selection and goes back to the lobby; double-clicking a
/// pack tile takes that pack's Random. BACK (or Esc) leaves without changing anything. Up/Down move through the maps,
/// Left/Right (or Page Up/Down) through the packs. The pictures come from <see cref="MapThumbs"/>; the lobby background
/// follows the selection.
/// </summary>
public sealed partial class Ui
{
    private bool _msOpen;
    private MapSelectState _ms;
    private int _msClickedPack = -1, _msClickedEntry = -1; // what the last single click landed on (a double-click must start there)
    private bool _msScrollPack, _msScrollEntry; // keyboard moved: scroll the selection into view this frame

    /// <summary>Is the map select screen up?</summary>
    public bool MapSelectOpen => _msOpen;

    /// <summary>
    /// While the screen is up: the selection to preview behind it (pack key, and the map's name, or null for Random).
    /// Null when the screen is closed (the lobby then previews the saved choice).
    /// </summary>
    public (string Pack, string? Map)? MapSelectPreview
    {
        get
        {
            if (!_msOpen)
                return null;
            var list = MapSelect.Current;
            if (list.Count == 0 || _ms.Pack < 0 || _ms.Pack >= list.Count)
                return null;
            var p = list[_ms.Pack];
            return (p.Key, _ms.Entry > 0 && _ms.Entry <= p.Maps.Length ? p.Maps[_ms.Entry - 1].Name : null);
        }
    }

    private void OpenMapSelect(Settings s)
    {
        var list = MapSelect.Current;
        var (pack, entry) = MapSelect.FromSettings(s, list);
        _ms = new MapSelectState { Pack = pack, Entry = entry };
        _msClickedPack = _msClickedEntry = -1;
        _msScrollPack = _msScrollEntry = true;
        _msOpen = true;
    }

    private void ChooseMap(Settings s, Audio audio, MapSelectPack p, int entry)
    {
        MapSelect.Apply(s, p, entry);
        audio.Effect(Sfx.HitTick, 0.5f);
        _msOpen = false;
    }

    private void DrawMapSelect(Settings s, Audio audio)
    {
        var list = MapSelect.Current;
        if (list.Count == 0)
        {
            _msOpen = false;
            return;
        }
        _ms.Pack = Math.Clamp(_ms.Pack, 0, list.Count - 1);
        _ms.Entry = Math.Clamp(_ms.Entry, 0, list[_ms.Pack].Maps.Length);
        var players = _page == Page.Multiplayer ? s.HostBots + 1 : s.Bots + 1; // you + bots, as the lobby's slider has it

        // ---- Keyboard ----------------------------------------------------------------------------
        if (ImGui.IsKeyPressed(ImGuiKey.Escape, false) || ImGui.IsKeyPressed(ImGuiKey.Backspace, false))
        {
            _msOpen = false;
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false))
        {
            ChooseMap(s, audio, list[_ms.Pack], _ms.Entry);
            return;
        }
        var before = _ms;
        if (ImGui.IsKeyPressed(ImGuiKey.DownArrow))
            _ms.Step(1, list);
        if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))
            _ms.Step(-1, list);
        if (ImGui.IsKeyPressed(ImGuiKey.RightArrow) || ImGui.IsKeyPressed(ImGuiKey.PageDown))
            _ms.StepPack(1, list);
        if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow) || ImGui.IsKeyPressed(ImGuiKey.PageUp))
            _ms.StepPack(-1, list);
        if (_ms.Pack != before.Pack || _ms.Entry != before.Entry)
        {
            _msScrollPack = _ms.Pack != before.Pack;
            _msScrollEntry = true;
            audio.Effect(Sfx.HitTick, 0.3f);
        }

        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + vp.WorkSize.X / 2, vp.WorkPos.Y + vp.WorkSize.Y / 2), ImGuiCond.Always, new float2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new float2(MathF.Min(1180 * _scale, vp.WorkSize.X - 40), MathF.Min(760 * _scale, vp.WorkSize.Y - 40)));
        ImGui.SetNextWindowBgAlpha(0.93f);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings
                                       | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoTitleBar;
        if (ImGui.Begin("##mapselect"u8, flags))
        {
            // ---- Header ------------------------------------------------------------------------------
            ImGui.PushFont(default, 28);
            ImGui.TextColored(Orange, "CHOOSE MAP"u8);
            ImGui.PopFont();
            ImGui.SameLine(0, 18 * _scale);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 8 * _scale);
            ImGuiSafe.TextColored(Dim, $"Current choice: {MapSelect.ChoiceText(s)}");

            var avail = ImGui.GetContentRegionAvail();
            var footerH = 40 * _scale;
            var bodyH = MathF.Max(160 * _scale, avail.Y - footerH - 10 * _scale);
            var listW = Math.Clamp(avail.X * 0.34f, 280 * _scale, 420 * _scale);
            var gap = 14 * _scale;

            // ---- Pack column -------------------------------------------------------------------------
            if (ImGui.BeginChild("##mspacks"u8, new float2(listW, bodyH), ImGuiChildFlags.None))
            {
                var dl = ImGui.GetWindowDrawList();
                var tw = ImGui.GetContentRegionAvail().X;
                var tileH = 104 * _scale;
                for (var i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    if (p.IsTrueRandom && i > 0)
                    {
                        // A little gap and a rule: True Random isn't a folder like the others.
                        var y = ImGui.GetCursorScreenPos().Y + 4 * _scale;
                        var x = ImGui.GetCursorScreenPos().X;
                        dl.AddLine(new float2(x + 8 * _scale, y), new float2(x + tw - 8 * _scale, y), new ImColor8(80, 84, 96, 255), 1f);
                        ImGui.Dummy(new float2(tw, 10 * _scale));
                    }
                    var p0 = ImGui.GetCursorScreenPos();
                    var clicked = ImGui.InvisibleButton($"##mspack{i}", new float2(tw, tileH));
                    var hot = ImGui.IsItemHovered();
                    var dbl = hot && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && _msClickedPack == i;
                    if (i == _ms.Pack && _msScrollPack)
                        ImGui.SetScrollHereY(0.5f);
                    if (dbl)
                    {
                        ChooseMap(s, audio, p, 0);
                        break;
                    }
                    if (clicked)
                    {
                        if (i != _ms.Pack || _ms.Entry != 0)
                            audio.Effect(Sfx.HitTick, 0.3f);
                        _ms.OpenPack(i); // Random comes pre-selected
                        _msClickedPack = i;
                        _msClickedEntry = -1;
                        _msScrollEntry = true;
                    }
                    var current = CurrentEntry(s, p) >= 0; // the saved choice is this pack's Random or one of its maps
                    DrawPackTile(dl, p, p0, new float2(tw, tileH), i == _ms.Pack, hot, current);
                    ImGui.Dummy(new float2(tw, 6 * _scale));
                }
            }
            ImGui.EndChild();
            _msScrollPack = false;

            // ---- The open pack -----------------------------------------------------------------------
            if (_msOpen)
            {
                ImGui.SameLine(0, gap);
                DrawPackEntries(s, audio, list[_ms.Pack], players, new float2(avail.X - listW - gap, bodyH));
            }

            // ---- Footer ------------------------------------------------------------------------------
            if (_msOpen)
            {
                var sel = list[_ms.Pack];
                ImGui.SetCursorPosY(ImGui.GetWindowSize().Y - ImGui.GetStyle().WindowPadding.Y - footerH + 4 * _scale);
                ImGui.SetCursorPosX(ImGui.GetStyle().WindowPadding.X);
                if (ImGui.Button("BACK##msback"u8, new float2(150 * _scale, footerH - 6 * _scale)))
                    _msOpen = false;
                ImGui.SameLine(0, 16 * _scale);
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (footerH - 6 * _scale - ImGui.GetFontSize()) / 2);
                ImGui.TextColored(Dim, "Click to select, double-click or Enter to choose. Up/Down: maps. Left/Right: packs. Esc: back."u8);
                var chooseW = 300 * _scale;
                ImGui.SameLine(ImGui.GetWindowSize().X - ImGui.GetStyle().WindowPadding.X - chooseW);
                ImGui.SetCursorPosY(ImGui.GetWindowSize().Y - ImGui.GetStyle().WindowPadding.Y - footerH + 4 * _scale);
                ImGui.PushStyleColor(ImGuiCol.Button, new float4(0.94f, 0.59f, 0.16f, 0.92f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new float4(1f, 0.68f, 0.28f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new float4(0.85f, 0.5f, 0.1f, 1f));
                ImGui.PushStyleColor(ImGuiCol.Text, new float4(0.09f, 0.06f, 0.03f, 1f));
                var label = _ms.Entry == 0
                    ? sel.IsTrueRandom ? "CHOOSE TRUE RANDOM###mschoose" : $"CHOOSE RANDOM FROM {sel.Name.ToUpperInvariant()}###mschoose"
                    : $"CHOOSE {sel.Maps[_ms.Entry - 1].Name.ToUpperInvariant()}###mschoose";
                if (ImGui.Button(label, new float2(chooseW, footerH - 6 * _scale)))
                    ChooseMap(s, audio, sel, _ms.Entry);
                ImGui.PopStyleColor(4);
            }
        }
        ImGui.End();
    }

    /// <summary>The right side: the pack's name and description, then Random and its maps.</summary>
    private void DrawPackEntries(Settings s, Audio audio, MapSelectPack p, int players, float2 size)
    {
        if (!ImGui.BeginChild("##msright"u8, size, ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.EndChild();
            return;
        }
        ImGui.PushFont(default, 24);
        ImGuiSafe.TextColored(new float4(1, 1, 1, 1), p.Name);
        ImGui.PopFont();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + size.X - 12 * _scale);
        ImGui.PushStyleColor(ImGuiCol.Text, new float4(0.78f, 0.79f, 0.83f, 1f));
        if (p.Pack is { } pack)
        {
            if (pack.Description.Length > 0)
                ImGuiSafe.TextWrapped(pack.Description);
            ImGuiSafe.TextColored(Dim, $"{p.CountLine}: {p.SizeLine}{(pack.Author.Length > 0 ? $"  -  by {pack.Author}" : "")}"
                + (pack.InTrueRandom ? "" : $"  -  not part of {MapPack.TrueRandomName}"));
        }
        else
        {
            var packs = string.Join(", ", MapPack.TrueRandomPacks(Maps.Packs).Select(x => x.Name));
            ImGuiSafe.TextWrapped($"Every match on a random map from any pack: {p.Pool.Length} maps from {packs}. "
                + "After a match, the vote offers maps from every pack too.");
            if (Maps.Packs.Any(x => !x.InTrueRandom && x.Maps.Count > 0))
                ImGuiSafe.TextColored(Dim, $"{string.Join(", ", Maps.Packs.Where(x => !x.InTrueRandom && x.Maps.Count > 0).Select(x => x.Name))} test maps are left out: pick them from their own pack.");
        }
        ImGui.PopStyleColor();
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        var listH = MathF.Max(80 * _scale, ImGui.GetContentRegionAvail().Y);
        if (ImGui.BeginChild($"##msentries{p.Key}", new float2(size.X, listH), ImGuiChildFlags.None))
        {
            var dl = ImGui.GetWindowDrawList();
            var clipMin = ImGui.GetWindowPos();
            var clipMax = clipMin + ImGui.GetWindowSize();
            var tw = ImGui.GetContentRegionAvail().X;
            var tileH = 96 * _scale;
            var currentEntry = CurrentEntry(s, p);
            for (var e = 0; e <= p.Maps.Length; e++)
            {
                var p0 = ImGui.GetCursorScreenPos();
                if (p0.Y + tileH < clipMin.Y || p0.Y > clipMax.Y)
                {
                    // Scrolled out of view: reserve the space only (no picture is asked for, so none is rendered yet).
                    ImGui.Dummy(new float2(tw, tileH));
                    if (e == _ms.Entry && _msScrollEntry)
                        ImGui.SetScrollHereY(0.5f);
                    ImGui.Dummy(new float2(tw, 4 * _scale));
                    continue;
                }
                var clicked = ImGui.InvisibleButton($"##msentry{e}", new float2(tw, tileH));
                var hot = ImGui.IsItemHovered();
                var dbl = hot && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && _msClickedEntry == e;
                if (e == _ms.Entry && _msScrollEntry)
                    ImGui.SetScrollHereY(0.5f);
                if (dbl)
                {
                    ChooseMap(s, audio, p, e);
                    break;
                }
                if (clicked)
                {
                    if (_ms.Entry != e)
                        audio.Effect(Sfx.HitTick, 0.3f);
                    _ms.Entry = e;
                    _msClickedEntry = e;
                }
                if (e == 0)
                    DrawRandomTile(dl, p, players, p0, new float2(tw, tileH), _ms.Entry == 0, hot, currentEntry == 0);
                else
                    DrawMapTile(dl, p.Maps[e - 1], players, p0, new float2(tw, tileH), _ms.Entry == e, hot, currentEntry == e);
                ImGui.Dummy(new float2(tw, 4 * _scale));
            }
        }
        ImGui.EndChild();
        _msScrollEntry = false;
        ImGui.EndChild();
    }

    /// <summary>The entry of <paramref name="p"/> the saved settings point at (-1: the setting is in another pack).</summary>
    private static int CurrentEntry(Settings s, MapSelectPack p)
    {
        if (s.MapRandom)
            return p.Key.Equals(s.MapPack.Trim(), StringComparison.OrdinalIgnoreCase) ? 0 : -1;
        var i = Array.FindIndex(p.Maps, m => m.Name.Equals(s.MapName, StringComparison.OrdinalIgnoreCase));
        return i >= 0 ? i + 1 : -1;
    }

    /// <summary>Tile background and outline in the weapon-select style: hover orange, selected blue, current choice an orange bar.</summary>
    private void TileFrame(ImDrawListPtr dl, float2 p0, float2 p1, bool selected, bool hot, bool current)
    {
        var r = 4 * _scale;
        dl.AddRectFilled(p0, p1, selected ? hot ? new ImColor8(38, 54, 74, 245) : new ImColor8(30, 43, 60, 245) : hot ? Hover8 : Card8, r);
        if (current)
            dl.AddRectFilled(p0, new float2(p0.X + 4 * _scale, p1.Y), Orange8, r, ImDrawFlags.RoundCornersLeft);
        if (selected)
            dl.AddRect(p0, p1, Select8, r, ImDrawFlags.None, 2.2f * _scale);
        else if (hot)
            dl.AddRect(p0, p1, Orange8, r, ImDrawFlags.None, 1.5f * _scale);
    }

    /// <summary>A small tag ("CURRENT", "SELECTED") at <paramref name="at"/>.</summary>
    private void Tag(ImDrawListPtr dl, float2 at, ImString text, bool filled, float4 clip)
    {
        var ts = ImGui.CalcTextSize(text);
        var a = at - new float2(0, 2 * _scale);
        if (filled)
        {
            dl.AddRectFilled(a, a + new float2(ts.X + 12 * _scale, ts.Y + 4 * _scale), Orange8, 2 * _scale);
            DlText(dl, at + new float2(6 * _scale, 0), new ImColor8(22, 16, 8, 255), text, clip);
        }
        else
        {
            dl.AddRect(a, a + new float2(ts.X + 12 * _scale, ts.Y + 4 * _scale), Select8, 2 * _scale, ImDrawFlags.None, 1.2f * _scale);
            DlText(dl, at + new float2(6 * _scale, 0), Select8, text, clip);
        }
    }

    private void DrawPackTile(ImDrawListPtr dl, MapSelectPack p, float2 p0, float2 size, bool selected, bool hot, bool current)
    {
        var p1 = p0 + size;
        TileFrame(dl, p0, p1, selected, hot, current);
        var pad = 8 * _scale;
        var t0 = p0 + new float2(pad + 4 * _scale, pad);
        var picH = size.Y - 2 * pad;
        var t1 = new float2(MathF.Min(t0.X + picH * 16f / 10f, p0.X + size.X * 0.5f), p1.Y - pad);
        DrawCollage(dl, p.Collage, t0, t1);
        if (p.IsTrueRandom)
            DrawQuestionMark(dl, t0, t1);

        var x = t1.X + 10 * _scale;
        var clip = new float4(x, p0.Y, p1.X - 6 * _scale, p1.Y);
        ImGui.PushFont(default, 19);
        DlText(dl, new float2(x, p0.Y + 9 * _scale), selected || hot ? White8 : new ImColor8(220, 222, 228, 255), p.Name, clip);
        ImGui.PopFont();
        DlText(dl, new float2(x, p0.Y + 34 * _scale), p.IsTrueRandom ? Orange8 : White8, p.CountLine, clip);
        DlText(dl, new float2(x, p0.Y + 52 * _scale), Dim8, p.SizeLine, clip);
        if (current)
            Tag(dl, new float2(x, p1.Y - 24 * _scale), "CURRENT"u8, true, clip);
        else if (selected)
            Tag(dl, new float2(x, p1.Y - 24 * _scale), "OPEN"u8, false, clip);
    }

    private void DrawRandomTile(ImDrawListPtr dl, MapSelectPack p, int players, float2 p0, float2 size, bool selected, bool hot, bool current)
    {
        var p1 = p0 + size;
        TileFrame(dl, p0, p1, selected, hot, current);
        var pad = 8 * _scale;
        var t0 = p0 + new float2(pad + 4 * _scale, pad);
        var t1 = new float2(t0.X + (size.Y - 2 * pad) * 16f / 9f, p1.Y - pad);
        DrawCollage(dl, p.Collage, t0, t1);
        DrawQuestionMark(dl, t0, t1);

        var x = t1.X + 12 * _scale;
        var clip = new float4(x, p0.Y, p1.X - 6 * _scale, p1.Y);
        ImGui.PushFont(default, 19);
        DlText(dl, new float2(x, p0.Y + 8 * _scale), White8, p.IsTrueRandom ? "RANDOM FROM ANY PACK"u8 : "RANDOM"u8, clip);
        ImGui.PopFont();
        var fit = 0;
        foreach (var m in p.Pool)
            if (m.MaxPlayers >= players)
                fit++;
        DlText(dl, new float2(x, p0.Y + 33 * _scale), new ImColor8(200, 202, 210, 255),
            p.IsTrueRandom ? $"A different map from any pack each match ({p.Pool.Length} maps)" : $"A random one of the {p.Pool.Length} maps each match", clip);
        DlText(dl, new float2(x, p0.Y + 51 * _scale), fit > 0 ? Dim8 : Orange8,
            fit > 0 ? $"Picks among the {fit} that take {players} players (you + bots)" : $"None take {players} players: the biggest map is used", clip);
        if (current)
            Tag(dl, new float2(x, p1.Y - 24 * _scale), "CURRENT"u8, true, clip);
        else if (selected)
            Tag(dl, new float2(x, p1.Y - 24 * _scale), "SELECTED"u8, false, clip);
    }

    private void DrawMapTile(ImDrawListPtr dl, Map m, int players, float2 p0, float2 size, bool selected, bool hot, bool current)
    {
        var p1 = p0 + size;
        TileFrame(dl, p0, p1, selected, hot, current);
        var pad = 8 * _scale;
        var t0 = p0 + new float2(pad + 4 * _scale, pad);
        var t1 = new float2(t0.X + (size.Y - 2 * pad) * 16f / 9f, p1.Y - pad);
        DrawMapPicture(dl, m, t0, t1);

        var x = t1.X + 12 * _scale;
        var clip = new float4(x, p0.Y, p1.X - 6 * _scale, p1.Y);
        ImGui.PushFont(default, 19);
        DlText(dl, new float2(x, p0.Y + 8 * _scale), White8, m.Name, clip);
        ImGui.PopFont();
        var sizeName = m.Size switch
        {
            MapSize.Small => "Small",
            MapSize.Medium => "Medium",
            _ => "Large",
        };
        DlText(dl, new float2(x, p0.Y + 33 * _scale), m.MaxPlayers >= players ? new ImColor8(200, 202, 210, 255) : Orange8,
            $"{sizeName}, {m.SizeLabel}, up to {m.MaxPlayers} players{(m.MaxPlayers < players ? " (bots are cut to fit)" : "")}", clip);
        var tagW = current || selected ? 100 * _scale : 0;
        var descClip = new float4(x, p0.Y, p1.X - 6 * _scale - tagW, p1.Y - 4 * _scale);
        var desc = m.Author.Length > 0 ? $"{m.Description} By {m.Author}." : m.Description;
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new float2(x, p0.Y + 51 * _scale), Dim8, desc, MathF.Max(40, p1.X - 6 * _scale - tagW - x), descClip);
        var tagAt = new float2(p1.X - 6 * _scale - tagW + 8 * _scale, p0.Y + 9 * _scale);
        if (current)
            Tag(dl, tagAt, "CURRENT"u8, true, clip);
        else if (selected)
            Tag(dl, tagAt, "SELECTED"u8, false, clip);
    }

    /// <summary>Up to four map pictures in one rectangle: one fills it, two or three side by side, four in a 2x2 grid.</summary>
    private void DrawCollage(ImDrawListPtr dl, Map[] maps, float2 p0, float2 p1)
    {
        dl.AddRectFilled(p0, p1, new ImColor8(11, 12, 15, 255), 3 * _scale);
        if (maps.Length == 0)
            return;
        var g = MathF.Max(1f, 2 * _scale);
        var mid = (p0 + p1) / 2;
        if (maps.Length == 1)
            DrawMapPicture(dl, maps[0], p0, p1);
        else if (maps.Length < 4)
        {
            DrawMapPicture(dl, maps[0], p0, new float2(mid.X - g / 2, p1.Y));
            DrawMapPicture(dl, maps[1], new float2(mid.X + g / 2, p0.Y), p1);
        }
        else
        {
            DrawMapPicture(dl, maps[0], p0, mid - new float2(g / 2, g / 2));
            DrawMapPicture(dl, maps[1], new float2(mid.X + g / 2, p0.Y), new float2(p1.X, mid.Y - g / 2));
            DrawMapPicture(dl, maps[2], new float2(p0.X, mid.Y + g / 2), new float2(mid.X - g / 2, p1.Y));
            DrawMapPicture(dl, maps[3], mid + new float2(g / 2, g / 2), p1);
        }
    }

    /// <summary>
    /// One map's picture, cropped to fill the rectangle (thumbnails are 16:9). While it's being made, or if it couldn't be,
    /// a tile in the map's sky colour with a little hills-and-sun icon.
    /// </summary>
    private void DrawMapPicture(ImDrawListPtr dl, Map m, float2 p0, float2 p1)
    {
        var w = p1.X - p0.X;
        var h = p1.Y - p0.Y;
        if (w < 2 || h < 2)
            return;
        var tex = MapThumbs.Get(m, out var state);
        if (tex != 0)
        {
            // Crop: keep the centre of the 16:9 picture. GL textures start at the bottom row, so v runs top = 1 -> bottom = 0.
            var texAspect = MapThumbs.Width / (float)MapThumbs.Height;
            var aspect = w / h;
            float uSpan = 1, vSpan = 1;
            if (aspect > texAspect)
                vSpan = texAspect / aspect;
            else
                uSpan = aspect / texAspect;
            var uvMin = new float2(0.5f - uSpan / 2, 0.5f + vSpan / 2);
            var uvMax = new float2(0.5f + uSpan / 2, 0.5f - vSpan / 2);
            dl.AddImage(new ImTextureRef { _TexID = new ImTextureID((nint)tex) }, p0, p1, uvMin, uvMax);
            return;
        }
        static byte B(float v) => (byte)Math.Clamp((int)(v * 255), 0, 255);
        var sky = new ImColor8(B(m.Sky.X * 0.8f), B(m.Sky.Y * 0.8f), B(m.Sky.Z * 0.8f), 255);
        dl.AddRectFilled(p0, p1, sky);
        // Hills and a sun (the "no picture" icon), small and centred.
        var s = MathF.Min(w, h) * 0.5f;
        var c = new float2(p0.X + w / 2, p0.Y + h / 2 + s * 0.1f);
        var hill = state == MapThumbs.State.Failed ? new ImColor8(70, 74, 82, 255) : new ImColor8(60, 90, 60, 255);
        dl.AddTriangleFilled(c + new float2(-s * 0.6f, s * 0.35f), c + new float2(-s * 0.15f, -s * 0.3f), c + new float2(s * 0.3f, s * 0.35f), hill);
        dl.AddTriangleFilled(c + new float2(-s * 0.05f, s * 0.35f), c + new float2(s * 0.3f, -s * 0.1f), c + new float2(s * 0.65f, s * 0.35f), hill);
        dl.AddCircleFilled(c + new float2(s * 0.35f, -s * 0.35f), s * 0.11f, new ImColor8(240, 200, 90, 220), 12);
        if (state == MapThumbs.State.Pending)
        {
            // Being drawn: three dots that pulse (one picture is made per frame, so this rarely shows for long).
            var t = (float)ImGui.GetTime();
            for (var i = 0; i < 3; i++)
            {
                var a = (byte)(110 + 120 * MathF.Max(0, MathF.Sin(t * 6 - i * 0.9f)));
                dl.AddCircleFilled(new float2(p0.X + w / 2 + (i - 1) * 7 * _scale, p1.Y - 8 * _scale), 2.2f * _scale, new ImColor8(255, 255, 255, a), 8);
            }
        }
    }

    /// <summary>A dark veil with a big "?" over a picture (the Random entries and the True Random tile).</summary>
    private void DrawQuestionMark(ImDrawListPtr dl, float2 p0, float2 p1)
    {
        dl.AddRectFilled(p0, p1, new ImColor8(8, 9, 12, 120), 3 * _scale);
        var h = p1.Y - p0.Y;
        var size = h * 0.62f;
        var ts = ImGui.CalcTextSize("?"u8) * (size / ImGui.GetFontSize());
        var at = new float2((p0.X + p1.X - ts.X) / 2, (p0.Y + p1.Y - ts.Y) / 2);
        dl.AddText(ImGui.GetFont(), size, at + new float2(2, 2) * _scale, new ImColor8(0, 0, 0, 160), "?"u8, 0f, null);
        dl.AddText(ImGui.GetFont(), size, at, Orange8, "?"u8, 0f, null);
    }
}
