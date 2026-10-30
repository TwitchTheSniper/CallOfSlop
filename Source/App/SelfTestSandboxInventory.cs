using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Sandbox inventory": the inventory's data headless (SandboxCatalogue.cs, SandboxTiles.cs; docs/sandbox/CONSTRUCTS.md,
    /// "The inventory"). The categories hold every part, blueprint and tool exactly once, and today's roles all have a
    /// named group (none in "Other"); a role the catalogue doesn't know lands in "Other" (or a group its name suggests).
    /// Every tile slot (each part, blueprint and tool model) gets boxes to draw, a blueprint built as the server builds
    /// it, and a camera that frames every box inside its square with the thing filling most of it; slots sit in distinct
    /// places inside their atlas pages. (The atlas itself is drawn with GL: --inventory-shots.)
    /// </summary>
    private static void CheckSandboxInventory()
    {
        var cat = SandboxCatalogue.Make();
        var parts = ConstructParts.All;
        string Cover(SandboxCatalogue.Tree tree, int count, Func<int, bool>? hidden = null)
        {
            var seen = new int[count];
            var outside = 0;
            foreach (var s in tree.Sections)
                foreach (var i in s.Items)
                    if (i >= 0 && i < count)
                        seen[i]++;
                    else
                        outside++;
            // (legacy parts, replaced by routed links, are left out: shown, they'd count as wrong)
            var missing = Enumerable.Range(0, count).Count(i => seen[i] == 0 && hidden?.Invoke(i) != true);
            var shown = Enumerable.Range(0, count).Count(i => seen[i] > 0 && hidden?.Invoke(i) == true);
            var twice = seen.Count(n => n > 1);
            var groupSum = tree.Groups.Sum(x => x.Items);
            var want = count - Enumerable.Range(0, count).Count(i => hidden?.Invoke(i) == true);
            return missing == 0 && shown == 0 && twice == 0 && outside == 0 && groupSum == want && tree.Items == want ? "" : $"{missing} missing, {shown} legacy shown, {twice} twice, {outside} unknown, groups hold {groupSum} of {want}";
        }
        var partsCover = Cover(cat.Parts, parts.Count, i => parts[i].Legacy);
        var bpCover = Cover(cat.Blueprints, ConstructBuild.Blueprints.Count);
        var toolCover = Cover(cat.Tools, ConstructTools.All.Count);
        var groups = string.Join(", ", cat.Parts.Groups.Select(x => $"{x.Name} {x.Items}"));
        var other = cat.Parts.Groups.FirstOrDefault(x => x.Name == "Other");
        var otherParts = other == null ? "" : string.Join(", ", other.Sections.SelectMany(s => cat.Parts.Sections[s].Items).Select(i => parts[i].Name));
        Line($"inventory catalogue: parts in {cat.Parts.Groups.Length} groups ({groups}), {cat.Parts.Sections.Length} sections; blueprints {string.Join(", ", cat.Blueprints.Sections.Select(s => $"{s.Name} {s.Items.Length}"))}; "
             + $"tools {string.Join(", ", cat.Tools.Sections.Select(s => $"{s.Name} {s.Items.Length}"))}; covered: parts {(partsCover == "" ? "all once" : partsCover)}, blueprints {(bpCover == "" ? "all once" : bpCover)}, tools {(toolCover == "" ? "all once" : toolCover)}");
        if (partsCover != "" || bpCover != "" || toolCover != "")
            Fail("inventory: the categories don't hold every part, blueprint and tool exactly once");
        if (other != null)
            Fail($"inventory: today's parts should all have a named group, but {otherParts} are in Other");
        foreach (var name in new[] { "Structure", "Wheels and drivetrain", "Seats", "Joints", "Logic" })
            if (!cat.Parts.Groups.Any(x => x.Name == name))
                Fail($"inventory: no \"{name}\" group");

        // A role this build doesn't know (another branch's new role) still lands somewhere.
        var unknown = SandboxCatalogue.Classify(new ConstructPartDef { Name = "Mystery", Role = (PartRole)200 });
        Line($"inventory catalogue: an unknown role (200) goes to {unknown.Group} > {unknown.Section}");
        if (unknown.Group != "Other")
            Fail("inventory: an unknown role doesn't go to the Other group");

        // Tiles: every slot has boxes and a camera framing them; slots in distinct places in their pages.
        var count = SandboxTiles.Count;
        var boxes = new List<TileBox>();
        var empty = new List<string>();
        var offTile = new List<string>();
        var small = new List<string>();
        var places = new HashSet<(int, int, int)>();
        var badPlace = 0;
        var minFill = 1f;
        for (var slot = 0; slot < count; slot++)
        {
            var what = slot < parts.Count ? parts[slot].Name
                : slot < parts.Count + ConstructBuild.Blueprints.Count ? $"blueprint {ConstructBuild.Blueprints[slot - parts.Count].Name}"
                : $"tool {ConstructTools.All[slot - parts.Count - ConstructBuild.Blueprints.Count].Name}";
            SandboxTiles.Place(slot, out var page, out var x, out var y);
            if (!places.Add((page, x, y)) || x < 0 || y < 0 || x + SandboxTiles.Tile > SandboxTiles.PageSize || y + SandboxTiles.Tile > SandboxTiles.PageSize)
                badPlace++;
            if (!SandboxTiles.CollectBoxes(slot, boxes))
            {
                empty.Add(what);
                continue;
            }
            SandboxTiles.Frame(boxes, out var view, out var proj);
            var vp = view * proj;
            float maxX = 0, maxY = 0;
            var outside = false;
            foreach (var b in boxes)
                for (var k = 0; k < 8; k++)
                {
                    var p = Vector4.Transform(new Vector4(Vector3.Transform(new Vector3((k & 1) - 0.5f, ((k >> 1) & 1) - 0.5f, ((k >> 2) & 1) - 0.5f), b.Model), 1), vp);
                    var n = new Vector3(p.X, p.Y, p.Z) / p.W;
                    outside |= MathF.Abs(n.X) > 1.001f || MathF.Abs(n.Y) > 1.001f || n.Z < -0.001f || n.Z > 1.001f;
                    maxX = MathF.Max(maxX, MathF.Abs(n.X));
                    maxY = MathF.Max(maxY, MathF.Abs(n.Y));
                }
            var fill = MathF.Max(maxX, maxY);
            minFill = MathF.Min(minFill, fill);
            if (outside)
                offTile.Add(what);
            if (fill < 0.75f)
                small.Add($"{what} {fill:0.00}");
        }
        Line($"inventory tiles: {count} slots ({parts.Count} parts, {ConstructBuild.Blueprints.Count} blueprints, {ConstructTools.All.Count} tools) in {(count + SandboxTiles.PerPage - 1) / SandboxTiles.PerPage} page(s); "
             + $"without boxes: {(empty.Count == 0 ? "none" : string.Join(", ", empty))}; outside their tile: {(offTile.Count == 0 ? "none" : string.Join(", ", offTile))}; "
             + $"filling under 75%: {(small.Count == 0 ? "none" : string.Join(", ", small))} (least {minFill:0.00}); places clashing {badPlace}");
        if (empty.Count > 0 || offTile.Count > 0 || small.Count > 0 || badPlace > 0)
            Fail("inventory: not every part, blueprint and tool gets a framed tile in the atlas");
    }
}
