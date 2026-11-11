namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The joint-sides pass over the hand-written joint parts in Constructs/Parts (Hinge, Steering Pivot, Lid Hinge...): each file's "boxes"
/// rebuilt by <see cref="JointSides"/>, the rest of its text kept. Was tools/constructs/make_joint_sides.py, which also ran over the
/// actuators: <see cref="ActuatorParts"/> now applies the pass to its own files, so this one skips Constructs/Parts/Actuators.
/// </summary>
public sealed class MakeJointSides : IPartGenerator
{
    public string Name => "make_joint_sides";

    public void Make(PartOutput o)
    {
        var root = Path.Combine(o.Root, "Constructs", "Parts");
        foreach (var f in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var rel = Path.GetRelativePath(o.Root, f).Replace('\\', '/');
            if (rel.StartsWith("Constructs/Parts/Actuators/", StringComparison.Ordinal))
                continue;
            var txt = o.Read(rel);
            object? v;
            try
            {
                v = PyJson.Loads(txt);
            }
            catch (FormatException)
            {
                continue; // (not a file Python's json reads either)
            }
            if (v is not PyDict d || !JointSides.Applies(d))
                continue;
            o.File(rel, JointSides.Apply(txt));
        }
    }
}
