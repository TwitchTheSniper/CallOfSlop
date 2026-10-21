using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// One arm as scene nodes: root (the arm's space: the camera in first person, the body in third person) -> shoulder ->
/// upper arm -> forearm -> hand, with the sleeve / elbow / forearm / cuff boxes on their bones, one child group per
/// <see cref="HandShape"/> on the hand (only the current one is shown), a "hand_r" / "hand_l" socket at the hand frame, and
/// (left hand) the item carried during a reload. The bones are posed from a solved <see cref="ArmRig"/>, whose IK keeps
/// working in its own space; each bone's local matrix is its frame relative to its parent bone.
/// </summary>
public sealed class ArmNodes
{
    public readonly SceneNode Root, Shoulder, Upper, Fore, Hand, HandSocket;
    private readonly SceneNode _sleeve, _elbow, _forearm, _cuff;
    private readonly SceneNode[] _shapes;
    private readonly SceneNode? _carryA, _carryB;
    private readonly float _side;
    private float _upper = float.NaN, _fore, _thick, _handScale;

    /// <param name="suffix">"r" or "l": names the nodes "shoulder_r", "hand_l"...</param>
    /// <param name="side">+1 right, -1 left (hand shapes mirror in X).</param>
    /// <param name="carry">Give the hand two box nodes for what it carries during a reload.</param>
    public ArmNodes(string suffix, float side, bool carry)
    {
        _side = side;
        Root = new SceneNode("arm_" + suffix);
        Shoulder = Root.Add("shoulder_" + suffix);
        Upper = Shoulder.Add("upper_" + suffix);
        _sleeve = Upper.Add("sleeve");
        _sleeve.Renderable = Renderable.Box(Vector3.One);
        Fore = Upper.Add("forearm_" + suffix);
        _elbow = Fore.Add("elbow");
        _elbow.Renderable = Renderable.Box(Vector3.One);
        _forearm = Fore.Add("forearm_box");
        _forearm.Renderable = Renderable.Box(Vector3.One);
        _cuff = Fore.Add("cuff");
        _cuff.Renderable = Renderable.Box(Vector3.One);
        Hand = Fore.Add("hand_" + suffix + "_bone");
        _shapes = new SceneNode[Arms.Shapes.Length];
        for (var s = 0; s < _shapes.Length; s++)
        {
            _shapes[s] = Hand.Add(((HandShape)s).ToString().ToLowerInvariant());
            foreach (var p in Arms.Shapes[s])
                _shapes[s].Add(p.Finger ? "finger" : "palm").Renderable = Renderable.Box(Vector3.One);
        }
        HandSocket = Hand.AddSocket("hand_" + suffix, Vector3.Zero);
        if (carry)
        {
            _carryA = HandSocket.Add("carry");
            _carryA.Renderable = Renderable.Box(Vector3.One);
            _carryB = HandSocket.Add("carry");
            _carryB.Renderable = Renderable.Box(Vector3.One);
            _carryA.Visible = _carryB.Visible = false;
        }
    }

    /// <summary>
    /// Poses the bones from <paramref name="rig"/>'s last solve. <paramref name="space"/> is the world matrix of
    /// <see cref="Root"/>'s parent (the frame the rig's results are in is world; identity for first person's camera space).
    /// </summary>
    public void Pose(ArmRig rig, in Matrix4x4 space, in ArmColors c)
    {
        if (rig.Upper != _upper || rig.Fore != _fore || rig.Thick != _thick || rig.HandScale != _handScale)
            Shape(rig);

        // shoulder: a point in the arm's space; then each bone relative to the one before
        var shoulderWorld = Matrix4x4.CreateTranslation(rig.Shoulder);
        Shoulder.SetLocalMatrix(shoulderWorld * RigidInverse(space));
        var upper = rig.UpperWorld;
        upper.Translation -= rig.Shoulder; // its frame starts at the shoulder
        Upper.SetLocalMatrix(upper);
        Fore.SetLocalMatrix(rig.ForeWorld * RigidInverse(rig.UpperWorld));
        Hand.SetLocalMatrix(rig.HandWorld * RigidInverse(rig.ForeWorld));

        for (var s = 0; s < _shapes.Length; s++)
            _shapes[s].Visible = s == (int)rig.Shape;
        if (c.Sleeve != _sleeve.Renderable.Color || c.Glove != _shapes[0].Child(0).Renderable.Color || c.Fingers != _shapes[0].Child(1).Renderable.Color || c.Cuff != _cuff.Renderable.Color)
            Paint(c);
    }

    /// <summary>Shows the carried magazine / shells for this frame (as <see cref="Arms.DrawCarry"/> draws them), or hides it.</summary>
    public void Carry(bool carrying, HandShape shape, in ArmAnchorSet a)
    {
        if (_carryA == null || _carryB == null)
            return;
        _carryA.Visible = carrying;
        _carryB.Visible = carrying && a.Style == ReloadStyle.BreakOpen;
        if (!carrying)
            return;
        var c = a.Carry;
        var off = shape switch
        {
            HandShape.Under => new Vector3(0, c.Y / 2, 0),
            HandShape.Flat => new Vector3(0, -c.Y / 2, -0.01f),
            _ => a.Style == ReloadStyle.LmgBox ? new Vector3(c.X * 0.5f - 0.02f, 0, 0) : a.Style == ReloadStyle.MuzzleLoad ? new Vector3(0, 0.12f, 0) : Vector3.Zero,
        };
        _carryA.Renderable.Color = _carryB.Renderable.Color = a.CarryColor;
        if (a.Style == ReloadStyle.BreakOpen)
        {
            _carryA.SetLocalMatrix(Matrix4x4.CreateScale(c) * Matrix4x4.CreateTranslation(off + new Vector3(-0.012f, 0, 0)));
            _carryB.SetLocalMatrix(Matrix4x4.CreateScale(c) * Matrix4x4.CreateTranslation(off + new Vector3(0.012f, 0, 0)));
        }
        else
            _carryA.SetLocalMatrix(Matrix4x4.CreateScale(c) * Matrix4x4.CreateTranslation(off));
    }

    /// <summary>Box sizes and offsets from the rig's proportions (the same numbers <see cref="ArmRig.Draw"/> uses).</summary>
    private void Shape(ArmRig rig)
    {
        _upper = rig.Upper;
        _fore = rig.Fore;
        _thick = rig.Thick;
        _handScale = rig.HandScale;
        var t = _thick;
        _sleeve.SetLocalMatrix(Box(new Vector3(t, t, _upper + t * 0.3f), new Vector3(0, 0, -_upper * 0.5f)));
        _elbow.SetLocalMatrix(Box(new Vector3(t * 0.95f), Vector3.Zero));
        _forearm.SetLocalMatrix(Box(new Vector3(t * 0.84f, t * 0.8f, _fore * 0.8f), new Vector3(0, 0, -_fore * 0.4f)));
        _cuff.SetLocalMatrix(Box(new Vector3(t * 0.9f, t * 0.86f, _fore * 0.26f), new Vector3(0, 0, -_fore * 0.87f)));
        var k = _handScale;
        for (var s = 0; s < _shapes.Length; s++)
        {
            var parts = Arms.Shapes[s];
            for (var i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                _shapes[s].Child(i).SetLocalMatrix(Box(p.Size * k, new Vector3(p.Center.X * _side, p.Center.Y, p.Center.Z) * k));
            }
        }
    }

    private void Paint(in ArmColors c)
    {
        _sleeve.Renderable.Color = c.Sleeve;
        _elbow.Renderable.Color = c.Sleeve * 0.92f;
        _forearm.Renderable.Color = c.Sleeve;
        _cuff.Renderable.Color = c.Cuff;
        for (var s = 0; s < _shapes.Length; s++)
        {
            var parts = Arms.Shapes[s];
            for (var i = 0; i < parts.Length; i++)
                _shapes[s].Child(i).Renderable.Color = parts[i].Finger ? c.Fingers : c.Glove;
        }
    }

    private static Matrix4x4 Box(Vector3 size, Vector3 center) => Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(center);

    /// <summary>Inverse of a rotation + translation matrix (no scale): the transposed rotation and the translation undone.</summary>
    public static Matrix4x4 RigidInverse(in Matrix4x4 m)
    {
        var r = new Matrix4x4(m.M11, m.M21, m.M31, 0, m.M12, m.M22, m.M32, 0, m.M13, m.M23, m.M33, 0, 0, 0, 0, 1);
        var t = Vector3.TransformNormal(-m.Translation, r);
        r.M41 = t.X;
        r.M42 = t.Y;
        r.M43 = t.Z;
        return r;
    }
}
