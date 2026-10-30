using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Sandbox backpack in first person (ConstructBackpack.cs): a strip of its slot outlines at the bottom of the screen,
/// each the size of its slot's class, with the part in it as a tiny 3D tile (SandboxTiles' atlas; a colour swatch until
/// the tile is made) and the slot's class letter. Nothing else: stowing and drawing are keys. Allocates nothing per frame.
/// </summary>
public sealed partial class Ui
{
    private static readonly ImColor8 PackSlot = new(20, 23, 28, 200), PackEdge = new(120, 150, 180, 220), PackFull = new(140, 200, 120, 235), PackLetter = new(200, 210, 220, 230);
    private string[] _packLetters = [];
    private ConstructPartDef? _packDef;

    /// <summary>Slot outlines drawn last frame (the self-test).</summary>
    public int PackSlotsDrawn { get; private set; }

    /// <summary>Draws the local player's backpack strip (Sandbox, alive, not seated).</summary>
    internal void DrawSandboxPack(Game game, float2 pos, float2 size)
    {
        PackSlotsDrawn = 0;
        if (!game.SandboxOn || game.Local is not { Alive: true } me || ConstructEdits.Backpack is not { } bp || game.SandboxHands.Hand(me.Id).Seated)
            return;
        if (_packDef != bp)
        {
            _packDef = bp;
            _packLetters = bp.Slots.Select(s => ConstructStorage.SizeClass(s with { Label = "" })[..1].ToUpperInvariant()).ToArray(); // (S, M, L: the size class, whatever the slot is called)
        }
        var pack = game.SandboxHands.Pack(me.Id);
        if (pack.Items > 0)
            SandboxTiles.Want(); // (the parts' pictures, made over a few frames)
        var dl = ImGui.GetForegroundDrawList();
        var small = 34 * _scale;
        var gap = 6 * _scale;
        var total = 0f;
        for (var k = 0; k < bp.Slots.Length; k++)
            total += SlotSide(bp.Slots[k], small) + gap;
        var x = pos.X + (size.X - total + gap) * 0.5f;
        var bottom = pos.Y + size.Y - 92 * _scale;
        ImGui.PushFont(default, 13);
        for (var k = 0; k < bp.Slots.Length && k < pack.SlotCount; k++)
        {
            var side = SlotSide(bp.Slots[k], small);
            var a = new float2(x, bottom - side);
            var b = new float2(x + side, bottom);
            var d = ConstructParts.Get(pack.Def[k]);
            dl.AddRectFilled(a, b, PackSlot, 4 * _scale);
            if (d != null)
            {
                var pad = side * 0.08f;
                var i0 = new float2(a.X + pad, a.Y + pad);
                var i1 = new float2(b.X - pad, b.Y - pad);
                if (SandboxTiles.TryGet(SandboxTiles.SlotOf(SandboxTiles.Kind.Part, d.Id), out var tex, out var uv0, out var uv1))
                    dl.AddImage(new ImTextureRef { _TexID = new ImTextureID((nint)tex) }, i0, i1, new float2(uv0.X, uv0.Y), new float2(uv1.X, uv1.Y), new ImColor8(255, 255, 255, 255));
                else
                {
                    var c = d.Boxes[0].Color;
                    dl.AddRectFilled(new float2(a.X + side * 0.28f, a.Y + side * 0.28f), new float2(b.X - side * 0.28f, b.Y - side * 0.28f),
                        new ImColor8((byte)(c.X * 255), (byte)(c.Y * 255), (byte)(c.Z * 255), 255), 3 * _scale);
                }
            }
            dl.AddRect(a, b, d != null ? PackFull : PackEdge, 4 * _scale, ImDrawFlags.None, 1.5f * _scale);
            DlText(dl, new float2(a.X + 3 * _scale, a.Y + 1 * _scale), PackLetter, _packLetters[k]);
            x += side + gap;
            PackSlotsDrawn++;
        }
        ImGui.PopFont();
    }

    /// <summary>A slot's square on the strip: small slots small, bigger ones bigger.</summary>
    private static float SlotSide(in CSlotDef s, float small)
    {
        var big = MathF.Max(s.Size.X, MathF.Max(s.Size.Y, s.Size.Z));
        return big <= ConstructStorage.SmallSize + 0.01f ? small : big <= ConstructStorage.MediumSize + 0.01f ? small * 1.35f : small * 1.7f;
    }
}
