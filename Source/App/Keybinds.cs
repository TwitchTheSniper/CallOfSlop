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
    COUNT,
}

/// <summary>
/// Key and mouse bindings. A binding is an int: a <see cref="GlfwKey"/> value for keys, or
/// <c>-(button + 2)</c> for mouse buttons, so both fit in one settings value.
/// </summary>
public static class Keybinds
{
    public const int Unbound = int.MinValue;

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
    }

    public static BindContext Context(GameAction a) =>
        a switch
        {
            GameAction.ToolWheel or GameAction.Stow or GameAction.DrawPack or GameAction.RotateLeft or GameAction.RotateRight or GameAction.RotateUp
                or GameAction.RotateDown or GameAction.RollLeft or GameAction.RollRight or GameAction.SandboxInventory or GameAction.FreeRouting or GameAction.SnapSwitch or GameAction.RecentParts => BindContext.Sandbox,
            // (Sandbox has no scoreboard at all; its inventory is GameAction.SandboxInventory, the Zombies parts inventory stays on its own key)
            GameAction.SpectateNext or GameAction.SpectatePrev or GameAction.Scoreboard or GameAction.Inventory => BindContext.NotSandbox,
            _ => BindContext.Any,
        };

    /// <summary>The first Sandbox action in the list (the keybinds screen puts the Sandbox heading above it).</summary>
    public static readonly GameAction FirstSandboxAction = Enumerable.Range(0, (int)GameAction.COUNT).Select(i => (GameAction)i).First(a => Context(a) == BindContext.Sandbox);

    /// <summary>Can actions <paramref name="a"/> and <paramref name="b"/> both be live at once (so sharing a key is a clash)?</summary>
    public static bool Overlap(GameAction a, GameAction b)
    {
        if (a == GameAction.FreeRouting && b == GameAction.Sprint || a == GameAction.Sprint && b == GameAction.FreeRouting)
            return false; // (Free routing is a held modifier beside Sprint: both on Left Shift by default, sprinting while routing is fine)
        var x = Context(a);
        var y = Context(b);
        return x == BindContext.Any || y == BindContext.Any || x == y;
    }

    /// <summary>Is <paramref name="a"/> live in a match that is (<paramref name="sandbox"/>) or isn't Sandbox?</summary>
    public static bool ActiveIn(GameAction a, bool sandbox) =>
        Context(a) switch
        {
            BindContext.Sandbox => sandbox,
            BindContext.NotSandbox => !sandbox,
            _ => true,
        };

    /// <summary>
    /// This frame's raw key states (<paramref name="down"/>[i]: action i's binding is held) made into what the game reads:
    /// an action whose context isn't live is off, and while a Shift chord is held the plain actions on the same key are
    /// off (Shift + Left rolls instead of also turning). Nothing allocates.
    /// </summary>
    public static void Resolve(int[] binds, bool sandbox, bool[] down)
    {
        for (var i = 0; i < down.Length; i++)
            if (down[i] && !ActiveIn((GameAction)i, sandbox))
                down[i] = false;
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

    public static readonly int[] Defaults =
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
    ];

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
    /// </summary>
    public const int DefaultsVersion = 15;

    /// <summary>Default keys that changed, per binds version: (version that changed it, action, old default, new default).</summary>
    private static readonly (int Version, GameAction Action, int Old, int New)[] Changes =
    [
        (2, GameAction.Interact, Key(GlfwKey.E), Key(GlfwKey.F)),
        (2, GameAction.Equipment, Key(GlfwKey.F), Key(GlfwKey.E)),
        // Added actions (old default Unbound): an old file has no line for them, so they start on the new default; they are
        // put through the same clash check, so a key the player already uses elsewhere is never taken (the action stays unbound).
        (3, GameAction.ChatAll, Unbound, Key(GlfwKey.Enter)),
        (3, GameAction.ChatTeam, Unbound, Key(GlfwKey.Y)),
        (3, GameAction.PushToTalk, Unbound, Key(GlfwKey.X)),
        (4, GameAction.AudioMixer, Unbound, Key(GlfwKey.F8)),
        (5, GameAction.SpectateNext, Unbound, Key(GlfwKey.Right)),
        (5, GameAction.SpectatePrev, Unbound, Key(GlfwKey.Left)),
        (6, GameAction.WeaponTweaks, Unbound, Key(GlfwKey.F7)),
        (7, GameAction.DropItem, Unbound, Key(GlfwKey.T)),
        (7, GameAction.Inventory, Unbound, Key(GlfwKey.I)),
        (8, GameAction.PerfWindow, Unbound, Key(GlfwKey.F9)),
        (9, GameAction.LookAtInspector, Unbound, Key(GlfwKey.F6)),
        (9, GameAction.CopyLookAt, Unbound, Key(GlfwKey.F5)),
        (9, GameAction.PropEditor, Unbound, Key(GlfwKey.F10)),
        (10, GameAction.ToolWheel, Unbound, Mouse(GlfwMouseButton.Middle)),
        (11, GameAction.Stow, Unbound, Key(GlfwKey.H)),
        (11, GameAction.DrawPack, Unbound, Key(GlfwKey.J)),
        (12, GameAction.RotateLeft, Unbound, Key(GlfwKey.Left)),
        (12, GameAction.RotateRight, Unbound, Key(GlfwKey.Right)),
        (12, GameAction.RotateUp, Unbound, Key(GlfwKey.Up)),
        (12, GameAction.RotateDown, Unbound, Key(GlfwKey.Down)),
        (12, GameAction.RollLeft, Unbound, Shift(GlfwKey.Left)),
        (12, GameAction.RollRight, Unbound, Shift(GlfwKey.Right)),
        (13, GameAction.SandboxInventory, Unbound, Key(GlfwKey.Tab)),
        (14, GameAction.FreeRouting, Unbound, Key(GlfwKey.LeftShift)),
        (15, GameAction.SnapSwitch, Unbound, Key(GlfwKey.K)),
        (15, GameAction.RecentParts, Unbound, Key(GlfwKey.L)),
    ];

    /// <summary>
    /// An action split in two, per binds version: the new action <c>To</c> takes over what <c>From</c> did in its context,
    /// so a player who had rebound <c>From</c> (off its old default <c>Default</c>) gets that key on <c>To</c> too; one who
    /// never rebound it gets <c>To</c>'s new default (or, when another action already has that, <c>From</c>'s key).
    /// </summary>
    private static readonly (int Version, GameAction From, GameAction To, int Default)[] Carries =
    [
        (13, GameAction.Inventory, GameAction.SandboxInventory, Key(GlfwKey.I)),
    ];

    /// <summary>
    /// Updates binds saved under binds version <paramref name="fromVersion"/> to today's defaults: an action still on its
    /// old default key moves to the new one, and a bind the player changed stays as it is. A move that would put two
    /// actions on one key is skipped (that action keeps its old key), so migrating never creates a conflict.
    /// </summary>
    /// <returns>The actions that moved.</returns>
    public static List<GameAction> Migrate(int[] binds, int fromVersion)
    {
        var moved = new List<GameAction>();
        // Every action added after the file's version that sits on its new default is unbound first (and checked like a
        // move at its own version), so an action added later can't hold a key while an earlier version's carry asks
        // whether that key is free (a v12 file's rebound inventory key K and v15's SnapSwitch on K).
        foreach (var c in Changes)
            if (c.Version > fromVersion && c.Old == Unbound && binds[(int)c.Action] == c.New)
                binds[(int)c.Action] = Unbound;
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
            var step = Changes.Where(c => c.Version == version && binds[(int)c.Action] == c.Old).ToList();
            var before = (int[])binds.Clone();
            foreach (var c in step)
                binds[(int)c.Action] = c.New;
            // Undo any move that lands on a key another action now holds; undoing one can clash again, so repeat.
            for (var changed = true; changed;)
            {
                changed = false;
                foreach (var c in step)
                {
                    var a = (int)c.Action;
                    if (binds[a] == before[a])
                        continue;
                    var clash = false;
                    for (var i = 0; i < binds.Length; i++)
                        clash |= i != a && binds[i] == binds[a] && Overlap((GameAction)i, c.Action);
                    if (clash)
                    {
                        binds[a] = before[a];
                        changed = true;
                    }
                }
            }
            moved.AddRange(step.Where(c => binds[(int)c.Action] != before[(int)c.Action]).Select(c => c.Action));
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
        return moved;
    }

    /// <summary>Does no other action live beside <paramref name="a"/> hold <paramref name="code"/>?</summary>
    private static bool FreeFor(int[] binds, GameAction a, int code)
    {
        for (var i = 0; i < binds.Length; i++)
            if (i != (int)a && binds[i] == code && Overlap((GameAction)i, a))
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
            GameAction.DropItem => "Drop the gun in your hands (Zombies)",
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
    /// Binds <paramref name="code"/> to <paramref name="action"/>. If another action live at the same time had it, the two
    /// swap (an action of another context keeps the key: no clash). A Shift chord on an action that doesn't take chords
    /// binds the plain key.
    /// </summary>
    public static void Assign(int[] binds, GameAction action, int code)
    {
        if (IsChord(code) && !TakesChord(action))
            code &= ~ShiftFlag;
        var old = binds[(int)action];
        for (var i = 0; i < binds.Length; i++)
            if (i != (int)action && binds[i] == code && Overlap((GameAction)i, action))
                binds[i] = IsChord(old) && !TakesChord((GameAction)i) ? old & ~ShiftFlag : old;
        binds[(int)action] = code;
    }
}
