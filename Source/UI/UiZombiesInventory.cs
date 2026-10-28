using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Zombies: the quick parts inventory (the inventory key, I by default) and the workbench's loose parts list share one
/// drop picker: pick a part and how many, and DROP puts them on the floor in front of you as a pickup anyone on the team
/// can take with the interact key (Source/Game/ZombiesDropItems.cs; the server checks you have them).
/// </summary>
public sealed partial class Ui
{
    /// <summary>The quick parts inventory is open over the match (the mouse is free for it).</summary>
    public bool InventoryOpen { get; private set; }

    private string? _dropPart;
    private int _dropAmount = 1;

    public void OpenInventory()
    {
        InventoryOpen = true;
        _dropAmount = Math.Max(1, _dropAmount);
    }

    public void CloseInventory() => InventoryOpen = false;

    /// <summary>The quick inventory window (every frame while open). It closes itself when the run can't use it.</summary>
    public void DrawZInventory(Game game, Settings settings)
    {
        if (game.Local is not { } me || game.Mode != GameMode.Zombies)
        {
            CloseInventory();
            return;
        }
        var zp = game.ZP(me);
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + 24 * _scale, vp.WorkPos.Y + vp.WorkSize.Y * 0.45f), ImGuiCond.Always, new float2(0, 0.5f));
        ImGui.SetNextWindowSize(new float2(420 * _scale, 0), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.88f);
        if (ImGui.Begin("##zinventory"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing))
        {
            ImGui.PushTextWrapPos(0);
            ImGui.PushFont(default, 24);
            ImGuiSafe.TextColored(ZGold, "YOUR PARTS"u8);
            ImGui.PopFont();
            ImGuiSafe.TextColored(Dim, $"Pick a part and how many, then Drop: a teammate picks them up with [{Keybinds.Name(settings.Bind(GameAction.Interact))}]. "
                                       + $"[{Keybinds.Name(settings.Bind(GameAction.DropItem))}] drops the gun in your hands. The zombies don't wait.");
            ImGui.Separator();
            DrawPartDropPicker(game, zp, "inv");
            ImGui.Separator();
            if (ImGui.Button("Close"u8, new float2(-1, 28 * _scale)))
                CloseInventory();
            ImGui.PopTextWrapPos();
        }
        ImGui.End();
    }

    /// <summary>
    /// The loose parts as a list (in rarity colours) to pick from, an amount slider and DROP (shared by the quick inventory
    /// and the workbench). <paramref name="id"/> keeps the two sets of widgets apart.
    /// </summary>
    private void DrawPartDropPicker(Game game, ZPlayer zp, string id)
    {
        if (zp.Parts.Count == 0)
        {
            ImGui.TextColored(Dim, "No loose parts: zombies drop them (later rounds rarer ones); so do salvage piles and the slot machines."u8);
            return;
        }
        if (_dropPart != null && !zp.Parts.ContainsKey(_dropPart))
            _dropPart = null;
        foreach (var (name, n) in zp.Parts)
        {
            var part = Parts.Find(name);
            var col = part != null ? PartDef.RarityColor(part.Rarity) : new System.Numerics.Vector3(1);
            ImGui.PushStyleColor(ImGuiCol.Text, new float4(col.X, col.Y, col.Z, 1f));
            if (ImGui.Selectable($"{name} x{n}   ({(part != null ? PartDef.RarityName(part.Rarity) : "?")})##{id}{name}", string.Equals(_dropPart, name, StringComparison.OrdinalIgnoreCase)))
                _dropPart = name;
            ImGui.PopStyleColor();
        }
        if (_dropPart == null)
        {
            ImGui.TextColored(Dim, "Click a part to drop some of it."u8);
            return;
        }
        var have = zp.Parts.GetValueOrDefault(_dropPart);
        var max = Math.Max(1, Math.Min(have, game.ZRules.MaxPartsPerDrop));
        _dropAmount = Math.Clamp(_dropAmount, 1, max);
        ImGui.SetNextItemWidth(200 * _scale);
        if (max > 1)
            ImGui.SliderInt($"How many##{id}amount", ref _dropAmount, 1, max);
        else
            ImGuiSafe.TextColored(Dim, "How many: 1"u8);
        ImGui.SameLine();
        if (ImGui.Button($"DROP {_dropAmount}##{id}drop"))
        {
            var why = game.DropItemRequest(Game.DropParts, _dropPart, _dropAmount);
            if (BenchRun != null)
                WbSay(why ?? $"Dropped {_dropAmount} {_dropPart} on the floor in front of you.", why != null);
        }
    }
}
