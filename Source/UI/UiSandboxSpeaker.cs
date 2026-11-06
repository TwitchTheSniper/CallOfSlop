using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>A speaker's picture on the configure panel: the sound seen from above, its front up, shaded by <see cref="SpeakerShape.Level"/>, the same rule the sound uses.</summary>
public sealed partial class Ui
{
    private const int SpkCells = 32;

    private void SandboxConfigSpeaker(Construct c, int part, ConstructPartDef d)
    {
        if (!d.IsSpeaker || part < 0 || part >= c.PartCount)
            return;
        var shape = ConstructSpeakers.Shape(c.Parts[part], d);
        ImGui.SeparatorText("Coverage from above"u8);
        var side = 190 * _scale;
        var at = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new float2(side, side));
        var dl = ImGui.GetWindowDrawList();
        var cell = side / SpkCells;
        var mid = new float2(at.X + side * 0.5f, at.Y + side * 0.5f);
        for (var y = 0; y < SpkCells; y++)
            for (var x = 0; x < SpkCells; x++)
            {
                var dx = (x + 0.5f - SpkCells * 0.5f) / (SpkCells * 0.5f) * shape.Range;
                var dy = (y + 0.5f - SpkCells * 0.5f) / (SpkCells * 0.5f) * shape.Range;
                var distance = MathF.Sqrt(dx * dx + dy * dy);
                var angle = MathF.Atan2(MathF.Abs(dx), -dy) * 180f / MathF.PI; // (the front is up the picture)
                var level = shape.Level(distance, angle);
                var shade = (byte)(Math.Clamp(MathF.Sqrt(level), 0, 1) * 255);
                dl.AddRectFilled(new float2(at.X + x * cell, at.Y + y * cell), new float2(at.X + (x + 1) * cell, at.Y + (y + 1) * cell), new ImColor8((byte)(shade / 4), (byte)(shade * 3 / 4), (byte)(shade / 2), 255));
            }
        for (var ring = 1; ring <= 4; ring++)
            dl.AddCircle(mid, side * 0.5f * ring / 4, new ImColor8(255, 255, 255, (byte)(ring == 4 ? 255 : 96)));
        if (shape.Directional)
            foreach (var cone in (ReadOnlySpan<float>)[shape.Inner, shape.Outer])
                for (var sign = -1; sign <= 1; sign += 2)
                {
                    var a = sign * cone * 0.5f * MathF.PI / 180f;
                    dl.AddLine(mid, new float2(mid.X + MathF.Sin(a) * side * 0.5f, mid.Y - MathF.Cos(a) * side * 0.5f), new ImColor8(255, 208, 0, 192));
                }
        dl.AddRectFilled(new float2(mid.X - 3 * _scale, mid.Y - 3 * _scale), new float2(mid.X + 3 * _scale, mid.Y + 3 * _scale), new ImColor8(255, 255, 255, 255));
        ImGuiSafe.TextDisabled($"Rings every {shape.Range / 4:0.#} m");
    }
}
