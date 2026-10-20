using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>Where the gunsmith wants the 3D gun drawn this frame (ImGui pixel coordinates). Program renders it behind the UI.</summary>
public readonly record struct GunsmithPreview(WeaponDef Def, float2 WinMin, float2 WinMax, float2 ViewMin, float2 ViewMax);

/// <summary>
/// The attachment "showcase": the selected gun rendered large in the middle, one card per attachment
/// slot arranged around it (click a card to pick from that slot), and an "equipped" summary with stat
/// bars that show what the attachments change, green for better and red for worse.
/// </summary>
public sealed partial class Ui
{
    public GunsmithPreview? Preview;
    private bool _showcaseLastFrame;
    private AttachSlot? _openSlot;
    private AttachSlot? _hoverCard;

    private static readonly float4 SlotBlue = new(0.45f, 0.75f, 0.95f, 1f);
    private static readonly float4 CardBg = new(0.09f, 0.1f, 0.12f, 0.92f);
    private static readonly float4 CardHover = new(0.15f, 0.17f, 0.21f, 0.95f);

    // Card layout: (slot, x, y) as fractions of the showcase area. Top row above the gun, two below.
    private static readonly (AttachSlot Slot, float X, float Y)[] Cards =
    [
        (AttachSlot.Muzzle, 0.0f, 0.035f),
        (AttachSlot.Barrel, 0.255f, 0.0f),
        (AttachSlot.Underbarrel, 0.51f, 0.0f),
        (AttachSlot.Optic, 0.765f, 0.035f),
        (AttachSlot.Magazine, 0.40f, 0.81f),
        (AttachSlot.Stock, 0.765f, 0.81f),
    ];

    private void DrawShowcase(Audio audio, WeaponDef w, float2 area, float2 winPos, float2 winSize)
    {
        var prog = Progress;
        var origin = ImGui.GetCursorScreenPos();
        var kit = KitOf(w);
        var options = Attachments.For(w);
        var wxp = prog.WeaponXp(w.Id);

        // What the gun looks like right now (with the hovered attachment previewed on it).
        var previewKit = kit;
        if (_hoverAttachment is { } ha && options.Contains(ha))
            previewKit = Attachments.With(kit, ha.Slot, ha.Index);
        var current = Attachments.Apply(w, kit);
        var preview = Attachments.Apply(w, previewKit);

        Preview = new GunsmithPreview(preview, winPos, winPos + winSize,
            origin + new float2(area.X * 0.2f, area.Y * 0.17f), origin + new float2(area.X * 0.96f, area.Y * 0.78f));

        // ---- Title: weapon name and its XP, under the top cards ---------------------------
        var dl = ImGui.GetWindowDrawList();
        var titlePos = origin + new float2(area.X * 0.02f, area.Y * 0.2f);
        ImGui.SetCursorScreenPos(titlePos);
        ImGui.PushFont(default, 30);
        ImGuiSafe.TextColored(Orange, w.Name.ToUpperInvariant());
        ImGui.PopFont();
        ImGui.SetCursorScreenPos(titlePos + new float2(0, 36 * _scale));
        ImGuiSafe.TextColored(Dim, $"{w.ClassName}  -  {w.ModeName}");
        if (options.Count > 0)
        {
            var maxXp = Attachments.MaxXp(w);
            ImGui.SetCursorScreenPos(titlePos + new float2(0, 58 * _scale));
            ImGuiSafe.TextColored(Dim, $"Weapon XP {Math.Min(wxp, maxXp):N0} / {maxXp:N0}");
            ImGui.SetCursorScreenPos(titlePos + new float2(0, 78 * _scale));
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new float4(0.4f, 0.95f, 1f, 1f));
            ImGui.ProgressBar(Math.Clamp(wxp / (float)maxXp, 0f, 1f), new float2(area.X * 0.17f, 6 * _scale), ""u8);
            ImGui.PopStyleColor();
        }
        if (!InEdit(w))
        {
            // Opened from the weapon select on a gun that isn't equipped: a preview until you change something.
            ImGui.SetCursorScreenPos(titlePos + new float2(0, 92 * _scale));
            ImGui.PushTextWrapPos(titlePos.X - ImGui.GetWindowPos().X + area.X * 0.19f);
            ImGuiSafe.TextColored(Orange, $"Preview - picking an attachment equips this gun in {Edit.Name}.");
            ImGui.PopTextWrapPos();
        }

        // ---- Slot cards ------------------------------------------------------------------
        var cardSize = new float2(area.X * 0.235f, 62 * _scale);
        AttachSlot? hoveredCard = null;
        foreach (var (slot, fx, fy) in Cards)
        {
            var inSlot = options.Where(a => a.Slot == slot).ToArray();
            var equipped = Attachments.Get(slot, Attachments.Slot(kit, slot));
            var pos = origin + new float2(area.X * fx, area.Y * fy);
            ImGui.SetCursorScreenPos(pos);
            var hot = _hoverCard == slot || _openSlot == slot;
            ImGui.PushStyleColor(ImGuiCol.ChildBg, hot && inSlot.Length > 0 ? CardHover : CardBg);
            if (ImGui.BeginChild($"##card{(int)slot}", cardSize, ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            {
                var unlocked = inSlot.Count(a => wxp >= Attachments.UnlockXp(w, a));
                ImGuiSafe.TextColored(inSlot.Length > 0 ? SlotBlue : Dim, Attachments.SlotName(slot));
                if (inSlot.Length > 0)
                {
                    ImGui.SameLine();
                    ImGuiSafe.TextColored(Dim, $"  {unlocked}/{inSlot.Length}");
                }
                ImGui.Text(inSlot.Length == 0 ? "-" : equipped?.Name ?? (slot == AttachSlot.Optic ? "Iron sights" : "Empty"));
                // Orange dot in the corner when something is fitted
                if (equipped != null)
                {
                    var c = ImGui.GetWindowPos() + new float2(cardSize.X - 14 * _scale, 14 * _scale);
                    ImDrawListExtensions.AddCircleFilled(ImGui.GetWindowDrawList(), in c, 6 * _scale, new ImColor8(240, 150, 40, 255), 16);
                    var c2 = c;
                    ImDrawListExtensions.AddCircle(ImGui.GetWindowDrawList(), in c2, 8 * _scale, new ImColor8(240, 150, 40, 180), 16, 1.5f);
                }
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None) && inSlot.Length > 0)
                hoveredCard = slot;
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && inSlot.Length > 0 && !SecondClick)
            {
                _openSlot = slot;
                ImGui.OpenPopup("##attpick"u8, ImGuiPopupFlags.None);
                _pickerPos = pos + new float2(0, cardSize.Y + 4 * _scale);
                if (_pickerPos.Y + 320 * _scale > winPos.Y + winSize.Y)
                    _pickerPos = pos - new float2(0, 330 * _scale); // bottom cards open upwards
            }
        }
        _hoverCard = hoveredCard;

        // ---- Equipped summary + stat bars (bottom left) ------------------------------------
        var statsPos = origin + new float2(0, area.Y * 0.6f);
        ImGui.SetCursorScreenPos(statsPos);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, CardBg);
        if (ImGui.BeginChild("##equipped"u8, new float2(area.X * 0.36f, area.Y * 0.4f), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar))
        {
            var fitted = Attachments.InKit(kit).Count();
            var slots = Enumerable.Range(0, Attachments.SlotCount).Count(sl => options.Any(a => (int)a.Slot == sl));
            ImGuiSafe.TextColored(SlotBlue, $"Equipped {fitted}/{slots}:");
            var dots = ImGui.GetWindowDrawList();
            var basePos = ImGui.GetItemRectMax() + new float2(10 * _scale, -9 * _scale);
            for (var i = 0; i < slots; i++)
            {
                var c = basePos + new float2(i * 20 * _scale, 0);
                if (i < fitted)
                    ImDrawListExtensions.AddCircleFilled(dots, in c, 6 * _scale, new ImColor8(240, 150, 40, 255), 16);
                var c2 = c;
                ImDrawListExtensions.AddCircle(dots, in c2, 8 * _scale, new ImColor8(240, 150, 40, i < fitted ? (byte)200 : (byte)90), 16, 1.5f);
            }
            ImGui.Spacing();
            // Compare against the stock gun normally, or against your current build while hovering a pick.
            var reference = _hoverAttachment != null ? current : w;
            StatRow("Accuracy", StatBars.Accuracy(reference), StatBars.Accuracy(preview), StatBars.Change(reference.HipSpreadDeg, preview.HipSpreadDeg));
            StatRow("Range", StatBars.Range(reference), StatBars.Range(preview), StatBars.Change(reference.RangeM, preview.RangeM));
            StatRow("Fire rate", StatBars.FireRate(reference), StatBars.FireRate(preview), StatBars.Change(reference.Rpm, preview.Rpm));
            StatRow("Mobility", StatBars.Mobility(reference), StatBars.Mobility(preview), StatBars.Change(reference.Mobility, preview.Mobility));
            StatRow("Control", StatBars.Control(reference), StatBars.Control(preview), StatBars.Change(Recoil.EffectiveKickDeg(reference), Recoil.EffectiveKickDeg(preview)));
            StatRow("Aim speed", StatBars.AimSpeed(reference), StatBars.AimSpeed(preview), StatBars.Change(reference.AdsTime, preview.AdsTime));
            StatRow("Magazine", StatBars.Magazine(reference), StatBars.Magazine(preview), StatBars.Change(reference.Mag, preview.Mag));
            StatRow("Reload", StatBars.Reload(reference), StatBars.Reload(preview), StatBars.Change(reference.Reload, preview.Reload));
            ImGuiSafe.TextColored(Dim, $"{preview.Mag} rounds, {preview.RangeM:0} m range, {preview.AdsTime:0.00} s to aim, {preview.AdsZoom:0.0}x");
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();

        if (options.Count == 0)
        {
            ImGui.SetCursorScreenPos(origin + new float2(area.X * 0.45f, area.Y * 0.45f));
            ImGui.TextColored(Dim, "This weapon takes no attachments."u8);
        }

        DrawAttachmentPicker(audio, w, kit, wxp);
    }

    private float2 _pickerPos;

    /// <summary>The list that opens under a slot card: Empty, then each attachment (locked ones greyed with their XP).</summary>
    private void DrawAttachmentPicker(Audio audio, WeaponDef w, uint kit, int wxp)
    {
        AttachmentDef? hovered = null;
        ImGui.SetNextWindowPos(_pickerPos, ImGuiCond.Appearing, new float2(0, 0));
        ImGui.SetNextWindowSize(new float2(420 * _scale, 0));
        if (_openSlot is { } slot && ImGui.BeginPopup("##attpick"u8, ImGuiWindowFlags.None))
        {
            ImGuiSafe.TextColored(SlotBlue, Attachments.SlotName(slot).ToUpperInvariant());
            ImGui.Separator();
            var current = Attachments.Slot(kit, slot);
            // A click picks and closes the list; a double-click's second click is swallowed (see SecondClick).
            if (ImGui.Selectable(slot == AttachSlot.Optic ? "Iron sights##none"u8 : "Empty##none"u8, current == 0))
            {
                SetKitOf(audio, w, Attachments.With(kit, slot, 0));
                Acted();
                ImGui.CloseCurrentPopup();
            }
            foreach (var a in Attachments.For(w).Where(a => a.Slot == slot).OrderBy(a => Attachments.UnlockXp(w, a)))
            {
                var need = Attachments.UnlockXp(w, a);
                var unlocked = wxp >= need;
                if (!unlocked)
                    ImGui.PushStyleColor(ImGuiCol.Text, Dim);
                if (ImGui.Selectable(unlocked ? $"{a.Name}##p{a.Index}" : $"{a.Name}   [LOCKED - {need:N0} weapon XP]##p{a.Index}", current == a.Index,
                        unlocked ? ImGuiSelectableFlags.None : ImGuiSelectableFlags.NoAutoClosePopups))
                {
                    if (!unlocked)
                    {
                        LockedFeedback(audio);
                    }
                    else
                    {
                        SetKitOf(audio, w, Attachments.With(kit, slot, a.Index));
                        audio.Effect(Sfx.HitTick, 0.4f);
                        Acted();
                        ImGui.CloseCurrentPopup();
                    }
                }
                if (!unlocked)
                    ImGui.PopStyleColor();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                    hovered = a;
            }
            // Details of whatever is under the mouse
            var show = hovered ?? Attachments.Get(slot, current);
            if (show != null)
            {
                ImGui.Separator();
                ImGuiSafe.TextWrapped(show.Blurb);
                foreach (var pro in show.Pros)
                    ImGuiSafe.TextColored(Green, $"+ {pro}");
                foreach (var con in show.Cons)
                    ImGuiSafe.TextColored(Red, $"- {con}");
            }
            ImGui.EndPopup();
        }
        else
        {
            _openSlot = null;
        }
        _hoverAttachment = hovered;
    }

    /// <summary>A stat bar: white up to the smaller value, then green (better) or red (worse) for the change.</summary>
    /// <summary>One stat bar: the common part in grey, a gain in green or a loss in red, and the change in the stat itself as a percentage.</summary>
    private void StatRow(string label, float before, float after, string change = "")
    {
        before = Math.Clamp(before, 0.02f, 1f);
        after = Math.Clamp(after, 0.02f, 1f);
        ImGuiSafe.TextColored(Dim, label);
        ImGui.SameLine(100 * _scale);
        var p0 = ImGui.GetCursorScreenPos() + new float2(0, 5 * _scale);
        var width = ImGui.GetContentRegionAvail().X - 50 * _scale; // room for the "+25%" on the right
        var h = 5 * _scale;
        var dl = ImGui.GetWindowDrawList();
        var bgMax = p0 + new float2(width, h);
        ImDrawListExtensions.AddRectFilled(dl, in p0, in bgMax, new ImColor8(60, 64, 70, 255), 0, default);
        var common = MathF.Min(before, after);
        var cMax = p0 + new float2(width * common, h);
        ImDrawListExtensions.AddRectFilled(dl, in p0, in cMax, new ImColor8(200, 205, 210, 255), 0, default);
        if (MathF.Abs(after - before) > 0.002f)
        {
            var a = p0 + new float2(width * common, 0);
            var b = p0 + new float2(width * MathF.Max(before, after), h);
            var col = after > before ? new ImColor8(90, 220, 110, 255) : new ImColor8(230, 70, 60, 255);
            ImDrawListExtensions.AddRectFilled(dl, in a, in b, col, 0, default);
        }
        ImGui.Dummy(new float2(width, 14 * _scale));
        if (change.Length > 0)
        {
            ImGui.SameLine(0, 6 * _scale);
            ImGuiSafe.TextColored(after >= before ? new float4(0.35f, 0.86f, 0.43f, 1f) : new float4(0.9f, 0.27f, 0.24f, 1f), change);
        }
    }
}
