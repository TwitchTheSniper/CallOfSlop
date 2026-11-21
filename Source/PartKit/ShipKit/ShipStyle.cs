namespace CallOfSlop.PartKit.ShipKit;

/// <summary>
/// A faction's paint for a lofted ship (docs/sandbox/constructs/ship-kit.md): the skin, the armour, the frame and trim, the inside's
/// walls and decks, and the lights. The kit draws every piece with these, so a ship's colours come from its faction, never from the
/// kit. Shapes are not here: each ship gives its own (the kit's fitting specs).
/// </summary>
public sealed record ShipStyle(
    string Skin, string Skin2, string Armour, string Armour2, string Bare, string Frame, string Dark, string Hazard, string Red,
    string Glow, string Glass, string Link, string Paper, string Green, string Wall, string WallLow, string WallTrim, string Deck,
    string Grate, string Ceil, string Latch, string Screen, string Screen2, string Console, string Mat)
{
    /// <summary>The Traders' (their IT style, as the Satchel, the Packet and the Docket wear it): old office-case beige over a greyer
    /// skin, corporate blue, a dark frame, lanyard yellow where things move and where people go in, link-blue lights; pale office walls
    /// over a blue dado inside.</summary>
    public static readonly ShipStyle Traders = new(
        Skin: "#d8d0b8", Skin2: "#c4bca3", Armour: "#1f4e8c", Armour2: "#2a64a8", Bare: "#8e9296", Frame: "#3b3f44", Dark: "#26292d",
        Hazard: "#f2c400", Red: "#c0392b", Glow: "#ffe0a0", Glass: "#86a9b8", Link: "#0f7bd8", Paper: "#e6e2d6", Green: "#2f9a52",
        Wall: "#cdcac0", WallLow: "#2a64a8", WallTrim: "#3b3f44", Deck: "#5d6266", Grate: "#3d4144", Ceil: "#b4b6b2", Latch: "#c4362b",
        Screen: "#78c8e8", Screen2: "#60e090", Console: "#22262b", Mat: "#30353b");
}
