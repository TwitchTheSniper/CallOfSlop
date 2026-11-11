using System.Numerics;

namespace CallOfSlop;

// THROWAWAY (feat/recipe-picker): --smoketest --recipe-shots. Removed before the branch is finished.
public static unsafe partial class Program
{
    private static bool _recipeShots, _rsFire;
    private static Construct? _rsShow, _rsAsm;
    private static int _rsConsole = -1, _rsSmelter = -1, _rsFrames;
    private static long _rsBytes;
    internal static long _rsPickerBytes;
    private static double _rsAt;

    private static void RsAim(Game g, Player me, bool settled = false)
    {
        var c = _rsShow!;
        var d = c.DefOf(_rsConsole);
        var panel = d.Panels[0];
        var region = Array.FindIndex(panel.Touch, b => b.Action == "line.recipe");
        var r = panel.Touch[region].At;
        PanelTouch.Frame(c, _rsConsole, panel, out var centre, out var normal, out var up);
        var right = Vector3.Cross(up, normal);
        var target = centre + right * (((r.X + r.Z) * 0.5f - 0.5f) * panel.Size.X) + up * (((r.Y + r.W) * 0.5f - 0.5f) * panel.Size.Y);
        var eye = target + normal * 1.1f;
        if (!settled)
            me.Teleport(eye - new Vector3(0, World.EyeHeight, 0));
        var dir = Vector3.Normalize(target - (settled ? me.RenderEye : eye));
        me.Yaw = MathF.Atan2(dir.X, -dir.Z);
        me.Pitch = MathF.Asin(Math.Clamp(dir.Y, -1, 1));
        g.LookYaw = me.Yaw;
        g.LookPitch = me.Pitch;
    }

    private static void RecipeShotsPlay(Game g, double now, double t)
    {
        if (g.Local is not { } me)
            return;
        switch (_smokeScript)
        {
            case 0 when t > 1.0:
            {
                var bp = ConstructBuild.Blueprints.First(b => b.Name == "Production Showcase");
                var at = me.Position + new Vector3(0, 0, -40);
                at.Y = World.Map.GroundHeight(at.X, at.Z) + 0.3f;
                _rsShow = ConstructBuild.Build(g.Constructs, bp, at, Quaternion.Identity, out var why);
                Console.WriteLine($"[rshots] showcase {(_rsShow != null ? $"built, {_rsShow.PartCount} parts" : "FAILED: " + why)}");
                if (_rsShow is not { } c)
                {
                    _quit = true;
                    return;
                }
                Span<int> list = stackalloc int[16];
                for (var i = 0; i < c.PartCount && _rsConsole < 0; i++)
                    if (ConstructConfig.Has(c.DefOf(i), CfgKey.LinePick))
                    {
                        var n = ProductionLines.Machines(c, i, list);
                        for (var k = 0; k < n; k++)
                            if (c.DefOf(list[k]).Name.StartsWith("Smelter"))
                            {
                                _rsConsole = i;
                                _rsSmelter = list[k];
                                ConstructConfig.Put(ref c.Parts[i], c.DefOf(i), CfgKey.LinePick, k);
                            }
                    }
                Console.WriteLine($"[rshots] refining console part {_rsConsole}, smelter part {_rsSmelter}");
                g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Hands);
                _smokeScript = 1;
                break;
            }
            case 1 when t > 3.0:
                RsAim(g, me);
                _smokeScript = 2;
                break;
            case 2 when t > 4.0:
                RsAim(g, me);
                _rsBytes = GC.GetAllocatedBytesForCurrentThread();
                _rsFrames = _smokeFrames;
                _rsAt = t;
                _smokeScript = 3;
                break;
            case 3 when t > 14.0:
            {
                var b = GC.GetAllocatedBytesForCurrentThread() - _rsBytes;
                var f = _smokeFrames - _rsFrames;
                Console.WriteLine($"[rshots] baseline (picker closed) {t - _rsAt:0} s, {f} frames: {b / Math.Max(1, f)} bytes a frame on the main thread");
                RsAim(g, me);
                _smokeScript = 13;
                break;
            }
            case 13 when t > 15.5:
                RsAim(g, me, true);
                _smokeScript = 14;
                break;
            case 14 when t > 16.5:
                SmokeShot("rs_aim");
                _rsFire = true;
                _smokeScript = 4;
                break;
            case 4:
            {
                _rsFire = false;
                var c = _rsShow!;
                var eye = me.RenderEye;
                var dir = g.LookDir(g.LookYaw, g.LookPitch);
                var n = PanelTouch.Find(c, _rsConsole, eye, dir, out var spot);
                ref var h = ref g.SandboxHands.Hand(me.Id);
                Console.WriteLine($"[rshots] debug: panel {n}, spot {spot}, region {(n >= 0 ? PanelTouch.Region(c.DefOf(_rsConsole).Panels[n], spot) : -9)}, tool {h.Tool}, held {h.Held}, scrPart {_scrPart}, dist {Vector3.Distance(eye, c.WorldCom)}");
            }
                Console.WriteLine($"[rshots] after the press: picker open {Ui.RecipePickerOpen}, look '{g.SandboxLookText}', notice '{g.SandboxNotice}'");
                _rsAt = t;
                _smokeScript = 5;
                break;
            case 5 when t - _rsAt > 3.0:
                Console.WriteLine($"[rshots] smelter picker: {Ui.RecipePickerTiles} tiles, {Ui.RecipePickerPictures} with pictures, {SandboxTiles.Rendered} atlas tiles drawn");
                _shotFull = true;
                SmokeShot("rs_smelter");
                _rsPickerBytes = 0;
                _rsBytes = GC.GetAllocatedBytesForCurrentThread();
                _rsFrames = _smokeFrames;
                _rsAt = t;
                _smokeScript = 6;
                break;
            case 6 when t - _rsAt > 60.0:
            {
                var b = GC.GetAllocatedBytesForCurrentThread() - _rsBytes;
                var f = _smokeFrames - _rsFrames;
                Console.WriteLine($"[rshots] picker open {t - _rsAt:0} s, {f} frames: {b / Math.Max(1, f)} bytes a frame on the main thread; still open {Ui.RecipePickerOpen}; the picker's own draw {_rsPickerBytes / Math.Max(1, f)} bytes a frame");
                // The picker's action: the third tile (the second recipe), as a click sends it.
                var c = _rsShow!;
                var before = ConstructConfig.Get(c.Parts[_rsSmelter], c.DefOf(_rsSmelter), CfgKey.Recipe, 0);
                g.SandboxLineRecipe(c.Id, _rsConsole, _rsSmelter, 2, false);
                g.SandboxLineRecipe(c.Id, _rsConsole, _rsSmelter, 999, false);
                Console.WriteLine($"[rshots] pick: recipe {before} -> {ConstructConfig.Get(c.Parts[_rsSmelter], c.DefOf(_rsSmelter), CfgKey.Recipe, 0)}; a bad index says '{g.SandboxNotice}'");
                Ui.CloseRecipePicker();
                // An Assembler beside the player, its configure panel and the picker over it.
                var at = me.Position + World.FlatForward(me.Yaw) * -1.6f + new Vector3(0, 0.4f, 0);
                _rsAsm = ConstructBuild.PlaceNew(g.Constructs, ConstructParts.Need("Assembler"), at, 0);
                g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Configure);
                _rsAt = t;
                _smokeScript = 7;
                break;
            }
            case 7 when t - _rsAt > 1.0:
            {
                var a = _rsAsm!;
                a.Pinned = true;
                var d = a.DefOf(0);
                Ui.OpenSandboxConfig(a, 0);
                SetMouseCaptured(false);
                Ui.OpenRecipePickerConfig(a.Id, 0, d, d.CfgSlot[(int)CfgKey.Recipe]);
                Console.WriteLine($"[rshots] assembler {d.Name} tier {d.Tier}: config open {Ui.SandboxConfigOpen}, picker open {Ui.RecipePickerOpen}");
                _rsAt = t;
                _smokeScript = 8;
                break;
            }
            case 8 when t - _rsAt > 4.0:
                Console.WriteLine($"[rshots] assembler picker: {Ui.RecipePickerTiles} tiles, {Ui.RecipePickerPictures} with pictures; config open {Ui.SandboxConfigOpen}, picker {Ui.RecipePickerOpen}");
                _shotFull = true;
                SmokeShot("rs_assembler");
                _rsAt = t;
                _smokeScript = 9;
                break;
            case 9 when t - _rsAt > 1.0:
                Ui.CloseRecipePicker();
                Ui.CloseSandboxConfig();
                g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Material);
                _rsAt = t;
                _smokeScript = 10;
                break;
            case 10 when t - _rsAt > 0.5:
                _materialWheel.WantList = true;
                _rsAt = t;
                _smokeScript = 11;
                break;
            case 11 when t - _rsAt > 2.0:
                _shotFull = true;
                SmokeShot("rs_materials");
                _rsAt = t;
                _smokeScript = 12;
                break;
            case 12 when t - _rsAt > 0.5:
                Ui.CloseSandboxMaterialPicker();
                _quit = true;
                break;
        }
    }
}
