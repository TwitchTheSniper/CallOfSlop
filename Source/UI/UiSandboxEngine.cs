using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using CallOfSlop.EngineKit;

namespace CallOfSlop;

/// <summary>
/// The inventory's Engine tab (creative worlds only; docs/vehiclekit/ENGINEKIT.md, "Callers"): power, tech tier, layout, booster, size and an
/// optional bay to fit, then "MAKE ENGINE" asks the host (GameSandboxEngines.cs), which sets the engine down in front of the player. The
/// tab is part of the inventory window, so its mouse, Esc and reset are the inventory's. Buttons only note the ask; it is sent after the drawing.
/// </summary>
public sealed partial class Ui
{
    /// <summary>The engine asked for: kW, tier, layout, booster, size (-1 any), the bay (length, width, height; used when <see cref="SandboxEngineFits"/>), the seed (0: the server's 0).</summary>
    public int SandboxEngineKw = 300, SandboxEngineTier = 3, SandboxEngineSeed, SandboxEngineSize = -1;

    public EngineLayout SandboxEngineLayout;
    public EngineBoost SandboxEngineBoost;
    public bool SandboxEngineFits;
    public Vector3 SandboxEngineBay = EngineKitVehicle.Bay;

    private static readonly int[] EnginePresets = [120, 300, 800, 3000];
    private static readonly string[] EnginePresetLabels = ["120 kW##sbepre0", "300 kW##sbepre1", "800 kW##sbepre2", "3000 kW##sbepre3"];
    private static readonly string[] EngineTierLabels = ["1##sbetier", "2##sbetier", "3##sbetier", "4##sbetier", "5##sbetier"];
    private static readonly string[] EngineLayoutLabels = ["Any layout", "Inline", "V", "Flat (boxer)", "W", "Radial"];
    private static readonly string[] EngineBoostLabels = ["Any booster", "No booster", "Supercharger (tier 3+)", "Turbo (tier 4+)"];
    private static readonly string[] EngineSizeLabels = ["Any size", "Small", "Medium (x4)", "Large (x16)", "Capital (x64)"];
    private static readonly string[] EngineTierNotes =
    [
        "Tier 1: plain cylinders and carburettors.",
        "Tier 2: adds injector rails (x1.2 power, less fuel).",
        "Tier 3: bored cylinders (x1.3) and the supercharger (x1.25).",
        "Tier 4: performance cylinders (x1.6) and the turbo (x1.4, lag).",
        "Tier 5: racing cylinders (x2).",
    ];

    private void SbDrawEngine(Game game, Player me)
    {
        if (!game.SandboxCreativeSpawns)
        {
            ImGui.Spacing();
            ImGui.TextColored(Dim, "Engines are made here only in creative worlds: in survival, build one from its pieces."u8);
            return;
        }
        ImGui.Spacing();
        ImGui.TextColored(Dim, "The Engine Kit plans and builds a modular engine for what you ask, in front of you. It needs a fuel tank and a gearbox to run."u8);
        ImGui.Separator();
        ImGui.SetNextItemWidth(200 * _scale);
        ImGui.InputInt("Power (kW)##sbekw"u8, ref SandboxEngineKw, 10, 100);
        SandboxEngineKw = Math.Clamp(SandboxEngineKw, 1, Game.MakeEngineMaxKw);
        for (var k = 0; k < EnginePresets.Length; k++)
        {
            ImGui.SameLine();
            if (ImGui.Button(EnginePresetLabels[k]))
                SandboxEngineKw = EnginePresets[k];
        }
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Dim, "Tech tier:"u8);
        for (var k = 0; k < EngineTierLabels.Length; k++)
        {
            ImGui.SameLine();
            if (ImGui.RadioButton(EngineTierLabels[k], SandboxEngineTier == k + 1))
                SandboxEngineTier = k + 1;
        }
        SandboxEngineTier = Math.Clamp(SandboxEngineTier, 1, EngineTierNotes.Length);
        ImGui.SameLine(0, 16 * _scale);
        ImGuiSafe.TextColored(Dim, EngineTierNotes[SandboxEngineTier - 1]);
        ImGui.SetNextItemWidth(200 * _scale);
        if (ImGui.BeginCombo("##sbelayout"u8, EngineLayoutLabels[(int)SandboxEngineLayout]))
        {
            for (var k = 0; k < EngineLayoutLabels.Length; k++)
                if (ImGui.Selectable(EngineLayoutLabels[k], (int)SandboxEngineLayout == k))
                    SandboxEngineLayout = (EngineLayout)k;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220 * _scale);
        if (ImGui.BeginCombo("##sbeboost"u8, EngineBoostLabels[(int)SandboxEngineBoost]))
        {
            for (var k = 0; k < EngineBoostLabels.Length; k++)
                if (ImGui.Selectable(EngineBoostLabels[k], (int)SandboxEngineBoost == k))
                    SandboxEngineBoost = (EngineBoost)k;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180 * _scale);
        if (ImGui.BeginCombo("##sbesize"u8, EngineSizeLabels[SandboxEngineSize + 1]))
        {
            for (var k = 0; k < EngineSizeLabels.Length; k++)
                if (ImGui.Selectable(EngineSizeLabels[k], SandboxEngineSize == k - 1))
                    SandboxEngineSize = k - 1;
            ImGui.EndCombo();
        }
        ImGui.Checkbox("It must fit a bay"u8, ref SandboxEngineFits);
        if (SandboxEngineFits)
        {
            ImGui.SameLine();
            if (ImGui.Button("Car bay##sbecar"u8))
                SandboxEngineBay = EngineKitVehicle.Bay;
            ImGui.SetNextItemWidth(260 * _scale);
            ImGui.SliderFloat("Length along the crank##sbebl"u8, ref SandboxEngineBay.X, Game.MakeEngineMinFit, 30f, "%.2f m"u8);
            ImGui.SetNextItemWidth(260 * _scale);
            ImGui.SliderFloat("Width##sbebw"u8, ref SandboxEngineBay.Y, Game.MakeEngineMinFit, 30f, "%.2f m"u8);
            ImGui.SetNextItemWidth(260 * _scale);
            ImGui.SliderFloat("Height##sbebh"u8, ref SandboxEngineBay.Z, Game.MakeEngineMinFit, 30f, "%.2f m"u8);
            SandboxEngineBay = Vector3.Clamp(SandboxEngineBay, new Vector3(Game.MakeEngineMinFit), new Vector3(Game.MakeEngineMaxFit));
        }
        ImGui.SetNextItemWidth(160 * _scale);
        ImGui.InputInt("Seed (varies the layout and pieces, not the power)##sbeseed"u8, ref SandboxEngineSeed, 1, 100);
        SandboxEngineSeed = Math.Clamp(SandboxEngineSeed, 0, 999_999);
        ImGui.Spacing();
        var make = BigButton("MAKE ENGINE##sbemake");
        ImGui.SameLine(0, 16 * _scale);
        ImGui.AlignTextToFramePadding();
        var notice = game.SandboxNotice;
        if (notice.Length > 0)
            ImGuiSafe.TextColored(new float4(1f, 0.85f, 0.5f, 1f), notice);
        if (make) // (noted in the drawing, sent after it)
            game.SandboxMakeEngine(SandboxEngineKw, SandboxEngineTier, SandboxEngineLayout, SandboxEngineBoost, SandboxEngineSize,
                SandboxEngineFits ? SandboxEngineBay : Vector3.Zero, SandboxEngineSeed, ghost: true);
        if (make)
            CloseSandboxParts(); // (placed with a ghost: GameSandboxBlueprintGhost.cs)
    }
}
