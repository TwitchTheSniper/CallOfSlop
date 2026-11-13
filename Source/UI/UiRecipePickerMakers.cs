using Brutal.ImGuiApi;

namespace CallOfSlop;

/// <summary>
/// The recipe picker's tiles for the machines that make things by shape rather than by recipe (production.md "The recipe picker"): a
/// press (Auto, then its fitted die's patterns; the other dies of its size dim, so it shows all it could make), a Die Lathe (the dies it
/// cuts; those of another size dim), a Fan Former (what it makes) and an Airframe Former (what it cuts). Each tile's value is the
/// setting <see cref="ProductionLines.RecipeKey"/> names, so the pick goes as every other machine's does.
/// </summary>
public sealed partial class Ui
{
    private static readonly ImColor8 RpPlain = new(70, 76, 88, 255);

    private int _rcpTestPick = -1;

    /// <summary>The open picker's tiles as text (window shots: --do "picker"), or why there are none.</summary>
    public string RecipePickerDump() =>
        !RecipePickerOpen || _rcp is not { } set ? "picker closed"
            : $"{set.Title.Split("###")[0]}, {set.Tiles.Length} tiles, groups {string.Join("/", set.Groups)}: "
              + string.Join("; ", set.Tiles.Select(t => $"{(t.Value >= 0 ? t.Value.ToString() : "dim")} {t.Name}{(t.Out.Length > 0 ? $" [{t.Out}]" : "")}"));

    /// <summary>Window shots (--do "picker N"): the tile of value <paramref name="value"/> is picked next frame, as a click on it is.</summary>
    public void RecipePickerPick(int value) => _rcpTestPick = value;

    private static ImColor8 PartCol(ConstructPartDef p) => Col8(p.Boxes.Length > 0 ? p.Boxes[0].Color : new System.Numerics.Vector3(0.5f));

    private static int PartTile(ConstructPartDef p) => SandboxTiles.SlotOf(SandboxTiles.Kind.Part, p.Id);

    /// <summary>A press's, a Die Lathe's or a Former's tiles (<paramref name="variant"/>: a press's fitted die family, "" for none).</summary>
    private static RpSet MakeMakerSet(ConstructPartDef d, int key, string variant)
    {
        var tiles = new List<RpTile>();
        var groups = new List<string> { "All" };
        var opts = d.Config[d.CfgSlot[(int)ProductionLines.RecipeKey(d)]].Options;
        if (d.IsLathe)
            LatheTiles(d, tiles);
        else if (d.IsPress)
            PressTiles(d, variant, tiles, groups);
        else
        {
            // A Former's kinds (its Makes or Cuts setting's options): a swatch each; its sizes stay as the configure tool set them.
            var shades = new ImColor8[] { new(96, 120, 150, 255), new(110, 140, 112, 255), new(150, 128, 92, 255), new(128, 104, 140, 255) };
            for (var k = 0; k < opts.Length; k++)
            {
                var what = d.IsFanFormer
                    ? $"{opts[k]}: what it makes next, at the Across, Blades and Blade style set with the configure tool, of what its bin holds."
                    : $"{opts[k]}: what it cuts next, at the Chord and Span set with the configure tool, skinned in what its bin holds.";
                tiles.Add(new RpTile { Value = k, Name = opts[k], Search = opts[k].ToLowerInvariant(), Tip = what, Swatch = shades[k % shades.Length] });
            }
        }
        var title = d.IsLathe ? "Cuts" : d.IsPress ? "Pattern" : d.IsFanFormer ? "Makes" : d.IsAirframeFormer ? "Cuts" : "Recipe";
        return new RpSet { Tiles = tiles.ToArray(), Groups = groups.ToArray(), Title = $"{title}: {d.Name}###rpick", Key = key };
    }

    /// <summary>A Die Lathe: None, then every die of the lathe list; a die of another size than the lathe's is dim.</summary>
    private static void LatheTiles(ConstructPartDef d, List<RpTile> tiles)
    {
        var t = PressTable.Current;
        var names = RecyclingTable.Current;
        var speed = MathF.Max(0.01f, d.RefineWork / (float)ConstructRefining.BaseWork);
        tiles.Add(new RpTile { Value = 0, Name = "None", Search = "none", Tip = "None: the lathe cuts nothing.", Swatch = RpPlain });
        for (var k = 0; k < t.Lathe.Length; k++)
        {
            var r = t.Lathe[k];
            var ok = ConstructPress.LatheCuts(d, r) && k + 1 < t.LatheChoices.Length;
            var tile = new RpTile
            {
                Value = ok ? k + 1 : -1,
                Name = r.Output.Name,
                Out = r.Count > 1 ? $"x{r.Count}" : "",
                Time = Secs(r.Seconds / speed),
                Slot = PartTile(r.Output),
                Swatch = PartCol(r.Output),
            };
            var bays = r.Bays.Where(b => b.Materials.Length > 0).ToArray();
            tile.InSlot = bays.Select(b => SandboxTiles.ResourceSlot(b.Materials[0])).ToArray();
            tile.InText = bays.Select(b => b.Label).ToArray();
            tile.InSwatch = bays.Select(b => Col8(names.ColourOf(b.Materials[0]))).ToArray();
            var takes = string.Join(" and ", r.Bays.Select(b => $"a {b.Label.ToLowerInvariant()} of {string.Join(" or ", b.Materials.Select(names.NameOf))}"));
            tile.Search = $"{r.Output.Name} {takes}".ToLowerInvariant();
            tile.Tip = ok
                ? $"{r.Output.Name}: cut from {takes} in {Secs(r.Seconds / speed)} on this {d.Name}."
                : $"{r.Output.Name}: another size of die; a {d.Name} cuts dies of its own size.";
            tiles.Add(tile);
        }
    }

    /// <summary>
    /// A press: Auto, then the fitted die's patterns (its Pattern setting: 1 + a pattern's place in the die's list), then the patterns of the
    /// other dies of its size, dim (fit that die to press them), each die a category.
    /// </summary>
    private static void PressTiles(ConstructPartDef d, string fitted, List<RpTile> tiles, List<string> groups)
    {
        var t = PressTable.Current;
        tiles.Add(new RpTile
        {
            Value = 0, Name = "Auto", Search = "auto", Swatch = RpPlain,
            Tip = fitted.Length > 0 ? "Auto: the blank picks the pattern (a standard blank gives its standard part)." : "Auto: the blank picks the pattern, once a die is fitted.",
        });
        var cls = d.PressDieSocket >= 0 ? d.Sockets[d.PressDieSocket].Class : (byte)0;
        // The dies of this press's size, the fitted one first.
        var dies = new List<(PressDie Die, ConstructPartDef? Part)>();
        foreach (var die in t.Dies)
        {
            ConstructPartDef? part = null;
            foreach (var p in ConstructParts.All)
                if (p.IsPressDie && p.DieFamily.Equals(die.Family, StringComparison.OrdinalIgnoreCase) && p.Sockets.Length > 0 && ConstructPress.DieClassOf(p) == cls)
                {
                    part = p;
                    break;
                }
            if (part == null && !die.Family.Equals(fitted, StringComparison.OrdinalIgnoreCase))
                continue; // (no die of this size)
            if (die.Family.Equals(fitted, StringComparison.OrdinalIgnoreCase))
                dies.Insert(0, (die, part));
            else
                dies.Add((die, part));
        }
        foreach (var (die, part) in dies)
        {
            var on = die.Family.Equals(fitted, StringComparison.OrdinalIgnoreCase);
            var gi = groups.Count;
            groups.Add(on ? $"{die.Family} (fitted)" : die.Family);
            var dieName = part?.Name ?? $"{die.Family} Die";
            for (var k = 0; k < die.Patterns.Length; k++)
            {
                var pt = die.Patterns[k];
                var label = Cap(pt.Label.Length > 0 ? pt.Label : pt.Id);
                var ok = on && k + 1 < PressTable.PatternChoices.Length;
                var tile = new RpTile
                {
                    Value = ok ? k + 1 : -1,
                    Name = label,
                    Out = on ? $"P{k + 1}" : "",
                    Slot = pt.Like is { } like ? PartTile(like) : -1,
                    Swatch = pt.Like is { } lc ? PartCol(lc) : RpPlain,
                    Group = gi,
                    Search = $"{label} {pt.Like?.Name} {die.Family}".ToLowerInvariant(),
                };
                if (pt.Bay2 is { } kit)
                {
                    tile.InSlot = [kit.Part is { } kp ? PartTile(kp) : SandboxTiles.ResourceSlot(kit.Material)];
                    tile.InText = ["kit"];
                    tile.InSwatch = [kit.Part is { } kc ? PartCol(kc) : Col8(RecyclingTable.Current.ColourOf(kit.Material))];
                }
                var kitText = pt.Bay2 is { } kw ? $", with {kw.What} in bay 2" : "";
                tile.Tip = ok
                    ? $"{label}: a part like the {pt.Like?.Name ?? label}, sized by the blank{kitText}."
                    : $"{label}: fit a {dieName} in the die slot to press it{kitText}.";
                tiles.Add(tile);
            }
        }
    }
}
