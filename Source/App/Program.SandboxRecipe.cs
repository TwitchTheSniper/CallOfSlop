namespace CallOfSlop;

/// <summary>
/// Sandbox: the recipe picker in the match (UiRecipePicker.cs; docs/sandbox/constructs/production.md "The recipe picker"). A Line Console's
/// Recipe button opens it on the line's picked machine (a refiner with recipes to choose; any other machine keeps the button's old step);
/// the configure panel's recipe row opens it too, and so does a machine's own Recipe button (<see cref="MachineRecipePicker"/>). It holds the mouse while open and closes when its machine or console goes, the player
/// dies or walks off, or the configure panel it came from closes.
/// </summary>
public static partial class Program
{
    /// <summary>The Recipe button on a console's line page: true when the picker opened (nothing is sent until a tile is clicked).</summary>
    private static bool LineRecipePicker(Construct c, int console)
    {
        var d = c.DefOf(console);
        if (ConstructConfig.Get(c.Parts[console], d, CfgKey.LinePage, 0) >= 0.5f || MenusOpen)
            return false; // (the bill page's third button is Clear)
        var m = ProductionLines.Picked(c, console, out var same);
        if (m < 0 || !Ui.RecipePickable(c.DefOf(m)))
            return false;
        Ui.OpenRecipePicker(c.Id, console, m, c.DefOf(m), same, Ui.RecipeVariant(c, m));
        SetMouseCaptured(false);
        return true;
    }

    /// <summary>
    /// A machine's own Recipe button (touch action "machine.recipe" on its screen): opens the recipe picker on that machine, as the console's
    /// Recipe button does on its line's pick. True when it opened; false leaves the click to the server's touch (its refusal, or the step
    /// through the recipes on a machine the picker doesn't serve). Any machine with a recipe setting and a list in the picker
    /// (<see cref="Ui.RecipePickable"/>) takes it with one call: put the button on its screen and call this.
    /// </summary>
    internal static bool MachineRecipePicker(Construct c, int machine)
    {
        if (MenusOpen || machine < 0 || machine >= c.PartCount)
            return false;
        var d = c.DefOf(machine);
        if (!Ui.RecipePickable(d) || c.Parts[machine].Broken || !ConstructPower.On(c, machine))
            return false; // (no power: the server's touch says so)
        Ui.OpenRecipePickerMachine(c.Id, machine, d, Ui.RecipeVariant(c, machine));
        SetMouseCaptured(false);
        return true;
    }

    /// <summary>The picker over the match (the Sandbox ImGui pass); it closes (the mouse goes back to aiming) when it should.</summary>
    private static void DrawRecipePickerInMatch(double now)
    {
        if (!Ui.RecipePickerOpen)
            return;
        var g = _game;
        var me = g?.Local;
        var (body, machine, console) = Ui.RecipePickerTarget;
        var c = g?.Constructs.Get(body);
        var gone = _session == null || g is not { SandboxOn: true } || me is not { Alive: true } || g.Phase != MatchPhase.Playing || c == null
            || machine < 0 || machine >= c.PartCount || !Ui.RecipePickable(c.DefOf(machine)) || Ui.RecipeVariant(c, machine) != Ui.RecipePickerVariant // (a press's die changed: its patterns did)
            || (Ui.RecipePickerFromConfig ? !Ui.SandboxConfigOpen : _mouseCaptured || console < 0 || console >= c.PartCount || console != machine && !ConstructConfig.Has(c.DefOf(console), CfgKey.LinePick)
                || System.Numerics.Vector3.Distance(me!.Position, c.WorldCom) > c.Radius + ConstructConfig.Reach + 4);
        if (!gone)
        {
            var md = c!.DefOf(machine);
            var current = (int)ConstructConfig.Get(c.Parts[machine], md, ProductionLines.RecipeKey(md), 0);
            if (Ui.DrawRecipePicker(g!, current, now))
                return;
        }
        Ui.CloseRecipePicker();
        if (!MenusOpen) // (opened from the configure panel, that panel keeps the mouse)
            SetMouseCaptured(true);
        _swallowClick = true; // (the click that picked mustn't also press the console's button again)
    }
}
