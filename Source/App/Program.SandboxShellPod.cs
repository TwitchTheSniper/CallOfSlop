using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Sandbox: shell pods on the client (ConstructShellPod.cs; docs/sandbox/design/shell-pod.md). In a robot shell, standing in an empty
/// pod's frame that takes it, the look line names the pod and the use key presses its button (CActKind.Use on the pod: the server
/// folds the shell away, GameShellPod.cs), wherever the crosshair is. A pod under the crosshair otherwise shows what it holds. Nothing
/// holds the mouse.
/// </summary>
public static partial class Program
{
    private static string _podLook = "";
    private static double _podLookAt;
    private static long _podLookKey;

    /// <summary>The pod whose frame this player (in a shell) stands in, or none; sets the look line for it, or for a pod under the crosshair.</summary>
    private static (Construct? Body, int Part) ShellPodFrame(Game g, Player me, bool aimed, in ConstructHit hit)
    {
        var world = g.Constructs;
        if (me.InShell)
        {
            var mid = me.Position + GravityFrames.UpAt(me.Position) * (World.PlayerHeight * ShellFrames.ScaleOf(me.ShellFrame) * 0.5f);
            foreach (var c in world.Bodies)
            {
                if (c.PodCount == 0 || Vector3.DistanceSquared(c.WorldCom, me.Position) > (c.Radius + 12) * (c.Radius + 12))
                    continue;
                for (var q = 0; q < c.PodCount; q++)
                {
                    int i = c.Pods[q];
                    if (i >= c.PartCount || c.DefOf(i).ShellPod is not { } pd || c.Parts[i].Broken || ConstructShellPod.Holds(c.Parts[i])
                        || !ConstructShellPod.Fits(pd, me.ShellFrame) || ConstructShellPod.JobAt(world, c.Id, i) >= 0 || !ConstructShellPod.InFrame(c, i, me.ShellFrame, mid))
                        continue;
                    g.SandboxLookText = PodLook(g, c, i, true);
                    return (c, i);
                }
            }
        }
        if (aimed && hit.RoutePlus1 == 0 && hit.Part >= 0 && hit.Part < hit.Body.PartCount && hit.Body.DefOf(hit.Part).ShellPod != null && hit.Distance <= ConstructBuild.UseReach + 2)
            g.SandboxLookText = PodLook(g, hit.Body, hit.Part, false);
        return (null, -1);
    }

    /// <summary>A pod's look line, made again when what it shows changes or at most twice a second.</summary>
    private static string PodLook(Game g, Construct c, int part, bool inFrame)
    {
        var now = ConstructConveyor.Clock;
        var key = ((long)c.Id << 20) ^ part ^ ((long)g.Constructs.PodJobCount << 40) ^ (inFrame ? 1L << 50 : 0) ^ ((long)ConstructShellPod.FrameIn(c.Parts[part]) + 1 << 52);
        if (key != _podLookKey || now - _podLookAt > 0.5)
        {
            (_podLookKey, _podLookAt) = (key, now);
            _podLook = ConstructShellPod.Look(g.Constructs, c, part, inFrame);
        }
        return _podLook;
    }
}
