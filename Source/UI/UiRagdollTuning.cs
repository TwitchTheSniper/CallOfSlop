using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The ragdoll tuning panel (a button in the F7 weapon tweak window): a slider per <see cref="RagdollTuning"/> value
/// (soft joint limits, the relax pull, shot and blast pushes), live and local (ragdolls are cosmetic), with the file's
/// value and a reset per row, and Reload Ragdolls.json. Closed, it costs one bool check a frame.
/// </summary>
public sealed partial class Ui
{
    public bool RagdollTuningOpen;

    private static readonly string[] RtSliderIds = RagdollTuning.Params.Select(p => "##rt_" + p.Key).ToArray();
    private static readonly string[] RtResetIds = RagdollTuning.Params.Select(p => "R##rtr_" + p.Key).ToArray();

    private void DrawRagdollTuning()
    {
        if (!RagdollTuningOpen)
            return;
        RagdollTuning.EnsureLoaded();
        ImGui.SetNextWindowSize(new float2(560 * _scale, 640 * _scale), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(0.94f);
        if (ImGui.Begin("Ragdoll tuning###ragdolltuning"u8, ref RagdollTuningOpen))
        {
            ImGui.TextColored(Dim, "Live, on this PC only (ragdolls are cosmetic). The file is Ragdolls/Ragdolls.json."u8);
            if (ImGui.Button("Reload Ragdolls.json"u8))
                RagdollTuning.Reload();
            if (RagdollTuning.LoadErrors.Count > 0)
                ImGuiSafe.TextColored(Orange, RagdollTuning.LoadErrors[0]);
            if (ImGui.BeginTable("##rttable"u8, 3, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
            {
                var ps = RagdollTuning.Params;
                for (var i = 0; i < ps.Length; i++)
                {
                    ref readonly var p = ref ps[i];
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(p.Label);
                    if (ImGui.IsItemHovered())
                        ImGuiSafe.SetItemTooltip(p.Help);
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(-1);
                    var v = RagdollTuning.Values[(int)p.Id];
                    if (ImGui.SliderFloat(RtSliderIds[i], ref v, p.Min, p.Max, "%.4g"u8))
                        RagdollTuning.Values[(int)p.Id] = Math.Clamp(v, p.Min, p.Max);
                    ImGui.TableNextColumn();
                    ImGui.BeginDisabled(RagdollTuning.Values[(int)p.Id] == p.Default);
                    if (ImGui.SmallButton(RtResetIds[i]))
                        RagdollTuning.Values[(int)p.Id] = p.Default;
                    ImGui.EndDisabled();
                }
                ImGui.EndTable();
            }
        }
        ImGui.End();
    }
}
