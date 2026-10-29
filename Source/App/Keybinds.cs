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
    ];

    /// <summary>
    /// Version of <see cref="Defaults"/>, saved as <c>binds_version</c> in settings.txt. Files from before a change get
    /// <see cref="Migrate"/>d once on load. 1 (no line in the file): Equipment F, Interact E. 2: Interact F, Equipment E.
    /// 3: chat and voice keys added (Chat all Enter, Chat team Y, Push to talk X), each only if no other action has the key.
    /// 4: Audio mixer F8 added (same rule). 5: Spectate next / previous (Zombies) on the Right / Left arrows (same rule).
    /// 6: Weapon tweaks F7 added (same rule). 7: Drop item T and Parts inventory I (Zombies) added (same rule).
    /// 8: Performance window F9 added (same rule). 9: dev tools: Look-at inspector F6, Copy look-at reference F5 and Prop
    /// editor F10 added (same rule).
    /// </summary>
    public const int DefaultsVersion = 9;

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
        foreach (var version in Changes.Select(c => c.Version).Distinct().Order())
        {
            if (version <= fromVersion)
                continue;
            foreach (var c in Changes)
                if (c.Version == version && c.Old == Unbound && binds[(int)c.Action] == c.New)
                    binds[(int)c.Action] = Unbound; // an added action on its new default: check it like a move
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
                        clash |= i != a && binds[i] == binds[a];
                    if (clash)
                    {
                        binds[a] = before[a];
                        changed = true;
                    }
                }
            }
            moved.AddRange(step.Where(c => binds[(int)c.Action] != before[(int)c.Action]).Select(c => c.Action));
        }
        return moved;
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
            GameAction.Scoreboard => "Scoreboard (hold)",
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
            GameAction.Inventory => "Parts inventory: drop loose parts (Zombies)",
            GameAction.PerfWindow => "Performance window (window, corner overlay, off)",
            GameAction.LookAtInspector => "Look-at inspector (dev: what the crosshair is on)",
            GameAction.CopyLookAt => "Copy the look-at reference (dev, inspector open)",
            GameAction.PropEditor => "Prop editor (dev: the prop under the crosshair)",
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

    public static bool IsDown(GlfwWindow window, int code)
    {
        if (code == Unbound)
            return false;
        if (code < 0)
            return window.GetMouseButton((GlfwMouseButton)(-code - 2)) == GlfwButtonAction.Press;
        return window.GetKey((GlfwKey)code) == GlfwKeyAction.Press;
    }

    /// <summary>Binds <paramref name="code"/> to <paramref name="action"/>. If another action had it, the two swap.</summary>
    public static void Assign(int[] binds, GameAction action, int code)
    {
        var old = binds[(int)action];
        for (var i = 0; i < binds.Length; i++)
            if (i != (int)action && binds[i] == code)
                binds[i] = old;
        binds[(int)action] = code;
    }
}
