namespace CallOfSlop.VehicleKit;

/// <summary>The factions' styles (docs/vehiclekit/VEHICLEKIT.md, "A faction style").</summary>
public static class Styles
{
    /// <summary>Bandits: rust and bare metal, nothing matches, spikes, war paint and trophies.</summary>
    public static readonly FactionStyle Bandit = new()
    {
        Name = "Bandit",
        Group = "Bandit",
        Palette = new()
        {
            ["rust"] = ["#7a4326", "#8c5230", "#6b3a22", "#94603a", "#5e3320"],
            ["body"] = ["#8a8d8f", "#6f7477", "#a39a84", "#7a4326", "#5f6a55", "#8c5230"], // (bare metal, primer, old paint)
            ["frame"] = ["#3a3330", "#46392f", "#2f2c2a"],
            ["war"] = ["#a81c14", "#d8cdb0", "#c8a018", "#17181a"],
        },
        Parts = new()
        {
            ["wheel"] = ["Off-Road Wheel"],
            ["spring"] = ["Suspension"],
            ["differential"] = ["Differential"],
            ["engine"] = ["Engine", "Engine", "V8 Engine"],
            ["gearbox"] = ["Automatic Gearbox"],
            ["deck"] = ["Plate Steel", "Tread Plate", "Sheet Steel"],
            ["bed"] = ["Plate Steel", "Sheet Steel", "Tread Plate", "Wood Planks"],
            ["sheet"] = ["Sheet Steel", "Sheet Steel", "Plate Steel", "Tread Plate"],
            ["grille"] = ["Mesh Grate"],
            ["patch"] = ["Sheet Steel 0.5 m", "Plate Steel 0.5 m", "Tread Plate 0.5 m"],
            ["spike"] = ["Iron Spike", "Iron Spike", "Long Iron Spike"],
            ["trophy"] = ["Skull Trophy", "Horned Skull", "Hubcap Plate"],
            ["loot"] = ["Ammo Crate", "Ammo Crate", "Ammo Box", "Small Fuel Tank"],
        },
        // The weapon slot. Another branch adds the flamethrower and the harpoon launcher: each is one more line here.
        Weapons = [new WeaponOption("Mounted MG")],
        Limits = new() { ["crookedPlates"] = 4, ["tiltDegrees"] = 5, ["thirdSeat"] = 0.5 },
    };
}
