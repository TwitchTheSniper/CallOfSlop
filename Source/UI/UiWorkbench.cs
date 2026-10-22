using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// What the workbench asks Program to start: a Workbench Range session with these built weapons (slots 3-4 used once
/// the slot upgrade is on or bought at the range's station), the built melee weapon (null = the standard strike) and
/// how many practice dummies stand down the range.
/// </summary>
public sealed record WorkbenchLaunch(WeaponDef Primary, WeaponDef Secondary, WeaponDef? Third = null, WeaponDef? Fourth = null, MeleeDef? Melee = null,
    bool ExtraSlots = false, int Dummies = 0);

/// <summary>
/// The Weapon Workbench (Zombies preview, docs/zombies/WEAPON-BUILDER.md): every part (sandbox: all unlocked) on the left,
/// the live 3D build in the middle with its part tree under it (the grip's receiver slots first), stats with deltas and
/// problems on the right, four save slots (3-4 with the slot upgrade), the melee slot (its own section: hilts, heads,
/// mods), the standard builds as presets, the sandbox toggles (slots 3-4, practice dummies) and Test fire, which opens the
/// Workbench Range with your builds. Click a socket, then a part (or a part, then a socket).
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

    /// <summary>Editing the melee slot (hilts, heads, mods) instead of a gun slot.</summary>
    private bool _wbMelee;

    private static readonly PartCategory[] MeleeCategories = [PartCategory.Hilt, PartCategory.Head, PartCategory.MeleeMod];

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
        _wbMelee = b.IsMelee;
        _wbCat = b.IsMelee ? PartCategory.Head : PartCategory.Grip;
        if (_wbNameInput != null)
            ImGuiSafe.SetInput(_wbNameInput, _wb.Name);
        WorkbenchChanged();
    }

    /// <summary>Switches the editor to the melee slot (or back to the selected gun slot).</summary>
    private void LoadWorkbenchMelee(bool melee)
    {
        if (BenchRun != null)
        {
            BenchLoad(melee ? 4 : 0); // a Zombies workbench: your own slots (UiWorkbenchRun.cs)
            return;
        }
        _wbMelee = melee;
        _wbPart = null;
        if (!melee)
        {
            LoadWorkbenchSlot(BuildStore.Selected);
            return;
        }
        _wbCat = PartCategory.Head;
        _wb = BuildStore.Melee?.Clone() ?? new WeaponBuild("Melee", Parts.InCategory(PartCategory.Hilt).First(p => p.IsRoot));
        _wbNameInput ??= new ImInputString(33);
        ImGuiSafe.SetInput(_wbNameInput, _wb.Name);
        _wbSocketParent = _wbSocketName = null;
        WorkbenchChanged();
    }

    private void LoadWorkbenchSlot(int slot)
    {
        if (BenchRun != null)
        {
            BenchLoad(slot);
            return;
        }
        BuildStore.Selected = slot;
        _wbMelee = false;
        if (MeleeCategories.Contains(_wbCat))
            _wbCat = PartCategory.Grip;
        _wb = BuildStore.Slots[slot]?.Clone() ?? new WeaponBuild($"Build {slot + 1}", Parts.InCategory(PartCategory.Grip).First(p => p.Frame != null));
        _wbNameInput ??= new ImInputString(33);
        ImGuiSafe.SetInput(_wbNameInput, _wb.Name);
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

    /// <summary>The build moved onto another grip (or hilt): receivers go into the same-numbered slots, the rest by socket name where it fits.</summary>
    private static WeaponBuild Regrip(WeaponBuild b, PartDef grip)
    {
        var nb = new WeaponBuild(b.Name, grip); // the fire style comes with the grip
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
                launch = BenchRun != null ? DrawBenchRunSide(b) : DrawWorkbenchSide(b); // Zombies: your slots, parts and scrap (UiWorkbenchRun.cs)
            ImGui.EndChild();

            // ---- Footer ----
            if (ImGui.Button("BACK"u8, new float2(160 * _scale, 34 * _scale)) || ImGui.IsKeyPressed(ImGuiKey.Escape, false))
            {
                WorkbenchOpen = false;
                BenchRun = null;
            }
            ImGui.SameLine();
            ImGuiSafe.TextColored(Dim, _wbMessage.Length > 0 ? _wbMessage
                : BenchRun != null ? "Click a socket in the tree, then a part. Only the parts you own this run are listed."
                : "Click a socket in the tree, then a part (or a part, then a socket). Sandbox: every part is unlocked.");
        }
        ImGui.End();
        return launch;
    }

    private void DrawWorkbenchParts(WeaponBuild b)
    {
        // Gun slots or the melee slot: each has its own parts.
        if (ImGui.RadioButton("GUNS"u8, !_wbMelee) && _wbMelee)
            LoadWorkbenchMelee(false);
        ImGui.SameLine();
        if (ImGui.RadioButton("MELEE SLOT"u8, _wbMelee) && !_wbMelee)
            LoadWorkbenchMelee(true);
        ImGui.TextColored(Orange, "PARTS"u8);
        var shown = 0;
        foreach (var c in Enum.GetValues<PartCategory>())
        {
            var n = Parts.InCategory(c).Count();
            if (n == 0 || MeleeCategories.Contains(c) != _wbMelee)
                continue;
            var on = c == _wbCat;
            if (on)
                ImGui.PushStyleColor(ImGuiCol.Button, new float4(0.3f, 0.22f, 0.1f, 1f));
            if (ImGui.Button($"{PartDef.CategoryName(c)} ({n})##cat{(int)c}"))
                _wbCat = c;
            if (on)
                ImGui.PopStyleColor();
            if (++shown % 3 != 0)
                ImGui.SameLine();
        }
        ImGui.NewLine();
        ImGui.Separator();
        var socketParent = _wbSocketParent != null ? b.At(_wbSocketParent) : null;
        foreach (var p in Parts.InCategory(_wbCat))
        {
            if (!BenchShows(p, b))
                continue; // a Zombies workbench lists only the parts you own this run
            string? why = null;
            if (BenchRun != null && BenchAvailable(p, b) <= 0)
                why = "you have none left to fit (zombies drop parts; or craft one with scrap)";
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
            if (BenchRun != null && BenchAvailable(p, b) <= 0)
            {
                _wbMessage = $"You have no {p.Name} left to fit.";
                continue;
            }
            _wbPart = p;
            if (p.IsRoot)
            {
                _wb = Regrip(b, p);
                _wbMessage = p.Hilt != null ? $"Moved the head and mods onto the {p.Name}." : $"Moved the build onto the {p.Name} ({p.ReceiverCapacity} receiver slots, fires {p.Frame!.Style.ToString().ToLowerInvariant()}).";
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
        var why = BenchRun != null && BenchAvailable(p, b) <= 0 ? $"You have no {p.Name} left to fit." : BuildRules.WhyNot(b, _wbSocketParent, _wbSocketName, p);
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
            bits.Add($"Holds {p.ReceiverCapacity} receivers, always fires {f.Style.ToString().ToLowerInvariant()} ({(f.Style == FireStyle.Volley ? "all at once" : "taking turns")}), carries {f.Carry:0} kg{(f.Ads ? "" : ", hip fire only")}"
                     + (p.Sockets.Any(s => s.Type == "hopper") ? ", takes a shared feed" : ""));
        if (p.Shared is { } sh)
            bits.Add($"shared feed: feeds up to {sh.Feeds} receivers with no feed of their own (pooled)");
        if (p.CarryBonus > 0)
            bits.Add($"weight relief: the grip carries {p.CarryBonus:0.#} kg more");
        if (p.Hilt is { } hl)
            bits.Add($"melee hilt: {hl.Damage:0} damage, {hl.Swing:0.00} s swing, {hl.Reach:0.0} m reach, cleave {hl.Cleave}{(hl.Stun > 0 ? $", stun {hl.Stun:0.0} s" : "")}, {hl.Hands}-handed");
        if (p.Melee is { } me)
        {
            var mb = new List<string>();
            if (me.Damage != 0) mb.Add($"damage {(me.Damage > 0 ? "+" : "")}{me.Damage:0}");
            if (me.DamageMult != 1) mb.Add($"damage x{me.DamageMult:0.##}");
            if (me.SwingMult != 1) mb.Add($"swing time x{me.SwingMult:0.##}");
            if (me.Reach != 0) mb.Add($"reach {(me.Reach > 0 ? "+" : "")}{me.Reach:0.##} m");
            if (me.Cleave != 0) mb.Add($"cleave {(me.Cleave > 0 ? "+" : "")}{me.Cleave}");
            if (me.Stun != 0) mb.Add($"stun +{me.Stun:0.0} s");
            bits.Add(string.Join(", ", mb));
        }
        if (p.Receiver is { } r)
            bits.Add($"{r.Action.ToString().ToLowerInvariant()} action, {r.Bore} rounds, {(r.RpmPerBarrel > 0 ? $"{r.RpmPerBarrel} rpm per barrel" : $"{r.Rpm} rpm")}");
        if (p.Split is { } s)
            bits.Add($"{p.Sockets.Count(x => x.Type == "barrel")} outputs at {SplitInfo.OutputMult(p) * 100:0}% damage each (the same total){(s.SpreadMult != 1 ? $", spread x{s.SpreadMult:0.##}" : "")}");
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
        ImGuiSafe.TextColored(WbCol(PartDef.RarityColor(grip.Rarity)), grip.Name);
        ImGui.SameLine();
        if (b.IsMelee)
            ImGui.TextColored(Dim, "  melee weapon   (pick another hilt from the Hilt list to move the head and mods)"u8);
        else
        {
            var fed = b.HopperFed().Count;
            ImGuiSafe.TextColored(Dim, $"  receiver slots {b.Receivers.Count}/{grip.ReceiverCapacity}, fires {b.Style.ToString().ToUpperInvariant()}"
                                       + (b.Hopper is { } h ? $", {h.Part.Name} feeds {fed}/{h.Part.Shared!.Feeds}" : "") + "   (pick another grip to move the build)");
        }
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
                {
                    Place(b, _wbPart);
                    if (node.Child(s.Name) != child)
                    {
                        // The tree changed under us: stop drawing it this frame (the old child is off the build now).
                        ImGui.PopStyleColor();
                        return;
                    }
                }
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
        "upgrade" => PartCategory.Upgrade,
        "amp" => PartCategory.Multiplier,
        "hopper" => PartCategory.Feed,
        "head" => PartCategory.Head,
        "mod" => PartCategory.MeleeMod,
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
        ImGui.TextColored(Orange, _wbMelee ? "MELEE SLOT"u8 : "BUILD"u8);
        ImGui.SetNextItemWidth(-1);
        try
        {
            if (ImGui.InputText("##wbname"u8, _wbNameInput!))
            {
                b.Name = WeaponBuild.CleanName(_wbNameInput!.ToString());
                WorkbenchChanged();
            }
        }
        catch (Exception ex)
        {
            ImGuiSafe.FieldFailed("build name", ex);
        }
        if (b.Frame is { } frame)
            ImGuiSafe.TextColored(Dim, $"Fire style: {frame.Style.ToString().ToUpperInvariant()} (fixed by the {b.Root!.Part.Name}): "
                                       + (frame.Style == FireStyle.Volley ? "every receiver fires on each pull." : "the receivers take turns."));
        ImGui.Separator();

        if (_wbMelee)
            DrawWorkbenchMeleeStats(b);
        else
            DrawWorkbenchGunStats(b);
        var problems = _wbAsm?.Problems ?? [];
        if (problems.Count == 0)
            ImGui.TextColored(Green, _wbMelee ? "Ready to swing."u8 : "Ready to fire."u8);
        foreach (var p in problems.Take(8))
            ImGuiSafe.TextColored(p.Incomplete ? Yellow : Red, (p.Incomplete ? "Needs: " : "Problem: ") + p.Message);
        ImGui.Separator();

        if (_wbMelee)
        {
            ImGui.TextColored(Orange, "MELEE SLOT"u8);
            ImGuiSafe.TextColored(Dim, $"Saved: {BuildStore.Melee?.Name ?? "(the standard knife / butt strike)"}");
            if (ImGui.Button("Save melee"u8))
            {
                BuildStore.Melee = b.Clone();
                BuildStore.Save();
                _wbMessage = $"Saved '{b.Name}' to the melee slot.";
            }
            ImGui.SameLine();
            if (ImGui.Button("Revert"u8))
                LoadWorkbenchMelee(true);
            ImGui.SameLine();
            if (ImGui.Button("Use the standard strike"u8))
            {
                BuildStore.Melee = null;
                BuildStore.Save();
                _wbMessage = "The melee key does the standard knife / butt strike again.";
            }
        }
        else
        {
            ImGui.TextColored(Orange, "SAVED BUILDS"u8);
            for (var i = 0; i < BuildStore.SlotCount; i++)
            {
                var s = BuildStore.Slots[i];
                var label = $"{i + 1}. {(s?.Name ?? "(empty)")}{(i >= BuildStore.FreeSlots ? "  (slot upgrade)" : "")}##slot{i}";
                if (ImGui.Selectable(label, BuildStore.Selected == i))
                    LoadWorkbenchSlot(i);
                if (ImGui.IsItemHovered())
                    ImGuiSafe.SetTooltip(i == BuildStore.Second ? "Your second weapon in the range." : i >= BuildStore.FreeSlots ? "Slots 3-4: used once the weapon-slot upgrade is on (toggle below, or the station in the range)." : "Click to edit this build.");
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
                _wb = new WeaponBuild(b.Name, b.Root?.Part ?? Parts.InCategory(PartCategory.Grip).First(p => p.Frame != null));
                WorkbenchChanged();
            }
            ImGuiSafe.TextColored(Dim, $"Second weapon in the range: slot {BuildStore.Second + 1}");
            ImGui.SameLine();
            if (ImGui.SmallButton("next"u8))
            {
                BuildStore.Second = (BuildStore.Second + 1) % BuildStore.SlotCount;
                BuildStore.Save();
            }
        }
        DrawWorkbenchPresets();
        ImGui.Separator();

        // ---- Sandbox toggles ----
        if (ImGui.Checkbox("Weapon slots 3-4 unlocked"u8, ref BuildStore.ExtraSlotsUnlocked))
            BuildStore.Save();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("In Zombies the slot upgrade is bought on the map, once per run. Here it's a toggle (or use the station by the range's spawn)."u8);
        ImGuiSafe.TextColored(Dim, $"Practice dummies: {BuildStore.Dummies}");
        ImGui.SameLine();
        if (ImGui.SmallButton("-##dum"u8) && BuildStore.Dummies > 0)
        {
            BuildStore.Dummies--;
            BuildStore.Save();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("+##dum"u8) && BuildStore.Dummies < 8)
        {
            BuildStore.Dummies++;
            BuildStore.Save();
        }
        ImGui.Spacing();
        var ready = _wbMelee ? _wbAsm?.Melee != null : _wbAsm?.Def != null;
        ImGui.BeginDisabled(!ready);
        if (ImGui.Button("TEST FIRE"u8, new float2(-1, 40 * _scale)) && ready)
        {
            launch = WorkbenchLaunchFor(b, out var why); // assembles every slot once, on the click
            if (launch == null)
                _wbMessage = why;
            else
            {
                if (_wbMelee)
                    BuildStore.Melee = b.Clone();
                else
                    BuildStore.Slots[BuildStore.Selected] = b.Clone();
                BuildStore.Save();
            }
        }
        ImGui.EndDisabled();
        ImGuiSafe.TextColored(Dim, $"Opens the Workbench Range with your builds ({Keybinds.NameOf(_hudSettings, GameAction.Interact)}: park / push a cart, buy the slot upgrade).");
        return launch;
    }

    private void DrawWorkbenchGunStats(WeaponBuild b)
    {
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
            if (Game.IsCart(cur))
                ImGuiSafe.TextColored(Yellow, $"A real cart: push it, {Keybinds.NameOf(_hudSettings, GameAction.Interact)} parks it, {Keybinds.NameOf(_hudSettings, GameAction.Interact)} at its bar mans its gun.");
            if (cur.BuildFlags.Length > 0)
                ImGuiSafe.TextColored(Dim, "Flags: " + string.Join(", ", cur.BuildFlags) + " (effects come with Zombies)");
        }
        ImGuiSafe.TextColored(Dim, $"{b.PartCount} parts, {b.Weight:0.0} kg (carry {b.CarryWeight:0.#} kg, max {b.CarryWeight * 2:0.#}), {b.ScrapValue} scrap");
    }

    private void DrawWorkbenchMeleeStats(WeaponBuild b)
    {
        var cur = _wbAsm?.Melee;
        var trial = _wbPart != null ? Trial(_wbPart)?.Melee : null;
        if (cur != null)
        {
            ImGuiSafe.TextColored(Dim, $"{(cur.TwoHanded ? "Two" : "One")}-handed melee weapon");
            StatLine("Damage", cur.Damage, trial?.Damage, "0", true);
            StatLine("Swing (s)", cur.Swing, trial?.Swing, "0.00", false);
            StatLine("DPS", cur.Dps, trial?.Dps, "0", true);
            StatLine("Reach (m)", cur.Reach, trial?.Reach, "0.0", true);
            StatLine("Cleave", cur.Cleave, trial?.Cleave, "0", true);
            StatLine("Stun (s)", cur.Stun, trial?.Stun, "0.0", true);
            if (cur.Flags.Length > 0)
                ImGuiSafe.TextColored(Dim, "Flags: " + string.Join(", ", cur.Flags));
            ImGuiSafe.TextColored(Dim, $"Standard strike: kills a player in one hit, {Game.MeleeObjectDamage:0} damage to zombies, {Game.MeleeCooldown:0.0} s, {Game.MeleeRange:0.0} m, one target.");
        }
        ImGuiSafe.TextColored(Dim, $"{b.PartCount} parts, {b.Weight:0.0} kg (max {BuildRules.MaxMeleeWeight:0}), {b.ScrapValue} scrap");
    }

    /// <summary>The standard complete builds (the mystery box's and the wall's), as presets for the editor.</summary>
    private void DrawWorkbenchPresets()
    {
        var list = StandardBuilds.All.Where(x => x.IsMelee == _wbMelee).ToList();
        if (list.Count == 0)
            return;
        if (!ImGui.CollapsingHeader("Standard builds (box / wall presets)"u8, ImGuiTreeNodeFlags.None))
            return;
        foreach (var sb in list)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, WbCol(PartDef.RarityColor(sb.Tier)));
            var clicked = ImGui.Selectable($"{sb.Name}  [{PartDef.RarityName(sb.Tier)}, {string.Join("/", sb.Sources)}]##preset{sb.Name}");
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
                ImGuiSafe.SetTooltip(sb.Flavor);
            if (!clicked)
                continue;
            _wb = sb.Build();
            if (_wbNameInput != null)
                ImGuiSafe.SetInput(_wbNameInput, _wb.Name);
            _wbSocketParent = _wbSocketName = null;
            _wbMessage = $"Loaded the standard build '{sb.Name}' (save it to keep it).";
            WorkbenchChanged();
        }
    }

    /// <summary>What Test fire would start (every slot's build that's complete, the melee slot, the toggles), or null with the reason.</summary>
    private WorkbenchLaunch? WorkbenchLaunchFor(WeaponBuild editing, out string why)
    {
        why = "";
        WeaponDef? Gun(int slot) =>
            !_wbMelee && slot == BuildStore.Selected ? _wbAsm?.Def : BuildStore.Slots[slot] is { IsMelee: false } sb ? WeaponBuilder.Assemble(sb).Def : null;
        var primary = Gun(BuildStore.Selected);
        if (primary == null && _wbMelee)
            primary = Enumerable.Range(0, BuildStore.SlotCount).Select(Gun).FirstOrDefault(d => d != null);
        if (primary == null)
        {
            why = _wbMelee ? "Save a complete gun to a slot first: the range needs a gun as well as the melee weapon." : "Finish the build first (it needs everything listed above).";
            return null;
        }
        var second = BuildStore.Second != BuildStore.Selected ? Gun(BuildStore.Second) : null;
        var rest = Enumerable.Range(0, BuildStore.SlotCount).Where(i => i != BuildStore.Selected && i != BuildStore.Second).Select(Gun).Where(d => d != null).ToList();
        var melee = _wbMelee ? _wbAsm?.Melee : BuildStore.Melee is { } m ? WeaponBuilder.Assemble(m).Melee : null;
        if (_wbMelee && melee == null)
        {
            why = "Finish the melee weapon first (it needs a head).";
            return null;
        }
        return new WorkbenchLaunch(primary, second ?? primary, rest.ElementAtOrDefault(0), rest.ElementAtOrDefault(1), melee, BuildStore.ExtraSlotsUnlocked, BuildStore.Dummies);
    }
}
