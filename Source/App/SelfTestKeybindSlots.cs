using Brutal.GlfwApi;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Several bindings per action (binds version 24, Keybinds.Slots): old settings lines (one <c>bind_X</c> per action,
    /// <c>bind_ToolWheel=-4</c> the middle button) load as the primaries with the new second defaults migrated in where
    /// free; new lines (<c>bind2_X</c>, <c>bind3_X</c>) load back, cleared ones stay cleared; a saved file's <c>bind_X</c>
    /// lines are plain numbers an older build still reads; any binding works its action (pressed with the first, released
    /// with the last), the Shift chord and context rules work per binding, and the controls screen's clash check sees a
    /// key two live actions share. Uses temporary files only. (Made after a settings crash: binding storage changed.)
    /// </summary>
    private static void CheckKeybindSlots()
    {
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        var d = Keybinds.Defaults;
        var mmb = Keybinds.Mouse(GlfwMouseButton.Middle);
        var grave = Keybinds.Key(GlfwKey.GraveAccent);
        var alt = Keybinds.Key(GlfwKey.LeftAlt);
        var wheel1 = Keybinds.At(GameAction.ToolWheel, 1);
        var wheel2 = Keybinds.At(GameAction.ToolWheel, 2);
        Want(d[(int)GameAction.ToolWheel] == mmb && d[wheel1] == grave && d[wheel2] == Keybinds.Unbound && Duplicates(d).Length == 0,
            $"the tool wheel's defaults aren't the middle button and the grave accent key (or the defaults clash: {Duplicates(d)})");

        var tmp = Path.Combine(Path.GetTempPath(), "callofslop-keyslots-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            Settings Load(params string[] lines)
            {
                File.WriteAllLines(tmp, ["name=Slots", .. lines]);
                return Settings.Load(tmp);
            }

            // 1. A binds version 23 file with only bind_ lines: primaries as written, the wheel's second binding added.
            var s = Load("binds_version=23", "bind_ToolWheel=-4", $"bind_Melee={Keybinds.Key(GlfwKey.V)}");
            Want(s.Binds[(int)GameAction.ToolWheel] == mmb && s.Binds[wheel1] == grave && s.Binds[wheel2] == Keybinds.Unbound,
                $"a version 23 file with bind_ToolWheel=-4 loads the wheel as {Keybinds.Name(s.Binds[(int)GameAction.ToolWheel])} / {Keybinds.Name(s.Binds[wheel1])} / {Keybinds.Name(s.Binds[wheel2])}");
            // 2. ...where the player put the grave accent key on Melee: the wheel's second binding stays unbound (no clash made).
            s = Load("binds_version=23", "bind_ToolWheel=-4", $"bind_Melee={grave}");
            Want(s.Binds[(int)GameAction.Melee] == grave && s.Binds[wheel1] == Keybinds.Unbound,
                $"a version 23 file with Melee on the grave accent key gave the wheel's second binding {Keybinds.Name(s.Binds[wheel1])}");
            // 3. A file from before binds_version (version 1) with no tool wheel line at all: middle button and grave accent.
            s = Load($"bind_Interact={Keybinds.Key(GlfwKey.E)}", $"bind_Equipment={Keybinds.Key(GlfwKey.F)}");
            Want(s.Binds[(int)GameAction.ToolWheel] == mmb && s.Binds[wheel1] == grave && s.Binds[(int)GameAction.Interact] == Keybinds.Key(GlfwKey.F),
                "a version 1 file doesn't get today's tool wheel bindings");
            // 4. New lines: three bindings, one second binding cleared, a bad line and an out-of-range action skipped.
            s = Load("binds_version=24", "bind_ToolWheel=-4", $"bind2_ToolWheel={alt}", "bind3_ToolWheel=-5", $"bind2_Jump={Keybinds.Unbound}",
                $"bind2_Fire={Keybinds.Key(GlfwKey.Enter)},{Keybinds.Key(GlfwKey.K)}", "bind3_COUNT=5", "bind2_999=5", "bind4_Jump=5");
            Want(s.Binds[(int)GameAction.ToolWheel] == mmb && s.Binds[wheel1] == alt && s.Binds[wheel2] == Keybinds.Mouse((GlfwMouseButton)3),
                $"bind2_/bind3_ lines load the wheel as {Keybinds.Name(s.Binds[(int)GameAction.ToolWheel])} / {Keybinds.Name(s.Binds[wheel1])} / {Keybinds.Name(s.Binds[wheel2])}");
            Want(s.Binds[Keybinds.At(GameAction.Fire, 1)] == Keybinds.Unbound && s.Binds[Keybinds.At(GameAction.Jump, 1)] == Keybinds.Unbound,
                "a bad bind2_ line or a cleared one didn't leave the binding unbound");
            // 5. Saved and loaded back the same; every bind_ line is a plain number (what an older build parses), one per action.
            s.Binds[Keybinds.At(GameAction.Jump, 2)] = Keybinds.Key(GlfwKey.Kp0);
            s.Save(tmp);
            var lines = File.ReadAllLines(tmp);
            var back = Settings.Load(tmp);
            Want(back.Binds.SequenceEqual(s.Binds), "the bindings don't load back as saved");
            var primaries = lines.Where(l => l.StartsWith("bind_")).ToArray();
            Want(primaries.Length == (int)GameAction.COUNT && primaries.All(l => int.TryParse(l[(l.IndexOf('=') + 1)..], out _)),
                $"the saved file has {primaries.Length} bind_ lines, or one an older build can't read as a number");
            Want(lines.Count(l => l.StartsWith("bind2_") || l.StartsWith("bind3_")) == (int)GameAction.COUNT * (Keybinds.Slots - 1), "the saved file doesn't write every second and third binding");
            // 6. The old file of the first case, saved by this build and read back: still the same.
            s = Load("binds_version=23", "bind_ToolWheel=-4");
            s.Save(tmp);
            Want(Settings.Load(tmp).Binds.SequenceEqual(s.Binds), "a migrated version 23 file doesn't load back as saved");
        }
        finally
        {
            try { File.Delete(tmp); } catch (IOException) { }
        }

        // 7. Any binding works the action: pressed with the first down, still down while one is, released with the last.
        var binds = (int[])d.Clone();
        Keybinds.Assign(binds, GameAction.ToolWheel, 2, alt);
        var slotDown = new bool[Keybinds.Total];
        var down = new bool[(int)GameAction.COUNT];
        var prev = new bool[(int)GameAction.COUNT];
        var trace = new List<string>();
        void Frame(bool sandbox, params int[] held)
        {
            Array.Copy(down, prev, down.Length);
            for (var i = 0; i < slotDown.Length; i++)
                slotDown[i] = binds[i] != Keybinds.Unbound && held.Contains(binds[i]);
            Keybinds.Resolve(binds, sandbox, slotDown);
            Keybinds.Collapse(slotDown, down);
        }
        bool W() => down[(int)GameAction.ToolWheel];
        bool Pressed() => W() && !prev[(int)GameAction.ToolWheel];
        bool Released() => !W() && prev[(int)GameAction.ToolWheel];
        foreach (var code in new[] { mmb, grave, alt })
        {
            Frame(true);
            Frame(true, code);
            Want(W() && Pressed(), $"{Keybinds.Name(code)} alone doesn't open the tool wheel");
        }
        Frame(true);
        Frame(true, mmb);
        var p1 = Pressed();
        Frame(true, mmb, grave);
        var p2 = Pressed();
        Frame(true, grave);
        var still = W() && !Released();
        Frame(true);
        var rel = Released();
        trace.Add($"middle: pressed {p1}; + grave: pressed again {p2}; middle up: still down {still}; grave up: released {rel}");
        Want(p1 && !p2 && still && rel, "the tool wheel isn't pressed once with the first binding and released with the last");
        Frame(false, grave);
        Want(!W(), "a Sandbox action's second binding works outside Sandbox");

        // 8. Chords per binding: Shift + Left rolls and hides the plain Left, but a second turn key held still turns.
        Keybinds.Assign(binds, GameAction.RotateLeft, 1, Keybinds.Key(GlfwKey.Kp4));
        Frame(true, Keybinds.Key(GlfwKey.Left), Keybinds.Shift(GlfwKey.Left));
        var rollOnly = down[(int)GameAction.RollLeft] && !down[(int)GameAction.RotateLeft];
        Frame(true, Keybinds.Key(GlfwKey.Left), Keybinds.Shift(GlfwKey.Left), Keybinds.Key(GlfwKey.Kp4));
        var both = down[(int)GameAction.RollLeft] && down[(int)GameAction.RotateLeft];
        Want(rollOnly && both, "the Shift chord hides a turn key's other binding, or doesn't hide its plain key");
        // The one-per-action form (the older tests' primaries) gives the same answer.
        var one = new bool[(int)GameAction.COUNT];
        for (var i = 0; i < one.Length; i++)
            one[i] = binds[i] == Keybinds.Key(GlfwKey.Left) || binds[i] == Keybinds.Shift(GlfwKey.Left);
        Keybinds.Resolve(binds, true, one);
        Want(one[(int)GameAction.RollLeft] && !one[(int)GameAction.RotateLeft], "Resolve on one entry per action doesn't apply the chord rule");

        // 9. Assigning: the same key in another slot of the action is cleared; a key another live action has is kept by both and flagged.
        Keybinds.Assign(binds, GameAction.ToolWheel, 1, alt);
        Want(binds[wheel1] == alt && binds[wheel2] == Keybinds.Unbound, "the same key twice on one action wasn't cleared from the other slot");
        Want(Keybinds.ClashAt(binds, wheel1) == (int)GameAction.GantryOrbit && binds[(int)GameAction.GantryOrbit] == alt,
            "Left Alt on the tool wheel and the gantry's orbit isn't flagged as a clash, or the orbit lost its key");
        Want(Keybinds.ClashAt(d, (int)GameAction.RotateLeft) == -1 && Keybinds.ClashAt(d, (int)GameAction.Noclip) == -1, "a default key shared across contexts (or allowed) is flagged");
        Keybinds.Clear(binds, GameAction.ToolWheel, 1);
        Want(binds[wheel1] == Keybinds.Unbound && Keybinds.First(binds, GameAction.ToolWheel) == mmb, "clearing a binding didn't, or the first binding isn't the primary");
        Keybinds.Clear(binds, GameAction.ToolWheel, 0);
        Keybinds.Assign(binds, GameAction.ToolWheel, 2, grave);
        Want(Keybinds.First(binds, GameAction.ToolWheel) == grave && Keybinds.Has(binds, GameAction.ToolWheel, grave) && !Keybinds.Has(binds, GameAction.ToolWheel, Keybinds.Unbound),
            "with the primary cleared the HUD's key isn't the next binding");

        // 10. Nothing allocates.
        Frame(true, mmb);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 200; k++)
        {
            Keybinds.Resolve(binds, (k & 1) == 0, slotDown, (k & 2) == 0);
            Keybinds.Collapse(slotDown, down);
            Keybinds.Resolve(binds, true, one);
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Want(bytes == 0, $"resolving the bindings allocated {bytes} bytes");

        Line($"slots: {Keybinds.Slots} per action; tool wheel {Keybinds.Name(d[(int)GameAction.ToolWheel])} / {Keybinds.Name(d[wheel1])}; {string.Join("; ", trace)}; {bytes} bytes in 600 resolves");
        if (problems.Count > 0)
            Fail("keybind slots: " + string.Join("; ", problems));
    }
}
