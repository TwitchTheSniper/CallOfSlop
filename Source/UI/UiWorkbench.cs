using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>What the workbench asks Program to start: a Workbench Range session with these two built weapons.</summary>
public sealed record WorkbenchLaunch(WeaponDef Primary, WeaponDef Secondary);

/// <summary>
/// The Weapon Workbench (Zombies preview, docs/zombies/WEAPON-BUILDER.md): every part (sandbox: all unlocked) on the left,
/// the live 3D build in the middle with its part tree under it (the grip's receiver slots first), stats with deltas and
/// problems on the right, four save slots (3-4 "unlockable in Zombies") and Test fire, which opens the Workbench Range
/// with the selected build and the second slot as your two weapons. Click a socket, then a part (or a part, then a socket).
/// </summary>
public sealed partial class Ui
{
    /// <summary>The workbench screen is up (opened from the main menu).</summary>
    public bool WorkbenchOpen;

    private WeaponBuild? _wb;
    private AssembledWeapon? _wbAsm;
    private PartCategory _wbCat = PartCategory.Grip;
    private PartDef? _wbPart;
    private string? _wbSocketParent, _wbSocketName;
    private string _wbMessage = "";
    private ImInputString? _wbNameInput;
    private (string Key, AssembledWeapon? Asm) _wbTrial;

    private static readonly float4 Yellow = new(1f, 0.85f, 0.3f, 1f);

    public void OpenWorkbench()
    {
        BuildStore.EnsureLoaded();
        WorkbenchOpen = true;
        LoadWorkbenchSlot(BuildStore.Selected);
    }

    /// <summary>Smoke test: shows <paramref name="b"/> on the workbench (not saved).</summary>
    public void SmokeWorkbenchBuild(WeaponBuild b)
    {
        _wb = b.Clone();
        _wbNameInput?.SetValue(_wb.Name.AsSpan());
        WorkbenchChanged();
    }

    private void LoadWorkbenchSlot(int slot)
    {
        BuildStore.Selected = slot;
        _wb = BuildStore.Slots[slot]?.Clone() ?? new WeaponBuild($"Build {slot + 1}", Parts.InCategory(PartCategory.Grip).First(p => p.IsRoot));
        _wbNameInput ??= new ImInputString(33);
        _wbNameInput.SetValue(_wb.Name.AsSpan());
        _wbSocketParent = _wbSocketName = null;
        WorkbenchChanged();
    }

    private void WorkbenchChanged()
    {
        _wbAsm = _wb != null ? WeaponBuilder.Assemble(_wb) : null;
        _wbTrial = ("", null);
    }

    private static float4 WbCol(Vector3 c) => new(c.X, c.Y, c.Z, 1f);

    private static string SocketLabel(PartSocket s) =>
        s.Name.Length == 0 ? s.Type : char.ToUpperInvariant(s.Name[0]) + s.Name[1..].Replace('_', ' ');

    /// <summary>The build with <paramref name="part"/> placed in the selected socket (or as a new root), for the stat deltas.</summary>
    private AssembledWeapon? Trial(PartDef part)
    {
        if (_wb == null)
            return null;
        var key = $"{part.Name}|{_wbSocketParent}|{_wbSocketName}";
        if (_wbTrial.Key == key)
            return _wbTrial.Asm;
        AssembledWeapon? asm = null;
        if (part.IsRoot)
            asm = WeaponBuilder.Assemble(Regrip(_wb, part));
        else if (_wbSocketParent != null && _wbSocketName != null && BuildRules.WhyNot(_wb, _wbSocketParent, _wbSocketName, part) == null)
        {
            var t = _wb.Clone();
            t.At(_wbSocketParent)!.Attach(_wbSocketName, part);
            asm = WeaponBuilder.Assemble(t);
        }
        _wbTrial = (key, asm);
        return asm;
    }

    /// <summary>The build moved onto another grip: receivers go into the same-numbered slots, the rest by socket name where it fits.</summary>
    private static WeaponBuild Regrip(WeaponBuild b, PartDef grip)
    {
        var nb = new WeaponBuild(b.Name, grip) { Style = grip.Frame!.FireStyles.Contains(b.Style) ? b.Style : grip.Frame.FireStyles[0] };
        if (b.Root != null)
            foreach (var (s, c) in b.Root.Children())
                if (grip.Sockets.FirstOrDefault(x => x.Name == s.Name && x.Type == s.Type) is { } ns)
                    nb.Root!.Attach(ns.Name, c.Clone());
        return nb;
    }

    /// <summary>Draws the workbench. Returns a launch request when Test fire is clicked.</summary>
    public WorkbenchLaunch? DrawWorkbench(Audio audio, double now)
    {
        if (_wb == null)
            OpenWorkbench();
        var b = _wb!;
        WorkbenchLaunch? launch = null;
        _loadoutDrawn = true; // keeps the 3D preview alive (see EndFrame)
        Preview = null;
        var vp = ImGui.GetMainViewport();
        var size = new float2(MathF.Min(1500 * _scale, vp.WorkSize.X - 30), MathF.Min(860 * _scale, vp.WorkSize.Y - 30));
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + vp.WorkSize.X / 2, vp.WorkPos.Y + vp.WorkSize.Y / 2), ImGuiCond.Always, new float2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(size);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings
                                       | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar;
        if (ImGui.Begin("WEAPON WORKBENCH (ZOMBIES PREVIEW)"u8, flags))
        {
            var winPos = ImGui.GetWindowPos();
            var winSize = ImGui.GetWindowSize();
            var avail = ImGui.GetContentRegionAvail();
            var leftW = avail.X * 0.25f;
            var rightW = avail.X * 0.27f;
            var midW = avail.X - leftW - rightW - 16 * _scale;
            var bodyH = avail.Y - 44 * _scale;

            // ---- Left: the parts ----
            if (ImGui.BeginChild("##wbparts"u8, new float2(leftW, bodyH), ImGuiChildFlags.Borders))
                DrawWorkbenchParts(b);
            ImGui.EndChild();
            ImGui.SameLine();

            // ---- Middle: preview on top, the part tree below ----
            if (ImGui.BeginChild("##wbmid"u8, new float2(midW, bodyH), ImGuiChildFlags.None))
            {
                var origin = ImGui.GetCursorScreenPos();
                var viewH = bodyH * 0.5f;
                if (_wbAsm?.PreviewDef is { } shown)
                {
                    var hovered = _wbPart != null ? Trial(_wbPart)?.PreviewDef : null;
                    Preview = new GunsmithPreview(hovered ?? shown, winPos, winPos + winSize, origin, origin + new float2(midW, viewH));
                }
                ImGui.Dummy(new float2(midW, viewH));
                ImGui.Separator();
                if (ImGui.BeginChild("##wbtree"u8, new float2(midW, 0), ImGuiChildFlags.Borders))
                    DrawWorkbenchTree(b);
                ImGui.EndChild();
            }
            ImGui.EndChild();
            ImGui.SameLine();

            // ---- Right: stats, problems, slots ----
            if (ImGui.BeginChild("##wbstats"u8, new float2(rightW, bodyH), ImGuiChildFlags.Borders))
                launch = DrawWorkbenchSide(b);
            ImGui.EndChild();

            // ---- Footer ----
            if (ImGui.Button("BACK"u8, new float2(160 * _scale, 34 * _scale)) || ImGui.IsKeyPressed(ImGuiKey.Escape, false))
                WorkbenchOpen = false;
            ImGui.SameLine();
            ImGuiSafe.TextColored(Dim, _wbMessage.Length > 0 ? _wbMessage : "Click a socket in the tree, then a part (or a part, then a socket). Sandbox: every part is unlocked.");
        }
        ImGui.End();
        return launch;
    }

    private void DrawWorkbenchParts(WeaponBuild b)
    {
        ImGui.TextColored(Orange, "PARTS"u8);
        foreach (var c in Enum.GetValues<PartCategory>())
        {
            var n = Parts.InCategory(c).Count();
            if (n == 0)
                continue;
            var on = c == _wbCat;
            if (on)
                ImGui.PushStyleColor(ImGuiCol.Button, new float4(0.3f, 0.22f, 0.1f, 1f));
            if (ImGui.Button($"{PartDef.CategoryName(c)} ({n})##cat{(int)c}"))
                _wbCat = c;
            if (on)
                ImGui.PopStyleColor();
            if ((int)c % 3 != 2)
                ImGui.SameLine();
        }
        ImGui.NewLine();
        ImGui.Separator();
        var socketParent = _wbSocketParent != null ? b.At(_wbSocketParent) : null;
        foreach (var p in Parts.InCategory(_wbCat))
        {
            string? why = null;
            if (!p.IsRoot && socketParent != null && _wbSocketName != null)
                why = BuildRules.WhyNot(b, _wbSocketParent!, _wbSocketName, p);
            ImGui.PushStyleColor(ImGuiCol.Text, why != null ? new float4(0.5f, 0.5f, 0.52f, 1f) : WbCol(PartDef.RarityColor(p.Rarity)));
            var clicked = ImGui.Selectable($"{p.Name}  [{PartDef.RarityName(p.Rarity)}]##part{p.Name}", _wbPart == p);
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
            {
                var tip = $"{p.Name} - {PartDef.RarityName(p.Rarity)} {PartDef.CategoryName(p.Category)}\n{p.Flavor}\n{PartSummary(p)}";
                if (why != null)
                    tip += $"\nDoesn't fit the selected socket: {why}";
                ImGuiSafe.SetTooltip(tip);
            }
            if (!clicked)
                continue;
            _wbPart = p;
            if (p.IsRoot)
            {
                _wb = Regrip(b, p);
                _wbMessage = $"Moved the build onto the {p.Name} ({p.ReceiverCapacity} receiver slots).";
                WorkbenchChanged();
            }
            else if (socketParent != null && _wbSocketName != null)
                Place(b, p);
        }
    }

    private void Place(WeaponBuild b, PartDef p)
    {
        if (_wbSocketParent == null || _wbSocketName == null)
            return;
        var why = BuildRules.WhyNot(b, _wbSocketParent, _wbSocketName, p);
        if (why != null)
        {
            _wbMessage = why;
            return;
        }
        b.At(_wbSocketParent)!.Attach(_wbSocketName, p);
        _wbMessage = $"Fitted the {p.Name}.";
        WorkbenchChanged();
    }

    /// <summary>One line on what a part is and does (bores, weight, stat changes).</summary>
    private static string PartSummary(PartDef p)
    {
        var bits = new List<string>();
        if (p.Frame is { } f)
            bits.Add($"Holds {p.ReceiverCapacity} receivers, fires {string.Join("/", f.FireStyles.Select(x => x.ToString().ToLowerInvariant()))}, carries {f.Carry:0} kg{(f.Ads ? "" : ", hip fire only")}");
        if (p.Receiver is { } r)
            bits.Add($"{r.Action.ToString().ToLowerInvariant()} action, {r.Bore} rounds, {(r.RpmPerBarrel > 0 ? $"{r.RpmPerBarrel} rpm per barrel" : $"{r.Rpm} rpm")}");
        if (p.Split is { } s)
            bits.Add($"{p.Sockets.Count(x => x.Type == "barrel")} outputs at {s.OutputMult * 100:0}% damage each");
        if (p.Bores.Length > 0)
            bits.Add("for " + string.Join(", ", p.Bores) + " rounds");
        if (p.RequiresAction.Length > 0)
            bits.Add("needs " + string.Join("/", p.RequiresAction.Select(a => a.ToString().ToLowerInvariant())) + " action");
        bits.Add($"{p.Weight:0.0#} kg, {p.Scrap} scrap");
        foreach (var e in p.Effects)
            bits.Add(e.Op switch
            {
                "set" => $"{WeaponFile.Key(e.Field)} {e.Value}",
                "mul" => $"{WeaponFile.Key(e.Field)} x{e.Value}",
                "grow" => $"{WeaponFile.Key(e.Field)} x{e.Value}",
                _ => $"{WeaponFile.Key(e.Field)} {((float)e.Value >= 0 ? "+" : "")}{e.Value}",
            });
        if (p.Flags.Length > 0)
            bits.Add("flags: " + string.Join(", ", p.Flags));
        return string.Join("; ", bits);
    }

    private void DrawWorkbenchTree(WeaponBuild b)
    {
        if (b.Root == null)
            return;
        var grip = b.Root.Part;
        ImGui.PushStyleColor(ImGuiCol.Text, WbCol(PartDef.RarityColor(grip.Rarity)));
        ImGui.Text(grip.Name);
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGuiSafe.TextColored(Dim, $"  receiver slots {b.Receivers.Count}/{grip.ReceiverCapacity}   (pick another grip from the Grip list to move the build)");
        DrawTreeNode(b, b.Root, 1);
    }

    private void DrawTreeNode(WeaponBuild b, BuildNode node, int depth)
    {
        var path = node.Path();
        foreach (var s in node.Part.Sockets)
        {
            var child = node.Child(s.Name);
            var selected = _wbSocketParent == path && _wbSocketName == s.Name;
            var need = s.Required || (node.Part.Category == PartCategory.Splitter && s.Type == "barrel");
            var label = $"{new string(' ', depth * 3)}{SocketLabel(s)}: {(child != null ? child.Part.Name : need ? "(empty - needed)" : "(empty)")}##{path}/{s.Name}";
            ImGui.PushStyleColor(ImGuiCol.Text, child != null ? WbCol(PartDef.RarityColor(child.Part.Rarity)) : need ? Yellow : Dim);
            if (ImGui.Selectable(label, selected))
            {
                _wbSocketParent = path;
                _wbSocketName = s.Name;
                _wbCat = s.Accepts.Length > 0 ? s.Accepts[0] : CategoryFor(s.Type);
                if (_wbPart != null && !_wbPart.IsRoot && _wbPart.Mount == s.Type)
                    Place(b, _wbPart);
            }
            ImGui.PopStyleColor();
            if (child != null)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton($"remove##rm{path}/{s.Name}"))
                {
                    child.Detach();
                    _wbMessage = $"Took off the {child.Part.Name}.";
                    WorkbenchChanged();
                    return;
                }
                DrawTreeNode(b, child, depth + 1);
            }
        }
    }

    private static PartCategory CategoryFor(string socketType) => socketType switch
    {
        "receiver" => PartCategory.Receiver,
        "barrel" => PartCategory.Barrel,
        "feed" => PartCategory.Feed,
        "ammo" => PartCategory.Ammo,
        "stock" => PartCategory.Stock,
        "foregrip" => PartCategory.Grip,
        "optic" => PartCategory.Optic,
        "muzzle" => PartCategory.Muzzle,
        _ => PartCategory.Utility,
    };

    private void StatLine(string name, float now, float? then, string fmt, bool higherIsBetter)
    {
        ImGui.Text(name);
        ImGui.SameLine(130 * _scale);
        ImGui.Text(now.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture));
        if (then is { } t && MathF.Abs(t - now) > MathF.Max(1e-3f, MathF.Abs(now) * 0.005f))
        {
            ImGui.SameLine(210 * _scale);
            var better = higherIsBetter ? t > now : t < now;
            ImGuiSafe.TextColored(better ? Green : Red, $"-> {t.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture)} ({StatBars.Change(MathF.Max(now, 1e-3f), t)})");
        }
    }

    private WorkbenchLaunch? DrawWorkbenchSide(WeaponBuild b)
    {
        WorkbenchLaunch? launch = null;
        ImGui.TextColored(Orange, "BUILD"u8);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##wbname"u8, _wbNameInput!))
        {
            b.Name = WeaponBuild.CleanName(_wbNameInput!.ToString());
            WorkbenchChanged();
        }
        if (b.Frame is { FireStyles.Length: > 1 } frame)
        {
            ImGui.Text("Fire style"u8);
            foreach (var st in frame.FireStyles)
            {
                ImGui.SameLine();
                if (ImGui.RadioButton($"{st}##style{st}", b.Style == st))
                {
                    b.Style = st;
                    WorkbenchChanged();
                }
            }
            ImGuiSafe.TextColored(Dim, b.EffectiveStyle == FireStyle.Volley ? "Volley: every receiver fires on each pull." : "Rotate: the receivers take turns.");
        }
        ImGui.Separator();

        var cur = _wbAsm?.Def;
        var trial = _wbPart != null ? Trial(_wbPart)?.Def : null;
        if (cur != null)
        {
            ImGuiSafe.TextColored(Dim, $"{cur.ClassName} - {cur.ModeName} - {b.Receivers.Count} receiver(s), {cur.BarrelCount} barrel(s)");
            StatLine("Damage / shot", cur.ShotDamage, trial?.ShotDamage, "0", true);
            StatLine("DPS", cur.Dps, trial?.Dps, "0", true);
            StatLine("Fire rate", cur.Rpm, trial?.Rpm, "0", true);
            StatLine("Magazine", cur.Mag, trial?.Mag, "0", true);
            StatLine("Reload (s)", cur.Reload, trial?.Reload, "0.0", false);
            StatLine("Hip spread", cur.HipSpreadDeg, trial?.HipSpreadDeg, "0.00", false);
            StatLine("Range (m)", cur.RangeM, trial?.RangeM, "0", true);
            StatLine("Recoil", Recoil.EffectiveKickDeg(cur), trial != null ? Recoil.EffectiveKickDeg(trial) : null, "0.00", false);
            StatLine("Aim time (s)", cur.AdsTime, trial?.AdsTime, "0.00", false);
            StatLine("Mobility", cur.Mobility, trial?.Mobility, "0.00", true);
            if (cur.SpoolTime > 0)
                ImGuiSafe.TextColored(Dim, $"Spins up over {cur.SpoolTime:0.0} s from {cur.SpoolStart * 100:0}% fire rate.");
            if (cur.NoAds)
                ImGui.TextColored(Yellow, "Hip fire only."u8);
            if (cur.BuildFlags.Length > 0)
                ImGuiSafe.TextColored(Dim, "Flags: " + string.Join(", ", cur.BuildFlags) + " (effects come with Zombies)");
        }
        ImGuiSafe.TextColored(Dim, $"{b.PartCount} parts, {b.Weight:0.0} kg (carry {b.Frame?.Carry ?? 0:0} kg), {b.ScrapValue} scrap");
        var problems = _wbAsm?.Problems ?? [];
        if (problems.Count == 0)
            ImGui.TextColored(Green, "Ready to fire."u8);
        foreach (var p in problems.Take(8))
            ImGuiSafe.TextColored(p.Incomplete ? Yellow : Red, (p.Incomplete ? "Needs: " : "Problem: ") + p.Message);
        ImGui.Separator();

        ImGui.TextColored(Orange, "SAVED BUILDS"u8);
        for (var i = 0; i < BuildStore.SlotCount; i++)
        {
            var s = BuildStore.Slots[i];
            var label = $"{i + 1}. {(s?.Name ?? "(empty)")}{(i >= BuildStore.FreeSlots ? "  (unlockable in Zombies)" : "")}##slot{i}";
            if (ImGui.Selectable(label, BuildStore.Selected == i))
                LoadWorkbenchSlot(i);
            if (ImGui.IsItemHovered())
                ImGuiSafe.SetTooltip(i == BuildStore.Second ? "Your second weapon in the range." : "Click to edit this build.");
        }
        if (ImGui.Button("Save to this slot"u8))
        {
            BuildStore.Slots[BuildStore.Selected] = b.Clone();
            BuildStore.Save();
            _wbMessage = $"Saved '{b.Name}' to slot {BuildStore.Selected + 1}.";
        }
        ImGui.SameLine();
        if (ImGui.Button("Revert"u8))
            LoadWorkbenchSlot(BuildStore.Selected);
        ImGui.SameLine();
        if (ImGui.Button("Clear"u8))
        {
            _wb = new WeaponBuild(b.Name, b.Root?.Part ?? Parts.InCategory(PartCategory.Grip).First(p => p.IsRoot));
            WorkbenchChanged();
        }
        ImGuiSafe.TextColored(Dim, $"Second weapon in the range: slot {BuildStore.Second + 1}");
        ImGui.SameLine();
        if (ImGui.SmallButton("next"u8))
        {
            BuildStore.Second = (BuildStore.Second + 1) % BuildStore.SlotCount;
            BuildStore.Save();
        }
        ImGui.Spacing();
        var ready = cur != null;
        ImGui.BeginDisabled(!ready);
        if (ImGui.Button("TEST FIRE"u8, new float2(-1, 40 * _scale)) && cur != null)
        {
            BuildStore.Slots[BuildStore.Selected] = b.Clone();
            BuildStore.Save();
            var second = BuildStore.Second != BuildStore.Selected && BuildStore.Slots[BuildStore.Second] is { } sb ? WeaponBuilder.Assemble(sb).Def : null;
            launch = new WorkbenchLaunch(cur, second ?? cur);
        }
        ImGui.EndDisabled();
        ImGui.TextColored(Dim, "Opens the Workbench Range with this build and the second slot as your weapons."u8);
        return launch;
    }
}
