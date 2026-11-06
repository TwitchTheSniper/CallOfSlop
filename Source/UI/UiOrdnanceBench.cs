using System.Text;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>The Ordnance Bench's window (docs/sandbox/CONSTRUCTS.md, "Modular ordnance"): sections from nose to tail, their settings, the numbers, and saved designs. The server checks everything.</summary>
public sealed partial class Ui
{
    public bool OrdnanceBenchOpen;

    private Game? _ordGame;
    private OrdnanceDesign _ord = new();
    private ImInputString? _ordName, _ordFilter;
    private ConstructPartDef[]? _ordSections;

    public void OpenOrdnanceBench(Game game)
    {
        _ordGame = game;
        OrdnanceBenchOpen = true;
        OrdnanceStore.EnsureLoaded();
        _ordSections ??= ConstructParts.All.Where(d => d.Ord != null).OrderBy(d => d.Ord!.Slot, StringComparer.Ordinal).ThenBy(d => d.Name, StringComparer.Ordinal).ToArray();
    }

    /// <summary>The window, between NewFrame and Render.</summary>
    public void DrawOrdnanceBench()
    {
        if (!OrdnanceBenchOpen || _ordGame is not { } game || _ordSections == null)
            return;
        var open = true;
        ImGui.SetNextWindowSize(new float2(860 * _scale, 560 * _scale), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Ordnance Bench###ordbench"u8, ref open, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse))
        {
            var kind = (int)_ord.Kind;
            if (ImGui.RadioButton("Bomb"u8, kind == 0))
                _ord.Kind = OrdKind.Bomb;
            ImGui.SameLine();
            if (ImGui.RadioButton("Missile"u8, kind == 1))
                _ord.Kind = OrdKind.Missile;
            ImGui.SameLine();
            ImGui.SetNextItemWidth(160 * _scale);
            var cm = _ord.DiameterCm;
            if (ImGui.InputInt("Diameter, cm"u8, ref cm, 5, 25))
                _ord.DiameterCm = Math.Clamp(cm, OrdnanceDesign.MinCm, OrdnanceDesign.MaxCm);
            if (ImGui.BeginTable("##ordcols"u8, 3, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
            {
                ImGui.TableNextColumn();
                OrdList();
                ImGui.TableNextColumn();
                OrdStack();
                ImGui.TableNextColumn();
                OrdNumbers(game);
                ImGui.EndTable();
            }
        }
        ImGui.End();
        if (!open)
            OrdnanceBenchOpen = false;
    }

    private void OrdList()
    {
        ImGui.TextColored(Orange, "SECTIONS"u8);
        _ordFilter ??= new ImInputString(33);
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##ordfilter"u8, "Filter..."u8, _ordFilter);
        var filter = ImGuiSafe.Text(_ordFilter);
        ImGui.BeginChild("##ordlist"u8, new float2(0, 0));
        var slot = "";
        foreach (var d in _ordSections!)
        {
            var o = d.Ord!;
            if (_ord.Kind == OrdKind.Bomb ? !o.ForBomb : !o.ForMissile)
                continue;
            if (filter.Length > 0 && !d.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;
            if (o.Slot != slot)
            {
                slot = o.Slot;
                ImGuiSafe.TextDisabled(slot);
            }
            if (ImGui.Selectable($"{d.Name}##ordadd{d.Id}"))
            {
                var values = new float[o.Settings.Length];
                for (var k = 0; k < values.Length; k++)
                    values[k] = o.Settings[k].Default;
                if (_ord.Pieces.Count > 0 && _ord.Pieces[^1].Section == d && values.Length == 0)
                    _ord.Pieces[^1] = _ord.Pieces[^1] with { Count = _ord.Pieces[^1].Count + 1 };
                else
                    _ord.Pieces.Add(new OrdPiece(d, 1, values));
            }
            if (ImGui.IsItemHovered())
                ImGuiSafe.SetTooltip(d.Description);
        }
        ImGui.EndChild();
    }

    private void OrdStack()
    {
        ImGui.TextColored(Orange, "NOSE"u8);
        ImGui.BeginChild("##ordstack"u8, new float2(0, -ImGui.GetTextLineHeightWithSpacing()));
        for (var i = 0; i < _ord.Pieces.Count; i++)
        {
            var p = _ord.Pieces[i];
            ImGui.PushID(i);
            if (ImGui.SmallButton("^"u8) && i > 0)
                (_ord.Pieces[i - 1], _ord.Pieces[i]) = (_ord.Pieces[i], _ord.Pieces[i - 1]);
            ImGui.SameLine();
            if (ImGui.SmallButton("v"u8) && i + 1 < _ord.Pieces.Count)
                (_ord.Pieces[i + 1], _ord.Pieces[i]) = (_ord.Pieces[i], _ord.Pieces[i + 1]);
            ImGui.SameLine();
            var gone = ImGui.SmallButton("x"u8);
            ImGui.SameLine();
            ImGuiSafe.TextWrapped(p.Section.Name);
            ImGui.SetNextItemWidth(110 * _scale);
            var count = p.Count;
            if (ImGui.InputInt("in a row"u8, ref count))
                _ord.Pieces[i] = p with { Count = Math.Clamp(count, 1, OrdnanceDesign.MaxCount) };
            var s = p.Section.Ord!.Settings;
            for (var k = 0; k < s.Length && k < p.Values.Length; k++)
            {
                ImGui.SetNextItemWidth(160 * _scale);
                ImGui.SliderFloat($"{s[k].Label} {s[k].Unit}##ords{k}", ref p.Values[k], s[k].Min, s[k].Max);
            }
            ImGui.PopID();
            ImGui.Separator();
            if (gone)
            {
                _ord.Pieces.RemoveAt(i);
                break;
            }
        }
        ImGui.EndChild();
        ImGui.TextColored(Orange, "TAIL"u8);
    }

    private void OrdNumbers(Game game)
    {
        var st = _ord.Stats();
        ImGui.TextColored(Orange, "NUMBERS"u8);
        ImGuiSafe.TextWrapped($"Mass {st.Mass:0.#} kg");
        ImGuiSafe.TextWrapped($"Length {st.Length:0.##} m, {st.Diameter * 100:0} cm across");
        ImGuiSafe.TextWrapped($"Payload {st.Payload:0.#} kg");
        ImGuiSafe.TextWrapped($"Blast {st.Blast:0.#} m, damage {st.Damage:0}");
        ImGuiSafe.TextWrapped($"Crater {st.Crater:0.#} m, fuse {st.Fuse:0.##} s");
        ImGuiSafe.TextDisabled($"Blast grows with the cube root of the filling, damage with its square root. Survival: {st.Items} section parts.");
        OrdCurve(st);
        OrdFlight(st);
        if (st.Problem.Length > 0)
            ImGuiSafe.TextColored(Orange, st.Problem);
        else if (ImGui.Button("Build"u8))
            game.SandboxSendOrdnance(_ord.Key());
        ImGui.Separator();
        ImGui.TextColored(Orange, "SAVED DESIGNS"u8);
        ImGui.SetNextItemWidth(160 * _scale);
        _ordName ??= new ImInputString(33);
        ImGui.InputTextWithHint("##ordname"u8, "Name"u8, _ordName);
        ImGui.SameLine();
        if (ImGui.Button("Save"u8) && st.Problem.Length == 0)
            OrdnanceStore.Put(ImGuiSafe.Text(_ordName), _ord.Key());
        for (var i = 0; i < OrdnanceStore.Designs.Count; i++)
        {
            var (name, key) = OrdnanceStore.Designs[i];
            ImGui.PushID(i);
            if (ImGui.SmallButton("x"u8))
            {
                OrdnanceStore.Remove(i);
                ImGui.PopID();
                break;
            }
            ImGui.SameLine();
            if (ImGui.Selectable(name) && OrdnanceDesign.Parse(key, out _) is { } d)
            {
                _ord = d;
                ImGuiSafe.SetInput(_ordName, name);
            }
            ImGui.PopID();
        }
    }

    /// <summary>The blast's curve: radius against kilograms of filling, this design's marked.</summary>
    private void OrdCurve(OrdStats st)
    {
        var w = MathF.Max(120f, ImGui.GetContentRegionAvail().X);
        var h = 70 * _scale;
        var p0 = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##ordcurve"u8, new float2(w, h));
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p0, p0 + new float2(w, h), new ImColor8(20, 22, 26, 255));
        var top = MathF.Max(100f, st.Payload * 2f);
        var most = OrdnanceDesign.Charge(top, top, top, top).Blast;
        float2 At(float kg) => new(p0.X + kg / top * w, p0.Y + h - OrdnanceDesign.Charge(kg, kg, kg, kg).Blast / most * (h - 4));
        var prev = At(0);
        for (var i = 1; i <= 40; i++)
        {
            var next = At(top * i / 40f);
            dl.AddLine(prev, next, new ImColor8(220, 150, 60, 255), 2f * _scale);
            prev = next;
        }
        if (st.Payload > 0)
        {
            var me = new float2(p0.X + st.Payload / top * w, p0.Y + h - st.Blast / most * (h - 4));
            ImDrawListExtensions.AddCircleFilled(dl, in me, 4f * _scale, new ImColor8(255, 230, 90, 255), 12);
        }
        if (ImGui.IsItemHovered())
            ImGuiSafe.SetTooltip($"Blast radius against filling, 0 to {top:0} kg ({most:0.#} m). The dot is this design (plain filling).");
    }

    /// <summary>The flight: thrust, stages, range, steering, seeker, fuses, charges and the engine limits it meets.</summary>
    private static void OrdFlight(OrdStats st)
    {
        var f = st.Flight;
        if (f.Stages.Exists(g => g.Thrust > 0))
        {
            ImGuiSafe.TextWrapped($"Thrust {f.ThrustWeight:0.##}x its weight at launch{(f.ThrustWeight < 1 ? ": too weak to leave a rail" : "")}");
            for (var i = 0; i < f.Stages.Count; i++)
            {
                var g = f.Stages[i];
                if (g.Thrust > 0)
                    ImGuiSafe.TextWrapped($"Stage {i + 1}: {g.Thrust / 1000f:0.#} kN for {g.Burn:0.#} s ({g.Fuels}{(g.AirThrust > 0 ? ", air only" : "")}{(g.MinSpeed > 0 ? $", from {g.MinSpeed:0} m/s" : "")}), {g.Mass:0} kg{(i + 1 < f.Stages.Count ? ", drops when spent" : "")}");
            }
            ImGuiSafe.TextWrapped($"About {f.Range:0} m on a level run, {f.TopSpeed:0} m/s at best");
        }
        if (f.TurnAir + f.TurnBurn + f.TurnAny > 0)
            ImGuiSafe.TextWrapped($"Turns {f.TurnAir:0} deg/s on fins at speed, {f.TurnBurn:0} while burning, {f.TurnAny:0} any time");
        if (f.Lift > 0)
            ImGuiSafe.TextWrapped($"Wings hold up {MathF.Min(1, f.Lift) * 100:0}% of its weight at speed{(f.Deploy > 0 ? $", open after {f.Deploy:0.#} s" : "")}: about {f.Glide:0.#} m along for each metre down");
        ImGuiSafe.TextWrapped($"Drag {f.Drag * 100:0.#}% of its speed a second at 100 m/s{(f.Deploy > 0 && f.Lift <= 0 ? $", once its chute opens ({f.Deploy:0.#} s)" : "")}");
        if (f.Seek != OrdSeek.None)
            ImGuiSafe.TextWrapped($"Seeker: {f.Seek.ToString().ToLowerInvariant()}, {f.SeekReach:0} m{(f.Seek is OrdSeek.Laser or OrdSeek.Radio ? $", channel {f.Channel:0}" : "")}");
        if (f.Fuses != OrdFuse.None)
            ImGuiSafe.TextWrapped($"Fuses: impact{((f.Fuses & OrdFuse.Prox) != 0 ? $", proximity {f.Prox:0} m" : "")}{((f.Fuses & OrdFuse.Altitude) != 0 ? $", altitude {f.FuseHeight:0} m" : "")}{((f.Fuses & OrdFuse.Airburst) != 0 ? $", airburst {f.FuseDistance:0} m from its aim" : "")}{((f.Fuses & OrdFuse.Timer) != 0 ? $", timer {f.Timer:0.#} s" : "")}{((f.Fuses & OrdFuse.Remote) != 0 ? $", remote on channel {f.RemoteChannel:0}" : "")}");
        for (var i = 0; i < f.Charges.Count; i++)
        {
            var c = f.Charges[i];
            if (f.Charges.Count > 1 || c.Sub != OrdSub.None || c.Kinetic)
                ImGuiSafe.TextWrapped($"Charge {i + 1}: {c.Payload:0} kg, {c.Blast:0.#} m{(c.Sub != OrdSub.None ? $", lets go {c.Count} {OrdFlightStats.SubWord(c.Sub)}" : "")}{(c.Kinetic ? ", hits with its speed" : "")}");
        }
        foreach (var line in f.Limits)
            ImGuiSafe.TextDisabled($"Limit: {line}.");
    }
}
