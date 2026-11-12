using Brutal.ImGuiApi;
using Brutal.Numerics;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The words of a lore item a player is looking at closely (Program.StoryLore.cs; docs/story/STORY.md "Lore"): a panel at
/// the left of the screen over the world, its title and paragraphs, and how to put it down. It takes no input, so the game
/// keeps the mouse. Every line is ASCII; nothing allocates in a steady frame.
/// </summary>
public sealed partial class Ui
{
    private LoreDef? _loreFor;
    private string _loreTitle = "", _loreHint = "";

    public void DrawStoryLore(LoreDef item)
    {
        if (_loreFor != item)
            (_loreFor, _loreTitle, _loreHint) = (item, item.Title.ToUpperInvariant(), $"{SandboxKeys.Use} or Esc: put it down");
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + 24 * _scale, vp.WorkPos.Y + vp.WorkSize.Y * 0.45f), ImGuiCond.Always, new float2(0, 0.5f));
        ImGui.SetNextWindowSize(new float2(440 * _scale, 0), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.88f);
        if (ImGui.Begin("##storylore"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoNav))
        {
            ImGui.PushTextWrapPos(0);
            ImGuiSafe.TextColored(Orange, _loreTitle);
            ImGui.Separator();
            for (var i = 0; i < item.Text.Length; i++)
            {
                if (i > 0)
                    ImGui.Spacing();
                ImGuiSafe.TextColored(StoryPlain, item.Text[i]);
            }
            ImGui.Separator();
            ImGuiSafe.TextColored(Dim, _loreHint);
            ImGui.PopTextWrapPos();
        }
        ImGui.End();
    }
}
