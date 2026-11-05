using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>The configure tool's panel on a rope or spring (docs/sandbox/CONSTRUCTS.md, "Ropes and springs"): its length and strength, or its stiffness and damping. Changes go as they are made, at most ten a second.</summary>
public sealed partial class Ui
{
    public bool ConnectorPanelOpen { get; private set; }

    private ushort _cnId;
    private float _cnLength, _cnTonnes, _cnKn, _cnDamp;
    private bool _cnPending;
    private double _cnSentAt;

    public void OpenConnectorPanel(in CConnector cn)
    {
        ConnectorPanelOpen = true;
        _cnId = cn.Id;
        _cnLength = cn.Length;
        _cnTonnes = cn.Rating / (1000f * ConstructPhysics.GravityAccel);
        _cnKn = cn.Stiffness / 1000f;
        _cnDamp = cn.Damping / 1000f;
        _cnPending = false;
        if (cn.Kind == ConnectorKind.Cylinder)
        {
            _cnKn = cn.Stiffness; // (metres, not kN: its shortest ...)
            _cnDamp = cn.Damping; // (... and longest)
        }
    }

    public void CloseConnectorPanel() => ConnectorPanelOpen = false;

    /// <summary>Draws it. False: it should close.</summary>
    public bool DrawConnectorPanel(Game game, Player me)
    {
        var k = game.Constructs.ConnectorIndex(_cnId);
        if (k < 0)
            return false;
        ref var cn = ref game.Constructs.Connectors[k];
        if (game.Constructs.ConnectorReach(cn, me.RenderEye) > ConstructConfig.Reach + 2f)
            return false;
        var open = true;
        ImGui.SetNextWindowSize(new float2(380 * _scale, 0), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.82f);
        if (ImGui.Begin(cn.Kind == ConnectorKind.Rope ? "Rope###sbconnector"u8 : cn.Kind == ConnectorKind.Spring ? "Spring###sbconnector"u8 : "Cylinder###sbconnector"u8, ref open, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize))
        {
            var size = ConstructWorld.ConnectorSizes[cn.Size];
            if (cn.Kind == ConnectorKind.Cylinder)
            {
                ImGui.TextColored(Dim, "A cylinder: how far it travels, and the most it pushes"u8);
                _cnPending |= ImGui.SliderFloat("Shortest (m)###cnmin"u8, ref _cnKn, ConstructWorld.ConnectorMinLength, ConstructWorld.ConnectorMaxLength, "%.2f"u8, ImGuiSliderFlags.AlwaysClamp | ImGuiSliderFlags.Logarithmic);
                _cnPending |= ImGui.SliderFloat("Longest (m)###cnmax"u8, ref _cnDamp, ConstructWorld.ConnectorMinLength, ConstructWorld.ConnectorMaxLength, "%.2f"u8, ImGuiSliderFlags.AlwaysClamp | ImGuiSliderFlags.Logarithmic);
                _cnPending |= ImGui.SliderFloat("Force (t)###cnfrc"u8, ref _cnTonnes, 0.01f, 20000f, "%.2f"u8, ImGuiSliderFlags.AlwaysClamp | ImGuiSliderFlags.Logarithmic);
            }
            else
            {
            ImGui.TextColored(Dim, cn.Kind == ConnectorKind.Rope ? "A rope: at most this long, and it snaps over its strength"u8 : "A spring: rests at this length. Stiffness 0 makes it a damper"u8);
            _cnPending |= ImGui.SliderFloat(cn.Kind == ConnectorKind.Rope ? "Length (m)###cnlen"u8 : "Rest length (m)###cnlen"u8, ref _cnLength, ConstructWorld.ConnectorMinLength, ConstructWorld.ConnectorMaxLength, "%.2f"u8, ImGuiSliderFlags.AlwaysClamp | ImGuiSliderFlags.Logarithmic);
            if (cn.Kind == ConnectorKind.Rope)
                _cnPending |= ImGui.SliderFloat("Strength (t)###cnstr"u8, ref _cnTonnes, size.Tonnes * 0.02f, size.Tonnes * (game.SandboxHands.InfiniteParts ? 20f : 1f), "%.2f"u8, ImGuiSliderFlags.AlwaysClamp | ImGuiSliderFlags.Logarithmic);
            else
            {
                _cnPending |= ImGui.SliderFloat("Stiffness (kN/m)###cnk"u8, ref _cnKn, 0, size.Stiffness * 0.02f, "%.1f"u8, ImGuiSliderFlags.AlwaysClamp);
                _cnPending |= ImGui.SliderFloat("Damping (kN s/m)###cnd"u8, ref _cnDamp, 0, size.Stiffness * 0.005f, "%.2f"u8, ImGuiSliderFlags.AlwaysClamp);
            }
            }
            if (ImGui.Button("Close"u8))
                open = false;
            ImGui.SameLine();
            ImGui.TextColored(Dim, "Esc closes"u8);
        }
        ImGui.End();
        var now = Environment.TickCount64 / 1000.0;
        if (_cnPending && (now - _cnSentAt >= 0.1 || !open))
        {
            _cnPending = false;
            _cnSentAt = now;
            var ram = cn.Kind == ConnectorKind.Cylinder;
            game.SandboxSendConnectorSet(_cnId, _cnLength, _cnTonnes * 1000f * ConstructPhysics.GravityAccel, ram ? _cnKn : _cnKn * 1000f, ram ? _cnDamp : _cnDamp * 1000f);
        }
        return open;
    }
}
