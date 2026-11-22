namespace CallOfSlop.PartKit.ShipKit;

/// <summary>
/// A faction's paint for a lofted ship (docs/sandbox/constructs/ship-kit.md): the skin, the armour, the frame and trim, the inside's
/// walls and decks, and the lights. The kit draws every piece with these, so a ship's colours come from its faction, never from the
/// kit. Shapes are not here: each ship gives its own (the kit's fitting specs).
/// </summary>
public sealed record ShipStyle(
    string Skin, string Skin2, string Armour, string Armour2, string Bare, string Frame, string Dark, string Hazard, string Red,
    string Glow, string Glass, string Link, string Paper, string Green, string Wall, string WallLow, string WallTrim, string Deck,
    string Grate, string Ceil, string Latch, string Screen, string Screen2, string Console, string Mat);
