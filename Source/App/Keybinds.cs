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
        Key(GlfwKey.F),
        Key(GlfwKey.V),
    ];

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
            GameAction.Grenade => "Throw grenade",
            GameAction.Equipment => "Use equipment",
            GameAction.Melee => "Melee",
            _ => a.ToString(),
        };

    public static string Name(int code)
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
