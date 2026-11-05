using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

public sealed partial class Ui
{
    /// <summary>A small box at the crosshair for typing on a part (Program.SandboxControls.cs). The text when Enter was pressed, else null. Esc is Program's.</summary>
    public string? DrawTypingBox(string name, bool digits, ImInputString input, ref bool start)
    {
        var size = ImGui.GetIO().DisplaySize;
        ImGui.SetNextWindowPos(new float2(size.X * 0.5f, size.Y * 0.62f), ImGuiCond.Always, new float2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new float2(ImGui.GetFontSize() * 22, 0), ImGuiCond.Always);
        string? typed = null;
        if (ImGui.Begin("##parttyping"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing))
        {
            ImGuiSafe.TextDisabled(digits ? $"{name}: digits, Enter accepts, Esc leaves" : $"{name}: type, Enter sets, Esc leaves");
            ImGui.SetNextItemWidth(-1);
            if (start)
            {
                ImGui.SetWindowFocus();
                ImGui.SetKeyboardFocusHere();
                start = false;
            }
            if (ImGui.InputText("##parttext"u8, input, digits ? ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CharsDecimal : ImGuiInputTextFlags.EnterReturnsTrue))
                typed = ImGuiSafe.Text(input);
        }
        ImGui.End();
        return typed;
    }
}
