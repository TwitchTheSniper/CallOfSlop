using System.Text;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>Everything the player can rebind.</summary>
public enum GameAction
{
    MoveForward,
    MoveBack,
    MoveLeft,
    MoveRight,
    Jump,
    Sprint,
    CrouchToggle,
    CrouchHold,
    Prone,
    Fire,
    Aim,
    Reload,
    PrimaryWeapon,
    SecondaryWeapon,
    SwapWeapon,
    Loadout,
    Scoreboard,
    Grenade,
    Equipment,
    Melee,
    Streak1,
    Streak2,
    Streak3,
    Ready,
    Interact,
    Weapon3,
    Weapon4,
    ChatAll, // binds version 3: chat (Source/UI/UiChat.cs) and voice (Source/Audio/Voice)
    ChatTeam,
    PushToTalk,
    AudioMixer, // the Audio mixer debug window (UiAudioMixer.cs)
    SpectateNext, // binds version 5: Zombies, watching a teammate after bleeding out (Program.Zombies.cs)
    SpectatePrev,
    WeaponTweaks, // binds version 6: the weapon tweak debug window (UiWeaponTweaks.cs)
    DropItem, // binds version 7: Zombies, drop the gun in your hands as a pickup (ZombiesDropItems.cs)
    Inventory, // Zombies: the parts inventory (drop loose parts; UiZombiesInventory.cs)
    PerfWindow, // binds version 8: the performance window / overlay (UiPerf.cs, Program.Perf.cs)
    LookAtInspector, // binds version 9: the dev look-at inspector (UiDevTools.cs, Program.DevTools.cs)
    CopyLookAt, // copy the inspector's reference line to the clipboard
    PropEditor, // the dev prop editor on the prop under the crosshair
    ToolWheel, // binds version 10: Sandbox, hold for the tool wheel (SandboxToolWheel.cs)
    Stow, // binds version 11: Sandbox, the part in your hands into your backpack (ConstructBackpack.cs)
    DrawPack, // Sandbox: the next part in your backpack into your hands
    RotateLeft, // binds version 12: Sandbox, turn the ghost part 90 degrees as you see it (Constructs/GhostTurn.cs)
    RotateRight,
    RotateUp, // tip it towards you (its top comes towards you, its front rises)
    RotateDown,
    RollLeft, // about the axis pointing away from you (Shift + Left by default)
    RollRight,
    SandboxInventory, // binds version 13: Sandbox, the inventory (parts, blueprints, tools; UiSandboxParts.cs) on Tab: there is no scoreboard there
    FreeRouting, // binds version 14: Sandbox, held while routing a link: no port snapping or branching (Program.SandboxLinks.cs; Left Shift, beside Sprint)
    SnapSwitch, // binds version 15: Sandbox, with a part held, switch which of its own connection points mates (Constructs/SnapBuild.cs)
    RecentParts, // Sandbox, hold for the quick wheel of the last 8 parts you placed (UI/SandboxRecentWheel.cs)
    ThirdPerson, // binds version 16: Sandbox, switch to the third-person camera (Source/App/ThirdPerson.cs; on T, which is Drop item in Zombies)
    ThirdPersonSide, // Sandbox: swap the third-person camera's shoulder
    SandboxDrop, // Sandbox: drop the part (or hose) in your hands; Drop item (T) is Zombies' only now
    WeatherMap, // binds version 17: the weather map window (maps with a "weather" section; Source/UI/UiWeather.cs) on F11
    Noclip, // binds version 18: creative Sandbox, fly with no collision (Source/Game/GameNoclip.cs; on V, which it shares with Melee: see Overlap)
    BuildMirror, // binds version 19: Sandbox build stations (Program.SandboxAids.cs): the mirror plane: off, then each axis
    BuildLine, // the Line Layer's drag on or off
    BuildReplace, // the Replacer on or off
    BuildGantry, // the Build Gantry's camera on or off
    BuildPlaneUp, // the gantry's build plane up a step, or the mirror plane's offset
    BuildPlaneDown,
    GantryOrbit, // held in the gantry's camera: the mouse turns the view instead of moving the cursor
    SelectMove, // binds version 19: Sandbox, the selector's actions on the group selected (Program.SandboxSelect.cs)
    SelectCopy,
    SelectPaint,
    SelectWeld,
    SelectSave, // ...save it as a blueprint (a Blueprint Desk's aid)
    SelectTool, // the selector itself, out or away (it has no slot on the tool wheel; the Tab inventory's Tools tab lists it too)
    StoryWindow, // binds version 20: Sandbox, the story window (the story world, its chronicle, the stories in progress; Source/UI/UiStory.cs) on F4
    FactionWindow, // binds version 21: Sandbox, the faction window (found, invite, join, give; Source/UI/UiFactions.cs) on F2
    SeatRollLeft, // binds version 22: in a seat, roll left and right (the seat's Roll axis: ConstructParts.KeyRollLeft); on Q and E, which do this only in a seat (see Resolve)
    SeatRollRight,
    SeatHook, // binds version 23: in a seat, hook to a launch rail, launch, undock (ConstructCarrier.cs)
    SeatEject, // ...and an ejection seat's handle (PilotGear.cs)
    Logbook, // binds version 25: Sandbox, the Logbook (the old records' fragments you hold, and your open leads; Source/UI/UiLogbook.cs) on F1
    JoinTalk, // binds version 26: Sandbox, join the talk another player is having with one of the story's people, while its notice shows (Program.StoryTalk.cs) on Backspace
    COUNT,
}

/// <summary>
/// Key and mouse bindings. A binding is an int: a <see cref="GlfwKey"/> value for keys, or
/// <c>-(button + 2)</c> for mouse buttons, so both fit in one settings value.
/// <para>
/// Each action has <see cref="Slots"/> bindings (primary, secondary, third), any of which works it: the action is down
/// while any of its bindings is held, so it is pressed when the first goes down and released when the last comes up.
/// A binds array holds them slot by slot: <c>binds[slot * COUNT + action]</c> (<see cref="At"/>), so <c>binds[(int)a]</c>
/// is the primary binding and code that only knows one binding per action still reads that. settings.txt keeps the
/// primary in <c>bind_X=CODE</c> (as before) and the others in <c>bind2_X</c> and <c>bind3_X</c>, which older builds ignore.
/// </para>
/// </summary>
public static class Keybinds
{
    public const int Unbound = int.MinValue;

    /// <summary>Bindings per action: primary, secondary, third.</summary>
    public const int Slots = 3;

    /// <summary>Actions.</summary>
    public const int Count = (int)GameAction.COUNT;

    /// <summary>Length of a binds array: every action's every slot.</summary>
    public const int Total = Count * Slots;

    /// <summary>The index of <paramref name="a"/>'s binding in <paramref name="slot"/> (0 is the primary).</summary>
    public static int At(GameAction a, int slot) => slot * Count + (int)a;

    /// <summary>The action a binds index belongs to.</summary>
    public static GameAction ActionAt(int index) => (GameAction)(index % Count);

    /// <summary>The first binding <paramref name="a"/> has (the primary unless that is cleared), or <see cref="Unbound"/>: what the HUD names.</summary>
    public static int First(int[] binds, GameAction a)
    {
        for (var s = 0; s < Slots; s++)
            if (binds[s * Count + (int)a] is var c and not Unbound)
                return c;
        return Unbound;
    }

    /// <summary>Is <paramref name="code"/> one of <paramref name="a"/>'s bindings?</summary>
    public static bool Has(int[] binds, GameAction a, int code)
    {
        if (code == Unbound)
            return false;
        for (var s = 0; s < Slots; s++)
            if (binds[s * Count + (int)a] == code)
                return true;
        return false;
    }

    /// <summary>Do <paramref name="a"/> and <paramref name="b"/> have a binding in common?</summary>
    public static bool ShareKey(int[] binds, GameAction a, GameAction b)
    {
        for (var s = 0; s < Slots; s++)
            if (Has(binds, b, binds[s * Count + (int)a]))
                return true;
        return false;
    }

    /// <summary>Is any of <paramref name="a"/>'s bindings held (the real keys and buttons)?</summary>
    public static bool IsDown(GlfwWindow window, int[] binds, GameAction a)
    {
        for (var s = 0; s < Slots; s++)
            if (IsDown(window, binds[s * Count + (int)a]))
                return true;
        return false;
    }

    /// <summary>
    /// Another action live beside the binding at <paramref name="index"/> on the same key (a clash the controls screen
    /// warns about), or -1. Nothing allocates.
    /// </summary>
    public static int ClashAt(int[] binds, int index)
    {
        var code = binds[index];
        if (code == Unbound)
            return -1;
        var a = ActionAt(index);
        for (var i = 0; i < binds.Length; i++)
            if (binds[i] == code && ActionAt(i) != a && Overlap(ActionAt(i), a))
                return (int)ActionAt(i);
        return -1;
    }

    public static int Mouse(GlfwMouseButton b) => -((int)b + 2);

    public static int Key(GlfwKey k) => (int)k;

    /// <summary>A Shift chord (Shift held with the key) as a binding: the key's code with this bit set. Only Sandbox actions take chords (Shift is the sprint key everywhere else).</summary>
    public const int ShiftFlag = 1 << 24;

    public static int Shift(GlfwKey k) => (int)k | ShiftFlag;

    /// <summary>Is <paramref name="code"/> a Shift chord?</summary>
    public static bool IsChord(int code) => code != Unbound && code >= 0 && (code & ShiftFlag) != 0;

    // ---- Contexts: one key can do different things in different modes ------------------------------------------

    /// <summary>
    /// Where an action is live (board item dev-sandbox-tab-inventory-ti01): everywhere, only in Sandbox, or in every mode
    /// but Sandbox. Two actions clash on a key only when their contexts overlap, so a Sandbox key may share a key with a
    /// key the other modes use (the rotate keys and the Zombies spectate keys on the arrows).
    /// </summary>
    public enum BindContext : byte
    {
        Any,
        Sandbox,
        NotSandbox,
        Seat, // only in a construct's seat, in Sandbox: while one is held, whatever else is on its key is off (Resolve)
    }

    public static BindContext Context(GameAction a) =>
        a switch
        {
            GameAction.ToolWheel or GameAction.Stow or GameAction.DrawPack or GameAction.RotateLeft or GameAction.RotateRight or GameAction.RotateUp
                or GameAction.RotateDown or GameAction.RollLeft or GameAction.RollRight or GameAction.SandboxInventory or GameAction.FreeRouting or GameAction.SnapSwitch or GameAction.RecentParts
                or GameAction.ThirdPerson or GameAction.ThirdPersonSide or GameAction.SandboxDrop or GameAction.Noclip or GameAction.BuildMirror or GameAction.BuildLine or GameAction.BuildReplace
                or GameAction.BuildGantry or GameAction.BuildPlaneUp or GameAction.BuildPlaneDown or GameAction.GantryOrbit or GameAction.SelectMove or GameAction.SelectCopy or GameAction.SelectPaint or GameAction.SelectWeld or GameAction.SelectSave or GameAction.SelectTool or GameAction.StoryWindow or GameAction.FactionWindow or GameAction.Logbook => BindContext.Sandbox,
            // (Sandbox has no scoreboard at all; its inventory is GameAction.SandboxInventory, the Zombies parts inventory stays on its own key)
            GameAction.SeatRollLeft or GameAction.SeatRollRight or GameAction.SeatHook or GameAction.SeatEject => BindContext.Seat,
            GameAction.SpectateNext or GameAction.SpectatePrev or GameAction.Scoreboard or GameAction.Inventory or GameAction.DropItem => BindContext.NotSandbox,
            _ => BindContext.Any,
        };

    /// <summary>The first Sandbox action in the list (the keybinds screen puts the Sandbox heading above it).</summary>
    public static readonly GameAction FirstSandboxAction = Enumerable.Range(0, (int)GameAction.COUNT).Select(i => (GameAction)i).First(a => Context(a) == BindContext.Sandbox);

    /// <summary>Can actions <paramref name="a"/> and <paramref name="b"/> both be live at once (so sharing a key is a clash)?</summary>
    public static bool Overlap(GameAction a, GameAction b)
    {
        if (a == GameAction.FreeRouting && b == GameAction.Sprint || a == GameAction.Sprint && b == GameAction.FreeRouting)
            return false; // (Free routing is a held modifier beside Sprint: both on Left Shift by default, sprinting while routing is fine)
        if (a == GameAction.Noclip && b == GameAction.Melee || a == GameAction.Melee && b == GameAction.Noclip)
            return false; // (both on V by default: noclip has the key where it is on offer, creative Sandbox, and Melee everywhere else: Program.Noclip.cs)
        if (a is >= GameAction.SelectMove and <= GameAction.SelectWeld && b is >= GameAction.BuildMirror and <= GameAction.BuildGantry || b is >= GameAction.SelectMove and <= GameAction.SelectWeld && a is >= GameAction.BuildMirror and <= GameAction.BuildGantry)
            return false; // (the selector's actions and the stations' switches share keys: the switches are off while the selector is out: Program.SandboxAids.cs)
        var x = Context(a);
        var y = Context(b);
        if (x == BindContext.Seat || y == BindContext.Seat)
            return x == y || LiveInSeat(x == BindContext.Seat ? b : a); // (the roll keys take a key over in a seat, so they clash only with what a seat itself reads)
        return x == BindContext.Any || y == BindContext.Any || x == y;
    }

    /// <summary>The actions a seated player's controls use: the roll keys can't share a key with these.</summary>
    private static bool LiveInSeat(GameAction a) =>
        a is GameAction.MoveForward or GameAction.MoveBack or GameAction.MoveLeft or GameAction.MoveRight or GameAction.Jump or GameAction.Sprint or GameAction.CrouchHold or GameAction.CrouchToggle
            or GameAction.Fire or GameAction.Aim or GameAction.Reload or GameAction.Interact;

    /// <summary>Is <paramref name="a"/> live in a match that is (<paramref name="sandbox"/>) or isn't Sandbox, with the player in a seat or not?</summary>
    public static bool ActiveIn(GameAction a, bool sandbox, bool seated = false) =>
        Context(a) switch
        {
            BindContext.Sandbox => sandbox,
            BindContext.NotSandbox => !sandbox,
            BindContext.Seat => sandbox && seated,
            _ => true,
        };

    /// <summary>
    /// This frame's raw key states made into what the game reads. <paramref name="down"/> is either one entry per
    /// binding (<see cref="Total"/>, <c>down[i]</c>: binding i is held) or one per action (<see cref="Count"/>: the
    /// primary bindings only, as the older tests give it). An action whose context isn't live is off, while a Shift
    /// chord is held the plain bindings on the same key are off (Shift + Left rolls instead of also turning), and in a
    /// seat a roll key held turns off whatever else is on its key (Q and E roll, not swap weapons or use equipment).
    /// Each rule works on bindings, so an action stays down through another binding the rule didn't touch. Nothing allocates.
    /// </summary>
    public static void Resolve(int[] binds, bool sandbox, bool[] down, bool seated = false)
    {
        if (down.Length == binds.Length)
        {
            ResolveSlots(binds, sandbox, down, seated);
            return;
        }
        // (one per action: the primaries, put through the same rules in a scratch row)
        lock (Scratch)
        {
            Array.Clear(Scratch);
            for (var i = 0; i < down.Length && i < Count; i++)
                Scratch[i] = down[i];
            ResolveSlots(binds, sandbox, Scratch, seated);
            for (var i = 0; i < down.Length && i < Count; i++)
                down[i] = Scratch[i];
        }
    }

    private static readonly bool[] Scratch = new bool[Total];

    /// <summary>Per-binding states (from <see cref="Resolve"/>) into per-action ones: an action is down while any of its bindings is.</summary>
    public static void Collapse(bool[] slotDown, bool[] down)
    {
        for (var a = 0; a < down.Length; a++)
        {
            var on = false;
            for (var s = 0; s < Slots; s++)
                on |= slotDown[s * Count + a];
            down[a] = on;
        }
    }

    private static void ResolveSlots(int[] binds, bool sandbox, bool[] down, bool seated)
    {
        for (var i = 0; i < down.Length; i++)
            if (down[i] && !ActiveIn(ActionAt(i), sandbox, seated))
                down[i] = false;
        if (sandbox && seated)
            for (var i = 0; i < down.Length; i++)
            {
                if (!down[i] || ActionAt(i) is < GameAction.SeatRollLeft or > GameAction.SeatEject)
                    continue;
                for (var j = 0; j < down.Length; j++)
                    if (ActionAt(j) != ActionAt(i) && binds[j] == binds[i])
                        down[j] = false;
            }
        for (var i = 0; i < down.Length; i++)
        {
            if (!down[i] || !IsChord(binds[i]))
                continue;
            var key = binds[i] & ~ShiftFlag;
            for (var j = 0; j < down.Length; j++)
                if (binds[j] == key)
                    down[j] = false;
        }
    }

    private static int[] MakeDefaults()
    {
        var d = new int[Total];
        Array.Fill(d, Unbound);
        PrimaryDefaults.CopyTo(d, 0);
        foreach (var (a, code) in SecondDefaults)
            d[At(a, 1)] = code;
        return d;
    }

    /// <summary>
    /// Second bindings by default, for what a laptop can't press: the tool wheel (the middle mouse button) also on the key
    /// left of 1 (grave accent; Left Alt is the gantry camera's orbit key, live with the wheel). Binds version 24.
    /// </summary>
    private static readonly (GameAction Action, int Code)[] SecondDefaults =
    [
        (GameAction.ToolWheel, Key(GlfwKey.GraveAccent)),
    ];

    private static readonly int[] PrimaryDefaults =
    [
        Key(GlfwKey.W),
        Key(GlfwKey.S),
        Key(GlfwKey.A),
        Key(GlfwKey.D),
        Key(GlfwKey.Space),
        Key(GlfwKey.LeftShift),
        Key(GlfwKey.C),
        Key(GlfwKey.LeftControl),
        Key(GlfwKey.Z),
        Mouse(GlfwMouseButton.Left),
        Mouse(GlfwMouseButton.Right),
        Key(GlfwKey.R),
        Key(GlfwKey.Number1),
        Key(GlfwKey.Number2),
        Key(GlfwKey.Q),
        Key(GlfwKey.B),
        Key(GlfwKey.Tab),
        Key(GlfwKey.G),
        Key(GlfwKey.E), // Equipment (tactical): F until binds version 2
        Key(GlfwKey.V),
        Key(GlfwKey.Number4),
        Key(GlfwKey.Number5),
        Key(GlfwKey.Number6),
        Key(GlfwKey.F3),
        Key(GlfwKey.F), // Interact: E until binds version 2
        Key(GlfwKey.Number3),
        Unbound,
        Key(GlfwKey.Enter), // Chat (all): binds version 3
        Key(GlfwKey.Y), // Chat (team)
        Key(GlfwKey.X), // Push to talk
        Key(GlfwKey.F8), // Audio mixer: binds version 4
        Key(GlfwKey.Right), // Spectate next (Zombies): binds version 5 (Fire also works while spectating)
        Key(GlfwKey.Left), // Spectate previous (Aim also works)
        Key(GlfwKey.F7), // Weapon tweaks (dev window): binds version 6
        Key(GlfwKey.T), // Drop item (Zombies): binds version 7 (G is the grenade key)
        Key(GlfwKey.I), // Parts inventory (Zombies)
        Key(GlfwKey.F9), // Performance window: binds version 8
        Key(GlfwKey.F6), // Look-at inspector (dev): binds version 9
        Key(GlfwKey.F5), // Copy the look-at reference (dev)
        Key(GlfwKey.F10), // Prop editor (dev)
        Mouse(GlfwMouseButton.Middle), // Tool wheel (Sandbox): binds version 10 (Q is Swap weapon)
        Key(GlfwKey.H), // Stow in the backpack (Sandbox): binds version 11
        Key(GlfwKey.J), // Draw from the backpack (Sandbox)
        Key(GlfwKey.Left), // Rotate left (Sandbox): binds version 12 (the Zombies spectate keys share the arrows: other context)
        Key(GlfwKey.Right), // Rotate right (Sandbox)
        Key(GlfwKey.Up), // Rotate up (Sandbox)
        Key(GlfwKey.Down), // Rotate down (Sandbox)
        Shift(GlfwKey.Left), // Roll left (Sandbox)
        Shift(GlfwKey.Right), // Roll right (Sandbox)
        Key(GlfwKey.Tab), // Inventory (Sandbox): binds version 13 (the Scoreboard's Tab is off in Sandbox: other context)
        Key(GlfwKey.LeftShift), // Free routing (Sandbox linking tool): binds version 14 (shares Left Shift with Sprint: Overlap)
        Key(GlfwKey.K), // Switch the held part's connection point (Sandbox): binds version 15 (F is Interact, the middle button the tool wheel and the pick)
        Key(GlfwKey.L), // Recent parts wheel (Sandbox, hold)
        Key(GlfwKey.T), // Third person (Sandbox): binds version 16 (Drop item, also T, is Zombies' now: other context)
        Key(GlfwKey.N), // Third-person shoulder swap (Sandbox)
        Key(GlfwKey.Delete), // Drop the held part (Sandbox; Delete): the old Sandbox use of Drop item's T
        Key(GlfwKey.F11), // Weather map: binds version 17
        Key(GlfwKey.V), // Noclip (creative Sandbox): binds version 18 (shares Melee's V: Overlap)
        Key(GlfwKey.M), // Mirror plane (Sandbox): binds version 19
        Key(GlfwKey.O), // Line and fill
        Key(GlfwKey.P), // Replace part
        Key(GlfwKey.U), // Gantry camera
        Key(GlfwKey.PageUp), // Build plane up
        Key(GlfwKey.PageDown), // Build plane down
        Key(GlfwKey.LeftAlt), // Orbit, in the gantry's camera
        Key(GlfwKey.M), // Selector: move the group (Sandbox; shares Mirror's M: Overlap)
        Key(GlfwKey.O), // Selector: copy it (shares Line's O)
        Key(GlfwKey.P), // Selector: paint it (shares Replace's P)
        Key(GlfwKey.U), // Selector: weld it (shares Gantry's U)
        Key(GlfwKey.Comma), // Selector: save it as a blueprint
        Key(GlfwKey.Period), // The selector, out or away
        Key(GlfwKey.F4), // Story window (Sandbox): binds version 20
        Key(GlfwKey.F2), // Faction window (Sandbox): binds version 21
        Key(GlfwKey.Q), // Roll left, in a seat: binds version 22 (shares Swap weapon's Q: Overlap)
        Key(GlfwKey.E), // Roll right, in a seat (shares Equipment's E)
        Key(GlfwKey.G), // Hook, launch, undock, in a seat: binds version 23
        Key(GlfwKey.L), // Eject, in an ejection seat
        Key(GlfwKey.F1), // Logbook (Sandbox): binds version 25
        Key(GlfwKey.Backspace), // Join another player's talk (Sandbox): binds version 26
    ];

    /// <summary>
    /// Every binding's default, slot by slot (<see cref="At"/>): the primaries, then the second and third bindings, which
    /// are unbound but where a laptop lacks the primary (<see cref="SecondDefaults"/>). Declared after the lists it is
    /// made from: static fields start in the order they are written.
    /// </summary>
    public static readonly int[] Defaults = MakeDefaults();

    /// <summary>
    /// Version of <see cref="Defaults"/>, saved as <c>binds_version</c> in settings.txt. Files from before a change get
    /// <see cref="Migrate"/>d once on load. 1 (no line in the file): Equipment F, Interact E. 2: Interact F, Equipment E.
    /// 3: chat and voice keys added (Chat all Enter, Chat team Y, Push to talk X), each only if no other action has the key.
    /// 4: Audio mixer F8 added (same rule). 5: Spectate next / previous (Zombies) on the Right / Left arrows (same rule).
    /// 6: Weapon tweaks F7 added (same rule). 7: Drop item T and Parts inventory I (Zombies) added (same rule).
    /// 8: Performance window F9 added (same rule). 9: dev tools: Look-at inspector F6, Copy look-at reference F5 and Prop
    /// editor F10 added (same rule). 10: Tool wheel (Sandbox) on the middle mouse button added (same rule). 11: the
    /// Sandbox backpack's Stow H and Draw J added (same rule). 12: the Sandbox ghost's rotate keys (the arrows; roll on
    /// Shift + Left / Right) added (same rule; keys only clash within overlapping contexts, see <see cref="Context"/>).
    /// 13: the Sandbox inventory became its own action on Tab (Sandbox has no scoreboard); a player who had rebound the
    /// parts inventory key keeps that key for the Sandbox inventory (<see cref="Carries"/>). 14: Free routing (Sandbox
    /// linking tool) on Left Shift added (same rule; it may share Sprint's key, see <see cref="Overlap"/>).
    /// 15: snappy building: Switch connection point K and Recent parts wheel L (Sandbox; same rule).
    /// 16: Third person (Sandbox) on T and its shoulder swap on N, which Drop item keeps in the other modes (Drop item is now outside Sandbox only);
    /// Sandbox's own Drop part on Delete takes a custom Drop item key over (<see cref="Carries"/>).
    /// 17: Weather map F11 added (same rule). 18: Noclip (creative Sandbox) on V added (same rule; it may share Melee's key, see <see cref="Overlap"/>).
    /// 19: the build stations' keys (Sandbox): Mirror M, Line O, Replace P, Gantry U, build plane Page Up / Down, orbit Left Alt (same rule). The selector on the full stop, its actions on M, O, P, U and comma (the first four share those keys: see <see cref="Overlap"/>).
    /// 20: the Story window (Sandbox) on F4 added (same rule). 21: the Faction window (Sandbox) on F2 added (same rule).
    /// 22: roll left and right in a seat on Q and E added (same rule; they share those keys, see <see cref="Overlap"/> and <see cref="Resolve"/>).
    /// 23: Hook / launch / undock G and Eject L in a seat added (same rule).
    /// 24: several bindings per action (<see cref="Slots"/>; <c>bind2_X</c> and <c>bind3_X</c> lines); the tool wheel's second
    /// binding on the grave accent key added (same rule: left unbound when the player uses that key for something live beside it).
    /// 25: the Logbook (Sandbox) on F1 added (same rule).
    /// 26: Join another player's talk (Sandbox) on Backspace added (same rule).
    /// </summary>
    public const int DefaultsVersion = 26;

    /// <summary>Default keys that changed, per binds version: (version that changed it, action, old default, new default).</summary>
    private static readonly (int Version, int Index, int Old, int New)[] Changes =
    [
        (2, (int)GameAction.Interact, Key(GlfwKey.E), Key(GlfwKey.F)),
        (2, (int)GameAction.Equipment, Key(GlfwKey.F), Key(GlfwKey.E)),
        // Added actions (old default Unbound): an old file has no line for them, so they start on the new default; they are
        // put through the same clash check, so a key the player already uses elsewhere is never taken (the action stays unbound).
        (3, (int)GameAction.ChatAll, Unbound, Key(GlfwKey.Enter)),
        (3, (int)GameAction.ChatTeam, Unbound, Key(GlfwKey.Y)),
        (3, (int)GameAction.PushToTalk, Unbound, Key(GlfwKey.X)),
        (4, (int)GameAction.AudioMixer, Unbound, Key(GlfwKey.F8)),
        (5, (int)GameAction.SpectateNext, Unbound, Key(GlfwKey.Right)),
        (5, (int)GameAction.SpectatePrev, Unbound, Key(GlfwKey.Left)),
        (6, (int)GameAction.WeaponTweaks, Unbound, Key(GlfwKey.F7)),
        (7, (int)GameAction.DropItem, Unbound, Key(GlfwKey.T)),
        (7, (int)GameAction.Inventory, Unbound, Key(GlfwKey.I)),
        (8, (int)GameAction.PerfWindow, Unbound, Key(GlfwKey.F9)),
        (9, (int)GameAction.LookAtInspector, Unbound, Key(GlfwKey.F6)),
        (9, (int)GameAction.CopyLookAt, Unbound, Key(GlfwKey.F5)),
        (9, (int)GameAction.PropEditor, Unbound, Key(GlfwKey.F10)),
        (10, (int)GameAction.ToolWheel, Unbound, Mouse(GlfwMouseButton.Middle)),
        (11, (int)GameAction.Stow, Unbound, Key(GlfwKey.H)),
        (11, (int)GameAction.DrawPack, Unbound, Key(GlfwKey.J)),
        (12, (int)GameAction.RotateLeft, Unbound, Key(GlfwKey.Left)),
        (12, (int)GameAction.RotateRight, Unbound, Key(GlfwKey.Right)),
        (12, (int)GameAction.RotateUp, Unbound, Key(GlfwKey.Up)),
        (12, (int)GameAction.RotateDown, Unbound, Key(GlfwKey.Down)),
        (12, (int)GameAction.RollLeft, Unbound, Shift(GlfwKey.Left)),
        (12, (int)GameAction.RollRight, Unbound, Shift(GlfwKey.Right)),
        (13, (int)GameAction.SandboxInventory, Unbound, Key(GlfwKey.Tab)),
        (14, (int)GameAction.FreeRouting, Unbound, Key(GlfwKey.LeftShift)),
        (15, (int)GameAction.SnapSwitch, Unbound, Key(GlfwKey.K)),
        (15, (int)GameAction.RecentParts, Unbound, Key(GlfwKey.L)),
        (16, (int)GameAction.ThirdPerson, Unbound, Key(GlfwKey.T)),
        (16, (int)GameAction.ThirdPersonSide, Unbound, Key(GlfwKey.N)),
        (16, (int)GameAction.SandboxDrop, Unbound, Key(GlfwKey.Delete)),
        (17, (int)GameAction.WeatherMap, Unbound, Key(GlfwKey.F11)),
        (18, (int)GameAction.Noclip, Unbound, Key(GlfwKey.V)),
        (19, (int)GameAction.BuildMirror, Unbound, Key(GlfwKey.M)),
        (19, (int)GameAction.BuildLine, Unbound, Key(GlfwKey.O)),
        (19, (int)GameAction.BuildReplace, Unbound, Key(GlfwKey.P)),
        (19, (int)GameAction.BuildGantry, Unbound, Key(GlfwKey.U)),
        (19, (int)GameAction.BuildPlaneUp, Unbound, Key(GlfwKey.PageUp)),
        (19, (int)GameAction.BuildPlaneDown, Unbound, Key(GlfwKey.PageDown)),
        (19, (int)GameAction.GantryOrbit, Unbound, Key(GlfwKey.LeftAlt)),
        (19, (int)GameAction.SelectMove, Unbound, Key(GlfwKey.M)),
        (19, (int)GameAction.SelectCopy, Unbound, Key(GlfwKey.O)),
        (19, (int)GameAction.SelectPaint, Unbound, Key(GlfwKey.P)),
        (19, (int)GameAction.SelectWeld, Unbound, Key(GlfwKey.U)),
        (19, (int)GameAction.SelectSave, Unbound, Key(GlfwKey.Comma)),
        (19, (int)GameAction.SelectTool, Unbound, Key(GlfwKey.Period)),
        (20, (int)GameAction.StoryWindow, Unbound, Key(GlfwKey.F4)),
        (21, (int)GameAction.FactionWindow, Unbound, Key(GlfwKey.F2)),
        (22, (int)GameAction.SeatRollLeft, Unbound, Key(GlfwKey.Q)),
        (22, (int)GameAction.SeatRollRight, Unbound, Key(GlfwKey.E)),
        (23, (int)GameAction.SeatHook, Unbound, Key(GlfwKey.G)),
        (23, (int)GameAction.SeatEject, Unbound, Key(GlfwKey.L)),
        (24, At(GameAction.ToolWheel, 1), Unbound, Key(GlfwKey.GraveAccent)), // (a second binding: the laptop's way to the wheel)
        (25, (int)GameAction.Logbook, Unbound, Key(GlfwKey.F1)),
        (26, (int)GameAction.JoinTalk, Unbound, Key(GlfwKey.Backspace)),
    ];

    /// <summary>
    /// An action split in two, per binds version: the new action <c>To</c> takes over what <c>From</c> did in its context,
    /// so a player who had rebound <c>From</c> (off its old default <c>Default</c>) gets that key on <c>To</c> too; one who
    /// never rebound it gets <c>To</c>'s new default (or, when another action already has that, <c>From</c>'s key).
    /// </summary>
    private static readonly (int Version, GameAction From, GameAction To, int Default)[] Carries =
    [
        (13, GameAction.Inventory, GameAction.SandboxInventory, Key(GlfwKey.I)),
        (16, GameAction.DropItem, GameAction.SandboxDrop, Key(GlfwKey.T)),
    ];

    /// <summary>
    /// Updates binds saved under binds version <paramref name="fromVersion"/> to today's defaults: a binding still on its
    /// old default key moves to the new one, and a bind the player changed stays as it is. A move that would put two
    /// actions on one key is skipped (that binding keeps its old key), so migrating never creates a conflict. Changes
    /// name a binding (<see cref="At"/>), so a later version can add a second binding as it would add an action.
    /// </summary>
    /// <returns>The actions that moved, each once.</returns>
    public static List<GameAction> Migrate(int[] binds, int fromVersion)
    {
        var moved = new List<GameAction>();
        // Every binding added after the file's version that sits on its new default is unbound first (and checked like a
        // move at its own version), so an action added later can't hold a key while an earlier version's carry asks
        // whether that key is free (a v12 file's rebound inventory key K and v15's SnapSwitch on K).
        foreach (var c in Changes)
            if (c.Version > fromVersion && c.Index < binds.Length && c.Old == Unbound && binds[c.Index] == c.New)
                binds[c.Index] = Unbound;
        foreach (var version in Changes.Select(c => c.Version).Distinct().Order())
        {
            if (version <= fromVersion)
                continue;
            foreach (var c in Carries)
                if (c.Version == version && binds[(int)c.From] != c.Default && binds[(int)c.From] != Unbound && binds[(int)c.To] == Unbound && FreeFor(binds, c.To, binds[(int)c.From]))
                {
                    binds[(int)c.To] = binds[(int)c.From]; // a custom key carries over to the split-off action (when nothing live beside it has the key)
                    moved.Add(c.To);
                }
            var step = Changes.Where(c => c.Version == version && c.Index < binds.Length && binds[c.Index] == c.Old).ToList();
            var before = (int[])binds.Clone();
            foreach (var c in step)
                binds[c.Index] = c.New;
            // Undo any move that lands on a key another binding now holds; undoing one can clash again, so repeat.
            for (var changed = true; changed;)
            {
                changed = false;
                foreach (var c in step)
                {
                    var a = c.Index;
                    if (binds[a] == before[a])
                        continue;
                    var clash = false;
                    for (var i = 0; i < binds.Length; i++)
                        clash |= i != a && binds[i] == binds[a] && (ActionAt(i) == ActionAt(a) || Overlap(ActionAt(i), ActionAt(a)));
                    if (clash)
                    {
                        binds[a] = before[a];
                        changed = true;
                    }
                }
            }
            moved.AddRange(step.Where(c => binds[c.Index] != before[c.Index]).Select(c => ActionAt(c.Index)));
            // A split-off action whose new default was taken: it keeps the old action's key instead, when that is free in its context.
            foreach (var c in Carries)
            {
                if (c.Version != version || binds[(int)c.To] != Unbound || binds[(int)c.From] == Unbound)
                    continue;
                if (FreeFor(binds, c.To, binds[(int)c.From]))
                {
                    binds[(int)c.To] = binds[(int)c.From];
                    moved.Add(c.To);
                }
            }
        }
        return moved.Count > 1 ? moved.Distinct().ToList() : moved; // (an action named once, however many of its bindings moved)
    }

    /// <summary>Does no other action live beside <paramref name="a"/> hold <paramref name="code"/> (in any of its bindings)?</summary>
    private static bool FreeFor(int[] binds, GameAction a, int code)
    {
        for (var i = 0; i < binds.Length; i++)
            if (ActionAt(i) != a && binds[i] == code && Overlap(ActionAt(i), a))
                return false;
        return true;
    }

    public static string Label(GameAction a) =>
        a switch
        {
            GameAction.MoveForward => "Move forward",
            GameAction.MoveBack => "Move back",
            GameAction.MoveLeft => "Move left",
            GameAction.MoveRight => "Move right",
            GameAction.Jump => "Jump / stand up",
            GameAction.Sprint => "Sprint",
            GameAction.CrouchToggle => "Crouch (toggle)",
            GameAction.CrouchHold => "Crouch (tap) / prone (hold)",
            GameAction.Prone => "Prone (toggle)",
            GameAction.Fire => "Fire",
            GameAction.Aim => "Aim down sights",
            GameAction.Reload => "Reload",
            GameAction.PrimaryWeapon => "Primary weapon",
            GameAction.SecondaryWeapon => "Secondary weapon",
            GameAction.SwapWeapon => "Swap weapon",
            GameAction.Loadout => "Loadout menu",
            GameAction.Scoreboard => "Scoreboard (hold; not in Sandbox)",
            GameAction.Grenade => "Throw grenade (Zombies: take apart at the box)",
            GameAction.Equipment => "Use equipment",
            GameAction.Melee => "Melee",
            GameAction.Streak1 => "Kill streak 1 (cheapest)",
            GameAction.Streak2 => "Kill streak 2",
            GameAction.Streak3 => "Kill streak 3 (biggest)",
            GameAction.Ready => "Ready up (warmup; Zombies: the next round)",
            GameAction.Interact => "Interact (doors; Zombies: cart, slot upgrade)",
            GameAction.Weapon3 => "Weapon slot 3 (Zombies)",
            GameAction.Weapon4 => "Weapon slot 4 (Zombies)",
            GameAction.ChatAll => "Chat (all)",
            GameAction.ChatTeam => "Chat (team)",
            GameAction.PushToTalk => "Push to talk (voice chat)",
            GameAction.AudioMixer => "Audio mixer (debug window)",
            GameAction.SpectateNext => "Spectate next player (Zombies, out)",
            GameAction.SpectatePrev => "Spectate previous player (Zombies, out)",
            GameAction.WeaponTweaks => "Weapon tweaks (debug window)",
            GameAction.DropItem => "Drop the gun in your hands (Zombies; not in Sandbox)",
            GameAction.Inventory => "Parts inventory (Zombies: drop loose parts)",
            GameAction.PerfWindow => "Performance window (window, corner overlay, off)",
            GameAction.LookAtInspector => "Look-at inspector (dev: what the crosshair is on)",
            GameAction.CopyLookAt => "Copy the look-at reference (dev, inspector open)",
            GameAction.PropEditor => "Prop editor (dev: the prop under the crosshair)",
            GameAction.ToolWheel => "Tool wheel (Sandbox: hold, flick to a tool, let go)",
            GameAction.Stow => "Stow in the backpack (Sandbox: what you hold)",
            GameAction.DrawPack => "Draw from the backpack (Sandbox: the next part)",
            GameAction.RotateLeft => "Turn the part left (Sandbox ghost)",
            GameAction.RotateRight => "Turn the part right (Sandbox ghost)",
            GameAction.RotateUp => "Tip the part towards you (Sandbox ghost)",
            GameAction.RotateDown => "Tip the part away from you (Sandbox ghost)",
            GameAction.RollLeft => "Roll the part left (Sandbox ghost)",
            GameAction.RollRight => "Roll the part right (Sandbox ghost)",
            GameAction.SandboxInventory => "Inventory (Sandbox: parts, blueprints, tools; again or Esc closes)",
            GameAction.FreeRouting => "Free routing (Sandbox linking tool: hold for no port snap)",
            GameAction.SnapSwitch => "Switch connection point (Sandbox: which of the held part's points mates)",
            GameAction.RecentParts => "Recent parts wheel (Sandbox: hold, flick, let go)",
            GameAction.ThirdPerson => "Third person (Sandbox: on foot, or the orbit camera in a seat)",
            GameAction.ThirdPersonSide => "Third person: swap shoulder (Sandbox)",
            GameAction.SandboxDrop => "Drop the part in your hands (Sandbox)",
            GameAction.WeatherMap => "Weather map (maps with weather: storms, time speed)",
            GameAction.Noclip => "Noclip (creative Sandbox: fly through everything, Sprint for faster)",
            GameAction.BuildMirror => "Mirror plane (Sandbox: off, then each axis; needs a Mirror Table)",
            GameAction.BuildLine => "Line and fill (Sandbox: drag a row of parts; needs a Line Layer)",
            GameAction.BuildReplace => "Replace part (Sandbox: swap the aimed part; needs a Replacer)",
            GameAction.BuildGantry => "Gantry camera (Sandbox: orbit your build; needs a Build Gantry)",
            GameAction.BuildPlaneUp => "Build plane up (Sandbox: the gantry's plane, or the mirror's offset)",
            GameAction.BuildPlaneDown => "Build plane down (Sandbox: the gantry's plane, or the mirror's offset)",
            GameAction.GantryOrbit => "Orbit (Sandbox gantry camera: hold, and the mouse turns the view)",
            GameAction.SelectMove => "Selector: move the group",
            GameAction.SelectCopy => "Selector: copy the group",
            GameAction.SelectPaint => "Selector: paint the group",
            GameAction.SelectWeld => "Selector: attach the group",
            GameAction.SelectSave => "Selector: save the group as a blueprint",
            GameAction.SelectTool => "Selector: take it out or put it away (build stations)",
            GameAction.StoryWindow => "Story window (Sandbox host: the story world, its chronicle, the stories in progress)",
            GameAction.FactionWindow => "Faction window (Sandbox: found or join a faction, share and give what you own)",
            GameAction.SeatRollLeft => "Roll left (in a seat)",
            GameAction.SeatRollRight => "Roll right (in a seat)",
            GameAction.SeatHook => "Hook, launch or undock (in a seat)",
            GameAction.SeatEject => "Eject (in an ejection seat)",
            GameAction.Logbook => "Logbook (Sandbox: the old records you have found, and your leads)",
            GameAction.JoinTalk => "Join a chat (Sandbox: when another player talks to someone, while the notice shows)",
            _ => a.ToString(),
        };

    /// <summary>
    /// The display name of a key or mouse button code ("W", "Left Shift", "Mouse 4"). The HUD asks for a dozen of these
    /// every frame, so each code's name is worked out once and kept (it would allocate a few strings every call);
    /// <see cref="ForgetNames"/> drops them when the window regains focus, so a keyboard layout switched meanwhile shows.
    /// </summary>
    public static string Name(int code)
    {
        lock (NameCache)
        {
            if (NameCache.TryGetValue(code, out var cached))
                return cached;
            var name = NameUncached(code);
            NameCache[code] = name;
            return name;
        }
    }

    private static readonly Dictionary<int, string> NameCache = new();

    /// <summary>Forgets the cached key names (the window gained focus: the keyboard layout may have changed).</summary>
    public static void ForgetNames()
    {
        lock (NameCache)
            NameCache.Clear();
    }

    private static string NameUncached(int code)
    {
        if (code == Unbound)
            return "-";
        if (IsChord(code))
            return "Shift + " + NameUncached(code & ~ShiftFlag);
        if (code < 0)
        {
            var button = -code - 2;
            return button switch
            {
                0 => "Mouse Left",
                1 => "Mouse Right",
                2 => "Mouse Middle",
                _ => $"Mouse {button + 1}",
            };
        }

        var key = (GlfwKey)code;
        string? printable = null;
        try
        {
            printable = Glfw.GetKeyName(key, 0); // follows the user's keyboard layout for printable keys
        }
        catch
        {
            // Not available for this key.
        }
        if (!string.IsNullOrWhiteSpace(printable))
            return printable.ToUpperInvariant();

        // "LeftShift" -> "Left Shift", "Kp5" -> "Kp 5"
        var raw = key.ToString();
        var sb = new StringBuilder();
        for (var i = 0; i < raw.Length; i++)
        {
            if (i > 0 && (char.IsUpper(raw[i]) || (char.IsDigit(raw[i]) && !char.IsDigit(raw[i - 1]))))
                sb.Append(' ');
            sb.Append(raw[i]);
        }
        return sb.ToString().Replace("Number ", "");
    }

    /// <summary>The key name bound to <paramref name="a"/> in <paramref name="s"/>, or its default key's without settings.</summary>
    public static string NameOf(Settings? s, GameAction a) => Name(s != null ? s.Bind(a) : Defaults[(int)a]);

    /// <summary>Can <paramref name="a"/> be bound to a Shift chord? Sandbox actions only (Shift sprints everywhere else, and a plain key works whatever modifiers are held).</summary>
    public static bool TakesChord(GameAction a) => Context(a) == BindContext.Sandbox;

    public static bool IsDown(GlfwWindow window, int code)
    {
        if (code == Unbound)
            return false;
        if (code < 0)
            return window.GetMouseButton((GlfwMouseButton)(-code - 2)) == GlfwButtonAction.Press;
        if (IsChord(code))
            return window.GetKey((GlfwKey)(code & ~ShiftFlag)) == GlfwKeyAction.Press
                   && (window.GetKey(GlfwKey.LeftShift) == GlfwKeyAction.Press || window.GetKey(GlfwKey.RightShift) == GlfwKeyAction.Press);
        return window.GetKey((GlfwKey)code) == GlfwKeyAction.Press;
    }

    /// <summary>
    /// Binds <paramref name="code"/> to <paramref name="action"/>'s binding in <paramref name="slot"/>. Another action live
    /// at the same time on that key keeps it: the two clash, and the controls screen warns (<see cref="ClashAt"/>) rather
    /// than moving the other one. The same key in another of this action's slots is cleared (it would do nothing twice).
    /// A Shift chord on an action that doesn't take chords binds the plain key.
    /// </summary>
    public static void Assign(int[] binds, GameAction action, int slot, int code)
    {
        if ((uint)slot >= Slots || (uint)At(action, slot) >= (uint)binds.Length)
            return;
        if (IsChord(code) && !TakesChord(action))
            code &= ~ShiftFlag;
        for (var s = 0; s < Slots; s++)
            if (s != slot && At(action, s) < binds.Length && binds[At(action, s)] == code)
                binds[At(action, s)] = Unbound;
        binds[At(action, slot)] = code;
    }

    /// <summary>The primary binding (<see cref="Assign(int[], GameAction, int, int)"/> in slot 0).</summary>
    public static void Assign(int[] binds, GameAction action, int code) => Assign(binds, action, 0, code);

    /// <summary>Clears <paramref name="action"/>'s binding in <paramref name="slot"/>.</summary>
    public static void Clear(int[] binds, GameAction action, int slot)
    {
        if ((uint)slot < Slots && (uint)At(action, slot) < (uint)binds.Length)
            binds[At(action, slot)] = Unbound;
    }
}
