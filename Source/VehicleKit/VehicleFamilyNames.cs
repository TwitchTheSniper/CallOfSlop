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
        ["Bandit"] = new(
            ["Rustbucket", "Scrapjaw", "Gutrunner", "Dustdevil", "Junkhammer", "Boneshaker", "Roadrash", "Sandgrinder", "Tetanus Express", "Rattletrap", "Gristle", "Big Mutha", "Chopshop", "Wrecker", "Grinder"],
            ["Mk1", "Mk2", "Mk3"],
            ["Old Mother Rust", "The Iron Widow", "King of the Scrapheap", "The Doom Wagon", "The Last Ride", "Big Mutha's Revenge"],
            new() { [VehicleFamilies.UpArmoured] = "Plated", [VehicleFamilies.GunTruck] = "Gunrig", [VehicleFamilies.TankerConversion] = "Guzzler" }),
        ["IT"] = new(
            ["Model T-1000 Helpdesk", "Ticket Queue", "Patch Tuesday", "Hotdesk", "Uptime", "Firewall", "Escalation", "Change Request", "Legacy System", "Service Desk", "Blue Screen", "Reboot", "Cloud Migration"],
            ["", "SP2", "Enterprise"],
            ["The Mainframe", "Production", "Root Access", "The CTO's Pet Project", "Single Point of Failure"],
            new() { [VehicleFamilies.UpArmoured] = "Hardened", [VehicleFamilies.GunTruck] = "Pen Test", [VehicleFamilies.TankerConversion] = "Data Lake" }),
        ["Scam"] = new(
            ["Cold Call", "Hot Lead", "Prize Winner", "Refund Desk", "Tech Support", "Account Verifier", "Extended Warranty", "Gift Card", "Lucky Draw", "Upsell", "Free Cruise"],
            ["", "Gold", "Platinum"],
            ["The Boiler Room", "Grand Prize", "Your Account Is Suspended", "The Big Payout", "Final Final Notice"],
            new() { [VehicleFamilies.UpArmoured] = "Premium", [VehicleFamilies.GunTruck] = "Hard Sell", [VehicleFamilies.TankerConversion] = "Bulk Deal" }),
        ["Highland"] = new(
            ["Glen Rover", "Ben Crawler", "Loch Runner", "Heather Hopper", "Bothy Bus", "Strath Hauler", "Corrie Climber", "Firth Flyer", "Moor Raider", "Croft Wagon", "Munro Bagger", "Kilt Lifter"],
            ["", "II", "Mor"],
            ["The Laird's Pride", "Auld Reekie", "The Big Yin", "The Bonnie Prince", "Wee Beastie"],
            new() { [VehicleFamilies.UpArmoured] = "Targe", [VehicleFamilies.GunTruck] = "Claymore", [VehicleFamilies.TankerConversion] = "Distillery" }),
        ["British"] = new(
            ["Wellington", "Marlborough", "Brigadier", "Bulldog", "Drizzle", "Teatime", "Pennyworth", "Sovereign", "Windsor", "Rainmaker", "Queue Jumper", "Stiff Upper", "Bowler"],
            ["Mk I", "Mk II", "Mk III"],
            ["Her Majesty's Displeasure", "The Last Bus Home", "Strongly Worded", "Jolly Good Show", "Mind the Gap"],
            new() { [VehicleFamilies.UpArmoured] = "Ironclad", [VehicleFamilies.GunTruck] = "Gunboat", [VehicleFamilies.TankerConversion] = "Bowser" }),
    };

    public static FamilyNameSet For(string style) => ByStyle.GetValueOrDefault(style) ?? Default;
}
