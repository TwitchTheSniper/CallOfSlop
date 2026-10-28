using System.Reflection;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// The OpenGL 4.6 / 4.1 split (Source/Render/RenderCaps.cs, docs/render/OPENGL46.md) without a GPU: every shader
    /// source in the game starts with the one version line the prelude swaps, and turns into a valid 4.6 or 4.1 source;
    /// a source without it is refused; the command-line flags; the 4.1 tier keeps every 4.6-only feature off.
    /// </summary>
    private static void CheckGl46()
    {
        // ---- Every shader source ---------------------------------------------------------------------------------
        var sources = new List<(string Name, string Src)>();
        foreach (var t in typeof(RenderCaps).Assembly.GetTypes())
        {
            if (t.ContainsGenericParameters)
                continue;
            foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (f.FieldType != typeof(string) || !(f.IsLiteral || f.IsInitOnly))
                    continue;
                var v = f.IsLiteral ? f.GetRawConstantValue() as string : t.Namespace == "CallOfSlop" && t.FullName!.Contains("Renderer") || t.Name is "SightLens" or "Renderer" ? f.GetValue(null) as string : null;
                if (v != null && v.Contains("void main()") && v.Contains("#version")) // (a body pasted after another source, like SkyRenderer.FragmentBody, has none)
                    sources.Add(($"{t.Name}.{f.Name}", v));
            }
        }
        if (sources.Count < 20)
            Fail($"found only {sources.Count} shader sources by reflection (expected every renderer's, 20+)");
        foreach (var (name, src) in sources)
        {
            if (!src.StartsWith(RenderCaps.SourceVersion + "\n", StringComparison.Ordinal) && !src.StartsWith(RenderCaps.SourceVersion + "\r\n", StringComparison.Ordinal))
                Fail($"{name}: a shader source must start with \"{RenderCaps.SourceVersion}\" on its own line (RenderCaps.Glsl swaps it)");
            if (src.Split("#version").Length != 2)
                Fail($"{name}: exactly one #version line");
            foreach (var gl46 in new[] { false, true })
            {
                RenderCaps.SetTierForTest(gl46);
                var s = RenderCaps.Glsl(src);
                var first = s[..s.IndexOf('\n')];
                if (first != (gl46 ? "#version 460 core" : "#version 410 core") || !s.Contains(gl46 ? "#define GL46 1\n" : "#define GL46 0\n")
                    || s.Split("#version").Length != 2 || !s.EndsWith(src[(src.IndexOf('\n') + 1)..], StringComparison.Ordinal))
                    Fail($"{name}: the {(gl46 ? "4.6" : "4.1")} prelude came out wrong: \"{first}\"");
            }
            // 4.6-only GLSL must sit behind #if GL46 (the 4.1 path compiles the same source); a compute shader is 4.6-only whole (GlCompute).
            var compute = src.Contains("local_size_x");
            foreach (var only46 in new[] { "gl_BaseInstance", "gl_DrawID", "buffer ", "layout(std430", "readonly buffer" })
                if (!compute && src.Contains(only46) && !src.Contains("#if GL46"))
                    Fail($"{name}: uses {only46.Trim()} outside an #if GL46 block");
        }
        RenderCaps.SetTierForTest(false);
        var refused = false;
        try { RenderCaps.Glsl("#version 330 core\nvoid main() {}"); }
        catch (InvalidOperationException) { refused = true; }
        if (!refused)
            Fail("a shader source with another #version line should be refused");

        // ---- Flags --------------------------------------------------------------------------------------------------
        var wasForced = RenderCaps.ForceGl41;
        RenderCaps.ForceGl41 = false;
        if (RenderCaps.Active("persistent") || !RenderCaps.Active("mdi") || RenderCaps.Active("bar"))
            Fail("experiment defaults: persistent off (measured as noise), mdi on, unknown names off");
        RenderCaps.ReadArgs(new[] { "--gl-no=mdi , foo", "--gl-on=persistent", "--selftest" });
        if (RenderCaps.ForceGl41 || RenderCaps.Active("mdi") || RenderCaps.Active("foo") || !RenderCaps.Active("persistent") || RenderCaps.Active("bar"))
            Fail("--gl-no= should turn the named experiments off and --gl-on= on, and nothing else");
        RenderCaps.Force("mdi", null);
        RenderCaps.Force("foo", null);
        RenderCaps.Force("persistent", null);
        if (RenderCaps.Active("persistent") || !RenderCaps.Active("mdi"))
            Fail("Force(name, null) should give an experiment its default back");
        if (RenderCaps.Want46(out var why) != !OperatingSystem.IsMacOS())
            Fail($"without --gl41 the game should ask for 4.6 (except on macOS): {why}");
        RenderCaps.ReadArgs(new[] { "--GL41" });
        if (!RenderCaps.ForceGl41 || RenderCaps.Want46(out why) || why != "--gl41")
            Fail("--gl41 should force the 4.1 path");
        RenderCaps.ForceGl41 = wasForced;
        if (RenderCaps.Gl46 || RenderCaps.PersistentBuffers || RenderCaps.DebugOutput || RenderCaps.MultiDrawIndirect || RenderCaps.Compute || RenderCaps.Dsa)
            Fail("the 4.1 tier must keep every 4.6-only feature off");
        Line($"  gl46: {sources.Count} shader sources, each turns into a 4.6 and a 4.1 source; flags --gl41 / --gl-on= / --gl-no= parsed; 4.1 tier has no 4.6 features");
    }
}
