namespace CallOfSlop;

public static partial class SelfTest
{
    private static void CheckPackAssets()
    {
        Fail("PINS " + string.Join(";", Maps.All.Select(m => $"{m.SourceFile?.Replace('\\', '/')}={m.ContentHash:x8}")));
    }
}
