using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The trader's shelf in a Zombies run (Source/Game/ZombiesTrader.cs; docs/zombies/ZOMBIES-MODE.md, "The trader"):
/// opened with the interact key at his counter, over the match (the world goes on: zombies keep coming and the HUD stays
/// up). Each shelf item on a row: the part's name in its rarity colour, its category, how many are left, the price and a
/// BUY button (or SOLD OUT, or the price in red when you can't afford it); then the consumables. The server decides
/// everything: a click asks (<see cref="Game.TraderBuyRequest"/>) and the shelf comes back changed. Esc or Close goes
/// back to the fight; walking away closes it. Every line is ASCII.
/// </summary>
public sealed partial class Ui
{
    public bool TraderOpen { get; private set; }

    public void OpenTrader() => TraderOpen = true;

    public void CloseTrader() => TraderOpen = false;

    /// <summary>The panel (called every frame while it's open; it closes itself when the trader can't be shopped at).</summary>
    public void DrawTrader(Game game, double now)
    {
        var t = game.ZTrader;
        if (!t.Here || game.Local is not { } me)
        {
            CloseTrader();
            return;
        }
        var zp = game.ZP(me);
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + vp.WorkSize.X - 24 * _scale, vp.WorkPos.Y + vp.WorkSize.Y * 0.45f), ImGuiCond.Always, new float2(1, 0.5f));
        ImGui.SetNextWindowSize(new float2(560 * _scale, 0), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.88f);
        if (ImGui.Begin("##trader"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing))
        {
            ImGui.PushTextWrapPos(0);
            ImGui.PushFont(default, 26);
            ImGuiSafe.TextColored(ZGold, "THE TRADER"u8);
            ImGui.PopFont();
            ImGuiSafe.TextColored(Dim, $"\"Have a look. First come, first served; I'm gone when round {t.LeaveRound} starts.\"   You have {zp.Points}.");
            ImGuiSafe.TextColored(Orange, "Bought parts go to your loose parts: fit them at a workbench."u8);
            ImGui.Separator();
            if (t.Count == 0)
                ImGuiSafe.TextColored(Dim, "The shelf is bare."u8);
            if (ImGui.BeginTable("##shelf"u8, 5, ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Item"u8, ImGuiTableColumnFlags.WidthStretch, 3f);
                ImGui.TableSetupColumn("Kind"u8, ImGuiTableColumnFlags.WidthStretch, 1.6f);
                ImGui.TableSetupColumn("Left"u8, ImGuiTableColumnFlags.WidthStretch, 0.7f);
                ImGui.TableSetupColumn("Price"u8, ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("##buy"u8, ImGuiTableColumnFlags.WidthStretch, 1.1f);
                ImGui.TableHeadersRow();
                for (var i = 0; i < t.Count; i++)
                {
                    var item = t.Stock[i];
                    var part = item.Kind == ZTraderItemKind.Part && item.Part >= 0 && item.Part < Parts.All.Length ? Parts.All[item.Part] : null;
                    var colour = part != null ? PartDef.RarityColor(part.Rarity) : new System.Numerics.Vector3(0.85f, 0.85f, 0.8f);
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGuiSafe.TextColored(new float4(colour.X, colour.Y, colour.Z, 1f), Game.TraderItemName(item));
                    ImGui.TableNextColumn();
                    ImGuiSafe.TextColored(Dim, part != null ? $"{PartDef.RarityName(part.Rarity)} {PartDef.CategoryName(part.Category)}" : item.Kind == ZTraderItemKind.Grenades ? "the round's count" : "all your guns");
                    ImGui.TableNextColumn();
                    ImGuiSafe.TextColored(Dim, item.Left == ZTraderItem.Unlimited ? "-" : item.Left.ToString());
                    ImGui.TableNextColumn();
                    var poor = zp.Points < item.Price;
                    ImGuiSafe.TextColored(item.SoldOut ? Dim : poor ? Red : new float4(1, 1, 1, 1), item.Price.ToString());
                    ImGui.TableNextColumn();
                    if (item.SoldOut)
                        ImGuiSafe.TextColored(Dim, "SOLD OUT"u8);
                    else
                    {
                        ImGui.BeginDisabled(poor);
                        if (ImGui.Button($"BUY##buy{i}", new float2(-1, 0)))
                            game.TraderBuyRequest(i);
                        ImGui.EndDisabled();
                    }
                }
                ImGui.EndTable();
            }
            ImGui.Separator();
            if (ImGui.Button("BACK TO THE FIGHT [Esc]"u8, new float2(240 * _scale, 30 * _scale)))
                CloseTrader();
            ImGui.PopTextWrapPos();
        }
        ImGui.End();
    }

    /// <summary>The trader's waypoint on the HUD: a label over his stall through the walls, or a line with his distance when he's off screen.</summary>
    private void DrawTraderLabel(Game game, Player me, double now)
    {
        var t = game.ZTrader;
        if (!t.Here)
            return;
        var d = System.Numerics.Vector3.Distance(me.Position, t.At);
        var fresh = (float)Math.Clamp(1 - (now - t.ChangedAt) / 12.0, 0, 1);
        var alpha = 0.75f + 0.25f * fresh * MathF.Abs(MathF.Sin((float)now * 4f));
        if (!Hud.WorldLabel(t.At + new System.Numerics.Vector3(0, 2.4f, 0), $"TRADER  {d:0} m", new System.Numerics.Vector3(1f, 0.8f, 0.35f), alpha, 16f * _scale))
            _traderOffScreen = $"The trader is here: {d:0} m away (leaves when round {t.LeaveRound} starts)";
    }

    /// <summary>The HUD line for a trader who's off screen this frame (set by <see cref="DrawTraderLabel"/>, shown in the prompt window).</summary>
    private string? _traderOffScreen;
}
