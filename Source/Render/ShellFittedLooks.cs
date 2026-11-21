using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A standing robot shell's fitted modules (render only; docs/sandbox/design/mining-shell.md "Shell Gantry"): a slot whose module isn't
/// its frame's own is drawn from that module's part (Constructs/Shells.json "part"; the boxes ShellParts.cs makes), standing straight
/// where the shell's own slot boxes were, as a worn shell shows it (ShellModuleNodes.cs). A slot with its frame's module keeps the
/// shell part's own boxes, so a shell as built stays in its merged mesh; one with a fitted module is walked part by part
/// (ConstructRender.MergeBoxes). The Shell Gantry's arms carry a module in the same look (GantryRender.DrawSlot). Each look is made
/// once a shell type, module and slot, in the shell part's metres; nothing allocates in a steady frame.
/// </summary>
public static class ShellFittedLooks
{
    private static readonly Dictionary<(ConstructPartDef Shell, byte Module, byte Slot), CPartBox[]?> _looks = [];

    /// <summary>The slots of standing shell part <paramref name="p"/> (a bit a slot) drawn from a fitted module's part, not its own boxes.</summary>
    public static int FittedMask(in CPart p, ConstructPartDef d)
    {
        if (!d.ShellBody)
            return 0;
        Span<byte> kit = stackalloc byte[ShellFrames.SlotCount];
        ShellFrames.ReadKit(p, d, kit);
        var mask = 0;
        for (var s = 0; s < kit.Length; s++)
            if (kit[s] != 0 && Fitted(d, s, kit[s] - 1) != null)
                mask |= 1 << s;
        return mask;
    }

    /// <summary>
    /// Module <paramref name="module"/> (its index) in slot <paramref name="slot"/> of shell type <paramref name="d"/>, when it is drawn
    /// from its own part: its boxes in the shell part's metres. Null: the shell's own slot boxes show it (its frame's own module, or one
    /// with no part loaded).
    /// </summary>
    public static CPartBox[]? Fitted(ConstructPartDef d, int slot, int module)
    {
        if ((uint)module > 255 || (uint)slot >= ShellFrames.SlotCount)
            return null;
        var key = (d, (byte)module, (byte)slot);
        if (_looks.TryGetValue(key, out var made))
            return made;
        var frame = ShellFrames.FrameOf(d);
        var all = ShellFrames.Modules;
        CPartBox[]? look = null;
        if (module < all.Length && all[module] is { Part.Length: > 0 } m && m.Slot == ShellFrames.SlotKind(slot) && !ReferenceEquals(m, ShellFrames.Of(frame).Modules[slot]))
        {
            if (ConstructParts.Find(m.Part) is { } md)
                look = Place(md, m, slot, ShellFrames.Of(frame).Scale);
            else
                Console.WriteLine($"[shells] the {m.Name} module's part \"{m.Part}\" isn't loaded: a standing {d.Name} shows its own {ShellFrames.SlotLabel(slot)}");
        }
        _looks[key] = look;
        return look;
    }

    private static readonly Dictionary<(ConstructPartDef Shell, byte Slot), CPartBox[]?> _own = [];

    /// <summary>
    /// The frame's own module in slot <paramref name="slot"/> of shell type <paramref name="d"/> as its part file has it (ShellParts.ModuleOnBody
    /// places it the same way), or null: that module has no part (the hopper, the drill arm). ConstructGantry.BoxSlots finds a slot's boxes by it.
    /// </summary>
    public static CPartBox[]? Own(ConstructPartDef d, int slot)
    {
        if ((uint)slot >= ShellFrames.SlotCount)
            return null;
        var key = (d, (byte)slot);
        if (_own.TryGetValue(key, out var made))
            return made;
        var frame = ShellFrames.Of(ShellFrames.FrameOf(d));
        CPartBox[]? look = null;
        if (frame.Modules[slot] is { Part.Length: > 0 } m && ConstructParts.Find(m.Part) is { } md)
            look = Place(md, m, slot, frame.Scale);
        _own[key] = look;
        return look;
    }

    /// <summary>
    /// A module part's boxes on a standing shell of size <paramref name="k"/> (the frame's scale), in a person-sized shell's places
    /// (ShellParts.Robot, whose ModuleOnBody places a frame's own modules the same way: keep the two alike) times <paramref name="k"/>: a head on the neck, a back module behind the torso facing out, an arm module hung
    /// from the elbow, a leg from the hip to the ground at its hip's height (its "joints"). A left arm or leg is mirrored.
    /// </summary>
    private static CPartBox[] Place(ConstructPartDef md, ShellModule m, int slot, float k)
    {
        var mirror = slot is 2 or 4 ? -1f : 1f;
        var size = k;
        var turn = false;
        Vector3 at;
        switch (slot)
        {
            case 0:
                at = new Vector3(0, 1.485f, 0); // (its neck 5 mm over the shell's: no shared face)
                break;
            case 1:
                at = new Vector3(0, 1.07f, 0.14f); // (sunk into the torso's back, its case's back on the shell's back socket at 0.2: a dock clamps on it)
                turn = true; // (its front, its lamps, faces out behind)
                break;
            case 2 or 3:
                at = new Vector3(0.315f * mirror, 0.62f, 0); // (its elbow coupling under the upper arm)
                break;
            default:
                at = new Vector3(0.11f * mirror, 0, 0);
                if (m.Hip > 0.1f)
                    size = k * 0.9f / m.Hip; // (a leg built at another size stands at this shell's hip)
                break;
        }
        var list = new List<CPartBox>(md.Boxes.Length);
        foreach (var b in md.Boxes)
        {
            if (b.Hidden)
                continue;
            var c = b.Center;
            if (turn)
                c = new Vector3(-c.X, c.Y, -c.Z);
            list.Add(b with { Center = at * k + new Vector3(c.X * mirror, c.Y, c.Z) * size, Half = b.Half * size });
        }
        return [.. list];
    }
}
