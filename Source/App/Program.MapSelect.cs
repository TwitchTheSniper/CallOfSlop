using Brutal.OpenGlApi;

namespace CallOfSlop;

/// <summary>The map select screen's smoke-test step (lobby, after the loadouts screen).</summary>
public static unsafe partial class Program
{
    // ---- Smoke step: open the map select screen on the High Ground pack (terrain and foliage: the dearest pictures),
    // wait for its thumbnails (one is made per frame), screenshot it (smoke_mapselect.bmp) and log what they cost.

    private const string MapSelectSmokePack = "High Ground";

    private static int _mapSelectSmokeStage;
    private static int _mapSelectSmokeRenderedBefore, _mapSelectSmokeFrames;
    private static double _mapSelectSmokeMsBefore;

    /// <summary>One tick of the step (<paramref name="t"/>: seconds since it began). Returns true when it's done.</summary>
    private static bool SmokeMapSelectTick(double now, double t)
    {
        switch (_mapSelectSmokeStage)
        {
            case 0:
                _mapSelectSmokeRenderedBefore = MapThumbs.Rendered;
                _mapSelectSmokeMsBefore = MapThumbs.TotalMs;
                _mapSelectSmokeFrames = _smokeFrames;
                Ui.SmokeOpenMapSelect(_settings, MapSelectSmokePack);
                Console.WriteLine($"[smoke] map select: opened on '{MapSelectSmokePack}' (Random pre-selected), {MapSelect.Current.Count} pack tiles incl. {MapPack.TrueRandomName}");
                _mapSelectSmokeStage = 1;
                return false;
            case 1 when (t > 1.0 && MapThumbs.Pending == 0) || t > 10:
                SmokeShot("mapselect");
                var made = MapThumbs.Rendered - _mapSelectSmokeRenderedBefore;
                var ms = MapThumbs.TotalMs - _mapSelectSmokeMsBefore;
                var frames = Math.Max(1, _smokeFrames - _mapSelectSmokeFrames);
                Console.WriteLine($"[mapselect] smoke: {made} thumbnail(s) rendered in {ms:0.0} ms total (avg {ms / Math.Max(1, made):0.0} ms, worst so far {MapThumbs.WorstMs:0.0} ms), "
                    + $"{MapThumbs.Failed} failed, {MapThumbs.Pending} still queued, {Ui.MapSelectVisibleMaps} pictures on screen, "
                    + $"{frames / Math.Max(t, 0.001):0} fps over {t:0.0} s while they were made, GL error 0x{(uint)OpenGl.Native.glGetError():X}");
                _mapSelectSmokeStage = 2;
                return false;
            case 2 when t > 1.2:
                Ui.SmokeCloseMapSelect();
                _mapSelectSmokeStage = 3;
                return true;
            default:
                return false;
        }
    }
}
