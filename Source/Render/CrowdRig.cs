using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A baked crowd zombie for the GPU (docs/zombies/MASS-HORDE.md, "Rendering"): the walk cycle of the box zombie
/// (<see cref="ZombieAnim"/>) recorded once at <see cref="Frames"/> phases, each frame every box's transform and colour.
/// The crowd's vertex shader reads a box's frame from a float texture (<see cref="Texels"/>: a row per frame, four
/// texels per box: the transform's three axis rows with the colour's red, green and blue in w, then its translation with
/// the draw mode in w), so a crowd zombie costs one instance and no CPU posing. Three levels of detail: near (the full
/// body with hands, feet and glowing eyes), mid (one box per limb) and far (legs, torso and head, not animated).
/// </summary>
public sealed class CrowdRig
{
    public const int TexelsPerBox = 4;

    public int Boxes, Frames;

    /// <summary>Gaits baked (walk, run, sprint: <see cref="ZSpeed"/>), each <see cref="Frames"/> rows; the far rig has one (standing).</summary>
    public int Gaits = 1;

    /// <summary>Metres walked per cycle of a gait (four half strides of the individuals' animation).</summary>
    public static float CycleOf(ZSpeed s) => 4f * ZombieAnim.StrideHalf(s);

    /// <summary>RGBA32F texels, <see cref="Gaits"/> x <see cref="Frames"/> rows (gait by gait) of <see cref="Boxes"/> x <see cref="TexelsPerBox"/>.</summary>
    public float[] Texels = [];

    /// <summary>Metres walked per walk cycle (the shader's frame from the distance walked).</summary>
    public float CycleMetres = 1f;

    public string Name = "";

    public int Width => Boxes * TexelsPerBox;

    private sealed class Rec : IDrawRecorder
    {
        public readonly List<(Matrix4x4 M, Vector3 C, int Mode)> Items = new();

        public void Draw(in Matrix4x4 model, Vector3 color, float alpha, int mode, Material material) => Items.Add((model, color, mode));

        public void SetBlend(bool on)
        {
        }

        public void SetDepthWrite(bool on)
        {
        }
    }

    /// <summary>The zombie palette (Program.Zombies.cs draws individuals with the same).</summary>
    public static readonly Vector3 Skin = new(0.36f, 0.52f, 0.3f), Shirt = new(0.33f, 0.3f, 0.26f), Pants = new(0.18f, 0.2f, 0.27f);

    /// <summary>Texture rows: every gait's frames.</summary>
    public int Rows => Gaits * Frames;

    /// <summary>Bakes one level of detail (0 near, 1 mid, 2 far): the walk, run and sprint cycles (the far rig stands). Headless (records draws; no GL).</summary>
    public static CrowdRig Bake(int lod, int frames = 16)
    {
        var rec = new Rec();
        var g = new Renderer { Recorder = rec };
        var rig = new CrowdRig { Name = lod switch { 0 => "near", 1 => "mid", _ => "far" } };
        rig.CycleMetres = 4f * ZombieAnim.StrideHalf(ZSpeed.Walk);
        if (lod >= 2)
        {
            // Far: legs, torso and head as three boxes, standing (too far to see the gait).
            rig.Frames = 1;
            rec.Items.Add((Matrix4x4.CreateScale(0.42f, 0.86f, 0.24f) * Matrix4x4.CreateTranslation(0, 0.43f, 0), Pants, 0));
            rec.Items.Add((Matrix4x4.CreateScale(0.5f, 0.62f, 0.3f) * Matrix4x4.CreateRotationX(-0.25f) * Matrix4x4.CreateTranslation(0, 1.17f, -0.06f), Shirt, 0));
            rec.Items.Add((Matrix4x4.CreateScale(0.3f, 0.3f, 0.3f) * Matrix4x4.CreateTranslation(0, 1.6f, -0.16f), Skin, 0));
            rig.Boxes = 3;
            rig.Fill(0, rec.Items);
            return rig;
        }
        rig.Frames = frames;
        rig.Gaits = 3;
        for (var row = 0; row < frames * rig.Gaits; row++)
        {
            var f = row % frames;
            rec.Items.Clear();
            var a = new ZombieAnimInput
            {
                Pos = Vector3.Zero,
                Yaw = 0,
                Mode = ZAnimMode.Locomotion,
                Speed = (ZSpeed)(row / frames),
                Move = 1f,
                Gait = f * MathF.Tau / frames,
                Time = f * 0.05f,
                Attack = -1,
                Stagger = -1,
            };
            ZombieAnim.Targets(a, out var t);
            ZombieAnim.Solve(a, t, out var pose);
            ZombieAnim.Draw(g, pose, Shirt, Skin, Pants, detail: lod == 0, lod: lod == 1, eyes: true);
            if (row == 0)
            {
                rig.Boxes = rec.Items.Count;
                rig.Texels = new float[rig.Rows * rig.Width * 4];
            }
            else if (rec.Items.Count != rig.Boxes)
                throw new InvalidOperationException($"crowd rig {rig.Name}: row {row} has {rec.Items.Count} boxes, row 0 {rig.Boxes}");
            rig.Fill(row, rec.Items);
        }
        return rig;
    }

    private void Fill(int frame, List<(Matrix4x4 M, Vector3 C, int Mode)> items)
    {
        if (Texels.Length == 0)
            Texels = new float[Math.Max(1, Rows) * Width * 4];
        var o = frame * Width * 4;
        foreach (var (m, c, mode) in items)
        {
            float[] row = [m.M11, m.M12, m.M13, c.X, m.M21, m.M22, m.M23, c.Y, m.M31, m.M32, m.M33, c.Z, m.M41, m.M42, m.M43, mode];
            row.CopyTo(Texels, o);
            o += 16;
        }
    }
}
