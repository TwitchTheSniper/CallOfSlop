namespace CallOfSlop.VehicleKit;

/// <summary>
/// A style's model names (docs/vehiclekit/VEHICLEKIT.md, "Families and convoys"): the model lines' names, how a mark reads, the named uniques' own names, and what a
/// variant is called. Player-visible text: ASCII only.
/// </summary>
/// <param name="Marks">Mk1, Mk2 and Mk3's words after the name ("" for none).</param>
public sealed record FamilyNameSet(string[] Models, string[] Marks, string[] Named, Dictionary<string, string>? Variants = null)
{
    public string VariantLabel(string variant) =>
        Variants?.GetValueOrDefault(variant) ?? variant switch
        {
            VehicleFamilies.UpArmoured => "Up-Armoured",
            VehicleFamilies.GunTruck => "Gun Truck",
            VehicleFamilies.TankerConversion => "Tanker",
            _ => variant,
        };
}

/// <summary>
/// The model names by style name, kept here rather than in <c>Styles.cs</c> (a style not named gets <see cref="Default"/>). Karl may edit these freely: a name is
/// picked from its list by the family's seed, so changing a list renames families but makes no vehicle differently.
/// </summary>
public static class VehicleFamilyNames
{
    public static readonly FamilyNameSet Default = new(
        ["Patriot", "Minuteman", "Ranger", "Homestead", "Sentinel", "Liberty", "Pioneer", "Frontier", "Lookout", "Stockade"],
        ["Mk1", "Mk2", "Mk3"],
        ["The Compound", "Old Faithful", "The Last Stand"]);

    public static readonly Dictionary<string, FamilyNameSet> ByStyle = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IT"] = new( // (the Traders: Crew/Factions/Traders.json)
            ["Ledger", "Manifest", "Consignment", "Waybill", "Freightliner", "Packet", "Courier", "Bulk Carrier", "Clearance", "Invoice", "Tally", "Dispatch", "Tradewind"],
            ["", "Plus", "Pro"],
            ["The Counting House", "Cash on Delivery", "The Long Haul", "Net Thirty", "The Bottom Line"],
            new() { [VehicleFamilies.UpArmoured] = "Hardened", [VehicleFamilies.GunTruck] = "Escort", [VehicleFamilies.TankerConversion] = "Bulk Tanker" }),
    };

    public static FamilyNameSet For(string style) => ByStyle.GetValueOrDefault(style) ?? Default;
}
