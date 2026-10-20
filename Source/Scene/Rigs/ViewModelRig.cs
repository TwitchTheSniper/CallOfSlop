using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The first-person viewmodel as a scene graph: camera (camera space: -Z forward, +X right, +Y up) -> gun (posed by the
/// <see cref="ViewModelAnimator"/>) -> gun parts and attachments (<see cref="GunRig"/>), and camera -> right / left arm
/// (shoulder -> upper arm -> forearm -> hand, <see cref="ArmNodes"/>) with the reload carry item on the left hand.
/// The animator still does all the animation and IK; this only holds the result as nodes and draws it.
/// </summary>
public sealed class ViewModelRig
{
    public readonly SceneNode Camera;
    public readonly GunRig Gun;
    public readonly ArmNodes Right, Left;

    public ViewModelRig()
    {
        Camera = new SceneNode("camera");
        Gun = new GunRig();
        Camera.Add(Gun.Root);
        Right = new ArmNodes("r", +1, carry: false);
        Left = new ArmNodes("l", -1, carry: true);
        Camera.Add(Right.Root);
        Camera.Add(Left.Root);
    }

    /// <summary>
    /// Takes this frame's animation (call <see cref="ViewModelAnimator.Update"/> first) and poses every node:
    /// gun pose, optic cutaway, magazine offset, muzzle flash, both arms and the carried item.
    /// </summary>
    public void Pose(ViewModelAnimator anim, WeaponDef def, float cutaway, bool flash, float time, in ArmColors colors)
    {
        Gun.Ensure(def);
        Gun.Pose(anim.GunPose, cutaway, anim.MagOffset, anim.MagHidden, flash, time);
        Right.Pose(anim.Right, Matrix4x4.Identity, colors);
        Left.Pose(anim.Left, Matrix4x4.Identity, colors);
        Left.Carry(anim.Carrying, anim.Left.Shape, anim.Anchors);
    }
}
