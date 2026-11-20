using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The style grading sheet (Karl, 2026-11-19): for one kind of item, several distinct design interpretations of his brief,
/// built as real parts so the sheet shows them through the game's own build rules (--preview-gallery DIR --group Style).
/// He grades them and picks one; the picked look is then built into the item's real generator, and these drafts go.
/// Group "Style" is unlisted (Constructs/Categories.json), so none of them show in the Parts tab. Writes Constructs/Parts/Styles.
///
/// Now: the stair round (StyleSheetParts.Stairs.cs, Karl 2026-11-20). Emptied before that, on 2026-11-20: Karl's liked looks (lockers A and E, the battery bank) went into the game as FittingLookParts, and
/// the other rounds are in git history (branch claude/project-thread-n4mjl5). A new draft builds on that file's kit:
/// <c>new FittingLookParts.Kit(new Part(name, Small, section, desc, ..., group: Group))</c> for a free-standing piece, or
/// <c>FittingLookParts.Reactor(look, 1, desc, height, name, section, ports, group: Group)</c> for a 1x1 module on the standard
/// Module Plug look (docs/sandbox/design/module-sockets.md).
/// </summary>
public sealed partial class StyleSheetParts : IPartGenerator
{
    public string Name => "style_sheet";

    private const string Out = "Constructs/Parts/Styles";
    internal const string Group = "Style";

    public void Make(PartOutput o)
    {
        o.Own(Out);
        var all = Stairs().ToList(); // (the stair round: StyleSheetParts.Stairs.cs)
        foreach (var p in all)
        {
            var d = p.Json();
            o.File($"{Out}/{FileName((string)d["name"]!)}", PyJson.Dumps(d, 2) + "\n");
        }
    }

}
