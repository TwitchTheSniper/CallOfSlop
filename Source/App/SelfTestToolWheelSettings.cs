using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Tool wheel settings" (a user's crash, 2026-11-07: taking the texture paint tool off the wheel in Settings > Tool wheel
    /// closed the game). Loads several saved wheels (the user's own line, the default, empty, one tool, unknown names, every
    /// tool, repeats) and, drawing the screen headless between steps, takes off each tool in turn, adds each tool that is off,
    /// moves each one earlier and later, empties the wheel and puts one tool back, and goes back to the default. Every step:
    /// nothing throws, no ImGui error, the on and off lists hold every tool once, the saved lists load back to the same wheel,
    /// and the match's wheel made from them has a segment per tool, drawn open.
    /// </summary>
    private static void CheckToolWheelSettings()
    {
        var all = (int)SandboxToolWheel.Slot.COUNT;
        string[][] files =
        [
            ["tool_wheel=PhysicsGun,Nudge,Welder,Configure,Hands,Remover,Linker,Selector", "tool_wheel_off=Tracks,GroundDrill,Paint,NoCollide", "tool_wheel_order=Welder=Freeform attach|Socket attach|Rope|Spring|Cylinder"],
            ["tool_wheel=default"],
            ["tool_wheel=", "tool_wheel_off=" + string.Join(",", Enum.GetNames<SandboxToolWheel.Slot>().Where(n => n != "COUNT"))],
            ["tool_wheel=Texture", "tool_wheel_off=" + string.Join(",", Enum.GetNames<SandboxToolWheel.Slot>().Where(n => n is not ("COUNT" or "Texture")))],
            ["tool_wheel=Hands,Hands,Bogus,7,COUNT,Texture,Fuse", "tool_wheel_off=Nothing"],
            ["tool_wheel=" + string.Join(",", Enum.GetNames<SandboxToolWheel.Slot>().Where(n => n != "COUNT").Reverse()), "tool_wheel_off="],
            ["tool_wheel=Selector,Fuse", "tool_wheel_off=Texture,Hands", "tool_wheel_order=Texture=Recent textures|Part;Fuse=x"],
        ];
        var tmp = Path.Combine(Path.GetTempPath(), $"cos-twsettings-{Environment.ProcessId}.txt");
        var ctx = ImGui.CreateContext();
        var errors0 = Ui.ImGuiErrors;
        var steps = 0;
        var bad = new List<string>();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = default;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset | ImGuiBackendFlags.RendererHasTextures;
            io.DisplaySize = new float2(1280, 720);
            io.DeltaTime = 1f / 60;
            Ui.SafeErrors(io);
            for (var f = 0; f < files.Length; f++)
            {
                File.WriteAllLines(tmp, files[f]);
                var where = $"file {f + 1}";
                try
                {
                    var s0 = Settings.Load(tmp);
                    var start = SandboxToolWheel.Resolve(s0.ToolWheel, s0.ToolWheelOff);
                    // Each action on each index, from the file's own wheel.
                    var cases = new List<(Ui.TwAct act, int at)> { (Ui.TwAct.Default, 0) };
                    for (var i = 0; i < all; i++)
                    {
                        cases.Add((Ui.TwAct.TakeOff, i));
                        cases.Add((Ui.TwAct.Earlier, i));
                        cases.Add((Ui.TwAct.Later, i));
                        cases.Add((Ui.TwAct.Add, i));
                    }
                    foreach (var (act, at) in cases)
                    {
                        where = $"file {f + 1}, {act} {at}";
                        var s = Settings.Load(tmp);
                        var ui = new Ui { TwNoSaveTest = true };
                        TwFrame(ui, s);
                        if (act != Ui.TwAct.Add && at >= ui.TwOnTest.Count || act == Ui.TwAct.Add && at >= ui.TwOffTest.Count)
                            continue;
                        if (act != Ui.TwAct.Add)
                        {
                            ui.TwSelectTest(at);
                            TwFrame(ui, s); // (the picked tool's ring drawn, as the screen does before its button is pressed)
                        }
                        ui.TwApply(s, act, at);
                        TwFrame(ui, s);
                        TwFrame(ui, s);
                        TwCheck(ui, s, where, bad);
                        steps++;
                    }
                    // Take every tool off, one by one from the end (the user's case: the last on the list), then add Hands back.
                    {
                        where = $"file {f + 1}, emptied";
                        var s = Settings.Load(tmp);
                        var ui = new Ui { TwNoSaveTest = true };
                        TwFrame(ui, s);
                        for (var guard = 0; ui.TwOnTest.Count > 0 && guard < all + 2; guard++)
                        {
                            ui.TwSelectTest(ui.TwOnTest.Count - 1);
                            TwFrame(ui, s);
                            ui.TwApply(s, Ui.TwAct.TakeOff);
                            TwFrame(ui, s);
                            TwCheck(ui, s, where, bad);
                            steps++;
                        }
                        if (ui.TwOnTest.Count != 0)
                            bad.Add($"{where}: {ui.TwOnTest.Count} tools left after taking every one off");
                        var hands = -1;
                        for (var i = 0; i < ui.TwOffTest.Count; i++)
                            if (ui.TwOffTest[i] == (int)SandboxToolWheel.Slot.Hands)
                                hands = i;
                        ui.TwApply(s, Ui.TwAct.Add, hands);
                        TwFrame(ui, s);
                        TwCheck(ui, s, where + " then Hands", bad);
                        if (ui.TwOnTest.Count != 1)
                            bad.Add($"{where}: {ui.TwOnTest.Count} tools after adding Hands to an empty wheel, want 1");
                    }
                    if (start.Count == 0 && files[f][0] != "tool_wheel=")
                        bad.Add($"file {f + 1}: an empty wheel from a file that names tools");
                }
                catch (Exception ex)
                {
                    bad.Add($"{where}: threw {ex.GetType().Name}: {ex.Message}");
                }
            }
            if (Ui.ImGuiErrors != errors0)
                bad.Add($"{Ui.ImGuiErrors - errors0} frames with ImGui errors");
        }
        finally
        {
            Ui.ImGuiErrors = errors0;
            ImString.SharedStorage.Reset();
            ImGui.DestroyContext(ctx);
            try { File.Delete(tmp); } catch (IOException) { }
        }
        foreach (var b in bad.Take(8))
            Fail($"tool wheel settings: {b}");
        Line($"tool wheel settings: {files.Length} saved wheels, {steps} changes drawn (take off, add, move, empty, default), {bad.Count} problems");
    }

    /// <summary>One frame of the tool wheel's screen.</summary>
    private static void TwFrame(Ui ui, Settings s)
    {
        ImGui.NewFrame();
        ImGui.SetNextWindowSize(new float2(900, 700), ImGuiCond.Always);
        ImGui.Begin("tool wheel test"u8);
        try
        {
            ui.DrawToolWheelEditor(s);
        }
        finally
        {
            ImGui.End();
            ImGui.Render();
            FakeTextureUploads(ImGui.GetDrawData());
            Ui.CountImGuiErrors();
        }
    }

    /// <summary>The screen's lists hold every tool once, the saved lists load back to them, and the match's wheel from them works.</summary>
    private static void TwCheck(Ui ui, Settings s, string where, List<string> bad)
    {
        var all = (int)SandboxToolWheel.Slot.COUNT;
        var on = ui.TwOnTest;
        var off = ui.TwOffTest;
        var seen = new int[all];
        foreach (var t in on.Concat(off))
            if (t >= 0 && t < all)
                seen[t]++;
            else
                bad.Add($"{where}: tool {t} isn't a tool");
        if (seen.Any(n => n != 1) || on.Count + off.Count != all)
            bad.Add($"{where}: the lists don't hold every tool once (on {on.Count}, off {off.Count})");
        var back = SandboxToolWheel.Resolve(s.ToolWheel, s.ToolWheelOff);
        if (!back.SequenceEqual(on))
            bad.Add($"{where}: the saved wheel loads back as {string.Join(",", back)}, the screen shows {string.Join(",", on)}");
        var wheel = new SandboxToolWheel();
        wheel.SetKeys(s);
        if (wheel.Layout.Length != on.Count)
            bad.Add($"{where}: the match's wheel has {wheel.Layout.Length} tools, the screen {on.Count}");
        for (var i = 0; i < wheel.Layout.Length; i++)
            _ = wheel.ItemLabel(wheel.SlotAt(i));
        if (wheel.Layout.Length == 0)
            return;
        ImGui.NewFrame();
        try
        {
            wheel.Menu.Open();
            for (var k = 0; k < 8; k++)
            {
                var a = k * MathF.Tau / 8;
                wheel.Menu.Move(new Vector2(MathF.Sin(a), -MathF.Cos(a)) * 400f - wheel.Menu.Pointer, 0.05f);
                wheel.Draw(1f);
            }
            var pick = wheel.Menu.Release();
            if (pick >= wheel.Layout.Length)
                bad.Add($"{where}: the match's wheel picked segment {pick} of {wheel.Layout.Length}");
        }
        finally
        {
            ImGui.Render();
            FakeTextureUploads(ImGui.GetDrawData());
            Ui.CountImGuiErrors();
        }
    }
}
